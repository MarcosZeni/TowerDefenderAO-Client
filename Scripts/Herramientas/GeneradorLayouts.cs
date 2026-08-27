using System.Collections.Generic;
using System.IO;
using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Los layouts escritos como vértices, no pintados a mano.
/// </summary>
/// <remarks>
/// <para>⚠ <b>No es un generador aleatorio.</b> Cada uno está escrito a mano; lo
/// único automático es trazar los tramos rectos entre vértices y abrir el borde
/// colocable. Un generador random saca rutas uniformemente mediocres, y lo que
/// hace buena a una ruta —un cuello, una recta larga para el cazador, una
/// espiral donde el daño en área paga— es diseño.</para>
///
/// <para>Escribir vértices en vez de dibujar píxeles tiene dos ventajas: el
/// camino sale <b>sin ramas por construcción</b>, y el largo se ajusta moviendo
/// un número.</para>
///
/// <para>Los tres primeros son los que dibujó el usuario a mano, y fijan el
/// estilo: rutas de <b>27 a 31</b> celdas, entrada en un borde, Torre adentro.
/// El resto sigue ese molde — si uno midiera 50 celdas sería mucho más fácil que
/// los demás, y con ranking por velocidad la seed decidiría el puesto antes de
/// que el jugador juegue.</para>
/// </remarks>
public static class GeneradorLayouts
{
    /// <summary>El largo al que apuntan todos, tomado de los tres dibujados.</summary>
    private const int LargoObjetivo = 29;

    /// <summary>Cuánto se le tolera. ±4 sobre 29 es un 14 %.</summary>
    private const int Tolerancia = 4;

    /// <summary>
    /// Cada receta es una polilínea: tramos rectos, cada par de vértices alineado.
    /// </summary>
    /// <remarks>
    /// El primero es la entrada y el último la Torre. La Torre tiene que quedar a
    /// 2+ celdas del borde, y dos tramos paralelos <b>no pueden ser vecinos</b>:
    /// si se tocan, el trazador ve una bifurcación.
    /// </remarks>
    /// <remarks>
    /// ⚠ <b>Los tramos paralelos van a DOS celdas, no a cuatro.</b> Es lo que
    /// separa un layout jugable de uno imposible, y salió de medirlo: con tramos
    /// anchos cada defensor cubre una sola pasada y las corridas mueren en la
    /// oleada 1; plegado, la franja de en medio cubre tres o cuatro y la corrida
    /// llega a la 30. La ruta más larga que entra en el campo —65 celdas— rinde
    /// PEOR que una plegada de 31, así que lo que importa no es el recorrido sino
    /// cuántas pasadas ve un mismo defensor.
    /// </remarks>
    private static readonly (string Name, Cell[] Vertices)[] Recetas =
    [
        // Serpiente plegada: tres tramos horizontales a dos celdas. Las franjas de
        // en medio cubren dos pasadas cada una.
        ("serpiente", [
            new Cell(3, 2), new Cell(14, 2), new Cell(14, 4),
            new Cell(5, 4), new Cell(5, 6), new Cell(13, 6),
        ]),

        // Peine: cinco dientes verticales a dos celdas. La forma más densa de
        // todas, y donde un solo alcance largo paga más.
        ("peine", [
            new Cell(3, 8), new Cell(3, 3), new Cell(5, 3),
            new Cell(5, 7), new Cell(7, 7), new Cell(7, 3),
            new Cell(9, 3), new Cell(9, 7), new Cell(11, 7),
            new Cell(11, 4),
        ]),

        // Espiral hacia adentro, con la Torre casi en el centro: desde el medio se
        // tocan varias vueltas a la vez.
        ("espiral", [
            new Cell(3, 2), new Cell(13, 2), new Cell(13, 6),
            new Cell(5, 6), new Cell(5, 4), new Cell(11, 4),
        ]),

        // Uve corta: dos pasadas horizontales pegadas. La más simple de leer.
        ("uve", [
            new Cell(3, 3), new Cell(15, 3), new Cell(15, 5),
            new Cell(4, 5),
        ]),

        // Zigzag vertical apretado. Le da valor a las columnas del medio, que en
        // las rutas horizontales quedan muertas.
        ("zigzag", [
            new Cell(3, 3), new Cell(3, 8), new Cell(5, 8),
            new Cell(5, 3), new Cell(7, 3), new Cell(7, 8),
            new Cell(9, 8), new Cell(9, 5),
        ]),

        // Gancho: entra por abajo, sube por la derecha y vuelve plegado al centro.
        ("gancho", [
            new Cell(4, 8), new Cell(14, 8), new Cell(14, 3),
            new Cell(6, 3), new Cell(6, 5), new Cell(12, 5),
        ]),

        // Doble ese al revés: arranca abajo y sube plegándose.
        ("dobleEse", [
            new Cell(3, 8), new Cell(12, 8), new Cell(12, 6),
            new Cell(4, 6), new Cell(4, 4), new Cell(13, 4),
        ]),

        // Caracol: vuelta larga por afuera y cierre hacia el medio.
        ("caracol", [
            new Cell(4, 3), new Cell(14, 3), new Cell(14, 7),
            new Cell(6, 7), new Cell(6, 5), new Cell(11, 5),
        ]),

        // Escalera: baja en escalones y vuelve por abajo de un tirón. La única con
        // una recta larga al final, para que no todas se jueguen igual.
        ("escalera", [
            new Cell(2, 2), new Cell(6, 2), new Cell(6, 4),
            new Cell(10, 4), new Cell(10, 6), new Cell(14, 6),
            new Cell(14, 8), new Cell(5, 8),
        ]),
    ];

