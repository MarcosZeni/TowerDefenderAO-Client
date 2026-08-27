namespace TowerOfArgentum.Simulacion;

/// <summary>Qué se está ofreciendo en la pantalla de elección.</summary>
public enum ChoiceType
{
    /// <summary>Sumar una clase nueva al roster, en nivel 1.</summary>
    Hire = 0,

    /// <summary>Subir de nivel una que ya está: stats y <b>un slot más</b>.</summary>
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
public readonly record struct Choice(
    ChoiceType Kind,
    DefenderClass DefenderClass,
    int ResultingLevel,
    int PerkId = -1);

/// <summary>
/// Arma las 3 opciones de la pantalla de elección.
/// </summary>
/// <remarks>
/// <para>Las clases <b>tocan al azar</b>: 1 de 3 al empezar, y después, cada
/// <c>oleadasEntreElecciones</c> oleadas (<c>curvas.json</c>), elegir entre levelear
/// una que ya se tiene o contratar otra.</para>
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

    /// <summary>
    /// Las 3 opciones del arranque: siempre contratar, porque el roster está
    /// vacío.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Al menos una de las tres es ofensiva, garantizado.</b> No es
    /// una concesión: con el reparto libre, un arranque de clérigo + bardo +
    /// druida es una corrida perdida en la oleada 1 sin que el jugador haya hecho
    /// nada mal. Medido con el corredor headless: dos de diez seeds terminaban
    /// así.</para>
    ///
    /// <para>Perder por una decisión propia es el juego; perder por el sorteo de
    /// la pantalla inicial, antes de tocar nada, es un bug de diseño.</para>
    /// </remarks>
    public static Choice[] Initial(Rng rng, BalanceData datos)
    {
        var salida = new Choice[3];

        // ⚠ Las ofensivas —las que no están marcadas «soporte» en clases.json—
        // son las que pegan lo suficiente para sostener solas una defensa.
        // Clérigo, bardo y druida pegan poco a propósito: su valor está en lo que
        // le hacen al resto del equipo. Hasta que exista el sistema de soporte no
        // tienen con qué compensar, pero incluso después van a seguir siendo malas
        // SOLAS, que es la situación del arranque.
        var ofensivas = datos.Classes.Offensive();

        // La garantizada sale primero, para que el resto se sortee sin ella.
        var garantizada = ofensivas[rng.Index(ofensivas.Length)];
        salida[0] = new Choice(ChoiceType.Hire, garantizada, 1);

        var pozo = new List<DefenderClass>(All);
        pozo.Remove(garantizada);

        for (int i = 1; i < 3; i++)
        {
            int j = rng.Index(pozo.Count);
            salida[i] = new Choice(ChoiceType.Hire, pozo[j], 1);

            // Se saca del pozo para que no salgan dos veces la misma clase: tres
            // opciones donde dos son iguales es una elección de dos.
            pozo.RemoveAt(j);
        }

        // Se mezclan para que la ofensiva no quede siempre en la primera
        // posición: si estuviera fija, la elección se volvería un reflejo y la
        // garantía sería visible como "la de arriba es la buena".
        for (int i = salida.Length - 1; i > 0; i--)
        {
            int j = rng.Between(0, i + 1);
            (salida[i], salida[j]) = (salida[j], salida[i]);
        }

        return salida;
    }

    /// <summary>
    /// Las 3 opciones de una elección de mitad de corrida.
    /// </summary>
    /// <remarks>
    /// <para>Se mezclan las dos cosas a propósito, y el jugador ve las dos en la
    /// misma pantalla: esa es la decisión "ancho o profundo". Las dos dan
    /// <b>+1 slot</b>, así que se diferencian en lo demás — contratar trae un rol
    /// nuevo pero arranca en cero; levelear engorda algo que ya funciona.</para>
    ///
    /// <para>Una clase que ya llegó al nivel 6 no vuelve a ofrecerse para
    /// levelear: del 7 en adelante ya no abre slots, así que sería una opción
    /// visiblemente peor que las otras dos y elegir dejaría de tener sentido.</para>
    /// </remarks>
    public static Choice[] Next(Roster roster, Rng rng, BalanceData datos)
    {
        var candidatas = new List<Choice>();

        foreach (var c in roster.Classes)
        {
            if (c.Level < datos.Classes.PerkSlotsPerClass)
                candidatas.Add(new Choice(ChoiceType.LevelUp, c.DefenderClass, c.Level + 1));
        }

        foreach (var clase in All)
        {
            if (!roster.Has(clase))
                candidatas.Add(new Choice(ChoiceType.Hire, clase, 1));
        }

        // Con el roster completo y todo en nivel 6 no queda nada que ofrecer. Es
        // un final de progresión legítimo, no un error: el jugador ya tiene todo
        // lo que el sistema puede darle, y de ahí en más el poder viene de los
        // drops.
        if (candidatas.Count == 0)
            return [];

        int cuantas = System.Math.Min(3, candidatas.Count);
        var salida = new Choice[cuantas];

        for (int i = 0; i < cuantas; i++)
        {
            int j = rng.Index(candidatas.Count);
            salida[i] = candidatas[j];
            candidatas.RemoveAt(j);
        }

        return salida;
    }
}
