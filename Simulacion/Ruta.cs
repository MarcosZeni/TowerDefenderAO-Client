namespace TowerOfArgentum.Simulacion;

/// <summary>Una celda de la grilla. Enteros, como todo acá.</summary>
public readonly record struct Celda(int X, int Y);

/// <summary>
/// El camino que recorren los enemigos, de la entrada del mapa a la Torre.
///
/// <para><b>Esta clase es la razón por la que la simulación no necesita
/// trigonometría.</b> Como la ruta es fija y conocida, un enemigo no guarda
/// <c>(x, y)</c>: guarda <b>cuánto avanzó</b>, un solo <c>long</c> en milésimas
/// de celda. Mover es una suma. La posición en pantalla se saca de acá al
/// dibujar, y <b>nunca vuelve a entrar a la simulación</b> — si volviera,
/// entraría redondeo del render en el estado del juego y se acabó el
/// determinismo.</para>
/// </summary>
public sealed class Ruta
{
    private readonly Celda[] _vertices;

    /// <summary>
    /// Distancia acumulada, en milésimas de celda, desde la entrada hasta cada
    /// vértice. Se calcula una vez en el constructor: ubicar un enemigo es una
    /// búsqueda binaria acá adentro, no un recorrido del camino entero.
    /// </summary>
    private readonly long[] _acumulado;

    public Ruta(IReadOnlyList<Celda> vertices)
    {
        if (vertices.Count < 2)
            throw new ArgumentException("Una ruta necesita al menos dos vértices.", nameof(vertices));

        _vertices = [.. vertices];
        _acumulado = new long[_vertices.Length];

        for (int i = 1; i < _vertices.Length; i++)
        {
            // ⚠ Los tramos son rectos y alineados a los ejes, así que el largo
            // es |dx| + |dy| y una de las dos siempre vale cero. Nada de
            // hipotenusas: un tramo en diagonal necesitaría una raíz, y en la
            // simulación no hay raíces (JUGABILIDAD.md §4.2).
            //
            // Si algún día un layout pide diagonales, la salida NO es agregar
            // Math.Sqrt acá: es escribir el largo del tramo en los datos del
            // mapa, ya calculado, y leerlo.
            int dx = System.Math.Abs(_vertices[i].X - _vertices[i - 1].X);
            int dy = System.Math.Abs(_vertices[i].Y - _vertices[i - 1].Y);

            if (dx != 0 && dy != 0)
            {
                throw new ArgumentException(
                    $"El tramo {i - 1}->{i} va en diagonal ({dx},{dy}). Los tramos tienen que " +
                    "ser rectos: la simulación mide el largo como |dx|+|dy| y no calcula raíces.",
                    nameof(vertices));
            }

            _acumulado[i] = _acumulado[i - 1] + ((long)(dx + dy) * Enteros.MilesimasPorCelda);
        }
    }

    /// <summary>Largo total, en milésimas de celda.</summary>
    public long Largo => _acumulado[^1];

    /// <summary>Dónde entran los enemigos.</summary>
    public Celda Entrada => _vertices[0];

    /// <summary>Dónde está la Torre: el final del camino.</summary>
    public Celda Final => _vertices[^1];

    public int CantidadVertices => _vertices.Length;

    public Celda Vertice(int i) => _vertices[i];

    /// <summary>
    /// La celda donde cae un enemigo que recorrió <paramref name="distancia"/>.
    ///
    /// <para>Redondea hacia la celda más cercana, que es lo correcto para
    /// decidir alcances. <b>Lo usa la simulación</b> —para saber qué defensor
    /// llega a qué enemigo— y también el render, pero el render pide además la
    /// fracción, con <see cref="PosicionFina"/>.</para>
    /// </summary>
    public Celda CeldaEn(long distancia)
    {
        var (a, b, avance, largoTramo) = UbicarTramo(distancia);

        if (largoTramo == 0)
            return a;

        // Se interpola en milésimas y se redondea al final, en una sola
        // división. Redondear antes daría escalones distintos según el tramo.
        long x = a.X + ((long)(b.X - a.X) * avance + (largoTramo / 2)) / largoTramo;
        long y = a.Y + ((long)(b.Y - a.Y) * avance + (largoTramo / 2)) / largoTramo;

        return new Celda((int)x, (int)y);
    }

