using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El puente entre la simulación y Godot.
/// </summary>
/// <remarks>
/// <para><b>Esta clase es la única que toca las dos cosas.</b> De un lado
/// <see cref="Partida"/>, que no sabe que Godot existe; del otro el render, que
/// no decide nada.</para>
///
/// <para>La prueba de que la separación está bien hecha: <b>bajarle los FPS al
/// juego no cambia en cuántos ticks tarda una oleada.</b> Si cambia, hay lógica
/// colgada del render y el determinismo ya se rompió.</para>
/// </remarks>
public partial class Game : Node2D
{
    /// <summary>
    /// Cuánto vale un tick en las unidades del acumulador.
    /// </summary>
    /// <remarks>
    /// <para>El acumulador ya no cuenta milisegundos: cuenta
    /// <b>microsegundos × ticks por segundo</b>. Así un tick vale exactamente un
    /// millón de esas unidades y no hay ningún redondeo en el medio.</para>
    ///
    /// <para>⚠ Acá vivía <c>MsPorTick = 1000 / 30</c>, que <b>truncaba a 33</b>
    /// los 33,33… ms reales de un tick. Eran dos bugs en uno: la simulación
    /// corría un 1 % rápido contra el reloj de pared, y —peor— con frames de
    /// 16 o 17 ms enteros el bucle sacaba 1, 2, 1, 1, 2 ticks por frame en vez
    /// de un ritmo parejo. Esa irregularidad es la mitad de lo "tosco" que se
    /// veía: los sprites avanzaban a saltos desiguales aunque los FPS
    /// estuvieran clavados.</para>
    /// </remarks>
    private const long UnidadesPorTick = 1_000_000;

    /// <summary>
    /// Techo de ticks por frame.
    /// </summary>
    /// <remarks>
    /// Sin esto, un frame largo —cargar un recurso, el sistema operativo
    /// robándose la CPU— deja un acumulador enorme y el juego intenta ponerse al
    /// día de golpe. Eso alarga <i>más</i> ese frame, que deja un acumulador
    /// todavía mayor: la espiral de la muerte. Con el techo el juego se atrasa
    /// respecto del reloj de pared, que es lo correcto — la corrida se mide en
    /// ticks, no en segundos.
    /// </remarks>
    private const int MaxTicksPorFrame = 8;

    public Run Run { get; private set; } = null!;
    private ActionLog _log = null!;
    private long _ultimoUs;
    private long _acumulador;

    /// <summary>
    /// Dónde estaba cada enemigo en la ruta <b>antes</b> del último tick, por
    /// <c>SpawnId</c> y en milésimas de celda.
    /// </summary>
    /// <remarks>
    /// <para>Es la mitad de atrás de la interpolación del render: el dibujo no
    /// muestra el estado del tick, muestra un punto <i>entre</i> el tick anterior
    /// y el actual. Sin esto los enemigos se mueven 30 veces por segundo y el
    /// monitor los repite dos o cinco frames seguidos — que es exactamente la
    /// sensación de tosco que hay que sacar.</para>
    ///
    /// <para>⚠ Esto <b>no vuelve nunca a la simulación</b>, igual que
    /// <c>Route.PosicionFina</c>. Se llena leyendo el estado justo antes de
    /// <c>Run.Advance</c> y solo lo consume <c>_Draw</c>.</para>
    ///
    /// <para>Se indexa por <c>SpawnId</c> porque no se reusa nunca: un enemigo
    /// muerto deja una entrada huérfana que se pisa en el clear del tick
    /// siguiente, y un enemigo recién aparecido no tiene entrada y se dibuja en
    /// su posición exacta, que es lo correcto.</para>
    /// </remarks>
    private readonly System.Collections.Generic.Dictionary<int, long> _distanciaPrevia = new();

    /// <summary>Multiplicador de velocidad. Acelerar no toca el ranking: los ticks son los mismos.</summary>
    private int _velocidad = 1;

    private bool _pausado;

    /// <summary>
    /// Si la corrida está en pausa. La leen y la escriben el HUD y las teclas.
    /// </summary>
    /// <remarks>
    /// ⚠ Pausar frena los ticks y el ranking desempata por ticks, así que en
    /// pausa se mira pero no se actúa (ver <c>PuedeActuar</c>). El botón del HUD
    /// pasa por acá, igual que el atajo de teclado: si hubiera dos caminos
    /// distintos, uno de los dos se olvidaría de esa regla.
    /// </remarks>
    public bool Pausado
    {
        get => _pausado;
        set
        {
            _pausado = value;
            QueueRedraw();
        }
    }

    /// <summary>El multiplicador de velocidad: 1, 2 o 4.</summary>
    public int Velocidad
    {
        get => _velocidad;
        set => _velocidad = value;
    }

    /// <summary>El menú de ESC, para poder abrirlo desde el botón del HUD.</summary>
    public EscMenuScreen? EscMenu { get; set; }

    /// <summary>Las opciones, para el botón del engranaje del HUD.</summary>
    public OptionsScreen? Options { get; set; }

    /// <summary>
    /// Fija con qué corrida se arranca. Se llama <b>antes</b> de agregar el nodo
    /// al árbol.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>La seed la manda el servidor</b>, en la respuesta de
    /// <c>POST /run/start</c>. Que la generara el cliente sería regalarle al
    /// tramposo la posibilidad de probar semillas hasta encontrar una fácil.
    ///
    /// <para><paramref name="runId"/> en 0 significa corrida local, sin servidor:
    /// sirve para probar sin levantar la API, y no se reporta a ningún lado.</para>
    /// </remarks>
    public void Configure(ulong seed, long runId)
    {
        Seed = seed;
        RunId = runId;
    }

    public override void _Ready()
    {
        // Sin Configurar previo, corrida local con una seed fija: así el proyecto
        // se puede abrir y probar sin tener la API arriba.
        if (Seed == 0)
            Seed = 20260825;

        // ⚠ EL MUNDO VIVE ADENTRO DEL MARCO. Todo lo que dibuja esta clase —y
        // todos sus hijos: el portal y el cristal— se sigue escribiendo en
        // coordenadas de mundo, en celdas de 96 px, y esta transformación lo
        // corre y lo achica para que entre en el hueco (INTERFAZ.md §3).
        //
        // Va acá, en el nodo, y no repartida por cada DrawRect: si la escala
        // estuviera en el dibujo habría que acordarse de aplicarla en veinte
        // lugares, y el que se olvide dibuja en otra parte de la pantalla.
        Position = UiLayout.WorldOrigin;
        Scale = new Vector2(UiLayout.WorldScale, UiLayout.WorldScale);

        // La corrida anterior horneó su piso; el de esta se hornea al primer frame.
        BakedFloor.Forget();

        // Con Retomar la partida ya viene armada y re-simulada; acá solo se crea
        // la nueva.
        if (Run is null)
        {
            // ⚠ El layout lo elige la SEED, con su propio generador. No se usa el
            // _rng de la partida porque ese ya arrancó a consumir números adentro
            // de Partida: pedirle uno acá correría toda la secuencia posterior.
            var mapaNuevo = Layouts.Choose(Seed, DataLoader.Current);
            _log = new ActionLog();
            Run = new Run(Seed, mapaNuevo, _log, DataLoader.Current);
            Run.Start();
        }

        var mapa = Run.Map;

        // El portal de entrada. Va de hijo y no dibujado a mano porque son
        // partículas: tienen que vivir entre frames, no repintarse de cero.
        var entrada = mapa.Route.Entry;
        var (px, py) = Canvas.CentroDe(entrada.X, entrada.Y);

        AddChild(new EntryPortal(Palette.Info)
        {
            Position = new Vector2(px, py),
        });

        // Y el otro extremo: la Torre. Va un poco más arriba del centro de su
        // celda porque flota, y la punta de abajo tiene que quedar apoyada.
        var (tx, ty) = Canvas.CentroDe(Run.Tower.Cell.X, Run.Tower.Cell.Y);

        AddChild(new TowerCrystal(this)
        {
            Position = new Vector2(tx, ty - 14),
        });

        _ultimoUs = FineClock.Us;

        GD.Print($"RunCurve iniciada · seed {Seed} · run {RunId} · " +
                 $"ruta de {mapa.Route.Length / Ints.ThousandthsPerCell} celdas");
    }

