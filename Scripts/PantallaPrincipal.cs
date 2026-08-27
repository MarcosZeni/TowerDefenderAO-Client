using System.Threading.Tasks;
using Godot;

namespace TowerOfArgentum;

/// <summary>
/// El menú fuera de la corrida: jugar, tienda de planos, ranking y billetera.
/// </summary>
/// <remarks>
/// Los cuatro paneles tocan el servidor, y esa es la diferencia con todo lo
/// demás: dentro de la corrida no hay red, todo pasa en el cliente. Acá es al
/// revés (INTERFAZ.md §4).
/// </remarks>
public partial class PantallaPrincipal : Control
{
    private Label _billetera = null!;
    private Label _mensaje = null!;
    private VBoxContainer _panel = null!;
    private Button _jugar = null!;

    private string[] _planos = [];
    private long _oro;

    /// <summary>Se dispara al empezar una corrida, con el id y la seed del servidor.</summary>
    public event System.Action<long, ulong>? EmpezarCorrida;

    /// <summary>Hay un guardado y el jugador quiere retomarlo.</summary>
    public event System.Action? ContinuarCorrida;

    private Button _continuar = null!;

    /// <summary>El panel de opciones. El mismo que abre el menú de ESC.</summary>
    private PantallaOpciones _opciones = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var caja = new VBoxContainer
        {
            Position = new Vector2(140, 140),
            CustomMinimumSize = new Vector2(700, 0),
        };

        caja.AddThemeConstantOverride("separation", 16);
        AddChild(caja);

        var titulo = new Label { Text = "Tower of Argentum" };
        titulo.AddThemeFontSizeOverride("font_size", 46);
        caja.AddChild(titulo);

        _billetera = new Label { Text = "…" };
        _billetera.AddThemeColorOverride("font_color", new Color(0.98f, 0.85f, 0.35f));
        _billetera.AddThemeFontSizeOverride("font_size", 24);
        caja.AddChild(_billetera);

        caja.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        // Continuar arriba de Jugar: si hay una corrida a medias, retomarla es
        // lo que el jugador vino a hacer — son horas de partida. Y Jugar con un
        // guardado presente ABANDONA esa corrida, así que el orden también avisa.
        _continuar = Boton("Continuar corrida", () => ContinuarCorrida?.Invoke());
        caja.AddChild(_continuar);

        _jugar = Boton("Jugar", () => _ = JugarAsync());
        caja.AddChild(_jugar);

        caja.AddChild(Boton("Tienda de planos", () => _ = MostrarTiendaAsync()));
        caja.AddChild(Boton("Ranking", () => _ = MostrarRankingAsync()));
        caja.AddChild(Boton("Opciones", () => _opciones.Abrir()));
        caja.AddChild(Boton("Salir de la cuenta", () => _ = SalirAsync()));

        // ⚠ Cerrar el juego desde el menú. Antes solo se salía con la X o con
        // Alt+F4; que un juego de pantalla completa no tenga salida propia es de
        // las cosas que más rápido se notan.
        caja.AddChild(Boton("Salir del juego", () => GetTree().Quit()));

