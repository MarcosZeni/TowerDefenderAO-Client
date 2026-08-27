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
public enum TierPerk
{
    Bronce = 0,
    Plata = 1,
    Oro = 2,
    Unico = 3,
}

/// <summary>Qué toca un perk numérico.</summary>
public enum EfectoPerk
{
    Ninguno = 0,
    Dano = 1,
    Cadencia = 2,
    Alcance = 3,

    /// <summary>Comportamiento: el disparo rebota a un segundo enemigo.</summary>
    Ricochet = 10,

    /// <summary>Comportamiento: el golpe daña también alrededor del objetivo.</summary>
    Splash = 11,

    /// <summary>Comportamiento: el objetivo queda más lento un rato.</summary>
    Ralentiza = 12,

    /// <summary>Comportamiento: cada tantos golpes, uno pega mucho más.</summary>
    GolpeCritico = 13,

    /// <summary>Comportamiento: envenena, daño por tick durante un rato.</summary>
    Veneno = 14,

    /// <summary>Comportamiento: cada tantos golpes, uno atraviesa la defensa.</summary>
    Penetrante = 15,

    // -------------------------------------------------------------------------
    //  Únicos — no caen ni se compran: salen de fusionar dos Oro con un plano
    // -------------------------------------------------------------------------

    /// <summary>Ricochet que además atraviesa.</summary>
    Perforacion = 20,

    /// <summary>Splash que además ralentiza.</summary>
    OndaDeFrio = 21,

    /// <summary>Crítico que además hace splash.</summary>
    IraDeDios = 22,

    /// <summary>Ricochet en cadena, sin tope de rebotes cercanos.</summary>
    Apocalipsis = 23,

    /// <summary>Veneno que además ralentiza.</summary>
    TerribleHambre = 24,

    /// <summary>Splash grande que pega dos veces.</summary>
    TormentaDivina = 25,
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
public sealed class DefinicionPerk
{
    public required int Id { get; init; }

    public required string Nombre { get; init; }

    public required TierPerk Tier { get; init; }

    public required EfectoPerk Efecto { get; init; }

    /// <summary>Cuánto suma, en puntos porcentuales. Cero si es comportamiento.</summary>
    public int Magnitud { get; init; }

    /// <summary>
    /// Qué clases lo pueden equipar. Vacío = cualquiera.
    /// </summary>
    /// <remarks>
    /// <b>Que un Oro sirva para 2-3 clases es lo que hace escribible el
    /// catálogo.</b> Con todo lo interesante siendo exclusivo, la cuenta daba
    /// entre 50 y 90 definiciones; compartiendo Bronce y Plata entre las 9 y Oro
    /// entre dos o tres, quedan ~40.
    /// </remarks>
    public Clase[] Clases { get; init; } = [];

    /// <summary>
    /// Cuántas copias del mismo perk se pueden equipar en una clase.
    /// </summary>
    /// <remarks>
    /// Sale del tier por defecto —Bronce 3, Plata 2, Oro y Único 1— y solo se
    /// escribe a mano cuando un perk necesita otra cosa. La regla es la misma de
    /// arriba: <b>apilable si es un número, único si es un comportamiento</b>.
    /// </remarks>
    public int MaxApilado { get; init; }

    public bool SirveA(Clase clase) => Clases.Length == 0 || System.Array.IndexOf(Clases, clase) >= 0;
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
public sealed record Plano(string Nombre, int OroA, int OroB, int UnicoResultante, bool Inicial);
