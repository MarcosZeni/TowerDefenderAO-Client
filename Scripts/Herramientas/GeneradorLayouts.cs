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
    private static readonly (string Nombre, Celda[] Vertices)[] Recetas =
    [
        // Serpiente plegada: tres tramos horizontales a dos celdas. Las franjas de
        // en medio cubren dos pasadas cada una.
        ("serpiente", [
            new Celda(3, 2), new Celda(14, 2), new Celda(14, 4),
            new Celda(5, 4), new Celda(5, 6), new Celda(13, 6),
        ]),

        // Peine: cinco dientes verticales a dos celdas. La forma más densa de
        // todas, y donde un solo alcance largo paga más.
        ("peine", [
            new Celda(3, 8), new Celda(3, 3), new Celda(5, 3),
            new Celda(5, 7), new Celda(7, 7), new Celda(7, 3),
            new Celda(9, 3), new Celda(9, 7), new Celda(11, 7),
            new Celda(11, 4),
        ]),

        // Espiral hacia adentro, con la Torre casi en el centro: desde el medio se
        // tocan varias vueltas a la vez.
        ("espiral", [
            new Celda(3, 2), new Celda(13, 2), new Celda(13, 6),
            new Celda(5, 6), new Celda(5, 4), new Celda(11, 4),
        ]),

        // Uve corta: dos pasadas horizontales pegadas. La más simple de leer.
        ("uve", [
            new Celda(3, 3), new Celda(15, 3), new Celda(15, 5),
            new Celda(4, 5),
        ]),

        // Zigzag vertical apretado. Le da valor a las columnas del medio, que en
        // las rutas horizontales quedan muertas.
        ("zigzag", [
            new Celda(3, 3), new Celda(3, 8), new Celda(5, 8),
            new Celda(5, 3), new Celda(7, 3), new Celda(7, 8),
            new Celda(9, 8), new Celda(9, 5),
        ]),

        // Gancho: entra por abajo, sube por la derecha y vuelve plegado al centro.
        ("gancho", [
            new Celda(4, 8), new Celda(14, 8), new Celda(14, 3),
            new Celda(6, 3), new Celda(6, 5), new Celda(12, 5),
        ]),

        // Doble ese al revés: arranca abajo y sube plegándose.
        ("dobleEse", [
            new Celda(3, 8), new Celda(12, 8), new Celda(12, 6),
            new Celda(4, 6), new Celda(4, 4), new Celda(13, 4),
        ]),

        // Caracol: vuelta larga por afuera y cierre hacia el medio.
        ("caracol", [
            new Celda(4, 3), new Celda(14, 3), new Celda(14, 7),
            new Celda(6, 7), new Celda(6, 5), new Celda(11, 5),
        ]),

        // Escalera: baja en escalones y vuelve por abajo de un tirón. La única con
        // una recta larga al final, para que no todas se jueguen igual.
        ("escalera", [
            new Celda(2, 2), new Celda(6, 2), new Celda(6, 4),
            new Celda(10, 4), new Celda(10, 6), new Celda(14, 6),
            new Celda(14, 8), new Celda(5, 8),
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

        GD.Print("── Generar layouts ────────────────────────────────");

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
            mapa.Guardar(ruta);
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

                GD.Print($"  ✓ {nombre,-18} ruta {mapa.LargoRuta,3} · " +
                         $"{mapa.ContarPegadas(),3} pegadas");
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
            // ⚠ El spread es lo que decide si el ranking mide al jugador.
            //
            // Ruta más larga = más tiempo de tiro = más fácil, y más ticks = peor
            // puesto. Si un layout mide 28 y otro 50, la seed decide el resultado
            // antes de que el jugador ponga el primer defensor.
            int spread = maximo == 0 ? 0 : (maximo - minimo) * 100 / maximo;

            GD.Print($"  largo de ruta: {minimo} a {maximo} · spread {spread}%");

            if (spread > 20)
            {
                GD.PrintErr("  ⚠ más de 20% de diferencia: el layout más largo es " +
                            "más fácil Y da peor puesto en el ranking");
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

                var r = CorredorHeadless.Correr(seed, new LogAcciones(), mapa, plan);

                suma += r.Oleada;
                defensores += r.Defensores;
                peor = System.Math.Min(peor, r.Oleada);
                mejor = System.Math.Max(mejor, r.Oleada);
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
    private static Mapa? AMapa(LayoutMapa layout, out List<(int Tick, Celda Celda)> plan)
    {
        plan = [];

        Celda? entrada = null;
        Celda? torre = null;
        var colocables = new List<Celda>();

        for (int y = 0; y < LayoutMapa.Alto; y++)
        {
            for (int x = 0; x < LayoutMapa.Ancho; x++)
            {
                switch (layout[x, y])
                {
                    case CeldaLayout.Entrada: entrada = new Celda(x, y); break;
                    case CeldaLayout.Torre: torre = new Celda(x, y); break;
                    case CeldaLayout.Colocable: colocables.Add(new Celda(x, y)); break;
                }
            }
        }

        if (entrada is not { } e || torre is not { } t)
            return null;

        var camino = layout.Trazar(e, t, out _);

        if (camino is null)
            return null;

        // ⚠ La Ruta se arma con los VÉRTICES, no con las celdas.
        //
        // Ruta interpola entre vértices, así que pasarle las 28 celdas del camino
        // le daría 27 tramos de una celda: el mismo recorrido, pero con veintisiete
        // veces más trabajo en cada consulta de posición.
        var vertices = new List<Celda> { camino[0] };

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

        var mapa = new Mapa(
            LayoutMapa.Ancho, LayoutMapa.Alto, new Ruta(vertices), colocables);

        // Un lugar cada tres celdas de camino, siempre pegado: cubre el recorrido
        // parejo en vez de amontonar todo en el arranque.
        var usadas = new HashSet<Celda>();
        int tick = 60;

        for (int i = 0; i < camino.Count; i += 3)
        {
            foreach (var v in LayoutMapa.Vecinos(camino[i]))
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
