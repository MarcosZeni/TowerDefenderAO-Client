namespace TowerOfArgentum.Simulacion;

/// <summary>Un enemigo del bestiario, ya reescalado.</summary>
public sealed class DatoEnemigo
{
    public required int Id { get; init; }
    public required string Nombre { get; init; }
    public required int Area { get; init; }
    public required int Hp { get; init; }
    public required int DanoMin { get; init; }
    public required int DanoMax { get; init; }
    public required int Velocidad { get; init; }
    public required int CadenciaTicks { get; init; }
    public required int Oro { get; init; }

    /// <summary>El <c>Body</c> de Austral. Pista para el pipeline de arte, no un `fileNum`.</summary>
    public int Body { get; init; }

    /// <summary>
    /// Porcentaje de tamaño al dibujar. 100 es el natural.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No entra en ninguna cuenta del tick.</b> Está en este archivo
    /// porque acá viven los datos del enemigo, no porque la simulación lo use: el
    /// enemigo no tiene tamaño, tiene una <c>DistanciaRecorrida</c> escalar.</para>
    ///
    /// <para>Existe porque en Austral hay cuerpos que comparten dos o tres bichos
    /// con stats distintos —"Lobo" y "Lobo de dungeon", "Zombie" y "Lord
    /// Zombie"—. Dentro de un mismo cuerpo, más grande es más duro.</para>
    /// </remarks>
    public int EscalaVisual { get; init; } = 100;

    /// <summary>
    /// La clave de este bicho en el índice de gráficos, sin la dirección:
    /// <c>bicho.hormiga</c> se dibuja con <c>bicho.hormiga.sur</c>.
    /// </summary>
    /// <remarks>
    /// La escribe el importador. Nadie la deriva del nombre en runtime: si el
    /// importador renombra un bicho, la clave cambia con él y el que dibuja se
    /// entera.
    /// </remarks>
    public string Grafico { get; init; } = "";
}

/// <summary>
/// Todos los datos de balance de una temporada.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Esto es ENTRADA de la simulación</b>, igual que la seed y el log.
/// Cambiar un número de acá vuelve irreplicables las corridas viejas: la misma
/// seed y el mismo log dan otro resultado. Por eso cada rebalanceo es una
/// <b>temporada</b> del ranking, separada por <see cref="Version"/>.</para>
///
/// <para>La simulación recibe esto por constructor y no lo lee de disco: leer
/// archivos es del cliente, que sabe de Godot. Así el mismo código puede correr
/// en el servidor para revalidar, donde los datos vienen de otro lado.</para>
/// </remarks>
public sealed class DatosBalance
{
    public required int Version { get; init; }

    /// <summary>
    /// Hash del contenido. Viaja en <c>/run/start</c> y el servidor lo compara.
    /// </summary>
    /// <remarks>
    /// Si el cliente juega con otros números que el servidor, la corrida no es
    /// reproducible ni comparable. <b>Rechazarla al empezar es infinitamente mejor
    /// que rechazarla después de tres horas.</b>
    /// </remarks>
    public required string Hash { get; init; }

    public required IReadOnlyList<DatoEnemigo> Enemigos { get; init; }

    // -------------------------------------------------------------------------
    //  Las tablas que antes vivían escritas en el código
    // -------------------------------------------------------------------------
    //
    //  ⚠ Todas son ENTRADA de la simulación y todas entran al hash, igual que el
    //  bestiario y los layouts. Cuelgan de acá y no de una clase estática por el
    //  mismo motivo por el que la simulación no lee disco: el servidor arma este
    //  objeto de otra fuente al revalidar, y con tablas estáticas tendría que
    //  compilar los mismos números adentro del binario.

    /// <summary>Las nueve clases y la curva de nivel. Sale de <c>clases.json</c>.</summary>
    public required TablaClases Clases { get; init; }

    /// <summary>El catálogo de perks. Sale de <c>perks.json</c>.</summary>
    public required TablaPerks Perks { get; init; }

    /// <summary>Las recetas de fusión. Sale de <c>planos.json</c>.</summary>
    public required TablaPlanos Planos { get; init; }

    /// <summary>Los parámetros del generador de armas. Sale de <c>generador-armas.json</c>.</summary>
    public required TablaArmas Armas { get; init; }

    /// <summary>La Torre, la corrida, la economía y las oleadas. Sale de <c>curvas.json</c>.</summary>
    public required DatoCurvas Curvas { get; init; }

