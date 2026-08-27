using System.Collections.Generic;
using System.Globalization;
using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Corre corridas sin render y reporta qué pasó.
/// </summary>
/// <remarks>
/// <para>Sale casi gratis porque la simulación ya es determinista y no depende
/// del render: no hay nada que simular "de mentira", es la misma
/// <see cref="Partida"/> que corre el juego.</para>
///
/// <para>Sirve para dos cosas distintas:</para>
///
/// <list type="number">
/// <item><b>Verificar el determinismo.</b> Misma seed y mismas acciones tienen
/// que dar el mismo número de ticks, exacto. Es el chequeo del que cuelga todo
/// lo demás: si esto falla, el anticheat del servidor acusa a jugadores
/// honestos.</item>
/// <item><b>Balancear.</b> Cien oleadas no se pueden ajustar jugándolas a mano
/// doscientas veces. Con esto se corren N corridas con una política scripteada y
/// se mira hasta dónde llegan.</item>
/// </list>
///
/// <para>Se invoca headless, sin abrir ventana:</para>
/// <code>
/// Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --determinismo
/// Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --balance 20
/// </code>
/// </remarks>
public static class CorredorHeadless
{
    /// <summary>
    /// Techo de ticks por corrida, para que un bug de "nunca termina" corte en
    /// vez de colgar el proceso. Una corrida de 100 oleadas ronda las tres horas,
    /// o sea ~324.000 ticks; el doble deja margen de sobra.
    /// </summary>
    private const int TechoTicks = 700_000;

