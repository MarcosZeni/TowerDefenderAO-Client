using Godot;
using TowerOfArgentum.Simulation;
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
public partial class DefenderScreen : Control
{
    private readonly Game _juego;
    private int _bajoElCursor = -1;

    /// <summary>
    /// El botón de vender ya está armado y el próximo click vende.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Vender pasó a vivir acá porque antes lo hacía el BOTÓN DERECHO sobre
    /// el mapa</b>, sin confirmar y sin poder deshacer: el gesto más fácil de
    /// hacer sin querer borraba una unidad puesta. Acá hay que seleccionar la
    /// ficha, apuntar a un botón que dice cuánto devuelve, y apretarlo dos veces.
    ///
    /// Dos clicks en el mismo botón y no un modal: un cartel a pantalla completa
    /// para vender una ficha de 100 de oro es más ceremonia que la decisión.
    /// </remarks>
    private bool _ventaArmada;

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

    public DefenderScreen(Game juego)
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
    private Defender? IsOpen()
    {
        var partida = _juego.Run;

        if (partida is null || partida.State != RunState.InProgress || _juego.SelectedDefender < 0)
            return null;

        foreach (var d in partida.Defenders)
        {
            if (d.PlacementId == _juego.SelectedDefender)
                return d;
        }

        return null;
    }

    /// <summary>A quién estaba armada la venta, para desarmarla al cambiar de ficha.</summary>
    private int _armadaPara = -1;

    public override void _Process(double delta)
    {
        var abierto = IsOpen();

        Visible = abierto is not null;

        // Cambiar de ficha —o cerrar el panel— desarma la venta. Que quede armada
        // de una ficha para la siguiente es la forma exacta de vender la que no
        // era.
        if (abierto?.PlacementId != _armadaPara)
        {
            _ventaArmada = false;
            _armadaPara = abierto?.PlacementId ?? -1;
        }

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
        if (IsOpen() is not { } defensor)
            return;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = FilaEn(GetGlobalMousePosition());
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                var posicion = GetGlobalMousePosition();

                if (RectVender().HasPoint(posicion))
                {
                    if (_ventaArmada)
                    {
                        _juego.Sell(defensor.PlacementId);
                        _juego.SelectedDefender = -1;
                        _ventaArmada = false;
                    }
                    else
                    {
                        _ventaArmada = true;
                    }

                    GetViewport().SetInputAsHandled();
                    break;
                }

                // Cualquier otro click desarma la venta. Un botón que se queda
                // armado mientras el jugador hace otra cosa es una trampa.
                _ventaArmada = false;

                int i = FilaEn(posicion);

                // Solo se marca atendido el click que cayó en una fila: el resto
                // tiene que seguir llegando al mapa, incluido el que cierra este
                // panel clickeando otra celda.
                if (i >= 0)
                {
                    _juego.SetTargetingMode(defensor.PlacementId, _modos[i]);
                    GetViewport().SetInputAsHandled();
                }

                break;
            }

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                _juego.SelectedDefender = -1;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>
    /// El panel, en la <b>columna derecha del marco</b>.
    /// </summary>
    /// <remarks>
    /// <para>Es la pared que el diseño reserva para «lo que está seleccionado», y
    /// un defensor clickeado es exactamente eso. <b>Ya no la comparte con
    /// nadie</b>: la tienda se mudó a <see cref="MarketScreen"/>, que es una
    /// ventana propia, y esta columna quedó entera para el defensor.</para>
    ///
    /// <para>Usa la columna entera aunque el contenido sea corto. Un panel a
    /// media altura dejaría un hueco de pared vacía en el medio del marco, que
    /// se lee peor que un panel con aire abajo.</para>
    /// </remarks>
    private static Rect2 Caja() => UiLayout.RunSidePanel;

    /// <summary>Dónde arranca la lista de modos de targeting, adentro del panel.</summary>
    private const float YTargeting = 384f;

    private static Rect2 FilaDe(int i)
    {
        var c = Caja();

        return new Rect2(
            c.Position.X + 14f, c.Position.Y + YTargeting + (i * AltoFila),
            c.Size.X - 28f, AltoFila - 5f);
    }

