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
///
/// <para>⚠ <b>Los cambios de ventana se aplican de a un paso por frame.</b>
/// Cambiar el modo y el tamaño en la misma llamada era lo que dejaba la opción
/// muerta: en Windows salir de pantalla completa restaura el rectángulo previo
/// por mensajes del sistema, y un <c>WindowSetSize</c> pegado al
/// <c>WindowSetMode</c> quedaba pisado por esa restauración. Por eso
/// <see cref="Apply"/> es asíncrono: modo, un frame, tamaño, un frame,
/// posición.</para>
///
/// <para>⚠ <b>Pantalla completa es la SIN BORDES (<c>Fullscreen</c>), no la
/// exclusiva.</b> La exclusiva en Windows es la que peor sale y vuelve del modo
/// ventana —y además pelea con el alt-tab y con el segundo monitor—; la sin
/// bordes cubre la pantalla igual y cambia de modo sin sorpresas.</para>
///
/// <para>El estado (modo y tamaño de ventana) vive acá y no se lee de
/// <c>DisplayServer</c>: como la aplicación es diferida, leerlo del sistema
/// justo después de apretar devuelve el valor viejo y la pantalla de opciones
/// marcaba la cajita equivocada.</para>
/// </remarks>
public static class Resolution
{
    /// <summary>Las que se ofrecen. Todas 16:9, que es la relación del lienzo.</summary>
    public static readonly Vector2I[] Options =
    [
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160),
    ];

    private const string Route = "user://video.cfg";

    /// <summary>
    /// La ventana por defecto. 1600x900 y no 1920x1080 porque en el monitor más
    /// común —1080p— una ventana del tamaño de la pantalla deja la barra de
    /// título afuera.
    /// </summary>
    private static readonly Vector2I DefaultWindow = new(1600, 900);

    /// <summary>Si está (o va a estar, apenas se aplique) en pantalla completa.</summary>
    public static bool IsFullscreen { get; private set; } = true;

    /// <summary>
    /// El tamaño de ventana elegido. Se conserva también en pantalla completa,
    /// para que «Ventana» vuelva al que el jugador tenía y no a uno fijo.
    /// </summary>
    public static Vector2I WindowSize { get; private set; } = DefaultWindow;

    /// <summary>Aplica lo guardado, o pantalla completa si no hay nada.</summary>
    public static void ApplySaved()
    {
        var config = new ConfigFile();

        bool completa = true;

        if (config.Load(Route) == Error.Ok)
        {
            completa = (bool)config.GetValue("video", "completa", true);

            int ancho = (int)config.GetValue("video", "ancho", DefaultWindow.X);
            int alto = (int)config.GetValue("video", "alto", DefaultWindow.Y);

            if (ancho > 0 && alto > 0)
                WindowSize = new Vector2I(ancho, alto);
        }

        if (completa)
            Complete();
        else
            InWindow(WindowSize);
    }

    public static void Complete()
    {
        IsFullscreen = true;
        Store();
        Apply();
    }

    public static void InWindow(Vector2I tamano)
    {
        IsFullscreen = false;
        WindowSize = tamano;
        Store();
        Apply();
    }

    /// <summary>
    /// Lleva la ventana real al estado guardado, de a un paso por frame.
    /// </summary>
    /// <remarks>
    /// <c>async void</c> a propósito: nadie espera el resultado, y quien apretó
    /// la cajita ya tiene el estado nuevo en <see cref="IsFullscreen"/> y
    /// <see cref="WindowSize"/> para dibujarse. Cada paso vuelve a leer el
    /// estado, así que si el jugador aprieta dos veces seguidas gana la última.
    /// </remarks>
    private static async void Apply()
    {
        if (Engine.GetMainLoop() is not SceneTree arbol)
            return;

        var ventana = arbol.Root;

        if (IsFullscreen)
        {
            if (ventana.Mode != Window.ModeEnum.Fullscreen)
                ventana.Mode = Window.ModeEnum.Fullscreen;

            return;
        }

        // Salir de maximizada o de pantalla completa primero: con la ventana en
        // cualquiera de esos dos estados Windows ignora el tamaño que se le pida.
        if (ventana.Mode != Window.ModeEnum.Windowed)
        {
            ventana.Mode = Window.ModeEnum.Windowed;
            await arbol.ToSignal(arbol, SceneTree.SignalName.ProcessFrame);

            if (IsFullscreen)
                return;
        }

        // Por si el modo anterior dejó la bandera puesta: sin bordes en ventana
        // no se puede mover ni cerrar desde la barra.
        ventana.Borderless = false;

        var tamano = WindowSize;

        if (ventana.Size != tamano)
        {
            ventana.Size = tamano;
            await arbol.ToSignal(arbol, SceneTree.SignalName.ProcessFrame);

            if (IsFullscreen || WindowSize != tamano)
                return;
        }

        // Centrar en el área útil —la pantalla sin la barra de tareas— y nunca
        // con la esquina afuera: sin esto la ventana queda donde estaba, y al
        // achicar puede terminar mitad fuera de la pantalla.
        var util = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var posicion = util.Position + ((util.Size - tamano) / 2);

        posicion.X = Mathf.Max(posicion.X, util.Position.X);
        posicion.Y = Mathf.Max(posicion.Y, util.Position.Y);

        ventana.Position = posicion;
    }

    private static void Store()
    {
        var config = new ConfigFile();

        config.SetValue("video", "completa", IsFullscreen);
        config.SetValue("video", "ancho", WindowSize.X);
        config.SetValue("video", "alto", WindowSize.Y);

        // user:// y no al lado del ejecutable: una build instalada en Archivos de
        // Programa no puede escribir en su propia carpeta.
        config.Save(Route);
    }

    /// <summary>Si existe un archivo de video guardado.</summary>
    public static bool HasSave() => ArchivoGodot.FileExists(Route);
}
