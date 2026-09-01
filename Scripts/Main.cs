using Godot;
using TowerOfArgentum.Simulation;

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
public partial class Main : Node2D
{
    private CanvasLayer _capaMenu = null!;
    private LoginScreen _login = null!;
    private MainScreen _menu = null!;

    private Node2D? _corrida;

    public override void _Ready()
    {
        // Las herramientas headless (--determinismo, --balance, --validar,
        // --importar-npcs) cortan acá: corren y salen sin armar nada.
        if (Tools.HeadlessRunner.HandleArguments())
        {
            GetTree().Quit();
            return;
        }

        // Argumentos de prueba del guardado en la nube. Permiten simular el
        // segundo aparato SIN un segundo aparato: dos instancias en la misma PC,
        // una de ellas con --celular, se comportan como PC y teléfono — otra
        // plataforma declarada y otro archivo de guardado local, mismo servidor.
        //
        //   --celular          se declara "celular" y guarda en un archivo aparte
        //   --servidor <url>   apunta a otra API (para el teléfono real: la IP
        //                      de LAN de la PC, http://192.168.x.x:5090)
        string[] argumentos = OS.GetCmdlineUserArgs();

        for (int i = 0; i < argumentos.Length; i++)
        {
            switch (argumentos[i])
            {
                case "--celular":
                    Device.SimulateMobile = true;
                    SaveFile.FileSuffix = ".celular";
                    GD.Print("Simulando celular: plataforma «celular», guardado aparte.");
                    break;

                case "--servidor" when i + 1 < argumentos.Length:
                    Net.ApiClient.BaseUrl = argumentos[i + 1];
                    GD.Print($"Servidor: {Net.ApiClient.BaseUrl}");
                    i++;
                    break;
            }
        }

        Resolution.ApplySaved();
        Settings.Load();
        Loc.Load(Settings.Language);

        // ⚠ Antes de armar el login, y en este orden: engancha el guardado de la
        // sesión a los cambios del cliente de red, y recién después lee el disco.
        //
        // Leer acá y no adentro de la pantalla no es un detalle: `_Ready` del
        // login corre al agregarlo al árbol, y ahí ya tiene que estar el nick
        // recordado para dejarlo escrito en el campo.
        SessionStore.Hook();
        SessionStore.Load();

        Diagnostico();

        _capaMenu = new CanvasLayer { Name = "CapaMenu", Layer = 5 };
        AddChild(_capaMenu);

        _login = new LoginScreen { Name = "Login" };
        _login.Entered += AlEntrar;
        _capaMenu.AddChild(_login);

        _menu = new MainScreen { Name = "Menu", Visible = false };
        _menu.StartRun += AlEmpezarCorrida;
        _menu.ContinueRun += AlContinuarCorrida;
        _capaMenu.AddChild(_menu);

        // ⚠ Va DESPUÉS de agregar las dos pantallas y NO se espera.
        //
        // Si hay sesión guardada, esto pega un /perfil/me y salta al menú solo.
        // Esperarlo acá congelaría el arranque hasta que el servidor conteste —o
        // hasta los 30 segundos de timeout si está apagado—, así que el juego
        // arranca mostrando el login y la pantalla se reemplaza sola si la sesión
        // resultó buena.
        _ = _login.ResumeSessionAsync();
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

        var juego = _corrida?.GetNodeOrNull<Game>("Game");

        if (juego?.Run is { State: RunState.InProgress })
            juego.StoreRun();
    }

