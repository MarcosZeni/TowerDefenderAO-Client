namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Lo que hizo el jugador. <b>Decisiones, nunca resultados.</b>
/// </summary>
/// <remarks>
/// <para>Todo lo que el RNG sembrado determina —qué NPC sale con perk, qué ítem
/// suelta, qué hay en la tienda, qué 3 perks se ofrecen— el servidor lo
/// <b>regenera</b> de la seed al revalidar. Guardarlo también sería inflar un log
/// que ya es grande con corridas de 100 oleadas, y no aportaría nada: el
/// servidor no le creería al cliente de todos modos.</para>
///
/// <para>Lo que el servidor <b>no</b> puede regenerar es lo que decidió una
/// persona. Eso, y solo eso, es lo que va acá.</para>
/// </remarks>
public enum ActionType
{
    /// <summary>Poner un defensor de una clase en una celda.</summary>
    Place = 0,

    /// <summary>Vender un defensor puesto. Devuelve el 50%.</summary>
    Sell = 1,

    /// <summary>Elegir una de las 3 opciones de la pantalla de elección.</summary>
    Choose = 2,

    /// <summary>Equipar un perk de la mochila en un slot de una clase.</summary>
    EquipPerk = 3,

    /// <summary>
    /// Sacar un perk equipado. <b>Lo destruye</b>: no vuelve a la mochila.
    /// </summary>
    RemovePerk = 4,

    /// <summary>Equipar una pieza de equipo en una clase.</summary>
    Equip = 5,

    /// <summary>Vender una pieza de equipo. El equipo sí se vende.</summary>
    SellGear = 6,

    /// <summary>Comprar algo en la tienda de la corrida.</summary>
    Buy = 7,

    /// <summary>Fusionar dos perks de Oro en un Único, con un plano aprendido.</summary>
    Fuse = 8,

    /// <summary>
    /// Llamar la próxima oleada antes de tiempo.
    ///
    /// <para>Existe por el ranking: sin esto el desempate por <c>ticks</c> no
    /// mide nada, porque todos los que ganan tardan lo mismo.</para>
    /// </summary>
    CallNextWave = 9,

    /// <summary>
    /// Tirar algo de la mochila para hacer lugar. Es una decisión —cuál se
    /// pierde— así que va al log.
    /// </summary>
    Discard = 10,

    /// <summary>
    /// Comprar un nivel de una clase del roster con el oro de la corrida.
    /// </summary>
    /// <remarks>
    /// ⚠ Es el sumidero de oro de la corrida: sin esto el jugador terminaba con
    /// el tablero lleno, la mochila llena y ~127.000 de oro que no compraban
    /// nada. El precio es cuadrático para que temprano el oro siga yendo al
    /// tablero. <b>No reemplaza la elección de cada tres oleadas</b>: esa sigue
    /// siendo la única forma de CONTRATAR una clase nueva.
    /// </remarks>
    BuyLevel = 11,

    /// <summary>
    /// Cambiarle el modo de targeting a un defensor puesto.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es del defensor, no de la clase</b>, y es la única acción del
    /// juego que lo es. Dos paladines de la misma ficha quieren targeting distinto
    /// según dónde estén parados —el de la entrada abre la oleada, el de la Torre
    /// contiene filtraciones—, y esa decisión de colocación es la mitad del valor
    /// de la mecánica.</para>
    ///
    /// <para>Es la mecánica de más profundidad por menos complejidad del género:
    /// cambia tácticamente el resultado sin tocar una sola estadística.</para>
    /// </remarks>
    SetTargeting = 12,

    /// <summary>
    /// Elegir cuál de los dos comportamientos de una clase está activo: el del
    /// perk de Oro/Único equipado, o el de la habilidad del arma.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>La ranura sigue siendo una.</b> Esto no permite tener los dos: permite
    /// elegir. Existe porque encontrar un arma excelente se sentía como un castigo
    /// —obligaba a apagar un Único que costó dos jefes y un plano— y el jugador
    /// terminaba no equipándola, que es lo peor que le puede pasar a un drop. Los
    /// <b>stats</b> del arma se conservan en los dos casos.
    /// </remarks>
    SetBehavior = 13,

    /// <summary>
    /// Mejorar varios perks de un tier en uno solo del tier de arriba.
    /// </summary>
    /// <remarks>
    /// <para>Existe porque el tope de la mochila lo marcaba la <b>basura</b>: con
    /// 4% de drop por bicho común y hasta 60 bichos por oleada, los 15 lugares
    /// se llenan de Bronce y Plata que nadie va a equipar, y a partir de ahí todo
    /// drop cae al vacío en silencio. La mejora le da salida a lo que sobra sin
    /// agrandar la mochila —agrandarla convertiría «qué me sirve» en una
    /// planilla, que es justamente lo que el tope evita—.</para>
    ///
    /// <para>⚠ <b><c>A</c> es una MÁSCARA DE BITS</b>, un bit por lugar de
    /// mochila, y <c>B</c> el tier de entrada. No entran cuatro índices en tres
    /// campos, y guardar «los primeros cuatro de ese tier» en vez de la selección
    /// haría que el servidor consumiera perks distintos de los que el jugador
    /// eligió: dos Bronce del mismo tier no son intercambiables si uno es de daño
    /// y el otro de alcance.</para>
    ///
    /// <para>Por eso <c>backpackSlots</c> no puede pasar de 31, y el cargador de
    /// datos lo verifica.</para>
    /// </remarks>
    Upgrade = 14,