    /// <summary>El botón de vender, abajo del panel y lejos de los de targeting.</summary>
    /// <remarks>
    /// Lejos a propósito: los cuatro de arriba se aprietan seguido y este borra
    /// una unidad. Pegados, la mano que viene de cambiar el targeting cae encima
    /// del que vende.
    /// </remarks>
    private static Rect2 RectVender()
    {
        var c = Caja();

        return new Rect2(c.Position.X + 14f, c.End.Y - 96f, c.Size.X - 28f, 44f);
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
        if (IsOpen() is not { } defensor)
            return;

        var partida = _juego.Run!;
        var stats = partida.Roster.StatsOf(defensor.DefenderClass, partida.Backpack, partida.CountUnitsPerClass());
        var caja = Caja();

        UiTooltip.Begin(GetGlobalMousePosition());

        this.Panel(caja);

        // El retrato arriba de todo: el jugador acaba de clickear una unidad del
        // mapa y lo primero que tiene que confirmar es cuál agarró. Con seis de
        // las nueve clases compartiendo muñeco en los tiers bajos, el nombre de
        // abajo es lo que desempata.
        var ficha = partida.Roster.Find(defensor.DefenderClass);

        if (ficha is not null)
        {
            var retrato = new Rect2(
                caja.Position.X + ((caja.Size.X - 120f) / 2f), caja.Position.Y + 14f, 120f, 120f);

            this.Portrait(retrato, ficha);
        }

        this.Label(caja.Row(140f, 32f), Loc.Class(defensor.DefenderClass).ToUpperInvariant(), 24,
            Palette.TextPrimary, HorizontalAlignment.Center);

        this.Label(caja.Row(172f, 22f), Loc.Role(defensor.DefenderClass), 14, Palette.TextMuted,
            HorizontalAlignment.Center);

        this.Rule(caja, 200f);

        // ⚠ En la escala del JUGADOR, no en la interna. Adentro el daño va en
        // centésimas de punto y el alcance en milésimas de celda: mostrarlos crudos
        // diría «1040 de daño» cuando el defensor pega 10,4.
        var stat = new Rect2(caja.Position.X + 16f, caja.Position.Y + 212f, caja.Size.X - 32f, 24f);

        // Los tres con icono y en un renglón: en una columna de 313 px, «alcance»
        // más el número ocupaban casi el ancho entero y el valor quedaba pegado
        // al borde.
        this.StatTrio(stat,
            Loc.Damage(stats.Damage), Loc.Range(stats.RangeMilli), stats.CadenceTicks.ToString());

        DibujarSoporte(defensor, stats, caja.Position.X + 16f, caja.Position.Y + 252f);

        var rotulo = new Rect2(caja.Position.X + 16f, caja.Position.Y + YTargeting - 26f,
            caja.Size.X - 32f, 22f);

        this.Label(rotulo, Loc.Get("ui.defensor.apunta_a").ToUpperInvariant(), 13,
            Palette.TextMuted);

        // ⚠ Es lo ÚNICO del juego que es de la ficha y no de la clase, y por eso
        // necesita cartel: todo lo demás que el jugador tocó hasta acá —nivel,
        // perks, arma— vale para las nueve fichas de esa clase a la vez. Dos
        // paladines de la misma clase quieren apuntar distinto según dónde estén
        // parados.
        UiTooltip.Help(rotulo, "ui.ayuda.targeting");

        for (int i = 0; i < _modos.Length; i++)
            DibujarModo(i, defensor.Targeting == _modos[i]);

        DibujarVender(partida, defensor);

        this.Label(caja.Row(caja.Size.Y - 26f, 22f), Loc.Get("ui.defensor.cerrar"), 13,
            Palette.TextDisabled, HorizontalAlignment.Center);

        UiTooltip.Draw(this);
    }