    /// <summary>
    /// La posición en milésimas de celda, sin redondear.
    ///
    /// <para><b>Solo para el render.</b> Con esto el sprite se mueve suave entre
    /// celdas en vez de saltar de una a otra. El resultado no vuelve nunca a la
    /// simulación.</para>
    /// </summary>
    public (long X, long Y) PosicionFina(long distancia)
    {
        var (a, b, avance, largoTramo) = UbicarTramo(distancia);

        long ax = (long)a.X * Enteros.MilesimasPorCelda;
        long ay = (long)a.Y * Enteros.MilesimasPorCelda;

        if (largoTramo == 0)
            return (ax, ay);

        long bx = (long)b.X * Enteros.MilesimasPorCelda;
        long by = (long)b.Y * Enteros.MilesimasPorCelda;

        return (
            ax + ((bx - ax) * avance / largoTramo),
            ay + ((by - ay) * avance / largoTramo));
    }

    /// <summary>
    /// Hacia dónde mira quien va por esa distancia: 0 norte, 1 este, 2 oeste,
    /// 3 sur.
    /// </summary>
    /// <remarks>
    /// <para><b>Solo para el render</b>, igual que <see cref="PosicionFina"/>. Los
    /// cuatro números son el orden en que el índice de gráficos guarda las
    /// direcciones, que a su vez es el de <c>Personajes.ind</c> de Austral.</para>
    ///
    /// <para>Se compara el tramo entero y no dos posiciones consecutivas: dos
    /// posiciones seguidas de un bicho lento pueden dar la misma milésima, y ahí
    /// la resta da cero y el sprite se daría vuelta solo.</para>
    ///
    /// <para>El diagonal no existe en este arte —AO tiene cuatro direcciones—, así
    /// que gana el eje que más se recorre. Empatados, gana el vertical: en un
    /// top-down el sprite de frente y de espaldas es el que más se distingue.</para>
    /// </remarks>
    public int Direccion(long distancia)
    {
        var (a, b, _, largoTramo) = UbicarTramo(distancia);

        if (largoTramo == 0)
            return 3;

        int dx = b.X - a.X;
        int dy = b.Y - a.Y;

        if (System.Math.Abs(dx) > System.Math.Abs(dy))
            return dx > 0 ? 1 : 2;

        return dy > 0 ? 3 : 0;
    }

    /// <summary>
    /// Qué tramo contiene esa distancia, y cuánto se avanzó adentro de él.
    /// </summary>
    private (Celda A, Celda B, long Avance, long LargoTramo) UbicarTramo(long distancia)
    {
        if (distancia <= 0)
            return (_vertices[0], _vertices[0], 0, 0);

        if (distancia >= Largo)
            return (_vertices[^1], _vertices[^1], 0, 0);

        // Búsqueda binaria: el primer vértice cuyo acumulado pasa la distancia.
        // Con rutas de 30 vértices un recorrido lineal daría igual, pero esto se
        // llama una vez por enemigo por tick, y en la oleada 100 hay muchos.
        int lo = 1;
        int hi = _acumulado.Length - 1;

        while (lo < hi)
        {
            int medio = lo + ((hi - lo) / 2);
            if (_acumulado[medio] <= distancia)
                lo = medio + 1;
            else
                hi = medio;
        }

        return (
            _vertices[lo - 1],
            _vertices[lo],
            distancia - _acumulado[lo - 1],
            _acumulado[lo] - _acumulado[lo - 1]);
    }
}