    /// <summary>
    /// Mover una ficha ya puesta a otra celda. <b>Gratis, y solo en preparación.</b>
    /// </summary>
    /// <remarks>
    /// <para><c>A</c> = <c>PlacementId</c>, <c>B</c> y <c>C</c> = la celda
    /// destino. Si la celda tiene otra ficha, las dos <b>se intercambian</b>: las
    /// dos celdas ya eran válidas para las dos, así que el cambio nunca puede
    /// dejar a nadie en un lugar prohibido.</para>
    ///
    /// <para>⚠ <b>Solo con el reloj congelado.</b> Mover con la oleada corriendo
    /// es reposicionar en combate —perseguir al bicho, esquivar al jefe— que es
    /// otra mecánica y no está balanceada. Con el reloj detenido no cuesta ticks
    /// y por lo tanto no toca el ranking.</para>
    ///
    /// <para>Gratis y no al 60% como vender: cobrar por acomodar hacía que
    /// convenga dejar una ficha mal puesta, que es exactamente lo que la
    /// devolución del 60% ya venía a evitar.</para>
    /// </remarks>
    MoveDefender = 15,

    /// <summary>
    /// Saltear la elección sin quedarse con ninguna opción.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Existe porque la elección DEJÓ DE SER GRATIS.</b> Mientras
    /// regalaba, «no se puede cerrar sin elegir» era una regla sana: no había
    /// motivo para no llevarse algo. Con las tres opciones cobrando, un jugador
    /// sin oro quedaría trabado en un modal que no se puede cerrar y con el reloj
    /// congelado — o sea, la corrida muerta sin haber perdido.</para>
    ///
    /// <para>Sin parámetros: no hay nada que elegir, y el cuándo ya es el tick.</para>
    /// </remarks>
    SkipChoice = 16,
}

/// <summary>
/// Una acción del jugador, atada al tick en que la hizo.
/// </summary>
/// <remarks>
/// Los campos son de propósito general a propósito: <c>A</c>, <c>B</c> y
/// <c>C</c> significan cosas distintas según el <see cref="Tipo"/>, y qué
/// significan está en <see cref="Accion"/>. Un registro por tipo de acción sería
/// más lindo de leer y mucho peor de serializar: el log tiene tope de tamaño y
/// esto se escribe decenas de miles de veces por corrida.
/// </remarks>
public readonly record struct PlayerAction(int Tick, ActionType Kind, int A, int B, int C)
{
    /// <summary>Poner: A = clase, B = celda X, C = celda Y.</summary>
    public static PlayerAction Place(int tick, DefenderClass clase, Cell celda)
        => new(tick, ActionType.Place, (int)clase, celda.X, celda.Y);

    /// <summary>Vender: A = idColocacion del defensor.</summary>
    public static PlayerAction Sell(int tick, int idColocacion)
        => new(tick, ActionType.Sell, idColocacion, 0, 0);

    /// <summary>
    /// Elegir: A = cuál de las 3 opciones (0, 1 o 2).
    ///
    /// <para>Se guarda el <b>índice</b>, no qué era la opción. Qué había en cada
    /// una lo regenera el servidor de la seed; si se guardara el contenido, un
    /// cliente modificado podría reclamar que le ofrecieron algo mejor.</para>
    /// </summary>
    public static PlayerAction Choose(int tick, int opcion)
        => new(tick, ActionType.Choose, opcion, 0, 0);

    /// <summary>Adelantar oleada. Sin parámetros: el cuándo ya es el tick.</summary>
    public static PlayerAction CallNextWave(int tick)
        => new(tick, ActionType.CallNextWave, 0, 0, 0);
}

/// <summary>
/// El log de la corrida.
/// </summary>
/// <remarks>
/// <para><b>Este objeto es también el archivo de guardado.</b> Sale gratis de la
/// determinismo: guardar es escribir estas acciones a disco, y cargar es
/// re-simular desde el tick 0 a máxima velocidad. No hay que serializar el
/// estado del juego, no hay formato de save que versionar, y no puede quedar
/// desincronizado del código — si el save carga, es porque el código lo reprodujo
/// exactamente.</para>
/// </remarks>
public sealed class ActionLog
{
    private readonly List<PlayerAction> _acciones = [];

    public IReadOnlyList<PlayerAction> All => _acciones;

    public int Count => _acciones.Count;

    /// <summary>
    /// Agrega una acción. <b>El orden de inserción es el orden de aplicación</b>
    /// cuando dos caen en el mismo tick (JUGABILIDAD.md §4.4).
    /// </summary>
    public void Agregar(PlayerAction accion) => _acciones.Add(accion);

    /// <summary>
    /// Las acciones de un tick, en el orden en que se registraron.
    /// </summary>
    /// <remarks>
    /// Recorrido lineal desde <paramref name="desde"/>, que la simulación
    /// avanza: el log está ordenado por tick porque se escribe mientras se
    /// juega, así que no hace falta buscar ni ordenar. Ordenarlo sería además
    /// peligroso — un sort inestable cambiaría el orden de dos acciones del
    /// mismo tick, y con él el resultado de la corrida.
    /// </remarks>
    public int AplicarDesde(int desde, int tick, Action<PlayerAction> aplicar)
    {
        int i = desde;
        while (i < _acciones.Count && _acciones[i].Tick == tick)
        {
            aplicar(_acciones[i]);
            i++;
        }
        return i;
    }
}