    /// <summary>
    /// Guarda la corrida en curso, para poder retomarla.
    /// </summary>
    /// <remarks>
    /// Sale gratis del determinismo: guardar es escribir la seed y el log, y
    /// cargar es re-simular desde el tick 0. Ver <see cref="SaveFile"/>.
    /// </remarks>
    public void StoreRun() => SaveFile.Store(Seed, RunId, _log, Run.Tick);

    /// <summary>Abierto el menú de ESC. Congela el reloj, como <c>_pausado</c>.</summary>
    public bool MenuOpen { get; set; }

    /// <summary>Si en el guardado ya quedó la preparación de esta oleada.</summary>
    private int _oleadaGuardada = -1;

    /// <summary>
    /// Autoguardado: una vez por oleada, al abrirse la preparación.
    /// </summary>
    /// <remarks>
    /// La preparación es el checkpoint natural —reloj congelado, recompensas
    /// cobradas— y guardar ahí cuesta nada: la seed y un log de bytes. Con esto,
    /// cerrar el juego de cualquier manera pierde como mucho la oleada en curso;
    /// el cierre ordenado de la ventana guarda además el tick exacto.
    /// </remarks>
    private void GuardarAlAbrirPreparacion()
    {
        if (!Run.WaitingForWaveStart || Run.State != RunState.InProgress)
            return;

        if (_oleadaGuardada == Run.Wave)
            return;

        _oleadaGuardada = Run.Wave;
        StoreRun();
    }

    /// <summary>
    /// Retoma una corrida guardada en lugar de empezar una nueva.
    /// </summary>
    /// <remarks>
    /// ⚠ Llamar ANTES de agregar el nodo al árbol: <c>_Ready</c> ve la partida ya
    /// puesta y no crea otra.
    /// </remarks>
    public void Resume(Run partida, ActionLog log, ulong seed, long runId)
    {
        Run = partida;
        _log = log;
        Seed = seed;
        RunId = runId;

        // La oleada retomada ya está guardada — es de donde venimos. Sin esto, el
        // primer frame la volvería a guardar, inofensivo pero al pedo.
        _oleadaGuardada = partida.Wave;
    }

    /// <summary>La clase que se va a poner con el próximo click.</summary>
    private DefenderClass _claseElegida = DefenderClass.Hunter;

    /// <summary>Bajo qué celda está el mouse. Fuera de la grilla, <c>null</c>.</summary>
    private Cell? _celdaMouse;

    /// <summary>
    /// Registra una acción del jugador y la encola para el tick <b>siguiente</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Siguiente y no actual, a propósito.</b> El tick actual puede estar a
    /// medio correr —el click llega en cualquier momento del frame— y meter una
    /// acción en la mitad de las nueve fases haría que el resultado dependa de
    /// cuándo cayó el evento dentro del frame. Con el tick siguiente, la acción
    /// entra siempre en la fase 1, que es donde el orden dice que entra.
    /// </remarks>
    private void Encolar(System.Func<int, PlayerAction> armar)
    {
        _log.Add(armar(Run.ActionTick));
    }

    /// <summary>Elige una de las 3 opciones. Lo llama la pantalla de elección.</summary>
    /// <remarks>
    /// ⚠ Se guarda el <b>índice</b>, no qué era la opción. Qué había en cada una
    /// lo regenera el servidor de la seed; si se guardara el contenido, un
    /// cliente modificado podría reclamar que le ofrecieron algo mejor.
    /// </remarks>
    public void Choose(int indice) => Encolar(t => PlayerAction.Choose(t, indice));

    /// <summary>
    /// Arranca la oleada que espera. Lo llama el botón y el fin de los 30 s.
    /// </summary>
    /// <remarks>
    /// Es la misma acción que adelantar: con la oleada anterior limpia significa
    /// «comenzar», y con bichos todavía vivos significa «metémelos encima». Un
    /// solo botón para el jugador y una sola acción en el log.
    /// </remarks>
    public void StartWave()
    {
        if (!Run.WaitingForWaveStart)
            return;

        Encolar(t => new PlayerAction(t, ActionType.CallNextWave, 0, 0, 0));
    }

    /// <summary>El id de la corrida del lado del servidor. 0 = corrida local.</summary>
    public long RunId { get; set; }

    /// <summary>El log, para poder reportarlo y guardarlo.</summary>
    public ActionLog Log => _log;

    /// <summary>La seed con la que corre. La manda el servidor.</summary>
    public ulong Seed { get; private set; }

    /// <summary>El overlay de mochila, para poder abrirlo con Tab.</summary>
    public BackpackScreen? Backpack { get; set; }

    /// <summary>
    /// El mercado —elección y tienda juntas—, para poder reabrirlo.
    /// </summary>
    /// <remarks>
    /// Se cierra con Esc una vez resuelta la elección, así que hace falta una
    /// puerta de vuelta: el botón MERCADO de la barra de preparación. Sin ella,
    /// cerrar la ventana perdería la tienda de esa oleada.
    /// </remarks>
    public MarketScreen? Market { get; set; }

    /// <summary>Equipa lo que está en ese lugar de la mochila.</summary>
    public void EquipFromBackpack(int indice, DefenderClass clase)
    {
        var cosa = Run.Backpack.Items[indice];

        // Perk y equipo son dos acciones distintas del log, aunque la UI las
        // muestre en la misma grilla: el perk se destruye al sacarlo y el equipo
        // vuelve. Confundirlas sería confundir las dos reglas.
        var tipo = cosa.Kind == ItemType.Perk ? ActionType.EquipPerk : ActionType.Equip;

        _log.Add(new PlayerAction(Run.ActionTick, tipo, indice, (int)clase, 0));
    }

    /// <summary>
    /// Saca un perk equipado. <b>Lo destruye.</b> La UI ya pidió confirmación.
    /// </summary>
    public void RemovePerk(DefenderClass clase, int slot)
        => _log.Add(new PlayerAction(Run.ActionTick, ActionType.RemovePerk, (int)clase, slot, 0));

    /// <summary>
    /// Mejora los perks marcados en uno del tier de arriba.
    /// </summary>
    /// <remarks>
    /// ⚠ Lo que va al log es la <b>máscara de bits</b> de la selección, no el
    /// perk que sale: el resultado lo sortea el RNG sembrado y el servidor lo
    /// regenera. Guardar el resultado dejaría que un cliente modificado reclamara
    /// haber fabricado el Oro que quisiera.
    /// </remarks>
    public void Upgrade(int mascara, PerkTier tier)
        => _log.Add(new PlayerAction(
            Run.ActionTick, ActionType.Upgrade, mascara, (int)tier, 0));

    /// <summary>
    /// Compra un nivel de una clase del roster con el oro de la corrida.
    /// </summary>
    /// <remarks>
    /// Es el sumidero de oro de la corrida. La pantalla ya chequeó el precio y el
    /// nivel máximo, pero el que manda es <c>Run</c>: la acción entra al log igual
    /// y ahí se valida, porque el log es lo que revalida el servidor.
    /// </remarks>
    public void BuyLevel(DefenderClass clase)
        => _log.Add(new PlayerAction(Run.ActionTick, ActionType.BuyLevel, (int)clase, 0, 0));

