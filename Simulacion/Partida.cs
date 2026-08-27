namespace TowerOfArgentum.Simulacion;

/// <summary>Cómo terminó, o que sigue.</summary>
public enum EstadoPartida
{
    EnCurso = 0,

    /// <summary>Cayó la Torre.</summary>
    Perdio = 1,

    /// <summary>Murió el jefe de la oleada 100.</summary>
    Gano = 2,
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
public sealed class Partida
{
    private readonly Aleatorio _rng;
    private readonly Mapa _mapa;
    private readonly Ruta _ruta;
    private readonly LogAcciones _log;

    /// <summary>Índice espacial de enemigos, para el alcance.</summary>
    private readonly GrillaEspacial _grilla;

    /// <summary>Último tick en que curó Vampirismo, por clase. Ver la habilidad.</summary>
    private readonly Dictionary<Clase, int> _ultimaCuraPorClase = [];

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
    private readonly List<Enemigo> _enemigos = [];

    /// <summary>Los defensores puestos, en orden de colocación.</summary>
    private readonly List<Defensor> _defensores = [];

    /// <summary>
    /// Los NPCs comprados de la oleada actual que todavía no entraron, en el
    /// orden en que se compraron.
    /// </summary>
    private readonly Queue<CompraNpc> _porSpawnear = new();

    /// <summary>Ticks entre spawn y spawn de la oleada actual.</summary>
    /// <remarks>Arranca en el de <c>curvas.json</c> hasta que la primera oleada imponga el suyo.</remarks>
    private int _intervaloSpawn;

    /// <summary>Oro que paga limpiar la oleada actual.</summary>
    private int _bonusOroOleada;

    /// <summary>Las clases que el jugador tiene, con su nivel, perks y equipo.</summary>
    public Roster Roster { get; }

    /// <summary>Los lugares de mochila, compartidos entre perks y equipo.</summary>
    public Mochila Mochila { get; }

    /// <summary>La tienda de la corrida. El stock lo decide la seed.</summary>
    public Tienda Tienda { get; } = new();

    /// <summary>
    /// Las 3 opciones de la pantalla de elección, cuando está abierta.
    /// </summary>
    /// <remarks>
    /// <b>Las genera la seed, no se guardan en el log.</b> El log lleva solo cuál
    /// de las 3 eligió el jugador, por índice. Si guardara el contenido, un
    /// cliente modificado podría reclamar que le ofrecieron algo mejor de lo que
    /// el servidor va a regenerar.
    /// </remarks>
    public Opcion[] OpcionesAbiertas { get; private set; } = [];

    public bool EsperandoEleccion => OpcionesAbiertas.Length > 0;

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
    public bool EsperandoInicioDeOleada { get; private set; }

    /// <summary>El reloj está detenido: preparación o elección.</summary>
    public bool EnPausa => EsperandoInicioDeOleada || EsperandoEleccion;

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
    public bool EsperaAlJugador => EnPausa && _cursorLog >= _log.Cantidad;

