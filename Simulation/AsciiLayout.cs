using System.Collections.Generic;

namespace TowerOfArgentum.Simulation;

/// <summary>
/// Convierte un layout escrito en ASCII a un <see cref="Mapa"/> jugable.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Vive en la simulación y NO lee archivos.</b> Recibe las filas ya
/// leídas. El layout es <b>entrada de la simulación</b> igual que la seed y el
/// bestiario —cambiarlo cambia la corrida—, así que el servidor tiene que armar
/// exactamente el mismo <c>Mapa</c> al revalidar. Si el parser estuviera del lado
/// del cliente, el servidor tendría el suyo y los dos se separarían.</para>
///
/// <para>Quién lee el disco es problema de cada mitad: el cliente desde
/// <c>res://Data/layouts/</c>, el servidor de donde le quede cómodo.</para>
///
/// <code>
/// E entrada · # ruta · . colocable · X bloqueado · T la Torre
/// </code>
/// </remarks>
public static class AsciiLayout
{
    public const int Width = 20;

    public const int Height = 11;

    /// <summary>
    /// Arma el <see cref="Mapa"/>, o devuelve <c>null</c> si el layout no cierra.
    /// </summary>
    /// <remarks>
    /// Devolver <c>null</c> y no lanzar: un layout roto es un problema de datos, y
    /// quien carga sabe mejor qué hacer —saltearlo y seguir con los demás— que
    /// esta función.
    /// </remarks>
    public static Map? Compose(IReadOnlyList<string> filas)
    {
        if (filas.Count < Height)
            return null;

        Cell? entrada = null;
        Cell? torre = null;
        var ruta = new HashSet<Cell>();
        var colocables = new List<Cell>();

        for (int y = 0; y < Height; y++)
        {
            if (filas[y].Length < Width)
                return null;

            for (int x = 0; x < Width; x++)
            {
                var c = new Cell(x, y);

                switch (filas[y][x])
                {
                    case 'E': entrada = c; ruta.Add(c); break;
                    case 'T': torre = c; ruta.Add(c); break;
                    case '#': ruta.Add(c); break;
                    case '.': colocables.Add(c); break;
                }
            }
        }

        if (entrada is not { } e || torre is not { } t)
            return null;

        var camino = Trace(ruta, e, t);

        if (camino is null)
            return null;

        // ⚠ Las ocho celdas alrededor de la Torre se abren SIEMPRE, aunque el
        // layout las haya pintado como bloqueadas.
        //
        // Un enemigo que llega a la Torre se queda ahí mordiéndola. Si ninguna
        // celda vecina es colocable, el jugador no tiene dónde poner a alguien
        // que lo saque, y la oleada no termina nunca: la corrida se convierte en
        // ver caer 1.000 HP de a 5, sin nada que hacer. Abrirlas acá —en el
        // parser, que corre igual en el cliente y en el servidor— garantiza que
        // ningún layout, ni el de hoy ni el que se dibuje mañana, pueda crear esa
        // situación.
        foreach (var v in Neighbours8(t))
        {
            if (v.X < 0 || v.Y < 0 || v.X >= Width || v.Y >= Height)
                continue;

            if (!ruta.Contains(v) && !colocables.Contains(v))
                colocables.Add(v);
        }

        return new Map(Width, Height, new Route(Vertices(camino)), colocables);
    }

    /// <summary>Las ocho vecinas de una celda, en orden fijo.</summary>
    private static IEnumerable<Cell> Neighbours8(Cell c)
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
    /// Sigue el camino de la entrada a la Torre.
    /// </summary>
    /// <remarks>
    /// Celda por celda, mirando los vecinos de ruta sin visitar. <b>Dos</b> es una
    /// bifurcación —y un enemigo que avanza por una distancia escalar no tiene
    /// cómo estar en dos ramas—; <b>cero</b> antes de la Torre es un corte.
    /// </remarks>
    private static List<Cell>? Trace(HashSet<Cell> ruta, Cell entrada, Cell torre)
    {
        var camino = new List<Cell> { entrada };
        var visitadas = new HashSet<Cell> { entrada };
        var actual = entrada;

        // Techo por las dudas: un camino no puede tener más celdas que el campo, y
        // sin el tope un dato raro colgaría la carga en vez de fallar.
        int tope = Width * Height;

        while (actual != torre && camino.Count <= tope)
        {
            Cell? siguiente = null;
            int cuantos = 0;

            foreach (var v in Neighbours4(actual))
            {
                if (visitadas.Contains(v) || !ruta.Contains(v))
                    continue;

                siguiente = v;
                cuantos++;
            }

            if (cuantos != 1 || siguiente is not { } s)
                return null;

            actual = s;
            visitadas.Add(actual);
            camino.Add(actual);
        }

        return actual == torre ? camino : null;
    }

    /// <summary>
    /// Los codos del camino, que es lo que <see cref="Ruta"/> necesita.
    /// </summary>
    /// <remarks>
    /// ⚠ Pasarle las 31 celdas en vez de los 7 codos daría el mismo recorrido con
    /// treinta tramos de una celda: <see cref="Ruta"/> interpola entre vértices, y
    /// eso es trabajo de más en cada consulta de posición, que ocurre una vez por
    /// enemigo por tick.
    /// </remarks>
    private static List<Cell> Vertices(List<Cell> camino)
    {
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
        return vertices;
    }

    private static IEnumerable<Cell> Neighbours4(Cell c)
    {
        if (c.X > 0) yield return new Cell(c.X - 1, c.Y);
        if (c.Y > 0) yield return new Cell(c.X, c.Y - 1);
        if (c.X < Width - 1) yield return new Cell(c.X + 1, c.Y);
        if (c.Y < Height - 1) yield return new Cell(c.X, c.Y + 1);
    }
}
