namespace TowerOfArgentum.Simulacion;

/// <summary>Cómo terminó, o que sigue.</summary>
public enum RunState
{
    InProgress = 0,

    /// <summary>Cayó la Torre.</summary>
    Lost = 1,

    /// <summary>Murió el jefe de la oleada 100.</summary>
    Won = 2,
}

/// <summary>
/// Una corrida entera. <b>Acá vive el determinismo.</b>
/// </summary>
/// <remarks>
/// <para>Reglas que no se negocian, porque son lo que hace que el servidor pueda
/// revalidar una corrida re-simulándola:</para>
///
/// <list type="bullet">
/// <item>Ningún <c>using Godot</c> en este proyecto. La simulación no sabe que
/// existe un render, y por eso bajarle los FPS al juego no cambia en cuántos
/// ticks pasan las cosas.</item>
/// <item>Nada de <c>float</c>, <c>double</c>, <c>System.Random</c> ni funciones
/// trascendentes. Ver <see cref="Enteros"/>.</item>
/// <item>Un solo <see cref="Aleatorio"/>, sembrado con la seed del servidor.</item>
/// <item>Las listas se recorren <b>siempre en orden de spawn</b> y no se
/// reordenan nunca.</item>
/// </list>
/// </remarks>
public sealed class Run
{
    private readonly Rng _rng;
    private readonly Map _mapa;
    private readonly Route _ruta;
    private readonly ActionLog _log;

    /// <summary>Índice espacial de enemigos, para el alcance.</summary>
    private readonly SpatialGrid _grilla;

    /// <summary>Último tick en que curó Vampirismo, por clase. Ver la habilidad.</summary>
    private readonly Dictionary<DefenderClass, int> _ultimaCuraPorClase = [];

    /// <summary>Buffer reusado por ElegirObjetivo, para no alocar por disparo.</summary>
    private readonly List<int> _candidatos = [];

    /// <summary>
    /// Los enemigos vivos, <b>en orden de spawn</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Nunca se reordena y nunca se borra con swap-back.</b> El truco de
    /// "muevo el último al lugar del muerto y achico la lista" es O(1) en vez de
    /// O(n), y es exactamente el bug que rompe la revalidación: cambia el orden
    /// de iteración, y con él a quién le pega cada defensor en el tick
    /// siguiente. Se borra respetando el orden, con
    /// <see cref="List{T}.RemoveAll"/>, que es estable.
    /// </remarks>
    private readonly List<Enemy> _enemigos = [];

    /// <summary>Los defensores puestos, en orden de colocación.</summary>
    private readonly List<Defender> _defensores = [];

    /// <summary>
    /// Los NPCs comprados de la oleada actual que todavía no entraron, en el
    /// orden en que se compraron.
    /// </summary>
    private readonly Queue<NpcPurchase> _porSpawnear = new();

    /// <summary>
    /// Los ecos pendientes, en orden de creación.
    /// </summary>
    /// <remarks>
    /// ⚠ Se resuelven <b>en orden de creación</b>, nunca al revés. Es la misma
    /// regla que la lista de enemigos: dos ecos que caen en el mismo tick sobre
    /// el mismo grupo tienen que resolverse siempre igual, o dos corridas con la
    /// misma seed matan enemigos distintos.
    /// </remarks>
    private readonly List<PendingEcho> _ecos = [];

    /// <summary>
    /// Los Oro que esta corrida puede llegar a ver. Se arma en el primer jefe.
    /// </summary>
    /// <remarks>
    /// ⚠ El catálogo tiene 22 Oro y ofrecerlos todos <b>diluye las recetas</b>:
    /// cada jefe pasa a ser una elección entre veintidós cosas casi equivalentes y
    /// juntar los dos componentes de un plano vuelve a ser suerte. Con un pool
    /// acotado por corrida, lo que sale se repite lo suficiente como para poder
    /// perseguirlo (Drops_v2).
    /// </remarks>
    private readonly List<int> _poolOro = [];

    /// <summary>
    /// Cuántos Oro se fabricaron mejorando Plata en esta corrida.
    /// </summary>
    /// <remarks>
    /// ⚠ El tope que lo acompaña —<c>maxCraftedGoldPerRun</c>— es lo que impide
    /// que la mejora reemplace al jefe como fuente de Oro. Ver
    /// <see cref="UpgradeData"/>.
    /// </remarks>
    private int _oroFabricado;

    /// <summary>
    /// Oro extra que ya pagaron las marcas de botín en esta oleada. Tiene tope.
    /// </summary>
    /// <remarks>
    /// <para>Lo comparten Saqueo —el perk de Oro— y Codicia —la habilidad de arma
    /// legendaria—, y compartirlo es lo correcto: <b>es un presupuesto de oro
    /// extra por oleada</b>, no el contador de una habilidad. Un enemigo lleva una
    /// sola marca a la vez, así que las dos nunca se pisan sobre el mismo bicho;
    /// lo que sí se pisan es el techo total, que es justo lo que se quiere acotar.</para>
    ///
    /// <para>⚠ Codicia dejó de ser la única habilidad sin techo. La economía tiene
    /// realimentación positiva —más oro, más defensores, más kills, más oro— así
    /// que una habilidad económica sin techo no es fuerte: <b>es la única
    /// build</b>. Sigue siendo mejor que Saqueo, como corresponde a una
    /// legendaria, pero ya no puede duplicar la economía de una oleada entera.</para>
    /// </remarks>
    private int _oroBotinOleada;

    /// <summary>
    /// Vida que ya curó Vampirismo en esta oleada, en la escala interna. Tiene tope.
    /// </summary>
    /// <remarks>
    /// ⚠ La ventana de 30 ticks por clase limita el <b>ritmo</b>, no el total: una
    /// oleada larga con una build de cadencia seguía curando más de lo que la
    /// oleada podía bajar, y la Torre quedaba prácticamente inmortal.
    /// </remarks>
    private int _curaVampirismoOleada;

    /// <summary>Ticks entre spawn y spawn de la oleada actual.</summary>
    /// <remarks>Arranca en el de <c>curvas.json</c> hasta que la primera oleada imponga el suyo.</remarks>
    private int _intervaloSpawn;

    /// <summary>
    /// Con qué forma entra la oleada actual. La sortea el armador.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Conserva el promedio del intervalo</b>, así que la oleada dura lo
    /// mismo: no es dificultad encubierta. Lo que cambia es el valor <b>relativo</b>
    /// del catálogo, que es la lección de Rogue Tower — que los enemigos lleguen
    /// juntos o separados altera qué torres sirven sin tocar una estadística.
    /// </remarks>
    private SpawnFormation _formacionOleada;

    /// <summary>Cuántos NPCs entraron ya en la oleada actual. Es el índice de la formación.</summary>
    private int _spawneadosEnOleada;

    /// <summary>
    /// La preparación en curso renovó el stock de la tienda.
    /// </summary>
    /// <remarks>
    /// Bandera y no un módulo recalculado en dos lugares: para cuando se lanza la
    /// oleada, <c>Wave</c> ya avanzó, y la cuenta habría que hacerla contra
    /// <c>Wave - 1</c>. Un desfasaje de uno en ese módulo ya movió una vez el
    /// momento en que abre la tienda —una oleada antes de la elección, cuando el
    /// punto entero es que caigan juntas— y de paso dejó vivo un stock que la
    /// elección podía invalidar.
    /// </remarks>
    private bool _tiendaAbierta;

    /// <summary>Oro que paga limpiar la oleada actual.</summary>
    private int _bonusOroOleada;

    /// <summary>Las clases que el jugador tiene, con su nivel, perks y equipo.</summary>
    public Roster Roster { get; }

    /// <summary>Los lugares de mochila, compartidos entre perks y equipo.</summary>
    public Backpack Backpack { get; }

    /// <summary>La tienda de la corrida. El stock lo decide la seed.</summary>
    public Shop Shop { get; } = new();

    /// <summary>
    /// Lo que pasó adentro de la corrida, contado mientras pasa.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Es salida, no entrada.</b> Nada de acá se lee para decidir nada, así
    /// que contar de más no puede cambiar una corrida y no entra al hash. Existe
    /// porque tres veces seguidas razonar sobre el balance dio la conclusión
    /// equivocada y medirlo dio la correcta — y con la tienda rotativa, «terminó
    /// con 12.000 sin gastar» sin desglose no dice si sobra oro, si los precios
    /// están mal o si no había nada que valiera la pena.
    /// </remarks>
    public RunMetrics Metrics { get; } = new();

    /// <summary>
    /// Las 3 opciones de la pantalla de elección, cuando está abierta.
    /// </summary>
    /// <remarks>
    /// <b>Las genera la seed, no se guardan en el log.</b> El log lleva solo cuál
    /// de las 3 eligió el jugador, por índice. Si guardara el contenido, un
    /// cliente modificado podría reclamar que le ofrecieron algo mejor de lo que
    /// el servidor va a regenerar.
    /// </remarks>
    public Choice[] OpenChoices { get; private set; } = [];

    public bool WaitingForChoice => OpenChoices.Length > 0;

    /// <summary>
    /// La oleada anterior está limpia y la próxima espera el visto bueno.
    /// </summary>
    /// <remarks>
    /// <para>Es la fase de preparación: se mira el tablero, se ponen defensores,
    /// se equipa y se compra. Reemplaza al hueco de 15 segundos que <b>corría el
    /// reloj</b>, y ese es todo el cambio: la preparación ahora es gratis.</para>
    ///
    /// <para>El tope de 30 segundos es de la interfaz, no de acá: se mide en reloj
    /// de pared y no entra a la simulación. Para el tick, esperar 2 segundos o 29
    /// es exactamente lo mismo.</para>
    /// </remarks>
    public bool WaitingForWaveStart { get; private set; }

    /// <summary>El reloj está detenido: preparación o elección.</summary>
    public bool Paused => WaitingForWaveStart || WaitingForChoice;

    /// <summary>
    /// La corrida no puede avanzar sola: espera al jugador y el log está agotado.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Sin esto, re-simular un log que termina en pausa es un cuelgue.</b>
    /// Los dos bucles que re-simulan —cargar una partida guardada y revalidar—
    /// giran mientras <c>Tick</c> no pase de un tope. Con el reloj congelado
    /// <c>Tick</c> no se mueve nunca, así que la condición no falla jamás.</para>
    ///
    /// <para>Y es exactamente el caso normal de una partida guardada: la pantalla
    /// de preparación es donde el jugador deja la corrida, así que el log termina
    /// ahí <b>casi siempre</b>.</para>
    /// </remarks>
    public bool EsperaAlJugador => Paused && _cursorLog >= _log.Count;

    /// <summary>Qué número de oleada viene. Durante la preparación, la que espera.</summary>
    public int ProximaOleada => _oleadaArmada is null ? Wave : Wave + 1;

    /// <summary>
    /// Qué trae la oleada que viene: cuántos de cada bicho y cuáles con perk.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que la pantalla de preparación muestra abajo. Sin esto el
    /// jugador arma su tablero a ciegas y la decisión se vuelve adivinanza —
    /// contra un jefe conviene daño concentrado, contra treinta hormigas conviene
    /// daño en área, y son decisiones opuestas.</para>
    ///
    /// <para>⚠ Que un NPC traiga perk <b>tiene que verse</b>, y este es el lugar:
    /// [INTERFAZ.md] §5 lo dejó anotado como pendiente porque un jugador que
    /// pierde contra un lobo blindado sin saber que estaba blindado no aprende
    /// nada.</para>
    /// </remarks>
    public IReadOnlyList<WavePreview> AvanceProximaOleada()
    {
        if (_oleadaArmada is not { } armada)
            return [];

        var porTipo = new Dictionary<int, WavePreview>();
        var orden = new List<int>();

        foreach (var compra in armada.Npcs)
        {
            if (!porTipo.TryGetValue(compra.TypeId, out var fila))
            {
                var dato = _datos.Get(compra.TypeId);

                fila = new WavePreview
                {
                    TypeId = compra.TypeId,
                    Name = dato?.Name ?? "?",
                    Graphic = dato?.Graphic ?? "",
                    IsBoss = compra.IsBoss,
                    Hp = dato?.Hp ?? 0,
                    MaxDamage = dato?.MaxDamage ?? 0,
                    Speed = dato?.Speed ?? 0,
                };

                porTipo[compra.TypeId] = fila;
                orden.Add(compra.TypeId);
            }

            fila.Cuantos++;

            if (compra.HealthPerk > 0 || compra.SpeedPerk > 0)
                fila.ConPerk++;

            // ⚠ Y se cuenta CUÁL, no solo cuántos.
            //
            // Antes esto era un contador único y la pantalla decía «1 con rasgo
            // (impulsivo o líder)» — o sea que el jugador tenía que prepararse
            // para las dos cosas porque no se le decía cuál era. Contra un líder
            // conviene concentrar fuego en él; contra un impulsivo, tener algo
            // listo antes de tiempo. Son dos respuestas distintas.
            if (compra.HealthPerk > 0)
                fila.ConVida++;

            if (compra.SpeedPerk > 0)
                fila.ConVelocidad++;

            // Los rasgos también tienen que verse: un líder que se ve igual que
            // un lobo normal es una mecánica invisible, y el jugador que pierde
            // contra algo que no sabía que estaba ahí no aprende nada.
            if (compra.Impulsive)
                fila.Impulsivos++;

            if (compra.Leader)
                fila.Lideres++;

            if (compra.Impulsive || compra.Leader)
                fila.ConRasgo++;
        }

        var salida = new List<WavePreview>(orden.Count);

        // En orden de aparición: el jugador ve la lista en el mismo orden en que
        // los bichos van a entrar por la ruta.
        foreach (int id in orden)
            salida.Add(porTipo[id]);

        return salida;
    }

    /// <summary>
    /// En qué tick entra una acción que el jugador toma <b>ahora</b>.
    /// </summary>
    /// <remarks>
    /// <para>Con el reloj corriendo va al tick <b>siguiente</b>: el actual puede
    /// estar a medio correr —el click llega en cualquier momento del frame— y
    /// meter una acción en la mitad de las nueve fases haría que el resultado
    /// dependa de cuándo cayó el evento adentro del frame.</para>
    ///
    /// <para>⚠ En pausa va al tick <b>actual</b>, y tiene que ser así: con el
    /// reloj detenido, una acción encolada para el tick siguiente no se aplicaría
    /// nunca —<c>Tick</c> no avanza— y el jugador quedaría trabado sin poder
    /// arrancar la oleada. El peligro que justifica el +1 no existe en pausa,
    /// justamente porque no hay ningún tick corriendo.</para>
    /// </remarks>
    public int TickDeAccion => Paused ? Tick : Tick + 1;

    /// <summary>Planos aprendidos en la cuenta. Los manda el servidor al empezar.</summary>
    private readonly HashSet<string> _planosAprendidos = [];

    private int _proximoIdSpawn;
    private int _proximaColocacion;
    private int _oleadaUltimaEleccion;
    private int _cursorLog;
    private int _ticksParaProximoSpawn;

    /// <summary>La oleada que viene, ya armada y esperando el botón.</summary>
    private BuiltWave? _oleadaArmada;

    /// <summary>
    /// Los datos de balance de la temporada. <b>Entrada de la simulación</b>,
    /// igual que la seed y el log.
    /// </summary>
    private readonly BalanceData _datos;

    public BalanceData Datos => _datos;

    /// <summary>
    /// Arma una corrida.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><paramref name="datos"/> es obligatorio.</b> Antes era opcional y se
    /// caía a un bestiario provisional escrito en el código; eso desapareció
    /// cuando todo el balance pasó a <c>Datos\*.json</c>. Una tabla de reserva
    /// compilada adentro es una segunda copia de los números que puede discrepar
    /// de la de disco, y el síntoma —una corrida que mide otra cosa— no se ve
    /// hasta que el ranking no cierra.
    /// </remarks>
    public Run(ulong seed, Map mapa, ActionLog log, BalanceData datos)
    {
        _rng = new Rng(seed);
        _mapa = mapa;
        _ruta = mapa.Route;
        _log = log;
        _grilla = new SpatialGrid(mapa.Width, mapa.Height);
        _datos = datos;

        Roster = new Roster(datos);
        Backpack = new Backpack(datos);
        _intervaloSpawn = datos.Curves.Wave.DefaultSpawnInterval;

        // El HP de la Torre entra en la escala interna, igual que el de los
        // enemigos y el daño de los defensores: los tres son el mismo eje y
        // escalar uno solo cambiaría el balance en vez de afinarlo.
        Tower = new Tower
        {
            MaxHp = Ints.ToHundredths(_datos.Curves.Tower.HpInicial),
            Hp = Ints.ToHundredths(_datos.Curves.Tower.HpInicial),
            Cell = _ruta.End,
        };
    }

    /// <summary>Tick actual. Es la unidad del ranking: menos ticks es mejor.</summary>
    public int Tick { get; private set; }

    /// <summary>Oleada actual, de 1 a 100.</summary>
    public int Wave { get; private set; }

    public int Gold { get; private set; }

    public Tower Tower { get; }

    public RunState State { get; private set; } = RunState.InProgress;

    public IReadOnlyList<Enemy> Enemies => _enemigos;

    public IReadOnlyList<Defender> Defenders => _defensores;

    public Route Route => _ruta;

    public Map Map => _mapa;

    /// <summary>
    /// ¿Se puede poner un defensor de esta clase en esta celda, ahora?
    /// </summary>
    /// <remarks>
    /// Lo usa la UI para pintar la celda de verde o de rojo bajo el cursor, y lo
    /// usa <see cref="Poner"/> para decidir. <b>Es la misma función en los dos
    /// casos, a propósito:</b> si la UI dijera que sí y la simulación que no, el
    /// jugador vería un click que no hace nada.
    /// </remarks>
    public bool PuedePoner(DefenderClass clase, Cell celda)
    {
        // Solo se puede poner una clase contratada. El roster arranca vacío y se
        // llena eligiendo, así que la primera elección es obligatoria antes de
        // poder jugar.
        if (!Roster.Has(clase))
            return false;

        if (!_mapa.EsColocable(celda))
            return false;

        if (Gold < _datos.StatsOf(clase).Cost)
            return false;

        foreach (var d in _defensores)
            if (d.Cell == celda)
                return false;

        return true;
    }

