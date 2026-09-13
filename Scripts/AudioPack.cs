using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// Encuentra el audio y lo monta: la carpeta del proyecto o <c>audio.pck</c>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Existe por una razón distinta que <see cref="ArtPack"/>, y conviene
/// no confundirlas.</b> El arte tiene que viajar CRUDO sí o sí: el juego hornea el
/// piso pegando cientos de tiles con <c>Image.BlitRect</c> y eso necesita píxeles
/// RGBA, no bloques comprimidos para la GPU. El audio no tiene ese problema —un
/// OGG se reproduce igual esté donde esté—, así que acá el paquete se hace por
/// <b>tamaño del ejecutable y por poder parchear</b>: cambiar la música es
/// reemplazar un archivo de 21 MB, no re-exportar 128.</para>
///
/// <para>⚠ <b>Y por eso los WAV se fueron.</b> Adentro del <c>.pck</c> los
/// archivos van crudos, sin pasar por el importador de Godot, que era justamente
/// el que comprimía los WAV a QOA —30,2 MB de fuente contra 6,0 MB adentro del
/// ejecutable—. Empaquetar esos WAV crudos habría hecho el paquete más grande que
/// lo que ahorraba. Se convirtieron a OGG: 3,5 MB, la misma duración exacta, y sin
/// perder nada que QOA no estuviera perdiendo ya.</para>
///
/// <para>Se llama una vez, en <c>Main._Ready</c>, al lado de
/// <see cref="ArtPack.Mount"/>.</para>
/// </remarks>
public static class AudioPack
{
    /// <summary>Nombre del paquete, igual en todas las plataformas.</summary>
    public const string PackName = "audio.pck";

    /// <summary>
    /// Dónde quedan los archivos del paquete una vez montado.
    /// </summary>
    /// <remarks>
    /// ⚠ NO es <c>res://Data/audio</c>, y es a propósito: en el editor esa carpeta
    /// existe de verdad, y montar encima dejaría dos fuentes para el mismo archivo
    /// sin manera de saber cuál ganó.
    /// </remarks>
    public const string MountPoint = "res://Audio";

    /// <summary>La carpeta del proyecto, la que usa el editor.</summary>
    private const string CarpetaDelProyecto = "res://Data/audio";

    /// <summary>El índice. Su presencia es la prueba de que el montaje sirvió.</summary>
    private const string Indice = "audio.json";

    private static bool _montado;

    /// <summary>
    /// Dónde está el audio: la carpeta del proyecto o el punto de montaje.
    /// </summary>
    /// <remarks>
    /// Lo lee <see cref="Audio.AudioBank"/>. Vale <see cref="CarpetaDelProyecto"/>
    /// hasta que <see cref="Mount"/> encuentre un paquete, así que llamarlo tarde
    /// no rompe nada: solo haría que el primer sonido saliera mudo.
    /// </remarks>
    public static string Root { get; private set; } = CarpetaDelProyecto;

    /// <summary>De dónde salió el audio. Para el diagnóstico del arranque.</summary>
    public static string Source { get; private set; } = "carpeta del proyecto";

    /// <summary>
    /// Busca el audio y lo monta. Idempotente.
    /// </summary>
    /// <remarks>
    /// <para>El orden es el mismo que el del arte y por los mismos motivos:</para>
    ///
    /// <list type="number">
    /// <item><b>La carpeta del proyecto, y solo en el editor</b>: el ciclo de
    /// trabajo diario. Copiar un sonido y escucharlo no puede exigir
    /// reempaquetar.</item>
    /// <item><b><c>audio.pck</c> al lado del ejecutable</b>: la build de
    /// escritorio.</item>
    /// <item><b><c>res://audio.pck</c></b>: Android, donde el paquete viaja adentro
    /// del <c>.pck</c> principal del APK.</item>
    /// <item><b>La carpeta del proyecto fuera del editor</b>: una build corriendo
    /// desde el repo, sin empaquetar.</item>
    /// </list>
    ///
    /// <para>⚠ <b>No hay copia a <c>user://</c></b>, que el arte sí tiene. Aquel la
    /// necesita porque <c>Image.LoadFromFile</c> pide una ruta del sistema de
    /// archivos; acá todo pasa por <c>FileAccess.GetFileAsBytes</c>, que lee
    /// adentro de un <c>.pck</c> anidado sin problema.</para>
    /// </remarks>
    public static void Mount()
    {
        if (_montado)
            return;

        _montado = true;

        // 1. En el editor manda la carpeta del proyecto, aunque haya un audio.pck
        //    viejo al lado: la fuente de verdad mientras se desarrolla es Data\.
        if (OS.HasFeature("editor") && HayCarpetaDelProyecto())
        {
            Anunciar("carpeta del proyecto (editor)");
            return;
        }

        // 2. Al lado del ejecutable. Solo con ruta absoluta, por lo mismo que el
        //    arte: en Android la carpeta sale vacía y "audio.pck" a secas falla al
        //    montarse antes de que el paso 3 lo monte bien.
        string junto = OS.GetExecutablePath().GetBaseDir();

        if (junto.IsAbsolutePath() && Montar(junto.PathJoin(PackName), "al lado del ejecutable"))
            return;

        // 3. Adentro del paquete principal.
        if (Montar($"res://{PackName}", "adentro del paquete principal"))
            return;

        // 4. La carpeta del proyecto fuera del editor: corriendo desde el repo.
        if (HayCarpetaDelProyecto())
        {
            Anunciar("carpeta del proyecto (sin empaquetar)");
            return;
        }

        GD.PushWarning(
            $"No se encontró el audio: ni {PackName} ni {CarpetaDelProyecto}. El juego " +
            "queda mudo. Armar el paquete con --empaquetar-audio.");
    }

    private static bool HayCarpetaDelProyecto() =>
        ArchivoGodot.FileExists($"{CarpetaDelProyecto}/{Indice}");

    private static bool Montar(string ruta, string origen)
    {
        if (!ArchivoGodot.FileExists(ruta))
            return false;

        // `replaceFiles: false`, igual que el arte: el paquete de audio no tiene
        // por qué pisar nada del juego.
        if (!ProjectSettings.LoadResourcePack(ruta, replaceFiles: false))
        {
            GD.PushWarning($"No se pudo montar {ruta}.");
            return false;
        }

        // Montar puede dar true y no traer lo que se esperaba —un pck armado con
        // otra ruta virtual adentro—. El índice es la prueba de que sirve.
        if (!ArchivoGodot.FileExists($"{MountPoint}/{Indice}"))
        {
            GD.PushWarning($"{ruta} se montó pero no tiene {MountPoint}/{Indice}.");
            return false;
        }

        Root = MountPoint;
        Anunciar(origen);

        return true;
    }

    private static void Anunciar(string origen)
    {
        Source = origen;
        GD.Print($"Audio: {origen} → {Root}");
    }
}
