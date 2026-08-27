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

        var titulo = new Label { Text = Loc.Get("ui.principal.titulo") };
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
        _continuar = Boton(Loc.Get("ui.principal.continuar"), () => ContinuarCorrida?.Invoke());
        caja.AddChild(_continuar);

        _jugar = Boton(Loc.Get("ui.principal.jugar"), () => _ = JugarAsync());
        caja.AddChild(_jugar);

        caja.AddChild(Boton(Loc.Get("ui.principal.tienda"), () => _ = MostrarTiendaAsync()));
        caja.AddChild(Boton(Loc.Get("ui.principal.ranking"), () => _ = MostrarRankingAsync()));
        caja.AddChild(Boton(Loc.Get("ui.principal.opciones"), () => _opciones.Abrir()));
        caja.AddChild(Boton(Loc.Get("ui.principal.salir_cuenta"), () => _ = SalirAsync()));

        // ⚠ Cerrar el juego desde el menú. Antes solo se salía con la X o con
        // Alt+F4; que un juego de pantalla completa no tenga salida propia es de
        // las cosas que más rápido se notan.
        caja.AddChild(Boton(Loc.Get("ui.principal.salir_juego"), () => GetTree().Quit()));

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
            _billetera.Text = Loc.Get("ui.principal.sin_conexion");
            _mensaje.Text = error ?? "";
            return;
        }

        _oro = perfil.Gold;
        _planos = perfil.Blueprints;

        _billetera.Text =
            $"{perfil.Nick} · {perfil.Gold} {Loc.Get("ui.principal.de_oro")}" +
            $" · {perfil.Blueprints.Length} {Loc.Get("ui.principal.planos")}";
        _mensaje.Text = "";
    }

    private async Task JugarAsync()
    {
        _jugar.Disabled = true;
        _mensaje.Text = Loc.Get("ui.principal.abriendo");

        // ⚠ El hash de los datos viaja acá. Si el cliente juega con otros números
        // que el servidor, la corrida no sería reproducible ni comparable, y es
        // infinitamente mejor rechazarla ahora que después de tres horas.
        var (ok, datos, error) = await Red.ApiCliente.IniciarCorridaAsync(CargadorDatos.Actual.Hash);

        _jugar.Disabled = false;

        if (!ok || datos is null)
        {
            _mensaje.Text = error ?? Loc.Get("ui.principal.error_abrir");
            return;
        }

        if (datos.Abandoned is > 0)
        {
            // Una sola corrida abierta por cuenta: abrir esta cerró la anterior.
            // Se dice, porque pueden haber sido horas de partida.
            _mensaje.Text = Loc.Get("ui.principal.corrida_abandonada", (int)datos.Abandoned.Value);
        }

        EmpezarCorrida?.Invoke(datos.RunId, datos.Seed);
    }

    private async Task MostrarRankingAsync()
    {
        Limpiar();
        _mensaje.Text = Loc.Get("ui.principal.cargando_ranking");

        var (ok, datos, error) = await Red.ApiCliente.RankingAsync(20);

        if (!ok || datos is null)
        {
            _mensaje.Text = error ?? Loc.Get("ui.principal.error_ranking");
            return;
        }

        _mensaje.Text = "";
        _panel.AddChild(Titulo(Loc.Get("ui.principal.ranking_temporada", datos.Version)));

        // El orden es gano DESC, oleada DESC, ticks ASC: arriba los que ganaron y
        // entre ellos el más rápido. Se muestran las dos columnas crudas, sin
        // fórmula compuesta que haya que explicar.
        foreach (var fila in datos.Filas)
        {
            int segundos = fila.Ticks / Simulacion.Ints.TicksPerSecond;

            var l = new Label
            {
                Text = $"{fila.Position,3}. {fila.Nick,-20} " +
                       $"{(fila.Won ? Loc.Get("ui.principal.gano") : Loc.Get("ui.principal.oleada_corta", fila.Wave)),-8} " +
                       $"{segundos / 60:00}:{segundos % 60:00}" +
                       (fila.Validated ? "  ✓" : ""),
            };

            if (fila.Won)
                l.AddThemeColorOverride("font_color", new Color(0.5f, 0.95f, 0.6f));

            _panel.AddChild(l);
        }

        if (datos.Filas.Length == 0)
            _panel.AddChild(new Label { Text = Loc.Get("ui.principal.sin_corridas") });
    }

    private async Task MostrarTiendaAsync()
    {
        Limpiar();
        await RefrescarAsync();

        _panel.AddChild(Titulo(Loc.Get("ui.principal.tienda")));

        _panel.AddChild(new Label
        {
            Text = Loc.Get("ui.principal.tienda_ayuda"),
        });

        _panel.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        foreach (var plano in CargadorDatos.Actual.Blueprints.All)
        {
            // ⚠ La identidad que viaja a la API es la KEY, no el nombre: el nombre
            // ahora vive en los archivos de idioma y cambia con el idioma, así que
            // un jugador en inglés compraría un plano que el servidor no conoce.
            // El planos.json DEL SERVIDOR tiene que listar estas mismas keys.
            bool aprendido = System.Array.IndexOf(_planos, plano.Key) >= 0;
            string nombreVisible = Loc.Get($"perk.{plano.Key}.name");

            // El inicial se muestra igual, marcado: sirve para que el jugador
            // entienda que la fusión existe antes de gastar un peso.
            if (plano.Initial)
            {
                _panel.AddChild(new Label { Text = $"  {nombreVisible} {Loc.Get("ui.principal.plano_inicial")}" });
                continue;
            }

            if (aprendido)
            {
                _panel.AddChild(new Label { Text = $"  {nombreVisible} {Loc.Get("ui.principal.plano_aprendido")}" });
                continue;
            }

            var fila = new HBoxContainer();
            fila.AddThemeConstantOverride("separation", 12);

            fila.AddChild(new Label { Text = $"  {nombreVisible}" });

            string clave = plano.Key;
            var comprar = new Button { Text = Loc.Get("ui.principal.comprar"), CustomMinimumSize = new Vector2(140, 40) };
            comprar.Pressed += () => _ = ComprarAsync(clave);
            fila.AddChild(comprar);

            _panel.AddChild(fila);
        }
    }

    private async Task ComprarAsync(string plano)
    {
        _mensaje.Text = Loc.Get("ui.principal.comprando");

        // ⚠ Se manda SOLO el nombre. El precio sale de la lista del servidor: si
        // lo mandara el cliente, se compraría todo a un peso.
        var (ok, _, error) = await Red.ApiCliente.ComprarPlanoAsync(plano);

        _mensaje.Text = ok
            ? $"{Loc.Get("ui.principal.aprendiste")} {plano}"
            : error ?? Loc.Get("ui.principal.error_comprar");

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
