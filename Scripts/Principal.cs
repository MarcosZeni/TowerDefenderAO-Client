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
        Configuracion.Cargar();
        Loc.Cargar(Configuracion.Idioma);

        // ⚠ Antes de armar el login, y en este orden: engancha el guardado de la
        // sesión a los cambios del cliente de red, y recién después lee el disco.
        //
        // Leer acá y no adentro de la pantalla no es un detalle: `_Ready` del
        // login corre al agregarlo al árbol, y ahí ya tiene que estar el nick
        // recordado para dejarlo escrito en el campo.
        Sesion.Enganchar();
        Sesion.Cargar();

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

        // ⚠ Va DESPUÉS de agregar las dos pantallas y NO se espera.
        //
        // Si hay sesión guardada, esto pega un /perfil/me y salta al menú solo.
        // Esperarlo acá congelaría el arranque hasta que el servidor conteste —o
        // hasta los 30 segundos de timeout si está apagado—, así que el juego
        // arranca mostrando el login y la pantalla se reemplaza sola si la sesión
        // resultó buena.
        _ = _login.ReanudarSesionAsync();
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

        if (juego?.Run is { State: RunState.InProgress })
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
        GD.Print($"  Cell   : {Escala.LadoCelda} px  ·  campo {Escala.CeldasAncho} x {Escala.CeldasAlto}");
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

    private Node2D ArmarCorrida(long runId, ulong seed, (Run Run, ActionLog Log)? retomada = null)
    {
        var raiz = new Node2D { Name = "RunCurve" };

        var juego = new Juego { Name = "Juego" };
        juego.Configurar(seed, runId);

        // Retomar va ANTES de AddChild: _Ready tiene que ver la partida puesta.
        if (retomada is { } r)
            juego.Retomar(r.Run, r.Log, seed, runId);

        raiz.AddChild(juego);

        // El HUD va en un CanvasLayer para quedar SIEMPRE encima del mundo y
        // —esto es lo que importa— para NO heredar la transformación de `juego`,
        // que está corrido y a escala adentro del marco (INTERFAZ.md §3). Colgado
        // de `raiz` como hermano tendría el mismo problema el día que alguien le
        // ponga una cámara al mundo.
        //
        // Encima y no flotando: el HUD son las cuatro paredes del marco, y el
        // mundo vive en el agujero del medio. Es la misma idea que Austral AO,
        // que dibuja su ventanita de juego adentro de un marco de madera.
        var capaHud = new CanvasLayer { Name = "CapaHud", Layer = 10 };
        raiz.AddChild(capaHud);
        capaHud.AddChild(new Hud(juego) { Name = "Hud" });

        var capaModal = new CanvasLayer { Name = "CapaModal", Layer = 20 };
        raiz.AddChild(capaModal);

        var mochila = new PantallaMochila(juego) { Name = "Backpack" };
        capaModal.AddChild(mochila);
        juego.Backpack = mochila;

        capaModal.AddChild(new PantallaFinDeCorrida(juego) { Name = "FinDeCorrida" });

        // La preparación va ANTES de la elección: cuando las dos podrían estar
        // abiertas manda la elección, que sí es modal y no se puede cerrar sin
        // elegir. La preparación se esconde sola en ese caso.
        capaModal.AddChild(new PantallaPreparacion(juego) { Name = "Preparacion" });

        // ⚠ El MERCADO reemplazó a la elección y a la tienda, que eran dos
        // pantallas. Iban en momentos distintos —primero elegir, después
        // comprar— y por eso la decisión que regala poder y la que lo cobra
        // nunca se veían juntas. Ver PantallaMercado.
        //
        // El panel del defensor seleccionado: targeting, venta y estados de
        // soporte. No es modal y no depende de la fase — el targeting se puede
        // cambiar con la oleada corriendo, que es cuando se nota que hace falta.
        capaModal.AddChild(new PantallaDefensor(juego) { Name = "Defensor" });

        // Va DESPUÉS del panel del defensor: el orden de los hijos es el orden de
        // dibujo, y una ventana grande tapada a medias por el panel de la columna
        // derecha se vería como un bug.
        var mercado = new PantallaMercado(juego) { Name = "Mercado" };
        capaModal.AddChild(mercado);
        juego.Mercado = mercado;

        // El menú de ESC, arriba de todo menos del mercado en la práctica: su
        // _Input cede el ESC cuando la elección o la mochila están abiertas.
        //
        // Opciones se agrega DESPUÉS para quedar encima del menú, y su _Input
        // corre antes: mientras está abierto se queda con el ESC.
        var opciones = new PantallaOpciones { Name = "Opciones" };

        var menuEsc = new PantallaMenuEsc(juego, opciones) { Name = "MenuEsc" };
        menuEsc.SalirAlMenu += VolverAlMenu;
        capaModal.AddChild(menuEsc);
        capaModal.AddChild(opciones);

        // Los tres botones de la esquina derecha de la barra de arriba —engranaje,
        // MENÚ y pausa— entran por acá. El HUD no los puede buscar en el árbol:
        // es hijo de otra capa y quedaría atado al nombre de un nodo.
        juego.EscMenu = menuEsc;
        juego.Opciones = opciones;

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
        if (juego?.Run is not null && juego.Run.State != RunState.InProgress)
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