    /// <summary>El defensor que está en esa celda, si hay alguno.</summary>
    public Defender? DefensorEn(Cell celda)
    {
        foreach (var d in _defensores)
            if (d.Cell == celda)
                return d;

        return null;
    }

    /// <summary>
    /// La ficha con ese <see cref="Defender.PlacementId"/>, o <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Por id y no por celda: mientras se arrastra una ficha, su celda es
    /// justamente el dato que está cambiando.
    /// </remarks>
    public Defender? DefensorPorId(int idColocacion)
    {
        foreach (var d in _defensores)
            if (d.PlacementId == idColocacion)
                return d;

        return null;
    }

    /// <summary>
    /// Avanza exactamente un tick.
    /// </summary>
    /// <remarks>
    /// <para><b>El orden de las nueve fases está congelado.</b> Cambiarlo cambia
    /// el resultado de corridas ya guardadas: una revalidación que corra con
    /// otro orden va a decir que el jugador hizo trampa. Si alguna vez hay que
    /// cambiarlo, es una temporada nueva del ranking, no un arreglo
    /// (JUGABILIDAD.md §4.4).</para>
    /// </remarks>
    public void Advance()
    {
        if (State != RunState.InProgress)
            return;

        // 1. Acciones del jugador encoladas para este tick.
        _cursorLog = _log.AplicarDesde(_cursorLog, Tick, AplicarAccion);

        // ⚠ En pausa el reloj NO corre: se atienden las acciones y se sale sin
        // tocar `Tick`.
        //
        // Es lo que hace que la preparación entre oleadas sea gratis. La regla de
        // INTERFAZ.md §5 —"pausar es tiempo de pensar gratis en una tabla de
        // velocidad"— apuntaba a la pausa A DEMANDA, la de frenar el reloj en
        // medio de una filtración para pensar cómo salir. Esta es otra cosa: cae
        // en un punto fijo, dura lo mismo para todos y no deja reaccionar a nada
        // que esté pasando. Los `ticks` siguen midiendo cuánto tardás en matar,
        // que es lo que dice si tu tablero es bueno.
        if (Paused)
            return;

        // 2. Spawn de los que le tocan a este tick, con sus perks ya tirados.
        FaseSpawn();

        // 3. Estados: vencer lo que se acabó, cobrar los DoT, soltar los ecos.
        FaseEstados();

        // 4. Mover: distanciaRecorrida += velocidadEfectiva.
        FaseMover();

        // 5. Llegadas: los que alcanzaron el final pasan a atacar la Torre.
        FaseLlegadas();

        // 6. Combate de la Torre: los que están en contacto le pegan.
        FaseCombateTorre();

        // 7. Defensores: bajar cooldown, elegir objetivo, disparar.
        FaseDefensores();

        // 8. Muertes: sumar el oro, tirar el drop, sacar a los muertos.
        FaseMuertes();

        // 9. Chequeos de fin.
        FaseChequeos();

        Tick++;
    }

    /// <summary>Corre N ticks de una. Para acelerar y para cargar una partida.</summary>
    public void Advance(int ticks)
    {
        for (int i = 0; i < ticks && State == RunState.InProgress; i++)
            Advance();
    }

    // =========================================================================
    //  Fase 1 — Acciones del jugador
    // =========================================================================

    private void AplicarAccion(PlayerAction accion)
    {
        switch (accion.Kind)
        {
            case ActionType.Place:
                Place((DefenderClass)accion.A, new Cell(accion.B, accion.C));
                break;

            case ActionType.Sell:
                Sell(accion.A);
                break;

            case ActionType.CallNextWave:
                Adelantar();
                break;

            case ActionType.Choose:
                Choose(accion.A);
                break;

            case ActionType.EquipPerk:
                EquipPerk(accion.A, (DefenderClass)accion.B);
                break;

            case ActionType.RemovePerk:
                RemovePerk((DefenderClass)accion.A, accion.B);
                break;

            case ActionType.Equip:
                Equip(accion.A, (DefenderClass)accion.B);
                break;

            case ActionType.SellGear:
                SellGear(accion.A);
                break;

            case ActionType.Buy:
                Buy(accion.A);
                break;

            case ActionType.Discard:
                Backpack.RemoveAt(accion.A);
                break;

            case ActionType.BuyLevel:
                BuyLevel((DefenderClass)accion.A);
                break;

            case ActionType.Fuse:
                Fuse(accion.A, accion.B, (DefenderClass)accion.C);
                break;

            case ActionType.Upgrade:
                Upgrade(accion.A, (PerkTier)accion.B);
                break;

            case ActionType.MoveDefender:
                MoveDefender(accion.A, new Cell(accion.B, accion.C));
                break;

            case ActionType.SetTargeting:
                SetTargeting(accion.A, (TargetingMode)accion.B);
                break;

            case ActionType.SetBehavior:
                SetBehavior((DefenderClass)accion.A, accion.B != 0);
                break;

            // El resto entra con sus sistemas. Se ignoran en silencio y no se
            // rompe: un log viejo tiene que poder re-simularse aunque el sistema
            // todavía no exista.
            default:
                break;
        }
    }

    private void Place(DefenderClass clase, Cell celda)
    {
        // Se re-valida acá aunque la UI ya haya validado, porque el log de una
        // corrida guardada se re-simula por este mismo camino: un log manipulado
        // que pida poner en una celda prohibida tiene que rebotar igual.
        if (!PuedePoner(clase, celda))
            return;

        var stats = _datos.StatsOf(clase);
        Gold -= stats.Cost;
        Metrics.GoldSpentDefenders += stats.Cost;

        _defensores.Add(new Defender
        {
            PlacementId = _proximaColocacion++,
            DefenderClass = clase,
            Cell = celda,
            PlacementTick = Tick,
            RemainingCooldown = 0,
        });
    }

    /// <summary>
    /// Mueve una ficha ya puesta. Si el destino está ocupado, <b>intercambia</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Solo con el reloj congelado.</b> Mover con la oleada corriendo
    /// es reposicionar en combate —perseguir al bicho, esquivar al jefe— y eso es
    /// otra mecánica, con otro balance. Congelado no cuesta ticks, así que no
    /// toca el ranking.</para>
    ///
    /// <para><b>Gratis.</b> Antes acomodar una colocación mal puesta era vender y
    /// volver a comprar, y al 60% de devolución convenía dejarla mal — que es
    /// justo lo que subir la devolución de 50 a 60 venía a evitar.</para>
    ///
    /// <para>El intercambio no necesita revalidar nada: las dos celdas ya tenían
    /// una ficha parada, así que las dos son colocables por construcción. El
    /// destino vacío sí, porque puede ser ruta o zona bloqueada.</para>
    ///
    /// <para>Se conserva todo el estado por unidad —cooldown, targeting,
    /// concentración, contador de golpes—. Es la misma ficha corrida de lugar, no
    /// una nueva: reiniciarlo convertiría mover en una forma barata de resetear
    /// los contadores.</para>
    /// </remarks>
    private void MoveDefender(int idColocacion, Cell destino)
    {
        if (!WaitingForWaveStart || !Map.InGrid(destino))
            return;

        Defender? quien = null;

        foreach (var d in _defensores)
        {
            if (d.PlacementId == idColocacion)
            {
                quien = d;
                break;
            }
        }

        if (quien is null || quien.Cell == destino)
            return;

        var otro = DefensorEn(destino);

        if (otro is not null)
        {
            (quien.Cell, otro.Cell) = (destino, quien.Cell);
            return;
        }

        if (!Map.EsColocable(destino))
            return;

        quien.Cell = destino;
    }

    /// <summary>
    /// Llama la próxima oleada antes de tiempo.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe por el ranking.</b> Con un tope duro en la oleada 100,
    /// todos los que ganan empatan; el desempate es por ticks, y sin una forma de
    /// ir más rápido ese desempate no mide nada.</para>
    ///
    /// <para><b>El riesgo:</b> la oleada nueva entra <b>sin que la anterior haya
    /// terminado de morir</b>. Eso es riesgo real y sale de la mecánica, no de un
    /// número arbitrario: adelantar dos veces seguidas apila tres oleadas
    /// caminando juntas.</para>
    ///
    /// <para>La misma acción hace <b>dos cosas según el momento</b>, y es un solo
    /// botón para el jugador:</para>
    ///
    /// <list type="bullet">
    /// <item>En la <b>fase de preparación</b> —oleada limpia, reloj detenido— es
    /// «comenzar oleada». No paga bonus: no se ahorró nada, porque esperar en
    /// pausa no cuesta ticks.</item>
    /// <item>Con la oleada <b>todavía viva</b> es adelantar de verdad. Paga bonus
    /// <b>proporcional a lo que queda en pie</b>, que es la medida del riesgo que
    /// se está tomando.</item>
    /// </list>
    ///
    /// <para>⚠ El bonus se movió de "ticks ahorrados de la pausa" a "enemigos
    /// vivos". Con la pausa gratis, lo primero pagaba por saltear una espera que
    /// ya no cuesta nada; lo segundo paga por meterse dos oleadas encima, que es
    /// lo que se quería premiar desde el principio.</para>
    /// </remarks>
    private void Adelantar()
    {
        // ⚠ Con la elección sin resolver no se lanza nada.
        //
        // Desde que el mercado junta la elección y la tienda en una ventana, la
        // preparación se arma ANTES de abrir la elección, así que las dos banderas
        // están prendidas a la vez. Sin esta guarda, un log —o un bot— que mande
        // «comenzar oleada» en ese tick se saltea la elección entera y con ella el
        // Oro del jefe o la clase nueva.
        if (WaitingForChoice)
            return;

        if (WaitingForWaveStart)
        {
            WaitingForWaveStart = false;
            LaunchNextWave();
            return;
        }

        // Con la oleada limpia y sin fase de preparación abierta no hay nada que
        // adelantar: la elección todavía no se resolvió, o la corrida terminó.
        if (_enemigos.Count == 0 && _porSpawnear.Count == 0)
            return;

        int vivos = _enemigos.Count + _porSpawnear.Count;

        Gold += Ints.Clamp(vivos * _datos.Curves.Economy.GoldPerCalledEnemy, 0, _datos.Curves.Economy.CallGoldCap);

        LaunchNextWave();
    }

    private void Choose(int indice)
    {
        if (indice < 0 || indice >= OpenChoices.Length)
            return;

        var opcion = OpenChoices[indice];

        // El oro inicial se entrega ACÁ y no en Empezar, porque depende de qué
        // clase tocó: dos fichas de TU clase, la misma cantidad para todos.
        //
        // Con un número fijo el arranque era una lotería: 200 eran cinco ladrones
        // o dos cazadores, y las corridas de clase cara se estancaban en la
        // oleada 1 — medido con --balance, mitad de las seeds muertas ahí. Es
        // determinista igual: sale de la acción de elegir, que está en el log.
        // ⚠ La oferta de Oro del jefe se resuelve acá y sale por otro camino: no
        // toca el roster, no entrega oro y no consume RNG. El perk va a la
        // mochila y el jugador decide después en qué clase lo pone.
        if (opcion.Kind == ChoiceType.Perk)
        {
            // Mochila llena: se pierde, igual que un drop común. La UI de verdad
            // abre el diálogo de "tirá uno" antes de llegar acá.
            Backpack.StorePerk(opcion.PerkId);

            if (_datos.Perks.Get(opcion.PerkId).Tier == PerkTier.Gold)
                Metrics.GoldPerksObtained++;

            OpenChoices = [];
            return;
        }

        bool primeraClase = Roster.Classes.Count == 0;

        if (opcion.Kind == ChoiceType.Hire)
        {
            Roster.Hire(opcion.DefenderClass);

            // ⚠ La tienda se arma ANTES de la elección para que las dos se vean
            // juntas, así que puede estar ofreciendo la misma clase que esto acaba
            // de regalar. Se retira: pagar por algo que ya se tiene es tirar oro,
            // y dejarla comprable cuelga al bot que reintenta.
            Shop.RetirarClase(opcion.DefenderClass);

            // Contratar una clase nueva ABRE Oro nuevo (Drops_v2): sin esto, un
            // roster que crece no cambia lo que el jefe puede ofrecer y contratar
            // tarde deja a la clase nueva sin comportamiento propio para siempre.
            AmpliarPoolDeOro(opcion.DefenderClass);
        }
        else
        {
            Roster.LevelUp(opcion.DefenderClass);
        }

        if (primeraClase)
            Gold = _datos.Curves.Economy.StartingGoldTokens * _datos.StatsOf(opcion.DefenderClass).Cost;

        OpenChoices = [];
    }

    /// <summary>Equipa un perk de la mochila en una clase.</summary>
    private void EquipPerk(int indiceMochila, DefenderClass clase)
    {
        if (indiceMochila < 0 || indiceMochila >= Backpack.Items.Count)
            return;

        var cosa = Backpack.Items[indiceMochila];
        if (cosa.Kind != ItemType.Perk)
            return;

        var ficha = Roster.Find(clase);
        if (ficha is null || !ficha.HasFreeSlot)
            return;

        var def = _datos.Perks.Get(cosa.Id);
        if (!def.WorksFor(clase))
            return;

        // ⚠ LA RANURA DE COMPORTAMIENTO ES UNA SOLA (Drops_v2).
        //
        // Antes se podían equipar tres Oro en la misma clase y solo el de tier
        // más alto hacía algo: dos slots y dos drops de jefe —o sea dos de las
        // ocho oportunidades de Oro de toda la corrida— gastados en nada, sin
        // que el juego avisara. Rebotar el segundo es la versión honesta: para
        // cambiar de comportamiento hay que sacar el que está, y sacarlo lo
        // destruye, que es la decisión que el sistema quiere que se tome.
        if (EsComportamiento(def.Tier) && ComportamientoDe(ficha) is not null)
            return;

        // El tope de apilado sale del tier: Bronce 3, Plata 2, Oro y Único 1.
        int yaTiene = 0;
        foreach (int p in ficha.Perks)
            if (p == cosa.Id)
                yaTiene++;

        if (yaTiene >= def.MaxStacked)
            return;

        Backpack.RemoveAt(indiceMochila);
        ficha.Perks.Add(cosa.Id);
    }

    /// <summary>
    /// Saca un perk equipado. <b>Lo destruye.</b>
    /// </summary>
    /// <remarks>
    /// No vuelve a la mochila, y eso es deliberado: la mochila es una sala de
    /// espera, no una bóveda. Sin esta regla, equipar dejaría de ser una decisión
    /// —probás uno, si no gusta lo volvés a guardar— y la elección se vuelve
    /// reversible, o sea gratis.
    /// </remarks>
    private void RemovePerk(DefenderClass clase, int slot)
    {
        var ficha = Roster.Find(clase);
        if (ficha is null || slot < 0 || slot >= ficha.Perks.Count)
            return;

        ficha.Perks.RemoveAt(slot);
    }

    private void Equip(int indiceMochila, DefenderClass clase)
    {
        if (indiceMochila < 0 || indiceMochila >= Backpack.Items.Count)
            return;

        var cosa = Backpack.Items[indiceMochila];
        if (cosa.Kind != ItemType.Weapon)
            return;

        var ficha = Roster.Find(clase);
        var arma = Backpack.WeaponById(cosa.Id);
        if (ficha is null || arma is null)
            return;

        // Lo que estaba puesto vuelve a la mochila si hay lugar. ⚠ El equipo SÍ
        // vuelve, al revés que los perks: el equipo se vende y se recambia, los
        // perks se destruyen. Son dos sistemas y no se mezclan.
        int anterior = ficha.Weapon;

        if (anterior >= 0 && !Backpack.Store(new StoredItem(ItemType.Weapon, anterior)))
            return;

        Backpack.RemoveAt(indiceMochila);
        ficha.Weapon = arma.Id;
    }

    private void SellGear(int indiceMochila)
    {
        if (indiceMochila < 0 || indiceMochila >= Backpack.Items.Count)
            return;

        var cosa = Backpack.Items[indiceMochila];
        if (cosa.Kind != ItemType.Weapon)
            return;

        var arma = Backpack.WeaponById(cosa.Id);
        if (arma is null)
            return;

        Backpack.RemoveAt(indiceMochila);
        Gold += arma.Value;
    }

    /// <summary>
    /// Compra un nivel de una clase que ya está en el roster.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es el sumidero de oro de la corrida.</b> Sin esto el jugador
    /// terminaba con el tablero lleno, la mochila llena y ~127.000 de oro que no
    /// compraban nada: el tope de lo que se puede <i>tener</i> llegaba mucho antes
    /// que el de lo que se puede <i>pagar</i>. Y no se arreglaba recortando el
    /// ingreso —con la mitad seguían sobrando 60.000—, porque el problema no era
    /// cuánto entra.</para>
    ///
    /// <para><b>El oro compra profundidad, no ancho.</b> Solo sube clases que ya
    /// están: contratar sigue siendo exclusivo de la elección de cada tres
    /// oleadas, así que esa decisión no se puede comprar. El precio cuadrático
    /// hace el resto: temprano el oro se va al tablero porque un nivel no entra en
    /// el bolsillo.</para>
    ///
    /// <para>No pasa del nivel máximo, que es también la cantidad de slots de
    /// perk: comprar más allá no abriría nada y sería oro tirado en silencio.</para>
    /// </remarks>
    private void BuyLevel(DefenderClass clase)
    {
        var ficha = Roster.Find(clase);

        if (ficha is null || ficha.Level >= ficha.MaxLevel)
            return;

        int precio = _datos.Curves.Economy.LevelUpPrice(ficha.Level + 1);

        if (precio > Gold)
            return;

        Gold -= precio;
        Metrics.GoldSpentLevels += precio;
        Roster.LevelUp(clase);
    }