    /// <summary>
    /// Arma los layouts, <b>los valida</b> y solo escribe los que pasan.
    /// </summary>
    /// <remarks>
    /// Escribir uno que no valida sería peor que no escribirlo: entraría al pozo
    /// del que la seed elige y rompería una corrida de horas.
    /// </remarks>
    public static void Correr()
    {
        string carpeta = ProjectSettings.GlobalizePath("res://Datos/layouts");
        Directory.CreateDirectory(carpeta);

        GD.Print("── Generate layouts ────────────────────────────────");

        int escritos = 0;
        int rechazados = 0;

        foreach (var (nombre, vertices) in Recetas)
        {
            var mapa = LayoutMapa.DeVertices(vertices);

            bool largoFuera = System.Math.Abs(mapa.LargoRuta - LargoObjetivo) > Tolerancia;

            if (!mapa.Valido || largoFuera)
            {
                rechazados++;

                GD.PrintErr($"  ✗ {nombre,-12} ruta {mapa.LargoRuta,3} · " +
                            (mapa.Valido
                                ? $"largo fuera de {LargoObjetivo}±{Tolerancia}"
                                : string.Join(" · ", mapa.Problemas)));

                continue;
            }

            string ruta = Path.Combine(carpeta, $"gen-{nombre}.json");
            mapa.Store(ruta);
            escritos++;

            GD.Print($"  ✓ {nombre,-12} ruta {mapa.LargoRuta,3} celdas · " +
                     $"{mapa.ContarPegadas(),3} colocables pegadas");
        }

        GD.Print($"\n  {escritos} escritos · {rechazados} rechazados");
    }

