namespace TowerOfArgentum.Simulation;

/// <summary>
/// Índice espacial de los enemigos, para no recorrer la lista entera por cada
/// defensor en cada tick.
/// </summary>
/// <remarks>
/// <para><b>El problema que resuelve.</b> Elegir objetivo era O(defensores ×
/// enemigos): con 60 enemigos y 30 defensores son 1.800 comparaciones por tick,
/// 54.000 por segundo. Anda igual, pero crece con el cuadrado justo en las
/// oleadas donde hay más de todo.</para>
///
/// <para>La grilla reparte los enemigos en celdas y un defensor mira solo las
/// celdas que le quedan en alcance. Con alcance 6 son 169 celdas en el peor caso,
/// pero la mayoría están vacías.</para>
///
/// <para>⚠ <b>Recorre siempre en orden de spawn.</b> Este es el punto delicado:
/// un índice espacial es exactamente la clase de estructura que reordena las
/// cosas sin avisar, y eso rompería la regla de desempate del combate —empate por
/// menor id de spawn— y con ella el determinismo. Por eso cada celda guarda
/// <b>índices</b> en la lista original y el que consulta los ordena antes de
/// usarlos.</para>
///
/// <para>Está basada en <c>Scripts\Spatial\</c> del cliente de Austral, pero
/// escrita de nuevo: la de allá indexa en coordenadas de mundo con flotantes, y
/// acá todo es entero y la posición sale de la ruta.</para>
/// </remarks>
public sealed class SpatialGrid
{
    private readonly int _ancho;
    private readonly int _alto;
    private readonly List<int>[] _celdas;

    public SpatialGrid(int ancho, int alto)
    {
        _ancho = ancho;
        _alto = alto;
        _celdas = new List<int>[ancho * alto];

        for (int i = 0; i < _celdas.Length; i++)
            _celdas[i] = [];
    }

    /// <summary>
    /// Vacía y vuelve a llenar la grilla con las posiciones de este tick.
    /// </summary>
    /// <remarks>
    /// Se reconstruye entera en vez de moverse incrementalmente. Con 60 enemigos
    /// es más barato que llevar la cuenta de quién cambió de celda, y —lo que
    /// importa más— no puede desincronizarse: un índice incremental con un bug
    /// deja un enemigo fantasma en una celda vieja, y el síntoma sería un
    /// defensor disparándole a la nada.
    /// </remarks>
    public void Rebuild(IReadOnlyList<Enemy> enemigos, Route ruta)
    {
        foreach (var celda in _celdas)
            celda.Clear();

        for (int i = 0; i < enemigos.Count; i++)
        {
            var e = enemigos[i];

            if (!e.Alive)
                continue;

            var c = ruta.CellAt(e.DistanceTravelled);

            if (c.X < 0 || c.X >= _ancho || c.Y < 0 || c.Y >= _alto)
                continue;

            _celdas[(c.Y * _ancho) + c.X].Add(i);
        }
    }

    /// <summary>
    /// Los índices de los enemigos que pueden estar en alcance, <b>en orden de
    /// spawn</b>.
    /// </summary>
    /// <remarks>
    /// Devuelve candidatos, no una respuesta: el rectángulo de celdas es más
    /// grande que el círculo de alcance, así que el que llama tiene que comparar
    /// la distancia igual. Lo que se ahorra es mirar a los que están lejos.
    /// </remarks>
    public void Candidates(Cell centro, int alcanceCeldas, List<int> salida)
    {
        salida.Clear();

        int x0 = System.Math.Max(0, centro.X - alcanceCeldas);
        int x1 = System.Math.Min(_ancho - 1, centro.X + alcanceCeldas);
        int y0 = System.Math.Max(0, centro.Y - alcanceCeldas);
        int y1 = System.Math.Min(_alto - 1, centro.Y + alcanceCeldas);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
                salida.AddRange(_celdas[(y * _ancho) + x]);
        }

        // ⚠ El orden de las celdas NO es el orden de spawn, así que hay que
        // ordenar. Sin esto, el desempate del combate —el enemigo con menor id de
        // spawn gana— dependería de en qué celda cayó cada uno, y dos corridas
        // idénticas darían resultados distintos.
        //
        // Sort de enteros sobre listas cortas: es más barato que el recorrido que
        // se ahorró.
        salida.Sort();
    }
}
