using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Herramientas;

/// <summary>Lo que puede ser una celda de un layout.</summary>
public enum CeldaLayout
{
    Bloqueado = 0,
    Colocable = 1,
    Route = 2,
    Entrada = 3,
    Tower = 4,
}

/// <summary>
/// Un layout y sus reglas, en un solo lugar.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Las reglas viven acá y nadie las copia.</b> Las usan el editor a
/// mano, el generador y el chequeo headless. Tres copias de "qué es un layout
/// válido" se separan a la primera corrección y después hay mapas que el editor
/// da por buenos y el juego rechaza.</para>
///
/// <para>El campo es <b>20×11</b>, la vista de Argentum con celdas cuadradas.</para>
/// </remarks>
public sealed class LayoutMapa
{
    public const int Width = 20;

    public const int Height = 11;

    private readonly CeldaLayout[,] _celdas = new CeldaLayout[Width, Height];

    /// <summary>Largo del camino en celdas, contando entrada y Torre. 0 si no traza.</summary>
    public int LargoRuta { get; private set; }

    public IReadOnlyList<string> Problemas { get; private set; } = [];

    public bool Valido => Problemas.Count == 0;

    public CeldaLayout this[int x, int y]
    {
        get => _celdas[x, y];
        set => _celdas[x, y] = value;
    }

    // -------------------------------------------------------------------------
    //  Construcción
    // -------------------------------------------------------------------------

    /// <summary>
    /// Traza un camino por vértices y le abre el borde colocable.
    /// </summary>
    /// <remarks>
    /// <para>Los vértices tienen que ser <b>consecutivamente alineados</b>: cada
    /// par comparte fila o columna. Un tramo en diagonal no se puede trazar sin
    /// inventar por dónde dobla, y esa invención es justo el tipo de decisión que
    /// después nadie puede leer del dato.</para>
    ///
    /// <para>El primero es la entrada y el último es la Torre.</para>
    /// </remarks>
    public static LayoutMapa DeVertices(IReadOnlyList<Cell> vertices)
    {
        var mapa = new LayoutMapa();

        if (vertices.Count < 2)
            return mapa;

        var camino = new List<Cell>();

        for (int i = 0; i < vertices.Count - 1; i++)
        {
            var a = vertices[i];
            var b = vertices[i + 1];

            int pasoX = System.Math.Sign(b.X - a.X);
            int pasoY = System.Math.Sign(b.Y - a.Y);

            // Si el tramo no está alineado se corta ahí: mejor un layout que no
            // valida que uno que dobla donde no dice el dato.
            if (pasoX != 0 && pasoY != 0)
                break;

            var actual = a;

            while (actual != b)
            {
                camino.Add(actual);
                actual = new Cell(actual.X + pasoX, actual.Y + pasoY);
            }
        }

        camino.Add(vertices[^1]);

        foreach (var c in camino)
        {
            if (Adentro(c))
                mapa._celdas[c.X, c.Y] = CeldaLayout.Route;
        }

        var entrada = camino[0];
        var torre = camino[^1];

        if (Adentro(entrada)) mapa._celdas[entrada.X, entrada.Y] = CeldaLayout.Entrada;
        if (Adentro(torre)) mapa._celdas[torre.X, torre.Y] = CeldaLayout.Tower;

        foreach (var c in camino)
            mapa.AbrirCostados(c);

        mapa.TaparEsquinas();
        mapa.Validar();
        return mapa;
    }

    /// <summary>
    /// Pone en colocable lo que rodea a una celda de ruta, si estaba bloqueado.
    /// </summary>
    /// <remarks>
    /// Los ocho vecinos y no los cuatro ortogonales: en una curva la diagonal es
    /// costado del camino igual, y dejarla bloqueada muerde las esquinas. Solo
    /// pisa lo bloqueado, así que dos tramos paralelos no se comen entre sí.
    /// </remarks>
    public void AbrirCostados(Cell c)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = c.X + dx;
                int y = c.Y + dy;

                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    continue;