    /// <summary>
    /// Revisa <b>todos</b> los layouts del disco, los dibujados y los generados.
    /// </summary>
    /// <remarks>
    /// Un layout se puede corregir a mano abriendo el JSON, y ahí es donde se
    /// rompe sin que nadie mire. Esto es la red.
    /// </remarks>
    /// <summary>
    /// Bloquea celdas colocables pegadas al camino hasta que ningún layout pase
    /// de <paramref name="tope"/>, y reescribe los que cambien.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es una corrección de EQUIDAD DEL RANKING, no de estética.</b> El
    /// layout lo sortea la seed del servidor, no lo elige el jugador, y
    /// <c>--probar-layouts</c> mide que el de más celdas pegadas llega doce oleadas
    /// más lejos que el de menos: gen-gancho, con 56, llega a la 74; gen-zigzag,
    /// con 36, llega a la 62. Doce oleadas que decide el sorteo antes de que nadie
    /// ponga un defensor.</para>
    ///
    /// <para>Solo se BAJA, nunca se sube, y por geometría: las celdas que tocan el
    /// camino existen porque el camino pasa por ahí, así que a un layout de ruta
    /// corta no se le pueden agregar. Bajar siempre se puede.</para>
    ///
    /// <para>Se bloquean las <b>últimas en orden de barrido</b>, que es abajo a la
    /// derecha. Es una regla arbitraria y por eso mismo sirve: es reproducible, y
    /// una elección "inteligente" de cuál sacar sería una decisión de diseño
    /// escondida en una herramienta.</para>
    ///
    /// <para>⚠ Los layouts entran al hash de <c>/run/start</c>. Correr esto es
    /// TEMPORADA NUEVA.</para>
    /// </remarks>
    public static void Emparejar(int tope)
    {
        string carpeta = ProjectSettings.GlobalizePath("res://Datos/layouts");

        GD.Print($"── Emparejar layouts a {tope} celdas pegadas ──────────");

        if (!Directory.Exists(carpeta))
        {
            GD.PrintErr("  ✗ no hay carpeta Datos/layouts");
            return;
        }

        var archivos = new List<string>(Directory.GetFiles(carpeta, "*.json"));
        archivos.Sort(System.StringComparer.Ordinal);

        int tocados = 0;

        foreach (string archivo in archivos)
        {
            string nombre = Path.GetFileNameWithoutExtension(archivo);
            var mapa = LayoutMapa.DeArchivo(archivo);

            if (mapa is null)
            {
                GD.PrintErr($"  ✗ {nombre,-18} no se pudo leer");
                continue;
            }

            int antes = mapa.ContarPegadas();

            if (antes <= tope)
            {
                GD.Print($"  · {nombre,-18} {antes,3} pegadas · queda igual");
                continue;
            }

            int sacadas = 0;

            for (int y = LayoutMapa.Height - 1; y >= 0 && mapa.ContarPegadas() > tope; y--)
            {
                for (int x = LayoutMapa.Width - 1; x >= 0 && mapa.ContarPegadas() > tope; x--)
                {
                    if (mapa[x, y] != CeldaLayout.Colocable || !Pegada(mapa, x, y))
                        continue;

                    mapa[x, y] = CeldaLayout.Bloqueado;
                    sacadas++;
                }
            }

            mapa.Validar();

            // ⚠ Bloquear una colocable no puede cortar el camino —el camino son
            // otras celdas— pero sí puede dejar el layout por debajo del mínimo de
            // pegadas o romper otra regla. Si pasa, no se escribe: mejor un layout
            // desparejo que uno inválido.
            if (!mapa.Valido)
            {
                GD.PrintErr($"  ✗ {nombre,-18} quedaría inválido: " +
                            string.Join(" · ", mapa.Problemas));
                continue;
            }

            mapa.Store(archivo);
            tocados++;

            GD.Print($"  ✓ {nombre,-18} {antes,3} → {mapa.ContarPegadas(),3} pegadas " +
                     $"· {sacadas} bloqueadas");
        }

        GD.Print($"\n  {tocados} layout(s) reescritos");

        if (tocados > 0)
            GD.Print("  ⚠ TEMPORADA NUEVA: los layouts entran al hash de /run/start");
    }

    /// <summary>¿Esta celda toca el camino, la entrada o la Torre?</summary>
    private static bool Pegada(LayoutMapa mapa, int x, int y)
    {
        foreach (var v in LayoutMapa.Neighbours(new Cell(x, y)))
        {
            if (mapa[v.X, v.Y] is CeldaLayout.Route or CeldaLayout.Entrada or CeldaLayout.Tower)
                return true;
        }

        return false;
    }

