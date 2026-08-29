using System.Collections.Generic;
using System.Text.Json;
using Godot;
using TowerOfArgentum.Simulation;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>Con qué se dibuja una clase en un tier: cuerpo, arma y cabeza.</summary>
/// <remarks>
/// <see cref="HeadOffset"/> va en <b>píxeles x4</b>, igual que todo lo que sale
/// del índice de gráficos. Quien dibuja lo escala junto con el sprite.
/// </remarks>
public sealed record Appearance(string Body, string Weapon, string Head, Vector2I HeadOffset);

/// <summary>
/// Lee <c>Data\apariencia.json</c>: clase + tier → claves de gráfico.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Las claves vienen del archivo, no se arman acá.</b> Las escribe
/// <c>--importar-apariencia</c> con la misma función que usa el traedor de arte.
/// Derivarlas en dos lugares es lo que dejó el índice apuntando a un bicho que ya
/// se llamaba de otra forma.</para>
///
/// <para>Este archivo <b>no</b> es entrada de la simulación: no entra en el hash
/// de <c>/run/start</c> y cambiarlo no invalida ninguna corrida. Es apariencia y
/// nada más — la armadura no da stats.</para>
/// </remarks>
public static class AppearanceLoader
{
    private const string Route = "res://Data/apariencia.json";

    private static readonly Dictionary<string, ClaseJson> _porClase =
        new(System.StringComparer.OrdinalIgnoreCase);

    private static bool _cargado;

    /// <summary>
    /// Cómo se ve una clase en un tier.
    /// </summary>
    /// <param name="tier">1, 2 o 3. Sale del nivel de la clase, no de un ítem.</param>
    /// <returns><c>null</c> si falta el archivo o esa clase no está.</returns>
    public static Appearance? De(DefenderClass clase, int tier)
    {
        Load();

        if (!_porClase.TryGetValue(clase.ToString(), out var c) || c.Tiers is null)
            return null;

        // El tier llega como 1..3 y el array es 0..2. Se clava adentro en vez de
        // devolver null: un nivel fuera de rango es un bug de otro lado, y dejar
        // al defensor sin dibujar lo escondería.
        int i = System.Math.Clamp(tier, 1, c.Tiers.Count) - 1;

        if (i < 0 || i >= c.Tiers.Count)
            return null;

        var t = c.Tiers[i];

        return new Appearance(
            t.Body ?? "",
            t.Weapon ?? "",
            c.Head ?? "",
            new Vector2I(t.HeadOffsetX, t.HeadOffsetY));
    }

    private static void Load()
    {
        if (_cargado)
            return;

        _cargado = true;

        using var f = ArchivoGodot.Open(Route, ArchivoGodot.ModeFlags.Read);

        if (f is null)
        {
            GD.PushWarning($"No se pudo leer {Route}. Los defensores se dibujan con primitivas.");
            return;
        }

        ArchivoJson? archivo;

        try
        {
            archivo = JsonSerializer.Deserialize<ArchivoJson>(
                f.GetAsText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException e)
        {
            GD.PushError($"{Route} no es JSON válido: {e.Message}");
            return;
        }

        if (archivo?.Classes is null)
            return;

        foreach (var c in archivo.Classes)
        {
            if (c.Class is { Length: > 0 })
                _porClase[c.Class] = c;
        }

        GD.Print($"Appearance: {_porClase.Count} clases");
    }

    private sealed class ArchivoJson
    {
        public int Version { get; set; }
        public List<ClaseJson>? Classes { get; set; }
    }

    private sealed class ClaseJson
    {
        public string? Class { get; set; }
        public string? Head { get; set; }
        public List<TierJson>? Tiers { get; set; }
    }

    private sealed class TierJson
    {
        public string? Body { get; set; }
        public string? Weapon { get; set; }

        /// <summary>Dónde va la cabeza respecto del apoyo del cuerpo, en píxeles x4.</summary>
        public int HeadOffsetX { get; set; }

        public int HeadOffsetY { get; set; }
    }
}
