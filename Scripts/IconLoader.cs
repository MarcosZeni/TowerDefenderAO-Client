using System.Collections.Generic;
using System.Text.Json;
using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// Lee <c>Resources\perks\iconos.json</c> y resuelve <b>key de perk → textura</b>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>El mapeo no está en <c>perks.json</c> a propósito.</b> El hash de
/// <c>/run/start</c> cubre el texto crudo de los siete JSON de balance,
/// comentarios incluidos: agregarle un campo <c>icon</c> a <c>perks.json</c>
/// cambiaría el hash, y <b>eso es temporada nueva del ranking</b> por una decisión
/// puramente cosmética. El icono es presentación, no entrada de la simulación, así
/// que vive en <c>Resources\</c>, fuera del hash — igual que <c>Data\lang\</c> y
/// por la misma razón.</para>
///
/// <para>Se cargan los <b>recortes</b> de las subcarpetas, no la lámina entera.
/// Son 69 PNG de ~100 px contra una lámina de 1536×1024 y 2,4 MB, y la lámina es
/// el original de trabajo: si mañana se rehornean los iconos componiéndolos sobre
/// <c>fondos\&lt;rareza&gt;.png</c>, los recortes cambian y esta clase no se
/// entera.</para>
///
/// <para>⚠ <b>Un perk sin icono no es un error.</b> Devuelve <c>null</c> y el que
/// dibuja sigue con el texto de siempre: el icono es un agregado sobre una pantalla
/// que ya funcionaba sin él, y quedarse sin arte no puede romper la mochila.</para>
/// </remarks>
public static class IconLoader
{
    /// <summary>
    /// Dónde está el arte de iconos. Lo decide <see cref="ArtPack"/> al arrancar.
    /// </summary>
    /// <remarks>
    /// Mismo criterio que <see cref="GraphicsLoader"/>: la carpeta suelta en el
    /// editor, el <c>arte.pck</c> montado en una build exportada.
    /// </remarks>
    private static string Carpeta => $"{ArtPack.Root}/perks";

    /// <summary>key del perk → ruta relativa del recorte.</summary>
    private static readonly Dictionary<string, string> _porPerk = new(System.StringComparer.Ordinal);

    /// <summary>ruta relativa → textura ya cargada.</summary>
    private static readonly Dictionary<string, Texture2D> _texturas = new(System.StringComparer.Ordinal);

    private static bool _cargado;

    /// <summary>El icono de un perk, por su <c>key</c> estable.</summary>
    /// <returns><c>null</c> si ese perk no tiene icono o falta el archivo.</returns>
    public static Texture2D? Perk(string key)
    {
        Load();

        if (!_porPerk.TryGetValue(key, out string? archivo))
            return null;

        if (_texturas.TryGetValue(archivo, out var lista))
            return lista;

        string ruta = $"{Carpeta}/{archivo}";

        if (!ArchivoGodot.FileExists(ruta))
        {
            GD.PushWarning($"Falta el icono «{archivo}» en {Carpeta}");
            return null;
        }

        // ⚠ Por ArtPack, no por Image.LoadFromFile: en una build exportada esto
        // sale de adentro del arte.pck y no tiene ruta del sistema de archivos.
        var imagen = ArtPack.LoadImage(ruta);

        if (imagen is null)
            return null;

        var textura = ImageTexture.CreateFromImage(imagen);
        _texturas[archivo] = textura;

        return textura;
    }

    /// <summary>
    /// Dibuja el icono de un perk encajado en un cuadrado, con su relación de
    /// aspecto.
    /// </summary>
    /// <remarks>
    /// Los recortes <b>no son todos del mismo lado</b> —van de 63 a 110 px, porque
    /// el resplandor de un marco dorado ocupa más que el de uno gris—, así que
    /// dibujarlos crudos daría iconos de tamaños distintos en la misma fila. Acá se
    /// escalan al cuadrado que pide el que dibuja.
    /// </remarks>
    /// <returns><c>false</c> si no había icono: el que llama dibuja el texto solo.</returns>
    public static bool Draw(CanvasItem lienzo, string key, float x, float y, float lado)
    {
        var textura = Perk(key);

        if (textura is null)
            return false;

        lienzo.DrawTextureRect(textura, new Rect2(x, y, lado, lado), tile: false);
        return true;
    }

    private static void Load()
    {
        if (_cargado)
            return;

        _cargado = true;

        string ruta = $"{Carpeta}/iconos.json";

        using var f = ArchivoGodot.Open(ruta, ArchivoGodot.ModeFlags.Read);

        if (f is null)
        {
            GD.PushWarning($"No se pudo leer {ruta}. Los perks se dibujan sin icono.");
            return;
        }

        System.Text.Json.Nodes.JsonNode? json;

        try
        {
            // ⚠ El archivo lleva comentarios `//`, igual que los de Data\: el
            // porqué de cada match vive al lado del match.
            json = System.Text.Json.Nodes.JsonNode.Parse(
                f.GetAsText(),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
        }
        catch (JsonException e)
        {
            GD.PushError($"{ruta} no es JSON válido: {e.Message}");
            return;
        }

        if (json?["porPerk"] is not System.Text.Json.Nodes.JsonObject porPerk)
        {
            GD.PushWarning($"{ruta} no tiene el bloque «porPerk».");
            return;
        }

        foreach (var (key, entrada) in porPerk)
        {
            if (entrada?["archivo"]?.GetValue<string>() is { Length: > 0 } archivo)
                _porPerk[key] = archivo;
        }

        GD.Print($"Iconos de perk: {_porPerk.Count} entradas en el índice");
    }
}