    /// <summary>
    /// Fusiona dos perks de Oro de la mochila en un Único, y lo equipa.
    /// </summary>
    /// <remarks>
    /// <para><b>Los Únicos no caen y no se compran: se fabrican.</b> Dos Oro se
    /// consumen y sale uno, que ocupa <b>un solo slot</b>. El slot liberado es la
    /// mitad del premio.</para>
    ///
    /// <para>La receta es un <b>plano</b>, comprado una vez con la billetera desde
    /// el menú principal y aprendido para siempre en esa cuenta. Junto con la
    /// billetera, es la única progresión permanente del proyecto — y la lista es
    /// finita, que es la condición que hace tolerable la ventaja del veterano.</para>
    /// </remarks>
    private void Fuse(int indiceA, int indiceB, DefenderClass clase)
    {
        if (indiceA == indiceB)
            return;

        if (indiceA < 0 || indiceA >= Backpack.Items.Count ||
            indiceB < 0 || indiceB >= Backpack.Items.Count)
        {
            return;
        }

        var a = Backpack.Items[indiceA];
        var b = Backpack.Items[indiceB];

        if (a.Kind != ItemType.Perk || b.Kind != ItemType.Perk)
            return;

        if (_datos.Perks.Get(a.Id).Tier != PerkTier.Gold ||
            _datos.Perks.Get(b.Id).Tier != PerkTier.Gold)
        {
            return;
        }

        var plano = _datos.Blueprints.Find(a.Id, b.Id);
        if (plano is null)
            return;

        // El plano tiene que estar aprendido, salvo los iniciales: sin al menos
        // una receta conocida, un jugador nuevo nunca ve un Único y la mecánica
        // le resulta invisible.
        if (!plano.Initial && !_planosAprendidos.Contains(plano.Key))
            return;

        var ficha = Roster.Find(clase);
        if (ficha is null)
            return;

        var resultante = _datos.Perks.Get(plano.ResultingUnique);
        if (!resultante.WorksFor(clase))
            return;

        // Se sacan en orden descendente para que el primer índice siga siendo
        // válido después de sacar el segundo.
        int mayor = System.Math.Max(indiceA, indiceB);
        int menor = System.Math.Min(indiceA, indiceB);
        Backpack.RemoveAt(mayor);
        Backpack.RemoveAt(menor);

        Metrics.UniquesCrafted++;

        // La oleada del PRIMER Único es la métrica que dice si la composición es
        // alcanzable: en la 90 el sistema existe pero no llega a jugarse, en la 20
        // llega demasiado fácil.
        if (Metrics.WaveOfFirstUnique == 0)
            Metrics.WaveOfFirstUnique = Wave;

        // Si no hay slot libre —o si la clase ya tiene un comportamiento puesto—
        // el Único va a la mochila y el jugador decide después. Perderlo por no
        // tener lugar sería castigar la mecánica que el juego quiere enseñar, y
        // meterlo pisando el Oro equipado destruiría un Oro sin preguntar.
        if (ficha.HasFreeSlot && ComportamientoDe(ficha) is null)
            ficha.Perks.Add(plano.ResultingUnique);
        else
            Backpack.StorePerk(plano.ResultingUnique);
    }

    /// <summary>Cuántos Oro se fabricaron mejorando en esta corrida.</summary>
    public int CraftedGold => _oroFabricado;

    /// <summary>El tope de Oro fabricable por corrida. 0 = la mejora a Oro está apagada.</summary>
    public int MaxCraftedGold => _datos.Curves.Upgrades.MaxCraftedGoldPerRun;

    /// <summary>
    /// Si el pool de Oro de la corrida ya existe.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Se consulta, no se construye.</b> El pool se arma en el primer jefe y
    /// armarlo consume RNG: una propiedad de lectura que lo creara movería el
    /// punto de consumo según quién mire, y la revalidación del servidor fallaría
    /// sin que nadie haya hecho trampa. Antes del primer jefe esto es
    /// <c>false</c> y mejorar Plata → Oro rebota.
    /// </remarks>
    public bool GoldPoolReady => _poolOro.Count > 0;

    /// <summary>Cuántos perks de ese tier hacen uno del tier de arriba. 0 = no se puede.</summary>
    public int UpgradeCost(PerkTier tier) => _datos.Curves.Upgrades.CostOf(tier);

    /// <summary>
    /// Mejora varios perks de un tier en uno solo del tier de arriba.
    /// </summary>
    /// <remarks>
    /// <para><b>Es una salida para la basura, no una fuente de poder.</b> Con
    /// del orden de 100 Bronce y 40 Plata cayendo por corrida contra 15 lugares
    /// pasada la oleada 20 todo drop cae al vacío en silencio; esto le da
    /// destino a lo que sobra sin agrandar la mochila, que es lo que convertiría
    /// «qué me sirve» en una planilla.</para>
    ///
    /// <para>⚠ <b>El Oro fabricado tiene tope por corrida</b>, y ese tope es lo
    /// que sostiene toda la economía del Oro: el juego está construido sobre ocho
    /// Oro de jefe más lo poco que venda la tienda, y los Únicos cuestan dos
    /// justamente porque hay pocos. Sin tope, doce Bronce por Oro entregaría más
    /// que los ocho jefes juntos.</para>
    ///
    /// <para>El Oro sale del <b>pool de la corrida</b> y <b>sin</b> el peso de
    /// sinergia: perseguir una receta concreta sigue siendo privilegio del jefe,
    /// que es el que ofrece de a dos para elegir. La Plata sale del catálogo
    /// filtrado por las clases del roster, igual que un drop.</para>
    ///
    /// <para>⚠ <paramref name="mascara"/> es la selección del jugador, un bit por
    /// lugar de mochila. Todos los chequeos —bits fuera de rango, algo que no sea
    /// un perk, un tier que no coincide, la cantidad exacta— van <b>antes</b> de
    /// tocar el RNG: un rebote que hubiera consumido un número al azar
    /// desincronizaría la revalidación del servidor.</para>
    /// </remarks>
    private void Upgrade(int mascara, PerkTier tier)
    {
        int cuantos = _datos.Curves.Upgrades.CostOf(tier);

        if (cuantos < 2 || mascara <= 0)
            return;

        // El tope se mira antes que nada: si ya no hay cupo, no se consume nada.
        if (tier == PerkTier.Silver && _oroFabricado >= _datos.Curves.Upgrades.MaxCraftedGoldPerRun)
            return;

        // Un bit encendido más allá de lo que hay en la mochila es un log
        // manipulado, no una selección: rebota entero en vez de ignorarlo.
        int validos = Backpack.Items.Count >= 31 ? int.MaxValue : (1 << Backpack.Items.Count) - 1;

        if ((mascara & ~validos) != 0)
            return;

        var indices = new List<int>(cuantos);

        for (int i = 0; i < Backpack.Items.Count; i++)
        {
            if ((mascara & (1 << i)) == 0)
                continue;

            var cosa = Backpack.Items[i];

            if (cosa.Kind != ItemType.Perk || _datos.Perks.Get(cosa.Id).Tier != tier)
                return;

            indices.Add(i);
        }

        if (indices.Count != cuantos)
            return;

        var clases = Roster.Available();

        if (clases.Count == 0)
            return;

        // ⚠ El pool se resuelve ANTES de consumir. Resolverlo después dejaría el
        // camino en que no hay ningún Oro que le sirva al roster: tres Plata
        // destruidas y nada a cambio.
        List<int>? candidatos = null;

        if (tier == PerkTier.Silver)
        {
            candidatos = PoolDeOro(clases);

            if (candidatos.Count == 0)
                return;
        }

        // De mayor a menor: sacar por el otro lado corre los índices de abajo y
        // se terminaría destruyendo un perk que el jugador no eligió.
        for (int i = indices.Count - 1; i >= 0; i--)
            Backpack.RemoveAt(indices[i]);

        Metrics.PerksUpgraded++;

        if (candidatos is null)
        {
            var plata = _datos.Perks.Roll(PerkTier.Silver, clases, _rng);

            if (plata is not null)
                Backpack.StorePerk(plata.Id);

            return;
        }

        int elegido = candidatos[_rng.Index(candidatos.Count)];

        _oroFabricado++;
        Metrics.GoldPerksObtained++;
        Metrics.GoldFromUpgrades++;
        Backpack.StorePerk(elegido);
    }

    /// <summary>
    /// Le cambia el modo de targeting a un defensor puesto.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es del defensor, no de la clase</b>, y es la única cosa del
    /// juego que lo es. La regla «todo es de la clase, nunca de la unidad» habla
    /// de <b>stats</b> —para que no existan unidades viejas que se quedan atrás— y
    /// esto no es un stat: es la misma memoria por unidad que ya llevan la
    /// concentración y el contador de golpes.</para>
    ///
    /// <para>Y tiene que ser por unidad para que la mecánica valga: dos paladines
    /// de la misma ficha quieren targeting distinto según dónde estén parados, y
    /// esa decisión de colocación es la mitad de lo que el targeting aporta.</para>
    ///
    /// <para>Se puede cambiar en cualquier momento y no cuesta nada. Cobrar por
    /// cambiarlo convertiría una decisión táctica en una decisión económica, que es
    /// otra cosa y ya tiene dónde tomarse.</para>
    /// </remarks>
    private void SetTargeting(int idColocacion, TargetingMode modo)
    {
        if (!System.Enum.IsDefined(modo))
            return;

        foreach (var d in _defensores)
        {
            if (d.PlacementId == idColocacion)
            {
                d.Targeting = modo;
                return;
            }
        }
    }

    /// <summary>
    /// Elige cuál de los dos comportamientos de una clase está activo.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>La ranura sigue siendo una.</b> Esto no permite tener el Oro y la
    /// habilidad del arma a la vez: permite decidir cuál de los dos habla. Tenerlos
    /// juntos multiplicaría las combinaciones a balancear por el catálogo entero de
    /// armas.</para>
    ///
    /// <para><b>Los stats del arma se conservan en los dos casos</b>, y ese es todo
    /// el punto: antes ganaba el arma siempre, así que encontrar una excelente
    /// obligaba a apagar un Único que costó dos jefes y un plano. El jugador
    /// terminaba no equipándola —lo peor que le puede pasar a un drop— para no
    /// perder la build.</para>
    ///
    /// <para>Es de la CLASE y no del defensor, al revés que el targeting: el arma y
    /// los perks viven en la ficha, así que la decisión vive donde viven las cosas
    /// entre las que se decide.</para>
    /// </remarks>
    private void SetBehavior(DefenderClass clase, bool preferirElPerk)
    {
        var ficha = Roster.Find(clase);

        if (ficha is not null)
            ficha.PreferPerkBehavior = preferirElPerk;
    }

    /// <summary>Oro y Único son comportamiento; Bronce y Plata son números.</summary>
    private static bool EsComportamiento(PerkTier tier)
        => tier is PerkTier.Gold or PerkTier.Unique;

    /// <summary>El perk de comportamiento que la clase tiene puesto, si hay.</summary>
    private PerkDefinition? ComportamientoDe(RosterClass ficha)
    {
        foreach (int id in ficha.Perks)
        {
            var p = _datos.Perks.Get(id);

            if (EsComportamiento(p.Tier))
                return p;
        }

        return null;
    }

    /// <summary>
    /// La clase tiene un comportamiento puesto <b>y</b> un arma con habilidad, así
    /// que uno de los dos está mudo.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Existe para que la interfaz lo pueda <b>decir</b>. La exclusión se
    /// mantiene —arma y perk son la misma ranura— pero antes era invisible: un Oro
    /// raro quedaba anulado por equipar un arma y el jugador no se enteraba nunca.</para>
    ///
    /// <para>Ahora además la pantalla tiene que ofrecer <b>elegir</b> cuál habla:
    /// eso es <see cref="SetBehavior"/>, y cuál está ganando lo dice
    /// <see cref="RosterClass.PreferPerkBehavior"/>.</para>
    /// </remarks>
    public bool HayConflictoDeComportamiento(DefenderClass clase)
    {
        var ficha = Roster.Find(clase);

        if (ficha is null || ComportamientoDe(ficha) is null || ficha.Weapon < 0)
            return false;

        return Backpack.WeaponById(ficha.Weapon)?.Ability is not (null or WeaponAbility.None);
    }

    /// <summary>El arma le está tapando el perk: hay conflicto y gana el arma.</summary>
    public bool ArmaTapaElRasgo(DefenderClass clase)
        => HayConflictoDeComportamiento(clase) && Roster.Find(clase)?.PreferPerkBehavior == false;

    /// <summary>
    /// Compra una oferta de la tienda.
    /// </summary>
    /// <remarks>
    /// Lo que va al log es el <b>índice</b> de la oferta, no qué era: el stock lo
    /// regenera el servidor de la seed. Guardar el contenido dejaría que un
    /// cliente modificado reclamara haber comprado algo que no estaba a la venta.
    /// </remarks>
    private void Buy(int indice)
    {
        var stock = Shop.Stock;

        if (indice < 0 || indice >= stock.Count)
            return;

        var oferta = stock[indice];

        if (oferta.Sold || Gold < oferta.Price)
            return;

        // ⚠ Cada categoría se entrega en un lugar distinto, y solo dos de las
        // cuatro ocupan mochila. Reclutar toca el roster y aprender un plano toca
        // el conocimiento de la corrida: exigirles lugar de mochila haría que
        // comprar una clase con la mochila llena rebotara en silencio.
        //
        // ⚠ Y en TODOS los casos se entrega ANTES de descontar. Cobrarle al
        // jugador por algo que no entra es el peor error posible de esta pantalla,
        // y es exactamente el que sale de descontar primero.
        switch (oferta.Kind)
        {
            case ShopKind.Perk:
                if (!Backpack.Store(new StoredItem(ItemType.Perk, oferta.Id)))
                    return;

                Metrics.CountPurchase(_datos.Perks.Get(oferta.Id).Tier, oferta.Price);
                break;

            case ShopKind.Weapon:
                if (!Backpack.Store(new StoredItem(ItemType.Weapon, oferta.Id)))
                    return;

                Metrics.BoughtWeapon++;
                Metrics.GoldSpentWeapons += oferta.Price;
                break;

            case ShopKind.Class:
            {
                var clase = (DefenderClass)oferta.Id;

                // ⚠ Una clase ya reclutada RETIRA la oferta en vez de rebotar en
                // silencio. Rebotar dejaba la oferta viva y comprable, y un bot
                // que reintenta con el reloj congelado no termina nunca: medido,
                // `--balance 24` sin terminar en 30 minutos. No se cobra nada.
                if (Roster.Has(clase))
                {
                    Shop.RetirarClase(clase);
                    return;
                }

                Roster.Hire(clase);

                // Contratar abre Oro nuevo, igual que contratar por elección: sin
                // esto, reclutar en la tienda dejaría a la clase nueva sin
                // comportamiento propio para siempre.
                AmpliarPoolDeOro(clase);

                Metrics.BoughtClass++;
                Metrics.GoldSpentClasses += oferta.Price;
                break;
            }

            default:
            {
                if (oferta.Id < 0 || oferta.Id >= _datos.Blueprints.All.Count)
                    return;

                var plano = _datos.Blueprints.All[oferta.Id];

                if (plano.Initial || _planosAprendidos.Contains(plano.Key))
                    return;

                // ⚠ SOLO PARA ESTA CORRIDA. El plano comprado con la billetera
                // desde el menú principal queda aprendido para siempre en la
                // cuenta; este muere con la corrida. Si persistiera, una corrida
                // afortunada regalaría progresión permanente y el precio de la
                // billetera dejaría de significar nada — y los planos son la única
                // progresión permanente del proyecto.
                _planosAprendidos.Add(plano.Key);

                Metrics.BoughtBlueprint++;
                Metrics.GoldSpentBlueprints += oferta.Price;
                break;
            }
        }

        Gold -= oferta.Price;
        Shop.Sell(indice);
    }

    /// <summary>Los planos que la cuenta tiene aprendidos. Los manda el servidor.</summary>
    public void AprenderPlano(string nombre) => _planosAprendidos.Add(nombre);

    /// <summary>
    /// ¿Esta corrida ya conoce la receta del plano número <paramref name="indice"/>?
    /// </summary>
    /// <remarks>
    /// Cuenta los tres orígenes juntos, que es lo que le importa a la pantalla: el
    /// de <c>initial: true</c>, el comprado con la billetera en el menú principal y
    /// el comprado con oro en la tienda de esta corrida. Adentro de la corrida los
    /// tres se usan igual; la diferencia —cuál sobrevive a la corrida— es cosa de
    /// la cuenta, no de la simulación.
    /// </remarks>
    public bool ConocePlano(int indice)
    {
        if (indice < 0 || indice >= _datos.Blueprints.All.Count)
            return false;

        var plano = _datos.Blueprints.All[indice];

        return plano.Initial || _planosAprendidos.Contains(plano.Key);
    }

    private void Sell(int idColocacion)
    {
        int i = _defensores.FindIndex(d => d.PlacementId == idColocacion);
        if (i < 0) return;

        var stats = _datos.StatsOf(_defensores[i].DefenderClass);
        Gold += Ints.MultiplyPercent(stats.Cost, _datos.Curves.Defenders.DefenderSellPercent);

        _defensores.RemoveAt(i);
    }

    // =========================================================================
    //  Fase 2 — Spawn
    // =========================================================================

