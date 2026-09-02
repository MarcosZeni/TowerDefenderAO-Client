using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// Encuentra el arte y lo monta sobre <c>res://</c>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>El arte NO está adentro del proyecto Godot.</b> <c>Resources\</c> es
/// hermana de <c>Client\</c>: así Godot no la importa, no genera <c>.import</c>,
/// el <c>.pck</c> principal queda chico y el arte se parchea aparte. En el editor
/// eso alcanza, porque <c>res://../Resources</c> es una carpeta real del disco.</para>
///
/// <para><b>En una build exportada no alcanza</b>, y en Android es directamente
/// imposible: no hay «carpeta al lado del ejecutable». Por eso el arte se empaqueta
/// en un <c>arte.pck</c> aparte —lo arma <c>--empaquetar-arte</c>— y se monta acá
/// con <see cref="ProjectSettings.LoadResourcePack"/>, que lo fusiona sobre
/// <c>res://</c>. Montado el paquete, las hojas se leen como cualquier archivo del
/// proyecto.</para>
///
/// <para>⚠ <b>Lo que NO se puede usar es <c>Image.LoadFromFile</c>.</b> Esa función
/// pide una ruta del sistema de archivos, y adentro de un <c>.pck</c> no hay ruta
/// del sistema de archivos. Por eso <see cref="LoadImage"/> lee los bytes y
/// decodifica en memoria — funciona igual con la carpeta suelta del editor y con el
/// paquete del teléfono, que es lo que permite que el resto del código no se entere
/// de dónde salió el PNG.</para>
///
/// <para>Se llama una vez, al principio de <c>Main._Ready</c>, antes de todo lo que
/// dibuja y antes de las herramientas headless.</para>
/// </remarks>
public static class ArtPack
{
    /// <summary>Nombre del paquete de arte, igual en todas las plataformas.</summary>
    public const string PackName = "arte.pck";

    /// <summary>
    /// Dónde quedan los archivos del paquete una vez montado.
    /// </summary>
    /// <remarks>
    /// Es la ruta virtual que escribe <c>--empaquetar-arte</c> adentro del
    /// <c>.pck</c>. No es una carpeta del proyecto: no existe hasta que el paquete
    /// se monta.
    /// </remarks>
    public const string MountPoint = "res://Art";

    /// <summary>La carpeta suelta del editor, hermana de <c>Client\</c>.</summary>
    private const string CarpetaSuelta = "res://../Resources";

    private static bool _montado;

    /// <summary>
    /// Dónde está el arte: la carpeta suelta o el punto de montaje del paquete.
    /// </summary>
    /// <remarks>
    /// Lo leen <see cref="GraphicsLoader"/> y <see cref="IconLoader"/>. Vale
    /// <see cref="CarpetaSuelta"/> hasta que <see cref="Mount"/> encuentre un
    /// paquete, así que llamarlo tarde no rompe nada: solo haría que lo primero en
    /// dibujarse saliera sin arte.
    /// </remarks>
    public static string Root { get; private set; } = CarpetaSuelta;

    /// <summary>De dónde salió el arte. Para el diagnóstico del arranque.</summary>
    public static string Source { get; private set; } = "carpeta suelta";