    /// <summary>
    /// Compra una oferta de la tienda rotativa.
    /// </summary>
    /// <remarks>
    /// ⚠ Lo que va al log es el <b>índice</b> de la oferta, no qué era: el stock lo
    /// regenera el servidor de la seed. Guardar el contenido dejaría que un cliente
    /// modificado reclamara haber comprado algo que no estaba a la venta.
    /// </remarks>
    public void Buy(int indice)
        => _log.Add(new PlayerAction(Run.ActionTick, ActionType.Buy, indice, 0, 0));

    /// <summary>
    /// Le cambia el modo de targeting a un defensor puesto.
    /// </summary>
    /// <remarks>
    /// ⚠ Va por <see cref="Defender.PlacementId"/> y no por celda: es del
    /// <b>defensor</b>, no de la clase ni del lugar. Dos paladines de la misma
    /// ficha quieren targeting distinto según dónde estén parados, y esa decisión
    /// de colocación es la mitad del valor de la mecánica.
    /// </remarks>
    public void SetTargetingMode(int idColocacion, TargetingMode modo)
        => _log.Add(new PlayerAction(
            Run.ActionTick, ActionType.SetTargeting, idColocacion, (int)modo, 0));

    /// <summary>
    /// Elige cuál de los dos comportamientos de una clase está activo.
    /// </summary>
    /// <remarks>
    /// ⚠ La ranura sigue siendo <b>una</b>: esto no permite tener el Oro y la
    /// habilidad del arma a la vez, permite decidir cuál habla. Los <b>stats</b>
    /// del arma se conservan en los dos casos, y ese es todo el punto — antes el
    /// arma ganaba siempre y encontrar una excelente obligaba a apagar un Único que
    /// costó dos jefes y un plano.
    /// </remarks>
    public void SetBehavior(DefenderClass clase, bool preferirElPerk)
        => _log.Add(new PlayerAction(
            Run.ActionTick, ActionType.SetBehavior, (int)clase, preferirElPerk ? 1 : 0, 0));

    /// <summary>La clase que se pone con el próximo click, para la UI.</summary>
    public DefenderClass ChosenClass => _claseElegida;

    /// <summary>Cicla entre las clases del roster.</summary>
    private void CambiarClase(int delta)
    {
        var disponibles = Run.Roster.Available();
        if (disponibles.Count == 0)
            return;

        int i = disponibles.IndexOf(_claseElegida);
        i = i < 0 ? 0 : (i + delta + disponibles.Count) % disponibles.Count;

        _claseElegida = disponibles[i];
        QueueRedraw();
    }

    /// <summary>
    /// En qué parte del tick actual está el dibujo, en milésimas: 0 recién
    /// terminado el tick, 1000 justo antes del que viene.
    /// </summary>
    /// <remarks>
    /// <para>Es el <i>alfa</i> de la interpolación. Se topea en 1000 a propósito:
    /// cuando el bucle choca contra <see cref="MaxTicksPorFrame"/> el acumulador
    /// queda por encima de un tick, y dejarlo pasar sería <b>extrapolar</b> —
    /// adivinar dónde va a estar el enemigo—. Adivinar se ve peor que atrasarse:
    /// un bicho que muere o frena en la Torre se pasaría de largo y volvería.</para>
    /// </remarks>
    private long FraccionMilesimas
    {
        get
        {
            long f = _acumulador * 1000 / UnidadesPorTick;

            return f < 0 ? 0 : (f > 1000 ? 1000 : f);
        }
    }

    /// <summary>
    /// El instante que se está dibujando, en milisegundos de mundo.
    /// </summary>
    /// <remarks>
    /// <para>Es el reloj de las animaciones de sprite. <b>Sale de la simulación,
    /// no del reloj de pared</b>: si el juego se atrasa contra el reloj —o si
    /// está en pausa, o corriendo a x4— las patas de los bichos se mueven al
    /// ritmo del mundo y no al de la máquina.</para>
    ///
    /// <para>Se cuenta desde <c>Tick - 1</c> porque el render va justo un tick
    /// atrás: es el mismo instante que muestra la posición interpolada, y si la
    /// animación fuera por delante de la posición el sprite pisaría en un lugar
    /// y se dibujaría en otro.</para>
    ///
    /// <para>⚠ Acá estaba <c>Run.Tick * MsPorTick</c>, con <c>MsPorTick</c>
    /// truncado a 33: la animación corría un 1 % rápida y, sobre todo, avanzaba
    /// a escalones de 33 ms. La división por <see cref="Ints.TicksPerSecond"/> al
    /// final —y no antes— mantiene los 33,33… exactos.</para>
    /// </remarks>
    private long MsMundo
    {
        get
        {
            // En milésimas de tick, para dividir una sola vez.
            long milesimas = (((long)Run.Tick - 1) * 1000) + FraccionMilesimas;

            return milesimas < 0 ? 0 : milesimas / Ints.TicksPerSecond;
        }
    }

    /// <summary>
    /// Dónde dibujar a este enemigo sobre la ruta, en milésimas de celda.
    /// </summary>
    /// <remarks>
    /// <para>Se interpola la <b>distancia recorrida</b>, no el par (x, y). Es un
    /// solo escalar y, sobre todo, <c>Route.PosicionFina</c> se encarga después
    /// de la geometría: el sprite dobla las esquinas por el camino en vez de
    /// cortar en diagonal, que es lo que pasaría al interpolar los píxeles.</para>
    ///
    /// <para>Sin entrada previa —enemigo recién aparecido— o sin avance —frenado
    /// pegándole a la Torre— se devuelve la posición exacta y no se inventa
    /// nada.</para>
    /// </remarks>
    private long DistanciaVisible(Enemy e)
    {
        if (!_distanciaPrevia.TryGetValue(e.SpawnId, out long previa))
            return e.DistanceTravelled;

        long avance = e.DistanceTravelled - previa;

        if (avance <= 0)
            return e.DistanceTravelled;

        return previa + (avance * FraccionMilesimas / 1000);
    }

    /// <summary>
    /// Corre un tick, anotando antes dónde estaba cada enemigo.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Todo tick pasa por acá.</b> Un <c>Run.Advance()</c> suelto deja la
    /// foto vieja y el render interpola contra una posición de dos ticks atrás:
    /// el enemigo se ve arrastrarse y después pegar un salto.
    /// </remarks>
    private void AvanzarUnTick()
    {
        _distanciaPrevia.Clear();

        foreach (var e in Run.Enemies)
            _distanciaPrevia[e.SpawnId] = e.DistanceTravelled;

        Run.Advance();
    }