    private void FaseSpawn()
    {
        if (_porSpawnear.Count == 0)
            return;

        if (_ticksParaProximoSpawn > 0)
        {
            _ticksParaProximoSpawn--;
            return;
        }

        // ⚠ Los perks del NPC ya vinieron decididos por el armador de la oleada,
        // que los tiró y los PAGÓ del presupuesto. Acá no se tira nada: si se
        // tirara, el servidor tendría que reproducir esa tirada en este mismo
        // punto de la secuencia del RNG, y cualquier cambio de orden de fases la
        // correría.
        var compra = _porSpawnear.Dequeue();
        var b = _datos.Get(compra.TypeId);

        if (b is null)
            return;   // el tipo no está en los datos: no spawnea nada

        // ⚠ El perk de vida se aplica sobre los PUNTOS del bestiario y recién
        // después se escala. Al revés daría lo mismo, pero así el número que se
        // muestra en la pantalla de preparación —"+50% de vida"— sale de la misma
        // cuenta que el HP real, sin una conversión en el medio que se pueda
        // olvidar.
        int hpMaximo = Ints.ToHundredths(Ints.WithPercents(b.Hp, compra.HealthPerk));

        // El daño sale del promedio de min y max, no de una tirada por golpe: una
        // tirada acá gastaría un número del RNG por cada golpe de cada enemigo, y
        // eso hace que agregar un defensor corra toda la secuencia posterior y
        // cambie los drops de la corrida entera.
        //
        // ⚠ Se escala ANTES de promediar. Al revés, un bicho de 3-4 pegaría 3 y no
        // 3,5: sería exactamente el truncamiento que la escala vino a sacar, dejado
        // en pie a un renglón de distancia del arreglo.
        int dano = System.Math.Max(
            Ints.ToHundredths(_datos.Combat.Enemy.MinimumDamage),
            Ints.ToHundredths(b.MinDamage + b.MaxDamage) / _datos.Combat.Enemy.DamageAverageDivisor);

        _enemigos.Add(new Enemy
        {
            SpawnId = _proximoIdSpawn++,
            TypeId = compra.TypeId,
            IsBoss = compra.IsBoss,
            MaxHp = hpMaximo,
            Hp = hpMaximo,
            Speed = b.Speed,
            Damage = dano,
            CadenceTicks = b.CadenceTicks,
            Gold = b.Gold,
            DistanceTravelled = 0,
            ExtraHealthPerk = compra.HealthPerk,
            ExtraSpeedPerk = compra.SpeedPerk,

            // Los dos rasgos también los decidió el armador y también los pagó del
            // presupuesto. Acá solo se copian: tirarlos en esta fase movería el
            // punto de consumo del RNG.
            Impulsive = compra.Impulsive,
            Leader = compra.Leader,
        });

        // ⚠ EL RITMO DE ENTRADA NO ES CONSTANTE: lo pone la formación de la
        // oleada, que conserva el promedio pero no la separación.
        //
        // No es una palanca de dificultad encubierta —la oleada dura lo mismo—
        // pero cambia el valor RELATIVO de casi todo el catálogo: en ráfaga
        // splash, cadena y quemadura valen el doble y el single target sufre; en
        // uniforme toman valor Concentración y el targeting «más fuerte».
        //
        // El índice es el del NPC dentro de la oleada, así que la forma se lee
        // igual sin importar cuántos se hayan comprado.
        int indice = _spawneadosEnOleada++;

        _ticksParaProximoSpawn = System.Math.Max(1, Ints.MultiplyPercent(
            _intervaloSpawn, _datos.Curves.Wave.Formations.PercentAt(_formacionOleada, indice)));
    }

    // =========================================================================
    //  Fase 3 — Estados
    // =========================================================================

    /// <summary>
    /// Vence lo que se acabó, cobra los daños por tiempo y suelta los ecos.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Va ANTES de mover, y eso importa.</b> Un enemigo al que el
    /// veneno mata en este tick no camina un paso más, y un slow que vence en
    /// este tick ya no lo frena. Ponerlo después de mover daría un tick de
    /// gracia a cada estado, que suena inocente hasta que se multiplica por cien
    /// oleadas y el ranking deja de cerrar.</para>
    ///
    /// <para>Un DoT que mata acá deja al enemigo con <c>Alive == false</c>, y la
    /// fase 8 le cobra el oro y le tira el drop como a cualquier otro muerto.</para>
    /// </remarks>
    private void FaseEstados()
    {
        foreach (var e in _enemigos)
        {
            if (e.SlowPercent > 0 && Tick >= e.SlowExpiryTick)
                e.SlowPercent = 0;

            if (e.VulnerablePercent > 0 && Tick >= e.VulnerableExpiryTick)
                e.VulnerablePercent = 0;

            if (e.WeakenPercent > 0 && Tick >= e.WeakenExpiryTick)
                e.WeakenPercent = 0;

            if (e.MarkPercent > 0 && Tick >= e.MarkExpiryTick)
                e.MarkPercent = 0;

            if (e.ExtraGoldPercent > 0 && Tick >= e.ExtraGoldExpiryTick)
                e.ExtraGoldPercent = 0;

            // El acelerón de enfurecer vence; el de impulsivo no —lo deja con
            // expiración en int.MaxValue—, y por eso son el mismo campo: son el
            // mismo efecto con distinta duración.
            if (e.TraitSpeedPercent > 0 && Tick >= e.TraitSpeedExpiryTick)
                e.TraitSpeedPercent = 0;

            if (!e.Alive)
                continue;

            DispararRasgoDeVelocidad(e);

            // Los tres DoT cobran en orden fijo. Es daño SECUNDARIO: no dispara
            // comportamientos, no aplica estados y no crea más secundarios.
            for (int i = 0; i < e.Dots.Length; i++)
            {
                int sale = e.Dots[i].Tick(Tick);

                if (sale > 0)
                    e.Hp -= sale;
            }
        }

        RecalcularAurasDeLider();

        // Las auras de cadencia también vencen acá: son estado del defensor, pero
        // se apagan por tiempo igual que los del enemigo.
        foreach (var d in _defensores)
        {
            if (d.CadenceBuffPercent > 0 && Tick >= d.CadenceBuffExpiryTick)
                d.CadenceBuffPercent = 0;
        }

        ResolverEcos();
    }

    /// <summary>
    /// Enfurecer y Impulsivo: el acelerón del tramo final, una sola vez.
    /// </summary>
    /// <remarks>
    /// <para>Son la misma mecánica con dos disparadores y dos duraciones, así que
    /// van en la misma función y en el mismo campo del enemigo. <b>Ninguno da daño
    /// extra</b>, ni cura, ni vuelve inmune, ni destruye defensores: un enemigo
    /// que además pega más fuerte al enfurecer convierte una filtración en una
    /// derrota instantánea que el jugador no tenía cómo prever.</para>
    ///
    /// <para><b>Enfurecer</b> lo tienen todos los jefes: al bajar del 35% aceleran
    /// un 15% durante 90 ticks. El jefe final acelera menos —es el final de cien
    /// oleadas, y ahí una aceleración grande es una derrota que no se ve venir.</para>
    ///
    /// <para><b>Impulsivo</b> es un rasgo ocasional de la tropa: bajo el 30%
    /// acelera un 20% y no se le vence. Tiene varias respuestas —Oportunista,
    /// concentrar fuego, slow, burst, defensa cerca de la Torre— así que no es un
    /// enemigo «lock-and-key», que es lo que Defender's Quest recomienda
    /// evitar.</para>
    ///
    /// <para>⚠ <b>Una sola vez por bicho</b>, y por eso la bandera. Sin ella, un
    /// enemigo que sube y baja del umbral —cura no hay, pero el HP máximo sí puede
    /// mirarse mal— refrescaría el acelerón para siempre.</para>
    /// </remarks>
    private void DispararRasgoDeVelocidad(Enemy e)
    {
        if (e.SpeedTraitFired || e.MaxHp <= 0)
            return;

        var t = _datos.Combat.Traits;

        if (e.IsBoss)
        {
            if (e.Hp * 100 / e.MaxHp > t.EnrageHpPercent)
                return;

            e.SpeedTraitFired = true;
            e.TraitSpeedPercent = EsJefeFinal(e)
                ? t.FinalBossEnrageSpeedPercent
                : t.EnrageSpeedPercent;

            e.TraitSpeedExpiryTick = Tick + t.EnrageTicks;
            return;
        }

        if (!e.Impulsive || e.Hp * 100 / e.MaxHp > t.ImpulsiveHpPercent)
            return;

        e.SpeedTraitFired = true;
        e.TraitSpeedPercent = t.ImpulsiveSpeedPercent;

        // Impulsivo no vence: el bicho ya está herido y le queda poca ruta. Poner
        // una duración sería un número más que balancear para un efecto que dura,
        // en la práctica, lo que tarda en morir.
        e.TraitSpeedExpiryTick = int.MaxValue;
    }

    /// <summary>
    /// Reparte el aura de los Líderes vivos.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Se recalcula entera cada tick, desde cero.</b> Es más trabajo que
    /// aplicarla al aparecer y vencerla por tiempo, y es lo correcto: <b>el aura
    /// muere con el líder</b>, y eso es exactamente lo que lo vuelve un objetivo
    /// prioritario sin agregar una sola resistencia ni inmunidad. Con expiración
    /// por tiempo, matarlo no se notaría hasta segundos después.</para>
    ///
    /// <para>⚠ <b>No apila</b>, igual que todos los estados del juego: gana la
    /// potencia más fuerte. Sin la regla, tres líderes juntos son un +30% que
    /// ningún número del catálogo anticipa.</para>
    ///
    /// <para>El radio se mide sobre la <b>ruta</b> y no sobre el mapa, que es como
    /// se mide toda la cercanía entre enemigos en este juego: la posición de un
    /// enemigo es un solo número.</para>
    /// </remarks>
    private void RecalcularAurasDeLider()
    {
        // Barato y en orden de spawn. La lista se recorre dos veces solo si hay al
        // menos un líder vivo, que es la excepción y no la regla.
        bool hayLider = false;

        foreach (var e in _enemigos)
        {
            e.LeaderAuraPercent = 0;
            hayLider |= e.Leader && e.Alive;
        }

        if (!hayLider)
            return;

        var t = _datos.Combat.Traits;

        foreach (var lider in _enemigos)
        {
            if (!lider.Leader || !lider.Alive)
                continue;

            foreach (var otro in _enemigos)
            {
                if (!otro.Alive || ReferenceEquals(otro, lider))
                    continue;

                long d = System.Math.Abs(otro.DistanceTravelled - lider.DistanceTravelled);

                // No apila: se conserva la potencia más fuerte.
                if (d <= t.LeaderRadiusThousandths && otro.LeaderAuraPercent < t.LeaderSpeedPercent)
                    otro.LeaderAuraPercent = t.LeaderSpeedPercent;
            }
        }
    }

    /// <summary>
    /// Suelta los ecos que ya cumplieron su espera.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>En orden de creación.</b> Recorrer al revés para poder borrar
    /// sobre la marcha es el atajo obvio y es exactamente el bug que rompe la
    /// revalidación: dos ecos que caen en el mismo tick sobre el mismo grupo
    /// resolverían al revés y matarían enemigos distintos.</para>
    ///
    /// <para>El eco guarda la <b>distancia recorrida</b> además del id de spawn,
    /// porque el objetivo original puede estar muerto y borrado de la lista
    /// cuando llega el momento. Ahí busca un vecino de ruta.</para>
    /// </remarks>
    private void ResolverEcos()
    {
        if (_ecos.Count == 0)
            return;

        foreach (var eco in _ecos)
        {
            if (Tick < eco.Tick)
                continue;

            var objetivo = PorIdDeSpawn(eco.SpawnId);

            objetivo ??= MasCercanoEnRuta(eco.Distance, eco.RadiusThousandths);

            if (objetivo is not null)
                GolpeSecundario(objetivo, eco.Damage);
        }

        _ecos.RemoveAll(e => Tick >= e.Tick);
    }

    private Enemy? PorIdDeSpawn(int id)
    {
        foreach (var e in _enemigos)
        {
            if (e.SpawnId == id)
                return e.Alive ? e : null;
        }

        return null;
    }

    /// <summary>El primero vivo dentro del radio de ruta, en orden de spawn.</summary>
    private Enemy? MasCercanoEnRuta(long distancia, int radio)
    {
        foreach (var e in _enemigos)
        {
            if (!e.Alive)
                continue;

            if (System.Math.Abs(e.DistanceTravelled - distancia) <= radio)
                return e;
        }

        return null;
    }

    // =========================================================================
    //  Fase 4 — Mover
    // =========================================================================

    private void FaseMover()
    {
        foreach (var e in _enemigos)
        {
            if (!e.Alive || e.AttackingTower)
                continue;

            // ⚠ Raíces frena de verdad, no ralentiza. Es el único control duro
            // del juego y por eso viene con período refractario y tope de veces
            // por enemigo: sin las dos cosas, tres druidas alcanzan para que nada
            // camine nunca más.
            if (e.Rooted(Tick))
                continue;

            e.DistanceTravelled += e.EffectiveSpeed;
        }
    }

    // =========================================================================
    //  Fase 5 — Llegadas
    // =========================================================================

    private void FaseLlegadas()
    {
        foreach (var e in _enemigos)
        {
            if (!e.Alive || e.AttackingTower)
                continue;

            if (e.DistanceTravelled >= _ruta.Length)
            {
                e.DistanceTravelled = _ruta.Length;
                e.AttackingTower = true;

                // Pega recién en su próximo turno de cadencia, no al llegar: si
                // no, un enemigo rápido descarga un golpe gratis por el solo
                // hecho de haber llegado en este tick.
                e.AttackCooldown = e.CadenceTicks;
            }
        }
    }

    // =========================================================================
    //  Fase 6 — Combate de la Torre
    // =========================================================================

    private void FaseCombateTorre()
    {
        // La Torre devuelve el golpe a UNO por turno: el más viejo de los que la
        // están mordiendo. El orden de `_enemigos` es el de spawn, así que
        // "el primero" siempre significa lo mismo en las dos mitades.
        Enemy? mordiendo = null;

        foreach (var e in _enemigos)
        {
            if (!e.Alive || !e.AttackingTower)
                continue;

            mordiendo ??= e;

            if (e.AttackCooldown > 0)
            {
                e.AttackCooldown--;
                continue;
            }

            // ⚠ Debilidad se cobra ACÁ y no al golpear al enemigo: el perk no
            // sube el daño de nadie, baja el castigo por filtrar. Es una utilidad
            // distinta del slow, y es la que le faltaba al catálogo — todo lo
            // demás era alguna forma de pegar más fuerte.
            int golpe = Ints.WithPercents(e.Damage, -e.WeakenPercent);

            Tower.Receive(golpe);
            Metrics.TowerDamageTaken += golpe;

            e.AttackCooldown = e.CadenceTicks;
        }

        // ⚠ El cooldown de la Torre baja SOLO cuando hay a quién pegarle.
        //
        // Si bajara siempre, llegaría a cero durante la caminata y el primero en
        // tocarla comería un golpe gratis por haber llegado en el tick correcto.
        // Es la misma razón por la que el enemigo tampoco pega al llegar.
        if (mordiendo is null)
        {
            Tower.AttackCooldown = _datos.Curves.Tower.CadenceTicks;
            return;
        }

        if (Tower.AttackCooldown > 0)
        {
            Tower.AttackCooldown--;
            return;
        }

        mordiendo.Hp -= Ints.ToHundredths(_datos.Curves.Tower.Damage);
        Tower.AttackCooldown = _datos.Curves.Tower.CadenceTicks;
    }

    // =========================================================================
    //  Fase 7 — Defensores
    // =========================================================================

    private void FaseDefensores()
    {
        // Se reconstruye una vez por tick, antes de que dispare nadie: si se
        // reconstruyera por defensor, el primero en disparar vería posiciones
        // distintas que el último y el resultado dependería del orden de la
        // lista de defensores.
        _grilla.Rebuild(_enemigos, _ruta);

        foreach (var d in _defensores)
        {
            if (d.RemainingCooldown > 0)
            {
                d.RemainingCooldown--;
                continue;
            }

            // base + nivel + perks + equipo, recalculado cada vez. Nada de esto
            // vive en el defensor: un cazador puesto en la oleada 3 y otro puesto
            // en la 40 pegan exactamente igual.
            var stats = Roster.StatsOf(d.DefenderClass, Backpack);
            var objetivo = ElegirObjetivo(d, stats.RangeMilli);

            if (objetivo is null)
            {
                // Sin nadie a tiro. Preparación convierte esa espera en ventaja:
                // el próximo golpe sale cargado. Una sola carga guardada, o un
                // defensor en una esquina muerta del mapa acumularía un golpe
                // infinito para el día que algo le pase cerca.
                d.IdleTicks++;

                if (stats.Behavior == PerkEffect.Charge &&
                    d.IdleTicks >= _datos.Combat.Behaviors.ChargeIdleTicks)
                {
                    d.Charged = true;
                }

                continue;
            }

            d.IdleTicks = 0;

            // El contador de golpes del defensor: sube ANTES de resolver el
            // disparo, así "cada 7.º golpe" cuenta este. Lo usan el crítico,
            // Quebranto, Raíces, Golpe Sísmico, Inspiración y Eco Arcano.
            d.HitCounter++;

            // La concentración se actualiza antes de calcular el daño: el golpe
            // que suma el stack ya lo cobra.
            ActualizarFoco(d, objetivo);

            // Daño instantáneo. El proyectil que se ve volar es SOLO render: si
            // el daño esperara a que el sprite llegue, el resultado dependería
            // de los FPS.
            int dano = stats.Damage;

            // ⚠ Dos pasadas de escalado, y el ORDEN ESTÁ FIJO: primero el
            // comportamiento del perk, después la habilidad del arma. En la
            // práctica nunca están los dos —el arma tapa al perk— pero el orden
            // igual se congela: si algún día conviven, el resultado no puede
            // depender de en qué rama del switch cayó cada uno.
            dano = AdjustDamageByBehavior(stats, d, objetivo, dano);
            dano = AdjustDamageByAbility(stats, d, objetivo, dano);

            // El golpe DIRECTO: el único que cobra vulnerabilidad y marca, y el
            // único que puede disparar un comportamiento.
            int overkill = GolpeDirecto(d, objetivo, dano);

            ApplyBehavior(stats, d, objetivo, dano, overkill);
            ApplyWeaponAbility(stats, d, objetivo, dano);

            d.LastHitTick = Tick;
            d.RemainingCooldown = CadenciaConAura(d, stats);
        }
    }

    /// <summary>
    /// La cadencia real del defensor, contando el aura de un aliado.
    /// </summary>
    /// <remarks>
    /// ⚠ El aura <b>no</b> entra en <see cref="Roster.StatsOf"/> y no puede
    /// entrar: los stats son de la CLASE y el aura es de ESTA unidad, que depende
    /// de dónde está parada. Meterla ahí haría que dos bardos idénticos en
    /// esquinas opuestas del mapa devolvieran stats distintos, y toda la promesa
    /// de "el defensor no guarda nada propio" se caería.
    ///
    /// ⚠ La cadencia va al REVÉS: menos ticks es más rápido, así que un +10%
    /// DIVIDE el intervalo.
    /// </remarks>
    private int CadenciaConAura(Defender d, EffectiveStats stats)
    {
        if (d.CadenceBuffPercent <= 0 || Tick >= d.CadenceBuffExpiryTick)
            return stats.CadenceTicks;

        return System.Math.Max(
            1, stats.CadenceTicks * 100 / System.Math.Max(1, 100 + d.CadenceBuffPercent));
    }