    /// <summary>Los números del tick. Sale de <c>combate.json</c>.</summary>
    public required DatoCombate Combate { get; init; }

    /// <summary>El reparto de las 100 oleadas entre las 8 áreas.</summary>
    /// <remarks>
    /// Se deriva de <see cref="Curvas"/> al indexar, para que no haya dos copias
    /// del mismo array que puedan discrepar.
    /// </remarks>
    public TablaAreas Areas { get; private set; } = new([100]);

    /// <summary>Atajo a los stats base de una clase.</summary>
    public DatoClase StatsDe(Clase clase) => Clases.De(clase);

    /// <summary>
    /// Los layouts entre los que elige la seed.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Son entrada de la simulación</b>, igual que el bestiario:
    /// cambiar un layout cambia la corrida, así que entran al hash y el servidor
    /// arma exactamente los mismos al revalidar. Antes eran tres formas escritas
    /// en código; ahora se dibujan con el editor y se leen del disco.</para>
    ///
    /// <para>El orden importa y tiene que ser el mismo en las dos mitades, porque
    /// la seed elige por índice. Quien carga los ordena por nombre de archivo.</para>
    /// </remarks>
    public required IReadOnlyList<Mapa> Layouts { get; init; }

    /// <summary>
    /// El layout que le toca a una corrida.
    /// </summary>
    /// <remarks>
    /// El RNG que se le pasa tiene que ser el <b>de mapa</b>, aparte del de
    /// combate: si comparten stream, agregar una tirada de layout corre todas las
    /// tiradas de combate que vienen después y la corrida vieja deja de
    /// reproducirse.
    /// </remarks>
    public Mapa LayoutElegido(Aleatorio rng)
        => Layouts.Count == 0
            ? throw new System.InvalidOperationException("no hay layouts cargados")
            : Layouts[rng.Rango(0, Layouts.Count)];

    private readonly Dictionary<int, DatoEnemigo> _porId = [];

    /// <summary>Índice 0 sin usar: las áreas se cuentan desde 1.</summary>
    private List<DatoEnemigo>[] _porArea = [];

    /// <summary>Arma los índices. Se llama una vez, al cargar.</summary>
    public void Indexar()
    {
        Areas = new TablaAreas(Curvas.Corrida.UltimaOleadaPorArea);

        _porArea = new List<DatoEnemigo>[Curvas.Corrida.CantidadAreas + 1];

        for (int a = 0; a < _porArea.Length; a++)
            _porArea[a] = [];

        foreach (var e in Enemigos)
        {
            _porId[e.Id] = e;

            if (e.Area >= 1 && e.Area < _porArea.Length)
                _porArea[e.Area].Add(e);
        }
    }

    public DatoEnemigo? Por(int id) => _porId.GetValueOrDefault(id);

    public IReadOnlyList<DatoEnemigo> DeArea(int area)
        => area >= 1 && area < _porArea.Length ? _porArea[area] : [];

    /// <summary>
    /// El jefe de un área: el más duro de esa área.
    /// </summary>
    /// <remarks>
    /// Se deriva en vez de escribirse a mano, por lo mismo que el área: agregar
    /// una criatura nueva no obliga a decidir nada. Y como la lista viene ordenada
    /// por HP, es el último.
    /// </remarks>
    public DatoEnemigo? JefeDe(int area)
    {
        var lista = DeArea(area);

        if (lista.Count == 0)
            return null;

        // ⚠ El jefe de la ÚLTIMA área se elige por NOMBRE y no por HP.
        //
        // Es la única excepción a "el jefe es el más duro del área", y es
        // deliberada: matarlo es cómo se gana la corrida, y ese momento tiene que
        // ser reconocible. Por HP crudo el más duro del área 8 es el Golem, que
        // como final de cien oleadas no dice nada.
        //
        // El nombre sale de curvas.json («jefe.nombreJefeFinal»). Si el bestiario
        // no lo trae —porque cambió el filtro del importador— se cae al más duro,
        // que es lo correcto: mejor un final genérico que una corrida que no se
        // puede terminar.
        if (area == Curvas.Corrida.CantidadAreas && Curvas.Jefe.NombreJefeFinal.Length > 0)
        {
            foreach (var e in lista)
            {
                if (e.Nombre.Contains(Curvas.Jefe.NombreJefeFinal, System.StringComparison.OrdinalIgnoreCase))
                    return e;
            }
        }

        return lista[^1];
    }
}