    public static void Validar()
    {
        string carpeta = ProjectSettings.GlobalizePath("res://Datos/layouts");

        GD.Print("── Layouts en disco ───────────────────────────────");

        if (!Directory.Exists(carpeta))
        {
            GD.PrintErr("  ✗ no hay carpeta Datos/layouts");
            return;
        }

        var archivos = new List<string>(Directory.GetFiles(carpeta, "*.json"));
        archivos.Sort(System.StringComparer.Ordinal);

        if (archivos.Count == 0)
        {
            GD.PrintErr("  ✗ no hay ningún layout");
            return;
        }

        int buenos = 0;
        int malos = 0;
        int minimo = int.MaxValue;
        int maximo = 0;
        int minPegadas = int.MaxValue;
        int maxPegadas = 0;

        foreach (string archivo in archivos)
        {
            string nombre = Path.GetFileNameWithoutExtension(archivo);
            var mapa = LayoutMapa.DeArchivo(archivo);

            if (mapa is null)
            {
                malos++;
                GD.PrintErr($"  ✗ {nombre,-18} no se pudo leer");
                continue;
            }

            if (mapa.Valido)
            {
                buenos++;
                minimo = System.Math.Min(minimo, mapa.LargoRuta);
                maximo = System.Math.Max(maximo, mapa.LargoRuta);

                int pegadas = mapa.ContarPegadas();
                minPegadas = System.Math.Min(minPegadas, pegadas);
                maxPegadas = System.Math.Max(maxPegadas, pegadas);

                GD.Print($"  ✓ {nombre,-18} ruta {mapa.LargoRuta,3} · " +
                         $"{pegadas,3} pegadas");
            }
            else
            {
                malos++;
                GD.PrintErr($"  ✗ {nombre,-18} {string.Join(" · ", mapa.Problemas)}");
            }
        }

        GD.Print($"\n  {buenos} válidos · {malos} con problemas");

        if (buenos > 0)
        {
            int spread = maximo == 0 ? 0 : (maximo - minimo) * 100 / maximo;
            int spreadPegadas = maxPegadas == 0 ? 0 : (maxPegadas - minPegadas) * 100 / maxPegadas;

            GD.Print($"  largo de ruta:  {minimo} a {maximo} · spread {spread}%");
            GD.Print($"  celdas pegadas: {minPegadas} a {maxPegadas} · spread {spreadPegadas}%");

            // ⚠ LAS CELDAS PEGADAS SON LA MEDIDA QUE IMPORTA, no el largo de ruta.
            //
            // Antes el aviso miraba el largo, y --probar-layouts dice que el largo
            // casi no predice nada: gen-uve tiene la segunda ruta más corta (26) y
            // llega a la oleada 68, gen-serpiente tiene la más larga (33) y llega a
            // la misma 68. Lo que sí predice es cuántas celdas colocables tocan el
            // camino, porque eso es cuántos defensores entran: gen-gancho tiene 56
            // y llega a 74; gen-zigzag tiene 36 y llega a 62.
            //
            // Y eso contamina el ranking dos veces: el layout con más lugares es
            // más fácil, y el jugador no lo elige —lo sortea la seed del servidor—.
            if (spreadPegadas > 20)
            {
                GD.PrintErr($"  ⚠ {spreadPegadas}% de diferencia en celdas pegadas: el layout con " +
                            "más lugares es más fácil, y lo sortea la seed, no el jugador");
            }

            if (spread > 20)
            {
                GD.Print("  · el largo de ruta también difiere más de 20%, pero " +
                         "--probar-layouts dice que casi no predice el resultado");
            }
        }
    }

    // -------------------------------------------------------------------------
    //  La prueba de verdad: jugarlos
    // -------------------------------------------------------------------------

