namespace TowerOfArgentum.Simulation;

/// <summary>
/// Qué se está ofreciendo en la pantalla de elección.
/// </summary>
/// <remarks>
/// ⚠ <b>Solo queda <see cref="Perk"/>.</b> <c>Hire</c> y <c>LevelUp</c> abrían una
/// pantalla de tres opciones donde se tomaba UNA y las otras dos desaparecían,
/// pegada al lado de una tienda de cuatro donde solo se consumía la comprada. Dos
/// reglas opuestas en la misma pantalla: para el jugador era una sola tienda, así
/// que comprar un nivel y ver evaporarse otras dos ofertas se leía como un bug.
/// Ahora son <c>ShopKind.Class</c> y <c>ShopKind.Level</c>.
///
/// Los dos valores <b>se conservan</b> y no se reciclan: son el formato de
/// almacenamiento del log, y un log viejo que traiga un <c>Choose</c> apuntando a
/// una de aquellas tiene que poder leerse sin que el número signifique otra cosa.
/// </remarks>
public enum ChoiceType
{
    /// <summary>⚠ Sin uso desde que la elección se fusionó con la tienda.</summary>
    Hire = 0,

    /// <summary>⚠ Sin uso desde que la elección se fusionó con la tienda.</summary>
    LevelUp = 1,

    /// <summary>
    /// Quedarse con uno de los perks de Oro que ofrece un jefe.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>El jefe ofrece varios y el jugador elige uno</b> (Drops_v2). Sigue
    /// saliendo un solo Oro por jefe: lo que cambia no es cuánto poder entrega,
    /// es que el jugador decide cuál. Con un Oro al azar y ocho jefes por
    /// corrida, ver los <b>dos</b> componentes de una receta era cuestión de
    /// suerte, y los Únicos eran una lotería en vez de un objetivo de build.
    /// </remarks>
    Perk = 2,
}

/// <summary>Una de las opciones que se ofrecen.</summary>
/// <param name="DefenderClass">
/// En una oferta de perk, una clase del roster que lo puede equipar. No decide
/// nada —el perk va a la mochila y el jugador lo equipa donde quiera—: está para
/// que la pantalla pueda decir a quién le sirve.
/// </param>
/// <param name="PerkId">El perk ofrecido, o <c>-1</c> si la opción no es de perk.</param>
/// <param name="Price">
/// Cuánto cuesta quedarse con esta opción. <c>0</c> = gratis.
/// </param>
/// <remarks>
/// ⚠ <b>El precio va en la opción y no se recalcula al elegir.</b> Lo fija
/// <see cref="ChoiceBuilder"/> cuando arma la pantalla, y el servidor lo vuelve a
/// derivar de la seed al revalidar. Recalcularlo en <c>Choose</c> lo ataría a la
/// oleada del momento en que el jugador aprieta, y como la elección no tiene
/// límite de tiempo, esperar cambiaría el precio de algo que ya estaba ofrecido.
/// </remarks>
public readonly record struct Choice(
    ChoiceType Kind,
    DefenderClass DefenderClass,
    int ResultingLevel,
    int PerkId = -1,
    int Price = 0);

/// <summary>
/// El universo de clases del juego.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Antes armaba la pantalla de elección</b> —tres opciones, se tomaba
/// una, las otras dos desaparecían— y de eso ya no queda nada: contratar y subir
/// de nivel son ofertas de la tienda (<c>ShopKind.Class</c> y
/// <c>ShopKind.Level</c>). Lo único que sobrevivió es esta lista, que la usan la
/// tienda, los drops y las pantallas.</para>
///
/// <para>Se conserva el nombre a propósito: está escrito en una docena de
/// archivos y renombrarlo no gana una línea de código.</para>
///
/// <para>⚠ <b>Las trabajadoras quedan afuera</b> —leñador, minero, pescador,
/// herrero, carpintero, sastre—. Este juego es pelear, y ellas no pelean.</para>
/// </remarks>
public static class ChoiceBuilder
{
    /// <summary>Las 9 de combate. Es todo el universo de clases del juego.</summary>
    public static readonly DefenderClass[] All =
    [
        DefenderClass.Mage, DefenderClass.Cleric, DefenderClass.Warrior, DefenderClass.Assassin, DefenderClass.Thief,
        DefenderClass.Bard, DefenderClass.Druid, DefenderClass.Paladin, DefenderClass.Hunter,
    ];
}