    /// <summary>
    /// Busca el arte y lo monta. Idempotente.
    /// </summary>
    /// <remarks>
    /// <para>El orden de los candidatos es el orden de preferencia, y cada uno está
    /// por una plataforma:</para>
    ///
    /// <list type="number">
    /// <item><b>La carpeta suelta</b>, y solo en el editor: es el ciclo de trabajo
    /// diario. Traer una hoja con <c>--traer-bestiario</c> y verla en el juego no
    /// puede exigir reempaquetar 56 MB.</item>
    /// <item><b><c>arte.pck</c> al lado del ejecutable</b>: la build de escritorio.
    /// Parchear el arte es reemplazar ese archivo, sin tocar el juego.</item>
    /// <item><b><c>res://arte.pck</c></b>: Android, donde el paquete de arte viaja
    /// adentro del <c>.pck</c> principal del APK.</item>
    /// <item><b><c>user://arte.pck</c></b>: red de contención. Si el motor no puede
    /// montar un <c>.pck</c> anidado adentro de otro, se lo copia una vez al
    /// almacenamiento privado de la app y se monta desde ahí. Cuesta otros 56 MB
    /// del teléfono, y por eso es el último y no el primero.</item>
    /// </list>
    /// </remarks>
    public static void Mount()
    {
        if (_montado)
            return;

        _montado = true;

        // 1. En el editor manda la carpeta suelta, aunque haya un arte.pck viejo
        //    dando vueltas al lado: la fuente de verdad mientras se desarrolla es
        //    Resources\, no una foto de Resources\ de hace tres días.
        if (OS.HasFeature("editor") && HayCarpetaSuelta())
        {
            Anunciar("carpeta suelta (editor)");
            return;
        }

        // 2. Al lado del ejecutable.
        if (Montar(OS.GetExecutablePath().GetBaseDir().PathJoin(PackName), "al lado del ejecutable"))
            return;

        // 3. Adentro del paquete principal.
        string interno = $"res://{PackName}";

        if (Montar(interno, "adentro del paquete principal"))
            return;

        // 4. La copia en user://: la de una corrida anterior, o una recién sacada.
        string enUser = $"user://{PackName}";

        if (ArchivoGodot.FileExists(enUser) && Montar(enUser, "copia en user://"))
            return;

        if (Extraer(interno, enUser) && Montar(enUser, "copia recién extraída a user://"))
            return;

        // 5. La carpeta suelta fuera del editor: una build de escritorio corriendo
        //    desde el repo, sin empaquetar. Es lo que pasaba antes de todo esto.
        if (HayCarpetaSuelta())
        {
            Anunciar("carpeta suelta (sin empaquetar)");
            return;
        }

        GD.PushWarning(
            $"No se encontró el arte: ni {PackName} ni {CarpetaSuelta}. El juego dibuja " +
            "con primitivas. Armar el paquete con --empaquetar-arte.");
    }

    /// <summary>
    /// Un PNG del arte, decodificado en memoria.
    /// </summary>
    /// <remarks>
    /// ⚠ Acá NO va <c>Image.LoadFromFile</c>: pide una ruta del sistema de archivos
    /// y adentro de un <c>.pck</c> no hay tal cosa. <c>GetFileAsBytes</c> pasa por
    /// el sistema de archivos virtual de Godot, que es el único que sabe leer las
    /// dos formas.
    /// </remarks>
    /// <returns><c>null</c> si el archivo no está o no es un PNG legible.</returns>
    public static Image? LoadImage(string ruta)
    {
        if (!ArchivoGodot.FileExists(ruta))
            return null;

        byte[] datos = ArchivoGodot.GetFileAsBytes(ruta);

        if (datos.Length == 0)
            return null;

        var imagen = new Image();

        return imagen.LoadPngFromBuffer(datos) == Error.Ok ? imagen : null;
    }

    private static bool HayCarpetaSuelta() =>
        ArchivoGodot.FileExists($"{CarpetaSuelta}/graficos.json");

    private static bool Montar(string ruta, string origen)
    {
        if (!ArchivoGodot.FileExists(ruta))
            return false;

        // `replaceFiles: false` a propósito: el paquete de arte no tiene por qué
        // pisar nada del juego, y si algún día trae un archivo con el mismo nombre
        // que uno del proyecto, que gane el del proyecto.
        if (!ProjectSettings.LoadResourcePack(ruta, replaceFiles: false))
        {
            GD.PushWarning($"No se pudo montar {ruta}.");
            return false;
        }

        // Montar puede dar true y no traer lo que se esperaba —un pck armado con
        // otra ruta virtual adentro—. El índice es la prueba de que sirve.
        if (!ArchivoGodot.FileExists($"{MountPoint}/graficos.json"))
        {
            GD.PushWarning($"{ruta} se montó pero no tiene {MountPoint}/graficos.json.");
            return false;
        }

        Root = MountPoint;
        Anunciar(origen);

        return true;
    }

    /// <summary>Copia el paquete a <c>user://</c> para poder montarlo desde ahí.</summary>
    private static bool Extraer(string desde, string hacia)
    {
        if (!ArchivoGodot.FileExists(desde))
            return false;

        byte[] datos = ArchivoGodot.GetFileAsBytes(desde);

        if (datos.Length == 0)
            return false;

        using var destino = ArchivoGodot.Open(hacia, ArchivoGodot.ModeFlags.Write);

        if (destino is null)
        {
            GD.PushWarning($"No se pudo escribir {hacia}: {ArchivoGodot.GetOpenError()}");
            return false;
        }

        destino.StoreBuffer(datos);
        destino.Close();

        GD.Print($"Arte: {datos.Length / 1048576} MB extraídos a {hacia}");

        return true;
    }

    private static void Anunciar(string origen)
    {
        Source = origen;
        GD.Print($"Arte: {origen} → {Root}");
    }
}