    public override void _Process(double delta)
    {
        // ⚠ `delta` de Godot es un double y no se usa: el acumulador va en
        // enteros de microsegundos. No es que un double acá rompa el
        // determinismo —el acumulador solo decide CUÁNTOS ticks correr, no qué
        // pasa adentro— pero mantener la frontera limpia evita la discusión.
        long ahora = FineClock.Us;
        long transcurrido = ahora - _ultimoUs;
        _ultimoUs = ahora;

        // ⚠ En pausa el acumulador NO se llena.
        //
        // Y ojo con CUÁL pausa es cada una. `Partida.EnPausa` es la de la
        // simulación —preparación o elección—: ahí `Avanzar` no toca el reloj y
        // llamarlo por frame solo drena las acciones pendientes. `_pausado` y el
        // menú de ESC son pausas de PANTALLA con la simulación viva: ahí llamar
        // `Avanzar` corría un tick entero por frame — la "pausa" aceleraba el
        // juego al ritmo del render en vez de frenarlo. Por eso los dos casos se
        // separan.
        if (_pausado || MenuOpen)
        {
            QueueRedraw();
            return;
        }

        if (Run.Paused)
        {
            // Con la simulación en pausa no hay tick que interpolar: se vuelve a
            // cero para que el dibujo muestre el estado exacto y no una fracción
            // congelada del último tick que corrió.
            _acumulador = 0;

            AvanzarUnTick();
            QueueRedraw();
            GuardarAlAbrirPreparacion();
            return;
        }

        _acumulador += transcurrido * Ints.TicksPerSecond * _velocidad;

        int corridos = 0;
        while (_acumulador >= UnidadesPorTick && corridos < MaxTicksPorFrame)
        {
            AvanzarUnTick();
            _acumulador -= UnidadesPorTick;
            corridos++;
        }

        // Se descarta lo que sobró del acumulador por encima del techo: si no,
        // la deuda se arrastra al frame siguiente y nunca se paga.
        if (corridos == MaxTicksPorFrame && _acumulador > UnidadesPorTick * MaxTicksPorFrame)
            _acumulador = 0;

        // ⚠ Se redibuja SIEMPRE, corran ticks o no.
        //
        // Antes se redibujaba solo cuando había corrido al menos un tick, o sea
        // 30 veces por segundo: en un monitor de 60 Hz cada posición se repetía
        // dos frames y en uno de 144 Hz, cinco. Eso es lo que se veía tosco, y no
        // se arregla con la interpolación si el frame intermedio no se dibuja.
        QueueRedraw();

        AjustarClaseElegida();
    }

    /// <summary>
    /// Mantiene la clase elegida dentro del roster.
    /// </summary>
    /// <remarks>
    /// ⚠ Este método existe por un bug concreto: <c>_claseElegida</c> arrancaba
    /// fija en Cazador, y si la primera elección del jugador era otra clase,
    /// <see cref="Partida.CanPlace"/> rechazaba todas las celdas en silencio —
    /// el click no hacía nada y no decía por qué. El roster arranca vacío y se
    /// llena eligiendo, así que la clase elegida solo puede fijarse DESPUÉS de
    /// la primera elección, no en el campo.
    /// </remarks>
    private void AjustarClaseElegida()
    {
        if (Run.Roster.Has(_claseElegida))
            return;

        var disponibles = Run.Roster.Available();

        if (disponibles.Count > 0)
        {
            _claseElegida = disponibles[0];
            QueueRedraw();
        }
    }

    /// <summary>Elige la clase a poner. La llama la barra de clases del HUD.</summary>
    public void SelectClass(DefenderClass clase)
    {
        // ⚠ Una clase sellada se ve en la columna pero no se elige: elegirla
        // dejaría al jugador con un cursor cargado que ninguna celda acepta, y
        // `CanPlace` rebota en silencio — el peor no de todos, el que no dice
        // que dijo que no.
        if (Run.Roster.Find(clase) is not { Sealed: false } || _claseElegida == clase)
            return;

        _claseElegida = clase;
        QueueRedraw();
    }

    /// <summary>
    /// Intenta poner la clase elegida en la celda bajo esta posición de pantalla.
    /// </summary>
    /// <remarks>
    /// Es el destino del arrastre desde la barra de clases, y el mismo camino que
    /// usa el click directo en el mapa: una sola puerta para las dos formas de
    /// poner, así no pueden discrepar.
    /// </remarks>
    public bool PlaceAt(Vector2 posicion)
    {
        if (!PuedeActuar)
            return false;

        var celda = ACelda(posicion);

        if (celda is null || !Run.CanPlace(_claseElegida, celda.Value))
            return false;

        Encolar(t => PlayerAction.Place(t, _claseElegida, celda.Value));
        return true;
    }

    /// <summary>
    /// Saltea la elección sin llevarse nada.
    /// </summary>
    /// <remarks>
    /// ⚠ Existe porque la elección dejó de ser gratis: con las tres opciones
    /// cobrando, un jugador sin oro quedaría trabado en un modal que no se cierra
    /// y con el reloj congelado.
    /// </remarks>
    public void SkipChoice()
        => Encolar(t => new PlayerAction(t, ActionType.SkipChoice, 0, 0, 0));

    /// <summary>
    /// Vende una ficha puesta. Devuelve el <c>defenderSellPercent</c> del costo.
    /// </summary>
    /// <remarks>
    /// ⚠ Lo dispara el botón del panel del defensor, en <b>dos clicks</b>. Antes
    /// lo hacía el botón derecho sobre el mapa, sin confirmación: el gesto más
    /// fácil de hacer sin querer borraba una unidad puesta y su oro.
    /// </remarks>
    public void Sell(int idColocacion)
        => Encolar(t => PlayerAction.Sell(t, idColocacion));

    /// <summary>
    /// Mueve una ficha ya puesta a otra celda. Ocupada, las dos se intercambian.
    /// </summary>
    /// <remarks>
    /// ⚠ Va por <see cref="Defender.PlacementId"/>, igual que vender y que el
    /// targeting: la celda de origen ya no sirve como identidad en el momento en
    /// que la ficha se está moviendo.
    /// </remarks>
    public void MoveDefender(int idColocacion, Cell destino)
        => Encolar(t => new PlayerAction(
            t, ActionType.MoveDefender, idColocacion, destino.X, destino.Y));

    // =====================================================================
    // Arrastrar una ficha por el tablero
    // =====================================================================

    /// <summary>La ficha que se está arrastrando, o -1.</summary>
    private int _arrastreId = -1;

    /// <summary>Dónde se apretó el botón, para distinguir un click de un arrastre.</summary>
    private Vector2 _arrastreDesde;

    /// <summary>
    /// Ya se movió lo suficiente como para que esto sea un arrastre y no un click.
    /// </summary>
    /// <remarks>
    /// ⚠ El umbral existe porque <b>un click y un arrastre empiezan igual</b>. Sin
    /// él, seleccionar una ficha con la mano temblando la movería una celda, y
    /// mover sería tan fácil de hacer sin querer como lo era vender con el botón
    /// derecho — el problema que esto vino a arreglar.
    /// </remarks>
    private bool _arrastrando;

    private const float UmbralArrastre = 8f;

    /// <summary>La ficha que se arrastra ahora, o <c>null</c>. La mira el dibujo.</summary>
    public Defender? Arrastrada
        => _arrastrando && _arrastreId >= 0 ? Run.DefenderById(_arrastreId) : null;

