using System.Threading.Tasks;
using Godot;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// Ingreso y registro.
/// </summary>
/// <remarks>
/// <para>Usa nodos de UI de Godot y no dibujo a mano, al revés que el HUD: acá
/// hay <b>campos de texto</b>, y escribir un campo de texto desde cero —cursor,
/// selección, portapapeles, acentos con teclado muerto— es mucho trabajo para
/// reinventar algo que el motor ya trae bien.</para>
///
/// <para>⚠ <b>La contraseña viaja en claro sobre TLS y la hashea el servidor.</b>
/// Si la hasheara el cliente, ese hash <i>sería</i> la contraseña: quien copiara
/// la base lo reenviaría y entraría. Y hashear con Argon2 acá tampoco serviría,
/// porque la sal es por usuario y vive en la base — habría que pedirla antes de
/// loguear, y eso delata qué cuentas existen.</para>
///
/// <para>Lo que <b>no</b> hay: recuperar contraseña por mail. No hay servidor de
/// correo, y prometer un mail que no llega es peor que no ofrecerlo.</para>
/// </remarks>
public partial class LoginScreen : Control
{
    private LineEdit _nick = null!;
    private LineEdit _password = null!;
    private Label _mensaje = null!;
    private Button _entrar = null!;
    private Button _registrar = null!;
    private CheckBox _recordar = null!;
    private float _keyboardOffset;

    /// <summary>
    /// El formulario, escondido mientras se prueba la sesión guardada.
    /// </summary>
    /// <remarks>
    /// Se esconde entero en vez de solo apagar los botones: mostrar campos de
    /// texto vacíos mientras el juego dice «entrando como Nacho» invita a
    /// escribir encima de algo que está por desaparecer.
    /// </remarks>
    private VBoxContainer _formulario = null!;

    /// <summary>Se dispara cuando la sesión quedó abierta.</summary>
    public event System.Action? Entered;

    /// <summary>
    /// El marco que se dibuja detrás de los nodos, con el estilo del diseño.
    /// </summary>
    /// <remarks>
    /// Los <c>Control</c> del motor van adentro de esto. Sin el marco, los campos
    /// flotarían sobre un fondo vacío y el login sería la única pantalla del juego
    /// sin panel.
    /// </remarks>
    private Rect2 Marco()
        => new((Canvas.CanvasWidth / 2f) - 300f, (Canvas.CanvasHeight / 2f) - 260f + _keyboardOffset, 600f, 520f);

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        SetProcess(OS.HasFeature("mobile"));

        // ⚠ El tema va acá y no en cada nodo: es la única pantalla con nodos del
        // motor, y con el tema de fábrica de Godot se vería de otro juego que el
        // resto de la interfaz.
        Theme = UiTheme.Current;

        var marco = Marco();

        var caja = new VBoxContainer
        {
            Position = new Vector2(marco.Position.X + 80f, marco.Position.Y + 120f),
            CustomMinimumSize = new Vector2(440, 0),
        };

        caja.AddThemeConstantOverride("separation", 14);
        AddChild(caja);

        _formulario = caja;

        caja.AddChild(Etiqueta(Loc.Get("ui.login.nombre")));

        _nick = new LineEdit
        {
            CustomMinimumSize = new Vector2(440, 46),
            MaxLength = 20,
            // En celular se abre al tocar el campo, no al enfocar durante el arranque.
            VirtualKeyboardShowOnFocus = !OS.HasFeature("mobile"),

            // El nick de la última sesión queda escrito, aunque el token haya
            // caducado o el jugador haya salido de la cuenta. No es un secreto, y
            // volver a tipearlo es la mitad de la molestia de entrar.
            Text = SessionStore.RememberedNick,
        };

        caja.AddChild(_nick);

        caja.AddChild(Etiqueta(Loc.Get("ui.login.contrasena")));

        _password = new LineEdit
        {
            CustomMinimumSize = new Vector2(440, 46),
            Secret = true,
            VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Password,
            VirtualKeyboardShowOnFocus = !OS.HasFeature("mobile"),
        };

        // Enter en la contraseña entra directo: es lo que espera cualquiera que
        // haya usado un login alguna vez.
        // El parámetro se llama `texto` y no `_`: con `_` el compilador lo toma
        // como el nombre del parámetro (un string) y después `_ = EntrarAsync()`
        // intenta asignarle un Task.
        _password.TextSubmitted += texto => { _ = EntrarAsync(); };

        caja.AddChild(_password);

