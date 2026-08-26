using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// La raíz. Orquesta las tres etapas: login, menú y corrida.
/// </summary>
/// <remarks>
/// <para>Una sola escena que muestra y esconde capas, en vez de cambiar de
/// escena. Es más simple —no hay estado que pasar de una a otra— y evita el bug
/// clásico de perder una corrida en curso al navegar.</para>
///
/// <para>El diagnóstico del arranque verifica dos cosas que, si están mal, se
/// descubrirían mucho más tarde y disfrazadas de otra cosa: que <c>net10.0</c>
/// levanta contra <c>GodotSharp</c> de <c>net8.0</c>, y que el lienzo lógico es
/// 1920x1080.</para>
/// </remarks>
public partial class Principal : Node2D
{
    private CanvasLayer _capaMenu = null!;
    private PantallaLogin _login = null!;
    private PantallaPrincipal _menu = null!;

    private Node2D? _corrida;

    public override void _Ready()
    {
        // Las herramientas headless (--determinismo, --balance, --validar,
        // --importar-npcs) cortan acá: corren y salen sin armar nada.
        if (Herramientas.CorredorHeadless.AtenderArgumentos())
        {
            GetTree().Quit();
            return;
        }

        Resolucion.AplicarGuardada();
        Diagnostico();

        _capaMenu = new CanvasLayer { Name = "CapaMenu", Layer = 5 };
        AddChild(_capaMenu);

        _login = new PantallaLogin { Name = "Login" };
        _login.Entro += AlEntrar;
        _capaMenu.AddChild(_login);

        _menu = new PantallaPrincipal { Name = "Menu", Visible = false };
        _menu.EmpezarCorrida += AlEmpezarCorrida;
        _menu.ContinuarCorrida += AlContinuarCorrida;
        _capaMenu.AddChild(_menu);
    }

    /// <summary>
    /// Al cerrarse la ventana —la X, Alt+F4—, la corrida en curso se guarda.
    /// </summary>
    /// <remarks>
    /// Es la red de seguridad de abajo de todo: el autoguardado por oleada y el
    /// menú de ESC son las salidas ordenadas, esto ataja las otras. Godot manda
    /// esta notificación ANTES de cerrar, así que el guardado sincrónico llega.
    /// </remarks>
    public override void _Notification(int que)
    {
        if (que != NotificationWMCloseRequest)
            return;

        var juego = _corrida?.GetNodeOrNull<Juego>("Juego");

        if (juego?.Partida is { Estado: EstadoPartida.EnCurso })
            juego.GuardarCorrida();
    }

    /// <summary>Retoma la corrida guardada: re-simula el log y arma la escena.</summary>
    private void AlContinuarCorrida()
    {
        var (partida, log, seed, runId) = Guardado.Cargar();

        if (partida is null || log is null)
        {
            GD.PushWarning("No se pudo cargar el guardado.");
            return;
        }

        _capaMenu.Visible = false;

        _corrida?.QueueFree();
        _corrida = ArmarCorrida(runId, seed, (partida, log));
        AddChild(_corrida);
    }

    private void Diagnostico()
    {
        var lienzo = GetViewportRect().Size;

        GD.Print("── Tower of Argentum ──────────────────────────────");
        GD.Print($"  Godot   : {Engine.GetVersionInfo()["string"]}");
        GD.Print($"  .NET    : {System.Environment.Version}");
        GD.Print($"  Lienzo  : {lienzo.X} x {lienzo.Y}");
        GD.Print($"  Celda   : {Escala.LadoCelda} px  ·  campo {Escala.CeldasAncho} x {Escala.CeldasAlto}");
        GD.Print("───────────────────────────────────────────────────");

        if (!Mathf.IsEqualApprox(lienzo.X, Escala.LienzoAncho) ||
            !Mathf.IsEqualApprox(lienzo.Y, Escala.LienzoAlto))
        {
            GD.PushWarning(
                $"El lienzo lógico da {lienzo.X}x{lienzo.Y} y tiene que dar " +
                $"{Escala.LienzoAncho}x{Escala.LienzoAlto}. Revisar [display] en project.godot.");
        }
    }

