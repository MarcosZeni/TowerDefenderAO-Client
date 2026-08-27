namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Los cuatro escalones del catálogo.
/// </summary>
/// <remarks>
/// <para><b>La línea que divide todo pasa entre Plata y Oro: Bronce y Plata son
/// cantidad, Oro y Único son comportamiento.</b> De ahí sale la regla de apilado
/// sin tener que pensarla perk por perk — "Ricochet dos veces" no quiere decir
/// nada, "+100 de daño dos veces" sí.</para>
///
/// <para>Y de ahí sale también por qué un Bronce puede decir <c>+100</c> y un
/// Único <c>+50</c> sin que esté mal: <b>no se comparan en el mismo eje</b>. El
/// Bronce escala un número que ya existía; el Único agrega algo que la clase no
/// hacía.</para>
/// </remarks>
public enum PerkTier
{
    Bronze = 0,
    Silver = 1,
    Gold = 2,
    Unique = 3,
}

/// <summary>Qué toca un perk numérico.</summary>
public enum PerkEffect
{
    None = 0,
    Damage = 1,
    Cadence = 2,
    Range = 3,

    /// <summary>Comportamiento: el disparo rebota a un segundo enemigo.</summary>
    Ricochet = 10,

    /// <summary>Comportamiento: el golpe daña también alrededor del objetivo.</summary>
    Splash = 11,

    /// <summary>Comportamiento: el objetivo queda más lento un rato.</summary>
    Slow = 12,

    /// <summary>Comportamiento: cada tantos golpes, uno pega mucho más.</summary>
    CriticalHit = 13,

    /// <summary>Comportamiento: envenena, daño por tick durante un rato.</summary>
    Poison = 14,

    /// <summary>Comportamiento: cada tantos golpes, uno atraviesa la defensa.</summary>
    Piercing = 15,

    // -------------------------------------------------------------------------
    //  Únicos — no caen ni se compran: salen de fusionar dos Oro con un plano
    // -------------------------------------------------------------------------

    /// <summary>Ricochet que además atraviesa.</summary>
    Perforation = 20,

    /// <summary>Splash que además ralentiza.</summary>
    FrostWave = 21,

    /// <summary>Crítico que además hace splash.</summary>
    WrathOfGod = 22,

    /// <summary>Ricochet en cadena, sin tope de rebotes cercanos.</summary>
    Apocalypse = 23,

    /// <summary>Veneno que además ralentiza.</summary>
    TerribleHunger = 24,

    /// <summary>Splash grande que pega dos veces.</summary>
    DivineStorm = 25,
}

/// <summary>
/// Una definición del catálogo.
/// </summary>
/// <remarks>
/// ⚠ Los perks <b>no son de la unidad: son de la clase</b>. Toda unidad de esa
/// clase los tiene, la que ya estaba puesta y la que se ponga después. Un
/// defensor no guarda perks: calcula <c>base + nivel + perks + equipo</c> cada
/// vez, así que no hay estado duplicado ni desincronización posible.
/// </remarks>
public sealed class PerkDefinition
{
    public required int Id { get; init; }

    public required string Key { get; init; }

    public required PerkTier Tier { get; init; }

    public required PerkEffect Effect { get; init; }

    /// <summary>Cuánto suma, en puntos porcentuales. Cero si es comportamiento.</summary>
    public int Magnitude { get; init; }

    /// <summary>
    /// Qué clases lo pueden equipar. Vacío = cualquiera.
    /// </summary>
    /// <remarks>
    /// <b>Que un Oro sirva para 2-3 clases es lo que hace escribible el
    /// catálogo.</b> Con todo lo interesante siendo exclusivo, la cuenta daba
    /// entre 50 y 90 definiciones; compartiendo Bronce y Plata entre las 9 y Oro
    /// entre dos o tres, quedan ~40.
    /// </remarks>
    public DefenderClass[] Classes { get; init; } = [];

    /// <summary>
    /// Cuántas copias del mismo perk se pueden equipar en una clase.
    /// </summary>
    /// <remarks>
    /// Sale del tier por defecto —Bronce 3, Plata 2, Oro y Único 1— y solo se
    /// escribe a mano cuando un perk necesita otra cosa. La regla es la misma de
    /// arriba: <b>apilable si es un número, único si es un comportamiento</b>.
    /// </remarks>
    public int MaxStacked { get; init; }

    public bool WorksFor(DefenderClass clase) => Classes.Length == 0 || System.Array.IndexOf(Classes, clase) >= 0;
}

/// <summary>
/// Una receta de fusión: dos Oro más un plano dan un Único.
/// </summary>
/// <remarks>
/// <para>El plano se compra <b>una vez, con la billetera, desde el menú
/// principal</b>, y queda aprendido para siempre en esa cuenta. Junto con la
/// billetera, es la única progresión permanente del proyecto.</para>
///
/// <para>La lista es <b>finita</b>, y esa es la condición que hace tolerable la
/// ventaja del veterano: tiene techo. Por eso no se agrega equipo permanente ni
/// nada que escale sin fin.</para>
///
/// <para>⚠ Las recetas viven en <c>Datos\planos.json</c> y se referencian por
/// <b>nombre</b>: los ids se resuelven al cargar. Un id escrito a mano quedaría
/// apuntando a otro perk en cuanto alguien agregue uno en el medio, y el síntoma
/// sería una fusión que produce algo que no es.</para>
/// </remarks>
public sealed record Blueprint(string Key, int GoldA, int GoldB, int ResultingUnique, bool Initial);