        // ⚠ Prendido de fábrica. Escribir la contraseña en cada arranque es la
        // molestia más grande del juego y la que menos aporta; el tilde existe
        // apagable porque el archivo de sesión NO se puede cifrar de verdad —la
        // clave viviría al lado— y en una computadora compartida esta es la
        // respuesta real. Lo que se guarda es el token, nunca la contraseña.
        _recordar = new CheckBox
        {
            Text = Loc.Get("ui.login.recordarme"),
            ButtonPressed = Settings.RememberSession,
        };

        _recordar.Toggled += marcado =>
        {
            Settings.RememberSession = marcado;
            Settings.Store();

            if (!marcado)
                SessionStore.Delete();
        };

        caja.AddChild(_recordar);

        caja.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        var botones = new HBoxContainer();
        botones.AddThemeConstantOverride("separation", 12);
        caja.AddChild(botones);

        _entrar = new Button { Text = Loc.Get("ui.login.entrar"), CustomMinimumSize = new Vector2(214, 50) };
        _entrar.Pressed += () => _ = EntrarAsync();
        botones.AddChild(_entrar);

        _registrar = new Button { Text = Loc.Get("ui.login.crear_cuenta"), CustomMinimumSize = new Vector2(214, 50) };
        _registrar.Pressed += () => _ = RegisterAsync();
        botones.AddChild(_registrar);

