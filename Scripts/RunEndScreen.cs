using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El cartel de fin de corrida, sobre el mundo congelado.
/// </summary>
/// <remarks>
/// <para>No es una pantalla aparte: es un panel encima de lo que quedó. Ver el
/// campo de batalla en el momento en que cayó la Torre —o en el que murió el jefe
/// final— es parte de lo que hace que el final signifique algo.</para>
///
/// <para>Es también donde se cierra la corrida contra el servidor:
/// <c>POST /run/finish</c> con el log. <b>El oro que va a la billetera lo calcula
/// el servidor</b>, así que este panel lo muestra recién cuando contesta.</para>
/// </remarks>
public partial class RunEndScreen : Control
{
    private readonly Game _juego;

    private bool _reportado;
    private string _estadoEnvio = "";
    private long _oroGanado = -1;

    /// <summary>El jugador pidió volver al menú con un click o un toque. Lo escucha <c>Main</c>.</summary>
    public event System.Action? Dismissed;

    /// <summary>Cuándo apareció el cartel, en milisegundos del motor. 0 = todavía no.</summary>
    private ulong _aparecioMs;

    /// <summary>
    /// Cuánto tiene que estar a la vista el cartel antes de que un toque lo cierre.
    /// </summary>
    /// <remarks>
    /// Sin esto, el jugador que está tocando el tablero justo cuando cae la Torre
    /// sale al menú sin llegar a ver el resultado de horas de corrida.
    /// </remarks>
    private const ulong MsAntesDeSalir = 1500;

    public RunEndScreen(Game juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        var partida = _juego.Run;
        bool termino = partida is not null && partida.State != RunState.InProgress;

        Visible = termino;

        if (!termino)
            return;

        if (!_reportado)
        {
            _reportado = true;
            _aparecioMs = Time.GetTicksMsec();
            _ = ReportarAsync(partida!);
        }

        QueueRedraw();
    }

