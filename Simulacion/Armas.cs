namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Lo que una habilidad de arma le hace al disparo.
/// </summary>
/// <remarks>
/// <para>⚠ Estas <b>no son</b> los <see cref="EfectoPerk"/>. Un perk es una
/// decisión de construcción: se elige, se equipa y ocupa un slot. Una habilidad
/// de arma <b>viene con el arma</b>: la tirás o no la tirás.</para>
///
/// <para>Y por eso pueden ser más raras. Un perk que rompe el juego arruina la
/// progresión, porque el jugador lo va a buscar siempre; un arma que rompe el
/// juego es un hallazgo, y la corrida siguiente no lo tiene.</para>
/// </remarks>
public enum WeaponAbility
{
    None = 0,

    // --- Comunes: tocan un número ---

    /// <summary>El golpe pega también alrededor del objetivo.</summary>
    Splash = 1,

    /// <summary>El disparo rebota a otro enemigo.</summary>
    Bounce = 2,

    /// <summary>El objetivo camina más lento.</summary>
    Frost = 3,

    /// <summary>Daño por tick durante un rato.</summary>
    Poison = 4,

    // --- Raras: cambian cómo funciona el defensor ---

    /// <summary>Cada N golpes, uno pega muchísimo más.</summary>
    Critical = 10,

    /// <summary>Cuanto más lejos el objetivo, más daño.</summary>
    Sniper = 11,

    /// <summary>Cuanto más cerca, más daño. Lo opuesto.</summary>
    Butcher = 12,

    /// <summary>Cada enemigo que mata sube el daño, hasta un techo. Se resetea al terminar la oleada.</summary>
    Thirst = 13,

    /// <summary>Dispara dos veces por cada cooldown.</summary>
    DoubleShot = 14,

    // --- Que rompen el juego: son el premio de una corrida ---

    /// <summary>
    /// El daño ignora todo y mata de un golpe a lo que esté por debajo de un
    /// porcentaje de vida.
    /// </summary>
    Execute = 20,

    /// <summary>Le pega a TODOS los enemigos en alcance, no a uno.</summary>
    Chain = 21,

    /// <summary>El oro que suelta lo que mata se multiplica.</summary>
    Greed = 22,

    /// <summary>Cada golpe le devuelve vida a la Torre.</summary>
    Vampirism = 23,
}

/// <summary>
/// De qué salió el nombre base del arma: del número que quedó más alto.
/// </summary>
/// <remarks>
/// Sale de los números y no al revés, así que un arma generada nunca promete
/// algo que no hace.
/// </remarks>
public enum WeaponBaseKind
{
    Damage = 0,
    Cadence = 1,
    Range = 2,
}

/// <summary>Cuán raro es un arma. Decide cuántos afijos lleva y de qué clase.</summary>
public enum WeaponRarity
{
    Common = 0,
    Rare = 1,
    Epic = 2,

    /// <summary>Una por corrida, si hay suerte. Trae una habilidad que rompe.</summary>
    Legendary = 3,
}

/// <summary>
/// Un arma. <b>El único ítem con stats del juego.</b>
/// </summary>
/// <remarks>
/// <para>⚠ Armadura, casco y escudo <b>no dan nada</b>: son el frame que cambia
/// cuando la clase sube de nivel. Todo el poder del equipo vive acá.</para>
///
/// <para>Concentrar los stats en una sola pieza hace dos cosas. La decisión de
/// equipar se vuelve nítida —hay un solo lugar, y lo que entra saca lo que
/// estaba— y las armas pueden ser mucho más locas sin desbalancear: si hubiera
/// cuatro piezas con habilidades, habría que pensar cada combinación de cuatro.</para>
/// </remarks>
public sealed class Weapon
{
    public required int Id { get; init; }