    public override void _UnhandledInput(InputEvent evento)
    {
        switch (evento)
        {
            case InputEventMouseMotion movimiento:
                _celdaMouse = ACelda(movimiento.Position);

                // El arrastre arranca recién al pasar el umbral: hasta ahí sigue
                // siendo un click que todavía no se soltó.
                //
                // ⚠ Y solo en preparación: con la oleada corriendo el gesto sigue
                // siendo un click que selecciona, porque mover en combate es otra
                // mecánica (Acciones.cs, MoveDefender). Un arrastre que se dibuja
                // y después no hace nada se lee como un bug.
                if (_arrastreId >= 0 && !_arrastrando && Run.WaitingForWaveStart &&
                    movimiento.Position.DistanceTo(_arrastreDesde) > UmbralArrastre)
                {
                    _arrastrando = true;
                }

                QueueRedraw();
                return;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } suelta
                when _arrastreId >= 0:
                SoltarArrastre(suelta.Position);
                return;

            case InputEventMouseButton boton when boton.Pressed:
                AtenderClick(boton);
                return;

            case InputEventKey tecla when tecla.Pressed && !tecla.Echo:
                AtenderTecla(tecla);
                return;
        }
    }

    /// <summary>
    /// Si el jugador puede tocar algo ahora.
    /// </summary>
    /// <remarks>
    /// En pausa se mira pero no se actúa: pausar frena los ticks, y el ranking
    /// desempata por ticks, así que actuar en pausa sería tiempo de pensar gratis
    /// en una tabla de velocidad.
    /// </remarks>
    private bool PuedeActuar => !_pausado && Run.State == RunState.InProgress;

    private void AtenderClick(InputEventMouseButton boton)
    {
        if (!PuedeActuar)
            return;

        var celda = ACelda(boton.Position);
        if (celda is null)
            return;

        if (boton.ButtonIndex == MouseButton.Left)
        {
            // ⚠ Click en una celda OCUPADA agarra la ficha; en una vacía, pone.
            //
            // No hay conflicto porque las dos cosas son excluyentes por
            // construcción: `PuedePoner` ya rechazaba la celda ocupada. Agarrar y
            // soltar en el mismo lugar es un click, y el click selecciona: abre el
            // panel de targeting, que es lo único que se decide por defensor.
            var ocupada = Run.DefenderAt(celda.Value);

            if (ocupada is not null)
            {
                _arrastreId = ocupada.PlacementId;
                _arrastreDesde = boton.Position;
                _arrastrando = false;

                return;
            }

            SelectedDefender = -1;
            PlaceAt(boton.Position);
        }
        else if (boton.ButtonIndex == MouseButton.Right)
        {
            // ⚠ EL BOTÓN DERECHO YA NO VENDE.
            //
            // Vendía la ficha de abajo del cursor, sin confirmar y sin poder
            // deshacer: un click derecho al costado —el gesto más fácil de hacer
            // sin querer que hay— borraba una unidad puesta y su oro. Vender vive
            // ahora en el panel del defensor, donde hay que apuntar a un botón que
            // dice cuánto devuelve.
            //
            // Acá cancela: suelta el arrastre y cierra el panel.
            _arrastreId = -1;
            _arrastrando = false;
            SelectedDefender = -1;
        }
    }

    /// <summary>
    /// Suelta la ficha que se estaba arrastrando.
    /// </summary>
    /// <remarks>
    /// <para>Sin haber pasado el umbral fue un <b>click</b>, y el click
    /// selecciona: volver a clickear la misma la deselecciona, así el panel se
    /// cierra desde donde se abrió.</para>
    ///
    /// <para>Con arrastre de verdad, manda la celda destino. La simulación
    /// revalida todo —que sea preparación, que la celda sea colocable, que la
    /// ficha exista— porque el log se re-simula por ese mismo camino.</para>
    /// </remarks>
    private void SoltarArrastre(Vector2 posicion)
    {
        int id = _arrastreId;
        bool arrastro = _arrastrando;

        _arrastreId = -1;
        _arrastrando = false;

        if (!arrastro)
        {
            SelectedDefender = SelectedDefender == id ? -1 : id;
            return;
        }

        var destino = ACelda(posicion);

        if (destino is null || !PuedeActuar)
            return;

        MoveDefender(id, destino.Value);
    }

    /// <summary>
    /// El defensor cuyo panel de targeting está abierto, o <c>-1</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ Va por <see cref="Defender.PlacementId"/> y no por celda ni por
    /// referencia: el defensor puede venderse mientras el panel está abierto, y un
    /// id que ya no existe simplemente no encuentra a nadie. Con una referencia
    /// guardada, el panel seguiría mostrando un defensor que ya no está en el mapa.
    /// </remarks>
    public int SelectedDefender { get; set; } = -1;

    /// <summary>
    /// Las teclas de la corrida, todas remapeables desde el menú de opciones.
    /// </summary>
    /// <remarks>
    /// ⚠ Se traduce <b>tecla → acción</b> con <see cref="Settings"/> en vez
    /// de comparar contra <c>Key.Space</c> y compañía: si las constantes
    /// quedaran acá, el remapeo del menú no haría nada y el bug sería mudo.
    /// </remarks>
    private void AtenderTecla(InputEventKey tecla)
    {
        if (Settings.ShortcutFor(tecla.Keycode) is not { } atajo)
            return;

        switch (atajo)
        {
            case Shortcut.Pausa:
                // ⚠ En pausa se puede MIRAR pero no ACTUAR.
                //
                // El problema: si los ticks no avanzan, pausar es tiempo de pensar
                // gratis, y el ranking desempata por ticks. Las tres salidas eran
                // pausa libre (amable, pero regala el desempate), pausa que igual
                // cuenta ticks (coherente, pero castiga levantarse de la silla) y
                // esta.
                //
                // Esta es la que conserva las dos cosas: el jugador puede irse sin
                // perder la corrida, y no puede usar la pausa para reorganizar su
                // defensa con calma mientras el cronómetro está frenado. Ver
                // Poder Actuar más abajo.
                _pausado = !_pausado;
                break;

            case Shortcut.Velocidad1: _velocidad = 1; break;
            case Shortcut.Velocidad2: _velocidad = 2; break;
            case Shortcut.Velocidad3: _velocidad = 4; break;

            // Cicla el roster en vez de mapear tecla a clase fija: las clases
            // tocan al azar, así que "W = mago" solo funciona si te tocó el mago.
            case Shortcut.ClaseAnterior: CambiarClase(-1); break;
            case Shortcut.ClaseSiguiente: CambiarClase(+1); break;

            // Tab abre la mochila. Es un overlay y NO frena la corrida: pausar es
            // tiempo de pensar gratis en una tabla de velocidad, y equipar tiene
            // que costar algo (INTERFAZ.md §5).
            case Shortcut.Backpack:
                Backpack?.Toggle();
                break;

            case Shortcut.CallNextWave:
                // Adelantar oleada: existe por el ranking. Sin esto, el desempate
                // por ticks no mide nada porque todos los que ganan tardan igual.
                if (PuedeActuar)
                    Encolar(PlayerAction.CallNextWave);

                break;
        }
    }

    /// <summary>
    /// Convierte una posición de <b>pantalla</b> en celda de la grilla.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Pasa por <see cref="UiLayout.ToWorld"/> primero.</b> El mundo ya
    /// no ocupa la pantalla entera: vive adentro del marco, corrido y a escala
    /// (<c>INTERFAZ.md</c> §3). Dividir la posición cruda por
    /// <see cref="Canvas.CellSide"/> —que es lo que hacía antes— devuelve una
    /// celda que no es la que el jugador está mirando.</para>
    ///
    /// <para>Un click fuera del marco no es de ninguna celda, y por eso se
    /// chequea <see cref="UiLayout.InWorld"/> antes: sin eso, un click sobre la
    /// columna del roster cae en coordenadas de mundo negativas, que
    /// <c>(int)</c> trunca <b>hacia cero</b> y convierte en la celda 0.</para>
    /// </remarks>
    private Cell? ACelda(Vector2 posicion)
    {
        if (!UiLayout.InWorld(posicion))
            return null;

        var mundo = UiLayout.ToWorld(posicion);

        var celda = new Cell(
            (int)(mundo.X / Canvas.CellSide),
            (int)(mundo.Y / Canvas.CellSide));

        return Run.Map.InGrid(celda) ? celda : null;
    }

    public override void _Draw()
    {
        const int celda = Canvas.CellSide;
        var ruta = Run.Route;
        var mapa = Run.Map;

        // El suelo del área, horneado. Va primero: todo lo demás se dibuja encima.
        //
        // Si falta el arte devuelve null y el mundo se dibuja como antes, sobre el
        // fondo pelado. Ver BakedFloor.
        var piso = BakedFloor.For(
            DataLoader.Current.Areas.OfWave(System.Math.Max(1, Run.NextWave)), Seed, Run.Map);

        if (piso is not null)
            DrawTexture(piso, Vector2.Zero);

        // Las zonas donde se puede poner. Tenues: son una ayuda, no el
        // protagonista.
        //
        // Sobre el piso horneado el tinte se apoya en el borde y no en el
        // relleno: una capa de color sobre pasto lo ensucia y tapa la textura,
        // mientras que el contorno marca la celda sin pintarla.
        bool hayPiso = piso is not null;

        var relleno = Palette.Success.Alpha(hayPiso ? 0.08f : 0.20f);
        var borde = Palette.Success.Alpha(hayPiso ? 0.45f : 0.28f);

        for (int y = 0; y < mapa.Height; y++)
        {
            for (int x = 0; x < mapa.Width; x++)
            {
                if (!mapa.IsPlaceable(new Cell(x, y)))
                    continue;

                DrawRect(new Rect2(x * celda, y * celda, celda, celda), relleno);
                DrawRect(new Rect2(x * celda, y * celda, celda, celda),
                    borde, filled: false, width: 1f);
            }
        }

        // La ruta, solo si no hay piso: el camino ya viene horneado adentro de la
        // textura, y esta línea marrón lo taparía con un color plano.
        if (!hayPiso)
        {
            for (int i = 1; i < ruta.VertexCount; i++)
            {
                var a = ruta.Vertex(i - 1);
                var b = ruta.Vertex(i);
                DrawLine(
                    new Vector2((a.X + 0.5f) * celda, (a.Y + 0.5f) * celda),
                    new Vector2((b.X + 0.5f) * celda, (b.Y + 0.5f) * celda),
                    new Color(0.35f, 0.30f, 0.22f),
                    celda * 0.8f);
            }
        }

        // La Torre no se dibuja acá: es el cristal, y va de hijo porque está
        // animado. Ver TowerCrystal.

        // Los defensores.
        var llevada = Arrastrada;

        foreach (var d in Run.Defenders)
        {
            // ⚠ La que se arrastra se dibuja en la celda de ABAJO DEL CURSOR, no
            // en la suya. Es lo que hace que arrastrar se sienta como llevar la
            // ficha: si se quedara en el origen, el jugador estaría moviendo un
            // recuadro y adivinando qué se mueve con él.
            var donde = ReferenceEquals(d, llevada) && _celdaMouse is { } bajoCursor
                ? bajoCursor
                : d.Cell;

            if (!DibujarDefensor(d, celda, donde))
            {
                DrawRect(
                    new Rect2(donde.X * celda + 8, donde.Y * celda + 8, celda - 16, celda - 16),
                    Palette.Success);
            }
        }

        // Los enemigos. La posición fina viene de la ruta y NO vuelve a la
        // simulación: es solo para que el sprite no salte de celda en celda.
        //
        // ⚠ Y no se dibuja el estado del tick, sino un punto INTERPOLADO entre el
        // tick anterior y el actual (ver `DistanciaVisible`). El costo es que el
        // render va un tick atrás —33 ms—; la ganancia es que el movimiento deja
        // de estar clavado a 30 pasos por segundo y se dibuja a los Hz del
        // monitor.
        foreach (var e in Run.Enemies)
        {
            long distancia = DistanciaVisible(e);

            var (fx, fy) = ruta.PosicionFina(distancia);

            var centro = new Vector2(
                (float)fx / Ints.ThousandthsPerCell * celda + (celda / 2f),
                (float)fy / Ints.ThousandthsPerCell * celda + (celda / 2f));

            // Los que llevan perk se ven distinto. Está escrito que "tiene que
            // verse"; esto es el placeholder de eso (INTERFAZ.md §5).
            bool conPerk = e.ExtraHealthPerk > 0 || e.ExtraSpeedPerk > 0;

            if (!DibujarBicho(e, distancia, centro, celda, conPerk))
            {
                // Sin sprite todavía: el arte entra por áreas y hoy está la 1. El
                // círculo de siempre, para que el área 5 se pueda jugar igual.
                DrawCircle(centro, celda * 0.3f,
                    conPerk ? Palette.Warning : Palette.Danger);
            }

            // Barra de vida. Los mismos tres colores que la de la Torre —verde,
            // ámbar, rojo—: el jugador ya sabe leerlos de la barra de arriba, y
            // dos escalas distintas de "cuánta vida queda" se confunden.
            int porcentaje = e.MaxHp == 0 ? 0 : e.Hp * 100 / e.MaxHp;
            var barra = new Rect2(centro.X - 20f, centro.Y - 30f, 40f, 5f);

            DrawRect(barra, Palette.BackgroundMain.Alpha(0.7f));
            DrawRect(new Rect2(barra.Position, new Vector2(40f * porcentaje / 100f, 5f)),
                Palette.Health(porcentaje));
        }

        DibujarCursor(celda);
    }

    /// <summary>
    /// Cuánto se achica el arte de disco al dibujarlo en el mundo.
    /// </summary>
    /// <remarks>
    /// El tile de AO en x4 mide 128 px y la celda mide 96 (<see cref="Canvas.CellSide"/>),
    /// así que la escala es 96/128 = 0,75 (INTERFAZ.md §2). ⚠ Acá decía 0,5, que
    /// era la escala de cuando la celda medía 64: con la celda en 96 todo el arte
    /// —bichos, cuerpos, cabezas y armas— se dibujaba a dos tercios de su tamaño y
    /// flotando sobre la ruta.
    /// </remarks>
    private const float EscalaSprite = 0.75f;

    /// <summary>
    /// Cuánto achicar el arte de un gráfico concreto, según en qué escala esté
    /// guardado.
    /// </summary>
    /// <remarks>
    /// <see cref="EscalaSprite"/> es la cuenta para el arte x4, que es como entró
    /// casi todo. Una hoja que pasó por <c>--reescalar-hoja</c> guarda el arte en
    /// x2 y tiene la mitad de píxeles de lado, así que hay que dibujarla al doble
    /// para que <b>se vea igual</b>: lo que cambia es la memoria, no la pantalla.
    /// </remarks>
    private static float EscalaDe(Graphic g)
        => EscalaSprite * 4f / (g.Scale > 0 ? g.Scale : 4);

    /// <summary>Los cuatro sufijos de dirección, en el orden de <see cref="Ruta.Direction"/>.</summary>
    private static readonly string[] Direcciones = ["norte", "este", "sur", "oeste"];

    /// <summary>
    /// Dibuja un defensor: cuerpo, cabeza y arma, en el tier que le da su nivel.
    /// </summary>
    /// <remarks>
    /// <para>Las tres piezas se apilan en el mismo punto y en este orden: cuerpo,
    /// cabeza —corrida por el offset que guarda <c>Personajes.ind</c>— y arma
    /// encima. Es el mismo armado que usa Austral, y es el motivo de que exista
    /// <c>cuerpo.*</c> y no un sprite entero por clase.</para>
    ///
    /// <para>El tier sale del <b>nivel de la clase</b>. Es lo único que hace
    /// visible subir de nivel: antes solo abría un slot de perk, que no se ve en
    /// el mapa.</para>
    /// </remarks>
    /// <returns><c>false</c> si falta el arte y hay que dibujar la primitiva.</returns>
    private bool DibujarDefensor(Defender d, int celda, Cell donde)
    {
        var enRoster = Run.Roster.Find(d.DefenderClass);

        if (enRoster is null)
            return false;

        var apariencia = AppearanceLoader.De(d.DefenderClass, enRoster.VisualTier);

        if (apariencia is null || apariencia.Body.Length == 0)
            return false;

        // Mira a lo que le está pegando. Mientras se arrastra no tiene objetivo
        // —el fantasma todavía no dispara—, así que sale de frente.
        string direccion = Direcciones[MirandoHacia(d, donde)];

        var centro = new Vector2(
            (donde.X + 0.5f) * celda,
            (donde.Y + 0.5f) * celda);

        // El cuerpo es la pieza que ancla: la cabeza y el arma van a su mismo
        // punto de apoyo. Si no está, no se dibuja nada, porque una cabeza sola
        // flotando es peor que el cuadrado verde.
        //
        // ⚠ El cuerpo va QUIETO, cuadro 0. Sus cuadros son el ciclo de caminar de
        // AO, y un defensor plantado que "camina" en el lugar marea — pedido del
        // 2026-08-26. La única pieza que anima es el arma.
        if (!DibujarPieza(apariencia.Body, direccion, centro, celda, Vector2.Zero, animar: false))
            return false;

        // ⚠ La cabeza va corrida, y el offset sale del cuerpo.
        //
        // Sin esto la cabeza queda a los pies del muñeco. El número está en
        // píxeles x4 y el mundo se dibuja a tres cuartos, así que se escala igual
        // que el sprite. Es lo que separa razas altas de bajas: -38 px contra -28.
        var offsetCabeza = new Vector2(
            apariencia.HeadOffset.X * EscalaSprite,
            apariencia.HeadOffset.Y * EscalaSprite);

        DibujarPieza(apariencia.Head, direccion, centro, celda, offsetCabeza, animar: false);
        DibujarPieza(apariencia.Weapon, direccion, centro, celda, Vector2.Zero, animar: true);

        return true;
    }

    /// <summary>Norte, este, sur y oeste como índices de <see cref="Direcciones"/>.</summary>
    /// <remarks>
    /// Es el <c>eHeading</c> de AO corrido a base cero: NORTH=1, EAST=2,
    /// SOUTH=3, WEST=4.
    /// </remarks>
    private const int Norte = 0;
    private const int Este = 1;
    private const int Sur = 2;
    private const int Oeste = 3;

    /// <summary>
    /// Hacia dónde mira un defensor: <b>al enemigo al que le está pegando</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Esto reemplazó a «mirar al camino».</b> La versión anterior
    /// orientaba el muñeco hacia la celda de ruta más cercana y no cambiaba
    /// nunca, porque una fila de defensores girando en bloque se veía mal. El
    /// problema es que una postura fija no dice <b>nada</b>: con dos bichos a
    /// tiro y un targeting configurable, ver a quién le apunta cada uno es la
    /// única forma de leer en el tablero qué está haciendo el modo elegido.</para>
    ///
    /// <para>Quién es el objetivo lo decide <c>ElegirObjetivo</c> adentro del
    /// tick y queda en <see cref="Defender.AimedAt"/>. <b>No se recalcula
    /// acá</b>: repetir los cuatro modos de targeting en el renderer es
    /// garantizar que las dos copias se separen en la primera corrección.</para>
    ///
    /// <para>Sin nadie a tiro —entre oleadas, o un defensor en una esquina
    /// muerta— mira al <b>sur</b>, que es la postura de frente y la misma que
    /// usan los retratos y las previsualizaciones.</para>
    ///
    /// <para>El diagonal no existe en este arte, así que gana el eje que más se
    /// recorre; empatados, el vertical, que es el que más se distingue en un
    /// top-down.</para>
    ///
    /// <para>Es render puro: la simulación no consulta esto y el tick no cambia.</para>
    /// </remarks>
    private static int MirandoHacia(Defender d, Cell donde)
    {
        if (d.AimedAt is not { } objetivo)
            return Sur;

        int dx = objetivo.X - donde.X;
        int dy = objetivo.Y - donde.Y;

        if (System.Math.Abs(dx) > System.Math.Abs(dy))
            return dx > 0 ? Este : Oeste;

        if (dy != 0)
            return dy > 0 ? Sur : Norte;

        // Justo encima: no hay a dónde girar, así que de frente.
        return Sur;
    }

    /// <summary>
    /// Dibuja una pieza de un muñeco, anclada abajo y centrada.
    /// </summary>
    /// <returns><c>false</c> si esa clave no tiene arte.</returns>
    private bool DibujarPieza(
        string clave, string direccion, Vector2 centro, int celda, Vector2 offset, bool animar)
    {
        if (clave.Length == 0)
            return false;

        string completa = $"{clave}.{direccion}";
        var g = GraphicsLoader.Data(completa);

        if (g is null)
            return false;

        // La animación sale del reloj del mundo, igual que la del bicho. Con
        // animar en falso va siempre el cuadro 0: el de estar parado.
        long ms = MsMundo;

        bool mueve = animar && Settings.AnimateWeapons;
        var textura = GraphicsLoader.Texture(completa, mueve ? (int)(ms / g.MsPerFrame) : 0);

        if (textura is null)
            return false;

        // x4 dibujado a tres cuartos, como todo el mundo (INTERFAZ.md §2).
        var tamano = textura.GetSize() * EscalaDe(g);

        var esquina = new Vector2(
            centro.X - (tamano.X / 2f) + offset.X,
            centro.Y + (celda / 2f) - tamano.Y + offset.Y);

        DrawTextureRect(textura, new Rect2(esquina, tamano), tile: false);

        return true;
    }

    /// <summary>
    /// Dibuja el sprite de un enemigo, si tiene.
    /// </summary>
    /// <remarks>
    /// <para>El cuadro sale del <b>tick de la simulación</b> y no del reloj de
    /// pantalla. Así el bicho se ve igual en dos corridas con la misma seed, y —lo
    /// que importa más— en una repetición acelerada la animación acompaña la
    /// velocidad en vez de quedarse fija mientras el mundo corre al triple.</para>
    ///
    /// <para>El sprite se ancla <b>abajo</b>, no al centro: los cuerpos de Austral
    /// son altos y angostos, y centrarlos deja al bicho flotando media celda por
    /// encima de la ruta.</para>
    /// </remarks>
    /// <returns><c>false</c> si ese bicho todavía no tiene arte.</returns>
    private bool DibujarBicho(Enemy e, long distancia, Vector2 centro, int celda, bool conPerk)
    {
        var dato = DataLoader.Current.Get(e.TypeId);

        if (dato is null || dato.Graphic.Length == 0)
            return false;

        // ⚠ La dirección se saca de la distancia INTERPOLADA, la misma con la que
        // se calculó `centro`. Con la del tick, en una esquina el sprite se da
        // vuelta hasta un tick antes o después de doblar, y se ve caminar de
        // costado media celda.
        int direccion = Run.Route.Direction(distancia);
        string clave = $"{dato.Graphic}.{Direcciones[direccion]}";

        var g = GraphicsLoader.Data(clave);

        if (g is null)
            return false;

        // ⚠ Se trabaja en milisegundos de mundo y recién ahí se divide.
        //
        // "Cada cuántos ticks cambia el cuadro" es una división entera que redondea
        // para abajo: con los 55 ms/cuadro que trae el arte de Austral y un tick de
        // 33 ms daba 1, o sea 33 ms, un 66 % más rápido de lo que dice el dato. La
        // hormiga caminaba como si patinara. `MsMundo` además avanza dentro del
        // tick, así que el cuadro cambia en el frame que le toca y no en el
        // borde del tick.
        long ms = MsMundo;
        int cuadro = (int)(ms / g.MsPerFrame);

        // Cada bicho arranca en un cuadro distinto. Sin esto los treinta enemigos
        // de una oleada mueven la pata en el mismo tick y parecen un desfile.
        // `IdSpawn` no se reusa nunca, así que sirve de desfasaje y no cambia
        // dentro de la vida del enemigo.
        cuadro += e.SpawnId;

        var textura = GraphicsLoader.Texture(clave, cuadro);

        if (textura is null)
            return false;

        // El arte está en x4 y el mundo se dibuja a tres cuartos de eso: la celda
        // mide 96 px porque el tile x4 de AO mide 128 (INTERFAZ.md §2). La escala
        // visual del bicho se multiplica encima.
        float escala = EscalaDe(g) * dato.VisualScale / 100f;

        var tamano = textura.GetSize() * escala;

        // `AnchorY` baja el sprite en los casos en que apoyar en el borde de la
        // celda no se ve apoyado. Va en píxeles de la hoja, así que se escala
        // igual que el sprite.
        var esquina = new Vector2(
            centro.X - (tamano.X / 2f),
            centro.Y + (celda / 2f) - tamano.Y + (g.AnchorY * escala));

        // Los que llevan perk van teñidos. Es el mismo placeholder que el círculo
        // amarillo, pero encima del sprite.
        var tinte = conPerk ? new Color(1f, 0.85f, 0.45f) : Colors.White;

        DrawTextureRect(textura, new Rect2(esquina, tamano), tile: false, modulate: tinte);

        return true;
    }

    /// <summary>
    /// La celda bajo el mouse, y qué pasaría si el jugador hace click ahí.
    /// </summary>
    /// <remarks>
    /// El alcance se dibuja <b>antes</b> de poner, no después: decidir dónde va
    /// un defensor sin ver hasta dónde llega es adivinar. Y el verde/rojo sale de
    /// <see cref="Partida.CanPlace"/>, la misma función que decide de verdad —
    /// si fueran dos, habría clicks que la UI promete y la simulación rechaza.
    /// </remarks>
    private void DibujarCursor(int celda)
    {
        if (_celdaMouse is not { } c)
            return;

        if (Arrastrada is { } llevada)
        {
            DibujarArrastre(llevada, c, celda);
            return;
        }

        bool puede = Run.CanPlace(_claseElegida, c);
        var ocupada = Run.DefenderAt(c);

        // El alcance de lo que se está por poner, o de lo que ya está ahí.
        //
        // ⚠ Sale de los stats EFECTIVOS —base + nivel + perks + equipo— y no de
        // los de la tabla. Con el alcance en milésimas de celda, un perk de
        // alcance mueve el círculo de verdad, y esta es la pantalla donde el
        // jugador decide dónde poner: un anillo que ignora los perks miente
        // justo en el momento en que la información importa.
        var claseAnillo = ocupada?.DefenderClass ?? _claseElegida;
        int alcanceMili = Run.Roster.StatsOf(claseAnillo, Run.Backpack).RangeMilli;

        if (puede || ocupada is not null)
        {
            var centro = new Vector2((c.X + 0.5f) * celda, (c.Y + 0.5f) * celda);
            float radio = alcanceMili * celda / (float)Ints.ThousandthsPerCell;

            DrawCircle(centro, radio, Palette.Info.Alpha(0.10f));
            DrawArc(centro, radio, 0, Mathf.Tau, 64, Palette.Info.Alpha(0.5f), 2f);
        }

        var color =
            ocupada is not null ? Palette.Warning.Alpha(0.85f) :
            puede ? Palette.Success.Alpha(0.85f) :
                    Palette.Danger.Alpha(0.65f);

        var marco = new Rect2(c.X * celda, c.Y * celda, celda, celda);

        DrawRect(marco, color, filled: false, width: 3f);

        // ⚠ El nombre de la clase que se va a poner, pegado a la celda.
        //
        // El click en una celda libre pone la clase elegida, y hasta acá lo único
        // que decía cuál era la tarjeta iluminada de la columna, al otro lado de
        // la pantalla. Mientras el ojo está en el tablero eligiendo dónde, la
        // pregunta «¿qué estoy por poner?» tiene que contestarse sin mover la
        // vista.
        if (ocupada is not null || !puede)
            return;

        // El mundo va a escala adentro del marco (INTERFAZ.md §3), así que el
        // tamaño se saca de la celda: un tamaño fijo en unidades de mundo se
        // vería más chico en pantalla de lo que dice el número.
        int tamano = (int)(celda * 0.22f);
        string nombre = Loc.Class(_claseElegida).ToUpperInvariant();

        float ancho = UiStyle.Width(nombre, tamano) + (celda * 0.18f);

        var chapa = new Rect2(
            marco.Position.X + ((celda - ancho) / 2f),
            marco.Position.Y - (celda * 0.34f),
            ancho, celda * 0.30f);

        DrawRect(chapa, Palette.BackgroundMain.Alpha(0.88f));
        DrawRect(chapa, color, filled: false, width: 2f);

        this.Label(chapa, nombre, tamano, Palette.TextPrimary, HorizontalAlignment.Center);
    }

    /// <summary>
    /// Lo que se ve mientras se arrastra una ficha: de dónde sale y qué va a pasar.
    /// </summary>
    /// <remarks>
    /// <para>Los tres resultados posibles tienen tres colores, y son los mismos
    /// que el resto del juego ya usa: <b>verde</b> se mueve a un lugar libre,
    /// <b>azul</b> —el que marca «esta clase»— las dos fichas se intercambian, y
    /// <b>rojo</b> ahí no se puede.</para>
    ///
    /// <para>El origen queda marcado en gris. Sin eso, al arrastrar tres celdas
    /// el jugador pierde de dónde salió la ficha y no sabe qué está deshaciendo
    /// si suelta el botón fuera del tablero.</para>
    /// </remarks>
    private void DibujarArrastre(Defender llevada, Cell destino, int celda)
    {
        var origen = new Rect2(llevada.Cell.X * celda, llevada.Cell.Y * celda, celda, celda);

        DrawRect(origen, Palette.BorderSoft.Alpha(0.30f));
        DrawRect(origen, Palette.BorderLight.Alpha(0.7f), filled: false, width: 2f);

        var otro = Run.DefenderAt(destino);

        var color =
            destino == llevada.Cell ? Palette.BorderLight.Alpha(0.8f)
            : otro is not null ? Palette.Info.Alpha(0.9f)
            : Run.Map.IsPlaceable(destino) ? Palette.Success.Alpha(0.9f)
            : Palette.Danger.Alpha(0.7f);

        // El anillo de alcance en el destino: mover una ficha es exactamente
        // decidir a qué llega, así que la pregunta se contesta antes de soltar.
        int alcanceMili = Run.Roster.StatsOf(llevada.DefenderClass, Run.Backpack).RangeMilli;

        var centro = new Vector2((destino.X + 0.5f) * celda, (destino.Y + 0.5f) * celda);
        float radio = alcanceMili * celda / (float)Ints.ThousandthsPerCell;

        DrawCircle(centro, radio, Palette.Info.Alpha(0.10f));
        DrawArc(centro, radio, 0, Mathf.Tau, 64, Palette.Info.Alpha(0.5f), 2f);

        // La otra ficha del intercambio se marca también: son DOS las que se
        // mueven, y marcar una sola esconde la mitad de lo que va a pasar.
        if (otro is not null)
        {
            DrawRect(new Rect2(destino.X * celda, destino.Y * celda, celda, celda),
                Palette.Info.Alpha(0.18f));
        }

        DrawRect(new Rect2(destino.X * celda, destino.Y * celda, celda, celda),
            color, filled: false, width: 3f);
    }
}