        // ⚠ El mensaje va colgado del marco y NO del formulario: mientras se
        // prueba la sesión guardada el formulario se esconde, y adentro se
        // escondería justo el renglón que dice qué está pasando.
        _mensaje = new Label
        {
            Text = "",
            Position = new Vector2(marco.Position.X + 40f, marco.End.Y - 62f),
            Size = new Vector2(520, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _mensaje.AddThemeColorOverride("font_color", Palette.Danger);
        AddChild(_mensaje);

        if (OS.HasFeature("mobile"))
        {
            ShowKeyboardOnTap(_nick);
            ShowKeyboardOnTap(_password);
        }

        // Con el nick ya escrito, el cursor va a la contraseña: es el único campo
        // que falta.
        if (_nick.Text.Length > 0)
            _password.GrabFocus();
        else
            _nick.GrabFocus();
    }

    /// <summary>
    /// Abre el teclado del celular al tocar el campo.
    /// </summary>
    /// <remarks>
    /// <para><c>VirtualKeyboardShowOnFocus</c> está apagado porque el foco inicial
    /// de <c>_Ready</c> abriría el teclado tapando el login apenas arranca; esto lo
    /// abre recién cuando el jugador toca el campo. Tocar un campo que ya tenía el
    /// foco también lo abre: es la única manera de recuperarlo después de cerrarlo
    /// con Atrás.</para>
    ///
    /// <para>⚠ <b>En el emulador NO se ve nunca, y no es un bug.</b> El emulador
    /// siempre expone un teclado físico (<c>qwerty2</c>), aun con
    /// <c>hw.keyboard=no</c>, y Godot no abre el teclado virtual si
    /// <see cref="DisplayServer.HasHardwareKeyboard"/> da true: ahí se escribe con
    /// el teclado de la PC. Medido el 2026-09-12: el toque llega, Godot llama y
    /// Android no recibe ningún pedido de mostrar.</para>
    /// </remarks>
    private static void ShowKeyboardOnTap(LineEdit field)
    {
        field.GuiInput += evento =>
        {
            // Godot emula el mouse desde el toque, así que soltar el dedo llega
            // como botón izquierdo soltado.
            if (evento is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                return;

            if (!field.Editable)
                return;

            DisplayServer.VirtualKeyboardShow(
                field.Text,
                field.GetGlobalRect(),
                (DisplayServer.VirtualKeyboardType)(int)field.VirtualKeyboardType,
                field.MaxLength,
                field.CaretColumn,
                field.CaretColumn);
        };
    }

    public override void _Process(double delta)
    {
        float offset = 0f;
        int keyboardHeight = DisplayServer.VirtualKeyboardGetHeight();

        if (IsVisibleInTree() && keyboardHeight > 0 &&
            GetViewport().GuiGetFocusOwner() is LineEdit field && IsAncestorOf(field))
        {
            // Android informa píxeles físicos; el formulario usa el lienzo lógico.
            // Se descuenta el desplazamiento previo para no acumularlo cada frame.
            float scale = GetScreenTransform().Y.Length();
            if (scale > 0f)
            {
                float fieldBottom = (field.GetScreenTransform() * field.Size).Y - _keyboardOffset * scale;
                float visibleBottom = DisplayServer.WindowGetSize().Y - keyboardHeight - 16f * scale;
                offset = Mathf.Min(0f, (visibleBottom - fieldBottom) / scale);
            }
        }

        if (!Mathf.IsEqualApprox(_keyboardOffset, offset))
        {
            var movement = new Vector2(0f, offset - _keyboardOffset);
            _formulario.Position += movement;
            _mensaje.Position += movement;
            _keyboardOffset = offset;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Prueba la sesión guardada en disco y entra sola si sirve.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No se espera desde el arranque.</b> Congelar la pantalla hasta
    /// que el servidor conteste —o hasta los 30 segundos de timeout si está
    /// apagado— convertiría una comodidad en el peor arranque posible. El juego
    /// muestra el login enseguida y esto lo reemplaza si la sesión resultó
    /// buena.</para>
    ///
    /// <para>⚠ <b>Quien valida es el SERVIDOR.</b> Que el token esté en disco y
    /// sin caducar no dice nada: pudo haberse cerrado la sesión desde otra
    /// máquina, o el servidor pudo revocarlo. Por eso pega un <c>/perfil/me</c> —
    /// el pedido más barato que exige token— y solo con esa respuesta da la
    /// sesión por abierta. Un 401 vacía el token y borra el archivo solo, por el
    /// enganche de <see cref="SessionStore"/>.</para>
    /// </remarks>
    public async Task ResumeSessionAsync()
    {
        // El disco ya se leyó en `Main._Ready`: si hay token, está puesto.
        if (!Net.ApiClient.LoggedIn)
            return;

        _formulario.Visible = false;
        Ocupado(true, $"{Loc.Get("ui.login.reanudando")} {SessionStore.RememberedNick}…");

        var (ok, _, _) = await Net.ApiClient.ProfileAsync();

        if (ok)
        {
            _mensaje.Text = "";
            Entered?.Invoke();
            return;
        }

        // No sirvió: el formulario vuelve, con el nick escrito. No se muestra el
        // error del servidor —«token inválido» no le dice nada a nadie— sino qué
        // tiene que hacer.
        _formulario.Visible = true;
        _entrar.Disabled = false;
        _registrar.Disabled = false;

        Message(Loc.Get("ui.login.sesion_vencida"));
        _password.GrabFocus();
    }

    /// <summary>El fondo y el marco. Los campos son nodos y se dibujan encima.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, Canvas.CanvasWidth, Canvas.CanvasHeight), Palette.BackgroundMain);

        var marco = Marco();

        this.Panel(marco, elevado: true, opacidad: 1f);
        this.Glow(marco, Palette.Info, 0.8f, 5);

        this.Label(marco.Row(44f, 52f), Loc.Get("ui.login.titulo"), 42, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Rule(marco, 108f);
    }

    private static Label Etiqueta(string texto)
    {
        var l = new Label { Text = texto };

        l.AddThemeColorOverride("font_color", Palette.TextMuted);
        l.AddThemeFontSizeOverride("font_size", 15);

        return l;
    }

    private async Task EntrarAsync()
    {
        if (!Validate())
            return;

        Ocupado(true, Loc.Get("ui.login.entrando"));

        var (sesion, error) = await Net.ApiClient.LoginAsync(_nick.Text.Trim(), _password.Text);

        Terminar(sesion is not null, error);
    }

    private async Task RegisterAsync()
    {
        if (!Validate())
            return;

        Ocupado(true, Loc.Get("ui.login.creando"));

        var (sesion, error) = await Net.ApiClient.RegisterAsync(_nick.Text.Trim(), _password.Text, null);

        Terminar(sesion is not null, error);
    }

    /// <summary>
    /// Chequeo local, solo para no gastar un viaje al servidor.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>No reemplaza la validación del servidor</b>, que es la que manda: un
    /// cliente modificado se saltea esto en un segundo. Acá está por comodidad, no
    /// por seguridad.
    /// </remarks>
    private bool Validate()
    {
        string nick = _nick.Text.Trim();

        if (nick.Length is < 3 or > 20 || nick.Contains(' ', System.StringComparison.Ordinal))
        {
            Message(Loc.Get("ui.login.nombre_invalido"));
            return false;
        }

        if (_password.Text.Length < 8)
        {
            Message(Loc.Get("ui.login.pass_corta"));
            return false;
        }

        return true;
    }

    private void Ocupado(bool ocupado, string texto)
    {
        _entrar.Disabled = ocupado;
        _registrar.Disabled = ocupado;

        _mensaje.AddThemeColorOverride("font_color", Palette.TextMuted);
        _mensaje.Text = texto;
    }

    private void Terminar(bool ok, string? error)
    {
        _entrar.Disabled = false;
        _registrar.Disabled = false;

        if (ok)
        {
            _mensaje.Text = "";
            Entered?.Invoke();
            return;
        }

        Message(error ?? Loc.Get("ui.login.error_entrar"));
    }

    private void Message(string texto)
    {
        _mensaje.AddThemeColorOverride("font_color", Palette.Danger);
        _mensaje.Text = texto;
    }
}