    /// <summary>
    /// Un click o un toque, soltado, vuelve al menú.
    /// </summary>
    /// <remarks>
    /// ⚠ Antes la única salida era ESC, y en el celular no hay ESC: la corrida
    /// terminaba y la pantalla quedaba trabada. Va en <c>_UnhandledInput</c> para
    /// que los carteles de ayuda y los botones de arriba sigan atendiendo primero.
    /// </remarks>
    public override void _UnhandledInput(InputEvent evento)
    {
        if (!Visible || _aparecioMs == 0)
            return;

        if (evento is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            return;

        if (Time.GetTicksMsec() - _aparecioMs < MsAntesDeSalir)
            return;

        GetViewport().SetInputAsHandled();
        Dismissed?.Invoke();
    }

    /// <summary>
    /// Manda el resultado al servidor.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No manda oro ni precios.</b> Reporta hasta dónde llegó y el
    /// servidor deriva <c>oroGanado = f(gano, oleada)</c>. La billetera compra
    /// planos, que son progresión permanente: oro falso se volvería poder real, y
    /// la columna <c>validado</c> no sirve de freno porque el oro se gasta al
    /// instante.</para>
    ///
    /// <para>Si falla, el guardado local <b>no se borra</b>: la corrida se puede
    /// reintentar más tarde en vez de perderse por un problema de red.</para>
    /// </remarks>
    private async System.Threading.Tasks.Task ReportarAsync(Run partida)
    {
        if (!Net.ApiClient.LoggedIn || _juego.RunId <= 0)
        {
            _estadoEnvio = Loc.Get("ui.fin.corrida_local");
            return;
        }

        _estadoEnvio = Loc.Get("ui.fin.reportando");

        byte[] log = BinaryLog.Write(_juego.Log);

        var (ok, datos, error) = await Net.ApiClient.FinishRunAsync(
            _juego.RunId,
            partida.State == RunState.Won,
            partida.Wave,
            DataLoader.Current.Areas.OfWave(partida.Wave),
            partida.Tick,
            log);

        if (ok && datos is not null)
        {
            _oroGanado = datos.GoldEarned;
            _estadoEnvio = $"{Loc.Get("ui.fin.billetera")} {datos.GoldTotal}";
            SaveFile.Delete();
        }
        else
        {
            // El guardado queda: la corrida se puede reintentar. Perderla por un
            // corte de red sería lo peor que puede pasar después de horas de
            // juego.
            _estadoEnvio = error ?? Loc.Get("ui.fin.no_se_pudo_reportar");
        }
    }

    public override void _Draw()
    {
        var partida = _juego.Run;
        if (partida is null || partida.State == RunState.InProgress)
            return;

        bool gano = partida.State == RunState.Won;

        // ⚠ Esta pantalla también explica, y es donde más falta hace: es el único
        // momento en que el jugador ve las tres cifras con las que lo va a ordenar
        // el ranking. Entender acá por qué quedó donde quedó es lo que hace que la
        // próxima corrida se juegue distinto.
        UiTooltip.Begin(GetGlobalMousePosition());

        this.Scrim();

        var panel = new Rect2((Canvas.CanvasWidth / 2f) - 420f, (Canvas.CanvasHeight / 2f) - 220f,
            840f, 440f);

        var acento = gano ? Palette.Success : Palette.Danger;

        this.Panel(panel, elevado: true, opacidad: 1f);
        this.Glow(panel, acento, 2f, 6);

        DrawRect(panel, acento, filled: false, width: 3f);
        DrawRect(panel.Row(0f, 6f), acento);

        // La cruz vuelve al menú. Tocar en cualquier lado ya lo hacía, pero eso no
        // se ve: una ventana sin botón de cerrar se lee como trabada.
        this.CloseButton(UiStyle.CloseRectFor(panel, 14f));

        this.Label(panel.Row(60f, 60f), Loc.Get(gano ? "ui.fin.ganaste" : "ui.fin.cayo_la_torre"),
            52, acento, HorizontalAlignment.Center);

        // "Llegaste a la N de 100" y no solo "oleada N": el 100 es el final real
        // de la corrida, y verlo dice cuánto falta.
        this.Label(panel.Row(130f, 30f),
            gano
                ? Loc.Get("ui.fin.mataste_al_jefe")
                : $"{Loc.Get("ui.fin.llegaste_a_la_oleada")} {partida.Wave} " +
                  $"{Loc.Get("ui.fin.de")} {DataLoader.Current.Curves.RunCurve.WavesPerRun}.",
            22, Palette.TextPrimary, HorizontalAlignment.Center);

        this.Rule(panel, 180f);

        // Las tres cifras que el ranking mira, en columnas: se comparan con las
        // de la corrida anterior sin leer una frase.
        int segundos = partida.Tick / Ints.TicksPerSecond;

        Cifra(0, Loc.Get("ui.fin.area"),
            DataLoader.Current.Areas.OfWave(partida.Wave).ToString(), Palette.TextPrimary,
            "ui.ayuda.fin_area");

        Cifra(1, Loc.Get("ui.preparacion.wave"), partida.Wave.ToString(), Palette.TextPrimary,
            "ui.ayuda.fin_oleada");

        Cifra(2, Loc.Get("ui.fin.ticks"), $"{segundos / 60:00}:{segundos % 60:00}", Palette.Info,
            "ui.ayuda.fin_ticks");

        // Los ticks son el desempate del ranking, así que se dice para qué sirven.
        var desempate = panel.Row(300f, 24f);

        this.Label(desempate, Loc.Get("ui.fin.desempate"), 16, Palette.TextMuted,
            HorizontalAlignment.Center);

        UiTooltip.Help(desempate, "ui.ayuda.ranking");

        if (_oroGanado >= 0)
        {
            var oro = new Rect2(panel.Position.X, panel.Position.Y + 334f, panel.Size.X, 30f);

            this.Price(oro, (int)_oroGanado, alcanza: true, HorizontalAlignment.Center);

            this.Label(panel.Row(366f, 24f), Loc.Get("ui.fin.de_oro_a_la_billetera"), 16,
                Palette.Gold, HorizontalAlignment.Center);

            // ⚠ Este oro SÍ es permanente, al revés del de la corrida. Es la
            // única vez que los dos oros aparecen cerca, y confundirlos es lo que
            // hace que alguien guarde oro adentro de la corrida creyendo que se
            // lo lleva.
            UiTooltip.Over(oro, UiStyle.Thousands((int)_oroGanado), "ui.ayuda.oro_ganado");
        }

        this.Label(panel.Row(panel.Size.Y - 62f, 24f), _estadoEnvio, 16, Palette.TextMuted,
            HorizontalAlignment.Center);

        this.Label(panel.Row(panel.Size.Y - 36f, 24f),
            Loc.Get(Device.IsTouch ? "ui.fin.tocar_volver" : "ui.fin.esc_volver"), 15,
            Palette.TextDisabled, HorizontalAlignment.Center);

        // Último de todo, como en el resto de las pantallas: el cartel va encima.
        UiTooltip.Draw(this);

        void Cifra(int i, string etiqueta, string valor, Color color, string ayuda)
        {
            float ancho = panel.Size.X / 3f;
            var celda = new Rect2(panel.Position.X + (i * ancho), panel.Position.Y + 200f, ancho, 90f);

            this.Label(celda.Row(0f, 22f), etiqueta.ToUpperInvariant(), 14, Palette.TextMuted,
                HorizontalAlignment.Center);

            this.Label(celda.Row(24f, 46f), valor, 40, color, HorizontalAlignment.Center);

            UiTooltip.Over(celda, $"{etiqueta}: {valor}", ayuda);
        }
    }
}
