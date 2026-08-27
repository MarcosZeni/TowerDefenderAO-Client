using Godot;
using TowerOfArgentum.Simulacion;

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
public partial class PantallaFinDeCorrida : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;

    private bool _reportado;
    private string _estadoEnvio = "";
    private long _oroGanado = -1;

    public PantallaFinDeCorrida(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
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
        if (!Red.ApiCliente.Logueado || _juego.RunId <= 0)
        {
            _estadoEnvio = Loc.Get("ui.fin.corrida_local");
            return;
        }

        _estadoEnvio = Loc.Get("ui.fin.reportando");

        byte[] log = BinaryLog.Escribir(_juego.Log);

        var (ok, datos, error) = await Red.ApiCliente.TerminarCorridaAsync(
            _juego.RunId,
            partida.State == RunState.Won,
            partida.Wave,
            CargadorDatos.Actual.Areas.OfWave(partida.Wave),
            partida.Tick,
            log);

        if (ok && datos is not null)
        {
            _oroGanado = datos.GoldEarned;
            _estadoEnvio = $"{Loc.Get("ui.fin.billetera")} {datos.GoldTotal}";
            Guardado.Borrar();
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

        DrawRect(new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto), new Color(0, 0, 0, 0.62f));

        var panel = new Rect2(Escala.LienzoAncho / 2 - 420, Escala.LienzoAlto / 2 - 220, 840, 440);
        var acento = gano ? new Color(0.4f, 0.95f, 0.5f) : new Color(0.92f, 0.35f, 0.32f);

        DrawRect(panel, new Color(0.07f, 0.07f, 0.10f, 0.97f));
        DrawRect(panel, acento, filled: false, width: 3f);
        DrawRect(new Rect2(panel.Position.X, panel.Position.Y, panel.Size.X, 8), acento);

        float x = panel.Position.X + 60;
        float y = panel.Position.Y + 100;

        Text(Loc.Get(gano ? "ui.fin.ganaste" : "ui.fin.cayo_la_torre"), x, y, 54, acento);

        y += 70;

        if (gano)
        {
            Text(Loc.Get("ui.fin.mataste_al_jefe"), x, y, 22, Blanco);
        }
        else
        {
            // "Llegaste a la N de 100" y no solo "oleada N": el 100 es el final
            // real de la corrida, y verlo dice cuánto falta.
            Text($"{Loc.Get("ui.fin.llegaste_a_la_oleada")} {partida.Wave} " +
                 $"{Loc.Get("ui.fin.de")} {CargadorDatos.Actual.Curves.RunCurve.WavesPerRun}.",
                x, y, 22, Blanco);
        }

        y += 60;

        int segundos = partida.Tick / Ints.TicksPerSecond;

        Text($"{Loc.Get("ui.fin.area")} {CargadorDatos.Actual.Areas.OfWave(partida.Wave)} · " +
              $"{segundos / 60:00}:{segundos % 60:00} · {partida.Tick} {Loc.Get("ui.fin.ticks")}",
            x, y, 21, Gris);

        y += 34;

        // Los ticks son el desempate del ranking, así que se dice para qué sirven.
        Text(Loc.Get("ui.fin.desempate"), x, y, 17, Gris);

        y += 62;

        if (_oroGanado >= 0)
        {
            Text($"+{_oroGanado} {Loc.Get("ui.fin.de_oro_a_la_billetera")}",
                x, y, 26, new Color(0.98f, 0.85f, 0.35f));
            y += 34;
        }

        Text(_estadoEnvio, x, y, 18, Gris);

        Text(Loc.Get("ui.fin.esc_volver"),
            panel.Position.X + 60, panel.Position.Y + panel.Size.Y - 40, 18, Gris);
    }

    private static readonly Color Blanco = new(0.93f, 0.93f, 0.91f);
    private static readonly Color Gris = new(0.62f, 0.62f, 0.60f);

    private void Text(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