    /// <summary>
    /// El botón de vender, con lo que devuelve, en dos pasos.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Dice cuánto devuelve antes de apretarlo.</b> La devolución es
    /// del 60% del costo de colocación, y sin ese número vender es una apuesta:
    /// el jugador no puede saber si le conviene mover la ficha o cambiarla.</para>
    ///
    /// <para>Ahora que arrastrar mueve gratis, vender es para <b>cambiar de
    /// clase</b>, no para reubicar. El renglón de abajo lo dice, porque el jugador
    /// que viene del botón derecho va a seguir buscando acá la forma de acomodar
    /// el tablero.</para>
    /// </remarks>
    private void DibujarVender(Run partida, Defender defensor)
    {
        var r = RectVender();

        int costo = DataLoader.Current.StatsOf(defensor.DefenderClass).Cost;
        int devuelve = Ints.MultiplyPercent(
            costo, DataLoader.Current.Curves.Defenders.DefenderSellPercent);

        this.Button(r,
            _ventaArmada ? Loc.Get("ui.defensor.confirmar_venta") : Loc.Get("ui.defensor.vender"),
            Palette.Danger, relleno: _ventaArmada);

        this.Price(new Rect2(r.Position.X, r.End.Y + 2f, r.Size.X, 20f), devuelve,
            alcanza: true, HorizontalAlignment.Center);

        this.Label(new Rect2(r.Position.X, r.Position.Y - 20f, r.Size.X, 18f),
            Loc.Get("ui.defensor.arrastrar_para_mover"), 12, Palette.TextMuted,
            HorizontalAlignment.Center);

        // ⚠ Lo que se pierde al vender es la FICHA, no la clase: el nivel, los
        // perks y el arma son de la clase y quedan intactos. Es la confusión que
        // frena a un jugador nuevo de reacomodar el tablero.
        UiTooltip.Over(r,
            $"{Loc.Get("ui.defensor.vender")} · {devuelve}", "ui.ayuda.vender_defensor");
    }

    private void DibujarModo(int i, bool activo)
    {
        var r = FilaDe(i);

        // El activo se rellena; el de abajo del cursor solo se ilumina. Son los
        // mismos dos estados del botón del diseño, en versión de fila.
        this.Button(r, Loc.Get($"ui.targeting.{_modos[i]}"),
            activo ? Palette.Success : Palette.BorderNormal,
            relleno: activo, encima: i == _bajoElCursor);

        // Un modo por cartel, con la clave derivada del enum: agregar un quinto
        // modo es agregar su renglón a los idiomas y nada más.
        UiTooltip.Over(r, Loc.Get($"ui.targeting.{_modos[i]}"),
            $"ui.ayuda.targeting.{_modos[i]}");
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
        var combate = DataLoader.Current.Combat;

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
                Palette.Success, "ui.ayuda.aura");