    /// <summary>
    /// Corre la simulación sobre cada layout del disco y reporta hasta dónde llega.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Que un layout cumpla las reglas no dice que se pueda jugar.</b>
    /// Las reglas son geometría: un camino sin ramas, celdas al costado, la Torre
    /// lejos del borde. Que la ruta dé tiempo de matar lo que viene, que los
    /// lugares de colocación cubran los tramos que importan y que dos layouts sean
    /// igual de difíciles no se ve mirando el dibujo.</para>
    ///
    /// <para>Lo que hay que buscar en la salida no es el número absoluto —depende
    /// de la política tosca del corredor— sino la <b>diferencia entre layouts</b>.
    /// Si uno llega a la 40 y otro a la 20, la seed decide la corrida antes de que
    /// el jugador juegue, y eso rompe el ranking.</para>
    /// </remarks>
    /// <summary>
    /// Los doce layouts jugados, para la hoja <c>Layouts</c> del XLSX.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>El layout lo sortea la seed del servidor, no lo elige el
    /// jugador</b>, así que la diferencia entre el más fácil y el más difícil es
    /// ventaja repartida a dedo en un ranking que ordena por <c>ticks</c>. Esta
    /// hoja existe para que esa diferencia sea un número y no una impresión.</para>
    ///
    /// <para>La columna que importa es <c>Adjacent</c> —celdas colocables que tocan
    /// el camino—, no <c>RouteLength</c>: medido, el largo casi no predice el
    /// resultado y las pegadas sí, porque son cuántos defensores entran.</para>
    /// </remarks>
    internal static List<object[]> FilasDeLayouts(int corridas)
    {
        List<object[]> filas =
        [
            ["Layout", "RouteLength", "Adjacent", "WaveAvg", "WaveWorst", "WaveBest", "Defenders"],
        ];

        string carpeta = ProjectSettings.GlobalizePath("res://Datos/layouts");

        if (!Directory.Exists(carpeta))
            return filas;

        var archivos = new List<string>(Directory.GetFiles(carpeta, "*.json"));
        archivos.Sort(System.StringComparer.Ordinal);

        foreach (string archivo in archivos)
        {
            string nombre = Path.GetFileNameWithoutExtension(archivo);
            var layout = LayoutMapa.DeArchivo(archivo);

            if (layout is null || !layout.Valido)
                continue;

            var mapa = AMapa(layout, out _);

            if (mapa is null)
                continue;

            int suma = 0, defensores = 0, peor = int.MaxValue, mejor = 0;

            for (int i = 0; i < corridas; i++)
            {
                // Las mismas seeds fijas que --probar-layouts, para que las dos
                // salidas se puedan comparar.
                ulong seed = 1000UL + ((ulong)i * 7919UL);

                var r = CorredorHeadless.Correr(seed, new ActionLog(), mapa, null);

                suma += r.Wave;
                defensores += r.Defenders;
                peor = System.Math.Min(peor, r.Wave);
                mejor = System.Math.Max(mejor, r.Wave);
            }

            filas.Add(
            [
                nombre, layout.LargoRuta, layout.ContarPegadas(),
                corridas > 0 ? suma / corridas : 0, peor, mejor,
                corridas > 0 ? defensores / corridas : 0,
            ]);
        }

        return filas;
    }

    public static void Probar(int corridas)
    {
        string carpeta = ProjectSettings.GlobalizePath("res://Datos/layouts");

        GD.Print($"── Jugar cada layout, {corridas} corridas ─────────────");

        if (!Directory.Exists(carpeta))
        {
            GD.PrintErr("  ✗ no hay carpeta Datos/layouts");
            return;
        }

        var archivos = new List<string>(Directory.GetFiles(carpeta, "*.json"));
        archivos.Sort(System.StringComparer.Ordinal);

        int mejorGlobal = 0;
        int peorGlobal = int.MaxValue;

        foreach (string archivo in archivos)
        {
            string nombre = Path.GetFileNameWithoutExtension(archivo);
            var layout = LayoutMapa.DeArchivo(archivo);

            if (layout is null || !layout.Valido)
            {
                GD.PrintErr($"  ✗ {nombre,-18} no valida, no se prueba");
                continue;
            }

            var mapa = AMapa(layout, out var plan);

            if (mapa is null)
            {
                GD.PrintErr($"  ✗ {nombre,-18} no se pudo armar el mapa");
                continue;
            }

            int suma = 0;
            int defensores = 0;
            int peor = int.MaxValue;
            int mejor = 0;

            for (int i = 0; i < corridas; i++)
            {
                // Seeds fijas y no al azar: dos corridas de esta herramienta tienen
                // que dar lo mismo, o comparar layouts no significa nada.
                ulong seed = 1000UL + ((ulong)i * 7919UL);

                var r = CorredorHeadless.Correr(seed, new ActionLog(), mapa, null);

                suma += r.Wave;
                defensores += r.Defenders;
                peor = System.Math.Min(peor, r.Wave);
                mejor = System.Math.Max(mejor, r.Wave);
            }

            int media = suma / System.Math.Max(1, corridas);

            mejorGlobal = System.Math.Max(mejorGlobal, media);
            peorGlobal = System.Math.Min(peorGlobal, media);

            GD.Print($"  {nombre,-18} ruta {layout.LargoRuta,3} · " +
                     $"oleada media {media,3} (peor {peor,3} · mejor {mejor,3}) · " +
                     $"{defensores / System.Math.Max(1, corridas),2} defensores · " +
                     $"{plan.Count,2} lugares en el plan");
        }

        if (peorGlobal <= mejorGlobal && mejorGlobal > 0)
        {
            int spread = (mejorGlobal - peorGlobal) * 100 / mejorGlobal;

            GD.Print($"\n  media por layout: {peorGlobal} a {mejorGlobal} · spread {spread}%");

            if (spread > 25)
            {
                GD.PrintErr("  ⚠ más de 25% entre el layout más fácil y el más " +
                            "difícil: la seed decide demasiado");
            }
        }
    }