    /// <summary>
    /// Retoma la corrida guardada: re-simula el log y arma la escena.
    /// </summary>
    /// <remarks>
    /// <para>Hay dos guardados posibles y <b>gana el que llegó más lejos</b>: el de
    /// disco y el que dejó otro dispositivo en el servidor. Es lo que permite
    /// empezar en la PC y seguir en el celular, y al revés.</para>
    ///
    /// <para>La comparación es por <b>tick</b> y se hace antes de re-simular:
    /// <see cref="SaveFile.PeekTick"/> lee solo la cabecera del archivo, así que
    /// elegir no cuesta dos reconstrucciones para tirar una.</para>
    ///
    /// <para>⚠ Si el de la nube es de otra <b>temporada</b> —otra
    /// <c>datosVersion</c>— se ignora sin borrarlo. Un log solo se reproduce igual
    /// contra los números con los que se jugó, y esa corrida ya no es replicable
    /// con esta build; pero la fila es del servidor y no le toca al cliente
    /// decidir que se tire.</para>
    /// </remarks>
    private async void AlContinuarCorrida()
    {
        int tickLocal = SaveFile.PeekTick();

        Net.CurrentRun? nube = null;

        if (Net.ApiClient.LoggedIn)
        {
            var (ok, datos, _) = await Net.ApiClient.CurrentRunAsync();

            if (ok && datos is { Log.Length: > 0 } &&
                datos.DataVersion == DataLoader.Current.Version)
            {
                nube = datos;
            }
        }

        (Run? Run, ActionLog? Log, ulong Seed, long RunId) cargado;

        if (nube is not null && nube.SavedTick > tickLocal)
        {
            GD.Print($"Retomando de la nube: corrida {nube.RunId}, tick {nube.SavedTick} " +
                     $"(en disco había {tickLocal})");

            cargado = SaveFile.Rebuild(
                nube.Seed, nube.RunId, nube.SavedTick, System.Convert.FromBase64String(nube.Log!));

            // ⚠ El chequeo de determinismo entre aparatos, gratis. El que subió el
            // checkpoint reportó su oleada; si la re-simulación de ESTA máquina no
            // llega a la misma, las dos arquitecturas divergieron — que es
            // exactamente lo que la regla «todo en enteros, PRNG propio» promete
            // que no pasa. Si este aviso aparece alguna vez, es un bug de la
            // simulación y es el más caro del proyecto: a cazarlo, no a taparlo.
            if (cargado.Run is { } reconstruida && reconstruida.Wave != nube.Wave)
            {
                GD.PushError(
                    $"DETERMINISMO ROTO entre aparatos: el checkpoint dice oleada " +
                    $"{nube.Wave} y la re-simulación llegó a {reconstruida.Wave} " +
                    $"(seed {nube.Seed}, tick {nube.SavedTick}).");
            }
        }
        else
        {
            cargado = SaveFile.Load();
        }

        var (partida, log, seed, runId) = cargado;

        if (partida is null || log is null)
        {
            // ⚠ El guardado ilegible SE BORRA, y el botón se va con él.
            //
            // Antes solo se avisaba por consola y se volvía: el botón quedaba
            // ahí, y apretarlo no hacía absolutamente nada visible. Un guardado
            // que no carga hoy no va a cargar mañana —los motivos son la versión
            // del formato y el hash de los datos, y ninguno se arregla solo—, así
            // que dejarlo en disco es dejar un botón muerto en el menú.
            GD.PushWarning("No se pudo cargar el guardado: se descarta.");
            SaveFile.Delete();

            _ = _menu.RefreshAsync();
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
        GD.Print($"  Cell   : {Canvas.CellSide} px  ·  campo {Canvas.CellsWide} x {Canvas.CellsHigh}");
        GD.Print("───────────────────────────────────────────────────");

        if (!Mathf.IsEqualApprox(lienzo.X, Canvas.CanvasWidth) ||
            !Mathf.IsEqualApprox(lienzo.Y, Canvas.CanvasHeight))
        {
            GD.PushWarning(
                $"El lienzo lógico da {lienzo.X}x{lienzo.Y} y tiene que dar " +
                $"{Canvas.CanvasWidth}x{Canvas.CanvasHeight}. Revisar [display] en project.godot.");
        }
    }

    private void AlEntrar()
    {
        _login.Visible = false;
        _menu.Visible = true;
        _ = _menu.RefreshAsync();
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

        var juego = new Game { Name = "Game" };
        juego.Configure(seed, runId);

        // Retomar va ANTES de AddChild: _Ready tiene que ver la partida puesta.
        if (retomada is { } r)
            juego.Resume(r.Run, r.Log, seed, runId);

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

        var mochila = new BackpackScreen(juego) { Name = "Backpack" };
        capaModal.AddChild(mochila);
        juego.Backpack = mochila;

        capaModal.AddChild(new RunEndScreen(juego) { Name = "FinDeCorrida" });

        // La preparación va ANTES de la elección: cuando las dos podrían estar
        // abiertas manda la elección, que sí es modal y no se puede cerrar sin
        // elegir. La preparación se esconde sola en ese caso.
        capaModal.AddChild(new PreparationScreen(juego) { Name = "Preparacion" });

        // ⚠ El MERCADO reemplazó a la elección y a la tienda, que eran dos
        // pantallas. Iban en momentos distintos —primero elegir, después
        // comprar— y por eso la decisión que regala poder y la que lo cobra
        // nunca se veían juntas. Ver MarketScreen.
        //
        // El panel del defensor seleccionado: targeting, venta y estados de
        // soporte. No es modal y no depende de la fase — el targeting se puede
        // cambiar con la oleada corriendo, que es cuando se nota que hace falta.
        capaModal.AddChild(new DefenderScreen(juego) { Name = "Defensor" });

        // Va DESPUÉS del panel del defensor: el orden de los hijos es el orden de
        // dibujo, y una ventana grande tapada a medias por el panel de la columna
        // derecha se vería como un bug.
        var mercado = new MarketScreen(juego) { Name = "Mercado" };
        capaModal.AddChild(mercado);
        juego.Market = mercado;

        // ⚠ EL DROP DEL JEFE va DESPUÉS del mercado, y por eso está acá y no
        // arriba: es la única pantalla del juego que frena la corrida y no se
        // puede cerrar, así que tiene que quedar por encima de todo lo que sí se
        // cierra. Antes vivía adentro del mercado como una tarjeta más, al lado
        // de las ofertas de bronce.
        capaModal.AddChild(new BossDropScreen(juego) { Name = "DropDelJefe" });

        // El menú de ESC, arriba de todo menos del mercado en la práctica: su
        // _Input cede el ESC cuando la elección o la mochila están abiertas.
        //
        // Opciones se agrega DESPUÉS para quedar encima del menú, y su _Input
        // corre antes: mientras está abierto se queda con el ESC.
        var opciones = new OptionsScreen { Name = "Opciones" };

        var menuEsc = new EscMenuScreen(juego, opciones) { Name = "MenuEsc" };
        menuEsc.ExitToMenu += VolverAlMenu;
        capaModal.AddChild(menuEsc);
        capaModal.AddChild(opciones);

        // Los tres botones de la esquina derecha de la barra de arriba —engranaje,
        // MENÚ y pausa— entran por acá. El HUD no los puede buscar en el árbol:
        // es hijo de otra capa y quedaría atado al nombre de un nodo.
        juego.EscMenu = menuEsc;
        juego.Options = opciones;

        return raiz;
    }

    public override void _UnhandledInput(InputEvent evento)
    {
        if (evento is not InputEventKey tecla || !tecla.Pressed || tecla.Echo)
            return;

        if (tecla.Keycode != Key.Escape || _corrida is null)
            return;

        var juego = _corrida.GetNodeOrNull<Game>("Game");

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
        _ = _menu.RefreshAsync();
    }
}
