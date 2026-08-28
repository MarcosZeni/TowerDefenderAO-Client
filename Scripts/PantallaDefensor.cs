using Godot;
using TowerOfArgentum.Simulacion;
using TowerOfArgentum.Ui;

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
    private int _bajoElCursor = -1;

    /// <summary>Alto de cada fila de modo de targeting.</summary>
    private const int AltoFila = 34;

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
        UiStyle.Init();
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

    /// <summary>
    /// El panel, en la <b>columna de detalle</b> del diseño.
    /// </summary>
    /// <remarks>
    /// Es la columna que el diseño reserva para «lo que está seleccionado», y un
    /// defensor clickeado es exactamente eso. No usa el alto entero de la columna
    /// porque el contenido es corto: un panel de 700 px con 300 de texto adentro
    /// se lee como un error de layout.
    /// </remarks>
    private static Rect2 Caja()
        => new(UiLayout.DetailPanel.Position, new Vector2(UiLayout.DetailPanel.Size.X, 404f));

    private static Rect2 FilaDe(int i)
    {
        var c = Caja();

        return new Rect2(
            c.Position.X + 14f, c.Position.Y + 190f + (i * AltoFila),
            c.Size.X - 28f, AltoFila - 5f);
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

        this.Panel(caja);

        var titulo = caja.Row(12f, 32f);

        this.Label(titulo, Loc.Class(defensor.DefenderClass).ToUpperInvariant(), 24,
            Palette.TextPrimary, HorizontalAlignment.Center);

        this.Label(caja.Row(42f, 22f), Loc.Role(defensor.DefenderClass), 14, Palette.TextMuted,
            HorizontalAlignment.Center);

        this.Rule(caja, 68f);

        // ⚠ En la escala del JUGADOR, no en la interna. Adentro el daño va en
        // centésimas de punto y el alcance en milésimas de celda: mostrarlos crudos
        // diría «1040 de daño» cuando el defensor pega 10,4.
        var stat = new Rect2(caja.Position.X + 16f, caja.Position.Y + 76f, caja.Size.X - 32f, 20f);

        this.StatRow(stat, Loc.Get("ui.mochila.dano"), Loc.Damage(stats.Damage));
        this.StatRow(stat.Row(20f, 20f), Loc.Get("ui.mochila.alcance"), Loc.Range(stats.RangeMilli));
        this.StatRow(stat.Row(40f, 20f), Loc.Get("ui.mochila.cada"),
            $"{stats.CadenceTicks} {Loc.Get("ui.mochila.ticks")}");

        DibujarSoporte(defensor, stats, caja.Position.X + 16f, caja.Position.Y + 142f);

        this.Label(new Rect2(caja.Position.X + 16f, caja.Position.Y + 166f, caja.Size.X - 32f, 22f),
            Loc.Get("ui.defensor.apunta_a").ToUpperInvariant(), 13, Palette.TextMuted);

        for (int i = 0; i < _modos.Length; i++)
            DibujarModo(i, defensor.Targeting == _modos[i]);

        this.Label(caja.Row(caja.Size.Y - 26f, 22f), Loc.Get("ui.defensor.cerrar"), 13,
            Palette.TextDisabled, HorizontalAlignment.Center);
    }

    private void DibujarModo(int i, bool activo)
    {
        var r = FilaDe(i);

        // El activo se rellena; el de abajo del cursor solo se ilumina. Son los
        // mismos dos estados del botón del diseño, en versión de fila.
        this.Button(r, Loc.Get($"ui.targeting.{_modos[i]}"),
            activo ? Palette.Success : Palette.BorderNormal,
            relleno: activo, encima: i == _bajoElCursor);
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

            Renglon($"{Loc.Effect(stats.Behavior)}  ·  {Loc.Get("ui.defensor.radio")} {radio}  ·  " +
                    $"{alcanzados} {Loc.Get("ui.defensor.aliados")}",
                Palette.Success);

            y += 22;
        }

        // Y lo que está RECIBIENDO, con lo que le queda. Los décimos de segundo se
        // leen mejor que los ticks: el jugador no piensa en 30 Hz.
        if (defensor.CadenceBuffPercent > 0 && partida.Tick < defensor.CadenceBuffExpiryTick)
        {
            int decimas = (defensor.CadenceBuffExpiryTick - partida.Tick) * 10 / Ints.TicksPerSecond;

            Renglon($"{Loc.Get("ui.defensor.cadencia")} +{defensor.CadenceBuffPercent}%  ·  " +
                    $"{decimas / 10}.{decimas % 10}s",
                Palette.Legendary);

            y += 22;
        }

        // ⚠ Y el conflicto de comportamiento, que antes era MUDO: un Oro raro
        // quedaba anulado por equipar un arma y el jugador no se enteraba nunca.
        // Ahora se puede elegir cuál habla, y esto es lo que dice cuál está
        // hablando.
        if (partida.HayConflictoDeComportamiento(defensor.DefenderClass))
        {
            bool ganaElPerk = partida.Roster.Find(defensor.DefenderClass)?.PreferPerkBehavior == true;

            Renglon(Loc.Get(ganaElPerk ? "ui.defensor.manda_el_perk" : "ui.defensor.manda_el_arma"),
                ganaElPerk ? Palette.Legendary : Palette.Epic);
        }

        void Renglon(string texto, Color color)
            => this.Label(new Rect2(x, y, Caja().Size.X - 32f, 22f), texto, 14, color);
    }
}
