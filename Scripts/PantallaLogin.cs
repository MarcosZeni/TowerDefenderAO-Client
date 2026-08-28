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
public partial class PantallaLogin : Control
{
    private LineEdit _nick = null!;
    private LineEdit _password = null!;
    private Label _mensaje = null!;
    private Button _entrar = null!;
    private Button _registrar = null!;

    /// <summary>Se dispara cuando la sesión quedó abierta.</summary>
    public event System.Action? Entro;

    /// <summary>
    /// El marco que se dibuja detrás de los nodos, con el estilo del diseño.
    /// </summary>
    /// <remarks>
    /// Los <c>Control</c> del motor van adentro de esto. Sin el marco, los campos
    /// flotarían sobre un fondo vacío y el login sería la única pantalla del juego
    /// sin panel.
    /// </remarks>
    private static Rect2 Marco()
        => new((Escala.LienzoAncho / 2f) - 300f, (Escala.LienzoAlto / 2f) - 260f, 600f, 520f);

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);

        // ⚠ El tema va acá y no en cada nodo: es la única pantalla con nodos del
        // motor, y con el tema de fábrica de Godot se vería de otro juego que el
        // resto de la interfaz.
        Theme = UiTheme.Actual;

        var marco = Marco();

        var caja = new VBoxContainer
        {
            Position = new Vector2(marco.Position.X + 80f, marco.Position.Y + 120f),
            CustomMinimumSize = new Vector2(440, 0),
        };

        caja.AddThemeConstantOverride("separation", 14);
        AddChild(caja);

        caja.AddChild(Etiqueta(Loc.Get("ui.login.nombre")));

        _nick = new LineEdit
        {
            CustomMinimumSize = new Vector2(440, 46),
            MaxLength = 20,
        };

        caja.AddChild(_nick);

        caja.AddChild(Etiqueta(Loc.Get("ui.login.contrasena")));

        _password = new LineEdit
        {
            CustomMinimumSize = new Vector2(440, 46),
            Secret = true,
        };

        // Enter en la contraseña entra directo: es lo que espera cualquiera que
        // haya usado un login alguna vez.
        // El parámetro se llama `texto` y no `_`: con `_` el compilador lo toma
        // como el nombre del parámetro (un string) y después `_ = EntrarAsync()`
        // intenta asignarle un Task.
        _password.TextSubmitted += texto => { _ = EntrarAsync(); };

        caja.AddChild(_password);

        caja.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        var botones = new HBoxContainer();
        botones.AddThemeConstantOverride("separation", 12);
        caja.AddChild(botones);

        _entrar = new Button { Text = Loc.Get("ui.login.entrar"), CustomMinimumSize = new Vector2(214, 50) };
        _entrar.Pressed += () => _ = EntrarAsync();
        botones.AddChild(_entrar);

        _registrar = new Button { Text = Loc.Get("ui.login.crear_cuenta"), CustomMinimumSize = new Vector2(214, 50) };
        _registrar.Pressed += () => _ = RegistrarAsync();
        botones.AddChild(_registrar);

        _mensaje = new Label { Text = "" };
        _mensaje.AddThemeColorOverride("font_color", Palette.Danger);
        caja.AddChild(_mensaje);

        _nick.GrabFocus();
    }

    /// <summary>El fondo y el marco. Los campos son nodos y se dibujan encima.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto), Palette.BackgroundMain);

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
        if (!Validar())
            return;

        Ocupado(true, Loc.Get("ui.login.entrando"));

        var (sesion, error) = await Red.ApiCliente.LoginAsync(_nick.Text.Trim(), _password.Text);

        Terminar(sesion is not null, error);
    }

    private async Task RegistrarAsync()
    {
        if (!Validar())
            return;

        Ocupado(true, Loc.Get("ui.login.creando"));

        var (sesion, error) = await Red.ApiCliente.RegistrarAsync(_nick.Text.Trim(), _password.Text, null);

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
    private bool Validar()
    {
        string nick = _nick.Text.Trim();

        if (nick.Length is < 3 or > 20 || nick.Contains(' ', System.StringComparison.Ordinal))
        {
            Mensaje(Loc.Get("ui.login.nombre_invalido"));
            return false;
        }

        if (_password.Text.Length < 8)
        {
            Mensaje(Loc.Get("ui.login.pass_corta"));
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
            Entro?.Invoke();
            return;
        }

        Mensaje(error ?? Loc.Get("ui.login.error_entrar"));
    }

    private void Mensaje(string texto)
    {
        _mensaje.AddThemeColorOverride("font_color", Palette.Danger);
        _mensaje.Text = texto;
    }
}