                if (_celdas[x, y] == CeldaLayout.Bloqueado)
                    _celdas[x, y] = CeldaLayout.Colocable;
            }
        }
    }

    /// <summary>
    /// Vuelve a bloquear las esquinas que el borde automático haya abierto.
    /// </summary>
    /// <remarks>
    /// <para>Una ruta que pasa cerca de un borde abre colocables en las esquinas,
    /// y ahí flota el HUD: sería un lugar de colocación abajo de un panel, o sea
    /// un click que el jugador no puede hacer.</para>
    ///
    /// <para>Se tapa en vez de rechazar el layout porque <b>no es un error de
    /// diseño</b>: la ruta está bien, lo que sobra es el borde. Rechazarlo
    /// obligaría a mantener las rutas lejos de los bordes, que desperdicia media
    /// pantalla.</para>
    ///
    /// <para>⚠ Se tapa solo lo <b>colocable</b>. Si el camino mismo pasa por una
    /// esquina, se deja: taparlo cortaría la ruta.</para>
    /// </remarks>
    public void TaparEsquinas()
    {
        foreach (var c in Esquinas())
        {
            if (_celdas[c.X, c.Y] == CeldaLayout.Colocable)
                _celdas[c.X, c.Y] = CeldaLayout.Bloqueado;
        }
    }

    private static bool Adentro(Cell c)
        => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    // -------------------------------------------------------------------------
    //  Validación
    // -------------------------------------------------------------------------

    /// <summary>
    /// Las reglas duras de un layout, todas descubiertas midiendo.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><b>Un solo camino, sin ramas.</b> El enemigo avanza por una distancia
    /// escalar; una bifurcación no tiene cómo representarse.</item>
    /// <item><b>Colocables pegadas a la ruta</b>, o el cuerpo a cuerpo se pone,
    /// cuesta oro y no pega nunca.</item>
    /// <item><b>La Torre a 2+ celdas del borde.</b> Contra una esquina su anillo
    /// queda cortado y un enemigo que llegue se vuelve imposible de alcanzar.</item>
    /// <item><b>Esquinas de pantalla libres</b>, que es donde flota el HUD: una
    /// celda de colocación abajo de un panel es un click que no se puede hacer.</item>
    /// <item><b>Sin celdas de ruta sueltas</b> fuera del camino trazado.</item>
    /// </list>
    /// </remarks>
    public void Validar()
    {
        var problemas = new List<string>();

        Cell? entrada = null;
        Cell? torre = null;
        int celdasRuta = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                switch (_celdas[x, y])
                {
                    case CeldaLayout.Entrada: entrada = new Cell(x, y); celdasRuta++; break;
                    case CeldaLayout.Tower: torre = new Cell(x, y); celdasRuta++; break;
                    case CeldaLayout.Route: celdasRuta++; break;
                }
            }
        }

        LargoRuta = 0;

        if (entrada is null)
            problemas.Add("falta la entrada");

        if (torre is null)
            problemas.Add("falta la Torre");

        if (entrada is { } e && torre is { } t)
        {
            var camino = Trace(e, t, out string error);

            if (camino is null)
            {
                problemas.Add(error);
            }
            else
            {
                LargoRuta = camino.Count;

                if (camino.Count != celdasRuta)
                {
                    problemas.Add($"{celdasRuta - camino.Count} celda(s) de ruta " +
                                  "sueltas, fuera del camino");
                }
            }

            if (t.X < 2 || t.Y < 2 || t.X >= Width - 2 || t.Y >= Height - 2)
                problemas.Add($"la Torre ({t.X},{t.Y}) está a menos de 2 celdas del borde");
        }

        int pegadas = ContarPegadas();

        if (pegadas < 8)
            problemas.Add($"solo {pegadas} colocables pegadas a la ruta");

        foreach (var esquina in Esquinas())
        {
            if (_celdas[esquina.X, esquina.Y] == CeldaLayout.Colocable)
            {
                problemas.Add($"colocable en la esquina ({esquina.X},{esquina.Y}), " +
                              "donde flota el HUD");
                break;
            }
        }

        Problemas = problemas;
    }

    /// <summary>
    /// Sigue el camino de la entrada a la Torre, y se queja si se bifurca.
    /// </summary>
    /// <remarks>
    /// Celda por celda, mirando los vecinos de ruta sin visitar. <b>Dos</b> es una
    /// bifurcación y no hay forma de decir cuánto avanzó un enemigo; <b>cero</b>
    /// antes de la Torre es un camino cortado.
    /// </remarks>
    public List<Cell>? Trace(Cell entrada, Cell torre, out string error)
    {
        error = "";

        var camino = new List<Cell> { entrada };
        var visitadas = new HashSet<Cell> { entrada };
        var actual = entrada;

        while (actual != torre)
        {
            var siguientes = new List<Cell>();

            foreach (var v in Neighbours(actual))
            {
                if (visitadas.Contains(v))
                    continue;

                if (_celdas[v.X, v.Y] is CeldaLayout.Route or CeldaLayout.Tower)
                    siguientes.Add(v);
            }

            if (siguientes.Count == 0)
            {
                error = $"el camino se corta en ({actual.X},{actual.Y}) sin llegar a la Torre";
                return null;
            }

            if (siguientes.Count > 1)
            {
                error = $"el camino se bifurca en ({actual.X},{actual.Y})";
                return null;
            }

            actual = siguientes[0];
            visitadas.Add(actual);
            camino.Add(actual);
        }

        return camino;
    }

    /// <summary>Los cuatro vecinos ortogonales. No se camina en diagonal.</summary>
    public static IEnumerable<Cell> Neighbours(Cell c)
    {
        if (c.X > 0) yield return new Cell(c.X - 1, c.Y);
        if (c.Y > 0) yield return new Cell(c.X, c.Y - 1);
        if (c.X < Width - 1) yield return new Cell(c.X + 1, c.Y);
        if (c.Y < Height - 1) yield return new Cell(c.X, c.Y + 1);
    }

    public int ContarPegadas()
    {
        int cuantas = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_celdas[x, y] != CeldaLayout.Colocable)
                    continue;

                foreach (var v in Neighbours(new Cell(x, y)))
                {
                    if (_celdas[v.X, v.Y] is CeldaLayout.Route
                        or CeldaLayout.Entrada or CeldaLayout.Tower)
                    {
                        cuantas++;
                        break;
                    }
                }
            }
        }

        return cuantas;
    }

    /// <summary>Las cuatro esquinas de 3×2 celdas donde flota el HUD.</summary>
    public static IEnumerable<Cell> Esquinas()
    {
        for (int dy = 0; dy < 2; dy++)
        {
            for (int dx = 0; dx < 3; dx++)
            {
                yield return new Cell(dx, dy);
                yield return new Cell(Width - 1 - dx, dy);
                yield return new Cell(dx, Height - 1 - dy);
                yield return new Cell(Width - 1 - dx, Height - 1 - dy);
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Texto y disco
    // -------------------------------------------------------------------------

    public string[] AAscii()
    {
        var filas = new string[Height];

        for (int y = 0; y < Height; y++)
        {
            var fila = new StringBuilder(Width);

            for (int x = 0; x < Width; x++)
            {
                fila.Append(_celdas[x, y] switch
                {
                    CeldaLayout.Bloqueado => 'X',
                    CeldaLayout.Colocable => '.',
                    CeldaLayout.Route => '#',
                    CeldaLayout.Entrada => 'E',
                    _ => 'T',
                });
            }

            filas[y] = fila.ToString();
        }

        return filas;
    }

    public static LayoutMapa DeAscii(IReadOnlyList<string> filas)
    {
        var mapa = new LayoutMapa();

        for (int y = 0; y < Height && y < filas.Count; y++)
        {
            for (int x = 0; x < Width && x < filas[y].Length; x++)
            {
                mapa._celdas[x, y] = filas[y][x] switch
                {
                    '.' => CeldaLayout.Colocable,
                    '#' => CeldaLayout.Route,
                    'E' => CeldaLayout.Entrada,
                    'T' => CeldaLayout.Tower,
                    _ => CeldaLayout.Bloqueado,
                };
            }
        }

        mapa.Validar();
        return mapa;
    }

    public static LayoutMapa? DeArchivo(string ruta)
    {
        if (!System.IO.File.Exists(ruta))
            return null;

        var json = JsonNode.Parse(System.IO.File.ReadAllText(ruta, Encoding.UTF8));
        var filas = json?["filas"]?.AsArray();

        if (filas is null)
            return null;

        var lista = new List<string>(filas.Count);

        foreach (var f in filas)
            lista.Add(f?.GetValue<string>() ?? "");

        return DeAscii(lista);
    }

    /// <summary>
    /// Escribe el layout como ASCII adentro de un JSON.
    /// </summary>
    /// <remarks>
    /// ASCII y no una lista de coordenadas: el mapa <b>se ve</b> abriendo el
    /// archivo y se lee en un diff. Una lista de pares no la entiende nadie a los
    /// seis meses.
    /// </remarks>
    public void Store(string ruta)
    {
        var filas = new JsonArray();

        foreach (string fila in AAscii())
            filas.Add(fila);

        var salida = new JsonObject
        {
            ["version"] = 1,
            ["leyenda"] = "E entrada · # ruta · . colocable · X bloqueado · T la Torre",

            ["aviso"] = "Se dibuja con Escenas/EditorLayouts.tscn o lo genera " +
                        "--generar-layouts. Se puede corregir a mano, pero después hay " +
                        "que pasarlo por --validar-layouts.",

            ["ancho"] = Width,
            ["alto"] = Height,
            ["largoRuta"] = LargoRuta,
            ["valido"] = Valido,
            ["filas"] = filas,
        };

        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ruta)!);

        System.IO.File.WriteAllText(
            ruta,
            salida.ToJsonString(opciones),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
