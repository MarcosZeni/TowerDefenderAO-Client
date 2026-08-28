using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// El panel de un defensor seleccionado: a quién apunta, y qué está haciendo.
/// </summary>
/// <remarks>
/// <para><b>El targeting es la mecánica de más profundidad por menos complejidad
/// del género</b>, y no toca una sola estadística: cambia tácticamente el
/// resultado con los mismos números. Bloons usa First / Last / Close / Strong
/// desde hace años exactamente por eso.</para>
///
/// <para>⚠ <b>Es del DEFENSOR, no de la clase</b>, y es lo único del juego que lo
/// es. Dos paladines de la misma ficha quieren targeting distinto según dónde
/// estén parados —el de la entrada abre la oleada, el de la Torre contiene
/// filtraciones—, y esa decisión de colocación es la mitad del valor de la
/// mecánica. Por eso el panel se abre clickeando <b>una unidad</b> y no una clase
/// de la barra.</para>
///
/// <para>⚠ <b>Y muestra los estados de soporte.</b> Un aura que no se ve no es una
/// mecánica: es una casualidad. Si un número importa para tomar una decisión,
/// ocultarlo convierte la dificultad en adivinanza en lugar de estrategia — que es
/// el principio de «información total» de Defender's Quest, y es la razón por la
/// que bardo y clérigo se sentían inertes.</para>
/// </remarks>
public partial class PantallaDefensor : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;
    private int _bajoElCursor = -1;

    private const int Ancho = 340;
    private const int Alto = 268;
    private const int AltoFila = 30;

    /// <summary>Los cuatro modos, en el orden en que se muestran y se numeran.</summary>
    /// <remarks>
    /// ⚠ Cuatro y no diez. Con diez modos la pantalla se vuelve un menú que hay que
    /// estudiar, y la mitad no cambian nada en la práctica.
    /// </remarks>
    private static readonly TargetingMode[] _modos =
    [
        TargetingMode.First,
        TargetingMode.Strongest,
        TargetingMode.Weakest,
        TargetingMode.NearestTower,
    ];

    public PantallaDefensor(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Ignore: el panel no es modal. Sus clicks se atienden en _Input y todo lo
        // demás sigue llegando al mapa.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>El defensor abierto, o <c>null</c>.</summary>
    /// <remarks>
    /// Se resuelve por id en cada cuadro y no se guarda la referencia: si el
    /// jugador vende el defensor con el panel abierto, el id deja de encontrar a
    /// nadie y el panel se cierra solo.
    /// </remarks>
    private Defender? Abierto()
    {
        var partida = _juego.Run;

        if (partida is null || partida.State != RunState.InProgress || _juego.DefensorSeleccionado < 0)
            return null;

        foreach (var d in partida.Defenders)
        {
            if (d.PlacementId == _juego.DefensorSeleccionado)
                return d;
        }

        return null;
    }

    public override void _Process(double delta)
    {
        Visible = Abierto() is not null;

        if (Visible)
            QueueRedraw();
    }

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la cicatriz de siempre: con el
    /// hook de GUI el control queda sordo porque nunca toma el foco, y la posición
    /// del evento viene en píxeles de ventana, que con <c>stretch = keep</c> no
    /// coincide con el lienzo.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        if (Abierto() is not { } defensor)
            return;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = FilaEn(GetGlobalMousePosition());
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                int i = FilaEn(GetGlobalMousePosition());

                // Solo se marca atendido el click que cayó en una fila: el resto
                // tiene que seguir llegando al mapa, incluido el que cierra este
                // panel clickeando otra celda.
                if (i >= 0)
                {
                    _juego.CambiarTargeting(defensor.PlacementId, _modos[i]);
                    GetViewport().SetInputAsHandled();
                }

                break;
            }

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                _juego.DefensorSeleccionado = -1;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private static Rect2 Caja()
        => new(Escala.LienzoAncho - Ancho - 24, 96, Ancho, Alto);

    private static Rect2 FilaDe(int i)
    {
        var c = Caja();
        return new Rect2(c.Position.X + 12, c.Position.Y + 118 + (i * AltoFila), c.Size.X - 24, AltoFila - 4);
    }

    private static int FilaEn(Vector2 posicion)
    {
        for (int i = 0; i < _modos.Length; i++)
        {
            if (FilaDe(i).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    public override void _Draw()
    {
        if (Abierto() is not { } defensor)
            return;

        var partida = _juego.Run!;
        var stats = partida.Roster.StatsOf(defensor.DefenderClass, partida.Backpack);
        var caja = Caja();

        DrawRect(caja, new Color(0.07f, 0.08f, 0.12f, 0.95f));
        DrawRect(caja, new Color(0.85f, 0.72f, 0.30f, 0.55f), filled: false, width: 2f);

        float x = caja.Position.X + 16;
        float y = caja.Position.Y + 32;

        Text(Loc.Class(defensor.DefenderClass), x, y, 26, new Color(0.95f, 0.95f, 0.92f));

        // ⚠ En la escala del JUGADOR, no en la interna. Adentro el daño va en
        // centésimas de punto y el alcance en milésimas de celda: mostrarlos crudos
        // diría «1040 de daño» cuando el defensor pega 10,4.
        Text($"{Loc.Damage(stats.Damage)} · {Loc.Range(stats.RangeMilli)} · {stats.CadenceTicks}t",
            x, y + 26, 17, new Color(0.68f, 0.70f, 0.74f));

        DibujarSoporte(defensor, stats, x, y + 52);

        Text(Loc.Get("ui.defensor.apunta_a"), x, caja.Position.Y + 106, 16, new Color(0.60f, 0.64f, 0.70f));

        for (int i = 0; i < _modos.Length; i++)
            DibujarModo(i, defensor.Targeting == _modos[i]);

        Text(Loc.Get("ui.defensor.cerrar"),
            x, caja.Position.Y + caja.Size.Y - 12, 14, new Color(0.48f, 0.51f, 0.56f));
    }

    private void DibujarModo(int i, bool activo)
    {
        var r = FilaDe(i);
        bool resaltado = i == _bajoElCursor;

        if (activo)
        {
            DrawRect(r, new Color(0.20f, 0.32f, 0.22f));
            DrawRect(r, new Color(0.45f, 0.85f, 0.55f), filled: false, width: 2f);
        }
        else if (resaltado)
        {
            DrawRect(r, new Color(0.16f, 0.17f, 0.22f));
        }

        Text(Loc.Get($"ui.targeting.{_modos[i]}"),
            r.Position.X + 12, r.Position.Y + 20, 19,
            activo ? new Color(0.85f, 0.98f, 0.88f) : new Color(0.80f, 0.82f, 0.86f));
    }

    /// <summary>
    /// Lo que este defensor está recibiendo o dando de soporte, ahora.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Sin esto, bardo y clérigo son fe.</b> Su valor entero está en lo
    /// que le hacen al resto del equipo, y si eso no se ve en ningún lado el
    /// jugador no tiene forma de saber si poner el segundo bardo sirvió o no
    /// sirvió. Un aura invisible no es una mecánica sutil: es una casualidad.</para>
    ///
    /// <para>Es el principio de «información total»: si un número importa para
    /// tomar una decisión, ocultarlo convierte la dificultad en adivinanza en lugar
    /// de estrategia.</para>
    /// </remarks>
    private void DibujarSoporte(Defender defensor, EffectiveStats stats, float x, float y)
    {
        var partida = _juego.Run!;
        var combate = CargadorDatos.Actual.Combat;

        // Lo que ESTE defensor emite, si su comportamiento es un aura.
        if (stats.Behavior is PerkEffect.Inspire or PerkEffect.DivineStorm or PerkEffect.Blindness)
        {
            int radio = combate.States.InspireRadiusCells;
            int alcanzados = 0;

            foreach (var otro in partida.Defenders)
            {
                if (!ReferenceEquals(otro, defensor)
                    && Ints.SquaredDistance(defensor.Cell.X, defensor.Cell.Y, otro.Cell.X, otro.Cell.Y)
                       <= (long)radio * radio)
                {
                    alcanzados++;
                }
            }

            Text($"{Loc.Effect(stats.Behavior)}  ·  {Loc.Get("ui.defensor.radio")} {radio}  ·  " +
                 $"{alcanzados} {Loc.Get("ui.defensor.aliados")}",
                x, y, 16, new Color(0.72f, 0.88f, 0.72f));

            y += 22;
        }

        // Y lo que está RECIBIENDO, con lo que le queda. Los décimos de segundo se
        // leen mejor que los ticks: el jugador no piensa en 30 Hz.
        if (defensor.CadenceBuffPercent > 0 && partida.Tick < defensor.CadenceBuffExpiryTick)
        {
            int decimas = (defensor.CadenceBuffExpiryTick - partida.Tick) * 10 / Ints.TicksPerSecond;

            Text($"{Loc.Get("ui.defensor.cadencia")} +{defensor.CadenceBuffPercent}%  ·  " +
                 $"{decimas / 10}.{decimas % 10}s",
                x, y, 16, new Color(0.85f, 0.90f, 0.60f));

            y += 22;
        }

        // ⚠ Y el conflicto de comportamiento, que antes era MUDO: un Oro raro
        // quedaba anulado por equipar un arma y el jugador no se enteraba nunca.
        // Ahora se puede elegir cuál habla, y esto es lo que dice cuál está
        // hablando.
        if (partida.HayConflictoDeComportamiento(defensor.DefenderClass))
        {
            bool ganaElPerk = partida.Roster.Find(defensor.DefenderClass)?.PreferPerkBehavior == true;

            Text(Loc.Get(ganaElPerk ? "ui.defensor.manda_el_perk" : "ui.defensor.manda_el_arma"),
                x, y, 15, new Color(0.95f, 0.78f, 0.45f));
        }
    }

    private void Text(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