    /// <summary>
    /// Lleva la cuenta de golpes seguidos al mismo objetivo.
    /// </summary>
    /// <remarks>
    /// Se pierde al cambiar de objetivo o tras pasar demasiados ticks sin pegar.
    /// Los golpes secundarios no cuentan: si contaran, un mago con splash sumaría
    /// stacks sobre bichos que ni siquiera está apuntando.
    /// </remarks>
    private void ActualizarFoco(Defender d, Enemy objetivo)
    {
        var b = _datos.Combat.Behaviors;

        bool sigue = d.FocusTargetSpawnId == objetivo.SpawnId
                     && d.LastHitTick != int.MinValue
                     && Tick - d.LastHitTick <= b.FocusResetTicks;

        d.FocusStacks = sigue
            ? System.Math.Min(b.FocusMaxStacks, d.FocusStacks + 1)
            : 0;

        d.FocusTargetSpawnId = objetivo.SpawnId;
    }

    // =========================================================================
    //  El golpe: directo contra secundario
    // =========================================================================

    /// <summary>
    /// Un golpe <b>directo</b>. Devuelve el overkill, o 0 si no mató.
    /// </summary>
    /// <remarks>
    /// <para>Es el único daño que cobra vulnerabilidad y marca de caza, y el
    /// único que puede disparar un comportamiento. Todo lo que sale de él
    /// —splash, cadena, rebote, eco, DoT, overkill— pasa por
    /// <see cref="GolpeSecundario"/> y solo resta vida.</para>
    ///
    /// <para>El overkill sale de acá y no de una segunda cuenta porque es el
    /// único momento en que se sabe: una vez que la fase 8 saca al muerto de la
    /// lista, cuánto le sobró al golpe ya no existe en ningún lado.</para>
    /// </remarks>
    private int GolpeDirecto(Defender defensor, Enemy objetivo, int dano)
    {
        int real = Ints.WithPercents(dano, BonusRecibido(objetivo, defensor.DefenderClass));

        bool vivíaAntes = objetivo.Alive;

        objetivo.Hp -= real;

        int overkill = objetivo.Hp < 0 ? -objetivo.Hp : 0;

        // La muerte se le atribuye al golpe que la causó, que es el único momento
        // en que se sabe: la fase 8 saca al muerto de la lista sin registrar quién
        // fue.
        if (vivíaAntes && !objetivo.Alive)
            Metrics.KillsByClass[(int)defensor.DefenderClass]++;

        // ⚠ Métrica, no simulación: el daño que EFECTIVAMENTE hizo falta va a la
        // clase, y lo que sobró va aparte. Sumar el golpe entero mezclaría las dos
        // cosas y «Cazador hizo el 40% del daño» diría también cuánto desperdició.
        Metrics.DamageByClass[(int)defensor.DefenderClass] += real - overkill;
        Metrics.OverkillDamage += overkill;

        return overkill;
    }

    /// <summary>
    /// Daño <b>secundario</b>: resta vida y nada más.
    /// </summary>
    /// <remarks>
    /// ⚠ No aplica estados, no sube contadores, no dispara comportamientos y no
    /// crea más secundarios. Es la primera regla de Estados_v2 y la que hace
    /// acotable a todo el sistema: sin ella, Cadena crea Cadena y el eco dispara
    /// otro eco.
    /// </remarks>
    private void GolpeSecundario(Enemy objetivo, int dano)
    {
        if (dano <= 0)
            return;

        objetivo.Hp -= dano;

        // ⚠ Métrica, no simulación. Si esto se acerca al daño directo total, los
        // comportamientos dejaron de ser condimento y pasaron a ser la fuente de
        // daño — que es el síntoma de que un tope está mal puesto, y es
        // exactamente lo que le pasaba a Cadena antes del techo.
        Metrics.SecondaryDamage += dano;
    }

    /// <summary>
    /// Cuánto daño DIRECTO extra recibe este enemigo, en puntos porcentuales.
    /// </summary>
    /// <remarks>
    /// Vulnerabilidad la cobra cualquiera; la marca de caza <b>no la cobra la
    /// clase que marcó</b>, o sería un bonus propio disfrazado de soporte y dos
    /// cazadores serían mejor que un cazador y otra cosa.
    /// </remarks>
    private static int BonusRecibido(Enemy e, DefenderClass clase)
    {
        int extra = e.VulnerablePercent;

        if (e.MarkPercent > 0 && e.MarkOwner != clase)
            extra += e.MarkPercent;

        return extra;
    }

    // =========================================================================
    //  Los comportamientos de Oro y Único
    // =========================================================================

    /// <summary>
    /// Los comportamientos que <b>escalan el golpe</b>, antes de aplicarlo.
    /// </summary>
    /// <remarks>
    /// Van separados de los que agregan efectos por la misma razón que las
    /// habilidades de arma: lo que sale del golpe —splash, DoT, eco— tiene que
    /// calcularse sobre el daño <b>ya escalado</b>, o un asesino rematando haría
    /// el bonus solo al objetivo principal y no al sangrado.
    /// </remarks>
    private int AdjustDamageByBehavior(
        EffectiveStats stats, Defender defensor, Enemy objetivo, int dano)
    {
        var b = _datos.Combat.Behaviors;

        // Preparación gasta la carga en este golpe, y va primero para que lo demás
        // escale sobre el golpe cargado.
        if (stats.Behavior == PerkEffect.Charge && defensor.Charged)
        {
            defensor.Charged = false;
            dano = Ints.WithPercents(dano, stats.BehaviorMagnitude);
        }

        return stats.Behavior switch
        {
            // Concentración: +magnitud por cada golpe seguido al mismo objetivo.
            PerkEffect.Focus or PerkEffect.Perforation
                => Ints.WithPercents(dano, defensor.FocusStacks * stats.BehaviorMagnitude),

            // Oportunista remata lo herido; Primer Impacto abre lo entero. Son el
            // mismo mecanismo con el umbral dado vuelta.
            PerkEffect.Opportunist when PorDebajoDe(objetivo, b.OpportunistThresholdPercent)
                => Ints.WithPercents(dano, stats.BehaviorMagnitude),

            PerkEffect.Apocalypse when PorDebajoDe(objetivo, b.OpportunistThresholdPercent)
                => Ints.WithPercents(dano, stats.BehaviorMagnitudeB),

            PerkEffect.FirstStrike when PorEncimaDe(objetivo, b.FirstStrikeThresholdPercent)
                => Ints.WithPercents(dano, stats.BehaviorMagnitude),

            // Guardia del Umbral vale 0% casi siempre y muchísimo en la única
            // situación que importa: el bicho ya llegó y está mordiendo la Torre.
            PerkEffect.ThresholdGuard or PerkEffect.WrathOfGod when objetivo.AttackingTower
                => Ints.WithPercents(dano, stats.BehaviorMagnitude),

            _ => dano,
        };
    }

    private static bool PorDebajoDe(Enemy e, int porcentaje)
        => e.MaxHp > 0 && e.Hp * 100 / e.MaxHp <= porcentaje;

    private static bool PorEncimaDe(Enemy e, int porcentaje)
        => e.MaxHp > 0 && e.Hp * 100 / e.MaxHp > porcentaje;

    /// <summary>
    /// El efecto del perk de Oro o Único que tenga equipado la clase.
    /// </summary>
    /// <remarks>
    /// <para>Solo uno se aplica. Antes la regla era "gana el de tier más alto"
    /// porque se podían equipar varios; desde Drops_v2 la mochila directamente no
    /// deja poner un segundo comportamiento, así que la lista de acá tiene
    /// exactamente un caso vivo por defensor.</para>
    ///
    /// <para>⚠ Todo lo que sale de acá es <b>secundario</b>: no vuelve a entrar
    /// por esta función, no aplica estados salvo donde el perk lo dice
    /// explícitamente, y no sube contadores.</para>
    /// </remarks>
    /// <param name="dano">El golpe ya escalado, en centésimas de punto.</param>
    /// <param name="overkill">Lo que le sobró al golpe si mató. 0 si no mató.</param>
    private void ApplyBehavior(
        EffectiveStats stats, Defender defensor, Enemy objetivo, int dano, int overkill)
    {
        var c = _datos.Combat;
        var b = c.Behaviors;

        switch (stats.Behavior)
        {
            case PerkEffect.None:
                return;

            // --- Daño y limpieza ---------------------------------------------

            case PerkEffect.Ricochet:
                Rebotar(objetivo, dano, stats.BehaviorMagnitude, b.RicochetBounces, b.RicochetWindowThousandths);
                return;

            case PerkEffect.Chain:
                Encadenar(objetivo, dano, stats.BehaviorMagnitude, stats.BehaviorMagnitudeB, b.ChainMaxJumps, b.ChainWindowThousandths);
                return;

            case PerkEffect.Splash:
                Salpicar(objetivo, dano, stats.BehaviorMagnitude, b.SplashMaxSecondaries, b.SplashRadiusThousandths, slowPotencia: 0, slowTicks: 0);
                return;

            case PerkEffect.CriticalHit:
                if (defensor.HitCounter % b.HitsPerCritical == 0)
                    GolpeSecundario(objetivo, Ints.MultiplyPercent(dano, stats.BehaviorMagnitude));

                return;

            case PerkEffect.Overpenetration:
                Sobrepenetrar(objetivo, dano, overkill, stats.BehaviorMagnitude, b.OverpenRadiusThousandths);
                return;

            case PerkEffect.ArcaneEcho:
                if (defensor.HitCounter % b.EchoEveryHits == 0)
                    EncolarEco(objetivo, Ints.MultiplyPercent(dano, stats.BehaviorMagnitude));

                return;

            // --- Desgaste -----------------------------------------------------

            case PerkEffect.Poison:
                AplicarDot(objetivo, DotKind.Poison, Ints.MultiplyPercent(dano, stats.BehaviorMagnitude));
                return;

            case PerkEffect.Bleed:
                AplicarDot(objetivo, DotKind.Bleed, Ints.MultiplyPercent(dano, stats.BehaviorMagnitude));
                return;

            case PerkEffect.Burn:
                Quemar(objetivo, dano, stats.BehaviorMagnitude, stats.BehaviorMagnitudeB);
                return;

            // --- Control y soporte --------------------------------------------

            case PerkEffect.Slow:
                Ralentizar(objetivo, stats.BehaviorMagnitude, c.States.SlowDurationTicks);
                return;

            case PerkEffect.Weaken:
                Debilitar(objetivo, stats.BehaviorMagnitude);
                return;

            case PerkEffect.HuntMark:
                Marcar(objetivo, defensor.DefenderClass, stats.BehaviorMagnitude);
                return;

            case PerkEffect.Sunder:
                if (defensor.HitCounter % b.SunderEveryHits == 0)
                    Vulnerar(objetivo, stats.BehaviorMagnitude);

                return;

            case PerkEffect.Root:
                if (defensor.HitCounter % b.RootEveryHits == 0)
                    Enraizar(objetivo, c.States.RootTicks);

                return;

            case PerkEffect.Seismic:
                if (defensor.HitCounter % b.SeismicEveryHits == 0)
                    Empujar(objetivo, stats.BehaviorMagnitude, c.States.PushMaxPerEnemy);

                return;

            case PerkEffect.Inspire:
                if (defensor.HitCounter % b.InspireEveryHits == 0)
                    Inspirar(defensor, stats.BehaviorMagnitude);

                return;

            case PerkEffect.Loot:
                MarcarBotin(objetivo, stats.BehaviorMagnitude, c.States.LootMaxGoldPerEnemy, c.States.LootMaxGoldPerWave);
                return;

            // Estos ya escalaron el golpe en AdjustDamageByBehavior y no hacen
            // nada más. Están enumerados y no en el `default` a propósito: así
            // agregar un comportamiento nuevo obliga a decidir en qué lado va.
            case PerkEffect.Focus:
            case PerkEffect.Opportunist:
            case PerkEffect.ThresholdGuard:
            case PerkEffect.FirstStrike:
            case PerkEffect.Charge:
                return;

            // =================================================================
            //  Los Únicos: los dos Oro de su receta, al valor pleno de los dos
            // =================================================================

            // Onda Expansiva + Escarcha.
            case PerkEffect.FrostWave:
                Salpicar(objetivo, dano, stats.BehaviorMagnitude, c.Uniques.FrostWaveMaxSecondaries, b.SplashRadiusThousandths, stats.BehaviorMagnitudeB, c.Uniques.FrostWaveSlowTicks);
                return;

            // Concentración (ya escaló el golpe) + Sobrepenetración.
            case PerkEffect.Perforation:
                Sobrepenetrar(objetivo, dano, overkill, stats.BehaviorMagnitudeB, c.Uniques.PerforationRadiusThousandths);
                return;

            // Guardia del Umbral (ya escaló el golpe) + Quebranto.
            case PerkEffect.WrathOfGod:
                if (defensor.HitCounter % b.SunderEveryHits == 0)
                    Vulnerar(objetivo, stats.BehaviorMagnitudeB);

                return;

            // Hemorragia + Oportunista (ya escaló el golpe).
            case PerkEffect.Apocalypse:
                AplicarDot(objetivo, DotKind.Bleed, Ints.MultiplyPercent(dano, stats.BehaviorMagnitude));
                return;

            // Raíces + Tormenta de Fuego.
            case PerkEffect.TerribleHunger:
                AplicarDot(objetivo, DotKind.Burn, Ints.MultiplyPercent(dano, stats.BehaviorMagnitude));

                if (defensor.HitCounter % b.RootEveryHits == 0)
                    Enraizar(objetivo, c.Uniques.TerribleHungerRootTicks);

                return;

            // Inspiración + Quebranto, las dos en el mismo golpe periódico.
            case PerkEffect.DivineStorm:
                if (defensor.HitCounter % b.InspireEveryHits == 0)
                {
                    Inspirar(defensor, stats.BehaviorMagnitude);
                    Vulnerar(objetivo, stats.BehaviorMagnitudeB);
                }

                return;

            // Inspiración (periódica) + Debilidad (en cada golpe).
            case PerkEffect.Blindness:
                if (defensor.HitCounter % b.InspireEveryHits == 0)
                    Inspirar(defensor, stats.BehaviorMagnitude);

                Debilitar(objetivo, stats.BehaviorMagnitudeB);
                return;

            // Golpe Sísmico + Quebranto, las dos en el mismo golpe periódico.
            case PerkEffect.Strength:
                if (defensor.HitCounter % b.SeismicEveryHits == 0)
                {
                    Empujar(objetivo, stats.BehaviorMagnitude, c.Uniques.StrengthMaxPushPerEnemy);
                    Vulnerar(objetivo, stats.BehaviorMagnitudeB);
                }

                return;

            // Saqueo + Sobrepenetración, los dos con topes propios más chicos.
            case PerkEffect.Invisibility:
                MarcarBotin(objetivo, stats.BehaviorMagnitude, c.Uniques.InvisibilityMaxGoldPerEnemy, c.Uniques.InvisibilityMaxGoldPerWave);
                Sobrepenetrar(objetivo, dano, overkill, stats.BehaviorMagnitudeB, b.OverpenRadiusThousandths);
                return;
        }
    }

    // =========================================================================
    //  Los estados, uno por uno
    // =========================================================================
    //
    //  Todos comparten la misma forma: recortar la potencia si el objetivo es un
    //  jefe, quedarse con la más fuerte de las dos y refrescar la duración.
    //  Escribirlos por separado y no con un helper genérico es a propósito: son
    //  propiedades y no campos, así que un helper con `ref` no compila, y la
    //  alternativa —pasar delegados— haría ilegible la parte más delicada del
    //  tick a cambio de ahorrar quince líneas.

    /// <summary>El jefe de la oleada 100. Es un escalón aparte de los de área.</summary>
    private bool EsJefeFinal(Enemy e) => e.IsBoss && Wave >= _datos.Curves.RunCurve.WavesPerRun;

    /// <summary>
    /// Recorta la potencia de un estado según contra qué se aplica.
    /// </summary>
    /// <remarks>
    /// ⚠ Es lo que hace que los jefes sigan siendo jefes. El control <b>sigue
    /// sirviendo</b> —vale la mitad, no cero—: sin recorte, un druida y un mago
    /// dejan al jefe de área clavado y el combate deja de existir; con inmunidad
    /// total, los perks de control son basura en las ocho peleas que importan.
    /// </remarks>
    private int PotenciaContra(Enemy e, int potencia)
    {
        if (!e.IsBoss)
            return potencia;

        var jefe = _datos.Combat.Boss;

        return Ints.MultiplyPercent(
            potencia, EsJefeFinal(e) ? jefe.FinalBossPowerPercent : jefe.PowerPercent);
    }

    /// <summary>
    /// Aplica una ralentización TEMPORAL.
    /// </summary>
    /// <remarks>
    /// <para>Tres reglas: <b>no apila</b> —prevalece la potencia más fuerte—,
    /// <b>refresca</b> la duración en cada aplicación, y tiene <b>techo</b>. Antes
    /// era permanente, apilaba hasta -80% y dos fuentes de Escarcha dejaban la
    /// corrida en cámara lenta para siempre.</para>
    ///
    /// <para>El refresco tampoco es infinito: la duración acumulada no puede pasar
    /// de <c>slow.maxDurationTicks</c> contados desde ahora, o pegarle sin parar a
    /// un tanque lo dejaría lento el resto de la corrida.</para>
    /// </remarks>
    private void Ralentizar(Enemy enemigo, int magnitud, int ticks)
    {
        int potencia = System.Math.Min(
            PotenciaContra(enemigo, magnitud), _datos.Combat.States.SlowCapPercent);

        if (potencia <= 0 || ticks <= 0)
            return;

        if (potencia > enemigo.SlowPercent)
            enemigo.SlowPercent = potencia;

        int techo = Tick + _datos.Combat.States.SlowMaxDurationTicks;
        int vence = System.Math.Max(enemigo.SlowExpiryTick, Tick + ticks);

        enemigo.SlowExpiryTick = System.Math.Min(vence, techo);
    }