    private void AlEntrar()
    {
        _login.Visible = false;
        _menu.Visible = true;
        _ = _menu.RefrescarAsync();
    }

    /// <summary>
    /// Arranca la corrida con el <c>runId</c> y la seed que dio el servidor.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>La seed viene de afuera, siempre.</b> Si la generara el cliente, un
    /// tramposo podría probar semillas hasta encontrar una fácil y reclamar la
    /// corrida como buena.
    /// </remarks>
    private void AlEmpezarCorrida(long runId, ulong seed)
    {
        _capaMenu.Visible = false;

        _corrida?.QueueFree();
        _corrida = ArmarCorrida(runId, seed);
        AddChild(_corrida);
    }

    private Node2D ArmarCorrida(long runId, ulong seed, (Partida Partida, LogAcciones Log)? retomada = null)
    {
        var raiz = new Node2D { Name = "Corrida" };

        var juego = new Juego { Name = "Juego" };
        juego.Configurar(seed, runId);

        // Retomar va ANTES de AddChild: _Ready tiene que ver la partida puesta.
        if (retomada is { } r)
            juego.Retomar(r.Partida, r.Log, seed, runId);

        raiz.AddChild(juego);

        // El HUD va en un CanvasLayer para quedar SIEMPRE encima del mundo y no
        // verse afectado por ninguna transformación de cámara. Flota sobre el
        // arte, sin marco: es la diferencia con Austral AO, donde la interfaz
        // rodea una ventanita de juego (INTERFAZ.md §3).
        var capaHud = new CanvasLayer { Name = "CapaHud", Layer = 10 };
        raiz.AddChild(capaHud);
        capaHud.AddChild(new Hud(juego) { Name = "Hud" });

        var capaModal = new CanvasLayer { Name = "CapaModal", Layer = 20 };
        raiz.AddChild(capaModal);

        var mochila = new PantallaMochila(juego) { Name = "Mochila" };
        capaModal.AddChild(mochila);
        juego.Mochila = mochila;

        capaModal.AddChild(new PantallaFinDeCorrida(juego) { Name = "FinDeCorrida" });

        // La preparación va ANTES de la elección: cuando las dos podrían estar
        // abiertas manda la elección, que sí es modal y no se puede cerrar sin
        // elegir. La preparación se esconde sola en ese caso.
        capaModal.AddChild(new PantallaPreparacion(juego) { Name = "Preparacion" });

        // La elección va ÚLTIMA para quedar arriba de todo: frena la corrida y no
        // se puede cerrar sin elegir, así que nada puede taparla.
        capaModal.AddChild(new PantallaEleccion(juego) { Name = "Eleccion" });

        // El menú de ESC, arriba de todo menos de la elección en la práctica: su
        // _Input cede el ESC cuando la elección o la mochila están abiertas.
        var menuEsc = new PantallaMenuEsc(juego) { Name = "MenuEsc" };
        menuEsc.SalirAlMenu += VolverAlMenu;
        capaModal.AddChild(menuEsc);

        return raiz;
    }

    public override void _UnhandledInput(InputEvent evento)
    {
        if (evento is not InputEventKey tecla || !tecla.Pressed || tecla.Echo)
            return;

        if (tecla.Keycode != Key.Escape || _corrida is null)
            return;

        var juego = _corrida.GetNodeOrNull<Juego>("Juego");

        // Escape vuelve al menú solo si la corrida terminó. En el medio, salir
        // sería perder horas de juego por una tecla al lado de la que se quiso
        // apretar; para eso está el guardado.
        if (juego?.Partida is not null && juego.Partida.Estado != EstadoPartida.EnCurso)
            VolverAlMenu();
    }

    private void VolverAlMenu()
    {
        _corrida?.QueueFree();
        _corrida = null;

        _capaMenu.Visible = true;
        _ = _menu.RefrescarAsync();
    }
}
