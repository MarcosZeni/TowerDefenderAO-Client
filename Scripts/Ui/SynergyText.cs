using System.Collections.Generic;
using System.Text;
using TowerOfArgentum.Simulation;

namespace TowerOfArgentum.Ui;

/// <summary>
/// Cómo se cuenta una sinergia en pantalla: su escalón, su cartel, qué falta.
/// </summary>
/// <remarks>
/// <para>Compartido entre el panorama del HUD y el desglose del panel del
/// defensor, para que los dos digan lo mismo con las mismas palabras. Si cada
/// pantalla armara su frase, «+25 % de daño» y «daño +25 %» convivirían en la
/// misma columna a dos clicks de distancia.</para>
///
/// <para>⚠ Acá no hay una sola palabra del jugador: todo sale de
/// <c>Data\lang\</c> por <see cref="Loc"/>, con <c>{m}</c> para la magnitud y
/// <c>{c}</c> para una clase o una lista de clases. Regla de oro 4.</para>
/// </remarks>
public static class SynergyText
{
    /// <summary>Lo que da un escalón: «+25 % de daño», «25 % de doble oro al matar».</summary>
    public static string Effect(SynergyTier t)
        => Loc.Get($"synergy.effect.{t.Effect}", t.Magnitude);

    /// <summary>
    /// Un escalón entero en un renglón: cuántas clases pide, qué da, a quién y
    /// con qué tope.
    /// </summary>
    public static string Tier(SynergyTier t)
    {
        var sb = new StringBuilder();

        sb.Append(Loc.Get("ui.sinergia.escalon", t.Need));
        sb.Append(": ");
        sb.Append(Effect(t));

        // El «solo el Ladrón» es la diferencia entre una sinergia y una
        // asimétrica, y sin decirlo el Bardo creería que también roba.
        if (t.Only.Length > 0)
        {
            sb.Append("  ·  ");
            sb.Append(Loc.Get("ui.sinergia.solo").Replace("{c}", Classes(t.Only), System.StringComparison.Ordinal));
        }

        if (t.Cap > 0)
        {
            sb.Append("  ·  ");
            sb.Append(Loc.Get("ui.sinergia.tope", t.Cap));
        }

        return sb.ToString();
    }

    /// <summary>
    /// El cartel completo de una sinergia, para el tooltip: nombre, qué junta,
    /// cada escalón con el activo marcado, y qué falta para el siguiente.
    /// </summary>
    /// <remarks>
    /// Va con saltos de línea porque <see cref="UiTooltip"/> parte por ellos:
    /// una sinergia son tres o cuatro cosas distintas y en un renglón único es
    /// una tira que hay que leer buscando los separadores.
    /// </remarks>
    public static string Tooltip(Synergy s, IReadOnlyList<int> unidadesPorClase)
    {
        int presentes = s.CountPresent(unidadesPorClase);
        var activo = s.TierFor(presentes);

        var sb = new StringBuilder();

        sb.Append(Loc.Get($"synergy.{s.Key}.name").ToUpperInvariant());
        sb.Append("  ·  ");
        sb.Append(Classes(s.Classes));

        if (s.MinUnitsPerClass > 1)
        {
            sb.Append("  ·  ");
            sb.Append(Loc.Get("ui.sinergia.minimo", s.MinUnitsPerClass));
        }

        sb.Append('\n');
        sb.Append(Loc.Get($"synergy.{s.Key}.desc"));

        foreach (var t in s.Tiers)
        {
            sb.Append('\n');
            sb.Append(ReferenceEquals(t, activo) ? "▶ " : "   ");
            sb.Append(Tier(t));
        }

        sb.Append('\n');
        sb.Append(Missing(s, presentes, unidadesPorClase));

        return sb.ToString();
    }

    /// <summary>
    /// «Tenés 2 de 3 · falta Asesino», o «completa», o «falta Clérigo (1/2)» cuando
    /// la clase está pero no llega al mínimo.
    /// </summary>
    public static string Missing(Synergy s, int presentes, IReadOnlyList<int> unidadesPorClase)
    {
        int maximo = 0;

        foreach (var t in s.Tiers)
            maximo = System.Math.Max(maximo, t.Need);

        if (presentes >= maximo)
            return Loc.Get("ui.sinergia.completa");

        var faltan = new List<string>();

        foreach (var c in s.Classes)
        {
            int i = (int)c;
            int puestas = i >= 0 && i < unidadesPorClase.Count ? unidadesPorClase[i] : 0;

            if (puestas >= s.MinUnitsPerClass)
                continue;

            // Con mínimo 2, «falta Clérigo (1/2)» dice que no es reclutarlo sino
            // poner otro: es la única forma de que el mínimo se vea.
            faltan.Add(puestas > 0 && s.MinUnitsPerClass > 1
                ? $"{Loc.Class(c)} ({puestas}/{s.MinUnitsPerClass})"
                : Loc.Class(c));
        }

        return Loc.Get("ui.sinergia.tenes", presentes).Replace("{n}", maximo.ToString(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal)
               + "  ·  "
               + Loc.Get("ui.sinergia.falta").Replace("{c}", string.Join(", ", faltan), System.StringComparison.Ordinal);
    }

    /// <summary>«Guerrero, Paladín, Asesino».</summary>
    public static string Classes(IReadOnlyList<DefenderClass> clases)
    {
        var nombres = new List<string>(clases.Count);

        foreach (var c in clases)
            nombres.Add(Loc.Class(c));

        return string.Join(", ", nombres);
    }
}