    /// <summary>De qué número salió su nombre. El texto lo pone `Loc`.</summary>
    /// <remarks>
    /// ⚠ El arma NO guarda su nombre: guarda de qué está hecho. El nombre es
    /// texto, cambia con el idioma, y esto vive en `Simulacion` que corre igual
    /// en el servidor. Guardar la cadena ataba cada arma generada al idioma en el
    /// que se jugó la corrida.
    /// </remarks>
    public required WeaponBaseKind BaseKind { get; init; }

    public required WeaponRarity Rarity { get; init; }

    /// <summary>Qué gráfico usa. Índice en el catálogo de armas del arte.</summary>
    public required int GraphicId { get; init; }

    // --- Números, en puntos porcentuales sobre la base de la clase ---

    public int Damage { get; init; }

    public int Cadence { get; init; }

    public int Range { get; init; }

    // --- La habilidad, si tiene ---

    public WeaponAbility Ability { get; init; }

    /// <summary>Cuánto pega la habilidad, en porcentaje del daño del golpe.</summary>
    public int Magnitude { get; init; }

    /// <summary>
    /// Qué clases la pueden usar. Vacío = cualquiera.
    /// </summary>
    /// <remarks>
    /// Un arco en manos de un guerrero no tiene sentido, y un arma que solo sirve
    /// para una clase que no está en el roster es un lugar de mochila tirado. El
    /// generador filtra por las clases que el jugador tiene.
    /// </remarks>
    public DefenderClass[] Classes { get; init; } = [];

    /// <summary>Lo que paga la tienda. Las armas se venden.</summary>
    public required int Value { get; init; }

    public bool WorksFor(DefenderClass clase) => Classes.Length == 0 || System.Array.IndexOf(Classes, clase) >= 0;
}

/// <summary>
/// Genera armas: base, afijos y —si hay suerte— una habilidad.
/// </summary>
/// <remarks>
/// <para>Es el cuarto uso del mismo patrón de presupuesto —oleada, drop, tienda y
/// ahora arma— y por el mismo motivo: es la única forma de que el contenido
/// generado tenga un techo conocido.</para>
///
/// <para>La diferencia con el generador viejo es qué se compra con ese
/// presupuesto. Antes eran solo números; ahora una parte puede irse en una
/// <b>habilidad</b>, y ahí está la gracia: dos armas del mismo presupuesto pueden
/// ser una con números altos y ninguna habilidad, u otra floja en números con
/// algo que cambia cómo juega el defensor.</para>
/// </remarks>
public static class WeaponGenerator
{
    /// <summary>
    /// Genera un arma con ese presupuesto.
    /// </summary>
    /// <param name="presupuesto">Sube con la oleada: un arma tardía es mejor.</param>
    /// <param name="clases">Las del roster. El arma va a servir para alguna.</param>
    /// <param name="tabla">
    /// Los parámetros del generador, de <c>generador-armas.json</c>. Entran por
    /// parámetro y no de una constante porque son entrada de la simulación: el
    /// servidor arma la misma arma con la misma seed al revalidar.
    /// </param>
    public static Weapon Generate(
        int presupuesto, IReadOnlyList<DefenderClass> clases, Rng rng, int idNuevo, WeaponTable tabla)
    {
        var rareza = RollRarity(presupuesto, rng, tabla);

        // ⚠ La rareza se decide ANTES de gastar, y de ella sale cuánto se va en
        // habilidad. Al revés —gastar en números y ver qué sobra— las legendarias
        // saldrían siempre con números flojos, porque la habilidad se comería el
        // resto.
        int paraHabilidad = presupuesto * tabla.AbilityPercent(rareza) / 100;

        var habilidad = WeaponAbility.None;
        int magnitud = 0;

        if (paraHabilidad > 0)
        {
            habilidad = RollAbility(rareza, rng, tabla);
            magnitud = tabla.MagnitudeOf(habilidad, paraHabilidad);
        }

        int restante = presupuesto - paraHabilidad;
        int dano = 0, cadencia = 0, alcance = 0;

        int paso = tabla.Step;
        int costoDano = tabla.DamageCost * paso;
        int costoCadencia = tabla.CadenceCost * paso;
        int costoAlcance = tabla.RangeCost * paso;

        while (restante >= costoDano)
        {
            int cual = rng.Between(0, 3);

            if (cual == 0)
            {
                dano += paso;
                restante -= costoDano;
            }
            else if (cual == 1 && restante >= costoCadencia)
            {
                cadencia += paso;
                restante -= costoCadencia;
            }
            else if (restante >= costoAlcance)
            {
                alcance += paso;
                restante -= costoAlcance;
            }
            else
            {
                // Lo que queda no alcanza para el afijo que salió. Se corta en vez
                // de reintentar: reintentar hasta gastar el último punto haría que
                // todas las armas terminaran con el afijo más barato.
                break;
            }
        }

        // La clase sale del roster, así que el arma siempre le sirve a alguien. Un
        // arma para una clase que el jugador no tiene es un lugar de mochila
        // tirado a la basura.
        DefenderClass[] paraQuien = clases.Count == 0
            ? []
            : [clases[rng.Index(clases.Count)]];

        return new Weapon
        {
            Id = idNuevo,
            BaseKind = KindOf(dano, cadencia, alcance),
            Rarity = rareza,
            GraphicId = 0,
            Damage = dano,
            Cadence = cadencia,
            Range = alcance,
            Ability = habilidad,
            Magnitude = magnitud,
            Classes = paraQuien,
            Value = presupuesto / tabla.ValueDivisor * (1 + (int)rareza),
        };
    }

