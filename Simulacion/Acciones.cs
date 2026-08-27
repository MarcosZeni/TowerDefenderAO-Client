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
