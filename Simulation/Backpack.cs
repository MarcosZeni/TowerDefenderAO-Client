namespace TowerOfArgentum.Simulation;

/// <summary>Qué es una cosa guardada.</summary>
/// <remarks>
/// Solo dos. ⚠ <b>Armadura, casco y escudo no son ítems</b>: no dan stats y no
/// ocupan lugar. Son el frame que cambia cuando la clase sube de nivel.
/// </remarks>
public enum ItemType
{
    Perk = 0,

    /// <summary>Un arma. El único ítem con stats del juego.</summary>
    Weapon = 1,
}

/// <summary>Una cosa en la mochila: o un perk, o un arma.</summary>
public readonly record struct StoredItem(ItemType Kind, int Id);

/// <summary>
/// La mochila. <b>15 lugares, compartidos entre perks y equipo.</b>
/// </summary>
/// <remarks>
/// <para>Sirve para dos cosas concretas: guardar un perk de una clase que
/// todavía no contrataste, y aguantar uno bueno hasta que se abra un slot al
/// levelear. Sin mochila, todo perk que cae en mal momento se pierde, y eso
/// castiga sin enseñar nada.</para>
///
/// <para><b>El límite es lo que la hace jugable.</b> Sin tope se convierte en el
/// banco de un MMO: juntás todo, ordenás después, y "qué perk me sirve" deja de
/// ser una decisión para volverse una planilla. Con un tope chico, tener la
/// mochila llena <b>es</b> una decisión.</para>
///
/// <para><b>Una cosa, un lugar. Una sola mochila para las dos.</b> Sin tamaños
/// distintos, sin pesos, sin dos bolsos: eso es Tetris de inventario, que es un
/// juego distinto. Austral lo tiene porque es un MMO; un TD no lo necesita.</para>
/// </remarks>
public sealed class Backpack(BalanceData datos)
{
    private readonly List<StoredItem> _cosas = [];
    private readonly Dictionary<int, Weapon> _armas = [];
    private int _proximoIdArma;

    public IReadOnlyList<StoredItem> Items => _cosas;

    public int Used => _cosas.Count;

    /// <summary>Cuántos lugares tiene. Sale de <c>curvas.json</c>.</summary>
    public int Cap { get; } = datos.Curves.Defenders.BackpackSlots;

    public bool Full => _cosas.Count >= Cap;

    public Weapon? WeaponById(int id) => _armas.GetValueOrDefault(id);

    /// <summary>
    /// Mete algo. Devuelve <c>false</c> si no había lugar.
    /// </summary>
    /// <remarks>
    /// Cuando devuelve <c>false</c>, la UI muestra los 15 de la mochila
    /// <b>más el que acaba de caer</b> y el jugador destruye uno. Que el entrante
    /// esté en la lista importa: si no, un drop malo obliga a romper algo bueno, y
    /// perder algo por un ítem que ni se quería es de las cosas que más molestan.
    /// Es una decisión, así que va al log como <c>Descartar</c>.
    /// </remarks>
    public bool Store(StoredItem cosa)
    {
        if (Full)
            return false;

        _cosas.Add(cosa);
        return true;
    }

    public bool StorePerk(int idPerk) => Store(new StoredItem(ItemType.Perk, idPerk));

    /// <summary>
    /// Registra un arma generada y devuelve su id.
    /// </summary>
    /// <remarks>
    /// El id lo asigna la mochila y no el generador, porque tiene que ser único
    /// dentro de la corrida: el log guarda "equipé el arma 7", y si dos armas
    /// compartieran número, re-simular equiparía la otra.
    /// </remarks>
    public int RegisterWeapon(Weapon plantilla)
    {
        int id = _proximoIdArma++;

        _armas[id] = new Weapon
        {
            Id = id,
            BaseKind = plantilla.BaseKind,
            Rarity = plantilla.Rarity,
            GraphicId = plantilla.GraphicId,
            Damage = plantilla.Damage,
            Cadence = plantilla.Cadence,
            Range = plantilla.Range,
            Ability = plantilla.Ability,
            Magnitude = plantilla.Magnitude,
            Classes = plantilla.Classes,
            Value = plantilla.Value,
        };

        return id;
    }

    /// <summary>Cuántas armas se registraron. Es el próximo id que va a salir.</summary>
    public int NextWeaponId => _proximoIdArma;

    /// <summary>Saca una cosa de la mochila. El objeto en sí sigue existiendo.</summary>
    public bool Remove(StoredItem cosa)
    {
        int i = _cosas.IndexOf(cosa);
        if (i < 0)
            return false;

        // RemoveAt y no swap-back: el orden de la mochila es lo que el jugador
        // ve, y además cualquier reordenamiento silencioso cambia qué elige un
        // índice del log al re-simular.
        _cosas.RemoveAt(i);
        return true;
    }

    /// <summary>Saca por posición, que es como la nombra el log.</summary>
    public StoredItem? RemoveAt(int indice)
    {
        if (indice < 0 || indice >= _cosas.Count)
            return null;

        var cosa = _cosas[indice];
        _cosas.RemoveAt(indice);
        return cosa;
    }
}