    /// <summary>Qué número de oleada viene. Durante la preparación, la que espera.</summary>
    public int ProximaOleada => _oleadaArmada is null ? Oleada : Oleada + 1;

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
    public IReadOnlyList<AvanceDeOleada> AvanceProximaOleada()
    {
        if (_oleadaArmada is not { } armada)
            return [];

        var porTipo = new Dictionary<int, AvanceDeOleada>();
        var orden = new List<int>();

        foreach (var compra in armada.Npcs)
        {
            if (!porTipo.TryGetValue(compra.TipoId, out var fila))
            {
                var dato = _datos.Por(compra.TipoId);

                fila = new AvanceDeOleada
                {
                    TipoId = compra.TipoId,
                    Nombre = dato?.Nombre ?? "?",
                    Grafico = dato?.Grafico ?? "",
                    EsJefe = compra.EsJefe,
                };

                porTipo[compra.TipoId] = fila;
                orden.Add(compra.TipoId);
            }

            fila.Cuantos++;

            if (compra.PerkVida > 0 || compra.PerkVelocidad > 0)
                fila.ConPerk++;
        }

        var salida = new List<AvanceDeOleada>(orden.Count);

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
    public int TickDeAccion => EnPausa ? Tick : Tick + 1;

    /// <summary>Planos aprendidos en la cuenta. Los manda el servidor al empezar.</summary>
    private readonly HashSet<string> _planosAprendidos = [];

    private int _proximoIdSpawn;
    private int _proximaColocacion;
    private int _oleadaUltimaEleccion;
    private int _cursorLog;
    private int _ticksParaProximoSpawn;

    /// <summary>La oleada que viene, ya armada y esperando el botón.</summary>
    private OleadaArmada? _oleadaArmada;

    /// <summary>
    /// Los datos de balance de la temporada. <b>Entrada de la simulación</b>,
    /// igual que la seed y el log.
    /// </summary>
    private readonly DatosBalance _datos;

    public DatosBalance Datos => _datos;

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
    public Partida(ulong seed, Mapa mapa, LogAcciones log, DatosBalance datos)
    {
        _rng = new Aleatorio(seed);
        _mapa = mapa;
        _ruta = mapa.Ruta;
        _log = log;
        _grilla = new GrillaEspacial(mapa.Ancho, mapa.Alto);
        _datos = datos;

        Roster = new Roster(datos);
        Mochila = new Mochila(datos);
        _intervaloSpawn = datos.Curvas.Oleada.IntervaloSpawnPorDefecto;

        Torre = new Torre
        {
            HpMaximo = _datos.Curvas.Torre.HpInicial,
            Hp = _datos.Curvas.Torre.HpInicial,
            Celda = _ruta.Final,
        };
    }

    /// <summary>Tick actual. Es la unidad del ranking: menos ticks es mejor.</summary>
    public int Tick { get; private set; }

    /// <summary>Oleada actual, de 1 a 100.</summary>
    public int Oleada { get; private set; }

    public int Oro { get; private set; }

    public Torre Torre { get; }

    public EstadoPartida Estado { get; private set; } = EstadoPartida.EnCurso;

    public IReadOnlyList<Enemigo> Enemigos => _enemigos;

    public IReadOnlyList<Defensor> Defensores => _defensores;

    public Ruta Ruta => _ruta;

    public Mapa Mapa => _mapa;

    /// <summary>
    /// ¿Se puede poner un defensor de esta clase en esta celda, ahora?
    /// </summary>
    /// <remarks>
    /// Lo usa la UI para pintar la celda de verde o de rojo bajo el cursor, y lo
    /// usa <see cref="Poner"/> para decidir. <b>Es la misma función en los dos
    /// casos, a propósito:</b> si la UI dijera que sí y la simulación que no, el
    /// jugador vería un click que no hace nada.
    /// </remarks>
    public bool PuedePoner(Clase clase, Celda celda)
    {
        // Solo se puede poner una clase contratada. El roster arranca vacío y se
        // llena eligiendo, así que la primera elección es obligatoria antes de
        // poder jugar.
        if (!Roster.Tiene(clase))
            return false;

        if (!_mapa.EsColocable(celda))
            return false;

        if (Oro < _datos.StatsDe(clase).Costo)
            return false;

        foreach (var d in _defensores)
            if (d.Celda == celda)
                return false;

        return true;
    }

    /// <summary>El defensor que está en esa celda, si hay alguno.</summary>
    public Defensor? DefensorEn(Celda celda)
    {
        foreach (var d in _defensores)
            if (d.Celda == celda)
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
    public void Avanzar()
    {
        if (Estado != EstadoPartida.EnCurso)
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
        if (EnPausa)
            return;

        // 2. Spawn de los que le tocan a este tick, con sus perks ya tirados.
        FaseSpawn();

        // 3. Expirar los efectos vencidos.
        FaseExpirar();

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
    public void Avanzar(int ticks)
    {
        for (int i = 0; i < ticks && Estado == EstadoPartida.EnCurso; i++)
            Avanzar();
    }

    // =========================================================================
    //  Fase 1 — Acciones del jugador
    // =========================================================================

    private void AplicarAccion(Accion accion)
    {
        switch (accion.Tipo)
        {
            case TipoAccion.Poner:
                Poner((Clase)accion.A, new Celda(accion.B, accion.C));
                break;

            case TipoAccion.Vender:
                Vender(accion.A);
                break;

            case TipoAccion.AdelantarOleada:
                Adelantar();
                break;

            case TipoAccion.Elegir:
                Elegir(accion.A);
                break;

            case TipoAccion.EquiparPerk:
                EquiparPerk(accion.A, (Clase)accion.B);
                break;

            case TipoAccion.SacarPerk:
                SacarPerk((Clase)accion.A, accion.B);
                break;

            case TipoAccion.Equipar:
                Equipar(accion.A, (Clase)accion.B);
                break;

            case TipoAccion.VenderEquipo:
                VenderEquipo(accion.A);
                break;

            case TipoAccion.Comprar:
                Comprar(accion.A);
                break;

            case TipoAccion.Descartar:
                Mochila.SacarEn(accion.A);
                break;

            case TipoAccion.Fusionar:
                Fusionar(accion.A, accion.B, (Clase)accion.C);
                break;

            // El resto entra con sus sistemas. Se ignoran en silencio y no se
            // rompe: un log viejo tiene que poder re-simularse aunque el sistema
            // todavía no exista.
            default:
                break;
        }
    }

    private void Poner(Clase clase, Celda celda)
    {
        // Se re-valida acá aunque la UI ya haya validado, porque el log de una
        // corrida guardada se re-simula por este mismo camino: un log manipulado
        // que pida poner en una celda prohibida tiene que rebotar igual.
        if (!PuedePoner(clase, celda))
            return;

        var stats = _datos.StatsDe(clase);
        Oro -= stats.Costo;

        _defensores.Add(new Defensor
        {
            IdColocacion = _proximaColocacion++,
            Clase = clase,
            Celda = celda,
            TickColocacion = Tick,
            CooldownRestante = 0,
        });
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
        if (EsperandoInicioDeOleada)
        {
            EsperandoInicioDeOleada = false;
            LanzarProximaOleada();
            return;
        }

        // Con la oleada limpia y sin fase de preparación abierta no hay nada que
        // adelantar: la elección todavía no se resolvió, o la corrida terminó.
        if (_enemigos.Count == 0 && _porSpawnear.Count == 0)
            return;

        int vivos = _enemigos.Count + _porSpawnear.Count;

        Oro += Enteros.Limitar(vivos * _datos.Curvas.Economia.OroPorEnemigoAdelantado, 0, _datos.Curvas.Economia.TopeOroPorAdelantar);

        LanzarProximaOleada();
    }

    private void Elegir(int indice)
    {
        if (indice < 0 || indice >= OpcionesAbiertas.Length)
            return;

        var opcion = OpcionesAbiertas[indice];

        // El oro inicial se entrega ACÁ y no en Empezar, porque depende de qué
        // clase tocó: dos fichas de TU clase, la misma cantidad para todos.
        //
        // Con un número fijo el arranque era una lotería: 200 eran cinco ladrones
        // o dos cazadores, y las corridas de clase cara se estancaban en la
        // oleada 1 — medido con --balance, mitad de las seeds muertas ahí. Es
        // determinista igual: sale de la acción de elegir, que está en el log.
        bool primeraClase = Roster.Clases.Count == 0;

        if (opcion.Tipo == TipoOpcion.Contratar)
            Roster.Contratar(opcion.Clase);
        else
            Roster.Levelear(opcion.Clase);

        if (primeraClase)
            Oro = _datos.Curvas.Economia.FichasDeOroInicial * _datos.StatsDe(opcion.Clase).Costo;

        OpcionesAbiertas = [];
    }

    /// <summary>Equipa un perk de la mochila en una clase.</summary>
    private void EquiparPerk(int indiceMochila, Clase clase)
    {
        if (indiceMochila < 0 || indiceMochila >= Mochila.Cosas.Count)
            return;

        var cosa = Mochila.Cosas[indiceMochila];
        if (cosa.Tipo != TipoCosa.Perk)
            return;

        var ficha = Roster.Buscar(clase);
        if (ficha is null || !ficha.TieneSlotLibre)
            return;

        var def = _datos.Perks.Por(cosa.Id);
        if (!def.SirveA(clase))
            return;

        // El tope de apilado sale del tier: Bronce 3, Plata 2, Oro y Único 1.
        int yaTiene = 0;
        foreach (int p in ficha.Perks)
            if (p == cosa.Id)
                yaTiene++;

        if (yaTiene >= def.MaxApilado)
            return;

        Mochila.SacarEn(indiceMochila);
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
    private void SacarPerk(Clase clase, int slot)
    {
        var ficha = Roster.Buscar(clase);
        if (ficha is null || slot < 0 || slot >= ficha.Perks.Count)
            return;

        ficha.Perks.RemoveAt(slot);
    }

    private void Equipar(int indiceMochila, Clase clase)
    {
        if (indiceMochila < 0 || indiceMochila >= Mochila.Cosas.Count)
            return;

        var cosa = Mochila.Cosas[indiceMochila];
        if (cosa.Tipo != TipoCosa.Arma)
            return;

        var ficha = Roster.Buscar(clase);
        var arma = Mochila.ArmaPorId(cosa.Id);
        if (ficha is null || arma is null)
            return;

        // Lo que estaba puesto vuelve a la mochila si hay lugar. ⚠ El equipo SÍ
        // vuelve, al revés que los perks: el equipo se vende y se recambia, los
        // perks se destruyen. Son dos sistemas y no se mezclan.
        int anterior = ficha.Arma;

        if (anterior >= 0 && !Mochila.Guardar(new CosaGuardada(TipoCosa.Arma, anterior)))
            return;

        Mochila.SacarEn(indiceMochila);
        ficha.Arma = arma.Id;
    }

    private void VenderEquipo(int indiceMochila)
    {
        if (indiceMochila < 0 || indiceMochila >= Mochila.Cosas.Count)
            return;

        var cosa = Mochila.Cosas[indiceMochila];
        if (cosa.Tipo != TipoCosa.Arma)
            return;

        var arma = Mochila.ArmaPorId(cosa.Id);
        if (arma is null)
            return;

        Mochila.SacarEn(indiceMochila);
        Oro += arma.Valor;
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
    private void Fusionar(int indiceA, int indiceB, Clase clase)
    {
        if (indiceA == indiceB)
            return;

        if (indiceA < 0 || indiceA >= Mochila.Cosas.Count ||
            indiceB < 0 || indiceB >= Mochila.Cosas.Count)
        {
            return;
        }

        var a = Mochila.Cosas[indiceA];
        var b = Mochila.Cosas[indiceB];

        if (a.Tipo != TipoCosa.Perk || b.Tipo != TipoCosa.Perk)
            return;

        if (_datos.Perks.Por(a.Id).Tier != TierPerk.Oro ||
            _datos.Perks.Por(b.Id).Tier != TierPerk.Oro)
        {
            return;
        }

        var plano = _datos.Planos.Buscar(a.Id, b.Id);
        if (plano is null)
            return;

        // El plano tiene que estar aprendido, salvo los iniciales: sin al menos
        // una receta conocida, un jugador nuevo nunca ve un Único y la mecánica
        // le resulta invisible.
        if (!plano.Inicial && !_planosAprendidos.Contains(plano.Nombre))
            return;

        var ficha = Roster.Buscar(clase);
        if (ficha is null)
            return;

        var resultante = _datos.Perks.Por(plano.UnicoResultante);
        if (!resultante.SirveA(clase))
            return;

        // Se sacan en orden descendente para que el primer índice siga siendo
        // válido después de sacar el segundo.
        int mayor = System.Math.Max(indiceA, indiceB);
        int menor = System.Math.Min(indiceA, indiceB);
        Mochila.SacarEn(mayor);
        Mochila.SacarEn(menor);

        // Si no hay slot libre, el Único va a la mochila y el jugador decide
        // después. Perderlo por no tener lugar sería castigar la mecánica que el
        // juego quiere enseñar.
        if (ficha.TieneSlotLibre)
            ficha.Perks.Add(plano.UnicoResultante);
        else
            Mochila.GuardarPerk(plano.UnicoResultante);
    }

    /// <summary>
    /// Compra una oferta de la tienda.
    /// </summary>
    /// <remarks>
    /// Lo que va al log es el <b>índice</b> de la oferta, no qué era: el stock lo
    /// regenera el servidor de la seed. Guardar el contenido dejaría que un
    /// cliente modificado reclamara haber comprado algo que no estaba a la venta.
    /// </remarks>
    private void Comprar(int indice)
    {
        var stock = Tienda.Stock;

        if (indice < 0 || indice >= stock.Count)
            return;

        var oferta = stock[indice];

        if (oferta.Vendida || Oro < oferta.Precio)
            return;

        // ⚠ Si la mochila está llena la compra no pasa y el oro NO se gasta.
        // Cobrarle al jugador por algo que no entra sería el peor error posible de
        // esta pantalla, y es exactamente el que sale de descontar antes de
        // guardar.
        if (!Mochila.Guardar(new CosaGuardada(oferta.Tipo, oferta.Id)))
            return;

        Oro -= oferta.Precio;
        Tienda.Vender(indice);
    }

    /// <summary>Los planos que la cuenta tiene aprendidos. Los manda el servidor.</summary>
    public void AprenderPlano(string nombre) => _planosAprendidos.Add(nombre);

    private void Vender(int idColocacion)
    {
        int i = _defensores.FindIndex(d => d.IdColocacion == idColocacion);
        if (i < 0) return;

        var stats = _datos.StatsDe(_defensores[i].Clase);
        Oro += Enteros.MultiplicarPorcentaje(stats.Costo, _datos.Curvas.Defensores.PorcentajeVentaDefensor);

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
        var b = _datos.Por(compra.TipoId);

        if (b is null)
            return;   // el tipo no está en los datos: no spawnea nada

        int hpMaximo = Enteros.ConPorcentajes(b.Hp, compra.PerkVida);

        // El daño sale del promedio de min y max, no de una tirada por golpe: una
        // tirada acá gastaría un número del RNG por cada golpe de cada enemigo, y
        // eso hace que agregar un defensor corra toda la secuencia posterior y
        // cambie los drops de la corrida entera.
        int dano = System.Math.Max(_datos.Combate.DanoMinimoEnemigo, (b.DanoMin + b.DanoMax) / _datos.Combate.DivisorPromedioDanoEnemigo);

        _enemigos.Add(new Enemigo
        {
            IdSpawn = _proximoIdSpawn++,
            TipoId = compra.TipoId,
            EsJefe = compra.EsJefe,
            HpMaximo = hpMaximo,
            Hp = hpMaximo,
            Velocidad = b.Velocidad,
            Dano = dano,
            CadenciaTicks = b.CadenciaTicks,
            Oro = b.Oro,
            DistanciaRecorrida = 0,
            PerkVidaExtra = compra.PerkVida,
            PerkVelocidadExtra = compra.PerkVelocidad,
        });

        _ticksParaProximoSpawn = _intervaloSpawn;
    }

    // =========================================================================
    //  Fase 3 — Expirar efectos
    // =========================================================================

    private void FaseExpirar()
    {
        // Los dos efectos con duración que existen (propuesta V2): el slow y la
        // marca de Codicia. Vencen acá, en la fase que siempre estuvo reservada
        // para esto.
        foreach (var e in _enemigos)
        {
            if (e.SlowPorciento > 0 && Tick >= e.SlowExpiraTick)
                e.SlowPorciento = 0;

            if (e.OroExtraPorciento > 0 && Tick >= e.OroExtraExpiraTick)
                e.OroExtraPorciento = 0;
        }
    }

    // =========================================================================
    //  Fase 4 — Mover
    // =========================================================================

    private void FaseMover()
    {
        foreach (var e in _enemigos)
        {
            if (!e.Vivo || e.AtacandoTorre)
                continue;

            e.DistanciaRecorrida += e.VelocidadEfectiva;
        }
    }

    // =========================================================================
    //  Fase 5 — Llegadas
    // =========================================================================

    private void FaseLlegadas()
    {
        foreach (var e in _enemigos)
        {
            if (!e.Vivo || e.AtacandoTorre)
                continue;

            if (e.DistanciaRecorrida >= _ruta.Largo)
            {
                e.DistanciaRecorrida = _ruta.Largo;
                e.AtacandoTorre = true;

                // Pega recién en su próximo turno de cadencia, no al llegar: si
                // no, un enemigo rápido descarga un golpe gratis por el solo
                // hecho de haber llegado en este tick.
                e.CooldownAtaque = e.CadenciaTicks;
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
        Enemigo? mordiendo = null;

        foreach (var e in _enemigos)
        {
            if (!e.Vivo || !e.AtacandoTorre)
                continue;

            mordiendo ??= e;

            if (e.CooldownAtaque > 0)
            {
                e.CooldownAtaque--;
                continue;
            }

            Torre.Recibir(e.Dano);
            e.CooldownAtaque = e.CadenciaTicks;
        }

        // ⚠ El cooldown de la Torre baja SOLO cuando hay a quién pegarle.
        //
        // Si bajara siempre, llegaría a cero durante la caminata y el primero en
        // tocarla comería un golpe gratis por haber llegado en el tick correcto.
        // Es la misma razón por la que el enemigo tampoco pega al llegar.
        if (mordiendo is null)
        {
            Torre.CooldownAtaque = _datos.Curvas.Torre.CadenciaTicks;
            return;
        }

        if (Torre.CooldownAtaque > 0)
        {
            Torre.CooldownAtaque--;
            return;
        }

        mordiendo.Hp -= _datos.Curvas.Torre.Dano;
        Torre.CooldownAtaque = _datos.Curvas.Torre.CadenciaTicks;
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
        _grilla.Reconstruir(_enemigos, _ruta);

        foreach (var d in _defensores)
        {
            if (d.CooldownRestante > 0)
            {
                d.CooldownRestante--;
                continue;
            }

            // base + nivel + perks + equipo, recalculado cada vez. Nada de esto
            // vive en el defensor: un cazador puesto en la oleada 3 y otro puesto
            // en la 40 pegan exactamente igual.
            var stats = Roster.StatsDe(d.Clase, Mochila);
            var objetivo = ElegirObjetivo(d, stats.AlcanceCeldas);

            if (objetivo is null)
                continue;

            // El contador de golpes del defensor, para los críticos: sube ANTES
            // de resolver el disparo, así "cada 7.º golpe" cuenta este.
            d.ContadorGolpes++;

            // Daño instantáneo. El proyectil que se ve volar es SOLO render: si
            // el daño esperara a que el sprite llegue, el resultado dependería
            // de los FPS.
            int dano = stats.Dano;

            // La habilidad del arma puede cambiar el daño ANTES de aplicarlo:
            // francotirador, carnicero y sed lo escalan. Las que agregan efectos
            // van después, con el daño ya resuelto.
            dano = AjustarDanoPorHabilidad(stats, d, objetivo, dano);

            objetivo.Hp -= dano;

            AplicarComportamiento(stats, d, objetivo);
            AplicarHabilidadDeArma(stats, d, objetivo, dano);

            d.CooldownRestante = stats.CadenciaTicks;
        }
    }

    /// <summary>
    /// El efecto del perk de Oro o Único que tenga equipado la clase.
    /// </summary>
    /// <remarks>
    /// Solo uno se aplica: el de tier más alto. <b>Los comportamientos no se
    /// apilan</b> —"Ricochet dos veces" no quiere decir nada— y esa regla sale
    /// del tier, no de una lista de excepciones.
    /// </remarks>
    private void AplicarComportamiento(StatsEfectivos stats, Defensor defensor, Enemigo objetivo)
    {
        switch (stats.Comportamiento)
        {
            case EfectoPerk.Ninguno:
                return;

            case EfectoPerk.Ricochet:
                AplicarRicochet(objetivo, stats.Dano, stats.MagnitudComportamiento, rebotes: _datos.Combate.RebotesRicochet);
                return;

            case EfectoPerk.Perforacion:
                AplicarRicochet(objetivo, stats.Dano, stats.MagnitudComportamiento, rebotes: _datos.Combate.RebotesPerforacion);
                return;

            case EfectoPerk.Splash:
                AplicarSplash(objetivo, stats.Dano, stats.MagnitudComportamiento, ralentiza: false, maxSecundarios: _datos.Combate.MaxSecundariosSplashOro);
                return;

            case EfectoPerk.OndaDeFrio:
                AplicarSplash(objetivo, stats.Dano, stats.MagnitudComportamiento, ralentiza: true, maxSecundarios: _datos.Combate.MaxSecundariosOndaDeFrio);
                return;

            case EfectoPerk.Ralentiza:
                Ralentizar(objetivo, stats.MagnitudComportamiento);
                return;

            case EfectoPerk.Veneno:
            case EfectoPerk.TerribleHambre:
                // El veneno pega de una vez y no por tick: los efectos con
                // duración son la fase 3 y todavía no existe. Cuando exista, esto
                // se muda ahí, no se agrega otra fase.
                objetivo.Hp -= Enteros.MultiplicarPorcentaje(stats.Dano, stats.MagnitudComportamiento);

                if (stats.Comportamiento == EfectoPerk.TerribleHambre)
                    Ralentizar(objetivo, _datos.Combate.SlowTerribleHambre);

                return;

            case EfectoPerk.Penetrante:
                // Ignora la defensa. Como los enemigos todavía no la usan en el
                // cálculo de daño, por ahora es un golpe extra proporcional.
                objetivo.Hp -= Enteros.MultiplicarPorcentaje(stats.Dano, stats.MagnitudComportamiento / _datos.Combate.DivisorPenetrante);
                return;

            case EfectoPerk.IraDeDios:
            case EfectoPerk.TormentaDivina:
                // Únicos que combinan crítico y splash. Se resuelven como splash
                // reforzado: el radio es el mismo, el daño mucho mayor.
                AplicarSplash(objetivo, stats.Dano, stats.MagnitudComportamiento, ralentiza: false, maxSecundarios: _datos.Combate.MaxSecundariosDivino);
                return;

            case EfectoPerk.Apocalipsis:
                // Ricochet en cadena: rebota a los tres siguientes vivos.
                AplicarRicochet(objetivo, stats.Dano, stats.MagnitudComportamiento, rebotes: _datos.Combate.RebotesApocalipsis);
                return;

            case EfectoPerk.GolpeCritico:
                // Cada 7.º golpe DE ESTE defensor (propuesta V2). Sigue sin tirar
                // el dado —una tirada acá gastaría RNG por disparo y correría los
                // drops—, pero ya no es Tick % 7 global: con eso criticaban todos
                // juntos, y las cadencias múltiplo de 7 criticaban siempre o nunca.
                if (defensor.ContadorGolpes % _datos.Combate.GolpesPorCritico == 0)
                    objetivo.Hp -= Enteros.MultiplicarPorcentaje(stats.Dano, stats.MagnitudComportamiento);

                return;
        }
    }

    /// <summary>
    /// Le baja la velocidad a un enemigo.
    /// </summary>
    /// <remarks>
    /// Permanente mientras viva, no con duración, porque los efectos con
    /// vencimiento son la fase 3 del tick y todavía no existe. Cuando exista, el
    /// cambio va ahí y no acá.
    /// </remarks>
    /// <summary>
    /// Las habilidades que escalan el daño del golpe, antes de aplicarlo.
    /// </summary>
    /// <remarks>
    /// Van separadas de las que agregan efectos porque el orden importa: el
    /// splash tiene que salir del daño <b>ya escalado</b>, o un francotirador con
    /// splash haría el bonus solo al objetivo principal.
    /// </remarks>
    private int AjustarDanoPorHabilidad(
        StatsEfectivos stats, Defensor defensor, Enemigo objetivo, int dano)
    {
        switch (stats.Habilidad)
        {
            case HabilidadArma.Francotirador:
            case HabilidadArma.Carnicero:
            {
                // Cuánto del alcance está usando el disparo, en porcentaje. Se
                // mide en celdas al cuadrado —que es como se mide todo acá— y se
                // compara contra el alcance al cuadrado: no hace falta raíz.
                var celda = _ruta.CeldaEn(objetivo.DistanciaRecorrida);

                long d2 = Enteros.DistanciaCuadrado(
                    defensor.Celda.X, defensor.Celda.Y, celda.X, celda.Y);

                long alcance2 = (long)stats.AlcanceCeldas * stats.AlcanceCeldas;

                if (alcance2 <= 0)
                    return dano;

                int porcion = (int)System.Math.Min(100, d2 * 100 / alcance2);

                // Francotirador premia lo lejano, carnicero lo cercano. Son la
                // misma cuenta con el porcentaje dado vuelta, y por eso van juntas.
                int factor = stats.Habilidad == HabilidadArma.Francotirador
                    ? porcion
                    : 100 - porcion;

                return dano + Enteros.MultiplicarPorcentaje(dano, stats.MagnitudHabilidad * factor / 100);
            }

            case HabilidadArma.Sed:
                // Sube con cada muerte de la oleada y se reinicia al terminarla.
                // Techo 100% (propuesta V2): con oleadas de 60 bichos, el tope de
                // 300 se alcanzaba siempre y Sed era un ×4 garantizado.
                return dano + Enteros.MultiplicarPorcentaje(
                    dano, System.Math.Min(_datos.Combate.TopeBonusSed, defensor.MuertesEnLaOleada * stats.MagnitudHabilidad));

            case HabilidadArma.Critico:
                // Cada 7.º golpe DE ESTE defensor (propuesta V2), igual que el
                // perk. Sin dado, sin sincronización global.
                return defensor.ContadorGolpes % _datos.Combate.GolpesPorCritico == 0
                    ? Enteros.MultiplicarPorcentaje(dano, stats.MagnitudHabilidad)
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
    private void AplicarHabilidadDeArma(
        StatsEfectivos stats, Defensor defensor, Enemigo objetivo, int dano)
    {
        switch (stats.Habilidad)
        {
            case HabilidadArma.Splash:
                AplicarSplash(objetivo, dano, stats.MagnitudHabilidad, ralentiza: false, maxSecundarios: _datos.Combate.MaxSecundariosSplashArma);
                return;

            case HabilidadArma.Rebote:
                AplicarRicochet(objetivo, dano, stats.MagnitudHabilidad, rebotes: _datos.Combate.RebotesReboteArma);
                return;

            case HabilidadArma.Escarcha:
                Ralentizar(objetivo, stats.MagnitudHabilidad);
                return;

            case HabilidadArma.Veneno:
                objetivo.Hp -= Enteros.MultiplicarPorcentaje(dano, stats.MagnitudHabilidad);
                return;

            case HabilidadArma.Doble:
                // Un segundo golpe completo. Es la habilidad más simple y de las
                // más fuertes: duplica todo lo que el defensor haga, incluidos los
                // perks numéricos.
                objetivo.Hp -= dano;
                return;

            case HabilidadArma.Ejecutar:
            {
                // Umbrales de la propuesta V2: tropa hasta 20%, jefe hasta 5%, y
                // el JEFE FINAL es inmune — matarlo es cómo se gana la corrida,
                // y ganarla salteándole un cuarto de vida vaciaba el final.
                if (_datos.Combate.JefeFinalInmuneAEjecutar && objetivo.EsJefe && Oleada >= _datos.Curvas.Corrida.OleadasPorCorrida)
                    return;

                if (objetivo.Vivo && objetivo.HpMaximo > 0)
                {
                    int umbral = System.Math.Min(
                        stats.MagnitudHabilidad, objetivo.EsJefe ? _datos.Combate.UmbralEjecutarJefe : _datos.Combate.UmbralEjecutarTropa);

                    int porcentaje = objetivo.Hp * 100 / objetivo.HpMaximo;

                    if (porcentaje <= umbral)
                        objetivo.Hp = 0;
                }

                return;
            }

            case HabilidadArma.Cadena:
            {
                // Le pega a hasta 5 secundarios en alcance (propuesta V2). Sin el
                // techo, el daño escalaba lineal con la densidad y una oleada
                // apretada lo multiplicaba por diez.
                long alcance2 = (long)stats.AlcanceCeldas * stats.AlcanceCeldas;
                int extra = Enteros.MultiplicarPorcentaje(dano, stats.MagnitudHabilidad);
                int alcanzados = 0;

                foreach (var otro in _enemigos)
                {
                    if (alcanzados >= _datos.Combate.MaxSecundariosCadena)
                        break;

                    if (!otro.Vivo || ReferenceEquals(otro, objetivo))
                        continue;

                    var celda = _ruta.CeldaEn(otro.DistanciaRecorrida);

                    long d2 = Enteros.DistanciaCuadrado(
                        defensor.Celda.X, defensor.Celda.Y, celda.X, celda.Y);

                    if (d2 <= alcance2)
                    {
                        otro.Hp -= extra;
                        alcanzados++;
                    }
                }

                return;
            }

            case HabilidadArma.Vampirismo:
                // Cura la Torre — con techo en su vida máxima y UN proc por clase
                // cada 30 ticks (propuesta V2). El arma es de la clase: sin la
                // ventana, diez unidades con la misma arma curaban cientos por
                // segundo y la Torre se volvía inmortal.
                if (!_ultimaCuraPorClase.TryGetValue(defensor.Clase, out int ultima) ||
                    Tick - ultima >= _datos.Combate.CooldownVampirismoTicks)
                {
                    Torre.Hp = System.Math.Min(Torre.HpMaximo, Torre.Hp + stats.MagnitudHabilidad);
                    _ultimaCuraPorClase[defensor.Clase] = Tick;
                }

                return;

            case HabilidadArma.Codicia:
                // El oro extra se cobra en la fase de muertes, cuando se sepa si
                // este golpe mató. La marca EXPIRA a los 60 ticks (propuesta V2):
                // marcada para siempre, un tanque golpeado una vez era alcancía
                // aunque lo matara otro, minutos después.
                objetivo.OroExtraPorciento = stats.MagnitudHabilidad;
                objetivo.OroExtraExpiraTick = Tick + _datos.Combate.DuracionMarcaCodiciaTicks;
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
    private void AplicarRicochet(Enemigo objetivo, int danoBase, int magnitud, int rebotes)
    {
        long ventanaRuta = (long)_datos.Combate.VentanaSaltoRicochetCeldas * Enteros.MilesimasPorCelda;

        int dano = Enteros.MultiplicarPorcentaje(danoBase, magnitud);
        var anterior = objetivo;

        // Los ya golpeados en ESTA cadena, para no rebotar dos veces al mismo.
        // Lista y no HashSet: son 1-3 elementos.
        var golpeados = new List<Enemigo> { objetivo };

        while (rebotes > 0)
        {
            Enemigo? proximo = null;

            foreach (var otro in _enemigos)
            {
                if (!otro.Vivo || golpeados.Contains(otro))
                    continue;

                long diferencia = System.Math.Abs(
                    otro.DistanciaRecorrida - anterior.DistanciaRecorrida);

                if (diferencia <= ventanaRuta)
                {
                    proximo = otro;
                    break;
                }
            }

            if (proximo is null)
                break;

            proximo.Hp -= dano;
            golpeados.Add(proximo);
            anterior = proximo;
            rebotes--;
        }
    }

    /// <summary>
    /// Daña a los que están cerca del objetivo.
    /// </summary>
    /// <remarks>
    /// "Cerca" se mide <b>en la ruta</b>, no en el mapa, y da lo mismo porque
    /// todos caminan por ella. Comparar distancia recorrida es una resta;
    /// comparar posiciones serían dos conversiones y una distancia al cuadrado por
    /// enemigo.
    /// </remarks>
    /// <param name="maxSecundarios">
    /// Techo de alcanzados (propuesta V2): Oro 4, Único 6, Cadena 5. Sin techo,
    /// la densidad multiplicaba el daño 5-10× en oleadas apretadas y el splash
    /// escalaba solo con que hubiera más bichos.
    /// </param>
    private void AplicarSplash(
        Enemigo objetivo, int danoBase, int magnitud, bool ralentiza, int maxSecundarios)
    {
        long radio = (long)_datos.Combate.RadioSplashCeldas * Enteros.MilesimasPorCelda;

        int dano = Enteros.MultiplicarPorcentaje(danoBase, magnitud);
        int alcanzados = 0;

        foreach (var otro in _enemigos)
        {
            if (alcanzados >= maxSecundarios)
                break;

            if (!otro.Vivo || ReferenceEquals(otro, objetivo))
                continue;

            long diferencia = System.Math.Abs(otro.DistanciaRecorrida - objetivo.DistanciaRecorrida);

            if (diferencia > radio)
                continue;

            otro.Hp -= dano;
            alcanzados++;

            if (ralentiza)
                Ralentizar(otro, magnitud);
        }
    }

    /// <summary>
    /// Aplica una ralentización TEMPORAL (propuesta V2).
    /// </summary>
    /// <remarks>
    /// <para>Tres reglas: <b>no apila</b> —prevalece la potencia más fuerte—,
    /// <b>refresca</b> los 90 ticks en cada aplicación, y tiene <b>techo en
    /// 60%</b>. Antes era permanente, apilaba hasta -80% y dos fuentes de
    /// Escarcha dejaban la corrida en cámara lenta para siempre.</para>
    /// </remarks>
    private void Ralentizar(Enemigo enemigo, int magnitud)
    {
        int potencia = System.Math.Min(magnitud, _datos.Combate.TopeSlowPorciento);

        if (potencia <= 0)
            return;

        enemigo.SlowPorciento = System.Math.Max(enemigo.SlowPorciento, potencia);
        enemigo.SlowExpiraTick = Tick + _datos.Combate.DuracionSlowTicks;
    }

    /// <summary>
    /// A quién le pega un defensor: <b>al más adelantado que tenga en alcance</b>.
    /// </summary>
    /// <remarks>
    /// Empate por <see cref="Enemigo.IdSpawn"/> menor. Los empates son
    /// frecuentes —dos bichos iguales spawneados con un tick de diferencia van
    /// juntos toda la ruta— así que sin una regla explícita el objetivo lo
    /// decidiría el orden de la lista, que es justo lo que no puede decidir
    /// nada.
    /// </remarks>
    private Enemigo? ElegirObjetivo(Defensor defensor, int alcanceCeldas)
    {
        long alcanceCuadrado = (long)alcanceCeldas * alcanceCeldas;

        // La grilla devuelve candidatos por cercanía, ya ordenados por id de
        // spawn. Sigue habiendo que comparar la distancia: el rectángulo de
        // celdas es más grande que el círculo de alcance.
        _grilla.Candidatos(defensor.Celda, alcanceCeldas, _candidatos);

        Enemigo? mejor = null;

        foreach (int i in _candidatos)
        {
            var e = _enemigos[i];

            if (!e.Vivo)
                continue;

            var celda = _ruta.CeldaEn(e.DistanciaRecorrida);
            long d2 = Enteros.DistanciaCuadrado(
                defensor.Celda.X, defensor.Celda.Y, celda.X, celda.Y);

            if (d2 > alcanceCuadrado)
                continue;

            if (mejor is null || e.DistanciaRecorrida > mejor.DistanciaRecorrida)
            {
                mejor = e;
            }
            // El empate se resuelve por id de spawn menor. Como los candidatos
            // vienen en orden de spawn, el que llegó primero es el que ya está en
            // `mejor`: no hacer nada ES la regla.
        }

        return mejor;
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
            if (e.Vivo)
                continue;

            // El oro extra de Codicia se cobra ACÁ, no al golpear: solo cuenta si
            // el golpe efectivamente mató.
            Oro += Enteros.ConPorcentajes(e.Oro, e.OroExtraPorciento);

            TirarDrop(e);
        }

        // La habilidad Sed cuenta muertes por defensor. Se atribuyen al que tenga
        // al enemigo en alcance, que es una aproximación —no se sabe quién dio el
        // golpe final— pero es determinista y barata, que es lo que importa.
        ContarMuertesParaSed();

        // RemoveAll es estable: conserva el orden relativo de los que quedan.
        _enemigos.RemoveAll(e => !e.Vivo);
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
            var stats = Roster.StatsDe(d.Clase, Mochila);

            if (stats.Habilidad != HabilidadArma.Sed)
                continue;

            long alcance2 = (long)stats.AlcanceCeldas * stats.AlcanceCeldas;

            foreach (var e in _enemigos)
            {
                if (e.Vivo)
                    continue;

                var celda = _ruta.CeldaEn(e.DistanciaRecorrida);

                long dist2 = Enteros.DistanciaCuadrado(
                    d.Celda.X, d.Celda.Y, celda.X, celda.Y);

                if (dist2 <= alcance2)
                    d.MuertesEnLaOleada++;
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
    private void TirarDrop(Enemigo enemigo)
    {
        bool esJefe = enemigo.EsJefe;
        var clases = Roster.Disponibles();

        if (clases.Count == 0)
            return;

        if (esJefe)
        {
            var oro = _datos.Perks.Tirar(TierPerk.Oro, clases, _rng);
            if (oro is not null && !Mochila.GuardarPerk(oro.Id))
            {
                // Mochila llena: se pierde. La UI de verdad va a abrir el diálogo
                // de "destruí uno de los 15 o el entrante" antes de llegar acá.
            }

            // El jefe suelta además una pieza de equipo, con presupuesto atado a
            // la oleada: así un ítem de la 80 es mejor que uno de la 10 sin que
            // haya que escribir listas por tramo.
            var arma = GeneradorArmas.Generar(
                _datos.Curvas.PorOleada.PresupuestoDropJefe.En(Oleada),
                clases, _rng, Mochila.ProximoIdArma, _datos.Armas);

            int idArma = Mochila.RegistrarArma(arma);
            Mochila.Guardar(new CosaGuardada(TipoCosa.Arma, idArma));
            return;
        }

        // El bicho común tira bronce o plata, con poca chance.
        if (!_rng.Ocurre(_datos.Curvas.Drops.ChanceComun))
            return;

        if (_rng.Ocurre(_datos.Curvas.Drops.ChanceBronce))
        {
            var bronce = _datos.Perks.Tirar(TierPerk.Bronce, clases, _rng);
            if (bronce is not null)
                Mochila.GuardarPerk(bronce.Id);
        }
        else
        {
            var plata = _datos.Perks.Tirar(TierPerk.Plata, clases, _rng);
            if (plata is not null)
                Mochila.GuardarPerk(plata.Id);
        }
    }

    // =========================================================================
    //  Fase 9 — Chequeos de fin
    // =========================================================================

    private void FaseChequeos()
    {
        if (!Torre.EnPie)
        {
            Estado = EstadoPartida.Perdio;
            return;
        }

        bool oleadaLimpia = _enemigos.Count == 0 && _porSpawnear.Count == 0;

        if (!oleadaLimpia)
            return;

        if (Oleada >= _datos.Curvas.Corrida.OleadasPorCorrida)
        {
            // ⚠ Se gana matando al jefe de la 100, no "sobreviviendo". La
            // corrida TERMINA acá: no hay modo sin fin y no hay área 9.
            Estado = EstadoPartida.Gano;
            return;
        }

        // La elección frena la corrida: no se puede cerrar sin elegir. Frenarla
        // acá y no al empezar la oleada siguiente le da al jugador el momento de
        // calma para decidir, que es cuando la decisión se piensa.
        if (EsperandoEleccion)
            return;

        if (Oleada - _oleadaUltimaEleccion >= _datos.Curvas.Corrida.OleadasEntreElecciones)
        {
            var opciones = ArmadorEleccion.Siguiente(Roster, _rng, _datos);
            _oleadaUltimaEleccion = Oleada;

            if (opciones.Length > 0)
            {
                OpcionesAbiertas = opciones;
                return;
            }

            // Sin opciones —roster completo y todo en nivel 6— se sigue de largo.
            // Es un final de progresión legítimo, no un error.
        }

        // ⚠ Acá NO se lanza sola. Se abre la fase de preparación y se espera.
        //
        // Antes bajaba un contador de 15 segundos y la oleada entraba sola. Con la
        // preparación en pausa, quien la arranca es el jugador: la acción
        // `AdelantarOleada` es el botón «comenzar oleada», y la interfaz la manda
        // igual cuando se acaban los 30 segundos de reloj de pared.
        ArmarProximaOleada();
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
        Oro += _bonusOroOleada;
        _bonusOroOleada = 0;

        // ⚠ Se arma con `Oleada + 1` y el contador NO se toca todavía.
        //
        // Mientras dura la preparación, `Oleada` sigue diciendo la que se jugó. Si
        // se incrementara acá, una corrida abandonada en la preparación reportaría
        // al ranking una oleada que nunca se jugó.
        var receta = Balance.RecetaDe(Oleada + 1, _datos);
        _oleadaArmada = ArmadorOleadas.Armar(receta, _rng, _datos);

        Tienda.Renovar(Oleada + 1, Roster, Mochila, _rng, _datos);

        EsperandoInicioDeOleada = true;
    }

    /// <summary>
    /// Suelta la oleada ya armada. Cierra la preparación y arranca el reloj.
    /// </summary>
    private void LanzarProximaOleada()
    {
        if (_oleadaArmada is not { } armada)
            return;

        Oleada++;

        // ⚠ La Sed se reinicia con cada oleada. Sin esto el contador crecería
        // durante las 100 y el bonus sería infinito: el techo tiene que ser
        // cuánto se puede matar de una vez, no cuánto se lleva matado.
        foreach (var d in _defensores)
            d.MuertesEnLaOleada = 0;

        foreach (var compra in armada.Npcs)
            _porSpawnear.Enqueue(compra);

        _intervaloSpawn = armada.IntervaloSpawnTicks;
        _bonusOroOleada = armada.BonusOro;
        _ticksParaProximoSpawn = 0;
        _oleadaArmada = null;

        EsperandoInicioDeOleada = false;
    }

    /// <summary>
    /// Arranca la corrida lanzando la oleada 1.
    /// </summary>
    public void Empezar()
    {
        // ⚠ Acá NO se entrega oro: el inicial son dos fichas de la clase que
        // toque, y eso se sabe recién al elegirla. Ver Elegir().
        Oro = 0;

        // 1 de 3 al empezar. La corrida arranca con la pantalla de elección
        // abierta y el roster vacío: no se puede poner nada hasta elegir.
        OpcionesAbiertas = ArmadorEleccion.Inicial(_rng, _datos);

        LanzarProximaOleada();
    }
}