    /// <summary>
    /// Mira los argumentos y corre lo que corresponda.
    /// </summary>
    /// <returns><c>true</c> si se pidió una herramienta y ya se ejecutó.</returns>
    public static bool AtenderArgumentos()
    {
        string[] args = OS.GetCmdlineUserArgs();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--determinismo":
                    VerificarDeterminismo();
                    return true;

                case "--importar-npcs":
                    ImportadorNpcs.Correr();
                    return true;

                case "--importar-apariencia":
                    ImportadorApariencia.Correr();
                    return true;

                case "--traer-item":
                    TraerGrafico.TraerItem(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int it)) ? it : 0);
                    return true;

                case "--traer-razas":
                    TraerGrafico.TraerRazas();
                    return true;

                case "--traer-armaduras-bajas":
                    TraerGrafico.TraerArmadurasBajas();
                    return true;

                case "--traer-arte":
                    TraerGrafico.TraerArteDeClases(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int so)) ? so : 0);
                    return true;

                case "--traer-tiers":
                    TraerGrafico.TraerTiers();
                    return true;

                case "--listar-razas":
                    InspectorGraficos.ListarRazas();
                    return true;

                case "--verificar-arte":
                    VerificarArte.Correr();
                    return true;

                case "--presupuesto-arte":
                    InspectorGraficos.Budget();
                    return true;

                case "--traer-piso":
                    TraerGrafico.TraerPiso(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int ph)) ? ph : 0,
                        i + 2 < args.Length ? args[i + 2] : "",
                        (i + 3 < args.Length && int.TryParse(args[i + 3], out int pb)) ? pb : 0);
                    return true;

                case "--traer-tileset":
                    TraerGrafico.TraerTileset(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int th)) ? th : 0,
                        i + 2 < args.Length ? args[i + 2] : "");
                    return true;

                case "--exportar-balance":
                    ExportadorBalance.Correr(i + 1 < args.Length ? args[i + 1] : "");
                    return true;

                case "--muestrear-piso":
                    MuestrearPiso.Correr(
                        i + 1 < args.Length ? args[i + 1] : "piso.png",
                        (i + 2 < args.Length && ulong.TryParse(args[i + 2], out ulong sp)) ? sp : 20260825UL,
                        (i + 3 < args.Length && int.TryParse(args[i + 3], out int sa)) ? sa : 0);
                    return true;

                case "--verificar-tilesets":
                    VerificarTilesets.Correr(
                        i + 1 < args.Length ? args[i + 1] : "tilesets.png");
                    return true;

                case "--grilla-tileset":
                    InspectorGraficos.GrillaDeTileset(i + 1 < args.Length ? args[i + 1] : "");
                    return true;

                case "--muestrear-bloques":
                    InspectorGraficos.MuestrearBloques(
                        i + 1 < args.Length ? args[i + 1] : "",
                        i + 2 < args.Length ? args[i + 2] : "bloques.png");
                    return true;

                case "--muestrear-tilesets":
                    InspectorGraficos.MuestrearTilesets(
                        i + 1 < args.Length ? args[i + 1] : "",
                        i + 2 < args.Length ? args[i + 2] : "muestra.png");
                    return true;

                case "--clasificar-tilesets":
                    InspectorGraficos.ClasificarTilesets(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int mt)) ? mt : 64);
                    return true;

                case "--tilesets":
                    InspectorGraficos.Tilesets();
                    return true;

                case "--presupuesto-bestiario":
                    InspectorGraficos.PresupuestoBestiario();
                    return true;

                // Sin número trae el área 1: Ullathorpe es el único bestiario que
                // hace falta hoy, y el default no puede ser «las ocho» porque eso
                // son 97 MB de un tirón.
                case "--traer-bestiario":
                    TraerGrafico.TraerBestiario(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int ar)) ? ar : 1);
                    return true;

                case "--olvidar":
                    TraerGrafico.Olvidar(i + 1 < args.Length ? args[i + 1] : "");
                    return true;

                case "--verificar-indices":
                    InspectorGraficos.VerificarIndices();
                    return true;

                case "--verificar-lector":
                    InspectorGraficos.VerificarLector();
                    return true;

                case "--inspeccionar-grh":
                    InspectorGraficos.InspeccionarGrh(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int g)) ? g : 1);
                    return true;

                case "--inspeccionar-obj":
                    InspectorGraficos.InspeccionarObjeto(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int o)) ? o : 1);
                    return true;

                case "--inspeccionar-tiers":
                    InspectorGraficos.InspeccionarTiers();
                    return true;

                case "--generar-layouts":
                    GeneradorLayouts.Correr();
                    return true;

                case "--probar-layouts":
                    GeneradorLayouts.Probar(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int pl)) ? pl : 5);
                    return true;

                case "--validar-layouts":
                    GeneradorLayouts.Validar();
                    return true;

                case "--prueba-guardado":
                    ProbarGuardadoEnPreparacion();
                    return true;

                case "--validar":
                    ValidadorDatos.Correr();
                    return true;

                case "--traza":
                    ulong semilla = (i + 1 < args.Length && ulong.TryParse(args[i + 1], out ulong s)) ? s : 1;
                    Trace(semilla);
                    return true;

                case "--balance":
                    int corridas = (i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) ? n : 10;
                    Balance(corridas);
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// La prueba que sostiene el anticheat: <b>misma seed, mismas acciones,
    /// mismo resultado</b>.
    /// </summary>
    /// <remarks>
    /// <para>Se corre cada seed dos veces y se comparan cuatro números: ticks,
    /// oleada alcanzada, HP de la Torre y oro. Comparar solo los ticks dejaría
    /// pasar una divergencia que termina igual de larga por casualidad.</para>
    ///
    /// <para>El log de acciones es el mismo objeto en las dos pasadas, así que
    /// también verifica que re-simular un log dé el mismo resultado — que es
    /// exactamente lo que hace el servidor al revalidar, y lo que hace el juego
    /// al cargar una partida guardada.</para>
    /// </remarks>
    private static void VerificarDeterminismo()
    {
        GD.Print("── Prueba de determinismo ─────────────────────────");

        ulong[] seeds = [1, 42, 20260825, 999_999_937, ulong.MaxValue / 3];
        int fallas = 0;

        foreach (ulong seed in seeds)
        {
            var a = Correr(seed, ConAcciones(seed));
            var b = Correr(seed, ConAcciones(seed));

            bool igual =
                a.Ticks == b.Ticks &&
                a.Wave == b.Wave &&
                a.HpTorre == b.HpTorre &&
                a.Gold == b.Gold;

            if (igual)
            {
                GD.Print($"  ✓ seed {seed,-12}  {a.Ticks,7} ticks · oleada {a.Wave,3} · " +
                         $"torre {a.HpTorre,5} · oro {a.Gold}");
            }
            else
            {
                fallas++;
                GD.PrintErr($"  ✗ seed {seed} DIVERGE");
                GD.PrintErr($"      pasada 1: {a.Ticks} ticks · oleada {a.Wave} · torre {a.HpTorre} · oro {a.Gold}");
                GD.PrintErr($"      pasada 2: {b.Ticks} ticks · oleada {b.Wave} · torre {b.HpTorre} · oro {b.Gold}");
            }
        }

        // Segunda prueba: que dos seeds distintas den corridas distintas. Un
        // generador roto que devuelve siempre el mismo número pasaría la prueba
        // de arriba con honores.
        var x = Correr(7, new ActionLog());
        var y = Correr(8, new ActionLog());

        if (x.Ticks == y.Ticks && x.HpTorre == y.HpTorre && x.Gold == y.Gold)
        {
            fallas++;
            GD.PrintErr("  ✗ dos seeds distintas dieron corridas idénticas: el RNG no está sembrando");
        }
        else
        {
            GD.Print("  ✓ seeds distintas dan corridas distintas");
        }

        fallas += VerificarIdaYVueltaDelLog();

        GD.Print(fallas == 0
            ? "── DETERMINISTA ───────────────────────────────────"
            : $"── {fallas} FALLA(S) ─────────────────────────────────");
    }

    /// <summary>
    /// El log serializado tiene que re-simular idéntico.
    /// </summary>
    /// <remarks>
    /// <para><b>Esto es literalmente lo que hace el servidor al revalidar</b>, y
    /// también lo que hace el juego al cargar una partida guardada: toma la seed y
    /// el log, y re-simula desde el tick 0.</para>
    ///
    /// <para>Es una prueba distinta de la de arriba, aunque se parezca. Aquella
    /// verifica que la simulación sea determinista; esta verifica que el
    /// <b>formato</b> no pierda nada por el camino. Un varint mal truncado o un
    /// zigzag con el signo al revés pasaría la primera sin problemas.</para>
    /// </remarks>
    private static int VerificarIdaYVueltaDelLog()
    {
        const ulong seed = 20260825;

        // Primera pasada: se juega y se acumula el log.
        var original = new ActionLog();
        var a = Correr(seed, original);

        byte[] blob = BinaryLog.Escribir(original);
        var recuperado = BinaryLog.Leer(blob);

        if (recuperado.Count != original.Count)
        {
            GD.PrintErr($"  ✗ el log perdió acciones: {original.Count} → {recuperado.Count}");
            return 1;
        }

        for (int i = 0; i < original.Count; i++)
        {
            if (original.All[i] != recuperado.All[i])
            {
                GD.PrintErr($"  ✗ la acción {i} cambió al serializar: " +
                            $"{original.All[i]} → {recuperado.All[i]}");
                return 1;
            }
        }

        // Segunda pasada: se re-simula con el log recuperado y SIN política. Si el
        // resultado coincide, el log solo alcanza para reproducir la corrida — que
        // es la premisa entera del anticheat.
        var partida = new Run(seed, Layouts.Choose(seed, CargadorDatos.Actual), recuperado, CargadorDatos.Actual);
        partida.Empezar();

        while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
        {
            partida.Advance();

            // El log se agotó con la corrida esperando al jugador. Sin este corte
            // el bucle gira para siempre: en pausa el tick no avanza.
            if (partida.EsperaAlJugador)
                break;
        }

        bool igual =
            partida.Tick == a.Ticks &&
            partida.Wave == a.Wave &&
            partida.Tower.Hp == a.HpTorre &&
            partida.Gold == a.Gold;

        if (!igual)
        {
            GD.PrintErr("  ✗ re-simular el log guardado da otra corrida");
            GD.PrintErr($"      jugada    : {a.Ticks} ticks · oleada {a.Wave} · torre {a.HpTorre} · oro {a.Gold}");
            GD.PrintErr($"      re-simulada: {partida.Tick} ticks · oleada {partida.Wave} · " +
                        $"torre {partida.Tower.Hp} · oro {partida.Gold}");
            return 1;
        }

        int bytesPorAccion = original.Count == 0 ? 0 : blob.Length / original.Count;

        GD.Print($"  ✓ log re-simula idéntico · {original.Count} acciones en " +
                 $"{blob.Length} bytes ({bytesPorAccion} B/acción)");

        return 0;
    }

    /// <summary>
    /// Corre N corridas y reporta hasta dónde llegó cada una.
    /// </summary>
    /// <remarks>
    /// Con una política de juego fija: el mismo jugador imaginario en todas las
    /// seeds. Lo que varía es lo que decide la seed —composición de la oleada,
    /// perks de los NPCs— así que la dispersión del resultado mide cuánto pesa
    /// la suerte contra la habilidad.
    /// </remarks>
    private static void Balance(int corridas)
    {
        GD.Print($"── Balance · {corridas} corridas ──────────────────────");

        int suma = 0, peor = int.MaxValue, mejor = 0;
        int sumaOro = 0, peorOro = int.MaxValue, mejorOro = 0;

        for (int i = 0; i < corridas; i++)
        {
            ulong seed = (ulong)(i + 1) * 2_654_435_761UL;
            var r = Correr(seed, ConAcciones(seed));

            suma += r.Wave;
            if (r.Wave < peor) peor = r.Wave;
            if (r.Wave > mejor) mejor = r.Wave;

            sumaOro += r.Gold;
            if (r.Gold < peorOro) peorOro = r.Gold;
            if (r.Gold > mejorOro) mejorOro = r.Gold;

            GD.Print($"  seed {seed,-22} oleada {r.Wave,3} · {r.Ticks,7} ticks · " +
                     $"{r.Defenders,2} def · {r.ClasesRoster} clases · mochila {r.Backpack,2} · " +
                     $"oro {r.Gold,6} · {r.State}");
        }

        GD.Print($"── oleada:  peor {peor} · media {suma / corridas} · mejor {mejor} ──");

        // ⚠ EL ORO SIN GASTAR ES LA MEDICIÓN QUE PIDE Economia_Propuesta.
        //
        // La propuesta difiere los ajustes finos de economía hasta tener este
        // número: si el jugador termina la corrida con oro para dos o tres
        // compras completas de tienda sin haber sacrificado defensa, el ingreso
        // sobra y hay que recortarlo más. Con la mochila llena y el tablero
        // completo, el oro deja de ser un recurso y pasa a ser un marcador.
        //
        // Es el oro AL MORIR, así que un número alto en corridas cortas dice otra
        // cosa: que el jugador imaginario no supo en qué gastarlo, no que sobre.
        GD.Print($"── oro sin gastar al terminar:  peor {peorOro} · media {sumaOro / corridas} · mejor {mejorOro} ──");
    }

    internal readonly record struct Resultado(int Ticks, int Wave, int HpTorre, int Gold, RunState State, int Defenders, int ClasesRoster, int Backpack);

    /// <summary>
    /// Corre una sola corrida contando lo que pasa adentro.
    /// </summary>
    /// <remarks>
    /// Existe porque tres veces seguidas razonar sobre el balance dio una
    /// conclusión equivocada y medirlo dio la correcta. El agregado del corredor
    /// dice <i>hasta dónde llegó</i>; esto dice <b>por qué</b>.
    /// </remarks>
    private static void Trace(ulong seed)
    {
        var datos = CargadorDatos.Actual;
        var log = new ActionLog();
        var partida = new Run(seed, Layouts.Choose(seed, datos), log, datos);
        partida.Empezar();

        var pendientes = new Queue<(int Tick, Cell Cell)>(ColocacionesPlaneadas(seed, partida.Map));

        GD.Print($"── Traza · seed {seed} ────────────────────────────");
        GD.Print($"  ruta: {partida.Route.Length / Ints.ThousandthsPerCell} celdas");

        int oleadaAnterior = 0;
        int ultimoAviso = 0;

        while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
        {
            if (partida.WaitingForChoice)
                log.Agregar(PlayerAction.Choose(partida.TickDeAccion, MejorOpcion(partida)));

            // ⚠ La preparación se USA, no se saltea.
            //
            // Es la fase donde el jugador acomoda el tablero con el reloj
            // detenido, así que un corredor que apreta comenzar al instante mide
            // un juego que nadie juega. Se pone todo lo que el oro permita y
            // recién ahí se arranca.
            //
            // Va de a UNO por vuelta a propósito: `Oro` y `PuedePoner` no cambian
            // hasta que `Avanzar` aplique la acción, así que dos colocaciones en
            // la misma vuelta se decidirían con el oro viejo. Como el reloj está
            // congelado, las vueltas de más no cuestan ticks.
            if (partida.WaitingForWaveStart)
            {
                // ⚠ El orden importa: primero el tablero, después la tienda. Al
                // revés, la tienda se lleva el oro de la próxima colocación y el
                // jugador imaginario termina con la mochila llena y el mapa
                // vacío — que es peor que cualquiera de las dos cosas sola.
                //
                // Se arranca la oleada recién cuando no queda nada por hacer.
                bool puso = ColocarUnoEnPreparacion(partida, log, pendientes);
                bool compro = !puso && ComprarLoQuePueda(partida, log);

                if (!puso && !compro)
                {
                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.CallNextWave, 0, 0, 0));
                }

                EquiparLoQuePueda(partida, log);
                partida.Advance();
                continue;
            }

            if (pendientes.Count > 0)
            {
                var (tick, celda) = pendientes.Peek();

                if (partida.Tick >= tick)
                {
                    var clase = ClaseMasCara(partida);

                    if (clase is not null && partida.PuedePoner(clase.Value, celda))
                    {
                        log.Agregar(PlayerAction.Place(partida.TickDeAccion, clase.Value, celda));
                        pendientes.Dequeue();
                    }
                }
            }

            EquiparLoQuePueda(partida, log);
            partida.Advance();

            if (partida.Wave != oleadaAnterior)
            {
                oleadaAnterior = partida.Wave;
                ultimoAviso = partida.Tick;

                // El HP va dividido por la escala interna: los avisos de progreso
                // los lee una persona, y «torre 100000» no dice nada.
                GD.Print($"  ol {partida.Wave,3} · t {partida.Tick,7} · " +
                         $"torre {Ints.ToPoints(partida.Tower.Hp),5} · oro {partida.Gold,6} · " +
                         $"{partida.Defenders.Count,2} def · {partida.Enemies.Count,3} vivos");
            }

            // Si una oleada se estira mucho, se dice qué hay en pantalla. Es el
            // síntoma de "el jugador no puede matar lo que viene", que de otro
            // modo solo se ve como un número de ticks alto al final.
            if (partida.Tick - ultimoAviso > 3000)
            {
                ultimoAviso = partida.Tick;

                var clases = new System.Text.StringBuilder();
                foreach (var f in partida.Roster.Classes)
                    clases.Append(CultureInfo.InvariantCulture, $"{f.DefenderClass}(nv{f.Level}) ");

                GD.Print($"     ↳ atascado en ol {partida.Wave} · t {partida.Tick} · " +
                         $"torre {Ints.ToPoints(partida.Tower.Hp)} · {partida.Enemies.Count} vivos · " +
                         $"roster: {clases}");

                if (partida.Enemies.Count > 0)
                {
                    var e = partida.Enemies[0];
                    var d = datos.Get(e.TypeId);

                    GD.Print($"        primero: {d?.Name} {Ints.ToPoints(e.Hp)}/{Ints.ToPoints(e.MaxHp)} HP · " +
                             $"vel {e.EffectiveSpeed} · dist {e.DistanceTravelled / 1000} celdas" +
                             (e.AttackingTower ? " · PEGANDO LA TORRE" : ""));
                }
            }
        }

        GD.Print($"── fin: {partida.State} en ol {partida.Wave}, t {partida.Tick} ──");
    }

    /// <summary>
    /// Corre una partida con una política que <b>reacciona</b> al estado.
    /// </summary>
    /// <remarks>
    /// <para>No alcanza con una lista de acciones escrita de antemano: la
    /// pantalla de elección frena la corrida y no se puede cerrar sin elegir, así
    /// que un log ciego se quedaría trabado en la primera. La política decide
    /// mientras juega, como una persona.</para>
    ///
    /// <para>Las acciones igual se van al <b>log</b>, así que el resultado sigue
    /// siendo re-simulable: el servidor puede tomar ese log y reproducirlo sin
    /// saber nada de esta política.</para>
    /// </remarks>
    private static Resultado Correr(ulong seed, ActionLog log)
        => Correr(seed, log, null, null);

    /// <summary>
    /// La misma corrida, pero sobre un mapa cualquiera.
    /// </summary>
    /// <remarks>
    /// Con <c>null</c> usa el layout de prueba y el plan de colocación escrito a
    /// mano. Se le pasa un mapa cuando hay que <b>probar layouts nuevos</b>: que
    /// cumplan las reglas no dice si se pueden jugar, y eso solo lo contesta
    /// jugarlos.
    /// </remarks>
    internal static Resultado Correr(
        ulong seed, ActionLog log, Map? mapa, IEnumerable<(int Tick, Cell Cell)>? plan)
    {
        var partida = new Run(seed, mapa ?? Layouts.Choose(seed, CargadorDatos.Actual), log, CargadorDatos.Actual);
        partida.Empezar();

        var pendientes = new Queue<(int Tick, Cell Cell)>(
            plan ?? ColocacionesPlaneadas(seed, partida.Map));

        while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
        {
            // Elegir siempre la primera opción. Es una política mala a propósito:
            // mide el piso del juego, no el techo. Una política que elige bien
            // diría cuán alto se puede llegar, y las dos cotas hacen falta.
            if (partida.WaitingForChoice)
                log.Agregar(PlayerAction.Choose(partida.TickDeAccion, MejorOpcion(partida)));

            // ⚠ La preparación se USA, no se saltea.
            //
            // Es la fase donde el jugador acomoda el tablero con el reloj
            // detenido. Un corredor que aprieta comenzar al instante mide un
            // juego que nadie juega, y la diferencia no es de matices: con la
            // misma seed, saltearla muere en la oleada 1 y usarla llega a la 36.
            //
            // Va de a UNO por vuelta a propósito: `Oro` y `PuedePoner` no cambian
            // hasta que `Avanzar` aplique la acción, así que dos colocaciones en
            // la misma vuelta se decidirían las dos con el oro viejo. Como el
            // reloj está congelado, las vueltas de más no cuestan ticks.
            if (partida.WaitingForWaveStart)
            {
                // ⚠ El orden importa: primero el tablero, después la tienda. Al
                // revés, la tienda se lleva el oro de la próxima colocación y el
                // jugador imaginario termina con la mochila llena y el mapa
                // vacío — que es peor que cualquiera de las dos cosas sola.
                //
                // Se arranca la oleada recién cuando no queda nada por hacer.
                bool puso = ColocarUnoEnPreparacion(partida, log, pendientes);
                bool compro = !puso && ComprarLoQuePueda(partida, log);

                if (!puso && !compro)
                {
                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.CallNextWave, 0, 0, 0));
                }

                EquiparLoQuePueda(partida, log);
                partida.Advance();
                continue;
            }


            // Poner lo que toque, cuando el oro alcance. Si no alcanza, espera:
            // reintenta el mismo el tick siguiente.
            //
            // ⚠ La clase se elige acá, del roster, y no en el plan. Elegirla de
            // antemano entre las 9 trababa la cola para siempre: el plan pedía
            // una clase que el jugador no había contratado, PuedePoner rebotaba, y
            // como la cola se mira con Peek nunca pasaba al siguiente lugar.
            if (pendientes.Count > 0)
            {
                var (tick, celda) = pendientes.Peek();

                if (partida.Tick >= tick)
                {
                    var clase = ClaseMasCara(partida);

                    if (clase is not null && partida.PuedePoner(clase.Value, celda))
                    {
                        log.Agregar(PlayerAction.Place(partida.TickDeAccion, clase.Value, celda));
                        pendientes.Dequeue();
                    }
                }
            }

            // Equipar lo primero que sirva de la mochila, para que los drops no
            // se junten sin usarse.
            EquiparLoQuePueda(partida, log);

            partida.Advance();
        }

        return new Resultado(
            partida.Tick, partida.Wave, partida.Tower.Hp, partida.Gold, partida.State,
            partida.Defenders.Count, partida.Roster.Classes.Count, partida.Backpack.Ocupados);
    }

    /// <summary>
    /// Cuál de las 3 opciones conviene.
    /// </summary>
    /// <remarks>
    /// <para>Antes elegía siempre la primera, y eso <b>no medía el piso del juego
    /// sino el del peor jugador posible</b>: la elección inicial garantiza que
    /// haya una clase ofensiva entre las tres, pero las mezcla para que la
    /// garantía no sea visible, así que "siempre la primera" terminaba con un
    /// bardo solo. Medido: la mitad de las corridas moría en la oleada 1 con el
    /// roster en <c>Bardo(nv1)</c> y cero enemigos muertos.</para>
    ///
    /// <para>La heurística es tosca —daño por tick, y levelear vale como un 30%—
    /// pero es lo que haría cualquiera mirando la pantalla, que es exactamente lo
    /// que tiene que medir esto.</para>
    /// </remarks>
    private static int MejorOpcion(Run partida)
    {
        var opciones = partida.OpenChoices;
        int mejor = 0;
        int mejorPuntaje = int.MinValue;

        for (int i = 0; i < opciones.Length; i++)
        {
            // ⚠ La oferta de Oro del jefe también entra por acá (Drops_v2), y el
            // bot la resuelve de la forma más tonta que sirve: se queda con el
            // que COMPLETA UN PLANO si hay alguno, y si no con el primero.
            //
            // No intenta valuar comportamientos, y es a propósito: un bot que
            // eligiera bien los Oro mediría el techo del juego y esta herramienta
            // mide el PISO. Lo que sí tiene que hacer es no romperse.
            if (opciones[i].Kind == ChoiceType.Perk)
            {
                if (mejorPuntaje == int.MinValue)
                    mejor = i;

                continue;
            }

            var stats = CargadorDatos.Actual.StatsOf(opciones[i].DefenderClass);

            // Daño por cien ticks, para que la cadencia pese tanto como el golpe.
            int puntaje = stats.Damage * 100 / System.Math.Max(1, stats.CadenceTicks);

            // El alcance multiplica: un defensor con más alcance dispara a más
            // enemigos durante más tiempo.
            puntaje = puntaje * (100 + (stats.RangeCells * 15)) / 100;

            if (opciones[i].Kind == ChoiceType.LevelUp)
                puntaje = puntaje * 130 / 100;

            if (puntaje > mejorPuntaje)
            {
                mejorPuntaje = puntaje;
                mejor = i;
            }
        }

        return mejor;
    }

    /// <summary>
    /// La clase más cara del roster que el oro alcance a pagar.
    /// </summary>
    /// <remarks>
    /// Heurística deliberadamente simple: más caro suele ser mejor, y una
    /// política tonta que se entiende de un vistazo mide mejor el piso del juego
    /// que una lista de reglas que nadie sabe si son buenas.
    /// </remarks>
    private static DefenderClass? ClaseMasCara(Run partida)
    {
        DefenderClass? mejor = null;
        int mejorCosto = -1;

        foreach (var ficha in partida.Roster.Classes)
        {
            int costo = CargadorDatos.Actual.StatsOf(ficha.DefenderClass).Cost;

            if (costo > mejorCosto && costo <= partida.Gold)
            {
                mejor = ficha.DefenderClass;
                mejorCosto = costo;
            }
        }

        return mejor;
    }

    private static void EquiparLoQuePueda(Run partida, ActionLog log)
    {
        var cosas = partida.Backpack.Items;

        for (int i = 0; i < cosas.Count; i++)
        {
            if (cosas[i].Kind != ItemType.Perk)
                continue;

            var def = CargadorDatos.Actual.Perks.Get(cosas[i].Id);

            foreach (var ficha in partida.Roster.Classes)
            {
                if (ficha.HasFreeSlot && def.WorksFor(ficha.DefenderClass))
                {
                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.EquipPerk, i, (int)ficha.DefenderClass, 0));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Compra de la tienda lo primero que entre en el bolsillo y en la mochila.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Existe para que el oro sin gastar SIGNIFIQUE algo.</b> Sin esta
    /// política el jugador imaginario no compraba nunca, así que terminaba con
    /// miles de oro y ese número no medía la economía: medía que el bot no sabía
    /// gastarlo. Economia_Propuesta difiere los ajustes de ingreso hasta tener
    /// esa medición, y con un bot que no compra la medición es basura.</para>
    ///
    /// <para>La heurística es deliberadamente tonta —lo primero que alcance— por
    /// lo mismo que el resto: mide el piso del juego, no el techo.</para>
    ///
    /// <para>⚠ Comprar <b>no consume RNG</b>: el stock ya lo generó la seed al
    /// abrir la preparación, y la acción solo marca la oferta como vendida. Por
    /// eso se puede agregar a la política sin correr la secuencia y sin invalidar
    /// nada de lo que la revalidación reproduce.</para>
    /// </remarks>
    private static bool ComprarLoQuePueda(Run partida, ActionLog log)
    {
        // ⚠ Con la mochila llena no se compra NADA, y no es una optimización: la
        // compra rebota sin gastar oro, la oferta sigue en el stock y sigue
        // siendo accesible, así que el bucle de preparación la volvería a
        // encolar para siempre. La corrida se colgaría con el reloj congelado.
        if (partida.Backpack.Ocupados >= CargadorDatos.Actual.Curves.Defenders.BackpackSlots)
            return false;

        var stock = partida.Shop.Stock;

        for (int i = 0; i < stock.Count; i++)
        {
            if (stock[i].Sold || stock[i].Price > partida.Gold)
                continue;

            log.Agregar(new PlayerAction(partida.TickDeAccion, ActionType.Buy, i, 0, 0));
            return true;
        }

        return false;
    }

    /// <summary>
    /// La política de juego del jugador imaginario: poner unos defensores al
    /// principio y no tocar nada más.
    /// </summary>
    /// <remarks>
    /// Depende de la seed a propósito, para que no sean las mismas cinco
    /// acciones en todas las corridas. Cuando existan los sistemas de la Fase 3
    /// —perks, equipo, tienda— esta política se vuelve más rica, y ahí el
    /// corredor empieza a servir para balancear de verdad.
    /// </remarks>
    private static ActionLog ConAcciones(ulong seed) => new();

    /// <summary>
    /// Dónde va a querer poner defensores la política, y a partir de qué tick.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Las celdas se sacan del MAPA, no de una lista escrita a mano.</b>
    /// Antes eran coordenadas fijas del layout de prueba de 30×16. Al pasar el
    /// campo a 20×11 quedaron todas afuera, <c>PuedePoner</c> las rechazó, y como
    /// la cola se mira con <c>Peek</c> nunca avanzaba: las corridas morían en la
    /// oleada 1 con el oro intacto. Es el mismo bug que ya había pasado con la
    /// clase fijada de antemano.</para>
    ///
    /// <para>Se recorre la ruta y cada pocas celdas se toma una colocable pegada.
    /// Reparte parejo en vez de amontonar al principio, y sirve para cualquier
    /// layout — que es lo que hace falta ahora que son doce y los dibuja el
    /// usuario.</para>
    ///
    /// <para>La clase queda sin decidir acá: se resuelve al momento de poner,
    /// contra el roster que haya tocado.</para>
    /// </remarks>
    private static List<(int Tick, Cell Cell)> ColocacionesPlaneadas(ulong seed, Map mapa)
    {
        var rng = new Rng(seed ^ 0xA5A5_5A5AUL);

        var plan = new List<(int, Cell)>();
        var usadas = new HashSet<Cell>();

        long paso = 3 * Ints.ThousandthsPerCell;

        // ⚠ DESDE LA TORRE hacia la entrada, no al revés.
        //
        // Con dos fichas iniciales el orden decide la corrida: puestas al
        // principio de la ruta, un bicho que se les escapa llega a la Torre y
        // queda mordiéndola FUERA del alcance de todos — la oleada no termina
        // nunca, el bono no llega, y la corrida muere ahí (medido: seeds clavadas
        // 27.000 ticks en la oleada 1). Puestas al final, la Torre queda cubierta
        // desde la primera ficha, que es lo que hace cualquier humano.
        for (long d = mapa.Route.Length; d >= 0; d -= paso)
        {
            var enRuta = mapa.Route.CellAt(d);

            foreach (var v in Alrededor(enRuta))
            {
                if (!mapa.InGrid(v) || !mapa.EsColocable(v) || !usadas.Add(v))
                    continue;

                // Un poco de ruido en el momento para que dos seeds no pongan
                // exactamente en los mismos ticks.
                int retraso = rng.Between(0, Ints.SecondsToTicks(4));

                plan.Add((plan.Count * Ints.SecondsToTicks(8) + retraso, v));
                break;
            }
        }

        return plan;
    }

    /// <summary>Los ocho vecinos, para buscar dónde poner al lado del camino.</summary>
    private static IEnumerable<Cell> Alrededor(Cell c)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx != 0 || dy != 0)
                    yield return new Cell(c.X + dx, c.Y + dy);
            }
        }
    }


    /// <summary>
    /// Deja una corrida en la pantalla de preparación, la guarda y la retoma.
    /// </summary>
    /// <remarks>
    /// <para>Es el caso normal de una partida guardada: el jugador cierra el juego
    /// entre oleadas, no en medio de una. Y es el que más fácil se rompe, porque
    /// <b>el reloj está congelado</b>: los bucles que re-simulan giran mientras el
    /// tick no pase de un tope, y un tick que no avanza no llega nunca.</para>
    ///
    /// <para>Chequea las dos cosas que importan: que retomar <b>termine</b>, y que
    /// deje la corrida exactamente donde estaba —mismo tick, misma oleada, mismo
    /// oro, mismos defensores— y esperando el botón.</para>
    /// </remarks>
    private static void ProbarGuardadoEnPreparacion()
    {
        GD.Print("── Store en la pantalla de preparación ──────────");

        ulong[] seeds = [1, 42, 20260825, 999_999_937];
        int fallas = 0;
        int saltadas = 0;
        int probadas = 0;

        foreach (ulong seed in seeds)
        {
            var datos = CargadorDatos.Actual;
            var mapa = Layouts.Choose(seed, datos);

            // Se juega hasta que la simulación abra la preparación por TERCERA
            // vez: la primera es antes de la oleada 1 y no prueba gran cosa.
            var log = new ActionLog();
            var partida = new Run(seed, mapa, log, datos);
            partida.Empezar();

            int aperturas = 0;
            bool yaContada = false;
            var pendientes = new Queue<(int Tick, Cell Cell)>(
                ColocacionesPlaneadas(seed, mapa));

            while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
            {
                if (partida.WaitingForChoice)
                    log.Agregar(PlayerAction.Choose(partida.TickDeAccion, MejorOpcion(partida)));

                if (partida.WaitingForWaveStart)
                {
                    // La misma política que el resto: se usa la preparación para
                    // poner, y recién cuando no queda nada se arranca. Contar la
                    // apertura una sola vez, en la primera vuelta.
                    if (!yaContada)
                    {
                        aperturas++;
                        yaContada = true;
                    }

                    if (aperturas >= 3)
                        break;

                    if (!ColocarUnoEnPreparacion(partida, log, pendientes))
                    {
                        log.Agregar(new PlayerAction(
                            partida.TickDeAccion, ActionType.CallNextWave, 0, 0, 0));
                    }

                    partida.Advance();
                    continue;
                }

                yaContada = false;

                if (pendientes.Count > 0)
                {
                    var (tick, celda) = pendientes.Peek();

                    if (partida.Tick >= tick)
                    {
                        var clase = ClaseMasCara(partida);

                        if (clase is not null && partida.PuedePoner(clase.Value, celda))
                        {
                            log.Agregar(PlayerAction.Place(partida.TickDeAccion, clase.Value, celda));
                            pendientes.Dequeue();
                        }
                    }
                }

                partida.Advance();
            }

            if (aperturas < 3)
            {
                // No es una falla del guardado: la corrida se murió antes de
                // llegar a la tercera preparación. Es el problema de balance, y
                // contarlo acá escondería si el guardado anda o no.
                saltadas++;
                GD.Print($"  · seed {seed,-12} murió en la oleada {partida.Wave}, " +
                         "no llegó a la tercera preparación");
                continue;
            }

            // El guardado real es seed + log serializado. Se hace el viaje entero.
            var recuperado = BinaryLog.Leer(BinaryLog.Escribir(log));

            var retomada = new Run(seed, Layouts.Choose(seed, datos), recuperado, datos);
            retomada.Empezar();

            int vueltas = 0;

            while (retomada.State == RunState.InProgress && retomada.Tick < TechoTicks)
            {
                retomada.Advance();
                vueltas++;

                if (retomada.EsperaAlJugador)
                    break;
            }

            bool igual =
                retomada.Tick == partida.Tick &&
                retomada.Wave == partida.Wave &&
                retomada.Gold == partida.Gold &&
                retomada.Defenders.Count == partida.Defenders.Count &&
                retomada.Tower.Hp == partida.Tower.Hp &&
                retomada.WaitingForWaveStart;

            if (igual)
            {
                probadas++;
                GD.Print($"  ✓ seed {seed,-12} tick {partida.Tick,6} · oleada {partida.Wave,3} · " +
                         $"oro {partida.Gold,6} · {partida.Defenders.Count,2} def · " +
                         $"retomó en {vueltas} vueltas, esperando el botón");
            }
            else
            {
                fallas++;

                GD.PrintErr($"  ✗ seed {seed}: retomó distinto — " +
                            $"tick {retomada.Tick}/{partida.Tick} · " +
                            $"oleada {retomada.Wave}/{partida.Wave} · " +
                            $"oro {retomada.Gold}/{partida.Gold} · " +
                            $"def {retomada.Defenders.Count}/{partida.Defenders.Count} · " +
                            $"esperando={retomada.WaitingForWaveStart}");
            }
        }

        if (saltadas > 0)
            GD.Print($"  {saltadas} seed(s) no llegaron: es el balance, no el guardado");

        GD.Print(fallas == 0 && probadas > 0
            ? $"── GUARDADO OK · {probadas} probadas ──────────────"
            : $"── {fallas} FALLA(S) ──────────────────────────────");
    }

    /// <summary>
    /// Pone <b>un</b> defensor de la cola durante la preparación, si el oro llega.
    /// </summary>
    /// <remarks>
    /// <para>Uno por vuelta porque el estado no cambia hasta que
    /// <see cref="Partida.Avanzar"/> aplique la acción: dos colocaciones en la
    /// misma vuelta se decidirían las dos con el oro viejo, y la segunda sería un
    /// gasto que el jugador no podía pagar.</para>
    ///
    /// <para>Devolver <c>false</c> es la señal de que no queda nada por poner, y
    /// es lo que hace que el bucle apriete comenzar. Sin esa salida el corredor se
    /// queda en la preparación para siempre, porque el reloj no avanza.</para>
    /// </remarks>
    private static bool ColocarUnoEnPreparacion(
        Run partida, ActionLog log, Queue<(int Tick, Cell Cell)> pendientes)
    {
        if (pendientes.Count == 0)
            return false;

        var (_, celda) = pendientes.Peek();
        var clase = ClaseMasCara(partida);

        // Sin clase asequible o sin lugar válido, no se insiste: se arranca la
        // oleada y se reintenta en la preparación siguiente, con más oro.
        if (clase is null || !partida.PuedePoner(clase.Value, celda))
            return false;

        log.Agregar(PlayerAction.Place(partida.TickDeAccion, clase.Value, celda));
        pendientes.Dequeue();

        return true;
    }
}
