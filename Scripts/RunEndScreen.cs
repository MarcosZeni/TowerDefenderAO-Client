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
            _ = ReportarAsync(partida!);
        }

        QueueRedraw();
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

        this.Scrim();

        var panel = new Rect2((Canvas.CanvasWidth / 2f) - 420f, (Canvas.CanvasHeight / 2f) - 220f,
            840f, 440f);

        var acento = gano ? Palette.Success : Palette.Danger;

        this.Panel(panel, elevado: true, opacidad: 1f);
        this.Glow(panel, acento, 2f, 6);

        DrawRect(panel, acento, filled: false, width: 3f);
        DrawRect(panel.Row(0f, 6f), acento);

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
            DataLoader.Current.Areas.OfWave(partida.Wave).ToString(), Palette.TextPrimary);

        Cifra(1, Loc.Get("ui.preparacion.wave"), partida.Wave.ToString(), Palette.TextPrimary);

        Cifra(2, Loc.Get("ui.fin.ticks"), $"{segundos / 60:00}:{segundos % 60:00}", Palette.Info);

        // Los ticks son el desempate del ranking, así que se dice para qué sirven.
        this.Label(panel.Row(300f, 24f), Loc.Get("ui.fin.desempate"), 16, Palette.TextMuted,
            HorizontalAlignment.Center);

        if (_oroGanado >= 0)
        {
            var oro = new Rect2(panel.Position.X, panel.Position.Y + 334f, panel.Size.X, 30f);

            this.Price(oro, (int)_oroGanado, alcanza: true, HorizontalAlignment.Center);

            this.Label(panel.Row(366f, 24f), Loc.Get("ui.fin.de_oro_a_la_billetera"), 16,
                Palette.Gold, HorizontalAlignment.Center);
        }

        this.Label(panel.Row(panel.Size.Y - 62f, 24f), _estadoEnvio, 16, Palette.TextMuted,
            HorizontalAlignment.Center);

        this.Label(panel.Row(panel.Size.Y - 36f, 24f), Loc.Get("ui.fin.esc_volver"), 15,
            Palette.TextDisabled, HorizontalAlignment.Center);

        void Cifra(int i, string etiqueta, string valor, Color color)
        {
            float ancho = panel.Size.X / 3f;
            var celda = new Rect2(panel.Position.X + (i * ancho), panel.Position.Y + 200f, ancho, 90f);

            this.Label(celda.Row(0f, 22f), etiqueta.ToUpperInvariant(), 14, Palette.TextMuted,
                HorizontalAlignment.Center);

            this.Label(celda.Row(24f, 46f), valor, 40, color, HorizontalAlignment.Center);
        }
    }
}