        _mensaje = new Label { Text = "" };
        _mensaje.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.68f));
        caja.AddChild(_mensaje);

        // El panel de la derecha: acá se pinta el ranking o la tienda, sin
        // navegar a otra pantalla. Es lo que hace que las "cuatro pantallas" del
        // diseño sean en realidad una sola con paneles.
        _panel = new VBoxContainer
        {
            Position = new Vector2(900, 140),
            CustomMinimumSize = new Vector2(880, 0),
        };

        _panel.AddThemeConstantOverride("separation", 6);
        AddChild(_panel);

        // Último hijo: su _Input corre antes que el del resto y queda dibujado
        // encima de los botones, que es lo que un panel modal tiene que hacer.
        _opciones = new PantallaOpciones { Name = "Opciones" };
        AddChild(_opciones);
    }

    public override void _Notification(int que)
    {
        // Al volver de una corrida, la billetera cambió: se recarga sola en vez
        // de mostrar un número viejo.
        if (que == NotificationVisibilityChanged && Visible)
            _ = RefrescarAsync();
    }

    private static Button Boton(string texto, System.Action alApretar)
    {
        var b = new Button { Text = texto, CustomMinimumSize = new Vector2(420, 56) };
        b.Pressed += alApretar;
        return b;
    }

    public async Task RefrescarAsync()
    {
        // El botón de continuar existe solo mientras exista el guardado.
        _continuar.Visible = Guardado.Existe();

        var (ok, perfil, error) = await Red.ApiCliente.PerfilAsync();

        if (!ok || perfil is null)
        {
            _billetera.Text = "sin conexión con el servidor";
            _mensaje.Text = error ?? "";
            return;
        }

        _oro = perfil.Oro;
        _planos = perfil.Planos;

        _billetera.Text = $"{perfil.Nick} · {perfil.Oro} de oro · {perfil.Planos.Length} plano(s)";
        _mensaje.Text = "";
    }

    private async Task JugarAsync()
    {
        _jugar.Disabled = true;
        _mensaje.Text = "abriendo la corrida…";

        // ⚠ El hash de los datos viaja acá. Si el cliente juega con otros números
        // que el servidor, la corrida no sería reproducible ni comparable, y es
        // infinitamente mejor rechazarla ahora que después de tres horas.
        var (ok, datos, error) = await Red.ApiCliente.IniciarCorridaAsync(CargadorDatos.Actual.Hash);

        _jugar.Disabled = false;

        if (!ok || datos is null)
        {
            _mensaje.Text = error ?? "No se pudo abrir la corrida.";
            return;
        }

        if (datos.Abandonada is > 0)
        {
            // Una sola corrida abierta por cuenta: abrir esta cerró la anterior.
            // Se dice, porque pueden haber sido horas de partida.
            _mensaje.Text = $"se abandonó la corrida {datos.Abandonada} que tenías abierta";
        }

        EmpezarCorrida?.Invoke(datos.RunId, datos.Seed);
    }

    private async Task MostrarRankingAsync()
    {
        Limpiar();
        _mensaje.Text = "cargando el ranking…";

        var (ok, datos, error) = await Red.ApiCliente.RankingAsync(20);

        if (!ok || datos is null)
        {
            _mensaje.Text = error ?? "No se pudo cargar el ranking.";
            return;
        }

        _mensaje.Text = "";
        _panel.AddChild(Titulo($"Ranking · temporada {datos.Version}"));

        // El orden es gano DESC, oleada DESC, ticks ASC: arriba los que ganaron y
        // entre ellos el más rápido. Se muestran las dos columnas crudas, sin
        // fórmula compuesta que haya que explicar.
        foreach (var fila in datos.Filas)
        {
            int segundos = fila.Ticks / Simulacion.Enteros.TicksPorSegundo;

            var l = new Label
            {
                Text = $"{fila.Puesto,3}. {fila.Nick,-20} " +
                       $"{(fila.Gano ? "GANÓ" : $"ol {fila.Oleada}"),-8} " +
                       $"{segundos / 60:00}:{segundos % 60:00}" +
                       (fila.Validado ? "  ✓" : ""),
            };

            if (fila.Gano)
                l.AddThemeColorOverride("font_color", new Color(0.5f, 0.95f, 0.6f));

            _panel.AddChild(l);
        }

        if (datos.Filas.Length == 0)
            _panel.AddChild(new Label { Text = "todavía no hay ninguna corrida terminada" });
    }

    private async Task MostrarTiendaAsync()
    {
        Limpiar();
        await RefrescarAsync();

        _panel.AddChild(Titulo("Tienda de planos"));

        _panel.AddChild(new Label
        {
            Text = "Los planos son la única progresión permanente, y la lista es finita.",
        });

        _panel.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        foreach (var plano in Simulacion.Planos.Todos)
        {
            bool aprendido = System.Array.IndexOf(_planos, plano.Nombre) >= 0;

            // El inicial se muestra igual, marcado: sirve para que el jugador
            // entienda que la fusión existe antes de gastar un peso.
            if (plano.Inicial)
            {
                _panel.AddChild(new Label { Text = $"  {plano.Nombre} — ya lo sabés (viene de arranque)" });
                continue;
            }

            if (aprendido)
            {
                _panel.AddChild(new Label { Text = $"  {plano.Nombre} — aprendido" });
                continue;
            }

            var fila = new HBoxContainer();
            fila.AddThemeConstantOverride("separation", 12);

            fila.AddChild(new Label { Text = $"  {plano.Nombre}" });

            string nombre = plano.Nombre;
            var comprar = new Button { Text = "comprar", CustomMinimumSize = new Vector2(140, 40) };
            comprar.Pressed += () => _ = ComprarAsync(nombre);
            fila.AddChild(comprar);

            _panel.AddChild(fila);
        }
    }

    private async Task ComprarAsync(string plano)
    {
        _mensaje.Text = "comprando…";

        // ⚠ Se manda SOLO el nombre. El precio sale de la lista del servidor: si
        // lo mandara el cliente, se compraría todo a un peso.
        var (ok, _, error) = await Red.ApiCliente.ComprarPlanoAsync(plano);

        _mensaje.Text = ok ? $"aprendiste {plano}" : error ?? "No se pudo comprar.";

        await MostrarTiendaAsync();
    }

    private async Task SalirAsync()
    {
        await Red.ApiCliente.LogoutAsync();
        GetTree().ReloadCurrentScene();
    }

    private static Label Titulo(string texto)
    {
        var l = new Label { Text = texto };
        l.AddThemeFontSizeOverride("font_size", 28);
        return l;
    }

    private void Limpiar()
    {
        foreach (var hijo in _panel.GetChildren())
            hijo.QueueFree();
    }
}