    /// <summary>
    /// Pasa un layout dibujado a un <see cref="Mapa"/> de la simulación, con un
    /// plan de colocación derivado del propio mapa.
    /// </summary>
    /// <remarks>
    /// El plan se saca recorriendo el camino y tomando una celda colocable pegada
    /// cada pocas celdas. Repartir así en vez de usar coordenadas fijas es lo que
    /// permite probar cualquier layout: unas coordenadas escritas a mano solo
    /// sirven para el mapa para el que se escribieron.
    /// </remarks>
    private static Map? AMapa(LayoutMapa layout, out List<(int Tick, Cell Cell)> plan)
    {
        plan = [];

        Cell? entrada = null;
        Cell? torre = null;
        var colocables = new List<Cell>();

        for (int y = 0; y < LayoutMapa.Height; y++)
        {
            for (int x = 0; x < LayoutMapa.Width; x++)
            {
                switch (layout[x, y])
                {
                    case CeldaLayout.Entrada: entrada = new Cell(x, y); break;
                    case CeldaLayout.Tower: torre = new Cell(x, y); break;
                    case CeldaLayout.Colocable: colocables.Add(new Cell(x, y)); break;
                }
            }
        }

        if (entrada is not { } e || torre is not { } t)
            return null;

        var camino = layout.Trace(e, t, out _);

        if (camino is null)
            return null;

        // ⚠ La Ruta se arma con los VÉRTICES, no con las celdas.
        //
        // Ruta interpola entre vértices, así que pasarle las 28 celdas del camino
        // le daría 27 tramos de una celda: el mismo recorrido, pero con veintisiete
        // veces más trabajo en cada consulta de posición.
        var vertices = new List<Cell> { camino[0] };

        for (int i = 1; i < camino.Count - 1; i++)
        {
            int dxA = camino[i].X - camino[i - 1].X;
            int dyA = camino[i].Y - camino[i - 1].Y;
            int dxB = camino[i + 1].X - camino[i].X;
            int dyB = camino[i + 1].Y - camino[i].Y;

            if (dxA != dxB || dyA != dyB)
                vertices.Add(camino[i]);
        }

        vertices.Add(camino[^1]);

        var mapa = new Map(
            LayoutMapa.Width, LayoutMapa.Height, new Route(vertices), colocables);

        // ⚠ El plan queda para compatibilidad, pero PROBAR YA NO LO USA: pasa
        // null y deja que el corredor arme el suyo con la política normal, que
        // llena el tablero. Con este plan ralo cada layout se jugaba con siete
        // defensores y lo que se comparaba era el plan, no el layout.
        //
        // Un lugar cada tres celdas de camino, siempre pegado: cubre el recorrido
        // parejo en vez de amontonar todo en el arranque.
        var usadas = new HashSet<Cell>();
        int tick = 60;

        for (int i = 0; i < camino.Count; i += 3)
        {
            foreach (var v in LayoutMapa.Neighbours(camino[i]))
            {
                if (layout[v.X, v.Y] != CeldaLayout.Colocable || !usadas.Add(v))
                    continue;

                plan.Add((tick, v));
                tick += 240;
                break;
            }
        }

        return mapa;
    }
}
