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
/// <para>⚠ <b>Y por eso el modo «sin bordes» es otra cosa:</b> una ventana sin
/// barra de título, del tamaño elegido en la fila de resoluciones y centrada.
/// Del tamaño del monitor tapa también la barra de tareas; más chica, queda
/// como un recuadro limpio arriba del escritorio.</para>
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

    /// <summary>Los tres modos de la ventana.</summary>
    public enum WindowStyle
    {
        /// <summary>Pantalla completa, la sin bordes de Godot que cubre el monitor.</summary>
        Fullscreen,

        /// <summary>Ventana común, con barra de título.</summary>
        Windowed,

        /// <summary>Ventana sin barra de título, del tamaño elegido y centrada.</summary>
        Borderless,
    }

    /// <summary>El modo en que está (o va a estar, apenas se aplique) la ventana.</summary>
    public static WindowStyle Style { get; private set; } = WindowStyle.Fullscreen;

    /// <summary>Si está (o va a estar, apenas se aplique) en pantalla completa.</summary>
    public static bool IsFullscreen => Style == WindowStyle.Fullscreen;

    /// <summary>
    /// El tamaño de ventana elegido. Se conserva también en pantalla completa,
    /// para que «Ventana» vuelva al que el jugador tenía y no a uno fijo.
    /// </summary>
    public static Vector2I WindowSize { get; private set; } = DefaultWindow;

    /// <summary>Aplica lo guardado, o pantalla completa si no hay nada.</summary>
    public static void ApplySaved()
    {
        var config = new ConfigFile();

        if (config.Load(Route) == Error.Ok)
        {
            // ⚠ «modo» entró con la ventana sin bordes. Un video.cfg anterior solo
            // tiene «completa», y se sigue leyendo: sin eso, el que jugaba en
            // ventana arrancaría en pantalla completa después de actualizar.
            bool completa = (bool)config.GetValue("video", "completa", true);

            int modo = (int)config.GetValue("video", "modo",
                (int)(completa ? WindowStyle.Fullscreen : WindowStyle.Windowed));

            if (System.Enum.IsDefined(typeof(WindowStyle), modo))
                Style = (WindowStyle)modo;

            int ancho = (int)config.GetValue("video", "ancho", DefaultWindow.X);
            int alto = (int)config.GetValue("video", "alto", DefaultWindow.Y);

            if (ancho > 0 && alto > 0)
                WindowSize = new Vector2I(ancho, alto);
        }

        Store();
        Apply();
    }

    public static void Complete() => Set(WindowStyle.Fullscreen, WindowSize);

    public static void InWindow(Vector2I tamano) => Set(WindowStyle.Windowed, tamano);

    public static void Borderless(Vector2I tamano) => Set(WindowStyle.Borderless, tamano);

    /// <summary>
    /// Cambia el tamaño desde la fila de resoluciones.
    /// </summary>
    /// <remarks>
    /// Sin bordes sigue sin bordes. Desde pantalla completa pasa a ventana, que es
    /// lo que hacía antes: ahí el tamaño es el del monitor, y elegir otro solo
    /// tiene sentido en una ventana.
    /// </remarks>
    public static void Resize(Vector2I tamano)
        => Set(Style == WindowStyle.Borderless ? WindowStyle.Borderless : WindowStyle.Windowed, tamano);

    private static void Set(WindowStyle estilo, Vector2I tamano)
    {
        Style = estilo;
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
        // En el celular la ventana es la pantalla entera y el sistema no deja
        // cambiarle modo, tamaño ni posición. Opciones ni muestra esas filas ahí,
        // pero un video.cfg de otra plataforma se aplica igual al arrancar.
        if (OS.HasFeature("mobile"))
            return;

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

        // La bandera sale del modo: sin bordes la pide, y en ventana hay que
        // sacarla por si el modo anterior la dejó puesta —una ventana común sin
        // bordes no se puede mover ni cerrar desde la barra—.
        ventana.Borderless = Style == WindowStyle.Borderless;

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
        //
        // Sin bordes se centra contra la pantalla ENTERA y no contra el área útil:
        // del tamaño del monitor tiene que tapar la barra de tareas, que es para lo
        // que se elige ese modo.
        int pantalla = DisplayServer.WindowGetCurrentScreen();

        var util = Style == WindowStyle.Borderless
            ? new Rect2I(DisplayServer.ScreenGetPosition(pantalla), DisplayServer.ScreenGetSize(pantalla))
            : DisplayServer.ScreenGetUsableRect(pantalla);
        var posicion = util.Position + ((util.Size - tamano) / 2);

        posicion.X = Mathf.Max(posicion.X, util.Position.X);
        posicion.Y = Mathf.Max(posicion.Y, util.Position.Y);

        ventana.Position = posicion;
    }

    private static void Store()
    {
        var config = new ConfigFile();

        config.SetValue("video", "completa", IsFullscreen);
        config.SetValue("video", "modo", (int)Style);
        config.SetValue("video", "ancho", WindowSize.X);
        config.SetValue("video", "alto", WindowSize.Y);

        // user:// y no al lado del ejecutable: una build instalada en Archivos de
        // Programa no puede escribir en su propia carpeta.
        config.Save(Route);
    }

    /// <summary>Si existe un archivo de video guardado.</summary>
    public static bool HasSave() => ArchivoGodot.FileExists(Route);
}