    /// <summary>
    /// Tira la rareza.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Las tiradas están <b>encadenadas</b>, no son exclusivas: primero
    /// legendaria, si falla épica, si falla rara. La probabilidad efectiva de las
    /// de abajo es menor que su número.</para>
    ///
    /// <para>La chance de legendaria sube con el presupuesto, o sea con la oleada.
    /// Es a propósito: en la oleada 10 una legendaria arruinaría el resto de la
    /// corrida, y en la 90 es el premio por haber llegado. Aun así tiene techo:
    /// una legendaria tiene que seguir siendo un hallazgo. Si saliera seguido,
    /// dejaría de romper el juego y pasaría a ser el juego.</para>
    /// </remarks>
    private static WeaponRarity RollRarity(int presupuesto, Rng rng, WeaponTable tabla)
    {
        if (rng.Happens(tabla.LegendaryChance.En(presupuesto))) return WeaponRarity.Legendary;
        if (rng.Happens(tabla.EpicChance.En(presupuesto))) return WeaponRarity.Epic;
        if (rng.Happens(tabla.RareChance.En(presupuesto))) return WeaponRarity.Rare;

        return WeaponRarity.Common;
    }

    /// <summary>
    /// Elige la habilidad según la rareza.
    /// </summary>
    /// <remarks>
    /// Las que rompen el juego <b>solo salen en legendarias</b>. Es lo que las
    /// hace valer: si <c>Cadena</c> pudiera aparecer en una rara, el jugador la
    /// tendría en la oleada 15 y el resto de la corrida sería una formalidad.
    /// </remarks>
    public static WeaponAbility RollAbility(WeaponRarity rareza, Rng rng, WeaponTable tabla)
    {
        var pool = tabla.PoolOf(rareza);
        return pool[rng.Index(pool.Length)];
    }

    /// <summary>
    /// El nombre, armado de lo que el arma realmente tiene.
    /// </summary>
    /// <remarks>
    /// Sale de los números y no al revés, así que un arma generada nunca promete
    /// algo que no hace. Los nombres de las habilidades salen de los 41 hechizos
    /// reales de <c>Hechizos.dat</c>.
    /// </remarks>
    private static WeaponBaseKind KindOf(int dano, int cadencia, int alcance)
        => dano >= cadencia && dano >= alcance ? WeaponBaseKind.Damage
         : cadencia >= alcance ? WeaponBaseKind.Cadence
         : WeaponBaseKind.Range;
}
