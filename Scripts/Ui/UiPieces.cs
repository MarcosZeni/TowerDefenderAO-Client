using System.Collections.Generic;
using System.Text.Json;
using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum.Ui;

/// <summary>
/// Qué pieza de interfaz dibujar. El nombre es el archivo.
/// </summary>
/// <remarks>
/// Los valores coinciden con los nombres que escribe <c>--recortar-ui</c> en
/// <c>pieces.json</c>, pasados a <c>snake_case</c> por
/// <see cref="UiPieces.NombreDe"/>. Si se agrega una pieza a la hoja, va acá y
/// al array <c>NombresEsperados</c> del recortador, en el mismo orden.
/// </remarks>
public enum UiPiece
{
    Panel,
    PanelElevated,

    Button,
    ButtonHover,
    ButtonPressed,
    ButtonDisabled,

    TabActive,
    TabInactive,

    Slot,
    SlotCommon,
    SlotUncommon,
    SlotRare,
    SlotEpic,
    SlotLegendary,
    SlotUnique,

    BarRail,
    BarFill,
    BarCap,

    Corner,
    Rivet,
    Divider,
    Chevron,
    Coin,
}

/// <summary>
/// Las texturas de la interfaz y sus márgenes de 9-slice.
/// </summary>
/// <remarks>
/// <para>Las piezas las corta <c>--recortar-ui</c> de una hoja generada, y viven
/// en <c>Resources\ui\pieces\</c>. Se cargan por <see cref="ArtPack"/> —bytes
/// crudos decodificados en memoria— como todo el resto del arte, así que
/// funcionan igual con la carpeta suelta del editor y con el <c>arte.pck</c> del
/// teléfono.</para>
///
/// <para>⚠ <b>Si falta una pieza, no pasa nada.</b> <see cref="UiStyle"/> cae a
/// los rectángulos dibujados a mano de siempre. Es lo que permite probar dos
/// piezas sin tener las veintitrés, y lo que evita que un archivo mal copiado
/// deje el juego sin interfaz.</para>
///
/// <para>⚠ <b>El color sigue mandándolo <see cref="Palette"/>.</b> Las texturas
/// se dibujan con <c>modulate</c>, así que los seis marcos de rareza podrían
/// salir de una sola textura teñida. Hoy son seis archivos porque el metal de
/// cada uno tiene su propio brillo, pero la puerta queda abierta.</para>
/// </remarks>
public static class UiPieces
{
    private const string Carpeta = "/ui/pieces";

    private static readonly Dictionary<UiPiece, Texture2D?> _texturas = [];
    private static readonly Dictionary<string, int> _margenes = [];

    private static bool _indiceLeido;

    /// <summary>Cuántas piezas se pudieron cargar. 0 = interfaz dibujada a mano.</summary>
    public static int Loaded { get; private set; }

    /// <summary>Si hay al menos una pieza. Para el diagnóstico del arranque.</summary>
    public static bool Any => Loaded > 0;

    /// <summary>Olvida todo. La usan las herramientas offline.</summary>
    public static void Forget()
    {
        _texturas.Clear();
        _margenes.Clear();
        _indiceLeido = false;
        Loaded = 0;
    }

    /// <summary>
    /// La textura de una pieza, o <c>null</c> si no está.
    /// </summary>
    /// <remarks>
    /// El <c>null</c> también se cachea: sin eso, una pieza que falta se
    /// intentaría abrir del disco en cada frame.
    /// </remarks>
    public static Texture2D? Texture(UiPiece cual)
    {
        if (_texturas.TryGetValue(cual, out var lista))
            return lista;

        string ruta = $"{ArtPack.Root}{Carpeta}/{NombreDe(cual)}.png";
        var imagen = ArtPack.LoadImage(ruta);

        Texture2D? textura = imagen is null ? null : ImageTexture.CreateFromImage(imagen);

        _texturas[cual] = textura;

        if (textura is not null)
            Loaded++;

        return textura;
    }

    /// <summary>
    /// El margen de 9-slice de una pieza, en píxeles de la textura.
    /// </summary>
    /// <remarks>
    /// Sale de <c>pieces.json</c>, que lo escribe el recortador. Si el archivo no
    /// está, se cae a un octavo del lado más corto: no es el número bueno, pero
    /// deja la pieza dibujable mientras se arregla el índice.
    /// </remarks>
    public static int Margin(UiPiece cual, Texture2D textura)
    {
        LeerIndice();

        if (_margenes.TryGetValue(NombreDe(cual), out int m))
            return m;

        return Mathf.Max(2, (int)Mathf.Min(textura.GetWidth(), textura.GetHeight()) / 8);
    }