    /// <summary>Vulnerable: recibe más daño DIRECTO de todas las clases.</summary>
    private void Vulnerar(Enemy enemigo, int magnitud)
    {
        int potencia = PotenciaContra(enemigo, magnitud);

        if (potencia <= 0)
            return;

        if (potencia > enemigo.VulnerablePercent)
            enemigo.VulnerablePercent = potencia;

        enemigo.VulnerableExpiryTick = Tick + _datos.Combat.States.VulnerableTicks;
    }

    /// <summary>Debilidad: le hace menos daño a la Torre si llega.</summary>
    private void Debilitar(Enemy enemigo, int magnitud)
    {
        int potencia = PotenciaContra(enemigo, magnitud);

        if (potencia <= 0)
            return;

        if (potencia > enemigo.WeakenPercent)
            enemigo.WeakenPercent = potencia;

        enemigo.WeakenExpiryTick = Tick + _datos.Combat.States.WeakenTicks;
    }

    /// <summary>Marca de caza: las OTRAS clases le pegan más fuerte.</summary>
    private void Marcar(Enemy enemigo, DefenderClass clase, int magnitud)
    {
        int potencia = PotenciaContra(enemigo, magnitud);

        if (potencia <= 0)
            return;

        // ⚠ El dueño se pisa junto con la potencia. Si se conservara el viejo, un
        // cazador podría marcar y otro cobrar sobre su propia marca.
        if (potencia >= enemigo.MarkPercent)
        {
            enemigo.MarkPercent = potencia;
            enemigo.MarkOwner = clase;
        }

        enemigo.MarkExpiryTick = Tick + _datos.Combat.States.HuntMarkTicks;
    }

    /// <summary>
    /// Inmoviliza, con período refractario y tope por enemigo.
    /// </summary>
    /// <remarks>
    /// ⚠ Es el <b>único control duro</b> del juego, y por eso viene con las tres
    /// trabas juntas: dura poco, deja inmunidad al salir y no se puede repetir
    /// más de N veces sobre el mismo bicho. Sin ellas, tres druidas alcanzan para
    /// que nada camine nunca más y la dificultad deja de existir. La idea es del
    /// rework de aturdimiento de Infinitode 2.
    /// </remarks>
    private void Enraizar(Enemy enemigo, int ticks)
    {
        var estados = _datos.Combat.States;
        var jefe = _datos.Combat.Boss;

        if (Tick < enemigo.RootImmuneUntilTick || enemigo.RootCount >= estados.RootMaxPerEnemy)
            return;

        if (EsJefeFinal(enemigo) && jefe.FinalBossImmuneToHardCc)
            return;

        int dura = enemigo.IsBoss
            ? Ints.MultiplyPercent(ticks, jefe.HardCcDurationPercent)
            : ticks;

        if (dura < 1)
            return;

        enemigo.RootedUntilTick = Tick + dura;

        enemigo.RootImmuneUntilTick = enemigo.RootedUntilTick + estados.RootImmunityTicks +
            (enemigo.IsBoss ? jefe.HardCcExtraImmunityTicks : 0);

        enemigo.RootCount++;
    }

    /// <summary>
    /// Empuja al enemigo hacia atrás por la ruta.
    /// </summary>
    /// <remarks>
    /// <para>El presupuesto es finito por enemigo: sin tope, dos guerreros
    /// mantienen a un bicho oscilando en el mismo tramo para siempre.</para>
    ///
    /// <para>⚠ Nunca lo empuja detrás del spawn, y si estaba mordiendo la Torre
    /// vuelve a caminar — que es exactamente para lo que existe el perk.</para>
    /// </remarks>
    private void Empujar(Enemy enemigo, int milesimas, int maxPropio)
    {
        var jefe = _datos.Combat.Boss;

        if (EsJefeFinal(enemigo) && jefe.FinalBossImmuneToPush)
            return;

        int tope = enemigo.IsBoss ? jefe.MaxPushPerBoss : maxPropio;

        if (enemigo.PushCount >= tope)
            return;

        int fuerza = enemigo.IsBoss
            ? Ints.MultiplyPercent(milesimas, jefe.PushPercent)
            : milesimas;

        if (fuerza <= 0)
            return;

        enemigo.DistanceTravelled -= fuerza;

        if (enemigo.DistanceTravelled < 0)
            enemigo.DistanceTravelled = 0;

        if (enemigo.AttackingTower && enemigo.DistanceTravelled < _ruta.Length)
            enemigo.AttackingTower = false;

        enemigo.PushCount++;
    }

    /// <summary>
    /// Aura de cadencia a los defensores aliados cercanos.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>No afecta al emisor</b> —si no, sería un buff propio con nombre de
    /// soporte— y <b>no apila</b>: tres bardos siguen dando +10%, no +30%. Sin la
    /// segunda regla, spamear soportes es la mejor estrategia universal y todas
    /// las demás composiciones sobran.
    /// </remarks>
    private void Inspirar(Defender emisor, int potencia)
    {
        if (potencia <= 0)
            return;

        var estados = _datos.Combat.States;

        long radio = Ints.ToThousandths(estados.InspireRadiusCells);
        long radio2 = radio * radio;

        foreach (var otro in _defensores)
        {
            if (ReferenceEquals(otro, emisor))
                continue;

            long d2 = Ints.SquaredDistanceThousandths(
                emisor.Cell.X, emisor.Cell.Y, otro.Cell.X, otro.Cell.Y);

            if (d2 > radio2)
                continue;

            if (potencia > otro.CadenceBuffPercent)
                otro.CadenceBuffPercent = potencia;

            otro.CadenceBuffExpiryTick = Tick + estados.InspireTicks;
        }
    }

    /// <summary>
    /// Marca de botín: si muere marcado, suelta oro extra.
    /// </summary>
    /// <remarks>
    /// Dos topes, y hacen falta los dos: el de por enemigo impide que un tanque
    /// sea una alcancía, y el de por oleada impide que sesenta bichos marcados
    /// paguen sesenta veces ese tope. Sin ellos una build de economía se vuelve
    /// obligatoria, que es lo contrario de una decisión.
    /// </remarks>
    private void MarcarBotin(Enemy enemigo, int porcentaje, int maxPorEnemigo, int maxPorOleada)
    {
        if (porcentaje <= 0)
            return;

        // Los jefes quedan afuera: su oro ya es el premio de la pelea.
        if (_datos.Combat.Boss.LootIgnoresBosses && enemigo.IsBoss)
            return;

        if (porcentaje > enemigo.ExtraGoldPercent)
            enemigo.ExtraGoldPercent = porcentaje;

        enemigo.ExtraGoldMax = maxPorEnemigo;
        enemigo.ExtraGoldWaveCap = maxPorOleada;
        enemigo.ExtraGoldExpiryTick = Tick + _datos.Combat.States.LootTicks;
    }

    /// <summary>Suma daño por tiempo, conservando Potencia × Duración.</summary>
    private void AplicarDot(Enemy enemigo, DotKind cual, int danoTotal)
    {
        var estados = _datos.Combat.States;
        enemigo.Dots[(int)cual].Add(Tick, danoTotal, estados.DotTicks, estados.DotMaxTicks);
    }

    /// <summary>Quemadura al objetivo y una versión menor a los cercanos por ruta.</summary>
    private void Quemar(Enemy objetivo, int dano, int principal, int secundario)
    {
        var b = _datos.Combat.Behaviors;

        AplicarDot(objetivo, DotKind.Burn, Ints.MultiplyPercent(dano, principal));

        if (secundario <= 0 || b.BurnMaxSecondaries <= 0)
            return;

        int alcanzados = 0;

        foreach (var otro in _enemigos)
        {
            if (alcanzados >= b.BurnMaxSecondaries)
                break;

            if (!otro.Alive || ReferenceEquals(otro, objetivo))
                continue;

            if (System.Math.Abs(otro.DistanceTravelled - objetivo.DistanceTravelled) > b.BurnRadiusThousandths)
                continue;

            AplicarDot(otro, DotKind.Burn, Ints.MultiplyPercent(dano, secundario));
            alcanzados++;
        }
    }

    /// <summary>
    /// Transfiere parte del sobrante de una muerte al vecino de ruta.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Una sola transferencia y no encadena.</b> El techo sobre el daño del
    /// golpe impide que un golpe enorme contra un bicho de una vida se convierta
    /// en un segundo golpe enorme gratis.
    /// </remarks>
    private void Sobrepenetrar(Enemy muerto, int dano, int overkill, int porcentaje, int radio)
    {
        if (overkill <= 0 || porcentaje <= 0)
            return;

        int transfiere = Ints.MultiplyPercent(overkill, porcentaje);
        int techo = Ints.MultiplyPercent(dano, _datos.Combat.Behaviors.OverpenCapPercent);

        if (transfiere > techo)
            transfiere = techo;

        if (transfiere <= 0)
            return;

        foreach (var otro in _enemigos)
        {
            if (!otro.Alive || ReferenceEquals(otro, muerto))
                continue;

            if (System.Math.Abs(otro.DistanceTravelled - muerto.DistanceTravelled) > radio)
                continue;

            GolpeSecundario(otro, transfiere);
            return;
        }
    }

    /// <summary>Encola un eco para dentro de unos ticks.</summary>
    private void EncolarEco(Enemy objetivo, int dano)
    {
        if (dano <= 0)
            return;

        var b = _datos.Combat.Behaviors;

        _ecos.Add(new PendingEcho(
            Tick + b.EchoDelayTicks, objetivo.SpawnId, objetivo.DistanceTravelled, dano, b.EchoSearchThousandths));
    }

    /// <summary>
    /// Las habilidades que escalan el daño del golpe, antes de aplicarlo.
    /// </summary>
    /// <remarks>
    /// Van separadas de las que agregan efectos porque el orden importa: el
    /// splash tiene que salir del daño <b>ya escalado</b>, o un francotirador con
    /// splash haría el bonus solo al objetivo principal.
    /// </remarks>
    private int AdjustDamageByAbility(
        EffectiveStats stats, Defender defensor, Enemy objetivo, int dano)
    {
        switch (stats.Ability)
        {
            case WeaponAbility.Sniper:
            case WeaponAbility.Butcher:
            {
                // Cuánto del alcance está usando el disparo, en porcentaje. Se
                // mide al cuadrado —que es como se mide todo acá— y se compara
                // contra el alcance al cuadrado: no hace falta raíz. Las dos
                // distancias van en milésimas de celda, que es la unidad del
                // alcance desde la propuesta V2.
                var celda = _ruta.CellAt(objetivo.DistanceTravelled);

                long d2 = Ints.SquaredDistanceThousandths(
                    defensor.Cell.X, defensor.Cell.Y, celda.X, celda.Y);

                long alcance2 = (long)stats.RangeMilli * stats.RangeMilli;

                if (alcance2 <= 0)
                    return dano;

                int porcion = (int)System.Math.Min(100, d2 * 100 / alcance2);

                // Francotirador premia lo lejano, carnicero lo cercano. Son la
                // misma cuenta con el porcentaje dado vuelta, y por eso van juntas.
                int factor = stats.Ability == WeaponAbility.Sniper
                    ? porcion
                    : 100 - porcion;

                return dano + Ints.MultiplyPercent(dano, stats.AbilityMagnitude * factor / 100);
            }

            case WeaponAbility.Thirst:
                // Sube con cada muerte de la oleada y se reinicia al terminarla.
                // Techo 100% (propuesta V2): con oleadas de 60 bichos, el tope de
                // 300 se alcanzaba siempre y Sed era un ×4 garantizado.
                return dano + Ints.MultiplyPercent(
                    dano, System.Math.Min(_datos.Combat.Weapons.ThirstBonusCap, defensor.KillsInWave * stats.AbilityMagnitude));

            case WeaponAbility.Critical:
                // Cada 7.º golpe DE ESTE defensor (propuesta V2), igual que el
                // perk. Sin dado, sin sincronización global.
                return defensor.HitCounter % _datos.Combat.Behaviors.HitsPerCritical == 0
                    ? Ints.MultiplyPercent(dano, stats.AbilityMagnitude)
                    : dano;

            default:
                return dano;
        }
    }

