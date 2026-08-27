using System.Collections.Generic;
using System.Globalization;
using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Qué tan bien juega el jugador imaginario.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Un solo bot no alcanza para balancear.</b> <c>Resumen_Balance</c>
/// pide tres cotas —bot/piso 30-40, competente 60-75, build optimizada 90-100— y
/// con una sola política no hay con qué contestar dos de las tres. Peor: cuando
/// esa única política tenía un bug, el número que daba se leía como «el piso del
/// juego» en vez de «el techo del bot», y así se casi-cambia la curva por un
/// plan de colocación que se quedaba sin casilleros.</para>
///
/// <para>Las políticas se diferencian por <b>lo que el jugador sabe hacer</b>, no
/// por números: el piso no es el competente con menos oro, es alguien que cubre
/// mal el mapa y no usa la tienda.</para>
/// </remarks>
internal enum Politica
{
    /// <summary>
    /// Cubre la ruta salteado, no compra en la tienda, no compra niveles y no
    /// tira nada de la mochila. Sabe poner y sabe equipar, y nada más.
    /// </summary>
    Piso = 0,

    /// <summary>
    /// Llena el tablero, compra lo que entre, equipa armas, destraba la mochila y
    /// gasta el sobrante en niveles. No compone: no persigue planos ni sinergias.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Todo lo de <see cref="Normal"/> y además <b>compone</b>: arranca con los
    /// nueve planos aprendidos, elige los Oro del jefe que completan una receta y
    /// fusiona apenas puede.
    /// </summary>
    /// <remarks>
    /// ⚠ Los planos son la única progresión permanente del juego, así que «build
    /// optimizada» quiere decir <b>cuenta veterana</b>: sin ellos solo se puede
    /// armar una de las nueve recetas —la que viene con <c>"initial": true</c>— y
    /// la cota de arriba mediría a un jugador nuevo jugando bien, que no es lo
    /// que <c>Resumen_Balance</c> pregunta.
    /// </remarks>
    Techo = 2,
}

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

                case "--emparejar-layouts":
                    GeneradorLayouts.Emparejar(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int tope)) ? tope : 44);
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

                    // Sin política nombrada corren las tres, que es lo que hace
                    // falta casi siempre: una sola cota no dice nada.
                    Politica? sola = (i + 2 < args.Length ? args[i + 2] : "") switch
                    {
                        "piso" => Politica.Piso,
                        "normal" => Politica.Normal,
                        "techo" => Politica.Techo,
                        _ => null,
                    };

                    Balance(corridas, sola);
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
    private static void Balance(int corridas, Politica? sola)
    {
        if (sola is { } una)
        {
            BalanceDe(corridas, una);
            return;
        }

        // ⚠ Las tres de una, y de abajo hacia arriba. El número que importa es la
        // DISTANCIA entre las cotas —cuánto paga jugar mejor y cuánto paga
        // componer—, y leerla exige tenerlas juntas en la misma pantalla.
        BalanceDe(corridas, Politica.Piso);
        GD.Print("");
        BalanceDe(corridas, Politica.Normal);
        GD.Print("");
        BalanceDe(corridas, Politica.Techo);

        GD.Print("");
        GD.Print("── Objetivos de Resumen_Balance ────────────────────");
        GD.Print("   bot/piso 30-40 · competente 60-75 · build optimizada 90-100");
    }

    private static void BalanceDe(int corridas, Politica politica)
    {
        string nombre = politica switch
        {
            Politica.Piso => "piso",
            Politica.Techo => "techo",
            _ => "normal",
        };

        GD.Print($"── Balance · {nombre} · {corridas} corridas ───────────");

        int suma = 0, peor = int.MaxValue, mejor = 0;
        int sumaOro = 0, peorOro = int.MaxValue, mejorOro = 0;

        for (int i = 0; i < corridas; i++)
        {
            ulong seed = (ulong)(i + 1) * 2_654_435_761UL;
            var r = Correr(seed, ConAcciones(seed), politica);

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

        GD.Print($"── {nombre} · oleada:  peor {peor} · media {suma / corridas} · mejor {mejor} ──");

        // ⚠ EL ORO SIN GASTAR ES LA MEDICIÓN QUE PIDE Economia_Propuesta.
        //
        // La propuesta difiere los ajustes finos de economía hasta tener este
        // número: si el jugador termina la corrida con oro para dos o tres
        // compras completas de tienda sin haber sacrificado defensa, el ingreso
        // sobra y hay que recortarlo más. Con la mochila llena y el tablero
        // completo, el oro deja de ser un recurso y pasa a ser un marcador.
        //
        // ⚠ En «piso» este número NO mide la economía: esa política no compra
        // nada, así que lo que reporta es cuánto ingresa, no cuánto sobra.
        GD.Print($"── {nombre} · oro sin gastar:  peor {peorOro} · media {sumaOro / corridas} · mejor {mejorOro} ──");
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

        var pendientes = new Queue<(int Tick, Cell Cell)>(ColocacionesPlaneadas(seed, partida.Map, Politica.Normal));

        GD.Print($"── Traza · seed {seed} ────────────────────────────");
        GD.Print($"  ruta: {partida.Route.Length / Ints.ThousandthsPerCell} celdas");

        int oleadaAnterior = 0;
        int ultimoAviso = 0;

        while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
        {
            if (partida.WaitingForChoice)
                log.Agregar(PlayerAction.Choose(partida.TickDeAccion, MejorOpcion(partida, Politica.Normal)));

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
                bool puso = ColocarUnoEnPreparacion(partida, log, pendientes, Politica.Normal);
                bool compro = !puso && ComprarLoQuePueda(partida, log);

                // Con la mochila trabada la compra rebota: hacer un lugar es lo que
                // deja que el oro siga teniendo adónde ir.
                bool libero = !puso && !compro && LiberarMochila(partida, log);

                // Último: el sumidero del sobrante. Poner un defensor más siempre
                // vale más que un nivel comprado.
                bool subio = !puso && !compro && !libero && SubirNivelSiSobra(partida, log);

                if (!puso && !compro && !libero && !subio)
                {
                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.CallNextWave, 0, 0, 0));
                }

                // Ver la nota del mismo lugar en Correr: no se equipa en la vuelta
                // en que se tiró algo, o el índice apunta a otra cosa.
                if (!libero)
                    EquiparLoQuePueda(partida, log, Politica.Normal);

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

            EquiparLoQuePueda(partida, log, Politica.Normal);
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
    private static Resultado Correr(ulong seed, ActionLog log, Politica politica = Politica.Normal)
        => Correr(seed, log, null, null, politica);

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
        ulong seed, ActionLog log, Map? mapa, IEnumerable<(int Tick, Cell Cell)>? plan,
        Politica politica = Politica.Normal)
    {
        var partida = new Run(seed, mapa ?? Layouts.Choose(seed, CargadorDatos.Actual), log, CargadorDatos.Actual);

        // ⚠ Los planos van ANTES de Empezar: el sorteo de los Oro del jefe pesa
        // ×3 a la pareja de una receta conocida, así que aprenderlos después
        // cambiaría la oferta a mitad de corrida en vez de desde el arranque.
        //
        // Los planos son la única progresión permanente del juego, y por eso
        // «build optimizada» quiere decir cuenta veterana: sin ellos solo se puede
        // armar una de las nueve recetas y la cota de arriba mediría a un jugador
        // nuevo jugando bien, que no es lo que Resumen_Balance pregunta.
        if (politica == Politica.Techo)
        {
            foreach (var plano in CargadorDatos.Actual.Blueprints.All)
                partida.AprenderPlano(plano.Key);
        }

        partida.Empezar();

        var pendientes = new Queue<(int Tick, Cell Cell)>(
            plan ?? ColocacionesPlaneadas(seed, partida.Map, politica));

        while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
        {
            // ⚠ Con la mochila llena, EL ORO DEL JEFE SE PIERDE.
            //
            // `Run.Choose` lo guarda con `StorePerk`, y si no entra se descarta en
            // silencio —la UI de verdad abre el diálogo de «tirá uno» antes de
            // llegar ahí—. El bot no lo abría, así que llegaba a cada jefe con
            // 15/15 y perdía los ocho Oro de la corrida: el sistema de
            // comportamientos, los planos y la fusión no se activaban NUNCA.
            //
            // Medido: `techo` y `normal` daban resultados idénticos byte a byte,
            // con el peso de sinergia disparando 53 veces en seis corridas. Los
            // Oro que ofrecía el jefe no cambiaban nada porque ninguno entraba.
            if (partida.WaitingForChoice && !HacerLugarParaElOro(partida, log))
                log.Agregar(PlayerAction.Choose(partida.TickDeAccion, MejorOpcion(partida, politica)));

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
                bool puso = ColocarUnoEnPreparacion(partida, log, pendientes, politica);

                // ⚠ Las de abajo son lo que SEPARA las políticas. El piso sabe
                // poner y equipar; comprar, destrabar la mochila y gastar el
                // sobrante en niveles son cosas que un jugador aprende después, y
                // meterlas todas en una sola política borraba la cota de abajo.
                bool avanzado = politica != Politica.Piso;

                // Fusionar va PRIMERO de las avanzadas: saca dos Oro de la mochila
                // y devuelve un Único en un solo slot, así que destraba lugar en
                // los dos lados a la vez. Solo el techo compone.
                bool fusiono = !puso && politica == Politica.Techo
                               && FusionarSiPuede(partida, log);

                bool compro = !puso && !fusiono && avanzado && ComprarLoQuePueda(partida, log);

                // Con la mochila trabada la compra rebota: hacer un lugar es lo que
                // deja que el oro siga teniendo adónde ir.
                bool libero = !puso && !fusiono && !compro && avanzado
                              && LiberarMochila(partida, log);

                // Último: el sumidero del sobrante. Poner un defensor más siempre
                // vale más que un nivel comprado.
                bool subio = !puso && !fusiono && !compro && !libero && avanzado
                             && SubirNivelSiSobra(partida, log);

                if (!puso && !fusiono && !compro && !libero && !subio)
                {
                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.CallNextWave, 0, 0, 0));
                }

                // ⚠ NO se equipa en la misma vuelta que se fusionó o se tiró algo.
                //
                // Las acciones se aplican en orden al avanzar, pero los índices de
                // mochila se calculan TODOS contra el estado de antes: una fusión
                // saca dos ítems y un descarte saca uno, así que el índice del
                // equipar que va detrás apunta a otra cosa. Comprar sí es seguro
                // —el ítem entra al final y no corre a los de antes—.
                if (!fusiono && !libero)
                    EquiparLoQuePueda(partida, log, politica);

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
                    var clase = ClaseParaPoner(partida, politica);

                    if (clase is not null && partida.PuedePoner(clase.Value, celda))
                    {
                        log.Agregar(PlayerAction.Place(partida.TickDeAccion, clase.Value, celda));
                        pendientes.Dequeue();
                    }
                }
            }

            // Equipar lo primero que sirva de la mochila, para que los drops no
            // se junten sin usarse.
            EquiparLoQuePueda(partida, log, politica);

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
    private static int MejorOpcion(Run partida, Politica politica)
    {
        var opciones = partida.OpenChoices;
        int mejor = 0;
        int mejorPuntaje = int.MinValue;
        int mejorPerk = -1;

        for (int i = 0; i < opciones.Length; i++)
        {
            // ⚠ La oferta de Oro del jefe también entra por acá (Drops_v2).
            //
            // Piso y normal se quedan con el primero: no intentan valuar
            // comportamientos, y es a propósito, porque elegir bien los Oro mide
            // el techo del juego y esas dos políticas miden otra cosa.
            //
            // El techo sí compone: prefiere el Oro que COMPLETA una receta que ya
            // tiene a medias. Es la mitad de lo que hace que los Únicos sean un
            // objetivo de build y no una lotería —la otra mitad es el peso ×3 del
            // sorteo, que ya vive en la simulación—.
            if (opciones[i].Kind == ChoiceType.Perk)
            {
                if (mejorPerk < 0)
                    mejorPerk = i;

                if (politica == Politica.Techo && CompletaUnPlano(partida, opciones[i].PerkId))
                    mejorPerk = i;

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

        // Una pantalla trae SOLO ofertas de Oro o SOLO clases, nunca mezcladas,
        // así que si hubo alguna de perk esa es la respuesta.
        return mejorPerk >= 0 ? mejorPerk : mejor;
    }

    /// <summary>
    /// ¿Este Oro es la pareja de una receta que el jugador ya tiene a medias?
    /// </summary>
    /// <remarks>
    /// <para>Mira la mochila y lo equipado, igual que la simulación al armar la
    /// oferta. No filtra por plano aprendido porque el techo los aprende todos al
    /// arrancar; con una política que no lo hiciera habría que chequearlo, y esa
    /// política todavía no existe.</para>
    /// </remarks>
    private static bool CompletaUnPlano(Run partida, int idPerk)
    {
        foreach (var plano in CargadorDatos.Actual.Blueprints.All)
        {
            if (plano.GoldA == idPerk && Tiene(partida, plano.GoldB))
                return true;

            if (plano.GoldB == idPerk && Tiene(partida, plano.GoldA))
                return true;
        }

        return false;
    }

    /// <summary>¿Tiene este perk, en la mochila o equipado?</summary>
    private static bool Tiene(Run partida, int idPerk)
    {
        foreach (var cosa in partida.Backpack.Items)
        {
            if (cosa.Kind == ItemType.Perk && cosa.Id == idPerk)
                return true;
        }

        foreach (var ficha in partida.Roster.Classes)
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
    /// Fusiona el primer par de Oro de la mochila que forme una receta. Devuelve
    /// <c>true</c> si fusionó.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Los Únicos no caen y no se compran: se fabrican.</b> Una política
    /// que no fusiona nunca ve uno, así que la cota de arriba mediría el juego sin
    /// su mecánica de composición — que es justamente lo que la cota de arriba
    /// tiene que medir.</para>
    ///
    /// <para>Se chequean acá las mismas condiciones que <c>Run.Fuse</c>: los dos
    /// tienen que ser Oro, tiene que existir la receta, y tiene que haber una
    /// clase del roster que pueda usar el Único. Una acción que rebota no saca
    /// nada de la mochila, y el bucle de preparación —con el reloj congelado— la
    /// volvería a emitir para siempre.</para>
    ///
    /// <para>El primero que sirve, no el mejor: la política elige QUÉ Oro juntar
    /// en la pantalla del jefe, y una vez que los dos están en la mochila no hay
    /// nada que decidir.</para>
    /// </remarks>
    /// <summary>
    /// Tira algo de la mochila llena cuando el jefe está ofreciendo Oro. Devuelve
    /// <c>true</c> si tiró —y entonces todavía no se elige, se elige la vuelta que
    /// viene, con lugar—.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Un Oro que no entra se pierde en silencio</b>, y son ocho en toda
    /// la corrida: es la única fuente de comportamientos, la entrada a los planos y
    /// la condición para que exista la fusión. Sin esto el bot llegaba a cada jefe
    /// con 15/15 y los perdía todos.</para>
    ///
    /// <para>Se tira el de tier más bajo, y <b>nunca un Oro o un Único</b>: hacer
    /// lugar para un comportamiento tirando otro comportamiento no hace lugar,
    /// mueve el problema. Si en la mochila solo hay Oro y Únicos no se tira nada y
    /// se elige igual — perder el nuevo es mejor que perder uno que ya está.</para>
    /// </remarks>
    private static bool HacerLugarParaElOro(Run partida, ActionLog log)
    {
        var opciones = partida.OpenChoices;

        if (opciones.Length == 0 || opciones[0].Kind != ChoiceType.Perk)
            return false;

        if (partida.Backpack.Ocupados < CargadorDatos.Actual.Curves.Defenders.BackpackSlots)
            return false;

        var cosas = partida.Backpack.Items;
        var perks = CargadorDatos.Actual.Perks;

        int peor = -1;
        var peorTier = PerkTier.Unique;

        for (int i = 0; i < cosas.Count; i++)
        {
            // Un arma de más es lo primero que sobra: la ranura de equipo es una
            // sola por clase y la segunda no espera nada.
            if (cosas[i].Kind != ItemType.Perk)
            {
                peor = i;
                break;
            }

            var tier = perks.Get(cosas[i].Id).Tier;

            if (tier is PerkTier.Gold or PerkTier.Unique)
                continue;

            if (peor < 0 || tier < peorTier)
            {
                peor = i;
                peorTier = tier;
            }
        }

        if (peor < 0)
            return false;

        log.Agregar(new PlayerAction(partida.TickDeAccion, ActionType.Discard, peor, 0, 0));

        return true;
    }

    private static bool FusionarSiPuede(Run partida, ActionLog log)
    {
        var cosas = partida.Backpack.Items;
        var perks = CargadorDatos.Actual.Perks;

        for (int a = 0; a < cosas.Count; a++)
        {
            if (cosas[a].Kind != ItemType.Perk || perks.Get(cosas[a].Id).Tier != PerkTier.Gold)
                continue;

            for (int b = a + 1; b < cosas.Count; b++)
            {
                if (cosas[b].Kind != ItemType.Perk || perks.Get(cosas[b].Id).Tier != PerkTier.Gold)
                    continue;

                var plano = CargadorDatos.Actual.Blueprints.Find(cosas[a].Id, cosas[b].Id);
                if (plano is null)
                    continue;

                var unico = perks.Get(plano.ResultingUnique);

                foreach (var ficha in partida.Roster.Classes)
                {
                    if (!unico.WorksFor(ficha.DefenderClass))
                        continue;

                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.Fuse, a, b, (int)ficha.DefenderClass));

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Qué clase poner en la próxima celda, o <c>null</c> si el oro no alcanza
    /// para ninguna.
    /// </summary>
    /// <remarks>
    /// <para>Piso y normal ponen <b>la más cara que puedan pagar</b>. Es una
    /// heurística deliberadamente simple —más caro suele ser mejor— y como el
    /// roster crece a ocho o nueve clases, el tablero termina repartido entre
    /// todas.</para>
    ///
    /// <para>⚠ <b>El techo CONCENTRA en la mejor clase del roster</b>, y eso no es
    /// un detalle: un perk vive en la ficha de la clase, así que un Oro puesto en
    /// una clase que ocupa el 12% del tablero rinde el 12%. Medido: triplicar TODO
    /// el catálogo de Oro con el tablero repartido movía la corrida una sola
    /// oleada. Especializar es lo que convierte un comportamiento en una build, y
    /// «build optimizada» es literalmente el nombre de esta cota.</para>
    ///
    /// <para>Se cae a la más cara que se pueda pagar cuando la favorita no entra
    /// en el bolsillo: dejar la celda vacía esperando es peor que llenarla.</para>
    /// </remarks>
    private static DefenderClass? ClaseParaPoner(Run partida, Politica politica)
    {
        if (politica == Politica.Techo && ClaseFavorita(partida) is { } favorita
            && CargadorDatos.Actual.StatsOf(favorita).Cost <= partida.Gold)
        {
            return favorita;
        }

        return ClaseMasCara(partida);
    }

    /// <summary>
    /// La clase del roster con mejor daño por tick, pesado por alcance. Es el
    /// mismo puntaje con el que la política elige en la pantalla de elección, así
    /// que lo que contrata y lo que pone no se contradicen.
    /// </summary>
    private static DefenderClass? ClaseFavorita(Run partida)
    {
        DefenderClass? mejor = null;
        int mejorPuntaje = -1;

        foreach (var ficha in partida.Roster.Classes)
        {
            var stats = CargadorDatos.Actual.StatsOf(ficha.DefenderClass);

            int puntaje = stats.Damage * 100 / System.Math.Max(1, stats.CadenceTicks);
            puntaje = puntaje * (100 + (stats.RangeCells * 15)) / 100;

            if (puntaje > mejorPuntaje)
            {
                mejorPuntaje = puntaje;
                mejor = ficha.DefenderClass;
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

    /// <summary>
    /// Equipa lo primero de la mochila que entre en algún lado: un perk en un
    /// slot libre, o un arma en una clase que no tenga.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Las armas también.</b> Antes solo miraba perks, y las armas se
    /// quedaban en la mochila para siempre: llenaban los 15 lugares, y con la
    /// mochila llena <see cref="ComprarLoQuePueda"/> deja de comprar. El resultado
    /// era un bot que a mitad de corrida no gastaba más, y un «oro sin gastar» que
    /// medía eso y no la economía. Es el mismo error que el plan de colocaciones
    /// que se quedaba sin casilleros.</para>
    ///
    /// <para>Se equipa <b>solo en una clase que no tenga arma</b>, nunca
    /// recambiando: un swap devuelve la vieja a la mochila y el bucle de
    /// preparación —que corre con el reloj congelado— podría no terminar nunca.
    /// Cada acción de acá tiene que sacar algo de la mochila.</para>
    ///
    /// <para>Una acción por llamada, y la llamada se repite en cada vuelta: así el
    /// estado que mira siempre es el de después de aplicar la anterior.</para>
    /// </remarks>
    /// <summary>
    /// ¿Quedan al menos tres lugares en la mochila?
    /// </summary>
    /// <remarks>
    /// Es la válvula del techo, que guarda los Oro esperando la pareja de una
    /// receta. Guardar sin límite termina con la mochila trabada de Oro, y una
    /// mochila trabada apaga la tienda: se dejan de comprar perks y armas a cambio
    /// de esperar una receta que puede no salir.
    /// </remarks>
    private static bool HayLugarDeSobra(Run partida)
        => CargadorDatos.Actual.Curves.Defenders.BackpackSlots - partida.Backpack.Ocupados >= 3;

    private static void EquiparLoQuePueda(Run partida, ActionLog log, Politica politica)
    {
        var cosas = partida.Backpack.Items;

        for (int i = 0; i < cosas.Count; i++)
        {
            if (cosas[i].Kind == ItemType.Perk)
            {
                var def = CargadorDatos.Actual.Perks.Get(cosas[i].Id);

                // ⚠ El techo GUARDA los Oro en vez de equiparlos apenas caen.
                //
                // Con nueve clases y una ranura de comportamiento cada una, cada
                // Oro encuentra siempre dónde entrar, así que ninguno se queda en
                // la mochila y NUNCA se junta un par: medido, la fusión disparaba
                // 0,7 veces por corrida. Guardarlos es lo que hace que las recetas
                // se puedan armar, y es lo que haría un jugador que persigue un
                // Único.
                //
                // La válvula es la presión de mochila: con dos lugares o menos se
                // equipan igual, porque una mochila trabada de Oro apaga la tienda
                // y no compra nada a cambio.
                if (politica == Politica.Techo && def.Tier == PerkTier.Gold && HayLugarDeSobra(partida))
                    continue;

                var destino = DondeEquipar(partida, def, politica);

                if (destino is { } clase)
                {
                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.EquipPerk, i, (int)clase, 0));
                    return;
                }

                continue;
            }

            if (cosas[i].Kind != ItemType.Weapon)
                continue;

            foreach (var ficha in partida.Roster.Classes)
            {
                if (ficha.Weapon < 0)
                {
                    log.Agregar(new PlayerAction(
                        partida.TickDeAccion, ActionType.Equip, i, (int)ficha.DefenderClass, 0));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// En qué clase conviene poner este perk, o <c>null</c> si no entra en ninguna.
    /// </summary>
    /// <remarks>
    /// <para>Piso y normal usan la primera que lo acepte: es tosco y es lo que
    /// tiene que ser, porque miden las cotas de abajo.</para>
    ///
    /// <para>⚠ El techo lo pone en la clase que <b>más defensores tiene en el
    /// tablero</b>. Un perk vive en la ficha de la clase, así que su valor es
    /// proporcional a cuántas unidades de esa clase están puestas: el mismo Oro en
    /// una clase con doce defensores vale doce veces más que en una con uno. La
    /// primera que lo acepte es, casi siempre, la primera que se contrató.</para>
    /// </remarks>
    private static DefenderClass? DondeEquipar(Run partida, PerkDefinition def, Politica politica)
    {
        DefenderClass? mejor = null;
        int mejorPuestos = -1;

        foreach (var ficha in partida.Roster.Classes)
        {
            if (!PuedeEquipar(ficha, def))
                continue;

            if (politica != Politica.Techo)
                return ficha.DefenderClass;

            int puestos = 0;

            foreach (var d in partida.Defenders)
            {
                if (d.DefenderClass == ficha.DefenderClass)
                    puestos++;
            }

            if (puestos > mejorPuestos)
            {
                mejorPuestos = puestos;
                mejor = ficha.DefenderClass;
            }
        }

        return mejor;
    }

    /// <summary>
    /// ¿<c>Run.EquipPerk</c> va a aceptar este perk en esta clase?
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Las mismas cuatro condiciones que la simulación, no menos.</b>
    /// Antes acá se chequeaban dos —slot libre y que la clase lo pueda usar— y las
    /// otras dos las rebotaba <c>Run</c> en silencio. Una acción rebotada no saca
    /// el perk de la mochila, así que <see cref="EquiparLoQuePueda"/> la volvía a
    /// emitir en la vuelta siguiente, y en la siguiente: el mismo perk imposible
    /// escribiéndose en el log para siempre.</para>
    ///
    /// <para>La ranura de comportamiento es UNA —un Oro con un Oro puesto rebota—
    /// y el apilado tiene tope por tier: Bronce 3, Plata 2, Oro y Único 1.</para>
    /// </remarks>
    private static bool PuedeEquipar(RosterClass ficha, PerkDefinition def)
    {
        if (!ficha.HasFreeSlot || !def.WorksFor(ficha.DefenderClass))
            return false;

        bool comportamiento = def.Tier is PerkTier.Gold or PerkTier.Unique;
        int yaTiene = 0;

        foreach (int id in ficha.Perks)
        {
            var puesto = CargadorDatos.Actual.Perks.Get(id);

            if (comportamiento && puesto.Tier is PerkTier.Gold or PerkTier.Unique)
                return false;

            if (id == def.Id)
                yaTiene++;
        }

        return yaTiene < def.MaxStacked;
    }

    /// <summary>
    /// Hace UN lugar en la mochila llena, y solo si hay algo en la tienda para
    /// poner ahí. Devuelve <c>true</c> si liberó algo.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Solo se tira lo que no sirve.</b> Un perk que ninguna clase del
    /// roster puede usar es basura —la clase no está y no va a estar— y un arma de
    /// más cuando las nueve fichas ya tienen la suya es plata. Lo que sí entraría
    /// en algún lado no se toca: <see cref="EquiparLoQuePueda"/> lo va a agarrar en
    /// la vuelta siguiente.</para>
    ///
    /// <para>⚠ <b>Solo con algo comprable en el stock</b>, y no es un detalle de
    /// eficiencia: sin esa condición el bucle liberaría un lugar, no compraría
    /// nada, encontraría la mochila llena otra vez y seguiría hasta vaciarla.
    /// Atado al stock, se corta solo —la tienda tiene cuatro ofertas y cada compra
    /// las marca vendidas—.</para>
    ///
    /// <para>Existe por el mismo motivo que las armas en <c>EquiparLoQuePueda</c>:
    /// con la mochila trabada el bot deja de comprar a mitad de corrida, y el «oro
    /// sin gastar» mide eso en vez de la economía.</para>
    /// </remarks>
    private static bool LiberarMochila(Run partida, ActionLog log)
    {
        if (partida.Backpack.Ocupados < CargadorDatos.Actual.Curves.Defenders.BackpackSlots)
            return false;

        bool hayQueComprar = false;

        foreach (var oferta in partida.Shop.Stock)
        {
            if (!oferta.Sold && oferta.Price <= partida.Gold)
            {
                hayQueComprar = true;
                break;
            }
        }

        if (!hayQueComprar)
            return false;

        var cosas = partida.Backpack.Items;

        // 1) Un perk que ninguna clase del roster puede equipar nunca.
        for (int i = 0; i < cosas.Count; i++)
        {
            if (cosas[i].Kind != ItemType.Perk)
                continue;

            var def = CargadorDatos.Actual.Perks.Get(cosas[i].Id);
            bool leSirveAAlguna = false;

            foreach (var ficha in partida.Roster.Classes)
            {
                if (def.WorksFor(ficha.DefenderClass))
                {
                    leSirveAAlguna = true;
                    break;
                }
            }

            if (!leSirveAAlguna)
            {
                log.Agregar(new PlayerAction(partida.TickDeAccion, ActionType.Discard, i, 0, 0));
                return true;
            }
        }

        // 2) Un arma de más: si alguna ficha estuviera sin arma, EquiparLoQuePueda
        //    ya la habría puesto, así que llegar acá quiere decir que sobran.
        for (int i = 0; i < cosas.Count; i++)
        {
            if (cosas[i].Kind != ItemType.Weapon)
                continue;

            log.Agregar(new PlayerAction(partida.TickDeAccion, ActionType.SellGear, i, 0, 0));
            return true;
        }

        // 3) Todo lo que queda le sirve a alguna clase y espera un slot que abre
        //    al levelear. No se tira: la mochila queda trabada y eso es una
        //    decisión del juego, no del bot.
        return false;
    }

    /// <summary>
    /// Compra un nivel cuando ya no hay nada más que hacer con el oro. Devuelve
    /// <c>true</c> si compró.
    /// </summary>
    /// <remarks>
    /// <para>Va <b>último</b> en la preparación, después de poner, comprar y
    /// destrabar la mochila: es literalmente el sumidero del sobrante. Poner un
    /// defensor más siempre vale más que un nivel, y la política tiene que
    /// reflejar eso o el bot dejaría el tablero a medio llenar.</para>
    ///
    /// <para>Sube <b>la clase de nivel más bajo</b>, que es la que tiene el nivel
    /// siguiente más barato: el precio es cuadrático, así que repartir abre más
    /// slots por moneda que concentrar. Es una heurística tonta a propósito, como
    /// el resto —mide el piso, no el techo—, pero tonta y coherente.</para>
    ///
    /// <para>La afordabilidad se chequea acá y no solo en <c>Run.BuyLevel</c>:
    /// una acción que rebota devolvería <c>true</c> sin cambiar nada y el bucle de
    /// preparación, con el reloj congelado, no terminaría nunca.</para>
    /// </remarks>
    private static bool SubirNivelSiSobra(Run partida, ActionLog log)
    {
        var economia = CargadorDatos.Actual.Curves.Economy;

        DefenderClass? mejor = null;
        int mejorNivel = int.MaxValue;

        foreach (var ficha in partida.Roster.Classes)
        {
            if (ficha.Level >= ficha.MaxLevel || ficha.Level >= mejorNivel)
                continue;

            if (economia.LevelUpPrice(ficha.Level + 1) > partida.Gold)
                continue;

            mejor = ficha.DefenderClass;
            mejorNivel = ficha.Level;
        }

        if (mejor is null)
            return false;

        log.Agregar(new PlayerAction(
            partida.TickDeAccion, ActionType.BuyLevel, (int)mejor.Value, 0, 0));

        return true;
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
    private static List<(int Tick, Cell Cell)> ColocacionesPlaneadas(
        ulong seed, Map mapa, Politica politica)
    {
        var rng = new Rng(seed ^ 0xA5A5_5A5AUL);

        var plan = new List<(int, Cell)>();
        var usadas = new HashSet<Cell>();

        // ⚠ EL PASO Y LOS VECINOS SON LA POLÍTICA, no una constante.
        //
        // Normal y techo recorren la ruta celda por celda y toman TODOS los vecinos
        // colocables: llena el tablero, que es lo que hace cualquiera que ya
        // entendió el juego. Piso salta de a dos celdas y toma UNO, así que cubre
        // la ruta salteado y deja la mitad del tablero vacío.
        //
        // La diferencia no es cosmética: con paso 3 y un vecino —lo que había
        // antes para todos— el plan tenía ~11 lugares en una ruta de 33 celdas, el
        // bot moría con 8 defensores en un tablero de 40 y ese número se leía como
        // «el piso del juego» cuando era el techo del plan.
        bool lleno = politica != Politica.Piso;
        long paso = (lleno ? 1 : 2) * Ints.ThousandthsPerCell;

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

                if (!lleno)
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
                ColocacionesPlaneadas(seed, mapa, Politica.Normal));

            while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
            {
                if (partida.WaitingForChoice)
                    log.Agregar(PlayerAction.Choose(partida.TickDeAccion, MejorOpcion(partida, Politica.Normal)));

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

                    if (!ColocarUnoEnPreparacion(partida, log, pendientes, Politica.Normal))
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
        Run partida, ActionLog log, Queue<(int Tick, Cell Cell)> pendientes, Politica politica)
    {
        if (pendientes.Count == 0)
            return false;

        var (_, celda) = pendientes.Peek();
        var clase = ClaseParaPoner(partida, politica);

        // Sin clase asequible o sin lugar válido, no se insiste: se arranca la
        // oleada y se reintenta en la preparación siguiente, con más oro.
        if (clase is null || !partida.PuedePoner(clase.Value, celda))
            return false;

        log.Agregar(PlayerAction.Place(partida.TickDeAccion, clase.Value, celda));
        pendientes.Dequeue();

        return true;
    }
}