    /// <summary>
    /// Dibuja una pieza estirada al rectángulo, respetando su 9-slice.
    /// </summary>
    /// <remarks>
    /// <para>Va por <see cref="RenderingServer.CanvasItemAddNinePatch"/> y no por
    /// <c>NinePatchRect</c>: la interfaz de este juego se dibuja en modo
    /// inmediato adentro de <c>_Draw</c>, sin nodos por widget. Un
    /// <c>NinePatchRect</c> exigiría crear y mantener un nodo por panel, que es
    /// exactamente la arquitectura que el proyecto decidió no tener.</para>
    ///
    /// <para>Devuelve <c>false</c> si la pieza no está, para que el que llama
    /// dibuje su versión a mano.</para>
    /// </remarks>
    public static bool Piece(this CanvasItem ci, UiPiece cual, Rect2 r, Color? tinte = null)
    {
        if (Texture(cual) is not { } textura)
            return false;

        int m = Margin(cual, textura);

        // El margen no puede pasarse de la mitad del rectángulo de DESTINO: si se
        // pasa, las cuatro esquinas se superponen y la pieza sale deformada. Un
        // panel chico con una textura de margen 40 es el caso normal, no el raro.
        float tope = Mathf.Min(r.Size.X, r.Size.Y) / 2f;
        float margen = Mathf.Min(m, Mathf.Max(1f, tope - 1f));

        var esquina = new Vector2(margen, margen);

        RenderingServer.CanvasItemAddNinePatch(
            ci.GetCanvasItem(),
            r,
            new Rect2(0, 0, textura.GetWidth(), textura.GetHeight()),
            textura.GetRid(),
            esquina,
            esquina,
            RenderingServer.NinePatchAxisMode.Stretch,
            RenderingServer.NinePatchAxisMode.Stretch,
            drawCenter: true,
            tinte ?? Colors.White);

        return true;
    }

    /// <summary>
    /// Dibuja una pieza sin estirar, centrada en el rectángulo.
    /// </summary>
    /// <remarks>
    /// Para las que no tienen centro que estirar: el remache, la moneda, la
    /// escuadra de esquina, el chevrón. Estirar una moneda la vuelve un óvalo.
    /// </remarks>
    public static bool PieceFixed(this CanvasItem ci, UiPiece cual, Rect2 r, Color? tinte = null)
    {
        if (Texture(cual) is not { } textura)
            return false;

        float lado = Mathf.Min(r.Size.X, r.Size.Y);

        var destino = new Rect2(
            r.Position.X + ((r.Size.X - lado) / 2f),
            r.Position.Y + ((r.Size.Y - lado) / 2f),
            lado, lado);

        ci.DrawTextureRect(textura, destino, tile: false, tinte ?? Colors.White);
        return true;
    }

    /// <summary>
    /// Qué marco de hueco le toca a un color de rareza.
    /// </summary>
    /// <remarks>
    /// <para>Se compara contra los tokens de <see cref="Palette"/> en vez de
    /// recibir un <c>PerkTier</c> porque <see cref="UiStyle.Slot"/> recibe un
    /// color: lo llaman la mochila, que sí sabe la rareza, pero también la tienda
    /// y el panel del defensor, que ya la resolvieron a color.</para>
    ///
    /// <para>Un color que no es ninguno de los seis cae en <see cref="UiPiece.Slot"/>,
    /// el hueco neutro. Es lo correcto: un marco de rareza equivocado miente
    /// sobre el valor de un ítem, y en una grilla de quince el jugador lee el
    /// color antes que el nombre.</para>
    /// </remarks>
    public static UiPiece SlotFor(Color rareza)
    {
        if (Igual(rareza, Palette.Unique)) return UiPiece.SlotUnique;
        if (Igual(rareza, Palette.Legendary)) return UiPiece.SlotLegendary;
        if (Igual(rareza, Palette.Epic)) return UiPiece.SlotEpic;
        if (Igual(rareza, Palette.Rare)) return UiPiece.SlotRare;
        if (Igual(rareza, Palette.Uncommon)) return UiPiece.SlotUncommon;
        if (Igual(rareza, Palette.Common)) return UiPiece.SlotCommon;

        return UiPiece.Slot;

        // Sin alfa: el que llama a veces pasa el color con transparencia.
        static bool Igual(Color a, Color b)
            => Mathf.IsEqualApprox(a.R, b.R)
            && Mathf.IsEqualApprox(a.G, b.G)
            && Mathf.IsEqualApprox(a.B, b.B);
    }

    /// <summary>
    /// <c>SlotEpic</c> → <c>"slot_epic"</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ Se deriva del nombre del valor del enum y no de una tabla, y es la única
    /// derivación de nombre de archivo que hay en el proyecto. Se banca porque
    /// las dos puntas —el enum y el array del recortador— están en el mismo
    /// repo y una falta se ve al arrancar: la pieza no carga y la interfaz cae a
    /// los rectángulos, que es visible de inmediato.
    /// </remarks>
    private static string NombreDe(UiPiece cual)
    {
        string nombre = cual.ToString();
        var sb = new System.Text.StringBuilder(nombre.Length + 4);

        for (int i = 0; i < nombre.Length; i++)
        {
            if (i > 0 && char.IsUpper(nombre[i]))
                sb.Append('_');

            sb.Append(char.ToLowerInvariant(nombre[i]));
        }

        return sb.ToString();
    }

    private static void LeerIndice()
    {
        if (_indiceLeido)
            return;

        _indiceLeido = true;

        string ruta = $"{ArtPack.Root}{Carpeta}/pieces.json";

        using var f = ArchivoGodot.Open(ruta, ArchivoGodot.ModeFlags.Read);

        if (f is null)
        {
            GD.PushWarning($"[ui] no está {ruta}: los márgenes de 9-slice salen estimados.");
            return;
        }

        try
        {
            var opciones = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            using var doc = JsonDocument.Parse(f.GetAsText(), opciones);

            if (!doc.RootElement.TryGetProperty("piezas", out var piezas))
                return;

            foreach (var p in piezas.EnumerateObject())
            {
                if (p.Value.TryGetProperty("margen", out var m) && m.TryGetInt32(out int valor))
                    _margenes[p.Name] = valor;
            }
        }
        catch (System.Exception e)
        {
            GD.PushError($"[ui] pieces.json no se pudo leer: {e.Message}");
        }
    }
}
