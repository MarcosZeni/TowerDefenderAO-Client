namespace TowerOfArgentum.Simulation;

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

/// <summary>Qué hace un perk.</summary>
/// <remarks>
/// <para>Del 1 al 3 son <b>números</b> —Bronce y Plata— y del 10 en adelante son
/// <b>comportamientos</b> —Oro y Único—. Es la misma línea que divide el catálogo
/// en <see cref="PerkTier"/>, escrita acá para que el <c>switch</c> del tick no
/// tenga que consultarla.</para>
///
/// <para>⚠ <b>Un comportamiento se aplica SOLO sobre el golpe directo.</b> Nada de
/// lo que sale de él —eco, splash, cadena, rebote, DoT, transferencia de
/// overkill— vuelve a disparar un comportamiento. Es la primera regla de
/// <c>Estados_v2</c> y existe para que no haya cadenas recursivas: sin ella,
/// Cadena crea Cadena y el daño de una oleada apretada explota.</para>
/// </remarks>
public enum PerkEffect
{
    None = 0,
    Damage = 1,
    Cadence = 2,
    Range = 3,

    // -------------------------------------------------------------------------
    //  Oro — comportamiento. Lo comparten 2-3 clases
    // -------------------------------------------------------------------------

    /// <summary>El disparo rebota a UN enemigo cercano por ruta.</summary>
    Ricochet = 10,

    /// <summary>El golpe daña también a los cercanos por ruta, con techo.</summary>
    Splash = 11,

    /// <summary>El objetivo camina más lento un rato. No apila: gana el más fuerte.</summary>
    Slow = 12,

    /// <summary>Cada N golpes DE ESE DEFENSOR, uno pega mucho más.</summary>
    CriticalHit = 13,

    /// <summary>Veneno: daño repartido en ticks, con presupuesto conservado.</summary>
    Poison = 14,

    /// <summary>Cadena acotada: salta a hasta dos enemigos, cada vez por menos.</summary>
    Chain = 16,

    /// <summary>Quemadura al objetivo y una versión menor a los cercanos.</summary>
    Burn = 17,

    /// <summary>El golpeado le hace menos daño a la Torre si llega.</summary>
    Weaken = 18,

    /// <summary>Cada N golpes, el objetivo recibe más daño DIRECTO de todos.</summary>
    Sunder = 19,

    /// <summary>Golpes seguidos al mismo objetivo suben el daño, con techo.</summary>
    Focus = 30,

    /// <summary>Marca al objetivo: las OTRAS clases le pegan más fuerte.</summary>
    HuntMark = 31,

    /// <summary>Sangrado: parte del golpe se cobra en ticks.</summary>
    Bleed = 32,

    /// <summary>Cada N golpes inmoviliza, con período refractario y tope.</summary>
    Root = 33,

    /// <summary>Cada N golpes empuja al enemigo hacia atrás por la ruta.</summary>
    Seismic = 34,

    /// <summary>Cada N ataques, aura de cadencia a los defensores cercanos.</summary>
    Inspire = 35,

    /// <summary>Marca de botín: si muere marcado, suelta oro extra, con tope.</summary>
    Loot = 36,

    /// <summary>Más daño directo contra enemigos por debajo de un umbral de vida.</summary>
    Opportunist = 37,

    /// <summary>Más daño directo contra los que ya están mordiendo la Torre.</summary>
    ThresholdGuard = 38,

    /// <summary>Cada N golpes, un eco diferido por una fracción del daño.</summary>
    ArcaneEcho = 39,

    /// <summary>Al matar, parte del overkill salta al vecino de ruta.</summary>
    Overpenetration = 40,

    /// <summary>Más daño directo contra enemigos por encima de un umbral de vida.</summary>
    FirstStrike = 41,

    /// <summary>Sin objetivo durante un rato, el próximo golpe sale cargado.</summary>
    Charge = 42,

    // -------------------------------------------------------------------------
    //  Únicos — no caen ni se compran: salen de fusionar dos Oro con un plano
    // -------------------------------------------------------------------------
    //
    //  ⚠ Cada Único COMBINA los dos Oro de su receta, AL VALOR PLENO DE LOS DOS.
    //
    //  Iban rebajados a ~80% de cada componente, con este argumento: «un Único que
    //  sumara dos Oro completos en un solo slot sería estrictamente mejor que
    //  tener los dos». El argumento era falso, y lo invalida una regla del mismo
    //  archivo: LA RANURA DE COMPORTAMIENTO ES UNA. Una clase no puede equipar dos
    //  Oro, así que «tener los dos» en la misma clase no existe.
    //
    //  Lo que fusionar cuesta de verdad: consume DOS de los ~8 Oro de la corrida,
    //  y el segundo podría haber ido a otra clase que también lo usa.
    //
    //  Por eso llevan dos números: `magnitude` y `magnitudeB` de perks.json.

    /// <summary>Cazador: Concentración + Sobrepenetración.</summary>
    Perforation = 20,

    /// <summary>Mago: Onda Expansiva + Escarcha.</summary>
    FrostWave = 21,

    /// <summary>Paladín: Guardia del Umbral + Quebranto.</summary>
    WrathOfGod = 22,

    /// <summary>Asesino: Hemorragia + Oportunista.</summary>
    Apocalypse = 23,

    /// <summary>Druida: Raíces + Tormenta de Fuego.</summary>
    TerribleHunger = 24,

    /// <summary>Clérigo: Inspiración + Quebranto.</summary>
    DivineStorm = 25,

    /// <summary>Bardo: Inspiración + Debilidad.</summary>
    Blindness = 26,

    /// <summary>Guerrero: Golpe Sísmico + Quebranto.</summary>
    Strength = 27,

    /// <summary>Ladrón: Saqueo + Sobrepenetración.</summary>
    Invisibility = 28,
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

    /// <summary>Cuánto pega, en puntos porcentuales.</summary>
    public int Magnitude { get; init; }

    /// <summary>
    /// El <b>segundo</b> número, para los perks que combinan dos cosas.
    /// </summary>
    /// <remarks>
    /// <para>Lo usan los Únicos, que son la fusión de dos Oro: Onda de Frío hace
    /// splash por <see cref="Magnitude"/> y ralentiza por esto. Sin el segundo
    /// número habría que partir la definición entre <c>perks.json</c> y
    /// <c>combate.json</c>, y el que balancea tendría que mirar dos archivos para
    /// entender un solo perk.</para>
    ///
    /// <para>Cero en los perks que hacen una sola cosa.</para>
    /// </remarks>
    public int MagnitudeB { get; init; }

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
/// <para>⚠ Las recetas viven en <c>Data\planos.json</c> y se referencian por
/// <b>nombre</b>: los ids se resuelven al cargar. Un id escrito a mano quedaría
/// apuntando a otro perk en cuanto alguien agregue uno en el medio, y el síntoma
/// sería una fusión que produce algo que no es.</para>
/// </remarks>
public sealed record Blueprint(string Key, int GoldA, int GoldB, int ResultingUnique, bool Initial);
