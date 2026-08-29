namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Un layout jugable: la ruta más las celdas donde se puede poner un defensor.
/// </summary>
/// <remarks>
/// <para>Las zonas de colocación son <b>dato del mapa</b>, no una regla
/// calculada como "todo lo que esté a menos de N celdas de la ruta". Que las
/// pinte el diseñador es lo que permite que un layout tenga carácter: un cuello
/// de botella con pocos lugares buenos, un tramo largo sin dónde poner nada.
/// Calcularlas haría todos los mapas iguales.</para>
///
/// <para>Cada área tiene <b>3 layouts</b> y la seed elige cuál toca, así que dos
/// corridas no recorren lo mismo (OLEADAS.md).</para>
/// </remarks>
public sealed class Map
{
    /// <summary>
    /// Cuántas celdas alrededor de la Torre quedan siempre habilitadas.
    /// </summary>
    /// <remarks>
    /// Dos, para que llegue hasta el cuerpo a cuerpo: un guerrero puesto en el
    /// anillo exterior tiene alcance 1 y necesita estar pegado al enemigo, que
    /// está sobre la Torre.
    /// </remarks>
    private const int RadioDefensaTorre = 2;

    private readonly bool[] _colocable;

    /// <summary>
    /// Qué celdas pisa la ruta.
    /// </summary>
    /// <remarks>
    /// <para>Se llena en el mismo recorrido que apaga las colocables, porque es
    /// exactamente la misma pregunta contestada al revés. Recorrer la ruta de
    /// nuevo para armarla sería duplicar el paso más delicado —el que define qué
    /// es camino— y las dos copias podrían discrepar.</para>
    ///
    /// <para>Es una <b>consulta</b>: no cambia nada de la simulación. La usa el
    /// render para orientar a los defensores hacia el camino.</para>
    /// </remarks>
    private readonly bool[] _esRuta;

    public Map(int ancho, int alto, Route ruta, IEnumerable<Cell> zonasColocacion)
    {
        Width = ancho;
        Height = alto;
        Route = ruta;

        _colocable = new bool[ancho * alto];
        _esRuta = new bool[ancho * alto];

        // La Torre pegada a un borde deja el anillo de defensa cortado, y un
        // enemigo que llegue ahí puede volverse imposible de alcanzar. Se avisa en
        // vez de fallar: es un problema de diseño del layout, y el layout puede
        // estar a medio hacer.
        var fin = ruta.End;

        if (fin.X < RadioDefensaTorre || fin.X >= ancho - RadioDefensaTorre ||
            fin.Y < RadioDefensaTorre || fin.Y >= alto - RadioDefensaTorre)
        {
            CeldasDeDefensaCortadas = true;
        }

        foreach (var c in zonasColocacion)
        {
            if (InGrid(c))
                _colocable[Index(c)] = true;
        }

        // ⚠ El anillo alrededor de la Torre SIEMPRE es colocable, lo liste el
        //   layout o no.
        //
        // Sin esto, un enemigo que llega al final de la ruta es INMORTAL: se
        // planta a pegarle a la Torre y ningún defensor lo alcanza, porque las
        // zonas pintadas a mano no llegan hasta ahí. Una sola filtración deja la
        // corrida perdida aunque el jugador tenga la defensa entera en pie, y no
        // hay nada que pueda hacer al respecto.
        //
        // Pasó de verdad: el corredor headless mostró corridas muriendo en la
        // oleada 1 con dos enemigos de 16 HP pegándole a la Torre durante 15.000
        // ticks y cinco defensores sin poder tocarlos.
        //
        // Es una regla estructural y va acá, no en cada layout: un layout futuro
        // que se olvide de pintar esas celdas volvería a tener el agujero.
        for (int dy = -RadioDefensaTorre; dy <= RadioDefensaTorre; dy++)
        {
            for (int dx = -RadioDefensaTorre; dx <= RadioDefensaTorre; dx++)
            {
                var c = new Cell(ruta.End.X + dx, ruta.End.Y + dy);

                if (InGrid(c))
                    _colocable[Index(c)] = true;
            }
        }

        // ⚠ Una celda de la ruta nunca es colocable, aunque el layout la liste.
        // Es una guarda contra el error de dato más fácil de cometer al pintar
        // un mapa a mano —un rectángulo de zona que se come una vuelta del
        // camino— y el síntoma sería un defensor tapando el paso, que en este
        // juego no significa nada porque los enemigos no esquivan: siguen la
        // ruta y punto.
        for (long d = 0; d <= ruta.Length; d += Ints.ThousandthsPerCell / 2)
        {
            var c = ruta.CellAt(d);

            if (!InGrid(c))
                continue;

            _colocable[Index(c)] = false;
            _esRuta[Index(c)] = true;
        }
    }

    public int Width { get; }

    public int Height { get; }

    public Route Route { get; }

    /// <summary>
    /// La Torre quedó tan cerca de un borde que su anillo de defensa está
    /// cortado. Lo mira el validador de datos.
    /// </summary>
    public bool CeldasDeDefensaCortadas { get; }

    public bool InGrid(Cell c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

    /// <summary>¿Se puede poner un defensor acá?</summary>
    public bool EsColocable(Cell c) => InGrid(c) && _colocable[Index(c)];

    /// <summary>
    /// ¿Pasa la ruta por esta celda?
    /// </summary>
    /// <remarks>
    /// Fuera de la grilla devuelve <c>false</c>: el borde del mundo no es camino,
    /// y quien orienta un defensor necesita que el afuera cuente como "acá no hay
    /// nada" y no como un caso especial que tenga que recordar.
    /// </remarks>
    public bool EsRuta(Cell c) => InGrid(c) && _esRuta[Index(c)];

    private int Index(Cell c) => (c.Y * Width) + c.X;
}
