using System.Collections.Generic;
using System.Globalization;
using Godot;
using TowerOfArgentum.Simulation;

namespace TowerOfArgentum.Tools;

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
internal enum Policy
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
public static class HeadlessRunner
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
    public static bool HandleArguments()
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
                    NpcImporter.Execute();
                    return true;

                case "--importar-apariencia":
                    AppearanceImporter.Execute();
                    return true;

                case "--traer-item":
                    GraphicFetcher.FetchItem(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int it)) ? it : 0);
                    return true;

                case "--traer-razas":
                    GraphicFetcher.FetchRaces();
                    return true;

                case "--traer-armaduras-bajas":
                    GraphicFetcher.FetchLowArmors();
                    return true;

                case "--traer-arte":
                    GraphicFetcher.FetchClassArt(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int so)) ? so : 0);
                    return true;

                case "--traer-tiers":
                    GraphicFetcher.FetchTiers();
                    return true;

                case "--listar-razas":
                    GraphicsInspector.ListRaces();
                    return true;

                case "--verificar-arte":
                    ArtChecker.Execute();
                    return true;

                case "--presupuesto-arte":
                    GraphicsInspector.Budget();
                    return true;

                case "--traer-piso":
                    GraphicFetcher.FetchFloor(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int ph)) ? ph : 0,
                        i + 2 < args.Length ? args[i + 2] : "",
                        (i + 3 < args.Length && int.TryParse(args[i + 3], out int pb)) ? pb : 0);
                    return true;

                case "--traer-tileset":
                    GraphicFetcher.FetchTileset(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int th)) ? th : 0,
                        i + 2 < args.Length ? args[i + 2] : "");
                    return true;

                case "--exportar-balance":
                    // El segundo argumento son las corridas de medición. Sin él
                    // salen solo las diez hojas de datos, que es lo barato.
                    BalanceExporter.Execute(
                        i + 1 < args.Length ? args[i + 1] : "",
                        (i + 2 < args.Length && int.TryParse(args[i + 2], out int med)) ? med : 0);
                    return true;

                case "--muestrear-piso":
                    FloorSampler.Execute(
                        i + 1 < args.Length ? args[i + 1] : "piso.png",
                        (i + 2 < args.Length && ulong.TryParse(args[i + 2], out ulong sp)) ? sp : 20260825UL,
                        (i + 3 < args.Length && int.TryParse(args[i + 3], out int sa)) ? sa : 0);
                    return true;

                case "--verificar-tilesets":
                    TilesetChecker.Execute(
                        i + 1 < args.Length ? args[i + 1] : "tilesets.png");
                    return true;

                case "--grilla-tileset":
                    GraphicsInspector.TilesetGrid(i + 1 < args.Length ? args[i + 1] : "");
                    return true;

                case "--muestrear-bloques":
                    GraphicsInspector.SampleBlocks(
                        i + 1 < args.Length ? args[i + 1] : "",
                        i + 2 < args.Length ? args[i + 2] : "bloques.png");
                    return true;

                case "--muestrear-tilesets":
                    GraphicsInspector.SampleTilesets(
                        i + 1 < args.Length ? args[i + 1] : "",
                        i + 2 < args.Length ? args[i + 2] : "muestra.png");
                    return true;

                case "--clasificar-tilesets":
                    GraphicsInspector.ClassifyTilesets(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int mt)) ? mt : 64);
                    return true;

                case "--tilesets":
                    GraphicsInspector.Tilesets();
                    return true;

                case "--presupuesto-bestiario":
                    GraphicsInspector.BestiaryBudget();
                    return true;

                // Sin número trae el área 1: Ullathorpe es el único bestiario que
                // hace falta hoy, y el default no puede ser «las ocho» porque eso
                // son 97 MB de un tirón.
                case "--traer-bestiario":
                    GraphicFetcher.FetchBestiary(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int ar)) ? ar : 1);
                    return true;

                case "--olvidar":
                    GraphicFetcher.Forget(i + 1 < args.Length ? args[i + 1] : "");
                    return true;

                // Se corre DESPUÉS de olvidar, no una vez por olvido: renumerar
                // es lo único que no se puede deshacer entrada por entrada.
                case "--acomodar-graficos":
                    GraphicFetcher.Compact();
                    return true;

                // --reescalar-hoja <fileNum> <divisor>. El divisor 2 deja el arte
                // en x2: un cuarto de los píxeles, mismo tamaño en pantalla.
                case "--reescalar-hoja":
                    GraphicFetcher.RescaleSheet(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int hj)) ? hj : 0,
                        (i + 2 < args.Length && int.TryParse(args[i + 2], out int dv)) ? dv : 2);
                    return true;

                // --reescalar-todo <divisor>. Con 1 no achica: solo recorta el
                // aire y empaqueta. Saltea los pisos.
                case "--reescalar-todo":
                    GraphicFetcher.RescaleAll(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int dt)) ? dt : 1);
                    return true;

                // --fusionar-hojas <n> <n> <n>... [/<divisor>]. Junta varias hojas
                // en una. Sirve para el bicho que Austral guarda con una hoja por
                // dirección, como el Oso Polar.
                case "--fusionar-hojas":
                {
                    var cuales = new List<int>();
                    int divisorFusion = 1;

                    for (int j = i + 1; j < args.Length; j++)
                    {
                        if (args[j].StartsWith('/') && int.TryParse(args[j][1..], out int dvf))
                        {
                            divisorFusion = dvf;
                            continue;
                        }

                        if (!int.TryParse(args[j], out int hf))
                            break;

                        cuales.Add(hf);
                    }

                    GraphicFetcher.MergeSheets([.. cuales], divisorFusion);
                    return true;
                }

                // --partir-hoja <fileNum> <partes>. El inverso de fusionar, para
                // cuando el bicho no entra en una textura sola.
                case "--partir-hoja":
                    GraphicFetcher.SplitSheet(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int hpa)) ? hpa : 0,
                        (i + 2 < args.Length && int.TryParse(args[i + 2], out int npa)) ? npa : 2);
                    return true;

                // --achicar-hoja <fileNum> <porcentaje>. Achica el sprite DE
                // VERDAD: menos píxeles y más chico en pantalla. No confundir con
                // --reescalar-hoja, que compensa y deja el tamaño igual.
                case "--achicar-hoja":
                    GraphicFetcher.ShrinkSheet(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int hac)) ? hac : 0,
                        (i + 2 < args.Length && int.TryParse(args[i + 2], out int pac)) ? pac : 75);
                    return true;

                // --anclar-grafico <clave o prefijo> <px>. Baja el sprite al
                // dibujarlo. No toca píxeles: escribe un número en el índice.
                case "--anclar-grafico":
                    GraphicFetcher.AnchorGraphic(
                        i + 1 < args.Length ? args[i + 1] : "",
                        (i + 2 < args.Length && int.TryParse(args[i + 2], out int anc)) ? anc : 0);
                    return true;


                case "--centro-grafico":
                    GraphicsInspector.GraphicCenter(
                        i + 1 < args.Length ? args[i + 1] : "",
                        (i + 2 < args.Length && float.TryParse(args[i + 2],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float um))
                            ? um : 0.05f);
                    return true;

                case "--verificar-indices":
                    GraphicsInspector.CheckIndexes();
                    return true;

                case "--verificar-lector":
                    GraphicsInspector.CheckReader();
                    return true;

                case "--inspeccionar-grh":
                    GraphicsInspector.InspectGrh(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int g)) ? g : 1);
                    return true;

                case "--inspeccionar-obj":
                    GraphicsInspector.InspectObject(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int o)) ? o : 1);
                    return true;

                case "--inspeccionar-tiers":
                    GraphicsInspector.InspectTiers();
                    return true;

                case "--generar-layouts":
                    LayoutGenerator.Execute();
                    return true;

                case "--probar-layouts":
                    LayoutGenerator.Test(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int pl)) ? pl : 5);
                    return true;

                case "--validar-layouts":
                    LayoutGenerator.Validate();
                    return true;

                case "--emparejar-layouts":
                    LayoutGenerator.Balance(
                        (i + 1 < args.Length && int.TryParse(args[i + 1], out int tope)) ? tope : 44);
                    return true;

                case "--prueba-guardado":
                    ProbarGuardadoEnPreparacion();
                    return true;

                case "--validar":
                    DataValidator.Execute();
                    return true;

                case "--traza":
                    ulong semilla = (i + 1 < args.Length && ulong.TryParse(args[i + 1], out ulong s)) ? s : 1;
                    Trace(semilla);
                    return true;

                case "--balance":
                    int corridas = (i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) ? n : 10;

                    // Sin política nombrada corren las tres, que es lo que hace
                    // falta casi siempre: una sola cota no dice nada.
                    Policy? sola = (i + 2 < args.Length ? args[i + 2] : "") switch
                    {
                        "piso" => Policy.Piso,
                        "normal" => Policy.Normal,
                        "techo" => Policy.Techo,
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
            var a = Execute(seed, ConAcciones(seed));
            var b = Execute(seed, ConAcciones(seed));

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
        var x = Execute(7, new ActionLog());
        var y = Execute(8, new ActionLog());

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
        var a = Execute(seed, original);

        byte[] blob = BinaryLog.Write(original);
        var recuperado = BinaryLog.Read(blob);

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
        var partida = new Run(seed, Layouts.Choose(seed, DataLoader.Current), recuperado, DataLoader.Current);
        partida.Start();

        while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
        {
            partida.Advance();

            // El log se agotó con la corrida esperando al jugador. Sin este corte
            // el bucle gira para siempre: en pausa el tick no avanza.
            if (partida.WaitsForPlayer)
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
    private static void Balance(int corridas, Policy? sola)
    {
        if (sola is { } una)
        {
            BalanceDe(corridas, una);
            return;
        }

        // ⚠ Las tres de una, y de abajo hacia arriba. El número que importa es la
        // DISTANCIA entre las cotas —cuánto paga jugar mejor y cuánto paga
        // componer—, y leerla exige tenerlas juntas en la misma pantalla.
        BalanceDe(corridas, Policy.Piso);
        GD.Print("");
        BalanceDe(corridas, Policy.Normal);
        GD.Print("");
        BalanceDe(corridas, Policy.Techo);

        GD.Print("");
        GD.Print("── Objetivos ───────────────────────────────────────");

        // ⚠ Los objetivos de techo BAJARON, y no es una rendición: es lo que se
        // busca DESPUÉS de la tienda rotativa y del targeting, antes de recalibrar
        // las áreas 7 y 8. Medido antes de esos dos sistemas, normal daba 66 y
        // techo 67 — o sea que jugar mejor no pagaba nada, y ese era el problema
        // real, no que el techo no llegara a 100.
        //
        // La meta inmediata es SEPARACIÓN: piso 34-40, normal 63-72, techo 75-85.
        // Recién con techo claramente arriba de normal tiene sentido recalibrar
        // las dos últimas áreas para que una build excelente llegue a 90-100.
        GD.Print("   piso 34-40 · normal 63-72 · techo 75-85");
        GD.Print("   lo que importa es la DISTANCIA techo-normal: si es <5, jugar mejor no paga");
    }

    private static void BalanceDe(int corridas, Policy politica)
    {
        string nombre = politica switch
        {
            Policy.Piso => "piso",
            Policy.Techo => "techo",
            _ => "normal",
        };

        GD.Print($"── Balance · {nombre} · {corridas} corridas ───────────");

        int suma = 0, peor = int.MaxValue, mejor = 0;
        int sumaOro = 0, peorOro = int.MaxValue, mejorOro = 0;

        // Las métricas de todas las corridas, sumadas. Se reportan al final: el
        // porcentaje de compra de una sola corrida es ruido, el de veinticuatro es
        // una medición.
        var total = new RunMetrics();

        for (int i = 0; i < corridas; i++)
        {
            ulong seed = (ulong)(i + 1) * 2_654_435_761UL;
            var r = Execute(seed, ConAcciones(seed), politica);

            Acumular(total, r.Metrics);

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

        ReportarTienda(nombre, total, corridas);
    }

    /// <summary>Suma las métricas de una corrida al acumulado de la política.</summary>
    private static void Acumular(RunMetrics total, RunMetrics una)
    {
        total.ShopsVisited += una.ShopsVisited;
        total.ShopsWithSomethingUsable += una.ShopsWithSomethingUsable;
        total.GoldBeforeShopTotal += una.GoldBeforeShopTotal;
        total.GoldAfterShopTotal += una.GoldAfterShopTotal;
        total.GoldAtEnd += una.GoldAtEnd;
        total.RosterLevelTotal += una.RosterLevelTotal;
        total.ClassesAtMaxLevel += una.ClassesAtMaxLevel;

        total.OffersClass += una.OffersClass;
        total.OffersBronze += una.OffersBronze;
        total.OffersSilver += una.OffersSilver;
        total.OffersGold += una.OffersGold;
        total.OffersBlueprint += una.OffersBlueprint;
        total.OffersWeapon += una.OffersWeapon;

        total.BoughtClass += una.BoughtClass;
        total.BoughtBronze += una.BoughtBronze;
        total.BoughtSilver += una.BoughtSilver;
        total.BoughtGold += una.BoughtGold;
        total.BoughtBlueprint += una.BoughtBlueprint;
        total.BoughtWeapon += una.BoughtWeapon;

        total.GoldSpentClasses += una.GoldSpentClasses;
        total.GoldSpentBronze += una.GoldSpentBronze;
        total.GoldSpentSilver += una.GoldSpentSilver;
        total.GoldSpentGoldPerks += una.GoldSpentGoldPerks;
        total.GoldSpentBlueprints += una.GoldSpentBlueprints;
        total.GoldSpentWeapons += una.GoldSpentWeapons;
        total.GoldSpentLevels += una.GoldSpentLevels;
        total.GoldSpentDefenders += una.GoldSpentDefenders;

        total.GoldPerksObtained += una.GoldPerksObtained;
        total.UniquesCrafted += una.UniquesCrafted;
        total.WaveOfFirstUnique += una.WaveOfFirstUnique;
        total.PerksUpgraded += una.PerksUpgraded;
        total.GoldFromUpgrades += una.GoldFromUpgrades;

        total.SecondaryDamage += una.SecondaryDamage;
        total.OverkillDamage += una.OverkillDamage;
        total.TowerDamageTaken += una.TowerDamageTaken;

        for (int i = 0; i < total.DamageByClass.Length; i++)
        {
            total.DamageByClass[i] += una.DamageByClass[i];
            total.KillsByClass[i] += una.KillsByClass[i];
        }
    }

    /// <summary>
    /// El desglose de la tienda y de a dónde se fue el oro.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>«Terminó con 12.000 sin gastar» no dice nada solo.</b> Con la
    /// tienda rotativa el oro compra poder horizontal —clases—, vertical —perks— y
    /// habilitadores de build —planos—, así que sin desglose no se sabe si sobra
    /// oro, si los precios están mal, o si no había nada que valiera la pena.</para>
    ///
    /// <para>Los porcentajes de compra son la señal principal. Los rangos que el
    /// diseño busca: Bronce 40-65%, Plata 25-50%, Oro 15-35%, Plano 10-30%. Un Oro
    /// comprado el 90% de las veces está demasiado barato; el 3%, demasiado caro o
    /// demasiado temprano.</para>
    ///
    /// <para>Y el reparto del gasto: defensores + niveles 50-65%, perks 20-30%,
    /// planos 5-10%. Los dos extremos que hay que evitar son el 90% en perks —poner
    /// defensores dejó de competir— y el 2% —la tienda es decorativa—.</para>
    /// </remarks>
    private static void ReportarTienda(string nombre, RunMetrics m, int corridas)
    {
        if (m.ShopsVisited == 0)
        {
            GD.Print($"── {nombre} · tienda: no se abrió ninguna ──");
            return;
        }

        static int Porc(int comprados, int ofrecidos)
            => ofrecidos == 0 ? 0 : comprados * 100 / ofrecidos;

        GD.Print($"── {nombre} · tienda ────────────────────────────────");
        GD.Print($"   {m.ShopsVisited / corridas} tiendas por corrida · " +
                 $"{Porc(m.ShopsWithSomethingUsable, m.ShopsVisited)}% con algo pagable · " +
                 $"oro medio al abrir {m.GoldBeforeShopTotal / m.ShopsVisited} → " +
                 $"al cerrar {m.GoldAfterShopTotal / m.ShopsVisited}");

        GD.Print($"   ofertas/compras   clase {m.BoughtClass}/{m.OffersClass} ({Porc(m.BoughtClass, m.OffersClass)}%) · " +
                 $"bronce {m.BoughtBronze}/{m.OffersBronze} ({Porc(m.BoughtBronze, m.OffersBronze)}%) · " +
                 $"plata {m.BoughtSilver}/{m.OffersSilver} ({Porc(m.BoughtSilver, m.OffersSilver)}%)");

        GD.Print($"                     oro {m.BoughtGold}/{m.OffersGold} ({Porc(m.BoughtGold, m.OffersGold)}%) · " +
                 $"plano {m.BoughtBlueprint}/{m.OffersBlueprint} ({Porc(m.BoughtBlueprint, m.OffersBlueprint)}%) · " +
                 $"arma {m.BoughtWeapon}/{m.OffersWeapon} ({Porc(m.BoughtWeapon, m.OffersWeapon)}%)");
        GD.Print("   objetivo: bronce 40-65% · plata 25-50% · oro 15-35% · plano 10-30%");

        long gastado = m.GoldSpentClasses + m.GoldSpentBronze + m.GoldSpentSilver
                       + m.GoldSpentGoldPerks + m.GoldSpentBlueprints + m.GoldSpentWeapons
                       + m.GoldSpentLevels + m.GoldSpentDefenders;

        if (gastado > 0)
        {
            long perks = m.GoldSpentBronze + m.GoldSpentSilver + m.GoldSpentGoldPerks;
            long tablero = m.GoldSpentDefenders + m.GoldSpentLevels;

            GD.Print($"   gasto             tablero+niveles {tablero * 100 / gastado}% · " +
                     $"perks {perks * 100 / gastado}% · " +
                     $"planos {m.GoldSpentBlueprints * 100 / gastado}% · " +
                     $"clases {m.GoldSpentClasses * 100 / gastado}% · " +
                     $"armas {m.GoldSpentWeapons * 100 / gastado}%");
            GD.Print("   objetivo: tablero+niveles 50-65% · perks 20-30% · planos 5-10%");
        }

        // ⚠ Los dos números que desambiguan «sobró oro». Un roster lejos del máximo
        // CON oro sobrando dice que el sumidero NO se agotó, y entonces recortar el
        // ingreso arreglaría el síntoma equivocado.
        int maxNivel = DataLoader.Current.Classes.PerkSlotsPerClass;

        // Sin decimales y sin `double`: es un mensaje de consola que lee una
        // persona, y el promedio de «clases al máximo» se lee mejor como el total
        // sobre N que como 3,7.
        GD.Print($"   progresión        {m.RosterLevelTotal / corridas} niveles de roster " +
                 $"(máximo posible {ChoiceBuilder.All.Length * maxNivel}) · " +
                 $"{m.ClassesAtMaxLevel} clases al máximo en {corridas} corridas · " +
                 $"{m.GoldAtEnd / corridas} de oro sin gastar");

        // ⚠ Ocho Oro y cero Únicos quiere decir que la fusión existe y no se juega.
        // La oleada del primer Único dice si la composición llega a tiempo: en la
        // 90 el sistema existe pero no se juega; en la 20, llega demasiado fácil.
        int conUnico = m.UniquesCrafted > 0 ? m.WaveOfFirstUnique / System.Math.Max(1, m.UniquesCrafted) : 0;

        GD.Print($"   build             {m.GoldPerksObtained / corridas} Oro por corrida · " +
                 $"{m.UniquesCrafted} Únicos en {corridas} corridas · " +
                 $"primer Único ~oleada {conUnico}");

        // ⚠ Si el Oro fabricado se acerca al total, la mejora dejó de ser la
        // salida de las sobras y se volvió el camino principal al Oro — que es
        // lo que «maxCraftedGoldPerRun» existe para evitar.
        GD.Print($"   mejoras           {m.PerksUpgraded / corridas} por corrida · " +
                 $"{m.GoldFromUpgrades / corridas} de los Oro salieron de mejorar");

        // Si el secundario se acerca al directo, los comportamientos dejaron de ser
        // condimento y pasaron a ser la fuente de daño: es el síntoma de un tope
        // mal puesto, y es exactamente lo que le pasaba a Cadena.
        long directo = 0;

        foreach (long d in m.DamageByClass)
            directo += d;

        if (directo > 0)
        {
            GD.Print($"   daño              secundario {m.SecondaryDamage * 100 / directo}% del directo · " +
                     $"overkill {m.OverkillDamage * 100 / directo}%");
        }
    }

    internal readonly record struct Resultado(
        int Ticks, int Wave, int HpTorre, int Gold, RunState State,
        int Defenders, int ClasesRoster, int Backpack, RunMetrics Metrics);

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
        var datos = DataLoader.Current;
        var log = new ActionLog();
        var partida = new Run(seed, Layouts.Choose(seed, datos), log, datos);
        partida.Start();

        var pendientes = new Queue<(int Tick, Cell Cell)>(ColocacionesPlaneadas(seed, partida.Map, Policy.Normal));

        GD.Print($"── Traza · seed {seed} ────────────────────────────");
        GD.Print($"  ruta: {partida.Route.Length / Ints.ThousandthsPerCell} celdas");

        int oleadaAnterior = 0;
        int ultimoAviso = 0;

        while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
        {
            // ⚠ La elección corta la vuelta: desde el mercado, la preparación se
            // arma antes de abrirla y las dos banderas están prendidas a la vez.
            if (partida.WaitingForChoice)
            {
                Resolve(partida, log, Policy.Normal);
                partida.Advance();
                continue;
            }

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
                bool puso = ColocarUnoEnPreparacion(partida, log, pendientes, Policy.Normal);
                bool compro = !puso && ComprarLoQuePueda(partida, log, Policy.Normal);

                // Con la mochila trabada la compra rebota: hacer un lugar es lo que
                // deja que el oro siga teniendo adónde ir.
                bool libero = !puso && !compro && LiberarMochila(partida, log);

                // Último: el sumidero del sobrante. Poner un defensor más siempre
                // vale más que un nivel comprado.
                bool subio = !puso && !compro && !libero && SubirNivelSiSobra(partida, log);

                if (!puso && !compro && !libero && !subio)
                {
                    log.Add(new PlayerAction(
                        partida.ActionTick, ActionType.CallNextWave, 0, 0, 0));
                }

                // Ver la nota del mismo lugar en Correr: no se equipa en la vuelta
                // en que se tiró algo, o el índice apunta a otra cosa.
                if (!libero)
                    EquiparLoQuePueda(partida, log, Policy.Normal);

                partida.Advance();
                continue;
            }

            if (pendientes.Count > 0)
            {
                var (tick, celda) = pendientes.Peek();

                if (partida.Tick >= tick)
                {
                    var clase = ClaseMasCara(partida);

                    if (clase is not null && partida.CanPlace(clase.Value, celda))
                    {
                        log.Add(PlayerAction.Place(partida.ActionTick, clase.Value, celda));
                        pendientes.Dequeue();
                    }
                }
            }

            EquiparLoQuePueda(partida, log, Policy.Normal);
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
    private static Resultado Execute(ulong seed, ActionLog log, Policy politica = Policy.Normal)
        => Execute(seed, log, null, null, politica);

    /// <summary>
    /// La misma corrida, pero sobre un mapa cualquiera.
    /// </summary>
    /// <remarks>
    /// Con <c>null</c> usa el layout de prueba y el plan de colocación escrito a
    /// mano. Se le pasa un mapa cuando hay que <b>probar layouts nuevos</b>: que
    /// cumplan las reglas no dice si se pueden jugar, y eso solo lo contesta
    /// jugarlos.
    /// </remarks>
    internal static Resultado Execute(
        ulong seed, ActionLog log, Map? mapa, IEnumerable<(int Tick, Cell Cell)>? plan,
        Policy politica = Policy.Normal)
    {
        var partida = new Run(seed, mapa ?? Layouts.Choose(seed, DataLoader.Current), log, DataLoader.Current);

        // ⚠ Los planos van ANTES de Empezar: el sorteo de los Oro del jefe pesa
        // ×3 a la pareja de una receta conocida, así que aprenderlos después
        // cambiaría la oferta a mitad de corrida en vez de desde el arranque.
        //
        // Los planos son la única progresión permanente del juego, y por eso
        // «build optimizada» quiere decir cuenta veterana: sin ellos solo se puede
        // armar una de las nueve recetas y la cota de arriba mediría a un jugador
        // nuevo jugando bien, que no es lo que Resumen_Balance pregunta.
        if (politica == Policy.Techo)
        {
            foreach (var plano in DataLoader.Current.Blueprints.All)
                partida.LearnBlueprint(plano.Key);
        }

        partida.Start();

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
            //
            // ⚠ Y ahora la elección CORTA la vuelta.
            //
            // Desde que el mercado junta las dos pantallas, la preparación se arma
            // ANTES de abrir la elección, así que `WaitingForChoice` y
            // `WaitingForWaveStart` son verdaderas al mismo tiempo. Sin este
            // `continue`, la vuelta seguía de largo hasta el `CallNextWave` de
            // abajo y lanzaba la oleada con la elección sin resolver — que en el
            // tick en que `HacerLugarParaElOro` tira algo en vez de elegir, es
            // perder los ocho Oro de la corrida otra vez.
            if (partida.WaitingForChoice)
            {
                if (!HacerLugarParaElOro(partida, log))
                    Resolve(partida, log, politica);

                partida.Advance();
                continue;
            }

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
                bool avanzado = politica != Policy.Piso;

                // Fusionar va PRIMERO de las avanzadas: saca dos Oro de la mochila
                // y devuelve un Único en un solo slot, así que destraba lugar en
                // los dos lados a la vez. Solo el techo compone.
                bool fusiono = !puso && politica == Policy.Techo
                               && FusionarSiPuede(partida, log);

                // Mejorar va antes que comprar y antes que tirar: cuatro Bronce
                // que nadie va a equipar se convierten en una Plata y liberan tres
                // lugares. Tirarlos libera lo mismo y no devuelve nada, así que
                // toda vez que se pueda mejorar, tirar es la peor opción.
                bool mejoro = !puso && !fusiono && avanzado
                              && MejorarSiPuede(partida, log, politica);

                // ⚠ EL PISO TAMBIÉN COMPRA LO GRATIS, y antes no compraba nada.
                //
                // «No usar la tienda» dejó de ser una política jugable el día que
                // la pantalla de elección se fusionó con la tienda: la primera
                // clase de la corrida sale de ahí, así que un bot que no compra
                // arranca con el roster vacío y no lo llena nunca. Medido: las
                // doce seeds del piso muertas en la oleada 7, todas iguales.
                //
                // Lo que el piso sigue sin hacer es GASTAR: toma el cupo gratis y
                // nada más. Eso conserva lo que la cota de abajo mide —un jugador
                // que pone fichas y no administra oro— sin pedirle que adivine una
                // mecánica que el juego ya no tiene.
                bool compro = !puso && !fusiono && !mejoro
                              && (avanzado
                                  ? ComprarLoQuePueda(partida, log, politica)
                                  : ComprarSoloLoGratis(partida, log));

                // Con la mochila trabada la compra rebota: hacer un lugar es lo que
                // deja que el oro siga teniendo adónde ir.
                bool libero = !puso && !fusiono && !mejoro && !compro && avanzado
                              && LiberarMochila(partida, log);

                // ⚠ Las dos decisiones que no cuestan oro van ANTES del sumidero,
                // y solo en el techo: targeting y comportamiento activo son
                // exactamente lo que separa jugar bien de jugar, y si las tres
                // políticas las usaran, las tres cotas medirían lo mismo y las dos
                // mecánicas no aparecerían en la medición.
                //
                // Gratis, así que no compiten con nada: van antes de gastar.
                bool apunto = !puso && !fusiono && !mejoro && !compro && !libero
                              && politica == Policy.Techo
                              && AjustarTargeting(partida, log);

                bool eligio = !puso && !fusiono && !mejoro && !compro && !libero && !apunto
                              && politica == Policy.Techo
                              && SetBehavior(partida, log);

                // Último: el sumidero del sobrante. Poner un defensor más siempre
                // vale más que un nivel comprado.
                bool subio = !puso && !fusiono && !mejoro && !compro && !libero && !apunto
                             && !eligio && avanzado && SubirNivelSiSobra(partida, log);

                if (!puso && !fusiono && !mejoro && !compro && !libero && !apunto && !eligio && !subio)
                {
                    log.Add(new PlayerAction(
                        partida.ActionTick, ActionType.CallNextWave, 0, 0, 0));
                }

                // ⚠ NO se equipa en la misma vuelta que se fusionó o se tiró algo.
                //
                // Las acciones se aplican en orden al avanzar, pero los índices de
                // mochila se calculan TODOS contra el estado de antes: una fusión
                // saca dos ítems y un descarte saca uno, así que el índice del
                // equipar que va detrás apunta a otra cosa. Comprar sí es seguro
                // —el ítem entra al final y no corre a los de antes—.
                if (!fusiono && !mejoro && !libero)
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

                    if (clase is not null && partida.CanPlace(clase.Value, celda))
                    {
                        log.Add(PlayerAction.Place(partida.ActionTick, clase.Value, celda));
                        pendientes.Dequeue();
                    }
                }
            }

            // Equipar lo primero que sirva de la mochila, para que los drops no
            // se junten sin usarse.
            EquiparLoQuePueda(partida, log, politica);

            partida.Advance();
        }

        // Una corrida que se corta contra el techo de ticks nunca pasó por
        // FaseChequeos con un estado terminal, así que el oro final quedaría en 0
        // y arrastraría el promedio hacia abajo sin que nadie lo note.
        if (partida.Metrics.GoldAtEnd == 0)
            partida.Metrics.GoldAtEnd = partida.Gold;

        // ⚠ Los dos números que desambiguan «sobró oro». Se toman al final y no
        // durante, porque lo que interesa es dónde quedó parada la progresión: un
        // roster lejos del máximo CON oro sobrando dice que el sumidero no se
        // agotó, y entonces recortar el ingreso sería arreglar el síntoma
        // equivocado.
        foreach (var ficha in partida.Roster.Classes)
        {
            partida.Metrics.RosterLevelTotal += ficha.Level;

            if (ficha.Level >= ficha.MaxLevel)
                partida.Metrics.ClassesAtMaxLevel++;
        }

        return new Resultado(
            partida.Tick, partida.Wave, partida.Tower.Hp, partida.Gold, partida.State,
            partida.Defenders.Count, partida.Roster.Classes.Count, partida.Backpack.Used,
            partida.Metrics);
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
    /// <summary>
    /// Resuelve la elección: elige la mejor que se pueda pagar, o la saltea.
    /// </summary>
    /// <remarks>
    /// ⚠ Lo único que abre esto es <b>el Oro del jefe</b>. La pantalla de elección
    /// —contratar o subir de nivel— se fusionó con la tienda, así que esas dos
    /// decisiones pasan por <c>ComprarLoQuePueda</c> como cualquier otra oferta.
    /// </remarks>
    private static void Resolve(Run partida, ActionLog log, Policy politica)
    {
        int i = MejorOpcion(partida, politica);

        log.Add(i >= 0
            ? PlayerAction.Choose(partida.ActionTick, i)
            : new PlayerAction(partida.ActionTick, ActionType.SkipChoice, 0, 0, 0));
    }

    /// <summary>
    /// Cuál de los Oro que ofrece el jefe se lleva. <c>-1</c> = ninguno sirve.
    /// </summary>
    /// <remarks>
    /// Piso y normal se quedan con el primero: no intentan valuar comportamientos,
    /// y es a propósito, porque elegir bien los Oro mide el <b>techo</b> del juego
    /// y esas dos políticas miden otra cosa.
    ///
    /// El techo sí compone: prefiere el Oro que <b>completa</b> una receta que ya
    /// tiene a medias. Es la mitad de lo que hace que los Únicos sean un objetivo
    /// de build y no una lotería —la otra mitad es el peso de sinergia del sorteo,
    /// que ya vive en la simulación—.
    /// </remarks>
    private static int MejorOpcion(Run partida, Policy politica)
    {
        var opciones = partida.OpenChoices;
        int mejor = -1;

        for (int i = 0; i < opciones.Length; i++)
        {
            if (opciones[i].Kind != ChoiceType.Perk)
                continue;

            if (mejor < 0)
                mejor = i;

            if (politica == Policy.Techo && CompletaUnPlano(partida, opciones[i].PerkId))
                mejor = i;
        }

        return mejor;
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
        foreach (var plano in DataLoader.Current.Blueprints.All)
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

        if (partida.Backpack.Used < DataLoader.Current.Curves.Defenders.BackpackSlots)
            return false;

        var cosas = partida.Backpack.Items;
        var perks = DataLoader.Current.Perks;

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

        log.Add(new PlayerAction(partida.ActionTick, ActionType.Discard, peor, 0, 0));

        return true;
    }

    private static bool FusionarSiPuede(Run partida, ActionLog log)
    {
        var cosas = partida.Backpack.Items;
        var perks = DataLoader.Current.Perks;

        for (int a = 0; a < cosas.Count; a++)
        {
            if (cosas[a].Kind != ItemType.Perk || perks.Get(cosas[a].Id).Tier != PerkTier.Gold)
                continue;

            for (int b = a + 1; b < cosas.Count; b++)
            {
                if (cosas[b].Kind != ItemType.Perk || perks.Get(cosas[b].Id).Tier != PerkTier.Gold)
                    continue;

                var plano = DataLoader.Current.Blueprints.Find(cosas[a].Id, cosas[b].Id);
                if (plano is null)
                    continue;

                var unico = perks.Get(plano.ResultingUnique);

                foreach (var ficha in partida.Roster.Classes)
                {
                    if (!unico.WorksFor(ficha.DefenderClass))
                        continue;

                    log.Add(new PlayerAction(
                        partida.ActionTick, ActionType.Fuse, a, b, (int)ficha.DefenderClass));

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Convierte sobras en un perk del tier de arriba, si la mochila aprieta.
    /// </summary>
    /// <remarks>
    /// <para><b>Solo cuando la mochila va por dos tercios.</b> Mejorar en cuanto
    /// hay cuatro Bronce quemaría perks que todavía tenían dónde entrar; el
    /// sentido de la mecánica es darle destino a lo que ya sobra, y medirla de
    /// otra forma mediría un jugador que no existe.</para>
    ///
    /// <para><b>Plata → Oro solo en el techo.</b> Fabricar Oro es componer, que
    /// es exactamente lo que separa esa política de las otras dos; Bronce → Plata
    /// es higiene de mochila y la hace también el normal. Si las tres hicieran lo
    /// mismo, las tres cotas medirían lo mismo.</para>
    ///
    /// <para>Elige primero los que <b>ninguna clase del roster puede equipar</b>:
    /// son basura garantizada, y gastarlos primero es lo que un jugador haría.</para>
    /// </remarks>
    private static bool MejorarSiPuede(Run partida, ActionLog log, Policy politica)
    {
        int cap = DataLoader.Current.Curves.Defenders.BackpackSlots;

        if (partida.Roster.Classes.Count == 0 || partida.Backpack.Used * 3 < cap * 2)
            return false;

        // ⚠ `GoldPoolReady` no es una optimización: sin él, mejorar a Oro antes
        // del primer jefe rebota en la simulación —no hay pool de dónde sacarlo—
        // y el corredor vuelve a intentar la misma acción con el reloj congelado,
        // o sea que se cuelga sin avanzar un tick.
        //
        // Plata primero: rinde más por lugar liberado, y el tope de Oro fabricado
        // hace que la ventana sea corta.
        if (politica == Policy.Techo && partida.GoldPoolReady
            && partida.CraftedGold < partida.MaxCraftedGold
            && Intentar(PerkTier.Silver))
        {
            return true;
        }

        return Intentar(PerkTier.Bronze);

        bool Intentar(PerkTier tier)
        {
            int cuantos = partida.UpgradeCost(tier);

            if (cuantos < 2)
                return false;

            var cosas = partida.Backpack.Items;
            var inutiles = new List<int>();
            var resto = new List<int>();

            for (int i = 0; i < cosas.Count; i++)
            {
                if (cosas[i].Kind != ItemType.Perk)
                    continue;

                var def = DataLoader.Current.Perks.Get(cosas[i].Id);

                if (def.Tier != tier)
                    continue;

                bool leSirveAAlguna = false;

                foreach (var ficha in partida.Roster.Classes)
                {
                    if (def.WorksFor(ficha.DefenderClass))
                    {
                        leSirveAAlguna = true;
                        break;
                    }
                }

                (leSirveAAlguna ? resto : inutiles).Add(i);
            }

            if (inutiles.Count + resto.Count < cuantos)
                return false;

            int mascara = 0;
            int puestos = 0;

            foreach (int i in inutiles)
            {
                if (puestos == cuantos)
                    break;

                mascara |= 1 << i;
                puestos++;
            }

            foreach (int i in resto)
            {
                if (puestos == cuantos)
                    break;

                mascara |= 1 << i;
                puestos++;
            }

            log.Add(new PlayerAction(
                partida.ActionTick, ActionType.Upgrade, mascara, (int)tier, 0));

            return true;
        }
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
    private static DefenderClass? ClaseParaPoner(Run partida, Policy politica)
    {
        if (politica == Policy.Techo && ClaseFavorita(partida) is { } favorita
            && DataLoader.Current.StatsOf(favorita).Cost <= partida.Gold)
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
            // ⚠ La clase sellada no se puede poner todavía. Sin esto el bot
            // elegía la segunda gratis del arranque —suele ser la más fuerte,
            // porque la elige por puntaje—, `CanPlace` la rechazaba y el tablero
            // quedaba vacío las tres primeras oleadas: el piso medido se desplomaba
            // por un bug del bot y no por el balance.
            if (ficha.Sealed)
                continue;

            var stats = DataLoader.Current.StatsOf(ficha.DefenderClass);

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
            // Misma razón que en ClaseFavorita: una clase sellada no entra al
            // tablero, así que elegirla es no poner nada.
            if (ficha.Sealed)
                continue;

            int costo = DataLoader.Current.StatsOf(ficha.DefenderClass).Cost;

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
    /// ¿Este Oro conviene <b>guardarlo</b> en vez de equiparlo? Solo para el techo.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Un Oro equipado ya no se puede fusionar</b>: <c>Run.Fuse</c> toma
    /// índices de mochila, y sacarlo del slot lo destruye. Así que equipar el
    /// primer Oro que cae es renunciar a la mitad de una receta sin decirlo.</para>
    ///
    /// <para>La regla vieja era la presión de mochila —guardar mientras hubiera 3
    /// lugares libres—, y se rompió cuando la tienda empezó a llenar la mochila:
    /// con el bolso siempre apretado, todos los Oro se equipaban al instante y
    /// <b>ninguna corrida fabricaba un solo Único</b>. Medido: 0 en 24 corridas.</para>
    ///
    /// <para>La regla nueva es la que usaría alguien que persigue una receta:
    /// <b>guardar el Oro que es media receta conocida y cuya pareja todavía no
    /// tiene; equipar el que no le sirve a ninguna</b>. Un Oro que no participa en
    /// nada rinde más puesto en una clase que esperando en el bolso.</para>
    ///
    /// <para>Queda la válvula de siempre para que la mochila no se trabe del todo:
    /// sin ningún lugar libre se equipa igual, porque una mochila trabada apaga la
    /// tienda y no compra nada a cambio.</para>
    /// </remarks>
    private static bool GuardarloParaFusionar(Run partida, int idPerk)
    {
        if (partida.Backpack.Used >= DataLoader.Current.Curves.Defenders.BackpackSlots)
            return false;

        foreach (var plano in DataLoader.Current.Blueprints.All)
        {
            if (plano.GoldA == idPerk || plano.GoldB == idPerk)
                return true;
        }

        return false;
    }

    private static void EquiparLoQuePueda(Run partida, ActionLog log, Policy politica)
    {
        var cosas = partida.Backpack.Items;

        for (int i = 0; i < cosas.Count; i++)
        {
            if (cosas[i].Kind == ItemType.Perk)
            {
                var def = DataLoader.Current.Perks.Get(cosas[i].Id);

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
                if (politica == Policy.Techo && def.Tier == PerkTier.Gold
                    && GuardarloParaFusionar(partida, def.Id))
                {
                    continue;
                }

                var destino = DondeEquipar(partida, def, politica);

                if (destino is { } clase)
                {
                    log.Add(new PlayerAction(
                        partida.ActionTick, ActionType.EquipPerk, i, (int)clase, 0));
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
                    log.Add(new PlayerAction(
                        partida.ActionTick, ActionType.Equip, i, (int)ficha.DefenderClass, 0));
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
    private static DefenderClass? DondeEquipar(Run partida, PerkDefinition def, Policy politica)
    {
        DefenderClass? mejor = null;
        int mejorPuestos = -1;

        foreach (var ficha in partida.Roster.Classes)
        {
            if (!PuedeEquipar(ficha, def))
                continue;

            if (politica != Policy.Techo)
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
            var puesto = DataLoader.Current.Perks.Get(id);

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
        if (partida.Backpack.Used < DataLoader.Current.Curves.Defenders.BackpackSlots)
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

            var def = DataLoader.Current.Perks.Get(cosas[i].Id);
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
                log.Add(new PlayerAction(partida.ActionTick, ActionType.Discard, i, 0, 0));
                return true;
            }
        }

        // 2) Un arma de más: si alguna ficha estuviera sin arma, EquiparLoQuePueda
        //    ya la habría puesto, así que llegar acá quiere decir que sobran.
        for (int i = 0; i < cosas.Count; i++)
        {
            if (cosas[i].Kind != ItemType.Weapon)
                continue;

            log.Add(new PlayerAction(partida.ActionTick, ActionType.SellGear, i, 0, 0));
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
        var economia = DataLoader.Current.Curves.Economy;

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

        log.Add(new PlayerAction(
            partida.ActionTick, ActionType.BuyLevel, (int)mejor.Value, 0, 0));

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
    /// <summary>
    /// La compra del piso: solo lo que no cuesta oro.
    /// </summary>
    /// <remarks>
    /// ⚠ Sin esto el piso no tiene forma de conseguir su primera clase, porque la
    /// pantalla de elección que se la regalaba ya no existe. No es «el piso ahora
    /// compra»: es que tomar el cupo gratis dejó de ser una compra y pasó a ser el
    /// arranque de la corrida.
    /// </remarks>
    private static bool ComprarSoloLoGratis(Run partida, ActionLog log)
    {
        var stock = partida.Shop.Stock;
        bool mochilaLlena = partida.Backpack.Used >= DataLoader.Current.Curves.Defenders.BackpackSlots;

        for (int i = 0; i < stock.Count; i++)
        {
            if (stock[i].Sold || !stock[i].Free)
                continue;

            // Las mismas guardas que la compra normal: una acción que rebota no
            // marca la oferta como vendida, y el bucle de preparación —con el
            // reloj congelado— la volvería a encolar para siempre.
            if (stock[i].Kind is ShopKind.Perk or ShopKind.Weapon && mochilaLlena)
                continue;

            if (stock[i].Kind == ShopKind.Class && partida.Roster.Has((DefenderClass)stock[i].Id))
                continue;

            if (stock[i].Kind == ShopKind.Blueprint && partida.KnowsBlueprint(stock[i].Id))
                continue;

            if (stock[i].Kind == ShopKind.Level
                && partida.Roster.Find((DefenderClass)stock[i].Id) is { } f
                && f.Level >= DataLoader.Current.Curves.Shop.MaxLevelOnSale)
            {
                continue;
            }

            log.Add(new PlayerAction(partida.ActionTick, ActionType.Buy, i, 0, 0));
            return true;
        }

        return false;
    }

    private static bool ComprarLoQuePueda(Run partida, ActionLog log, Policy politica)
    {
        var stock = partida.Shop.Stock;
        bool mochilaLlena = partida.Backpack.Used >= DataLoader.Current.Curves.Defenders.BackpackSlots;

        // ⚠ EL TECHO COMPRA POR PRIORIDAD, NO POR ÍNDICE, y esa es una de las
        // cosas que lo separan de `normal`.
        //
        // Comprar la primera oferta que entre en el bolsillo es lo que haría
        // alguien que no mira: con la mochila llena la mitad del tiempo, el Oro
        // —que sale una vez cada tres o cuatro tiendas— se pierde porque para
        // cuando el bucle llega a esa ranura ya no queda lugar. Medido: el techo
        // compraba 0 de 13 Oro ofrecidos, y fabricaba cero Únicos.
        //
        // Ordenar por lo que vale hace lo que haría un jugador que sí mira: el Oro
        // primero, el plano después, y el Bronce recién cuando no hay nada mejor.
        int mejor = -1;
        int mejorPrioridad = int.MinValue;

        for (int i = 0; i < stock.Count; i++)
        {
            // ⚠ Cost y NO Price. Con el cupo gratis, `Price` sigue siendo el
            // precio real de la oferta y `Cost` es lo que sale hoy. Comparar
            // contra Price descartaba la oferta gratis del arranque —el jugador
            // arranca con cero oro— y el bot moría en la oleada 7 con el roster
            // vacío, en las cinco seeds.
            if (stock[i].Sold || stock[i].Cost > partida.Gold)
                continue;

            // ⚠ Solo dos de las cuatro categorías necesitan lugar de mochila.
            // Reclutar toca el roster y aprender un plano toca el conocimiento de
            // la corrida, así que con la mochila llena esas dos SIGUEN siendo
            // comprables — y a esa altura son justamente en lo que conviene gastar.
            //
            // Pero perks y armas rebotan sin gastar oro, y una acción que rebota no
            // saca la oferta del stock: el bucle de preparación la volvería a
            // encolar para siempre y la corrida se colgaría con el reloj congelado.
            bool necesitaMochila = stock[i].Kind is ShopKind.Perk or ShopKind.Weapon;

            if (necesitaMochila && mochilaLlena)
                continue;

            // ⚠ Las dos que rebotarían también se saltean acá, y no es paranoia:
            // una acción que rebota NO marca la oferta como vendida, así que el
            // bucle de preparación —con el reloj congelado— la volvería a encolar
            // para siempre y la corrida no terminaría nunca.
            //
            // Un plano ya conocido: la tienda no lo ofrece, pero el techo arranca
            // con los nueve aprendidos. Una clase ya reclutada: la tienda tampoco
            // la ofrece, pero el chequeo queda igual — es la misma trampa que ya
            // colgó una medición de 24 corridas, y la próxima política que se
            // escriba no tiene por qué acordarse.
            if (stock[i].Kind == ShopKind.Blueprint && partida.KnowsBlueprint(stock[i].Id))
                continue;

            if (stock[i].Kind == ShopKind.Class && partida.Roster.Has((DefenderClass)stock[i].Id))
                continue;

            // Un nivel que ya se compró desde la mochila: la oferta quedó vieja y
            // la simulación la retira sin cobrar. Saltearla acá evita gastar una
            // acción del log en algo que no hace nada.
            if (stock[i].Kind == ShopKind.Level
                && partida.Roster.Find((DefenderClass)stock[i].Id) is { } f
                && f.Level >= DataLoader.Current.Curves.Shop.MaxLevelOnSale)
            {
                continue;
            }

            // ⚠ Un plano cuyas DOS piezas faltan es oro tirado, y encima distorsiona
            // la medición: el bot compraba el 90% de los planos ofrecidos contra un
            // objetivo de 10-30%, y ese 90% no medía el precio sino que el bot
            // compraba lo primero que veía. Un plano se compra cuando ya se tiene
            // una de sus mitades, que es cuando la apuesta tiene sentido.
            if (stock[i].Kind == ShopKind.Blueprint && !MediaRecetaEnMano(partida, stock[i].Id))
                continue;

            int prioridad = politica == Policy.Techo ? Prioridad(stock[i]) : -i;

            if (prioridad > mejorPrioridad)
            {
                mejorPrioridad = prioridad;
                mejor = i;
            }
        }

        if (mejor < 0)
            return false;

        log.Add(new PlayerAction(partida.ActionTick, ActionType.Buy, mejor, 0, 0));
        return true;
    }

    /// <summary>
    /// Cuánto vale una oferta para el techo. <b>Más alto se compra primero.</b>
    /// </summary>
    /// <remarks>
    /// El orden sale de cuán escaso es lo que se compra, no de cuánto cuesta. El
    /// Oro aparece una vez cada tres o cuatro tiendas y es la entrada a los Únicos;
    /// el Bronce sale en casi todas y siempre va a haber otro.
    /// </remarks>
    private static int Prioridad(ShopOffer oferta) => oferta.Kind switch
    {
        ShopKind.Blueprint => 40,

        // ⚠ Subió de 20 a 30 —arriba de la Plata— cuando reclutar dejó de ser
        // gratis pasadas las `freeClasses`. Desde entonces la tienda es la ÚNICA
        // puerta para una clase nueva, y dejarla debajo de un perk de Plata
        // medía un jugador que se queda con dos clases toda la corrida.
        ShopKind.Class => 30,

        // ⚠ Sin este caso, un ShopKind.Level caía en el `_ =>` de abajo y buscaba
        // un PERK con el id de una CLASE: Perks.Get(3) por «Asesino». Subir de
        // nivel es la mitad de lo que era la pantalla de elección —«ancho o
        // profundo»— y vale como una clase nueva: abre un slot de perk y engorda
        // todas las fichas de esa clase que ya estén puestas.
        ShopKind.Level => 28,

        ShopKind.Weapon => 10,
        _ => DataLoader.Current.Perks.Get(oferta.Id).Tier switch
        {
            PerkTier.Bronze => 15,
            PerkTier.Silver => 25,
            _ => 50,
        },
    };

    /// <summary>¿El jugador ya tiene una de las dos piezas de este plano?</summary>
    private static bool MediaRecetaEnMano(Run partida, int indicePlano)
    {
        var todos = DataLoader.Current.Blueprints.All;

        if (indicePlano < 0 || indicePlano >= todos.Count)
            return false;

        var plano = todos[indicePlano];

        return Tiene(partida, plano.GoldA) || Tiene(partida, plano.GoldB);
    }

    /// <summary>
    /// Le pone a cada defensor recién puesto el targeting que le conviene.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Solo el techo</b>. El targeting es exactamente la clase de
    /// decisión que separa jugar bien de jugar: si piso y normal también lo usaran,
    /// las tres cotas medirían lo mismo y la mecánica no aparecería en la
    /// medición.</para>
    ///
    /// <para>La heurística son los cuatro ejemplos del análisis, y son los que
    /// haría cualquiera mirando la ficha: el que acumula golpes al mismo objetivo
    /// quiere el que más aguanta, el que remata quiere al más herido, el que pega
    /// más fuerte contra los que muerden la Torre quiere al que está más cerca de
    /// ella, y el resto se queda con el más adelantado.</para>
    ///
    /// <para>Devuelve <c>true</c> si emitió una acción: como todo lo demás en la
    /// preparación, va de a una por vuelta.</para>
    /// </remarks>
    private static bool AjustarTargeting(Run partida, ActionLog log)
    {
        foreach (var d in partida.Defenders)
        {
            var stats = partida.Roster.StatsOf(d.DefenderClass, partida.Backpack);

            var quiere = stats.Behavior switch
            {
                PerkEffect.Focus or PerkEffect.Perforation => TargetingMode.Strongest,
                PerkEffect.Opportunist or PerkEffect.Apocalypse => TargetingMode.Weakest,
                PerkEffect.ThresholdGuard or PerkEffect.WrathOfGod => TargetingMode.NearestTower,
                _ => TargetingMode.First,
            };

            if (d.Targeting == quiere)
                continue;

            log.Add(new PlayerAction(
                partida.ActionTick, ActionType.SetTargeting, d.PlacementId, (int)quiere, 0));

            return true;
        }

        return false;
    }

    /// <summary>
    /// Elige el comportamiento activo cuando la clase tiene perk <b>y</b> arma.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Solo el techo</b>, por lo mismo que el targeting: es una decisión
    /// que separa jugar bien de jugar.</para>
    ///
    /// <para>La regla es una sola y es la que se justifica sola: <b>un Único gana
    /// siempre</b>. Costó dos jefes, un plano y una fusión; la habilidad del arma
    /// vino en un drop y la corrida siguiente trae otra. Con un Oro la decisión ya
    /// no es obvia, así que se deja ganar al arma —que es el comportamiento por
    /// defecto— y no se toca.</para>
    /// </remarks>
    private static bool SetBehavior(Run partida, ActionLog log)
    {
        foreach (var ficha in partida.Roster.Classes)
        {
            if (!partida.HasBehaviorConflict(ficha.DefenderClass))
                continue;

            bool tieneUnico = false;

            foreach (int id in ficha.Perks)
                tieneUnico |= DataLoader.Current.Perks.Get(id).Tier == PerkTier.Unique;

            if (ficha.PreferPerkBehavior == tieneUnico)
                continue;

            log.Add(new PlayerAction(
                partida.ActionTick, ActionType.SetBehavior,
                (int)ficha.DefenderClass, tieneUnico ? 1 : 0, 0));

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
    /// campo a 20×11 quedaron todas afuera, <c>CanPlace</c> las rechazó, y como
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
        ulong seed, Map mapa, Policy politica)
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
        bool lleno = politica != Policy.Piso;
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
                if (!mapa.InGrid(v) || !mapa.IsPlaceable(v) || !usadas.Add(v))
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
            var datos = DataLoader.Current;
            var mapa = Layouts.Choose(seed, datos);

            // Se juega hasta que la simulación abra la preparación por TERCERA
            // vez: la primera es antes de la oleada 1 y no prueba gran cosa.
            var log = new ActionLog();
            var partida = new Run(seed, mapa, log, datos);
            partida.Start();

            int aperturas = 0;
            bool yaContada = false;
            var pendientes = new Queue<(int Tick, Cell Cell)>(
                ColocacionesPlaneadas(seed, mapa, Policy.Normal));

            while (partida.State == RunState.InProgress && partida.Tick < TechoTicks)
            {
                if (partida.WaitingForChoice)
                    log.Add(PlayerAction.Choose(partida.ActionTick, MejorOpcion(partida, Policy.Normal)));

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

                    if (!ColocarUnoEnPreparacion(partida, log, pendientes, Policy.Normal))
                    {
                        log.Add(new PlayerAction(
                            partida.ActionTick, ActionType.CallNextWave, 0, 0, 0));
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

                        if (clase is not null && partida.CanPlace(clase.Value, celda))
                        {
                            log.Add(PlayerAction.Place(partida.ActionTick, clase.Value, celda));
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
            var recuperado = BinaryLog.Read(BinaryLog.Write(log));

            var retomada = new Run(seed, Layouts.Choose(seed, datos), recuperado, datos);
            retomada.Start();

            int vueltas = 0;

            while (retomada.State == RunState.InProgress && retomada.Tick < TechoTicks)
            {
                retomada.Advance();
                vueltas++;

                if (retomada.WaitsForPlayer)
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
        Run partida, ActionLog log, Queue<(int Tick, Cell Cell)> pendientes, Policy politica)
    {
        if (pendientes.Count == 0)
            return false;

        var (_, celda) = pendientes.Peek();
        var clase = ClaseParaPoner(partida, politica);

        // Sin clase asequible o sin lugar válido, no se insiste: se arranca la
        // oleada y se reintenta en la preparación siguiente, con más oro.
        if (clase is null || !partida.CanPlace(clase.Value, celda))
            return false;

        log.Add(PlayerAction.Place(partida.ActionTick, clase.Value, celda));
        pendientes.Dequeue();

        return true;
    }
}