    /// <summary>
    /// Lo que la habilidad del arma hace además del golpe.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Nunca se ejecuta junto con un comportamiento de perk.</b> Cuando el
    /// arma tiene habilidad, <see cref="Roster.StatsDe"/> deja el comportamiento
    /// en <c>Ninguno</c>: son la misma ranura, y apilarlas es exactamente lo que
    /// la regla "los comportamientos no se apilan" existe para evitar.
    /// </remarks>
    private void ApplyWeaponAbility(
        EffectiveStats stats, Defender defensor, Enemy objetivo, int dano)
    {
        switch (stats.Ability)
        {
            case WeaponAbility.Splash:
                Salpicar(objetivo, dano, stats.AbilityMagnitude, _datos.Combat.Weapons.MaxSplashSecondaries, _datos.Combat.Behaviors.SplashRadiusThousandths, slowPotencia: 0, slowTicks: 0);
                return;

            case WeaponAbility.Bounce:
                Rebotar(objetivo, dano, stats.AbilityMagnitude, _datos.Combat.Weapons.BounceBounces, _datos.Combat.Behaviors.RicochetWindowThousandths);
                return;

            case WeaponAbility.Frost:
                // ⚠ Misma función que el perk Escarcha, así que hereda las mismas
                // reglas: no apila, refresca, tiene techo y los jefes lo resisten.
                // Dos sistemas de slow con reglas distintas es cómo se cuela el
                // stack infinito por la puerta de atrás.
                Ralentizar(objetivo, stats.AbilityMagnitude, _datos.Combat.States.SlowDurationTicks);
                return;

            case WeaponAbility.Poison:
                // Veneno de verdad desde Estados_v2: reparte el daño en ticks y se
                // funde con el del perk conservando Potencia × Duración. Antes era
                // un segundo golpe inmediato con nombre de veneno.
                AplicarDot(objetivo, DotKind.Poison, Ints.MultiplyPercent(dano, stats.AbilityMagnitude));
                return;

            case WeaponAbility.DoubleShot:
                // ⚠ El segundo golpe NO es entero: pega el 70% (combate.json).
                //
                // Un golpe completo equivale a duplicar el daño directo —+100%— y
                // a ese precio ninguna otra habilidad puede competir por la misma
                // ranura: Doble era la respuesta correcta siempre. Con 70 sigue
                // siendo clarísimamente legendaria y deja lugar para que Cadena,
                // Ejecutar y Codicia sean elecciones y no consuelos.
                //
                // ⚠ Va como SECUNDARIO: duplica el daño, no el disparo. Si contara
                // como directo dispararía otra vez todos los comportamientos, y el
                // arma que ya tapa al perk terminaría procándolo dos veces.
                GolpeSecundario(objetivo, Ints.MultiplyPercent(dano, _datos.Combat.Weapons.DoubleShotPercent));
                return;

            case WeaponAbility.Execute:
            {
                // Umbrales de la propuesta V2: tropa hasta 20%, jefe hasta 5%, y
                // el JEFE FINAL es inmune — matarlo es cómo se gana la corrida,
                // y ganarla salteándole un cuarto de vida vaciaba el final.
                if (_datos.Combat.Weapons.FinalBossImmuneToExecute && EsJefeFinal(objetivo))
                    return;

                if (objetivo.Alive && objetivo.MaxHp > 0)
                {
                    int umbral = System.Math.Min(
                        stats.AbilityMagnitude,
                        objetivo.IsBoss
                            ? _datos.Combat.Weapons.ExecuteThresholdBoss
                            : _datos.Combat.Weapons.ExecuteThresholdTroop);

                    int porcentaje = objetivo.Hp * 100 / objetivo.MaxHp;

                    if (porcentaje <= umbral)
                        objetivo.Hp = 0;
                }

                return;
            }

            case WeaponAbility.Chain:
            {
                // Le pega a hasta 5 secundarios en alcance, y ahora además con
                // TECHO DE DAÑO TOTAL (combate.json).
                //
                // ⚠ El tope de cantidad solo no alcanzaba. Con cinco secundarios al
                // 60% cada uno, Cadena vale +300% en una oleada apretada y +0% en
                // una vacía: una habilidad cuyo poder lo decide la FORMACIÓN de la
                // oleada y no el jugador. Con el techo al 150% del golpe directo, la
                // repartición de un 60% queda 60 / 60 / 30 / 0 / 0: el efecto
                // conserva su identidad y deja de multiplicar el DPS por densidad.
                long alcance2 = (long)stats.RangeMilli * stats.RangeMilli;
                int porSalto = Ints.MultiplyPercent(dano, stats.AbilityMagnitude);
                int presupuesto = Ints.MultiplyPercent(dano, _datos.Combat.Weapons.ChainSecondaryCapPercent);
                int alcanzados = 0;

                foreach (var otro in _enemigos)
                {
                    if (alcanzados >= _datos.Combat.Weapons.MaxChainSecondaries || presupuesto <= 0)
                        break;

                    if (!otro.Alive || ReferenceEquals(otro, objetivo))
                        continue;

                    var celda = _ruta.CellAt(otro.DistanceTravelled);

                    long d2 = Ints.SquaredDistanceThousandths(
                        defensor.Cell.X, defensor.Cell.Y, celda.X, celda.Y);

                    if (d2 <= alcance2)
                    {
                        // Lo que queda del presupuesto, si el salto entero no entra.
                        // Así el último toque es parcial en vez de desaparecer, que
                        // es más legible en pantalla y no cambia el total.
                        int golpe = System.Math.Min(porSalto, presupuesto);

                        GolpeSecundario(otro, golpe);
                        presupuesto -= golpe;
                        alcanzados++;
                    }
                }

                return;
            }

            case WeaponAbility.Vampirism:
            {
                // Cura la Torre — con techo en su vida máxima, UN proc por clase
                // cada 30 ticks, y ahora además un TOPE POR OLEADA.
                //
                // ⚠ La ventana limitaba el RITMO, no el total. Una oleada larga con
                // una build de cadencia seguía curando más de lo que la oleada podía
                // bajar, y la Torre quedaba prácticamente inmortal: el techo real
                // tiene que ser cuánto se puede curar en una oleada, igual que con
                // Sed y con Saqueo.
                int tope = Ints.ToHundredths(_datos.Combat.Weapons.VampirismMaxHealingPerWave);

                if (_curaVampirismoOleada >= tope)
                    return;

                if (!_ultimaCuraPorClase.TryGetValue(defensor.DefenderClass, out int ultima) ||
                    Tick - ultima >= _datos.Combat.Weapons.VampirismCooldownTicks)
                {
                    // ⚠ La magnitud son PUNTOS de vida (1 a 8), no un porcentaje:
                    // hay que escalarla como el HP de la Torre. Sin esto, curar 8
                    // sobre una Torre de 100.000 internos sería curar 0,08 y la
                    // habilidad quedaría muda.
                    int cura = System.Math.Min(
                        Ints.ToHundredths(stats.AbilityMagnitude), tope - _curaVampirismoOleada);

                    // Lo que la Torre no puede recibir tampoco se descuenta del
                    // tope: curar contra una Torre llena no gastó nada.
                    int antes = Tower.Hp;
                    Tower.Hp = System.Math.Min(Tower.MaxHp, Tower.Hp + cura);

                    _curaVampirismoOleada += Tower.Hp - antes;
                    _ultimaCuraPorClase[defensor.DefenderClass] = Tick;
                }

                return;
            }

            case WeaponAbility.Greed:
                // El oro extra se cobra en la fase de muertes, cuando se sepa si
                // este golpe mató. La marca EXPIRA a los 60 ticks (propuesta V2):
                // marcada para siempre, un tanque golpeado una vez era alcancía
                // aunque lo matara otro, minutos después.
                //
                // ⚠ CODICIA AHORA SÍ LLEVA TOPES. La economía tiene realimentación
                // positiva —más oro, más defensores, más kills, más oro— así que
                // una habilidad económica sin techo no es fuerte: es la única
                // build. Sigue siendo mejor que el perk Saqueo, como corresponde a
                // una legendaria, pero ya no puede duplicar la economía de una
                // oleada entera.
                //
                // Los dos topes hacen falta juntos: sin el de oleada, sesenta
                // bichos marcados pagan sesenta veces el tope individual.
                objetivo.ExtraGoldPercent = stats.AbilityMagnitude;
                objetivo.ExtraGoldMax = _datos.Combat.Weapons.GreedMaxExtraPerEnemy;
                objetivo.ExtraGoldWaveCap = _datos.Combat.Weapons.GreedMaxExtraPerWave;
                objetivo.ExtraGoldExpiryTick = Tick + _datos.Combat.Weapons.GreedMarkDurationTicks;
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// El golpe rebota en cadena a enemigos CERCANOS por ruta.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Cada salto exige estar a ≤2 celdas de ruta del objetivo ANTERIOR
    /// (propuesta V2). Antes rebotaba al siguiente vivo en orden de spawn sin
    /// mirar distancia: un rebote le pegaba a un bicho del otro lado del mapa.</para>
    ///
    /// <para>Dentro de la ventana se elige en orden de spawn, que es la regla de
    /// desempate de todo el combate.</para>
    /// </remarks>
    private void Rebotar(Enemy objetivo, int danoBase, int magnitud, int rebotes, int ventana)
    {
        int dano = Ints.MultiplyPercent(danoBase, magnitud);

        if (dano <= 0)
            return;

        var anterior = objetivo;

        // Los ya golpeados en ESTA cadena, para no rebotar dos veces al mismo.
        // Lista y no HashSet: son 1-3 elementos.
        var golpeados = new List<Enemy> { objetivo };

        while (rebotes > 0)
        {
            var proximo = ProximoEnVentana(anterior, golpeados, ventana);

            if (proximo is null)
                break;

            GolpeSecundario(proximo, dano);
            golpeados.Add(proximo);
            anterior = proximo;
            rebotes--;
        }
    }

    /// <summary>
    /// Cadena acotada: cada salto pega menos que el anterior.
    /// </summary>
    /// <remarks>
    /// Es lo que reemplazó al rebote global de Descarga Eléctrica. La diferencia
    /// con <see cref="Rebotar"/> es que los saltos <b>decaen</b>: el primero cobra
    /// <paramref name="primero"/> y los siguientes <paramref name="siguientes"/>,
    /// así el techo del perk se lee de sus dos números y no del largo de la
    /// oleada.
    /// </remarks>
    private void Encadenar(
        Enemy objetivo, int danoBase, int primero, int siguientes, int maxSaltos, int ventana)
    {
        var anterior = objetivo;
        var golpeados = new List<Enemy> { objetivo };

        for (int salto = 0; salto < maxSaltos; salto++)
        {
            int porcentaje = salto == 0 ? primero : siguientes;

            if (porcentaje <= 0)
                return;

            var proximo = ProximoEnVentana(anterior, golpeados, ventana);

            if (proximo is null)
                return;

            GolpeSecundario(proximo, Ints.MultiplyPercent(danoBase, porcentaje));
            golpeados.Add(proximo);
            anterior = proximo;
        }
    }

    /// <summary>
    /// El próximo enemigo vivo a menos de <paramref name="ventana"/> de ruta.
    /// </summary>
    /// <remarks>
    /// ⚠ La ventana se mide contra el objetivo <b>ANTERIOR</b> de la cadena, no
    /// contra el original. Antes se rebotaba al siguiente vivo en orden de spawn
    /// sin mirar distancia, y un rebote le pegaba a un bicho del otro lado del
    /// mapa. Dentro de la ventana se elige en orden de spawn, que es la regla de
    /// desempate de todo el combate.
    /// </remarks>
    private Enemy? ProximoEnVentana(Enemy anterior, List<Enemy> golpeados, int ventana)
    {
        foreach (var otro in _enemigos)
        {
            if (!otro.Alive || golpeados.Contains(otro))
                continue;

            if (System.Math.Abs(otro.DistanceTravelled - anterior.DistanceTravelled) <= ventana)
                return otro;
        }

        return null;
    }

    /// <summary>
    /// Daña a los que están cerca del objetivo, y opcionalmente los ralentiza.
    /// </summary>
    /// <remarks>
    /// <para>"Cerca" se mide <b>en la ruta</b>, no en el mapa, y da lo mismo
    /// porque todos caminan por ella. Comparar distancia recorrida es una resta;
    /// comparar posiciones serían dos conversiones y una distancia al cuadrado por
    /// enemigo.</para>
    ///
    /// <para>⚠ <b>Dos techos, no uno.</b> El de cantidad —cuántos alcanza— y el de
    /// daño total sumado, que sale de <c>splash.totalCapPercent</c>. Sin el
    /// segundo, subir el tope de secundarios sube el daño en la misma proporción y
    /// el perk deja de tener un número que se pueda leer.</para>
    /// </remarks>
    private void Salpicar(
        Enemy objetivo, int danoBase, int magnitud, int maxSecundarios, int radio,
        int slowPotencia, int slowTicks)
    {
        int porGolpe = Ints.MultiplyPercent(danoBase, magnitud);

        if (porGolpe <= 0 || maxSecundarios <= 0)
            return;

        int presupuesto = Ints.MultiplyPercent(danoBase, _datos.Combat.Behaviors.SplashTotalCapPercent);
        int gastado = 0;
        int alcanzados = 0;

        foreach (var otro in _enemigos)
        {
            if (alcanzados >= maxSecundarios || gastado >= presupuesto)
                break;

            if (!otro.Alive || ReferenceEquals(otro, objetivo))
                continue;

            if (System.Math.Abs(otro.DistanceTravelled - objetivo.DistanceTravelled) > radio)
                continue;

            int sale = porGolpe;

            if (gastado + sale > presupuesto)
                sale = presupuesto - gastado;

            GolpeSecundario(otro, sale);
            gastado += sale;
            alcanzados++;

            if (slowPotencia > 0)
                Ralentizar(otro, slowPotencia, slowTicks);
        }
    }

    /// <summary>
    /// A quién le pega un defensor, según su <see cref="Defender.Targeting"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Es la mecánica de más profundidad por menos complejidad del
    /// género</b>, y no toca una sola estadística: cambia tácticamente el
    /// resultado con los mismos números. Un cazador con Concentración en «más
    /// fuerte» se pega a tanques y jefes y acumula stacks; un asesino con
    /// Oportunista en «menos HP» se vuelve un rematador; un paladín con Guardia
    /// del Umbral en «más cerca de la Torre» contiene filtraciones.</para>
    ///
    /// <para>El de siempre —<see cref="TargetingMode.First"/>, el más adelantado—
    /// sigue siendo el valor por defecto, así que un defensor puesto y nunca
    /// tocado se comporta exactamente igual que antes.</para>
    ///
    /// <para>⚠ <b>El desempate es por <see cref="Enemy.SpawnId"/> menor en los
    /// cuatro modos.</b> Los empates son frecuentísimos —dos bichos iguales
    /// spawneados con un tick de diferencia van juntos toda la ruta y tienen el
    /// mismo HP— así que sin una regla explícita el objetivo lo decidiría el orden
    /// de la lista, que es justo lo que no puede decidir nada. Como los candidatos
    /// vienen en orden de spawn y solo se reemplaza con <c>&gt;</c> estricto, el
    /// que llegó primero se queda: <b>no hacer nada ES la regla</b>.</para>
    /// </remarks>
    private Enemy? ElegirObjetivo(Defender defensor, int alcanceMilesimas)
    {
        long alcanceCuadrado = (long)alcanceMilesimas * alcanceMilesimas;

        // La grilla devuelve candidatos por cercanía, ya ordenados por id de
        // spawn. Sigue habiendo que comparar la distancia: el rectángulo de
        // celdas es más grande que el círculo de alcance.
        //
        // ⚠ La grilla trabaja en CELDAS y el alcance viene en milésimas, así que
        // se redondea para arriba. Truncar dejaría afuera al enemigo que está en
        // la fracción de celda que el perk de alcance acaba de ganar, que es
        // exactamente lo que la escala nueva vino a arreglar.
        _grilla.Candidatos(
            defensor.Cell, Ints.ThousandthsToCellsUp(alcanceMilesimas), _candidatos);

        Enemy? mejor = null;
        long mejorPuntaje = 0;

        foreach (int i in _candidatos)
        {
            var e = _enemigos[i];

            if (!e.Alive)
                continue;

            var celda = _ruta.CellAt(e.DistanceTravelled);
            long d2 = Ints.SquaredDistanceThousandths(
                defensor.Cell.X, defensor.Cell.Y, celda.X, celda.Y);

            if (d2 > alcanceCuadrado)
                continue;

            long puntaje = PuntajeDeObjetivo(defensor.Targeting, e, celda);

            if (mejor is null || puntaje > mejorPuntaje)
            {
                mejor = e;
                mejorPuntaje = puntaje;
            }
        }

        return mejor;
    }

    /// <summary>
    /// Cuánto «quiere» este modo a este enemigo. <b>Más alto gana.</b>
    /// </summary>
    /// <remarks>
    /// <para>Un solo número y una sola comparación para los cuatro modos, en vez
    /// de cuatro bucles casi iguales: así el desempate por id de spawn está
    /// escrito una vez, y agregar un quinto modo no puede olvidarse de él.</para>
    ///
    /// <para>Los dos modos «menos» se invierten con una resta en vez de comparar
    /// al revés, por lo mismo. Todo <c>long</c> y todo entero: el HP llega a
    /// millones en la escala interna y la distancia al cuadrado en milésimas pasa
    /// holgadamente de <c>int</c>.</para>
    /// </remarks>
    private long PuntajeDeObjetivo(TargetingMode modo, Enemy e, Cell celda)
    {
        switch (modo)
        {
            case TargetingMode.Strongest:
                return e.Hp;

            case TargetingMode.Weakest:
                // Invertido con una resta: el de menos vida saca el puntaje más
                // alto, y el empate lo sigue resolviendo el orden de spawn.
                return long.MaxValue - e.Hp;

            case TargetingMode.NearestTower:
            {
                // ⚠ NO es lo mismo que Primero. Aquel mide avance sobre la ruta;
                // esto mide distancia a la Torre en el mapa. En un layout que
                // serpentea, el tramo final puede pasar al lado de uno temprano y
                // los dos modos eligen enemigos distintos.
                long d2 = Ints.SquaredDistance(
                    celda.X, celda.Y, Tower.Cell.X, Tower.Cell.Y);

                return long.MaxValue - d2;
            }

            default:
                return e.DistanceTravelled;
        }
    }

    // =========================================================================
    //  Fase 8 — Muertes
    // =========================================================================

    private void FaseMuertes()
    {
        // Primero cobrar y tirar los drops, EN ORDEN DE SPAWN, y recién después
        // sacarlos de la lista. Si se hiciera en una sola pasada borrando sobre
        // la marcha, el orden de las tiradas dependería de cómo borra la lista.
        foreach (var e in _enemigos)
        {
            if (e.Alive)
                continue;

            Gold += OroPorMatar(e);
            RollDrop(e);
        }

        // La habilidad Sed cuenta muertes por defensor. Se atribuyen al que tenga
        // al enemigo en alcance, que es una aproximación —no se sabe quién dio el
        // golpe final— pero es determinista y barata, que es lo que importa.
        ContarMuertesParaSed();

        // RemoveAll es estable: conserva el orden relativo de los que quedan.
        _enemigos.RemoveAll(e => !e.Alive);
    }

    /// <summary>
    /// Cuánto oro deja este muerto: el suyo por el área, más lo que agregue la
    /// marca de botín si la tiene.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>El multiplicador de área desacopla el escalado de los enemigos
    /// del escalado del dinero</b> (Economia_Propuesta). El presupuesto de oleada
    /// es cuadrático, así que sin esto el oro crecía con él y en las áreas
    /// tardías el jugador terminaba con el tablero lleno y oro sobrando: un
    /// recurso que dejó de ser un recurso. Se recorta la entrada en vez de subir
    /// los precios, que es lo que hace BTD6 — bajar el ingreso mantiene legibles
    /// los precios de la tienda.</para>
    ///
    /// <para>El extra de la marca se cobra ACÁ y no al golpear: solo cuenta si el
    /// golpe efectivamente mató. Cobrarlo al golpear daría oro por pegarle a algo
    /// que sobrevive.</para>
    /// </remarks>
    private int OroPorMatar(Enemy e)
    {
        int area = _datos.Areas.OfWave(Wave);
        int oro = Ints.MultiplyPercent(e.Gold, _datos.Curves.Economy.GoldPercentOfArea(area));

        if (e.ExtraGoldPercent <= 0)
            return oro;

        int extra = Ints.MultiplyPercent(oro, e.ExtraGoldPercent);

        // Techo por enemigo: sin él, un tanque golpeado una vez es una alcancía.
        if (e.ExtraGoldMax > 0 && extra > e.ExtraGoldMax)
            extra = e.ExtraGoldMax;

        // Techo por oleada: sin él, sesenta bichos marcados pagan sesenta veces
        // el techo individual y la build de economía vuelve a ser obligatoria.
        if (e.ExtraGoldWaveCap > 0)
        {
            int queda = e.ExtraGoldWaveCap - _oroBotinOleada;

            if (queda < 0)
                queda = 0;

            if (extra > queda)
                extra = queda;

            _oroBotinOleada += extra;
        }

        return oro + extra;
    }

    /// <summary>
    /// Suma las muertes de este tick a los defensores que tengan Sed.
    /// </summary>
    /// <remarks>
    /// <para>Se le atribuye la muerte a <b>todo</b> defensor con Sed que tuviera
    /// al enemigo en alcance, no al que dio el golpe final. Es una aproximación
    /// deliberada: llevar la cuenta de quién pegó último obligaría a guardar el
    /// último atacante en cada enemigo, y eso es estado duplicado que se puede
    /// desincronizar.</para>
    ///
    /// <para>Y para el jugador la diferencia no se nota: si dos defensores con Sed
    /// cubren la misma zona, los dos estuvieron matando.</para>
    /// </remarks>
    private void ContarMuertesParaSed()
    {
        foreach (var d in _defensores)
        {
            var stats = Roster.StatsOf(d.DefenderClass, Backpack);

            if (stats.Ability != WeaponAbility.Thirst)
                continue;

            long alcance2 = (long)stats.RangeMilli * stats.RangeMilli;

            foreach (var e in _enemigos)
            {
                if (e.Alive)
                    continue;

                var celda = _ruta.CellAt(e.DistanceTravelled);

                long dist2 = Ints.SquaredDistanceThousandths(
                    d.Cell.X, d.Cell.Y, celda.X, celda.Y);

                if (dist2 <= alcance2)
                    d.KillsInWave++;
            }
        }
    }

    /// <summary>
    /// La tirada de drop de un enemigo muerto.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Va en la fase 8 y en orden de spawn, siempre.</b> El servidor
    /// regenera esta misma tirada de la seed al revalidar; si se tira en otro
    /// orden sale otro ítem, y la revalidación falla sin que nadie haya hecho
    /// trampa.</para>
    ///
    /// <para><b>El jefe es la única fuente de Oro.</b> Es lo que hace que matar
    /// al jefe valga más que sobrevivirlo — sin eso, un jugador podría llegar a
    /// la oleada 100 sin haber peleado nunca de verdad.</para>
    ///
    /// <para>Nada de esto va al log: lo determina el RNG sembrado, así que el
    /// servidor lo reproduce solo. El log lleva decisiones, no resultados.</para>
    /// </remarks>
    private void RollDrop(Enemy enemigo)
    {
        bool esJefe = enemigo.IsBoss;
        var clases = Roster.Available();

        if (clases.Count == 0)
            return;

        if (esJefe)
        {
            // ⚠ El jefe FINAL no suelta nada (Drops_v2). La corrida termina al
            // matarlo, así que cualquier recompensa llega cuando ya no hay dónde
            // usarla: toda recompensa tiene que tener ventana real de uso.
            if (!_datos.Curves.Drops.FinalBossDrops && EsJefeFinal(enemigo))
                return;

            OfrecerOro(clases);

            // El jefe suelta además una pieza de equipo, con presupuesto atado a
            // la oleada: así un ítem de la 80 es mejor que uno de la 10 sin que
            // haya que escribir listas por tramo.
            var arma = WeaponGenerator.Generate(
                _datos.Curves.PorOleada.BossDropBudget.En(Wave),
                clases, _rng, Backpack.NextWeaponId, _datos.Weapons);

            int idArma = Backpack.RegisterWeapon(arma);
            Backpack.Store(new StoredItem(ItemType.Weapon, idArma));
            return;
        }

        // El bicho común tira bronce o plata, con poca chance.
        if (!_rng.Happens(_datos.Curves.Drops.CommonChance))
            return;

        if (_rng.Happens(_datos.Curves.Drops.BronzeChance))
        {
            var bronce = _datos.Perks.Roll(PerkTier.Bronze, clases, _rng);
            if (bronce is not null)
                Backpack.StorePerk(bronce.Id);
        }
        else
        {
            var plata = _datos.Perks.Roll(PerkTier.Silver, clases, _rng);
            if (plata is not null)
                Backpack.StorePerk(plata.Id);
        }
    }

    // =========================================================================
    //  El Oro del jefe: ofrecer varios y que el jugador elija
    // =========================================================================

    /// <summary>
    /// Abre la pantalla de elección con los Oro que ofrece este jefe.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Sigue saliendo UN SOLO Oro por jefe.</b> Lo que cambia no es
    /// cuánto poder entrega la corrida, es que el jugador decide cuál. Con un Oro
    /// al azar y ocho jefes, ver los <b>dos</b> componentes de una receta concreta
    /// era cuestión de suerte y los Únicos eran una lotería.</para>
    ///
    /// <para>Las opciones salen del pool de la corrida, filtradas por las clases
    /// activas —un Oro que ninguna clase del roster puede equipar es una de las
    /// ocho oportunidades tirada a la basura— y sorteadas con el peso de sinergia,
    /// que empuja hacia la pareja de un plano que el jugador ya está armando.</para>
    /// </remarks>
    private void OfrecerOro(IReadOnlyList<DefenderClass> clases)
    {
        var candidatos = PoolDeOro(clases);

        if (candidatos.Count == 0)
            return;

        int cuantas = System.Math.Min(_datos.Curves.Drops.BossGoldOptions, candidatos.Count);
        var opciones = new Choice[cuantas];

        for (int i = 0; i < cuantas; i++)
        {
            int elegido = SortearConSinergia(candidatos);

            opciones[i] = new Choice(
                ChoiceType.Perk, PrimeraClaseQueLoUsa(elegido, clases), 0, elegido);

            // Se saca del pozo: tres opciones donde dos son iguales es una
            // elección de dos.
            candidatos.Remove(elegido);
        }

        OpenChoices = opciones;
    }

    /// <summary>
    /// Sortea un Oro dando más peso a la pareja de un plano que ya está a medias.
    /// </summary>
    /// <remarks>
    /// ⚠ Es la mitad que hace que las recetas se puedan <b>perseguir</b>. La otra
    /// mitad la da que las recetas ahora se adivinan mirando el efecto: cada Único
    /// es la combinación real de sus dos componentes (ver <c>planos.json</c>). Sin
    /// las dos, un plano comprado con la billetera puede no completarse nunca y la
    /// única progresión permanente del juego se vuelve una promesa vacía.
    /// </remarks>
    private int SortearConSinergia(List<int> candidatos)
    {
        int peso = _datos.Curves.Drops.SynergyWeight;

        if (peso <= 1 || candidatos.Count == 1)
            return candidatos[_rng.Index(candidatos.Count)];

        var pesos = new int[candidatos.Count];
        int total = 0;

        for (int i = 0; i < candidatos.Count; i++)
        {
            pesos[i] = CompletaUnPlanoConocido(candidatos[i]) ? peso : 1;
            total += pesos[i];
        }

        int tirada = _rng.Between(0, total);

        for (int i = 0; i < candidatos.Count; i++)
        {
            tirada -= pesos[i];

            if (tirada < 0)
                return candidatos[i];
        }

        return candidatos[^1];
    }

    /// <summary>¿Este Oro es la pareja de algo que el jugador ya tiene?</summary>
    private bool CompletaUnPlanoConocido(int idPerk)
    {
        foreach (var plano in _datos.Blueprints.All)
        {
            if (!plano.Initial && !_planosAprendidos.Contains(plano.Key))
                continue;

            if (plano.GoldA == idPerk && TieneOro(plano.GoldB))
                return true;

            if (plano.GoldB == idPerk && TieneOro(plano.GoldA))
                return true;
        }

        return false;
    }

    /// <summary>¿Tiene este perk, equipado o en la mochila?</summary>
    private bool TieneOro(int idPerk)
    {
        foreach (var item in Backpack.Items)
        {
            if (item.Kind == ItemType.Perk && item.Id == idPerk)
                return true;
        }

        foreach (var ficha in Roster.Classes)
        {
            foreach (int p in ficha.Perks)
            {
                if (p == idPerk)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Los Oro de esta corrida que le sirven a alguna clase activa.
    /// </summary>
    /// <remarks>
    /// El pool se arma en el <b>primer jefe</b> y no al empezar, porque al empezar
    /// el roster está vacío: no habría con qué filtrar y saldría el catálogo
    /// entero. Es un punto fijo de la secuencia del RNG, que es lo único que la
    /// revalidación necesita.
    /// </remarks>
    private List<int> PoolDeOro(IReadOnlyList<DefenderClass> clases)
    {
        if (_poolOro.Count == 0)
            ArmarPoolDeOro(clases, _datos.Curves.Drops.GoldPoolSize);

        var salida = new List<int>(_poolOro.Count);

        foreach (int id in _poolOro)
        {
            if (AlgunaLoUsa(id, clases))
                salida.Add(id);
        }

        return salida;
    }

    private void ArmarPoolDeOro(IReadOnlyList<DefenderClass> clases, int cuantos)
    {
        var disponibles = new List<int>();

        foreach (var p in _datos.Perks.All)
        {
            // Los que ya están adentro no se vuelven a sortear: el pool es un
            // conjunto, y una entrada repetida sesgaría todos los sorteos
            // posteriores hacia ese perk sin que nadie lo haya decidido.
            if (p.Tier == PerkTier.Gold && !_poolOro.Contains(p.Id) && AlgunaLoUsa(p.Id, clases))
                disponibles.Add(p.Id);
        }

        while (_poolOro.Count < cuantos && disponibles.Count > 0)
        {
            int i = _rng.Index(disponibles.Count);
            _poolOro.Add(disponibles[i]);
            disponibles.RemoveAt(i);
        }
    }

    /// <summary>
    /// Suma unos Oro al pool cuando entra una clase nueva.
    /// </summary>
    /// <remarks>
    /// Sin esto, un roster que crece no cambia lo que el jefe puede ofrecer y
    /// contratar tarde deja a la clase nueva sin comportamiento propio para
    /// siempre — que es exactamente lo contrario de lo que contratar promete.
    ///
    /// ⚠ No hace nada si el pool todavía no existe: hasta el primer jefe no hay
    /// pool que ampliar, y crearlo acá movería el punto de consumo del RNG.
    /// </remarks>
    private void AmpliarPoolDeOro(DefenderClass nueva)
    {
        if (_poolOro.Count == 0)
            return;

        ArmarPoolDeOro([nueva], _poolOro.Count + _datos.Curves.Drops.GoldPoolPerNewClass);
    }

    private bool AlgunaLoUsa(int idPerk, IReadOnlyList<DefenderClass> clases)
    {
        var p = _datos.Perks.Get(idPerk);

        foreach (var c in clases)
        {
            if (p.WorksFor(c))
                return true;
        }

        return false;
    }

    private DefenderClass PrimeraClaseQueLoUsa(int idPerk, IReadOnlyList<DefenderClass> clases)
    {
        var p = _datos.Perks.Get(idPerk);

        foreach (var c in clases)
        {
            if (p.WorksFor(c))
                return c;
        }

        return clases.Count > 0 ? clases[0] : DefenderClass.Mage;
    }

    // =========================================================================
    //  Fase 9 — Chequeos de fin
    // =========================================================================

    private void FaseChequeos()
    {
        if (!Tower.Standing)
        {
            State = RunState.Lost;

            // El oro sin gastar al morir es la métrica que más veces mintió: sin
            // desglose no dice si sobra oro, si los precios están mal o si no había
            // nada que valiera la pena comprar.
            Metrics.GoldAtEnd = Gold;
            return;
        }

        bool oleadaLimpia = _enemigos.Count == 0 && _porSpawnear.Count == 0;

        if (!oleadaLimpia)
            return;

        if (Wave >= _datos.Curves.RunCurve.WavesPerRun)
        {
            // ⚠ Se gana matando al jefe de la 100, no "sobreviviendo". La
            // corrida TERMINA acá: no hay modo sin fin y no hay área 9.
            State = RunState.Won;
            Metrics.GoldAtEnd = Gold;
            return;
        }

        // La elección frena la corrida: no se puede cerrar sin elegir. Frenarla
        // acá y no al empezar la oleada siguiente le da al jugador el momento de
        // calma para decidir, que es cuando la decisión se piensa.
        if (WaitingForChoice)
            return;

        // ⚠ LA PREPARACIÓN SE ARMA ANTES DE ABRIR LA ELECCIÓN, y eso cambió.
        //
        // Antes la elección abría, el jugador elegía, y recién al tick siguiente
        // se armaba la oleada y se renovaba la tienda. O sea que las dos
        // decisiones —la que REGALA poder y la que lo COBRA— nunca se veían
        // juntas, y comparar «¿me conviene el perk de 900 o desbloquear el
        // druida?» era imposible: para cuando aparecía la tienda, la elección ya
        // estaba tomada.
        //
        // Armar primero las pone en la misma pantalla (INTERFAZ.md §4bis). Acá NO
        // se lanza la oleada: se abre la fase de preparación y se espera.
        //
        // ⚠ Cambia el ORDEN DE CONSUMO DEL RNG —antes elección, después oleada y
        // tienda; ahora al revés— así que las corridas guardadas con el orden
        // viejo no se re-simulan igual. Es temporada nueva.
        ArmarProximaOleada();

        if (Wave - _oleadaUltimaEleccion >= _datos.Curves.RunCurve.WavesBetweenChoices)
        {
            var opciones = ChoiceBuilder.Next(Roster, _rng, _datos);
            _oleadaUltimaEleccion = Wave;

            // Sin opciones —roster completo y todo en nivel 6— se sigue de largo.
            // Es un final de progresión legítimo, no un error.
            if (opciones.Length > 0)
                OpenChoices = opciones;
        }
    }

    // =========================================================================
    //  Oleadas
    // =========================================================================

    /// <summary>
    /// Arma la próxima oleada y la encola.
    /// </summary>
    /// <remarks>
    /// Provisional. La versión de verdad compra enemigos con un <b>presupuesto</b>
    /// que crece por oleada, eligiendo de la lista permitida del área. Ese
    /// presupuesto es el mismo patrón que usan el poder de los ítems y los
    /// drops, y es lo que hace que la seed decida de qué está hecha una oleada
    /// sin que el diseñador liste 100 oleadas a mano (OLEADAS.md).
    /// </remarks>
    /// <summary>
    /// Arma la próxima oleada y abre la fase de preparación.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Armar y soltar están separados a propósito.</b> La pantalla de
    /// preparación muestra qué viene —cuántos de cada bicho y cuáles traen
    /// perk— y para eso la oleada tiene que estar armada <b>antes</b> de que el
    /// jugador apriete comenzar.</para>
    ///
    /// <para>Adelantar el armado mueve el consumo del RNG a un tick anterior, y
    /// eso <b>no</b> rompe nada porque la secuencia no cambia: entre que se abre
    /// la preparación y se suelta la oleada no corre ninguna fase, y ninguna
    /// acción del jugador —poner, vender, comprar, equipar, fusionar, descartar—
    /// toca el generador. Se verificó una por una.</para>
    ///
    /// <para>La tienda se renueva acá también, y en el mismo orden que antes:
    /// primero la oleada, después la tienda. Invertirlos correría todos los
    /// números que vienen después y la revalidación fallaría. De paso, el jugador
    /// ve el stock nuevo durante la preparación, que es cuando lo va a usar.</para>
    /// </remarks>
    private void ArmarProximaOleada()
    {
        // El bonus de la oleada que se acaba de limpiar se cobra ACÁ: si se
        // cobrara al matar al último bicho, un jugador que pierde en ese mismo
        // tick se llevaría el bonus de una oleada que no terminó de aguantar. Y
        // cobrarlo antes de la preparación es lo que le da con qué comprar.
        Gold += _bonusOroOleada;
        _bonusOroOleada = 0;

        // ⚠ Se arma con `Oleada + 1` y el contador NO se toca todavía.
        //
        // Mientras dura la preparación, `Oleada` sigue diciendo la que se jugó. Si
        // se incrementara acá, una corrida abandonada en la preparación reportaría
        // al ranking una oleada que nunca se jugó.
        var receta = Balance.RecipeOf(Wave + 1, _datos);
        _oleadaArmada = WaveBuilder.Armar(receta, _rng, _datos);

        // ⚠ LA TIENDA YA NO SE RENUEVA TODAS LAS OLEADAS: abre cada
        // «intervalWaves», junto a la pantalla de elección. Son unas 33 tiendas en
        // una corrida completa en vez de 100.
        //
        // No es un recorte de contenido: es lo que pone las dos decisiones a
        // competir. La elección da poder gratis y la tienda lo cobra, y con la
        // tienda abriendo todas las oleadas el jugador nunca tenía que decidir
        // entre las dos — siempre había otra tienda a una oleada de distancia.
        //
        // ⚠ SE MIRA LA OLEADA QUE SE ACABA DE LIMPIAR, no la que viene.
        //
        // La elección periódica dispara en FaseChequeos cuando la oleada limpiada
        // es múltiplo de «wavesBetweenChoices», y recién en el tick siguiente se
        // llega acá a armar la preparación. Preguntar por `Wave + 1` abría la
        // tienda una oleada ANTES —al terminar la 2, la 5, la 8— y las dos
        // decisiones dejaban de caer juntas, que es el punto entero de la mecánica.
        //
        // Y tenía una consecuencia peor: el stock quedaba vivo mientras la
        // elección contrataba una clase, así que una oferta de «reclutar Cazador»
        // podía sobrevivir a que el jugador ya tuviera Cazador. Comprarla rebota
        // sin marcarse vendida, y un bot que reintenta con el reloj congelado
        // se cuelga para siempre. Medido: `--balance 24` sin terminar en 30 minutos.
        _tiendaAbierta = _datos.Curves.Shop.OpensAt(Wave);

        if (_tiendaAbierta)
        {
            Metrics.GoldBeforeShopTotal += Gold;

            Shop.Refresh(Wave + 1, Roster, Backpack, _planosAprendidos, _rng, _datos);
            Metrics.CountOffers(Shop.Stock, _datos);

            foreach (var oferta in Shop.Stock)
            {
                // Una oferta que el bolsillo alcanza a pagar es la definición
                // operativa de «la tienda ofreció algo». Si este número se despega
                // de ShopsVisited, la pantalla está frenando la corrida sin dar
                // nada — que es peor que no abrirla.
                if (oferta.Price <= Gold)
                {
                    Metrics.ShopsWithSomethingUsable++;
                    break;
                }
            }
        }

        WaitingForWaveStart = true;
    }

    /// <summary>
    /// Suelta la oleada ya armada. Cierra la preparación y arranca el reloj.
    /// </summary>
    private void LaunchNextWave()
    {
        if (_oleadaArmada is not { } armada)
            return;

        Wave++;

        // ⚠ La Sed se reinicia con cada oleada. Sin esto el contador crecería
        // durante las 100 y el bonus sería infinito: el techo tiene que ser
        // cuánto se puede matar de una vez, no cuánto se lleva matado.
        foreach (var d in _defensores)
            d.KillsInWave = 0;

        // Lo mismo con el tope de oro de Saqueo, y por lo mismo: el techo real es
        // cuánto se puede saquear en una oleada, no en cien.
        _oroBotinOleada = 0;

        // ⚠ Y con el tope de curación de Vampirismo, que es nuevo y va por lo
        // mismo: la ventana de 30 ticks limitaba el RITMO pero no el total, así
        // que una oleada larga con una build de cadencia seguía curando más de lo
        // que la oleada podía bajar.
        _curaVampirismoOleada = 0;

        // Lo que quedó en el bolsillo al arrancar la oleada es lo que la tienda
        // NO se llevó. Se mide solo en las oleadas de tienda: en las otras sería
        // el mismo número contado tres veces.
        //
        // ⚠ Sale de la bandera y no de recalcular el módulo. `Wave` ya se
        // incrementó arriba, así que la cuenta habría que hacerla contra `Wave - 1`
        // — y ese es exactamente el tipo de desfasaje de uno que ya rompió una vez
        // el momento en que abre la tienda.
        if (_tiendaAbierta)
        {
            Metrics.GoldAfterShopTotal += Gold;
            _tiendaAbierta = false;
        }

        // ⚠ La tienda CIERRA al lanzar la oleada. Es cuando el panel deja de verse,
        // y dejar el stock comprable después sería que la interfaz diga una cosa y
        // la simulación acepte otra.
        Shop.Close();

        foreach (var compra in armada.Npcs)
            _porSpawnear.Enqueue(compra);

        _intervaloSpawn = armada.SpawnIntervalTicks;
        _formacionOleada = armada.Formation;
        _spawneadosEnOleada = 0;
        _bonusOroOleada = armada.BonusOro;
        _ticksParaProximoSpawn = 0;
        _oleadaArmada = null;

        WaitingForWaveStart = false;
    }

    /// <summary>
    /// Arranca la corrida lanzando la oleada 1.
    /// </summary>
    public void Empezar()
    {
        // ⚠ Acá NO se entrega oro: el inicial son dos fichas de la clase que
        // toque, y eso se sabe recién al elegirla. Ver Elegir().
        Gold = 0;

        // 1 de 3 al empezar. La corrida arranca con la pantalla de elección
        // abierta y el roster vacío: no se puede poner nada hasta elegir.
        OpenChoices = ChoiceBuilder.Initial(_rng, _datos);

        LaunchNextWave();
    }
}