            y += 22;
        }

        // Y lo que está RECIBIENDO, con lo que le queda. Los décimos de segundo se
        // leen mejor que los ticks: el jugador no piensa en 30 Hz.
        if (defensor.CadenceBuffPercent > 0 && partida.Tick < defensor.CadenceBuffExpiryTick)
        {
            int decimas = (defensor.CadenceBuffExpiryTick - partida.Tick) * 10 / Ints.TicksPerSecond;

            // ⚠ Es el único lugar del juego donde se ve un estado con su duración,
            // así que es donde va la regla de los estados: no se apilan, gana el
            // más fuerte y cada aplicación renueva el reloj. Dos bardos no dan el
            // doble de cadencia, y eso cambia qué conviene poner.
            Renglon($"{Loc.Get("ui.defensor.cadencia")} +{defensor.CadenceBuffPercent}%  ·  " +
                    $"{decimas / 10}.{decimas % 10}s",
                Palette.Legendary, "ui.ayuda.estados");

            y += 22;
        }

        // ⚠ De dónde sale el número de arriba. El StatTrio ya muestra el daño CON
        // el multiplicador de sinergia, y sin este bloque el jugador veía un
        // Paladín pegando 30,6 sin ninguna forma de saber que 24,7 son suyos y
        // el resto es el tablero — que es la definición de mecánica invisible.
        //
        // Primero el total, después cada sinergia que le llega a ESTA clase, y
        // solo las que entran antes del targeting: el rótulo de «apunta a» está
        // clavado en YTargeting y un renglón de más se le mete debajo.
        {
            var tabla = DataLoader.Current.Synergies;
            var unidades = partida.CountUnitsPerClass();
            var (bonusDano, bonusAlcance) = tabla.BonusFor(defensor.DefenderClass, unidades);

            bool hayConflicto = partida.HasBehaviorConflict(defensor.DefenderClass);

            // ⚠ El cupo se mide contra el TOPE del rótulo «apunta a», que arranca
            // en YTargeting - 26, no contra YTargeting - 8: con el margen chico el
            // último renglón de sinergia se le metía encima.
            int lugar = (int)((YTargeting - 26f - (y - Caja().Position.Y)) / 22f) - (hayConflicto ? 1 : 0);

            if (lugar >= 1)
            {
                var partes = new List<string>(3);

                if (bonusDano > 0)
                    partes.Add(Loc.Get("synergy.effect.Damage", bonusDano));

                if (bonusAlcance > 0)
                    partes.Add(Loc.Get("synergy.effect.Range", bonusAlcance));

                // ⚠ Los efectos AL MATAR también cuentan para el total. `BonusFor`
                // solo suma daño y alcance, así que un Ladrón con Saqueo y nada
                // más leía «ninguna activa para esta clase» y abajo, en el mismo
                // panel, la lista de activas mostraba Saqueo.
                foreach (var b in tabla.OnKillFor(defensor.DefenderClass, unidades))
                    partes.Add(Loc.Get($"synergy.effect.{b.Effect}", b.Magnitude));

                string total = partes.Count > 0
                    ? string.Join("  ·  ", partes)
                    : Loc.Get("ui.defensor.sin_sinergia");

                Renglon($"{Loc.Get("ui.defensor.sinergias").ToUpperInvariant()}  {total}",
                    partes.Count > 0 ? Palette.Success : Palette.TextMuted,
                    "ui.ayuda.sinergias_defensor");

                y += 22;
                lugar--;

                foreach (var b in tabla.ActiveFor(unidades))
                {
                    if (lugar <= 0)
                        break;

                    var dueña = tabla.ByKey(b.Key)!;

                    if (!b.Reaches(defensor.DefenderClass, dueña))
                        continue;

                    // El renglón lleva el cartel ENTERO de la sinergia, el mismo del
                    // panorama: desde acá el jugador tiene que poder ver qué le
                    // falta para el escalón siguiente sin deseleccionar la unidad.
                    var caja = new Rect2(x, y, Caja().Size.X - 32f, 22f);

                    this.Label(caja, $"  {Loc.Get($"synergy.{b.Key}.name")}  {SynergyText.Effect(b.Tier)}", 13,
                        Palette.TextSecondary);

                    UiTooltip.Over(caja, SynergyText.Tooltip(dueña, unidades));

                    y += 22;
                    lugar--;
                }
            }
        }

        // ⚠ Y el conflicto de comportamiento, que antes era MUDO: un Oro raro
        // quedaba anulado por equipar un arma y el jugador no se enteraba nunca.
        // Ahora se puede elegir cuál habla, y esto es lo que dice cuál está
        // hablando.
        if (partida.HasBehaviorConflict(defensor.DefenderClass))
        {
            bool ganaElPerk = partida.Roster.Find(defensor.DefenderClass)?.PreferPerkBehavior == true;

            Renglon(Loc.Get(ganaElPerk ? "ui.defensor.manda_el_perk" : "ui.defensor.manda_el_arma"),
                ganaElPerk ? Palette.Legendary : Palette.Epic, "ui.ayuda.conflicto_comportamiento");
        }

        void Renglon(string texto, Color color, string ayuda = "")
        {
            var caja = new Rect2(x, y, Caja().Size.X - 32f, 22f);

            this.Label(caja, texto, 14, color);
            UiTooltip.Over(caja, texto, ayuda);
        }
    }
}
