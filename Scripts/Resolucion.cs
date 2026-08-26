using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// El menú de resolución.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Cambiar la resolución NO cambia el campo visible.</b> El juego se
/// dibuja siempre contra un lienzo lógico de 1920x1080 y Godot lo escala con
/// <c>stretch/mode = canvas_items</c> y <c>stretch/aspect = keep</c>. Lo que
/// cambia es la <b>nitidez</b>: en 1440p el mismo campo se rasteriza con más
/// píxeles reales.</para>
///
/// <para>Y <c>keep</c> no es una preferencia estética: con <c>expand</c>, un
/// monitor ultrawide vería más ruta, y en un juego con ranking global ordenado
/// por velocidad eso sería ventaja de hardware (INTERFAZ.md §1).</para>
/// </remarks>
public static class Resolucion
{
    /// <summary>Las que se ofrecen. Todas 16:9, que es la relación del lienzo.</summary>
    public static readonly Vector2I[] Opciones =
    [
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160),
    ];

    private const string Ruta = "user://video.cfg";

    /// <summary>Aplica lo guardado, o pantalla completa si no hay nada.</summary>
    public static void AplicarGuardada()
    {
        var config = new ConfigFile();

        if (config.Load(Ruta) != Error.Ok)
        {
            Completa();
            return;
        }

        bool completa = (bool)config.GetValue("video", "completa", true);

        if (completa)
        {
            Completa();
            return;
        }

        int ancho = (int)config.GetValue("video", "ancho", 1920);
        int alto = (int)config.GetValue("video", "alto", 1080);

        EnVentana(new Vector2I(ancho, alto));
    }

    public static void Completa()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
        Guardar(completa: true, DisplayServer.ScreenGetSize());
    }

    public static void EnVentana(Vector2I tamano)
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(tamano);

        // Centrar: sin esto la ventana queda donde estaba, y al achicar puede
        // terminar mitad afuera de la pantalla.
        var pantalla = DisplayServer.ScreenGetSize();
        DisplayServer.WindowSetPosition((pantalla - tamano) / 2);

        Guardar(completa: false, tamano);
    }

    private static void Guardar(bool completa, Vector2I tamano)
    {
        var config = new ConfigFile();

        config.SetValue("video", "completa", completa);
        config.SetValue("video", "ancho", tamano.X);
        config.SetValue("video", "alto", tamano.Y);

        // user:// y no al lado del ejecutable: una build instalada en Archivos de
        // Programa no puede escribir en su propia carpeta.
        config.Save(Ruta);
    }

    /// <summary>Si existe un archivo de video guardado.</summary>
    public static bool HayGuardado() => ArchivoGodot.FileExists(Ruta);
}
