using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El HUD de la corrida: <b>las paredes del marco donde va encastrado el mundo.</b>
/// </summary>
/// <remarks>
/// <para>Las medidas salen todas de <see cref="UiLayout"/>, que copia
/// <c>Resources\ui\tower_of_argentum_ui_mockup_spec.json</c>. Acá no hay ni un
/// número de posición suelto, y esa es la regla: si algo hay que mover, se mueve
/// en el layout y se mueve para todas las pantallas a la vez.</para>
///
/// <para>⚠ <b>El HUD ya no flota sobre el mundo: lo rodea.</b> Es un cambio de
/// fondo respecto de lo que decía <c>INTERFAZ.md</c> §3, y la razón es concreta:
/// con los paneles encima, la tienda y el panel del defensor tapaban celdas
/// jugables, y el jugador terminaba clickeando un panel en vez del mapa. Ahora
/// el mundo tiene su propio agujero y <b>nada se dibuja adentro salvo el
/// juego</b>.</para>
///
/// <para>Las cuatro paredes que dibuja esta clase:</para>
///
/// <list type="bullet">
/// <item><b>Arriba</b> — oleada, vida de la Torre, oro y los tres botones.</item>
/// <item><b>Izquierda</b> — el roster, de donde se arrastran las clases.</item>
/// <item><b>Abajo</b> — la build de la clase elegida de un vistazo.</item>
/// <item><b>El pie</b> — el consejo y la leyenda de rarezas.</item>
/// </list>
///
/// <para>La <b>quinta</b>, la columna de la derecha, no la dibuja el HUD: la
/// la ocupan <see cref="DefenderScreen"/> y el mercado, que
/// aparecen y desaparecen. El HUD deja el hueco y ellas lo llenan.</para>
///
/// <para>Y el HUD dibuja el <b>bisel</b>: el borde de metal entre las paredes y
/// el mundo, más el sobrante que deja centrar el 20×11 adentro del hueco. Va acá
/// y no en <see cref="Game"/> porque tiene que quedar <b>encima</b> del mundo,
/// para tapar lo que se salga —las partículas del portal, un sprite alto de la
/// primera fila—.</para>
/// </remarks>
public partial class Hud : Control
{
    private readonly Game _juego;

    public Hud(Game juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        UiStyle.Init();

        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>El botón de la barra de arriba bajo el cursor, o −1.</summary>
    private int _botonEncima = -1;

    /// <summary>Si arrancó un arrastre desde una tarjeta y todavía no soltó.</summary>
    private bool _arrastrando;

    public override void _Draw()
    {
        var p = _juego.Run;

        if (p is null)
            return;

        UiTooltip.Begin(GetGlobalMousePosition());

        // ⚠ El tablero se declara PRIMERO de todo, y eso es lo que lo pone último
        // en prioridad: gana el que declara al final, así que cualquier panel del
        // HUD dibujado más abajo pisa este cartel. Es lo correcto — la barra de
        // arriba tapa celdas del mundo, y ahí el jugador está señalando la barra.
        ExplicarElTablero(p);

        // El bisel primero: tapa lo que se haya salido del mundo, y las paredes
        // se dibujan después encima de sus bordes.
        this.Bezel(UiLayout.WorldFrame, UiLayout.WorldOnScreen);

        BarraDeArriba(p);
        ColumnaDeClases(p);

        // ⚠ La columna derecha tiene dos caras y manda el defensor: cuando hay uno
        // elegido, DefenderScreen dibuja su panel ahí y esto se calla. Sin
        // elegido, la columna quedaba VACÍA —la tienda se mudó al Mercado— y es
        // el lugar del tablero: qué sinergias están activas y cuáles a una clase
        // de estarlo. Es la razón para poner una clase que pega menos, y sin esto
        // el bonus era una mecánica invisible.
        if (_juego.SelectedDefender < 0)
            ColumnaDeSinergias(p);

        // ⚠ La barra de abajo la comparten dos pantallas: en combate muestra la
        // build y entre oleadas la preparación. Es el mismo rectángulo del
        // diseño, así que la que no manda se calla en vez de dibujar encima.
        if (!PreparationScreen.IsOpen(p))
            BarraDeBuild(p);

        Pie();

        if (Device.IsTouch)
            BotonesTactiles(p);

        // Antes del fantasma: mientras se arrastra una ficha el cursor lleva un
        // muñeco pegado, y un cartel debajo de él no se lee.
        //
        // ⚠ Y tampoco con un modal abierto. El mercado, la mochila y el drop del
        // jefe dibujan un velo encima de todo esto: un cartel del HUD quedaría
        // debajo del velo, medio apagado, explicando algo que en ese momento no
        // se puede tocar. Los que no velan nada —la preparación y el panel del
        // defensor— no cuentan: ahí el HUD se sigue usando.
        //
        // ⚠ En el dedo, un arrastre que no salió de la tarjeta TODAVÍA no es un
        // arrastre: apretar la tarjeta arma el arrastre, y mantenerla apretada es
        // justamente como se pide su cartel. Sin esta excepción, el cartel de las
        // tarjetas —el de la captura que abrió este cambio— no salía nunca.
        bool arrastrando = _arrastrando
                           && !(Device.IsTouch && FichaEn(GetGlobalMousePosition()) >= 0);

        if (!arrastrando && !HayModal())
        {
            UiTooltip.Draw(this);
        }
        else
        {
            // ⚠ Cancelar y no simplemente saltear el dibujo: el reloj de la
            // demora es estado que vive entre cuadros, y dejarlo contando un
            // ancla que el arrastre tapó hace que al soltar el botón aparezca de
            // golpe el cartel de lo que había abajo.
            UiTooltip.Cancel();
            DibujarFantasma(p);
        }

        if (p.State != RunState.InProgress)
            Cartel(p);
    }

    /// <summary>
    /// Si hay una pantalla con velo encima del HUD.
    /// </summary>
    /// <remarks>
    /// El drop del jefe no se pregunta por el nodo sino por la corrida —es la
    /// misma condición que usa <c>BossDropScreen.IsOpen</c>—, porque el HUD no
    /// tiene una referencia a esa pantalla y agregarla solo para esto sería atar
    /// dos nodos que hoy no se conocen.
    /// </remarks>
    private bool HayModal()
        => _juego.Market is { IsOpen: true }
           || _juego.Backpack is { IsOpen: true }
           || _juego.Run is { BossPicksLeft: > 0, OpenChoices.Length: > 0 };

    // =====================================================================
    // 0. El tablero
    // =====================================================================

    /// <summary>
    /// Qué hay en la celda bajo el cursor, y por qué no se puede poner ahí.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>El tablero era lo único del juego que no explicaba nada</b>, y
    /// es donde el jugador pasa la corrida entera. El mundo ya dice mucho con el
    /// color —verde se puede, rojo no, ámbar ocupada— pero <b>el color no dice el
    /// motivo</b>, y los motivos son cinco: la clase está sellada, no está
    /// reclutada, la celda es ruta, la celda es pared o no alcanza el oro. Un
    /// principiante que ve rojo sobre media pantalla concluye que el juego está
    /// roto, no que eligió una clase que todavía no puede poner.</para>
    ///
    /// <para>El motivo sale de <see cref="Run.PlaceBlockedKey"/>, el mismo que
    /// usa <c>CanPlace</c> para rebotar: el cartel y el rojo no se pueden separar
    /// nunca.</para>
    ///
    /// <para>Sobre una ficha ya puesta explica <b>la ficha</b> y no la colocación:
    /// ahí el próximo click abre su panel, no pone nada.</para>
    /// </remarks>
    private void ExplicarElTablero(Run p)
    {
        // ⚠ Se pregunta por la posición de AHORA y no por la celda que el mundo
        // tenga guardada: ese campo lo escribe un evento de movimiento, y una
        // pantalla que se coma el evento lo deja viejo. `CellAt` además devuelve
        // null fuera del marco, que es lo que evita explicar una celda tapada por
        // la barra de arriba.
        if (_juego.CellAt(GetGlobalMousePosition()) is not { } c)
            return;


        float lado = UiLayout.CellOnScreen;

        var enPantalla = new Rect2(
            UiLayout.ToScreen(new Vector2(c.X * Canvas.CellSide, c.Y * Canvas.CellSide)),
            new Vector2(lado, lado));

        // Una ficha puesta: quién es, en qué nivel, y que se arrastra para mover.
        if (p.DefenderAt(c) is { } puesto)
        {
            var ficha = p.Roster.Find(puesto.DefenderClass);

            UiTooltip.Over(enPantalla,
                ficha is null
                    ? Loc.Class(puesto.DefenderClass)
                    : $"{Loc.Class(puesto.DefenderClass)} · {Loc.Get("ui.hud.niv")} {ficha.Level}",
                "ui.ayuda.defensor_puesto");

            return;
        }

        string? motivo = p.PlaceBlockedKey(_juego.ChosenClass, c);

        // Libre y pagable: lo que falta decir es el gesto, porque el click sobre
        // una celda vacía dejó de poner nada a propósito.
        if (motivo is null)
        {
            UiTooltip.Over(enPantalla, Loc.Class(_juego.ChosenClass), "ui.ayuda.celda_libre");
            return;
        }

        UiTooltip.Over(enPantalla, Loc.Get(motivo), AyudaDelMotivo(motivo));
    }

    /// <summary>
    /// La explicación larga de cada motivo de bloqueo.
    /// </summary>
    /// <remarks>
    /// El renglón de arriba dice <b>qué pasa</b> —«esa celda es la ruta»— y esto
    /// dice <b>qué hacer</b>. Son dos textos y no uno solo porque el de arriba se
    /// sigue viendo con las ayudas apagadas.
    /// </remarks>
    private static string AyudaDelMotivo(string motivo) => motivo switch
    {
        "ui.mapa.clase_sellada" => "ui.ayuda.clase_sellada",
        "ui.mapa.clase_no_reclutada" => "ui.ayuda.roster_vacio",
        "ui.mapa.es_ruta" => "ui.ayuda.celda_ruta",
        "ui.mapa.no_colocable" => "ui.ayuda.celda_bloqueada",
        "ui.mapa.sin_oro" => "ui.ayuda.celda_sin_oro",
        _ => "ui.ayuda.celda_ocupada",
    };

    // =====================================================================
    // 1. La barra de arriba
    // =====================================================================

    /// <summary>
    /// Oleada, vida de la Torre, oro y los tres botones de la derecha.
    /// </summary>
    /// <remarks>
    /// El orden es el del diseño y es el correcto: <b>oleada primero</b> porque
    /// dice cuánto falta para terminar, <b>vida después</b> porque es la
    /// condición de derrota, y el oro al final porque es lo que se gasta entre
    /// oleada y oleada, no durante.
    /// </remarks>
    private void BarraDeArriba(Run p)
    {
        this.Panel(UiLayout.RunTopBar);

        // --- Oleada. El «/ 100» va escrito a propósito: la corrida TERMINA en
        //     la 100, y eso hay que comunicarlo desde la oleada 1, no
        //     descubrirlo al llegar.
        var oleada = UiLayout.WaveStatus;
        int total = DataLoader.Current.Curves.RunCurve.WavesPerRun;

        Emblema(oleada, Palette.TextSecondary);

        this.Label(oleada.Row(4f, 20f), Loc.Get("ui.hud.oleada"), 14, Palette.TextMuted,
            HorizontalAlignment.Left, EmblemaAncho);

        this.Label(oleada.Row(20f, 30f), $"{p.Wave}/{total}", 27, Palette.TextPrimary,
            HorizontalAlignment.Left, EmblemaAncho);

        // El cronómetro va acá y no en un panel propio: el ranking desempata por
        // ticks, así que el reloj es parte del estado de la corrida, no un
        // adorno.
        int segundos = p.Tick / Ints.TicksPerSecond;

        this.Label(oleada.Row(20f, 30f), $"{segundos / 60:00}:{segundos % 60:00}", 16,
            Palette.TextMuted, HorizontalAlignment.Right, 6f);

        // El chip entero es el blanco, no el número: quien no sabe qué es «18/100»
        // tampoco sabe sobre qué dígito poner el cursor.
        UiTooltip.Over(oleada, $"{p.Wave} / {total}", "ui.ayuda.oleada");

        // --- Vida de la Torre.
        var vida = UiLayout.TowerHp;
        int porcentaje = p.Tower.MaxHp == 0 ? 0 : p.Tower.Hp * 100 / p.Tower.MaxHp;

        Corazon(vida, Palette.Health(porcentaje));

        this.Label(vida.Row(2f, 20f), Loc.Get("ui.hud.vida_de_torre"), 14, Palette.TextMuted,
            HorizontalAlignment.Left, EmblemaAncho);

        // ⚠ El HP de la Torre vive en centésimas de punto adentro de la
        // simulación: crudo diría «100000 / 100000». `Loc.Points` lo devuelve a
        // los puntos que el jugador entiende.
        this.Bar(
            new Rect2(vida.Position.X + EmblemaAncho, vida.Position.Y + 24f,
                vida.Size.X - EmblemaAncho - 8f, 22f),
            porcentaje / 100f,
            Palette.Health(porcentaje),
            $"{Loc.Points(p.Tower.Hp)} / {Loc.Points(p.Tower.MaxHp)}");

        UiTooltip.Over(vida, $"{Loc.Points(p.Tower.Hp)} / {Loc.Points(p.Tower.MaxHp)}",
            "ui.ayuda.vida_de_torre");

        // --- Oro. ⚠ NO es la billetera: la billetera es progresión permanente y
        //     vive en la cuenta; esto muere con la corrida.
        var oro = UiLayout.GoldStatus;

        this.Coin(new Vector2(oro.Position.X + 20f, oro.Position.Y + (oro.Size.Y / 2f)), 16f);

        this.Label(oro.Row(2f, 20f), Loc.Get("ui.hud.oro"), 14, Palette.TextMuted,
            HorizontalAlignment.Left, EmblemaAncho);

        this.Label(oro.Row(20f, 30f), UiStyle.Thousands(p.Gold), 27, Palette.Gold,
            HorizontalAlignment.Left, EmblemaAncho);

        // ⚠ La ayuda del oro dice que NO es la billetera. Es la confusión más
        // cara de la corrida: quien cree estar juntando progresión permanente no
        // gasta, y el oro que sobra al final no compró nada.
        UiTooltip.Over(oro, UiStyle.Thousands(p.Gold), "ui.ayuda.oro");

        // --- Los tres botones.
        this.Button(UiLayout.SettingsButton, "", Palette.BorderLight,
            relleno: false, encima: _botonEncima == 0);
        Engranaje(UiLayout.SettingsButton);
        UiTooltip.Over(UiLayout.SettingsButton, Loc.Get("ui.opciones.titulo"));

        this.Button(UiLayout.MenuButton, Loc.Get("ui.hud.menu"), Palette.BorderLight,
            relleno: false, encima: _botonEncima == 1);
        UiTooltip.Help(UiLayout.MenuButton, "ui.ayuda.menu");

        this.Button(UiLayout.PauseButton, "", _juego.Pausado ? Palette.Warning : Palette.BorderLight,
            relleno: _juego.Pausado, encima: _botonEncima == 2);
        Pausa(UiLayout.PauseButton);

        UiTooltip.Over(UiLayout.PauseButton,
            Loc.Get(_juego.Pausado ? "ui.hud.reanudar" : "ui.hud.pausar"),
            "ui.ayuda.pausa");
    }

    /// <summary>Cuánto lugar deja el icono a la izquierda de un chip de la barra.</summary>
    private const float EmblemaAncho = 44f;

    /// <summary>
    /// La almena de la oleada.
    /// </summary>
    /// <remarks>
    /// La torre de RPG Awesome desde que la fuente entró; la almena a mano queda
    /// de reserva. Es <b>la Torre</b> y no una calavera a propósito: el número que
    /// está al lado es «vas por la oleada 18 de 100», o sea cuánto lleva
    /// aguantando lo que se defiende.
    /// </remarks>
    private void Emblema(Rect2 r, Color color)
    {
        var caja = new Rect2(r.Position.X + 6f, r.Position.Y + (r.Size.Y / 2f) - 14f, 28f, 28f);

        if (UiStyle.Icons is not null)
        {
            this.Icon(caja, UiStyle.Icono.Tower, color);
            return;
        }

        float lado = 22f;
        var c = new Vector2(r.Position.X + 20f, r.Position.Y + (r.Size.Y / 2f));

        DrawRect(new Rect2(c.X - (lado / 2f), c.Y - (lado * 0.25f), lado, lado * 0.75f), color);

        for (int i = 0; i < 3; i++)
        {
            DrawRect(new Rect2(
                c.X - (lado / 2f) + (i * lado * 0.4f), c.Y - (lado * 0.6f),
                lado * 0.2f, lado * 0.4f), color);
        }
    }

    /// <summary>
    /// El corazón de la vida de la Torre.
    /// </summary>
    /// <remarks>
    /// ⚠ Sale de <b>RPG Awesome</b> desde que la fuente entró al proyecto. Antes
    /// eran dos círculos y un triángulo dibujados a mano — se leía, pero al lado
    /// de los iconos de la fuente cantaba que era otra cosa. La reserva a mano
    /// queda para cuando el <c>.ttf</c> todavía no se importó.
    /// </remarks>
    private void Corazon(Rect2 r, Color color)
    {
        var caja = new Rect2(r.Position.X + 6f, r.Position.Y + (r.Size.Y / 2f) - 14f, 28f, 28f);

        if (UiStyle.Icons is not null)
        {
            this.Icon(caja, UiStyle.Icono.Health, color);
            return;
        }

        float radio = 7f;
        var c = new Vector2(r.Position.X + 20f, r.Position.Y + (r.Size.Y / 2f) - 3f);

        DrawCircle(c + new Vector2(-radio * 0.75f, 0), radio, color);
        DrawCircle(c + new Vector2(radio * 0.75f, 0), radio, color);

        DrawColoredPolygon(
        [
            c + new Vector2(-radio * 1.5f, radio * 0.35f),
            c + new Vector2(radio * 1.5f, radio * 0.35f),
            c + new Vector2(0, radio * 2.2f),
        ], color);
    }

    /// <summary>El engranaje del botón de opciones. Un aro con dientes.</summary>
    private void Engranaje(Rect2 r)
    {
        var c = r.Position + (r.Size / 2f);
        float radio = Mathf.Min(r.Size.X, r.Size.Y) * 0.28f;

        DrawArc(c, radio, 0f, Mathf.Tau, 20, Palette.TextSecondary, 3f);

        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6f;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

            DrawLine(c + (dir * radio), c + (dir * radio * 1.55f), Palette.TextSecondary, 3f);
        }
    }

    /// <summary>Las dos barras de la pausa, o el triángulo si ya está pausado.</summary>
    private void Pausa(Rect2 r)
    {
        var c = r.Position + (r.Size / 2f);
        float alto = r.Size.Y * 0.42f;
        var color = _juego.Pausado ? Palette.Warning : Palette.TextSecondary;

        if (_juego.Pausado)
        {
            DrawColoredPolygon(
            [
                c + new Vector2(-alto * 0.4f, -alto / 2f),
                c + new Vector2(alto * 0.6f, 0),
                c + new Vector2(-alto * 0.4f, alto / 2f),
            ], color);

            return;
        }

        DrawRect(new Rect2(c.X - 7f, c.Y - (alto / 2f), 5f, alto), color);
        DrawRect(new Rect2(c.X + 2f, c.Y - (alto / 2f), 5f, alto), color);
    }

    // =====================================================================
    // 2. La columna de clases
    // =====================================================================

    /// <summary>
    /// El roster: de acá se arrastran las clases al mapa.
    /// </summary>
    /// <remarks>
    /// El panel se dibuja <b>siempre</b>, aunque no haya ninguna clase: si
    /// apareciera recién con la primera contratación, el jugador no sabría dónde
    /// va a vivir su roster.
    /// </remarks>
    private void ColumnaDeClases(Run p)
    {
        this.Panel(UiLayout.RunRoster);
        this.SectionTitle(UiLayout.HeroesTitle, Loc.Get("ui.hud.heroes"));

        UiTooltip.Help(UiLayout.HeroesTitle, "ui.ayuda.roster");

        var clases = p.Roster.Classes;

        if (clases.Count == 0)
        {
            this.LockedClassCard(UiLayout.HeroCard(0), Loc.Get("ui.hud.aca_van_tus_clases"));
            this.LockedClassCard(UiLayout.HeroCard(1), Loc.Get("ui.hud.elegi_la_primera"));
            return;
        }

        int visibles = Mathf.Min(clases.Count, UiLayout.HeroCardsVisible);

        for (int i = 0; i < visibles; i++)
        {
            var ficha = clases[i];
            int costo = DataLoader.Current.StatsOf(ficha.DefenderClass).Cost;

            int puestas = 0;

            foreach (var d in p.Defenders)
            {
                if (d.DefenderClass == ficha.DefenderClass)
                    puestas++;
            }

            this.ClassCard(UiLayout.HeroCard(i), ficha, costo,
                alcanza: p.Gold >= costo,
                elegida: ficha.DefenderClass == _juego.ChosenClass,
                puestas);
        }

        // El hueco siguiente, si entra: dice que el roster puede crecer.
        if (visibles < UiLayout.HeroCardsVisible)
            this.LockedClassCard(UiLayout.HeroCard(visibles), Loc.Get("ui.hud.proximamente"));

        // Las que no entraron. Nueve clases contra cinco tarjetas: el número
        // suelto es más honesto que achicar las tarjetas hasta que no se lean.
        if (clases.Count <= UiLayout.HeroCardsVisible)
            return;

        var resto = UiLayout.HeroCard(UiLayout.HeroCardsVisible - 1);
        var contador = new Rect2(resto.Position.X, resto.End.Y + 2f, resto.Size.X, 20f);

        this.Label(contador, $"+{clases.Count - UiLayout.HeroCardsVisible}", 15, Palette.TextMuted,
            HorizontalAlignment.Right);

        // Un «+3» suelto se lee como un error de dibujo. Son clases del roster
        // que no entran en la columna, y siguen usándose desde la mochila.
        UiTooltip.Over(contador, $"+{clases.Count - UiLayout.HeroCardsVisible}",
            "ui.ayuda.clases_de_mas");
    }

    // =====================================================================
    // 3. La barra de la build
    // =====================================================================

    /// <summary>
    /// El panorama de sinergias: una fila por conjunto, con su estado.
    /// </summary>
    /// <remarks>
    /// <para>Tres estados y tres colores, en el orden en que el jugador los
    /// necesita: <b>activa</b> (verde, con el escalón que está dando),
    /// <b>a una clase</b> de activarse (ámbar, con lo que falta) e
    /// <b>inactiva</b> (apagada). Las activas van primero: son las que explican
    /// el número que pega el tablero ahora.</para>
    ///
    /// <para>Cada fila lleva el cartel entero —escalones, activo marcado, qué
    /// falta— por <see cref="SynergyText.Tooltip"/>, que es el mismo texto que
    /// usa el panel del defensor. El tablero se cuenta UNA vez por cuadro.</para>
    /// </remarks>
    private void ColumnaDeSinergias(Run p)
    {
        var caja = UiLayout.RunSidePanel;

        this.Panel(caja);

        var titulo = new Rect2(caja.Position.X + 14f, caja.Position.Y + 8f, caja.Size.X - 28f, 32f);

        this.SectionTitle(titulo, Loc.Get("ui.hud.sinergias"));
        UiTooltip.Help(titulo, "ui.ayuda.sinergias");

        var tabla = DataLoader.Current.Synergies;

        if (tabla.All.Count == 0)
            return;

        var unidades = p.CountUnitsPerClass();

        // Primero las activas, después las que están a una, después el resto.
        // Se ordena por estado y no por el orden del JSON porque lo que pega
        // AHORA tiene que estar arriba, no la que el diseñador escribió primero.
        var filas = new List<(Synergy S, int Presentes, SynergyTier? Activo, int Orden)>();

        foreach (var s in tabla.All)
        {
            int presentes = s.CountPresent(unidades);
            var activo = s.TierFor(presentes);

            int primerNeed = int.MaxValue;

            foreach (var t in s.Tiers)
                primerNeed = Mathf.Min(primerNeed, t.Need);

            int orden = activo is not null ? 0 : presentes >= primerNeed - 1 && presentes > 0 ? 1 : 2;
            filas.Add((s, presentes, activo, orden));
        }

        filas.Sort((a, b) => a.Orden != b.Orden ? a.Orden.CompareTo(b.Orden) : string.CompareOrdinal(a.S.Key, b.S.Key));

        const float AltoFila = 44f;
        float y = titulo.End.Y + 10f;

        foreach (var (s, presentes, activo, orden) in filas)
        {
            if (y + AltoFila > caja.End.Y - 8f)
                break;

            var fila = new Rect2(caja.Position.X + 14f, y, caja.Size.X - 28f, AltoFila - 6f);

            var color = orden switch
            {
                0 => Palette.Success,
                1 => Palette.Warning,
                _ => Palette.TextDisabled,
            };

            // La marca de estado: un cuadrado de color a la izquierda. Se dibuja
            // y no se escribe porque la fuente de respaldo del motor no trae los
            // círculos llenos y medios que serían lo natural.
            DrawRect(new Rect2(fila.Position.X, fila.Position.Y + 8f, 8f, 8f), color);

            int maximo = 0;

            foreach (var t in s.Tiers)
                maximo = Mathf.Max(maximo, t.Need);

            var nombre = new Rect2(fila.Position.X + 16f, fila.Position.Y, fila.Size.X - 16f, 22f);
            var detalle = new Rect2(fila.Position.X + 16f, fila.Position.Y + 20f, fila.Size.X - 16f, 18f);

            this.Label(nombre, Loc.Get($"synergy.{s.Key}.name"), 15,
                orden == 2 ? Palette.TextMuted : Palette.TextPrimary);

            this.Label(nombre, $"{presentes}/{maximo}", 14, color, HorizontalAlignment.Right);

            // Activa: lo que da. A una: lo que falta. Inactiva: nada, que ya
            // bastante ruido son diez filas.
            string segundo = activo is not null
                ? SynergyText.Effect(activo)
                : orden == 1 ? SynergyText.Missing(s, presentes, unidades) : "";

            if (segundo.Length > 0)
                this.Label(detalle, segundo, 12, orden == 0 ? Palette.TextSecondary : Palette.Warning);

            UiTooltip.Over(fila, SynergyText.Tooltip(s, unidades));

            y += AltoFila;
        }
    }

    /// <summary>
    /// La build de la clase elegida, de un vistazo: perks, arma, planos, mochila.
    /// </summary>
    /// <remarks>
    /// <para>El diseño pone acá cuatro bloques y el cuarto son consumibles.
    /// <b>Este juego no tiene consumibles</b> —no existen en
    /// <c>Doc\Jugabilidad\</c> ni en los datos— así que ese bloque muestra la
    /// <b>mochila</b>, que es lo que ocupa el mismo lugar en la cabeza del
    /// jugador: lo que tiene y todavía no puso.</para>
    ///
    /// <para>Es de la <b>clase elegida</b>, no del roster entero: todo en este
    /// juego es de la clase y nunca de la unidad, así que una barra de build que
    /// mezclara las nueve no diría nada.</para>
    /// </remarks>
    private void BarraDeBuild(Run p)
    {
        this.Panel(UiLayout.RunBottomBar);

        var ficha = p.Roster.Find(_juego.ChosenClass);

        Bloque(UiLayout.RunBottomBlock(0), Loc.Get("ui.hud.perks_equipados"), "ui.ayuda.perks_equipados");
        Bloque(UiLayout.RunBottomBlock(1), Loc.Get("ui.hud.arma_equipada"), "ui.ayuda.arma_equipada");
        Bloque(UiLayout.RunBottomBlock(2), Loc.Get("ui.hud.planos_activos"), "ui.ayuda.planos_activos");
        Bloque(UiLayout.RunBottomBlock(3), Loc.Get("ui.hud.mochila"), "ui.ayuda.mochila");

        if (ficha is not null)
        {
            PerksEquipados(UiLayout.RunBottomBlock(0), ficha);
            ArmaEquipada(UiLayout.RunBottomBlock(1), p, ficha);
        }

        PlanosActivos(UiLayout.RunBottomBlock(2), p);
        Mochila(UiLayout.RunBottomBlock(3), p);
    }

    /// <summary>El título y la separación de un bloque de la barra de build.</summary>
    /// <remarks>
    /// ⚠ El cartel cuelga del <b>bloque entero</b> y no del rótulo. Los cuatro
    /// bloques son las cuatro cosas que un jugador nuevo confunde entre sí
    /// —perks, arma, planos y mochila son cuatro sistemas distintos, no cuatro
    /// vistas del mismo— y el rótulo mide 22 px de alto sobre un bloque de 130.
    /// </remarks>
    private void Bloque(Rect2 r, string titulo, string ayuda)
    {
        this.Label(r.Row(0f, 22f), titulo.ToUpperInvariant(), 14, Palette.TextMuted);
        this.Rule(r, 24f);

        UiTooltip.Over(r, titulo, ayuda);
    }

    /// <summary>Alto de la fila de huecos dentro de un bloque de la build.</summary>
    private static Rect2 FilaDeHuecos(Rect2 bloque) => bloque.Row(32f, bloque.Size.Y - 34f);

    private void PerksEquipados(Rect2 r, RosterClass ficha)
    {
        var fila = FilaDeHuecos(r);
        float lado = fila.Size.Y;
        float paso = lado + 10f;

        // Se dibujan los slots ABIERTOS, no los ocupados: el hueco libre es la
        // mitad de la información. Un 2/6 se tiene que ver como cuatro huecos.
        int cuantos = Mathf.Min(ficha.OpenSlots, (int)(fila.Size.X / paso));

        for (int i = 0; i < cuantos; i++)
        {
            var hueco = new Rect2(fila.Position.X + (i * paso), fila.Position.Y, lado, lado);

            if (i < ficha.Perks.Count)
            {
                var perk = DataLoader.Current.Perks.Get(ficha.Perks[i]);

                this.PerkSlot(hueco, perk);
            }
            else
            {
                this.EmptySlot(hueco);
            }
        }
    }

    private void ArmaEquipada(Rect2 r, Run p, RosterClass ficha)
    {
        var fila = FilaDeHuecos(r);
        float lado = fila.Size.Y;
        var hueco = new Rect2(fila.Position.X, fila.Position.Y, lado, lado);

        var arma = ficha.Weapon < 0 ? null : p.Backpack.WeaponById(ficha.Weapon);

        if (arma is null)
        {
            this.EmptySlot(hueco);
            this.Label(new Rect2(hueco.End.X + 10f, fila.Position.Y, fila.Size.X - lado - 10f, fila.Size.Y),
                Loc.Get("ui.hud.sin_arma"), 14, Palette.TextDisabled);

            return;
        }

        // ⚠ Un arma con habilidad se marca en violeta y no en dorado: pelea por
        // la misma ranura de comportamiento que un Oro o un Único, y el jugador
        // elige cuál de los dos habla. Con el color del Oro esa competencia no
        // se vería.
        var color = arma.Ability == WeaponAbility.None ? Palette.Rare : Palette.Epic;

        this.Slot(hueco, color);

        this.Label(hueco, Loc.Weapon(arma)[..1].ToUpperInvariant(),
            (int)(lado * 0.5f), color, HorizontalAlignment.Center);

        float x = hueco.End.X + 10f;
        float ancho = r.End.X - x;
        float alto = fila.Size.Y / 3f;

        this.Label(new Rect2(x, fila.Position.Y, ancho, alto),
            Loc.Weapon(arma), 15, Palette.TextPrimary);

        this.Label(new Rect2(x, fila.Position.Y + alto, ancho, alto),
            $"{Loc.Get("ui.tienda.dano")} {Loc.Damage(arma.Damage)}", 13, Palette.TextSecondary);

        this.Label(new Rect2(x, fila.Position.Y + (alto * 2f), ancho, alto),
            $"{Loc.Get("ui.tienda.alcance")} {Loc.Range(arma.Range)}", 13, Palette.TextSecondary);
    }

    /// <summary>
    /// Los planos aprendidos: la única progresión permanente que hay.
    /// </summary>
    /// <remarks>
    /// Van en el HUD porque son la receta de fusión, y el jugador que sabe qué
    /// receta tiene mira distinto los Oro que le ofrece un jefe.
    /// </remarks>
    private void PlanosActivos(Rect2 r, Run p)
    {
        var fila = FilaDeHuecos(r);
        float lado = fila.Size.Y;
        float paso = lado + 8f;

        var todos = DataLoader.Current.Blueprints.All;
        int caben = Mathf.Max(1, (int)((fila.Size.X - 34f) / paso));

        // ⚠ Se cuentan los tres orígenes juntos —el de `initial`, el comprado con
        // la billetera y el comprado en la tienda de esta corrida— porque es lo
        // que le importa a la pantalla: adentro de la corrida los tres se usan
        // igual. `Run.ConocePlano` ya resuelve eso.
        int conocidos = 0;
        int dibujados = 0;

        for (int i = 0; i < todos.Count; i++)
        {
            if (!p.KnowsBlueprint(i))
                continue;

            conocidos++;

            if (dibujados >= caben)
                continue;

            var hueco = new Rect2(fila.Position.X + (dibujados * paso), fila.Position.Y, lado, lado);
            var salida = DataLoader.Current.Perks.Get(todos[i].ResultingUnique);

            // El plano se pinta con el color de lo que produce: un Único.
            this.Slot(hueco, Palette.Unique);

            if (!IconLoader.Draw(this, salida.Key,
                    hueco.Position.X + 5f, hueco.Position.Y + 5f, lado - 10f))
            {
                this.Label(hueco, Loc.Perk(salida)[..1].ToUpperInvariant(),
                    (int)(lado * 0.5f), Palette.Unique, HorizontalAlignment.Center);
            }

            dibujados++;
        }

        if (conocidos == 0)
        {
            this.EmptySlot(new Rect2(fila.Position, new Vector2(lado, lado)), bloqueado: true);

            this.Label(new Rect2(fila.Position.X + paso, fila.Position.Y, fila.Size.X - paso, fila.Size.Y),
                Loc.Get("ui.hud.sin_planos"), 13, Palette.TextDisabled);

            return;
        }

        if (conocidos > dibujados)
        {
            this.Label(fila, $"+{conocidos - dibujados}", 14, Palette.TextMuted,
                HorizontalAlignment.Right);
        }
    }

    /// <summary>
    /// Lo que espera en la mochila y cuánto lugar queda.
    /// </summary>
    /// <remarks>
    /// Cuando está llena, cada drop obliga a destruir algo. Que se vea el 15/15
    /// <b>antes</b> de que pase es lo que convierte eso en una decisión y no en
    /// una sorpresa.
    /// </remarks>
    private void Mochila(Rect2 r, Run p)
    {
        var fila = FilaDeHuecos(r);
        int cap = DataLoader.Current.Curves.Defenders.BackpackSlots;
        bool llena = p.Backpack.Used >= cap;

        float lado = fila.Size.Y;
        float paso = lado + 8f;
        int caben = Mathf.Max(1, (int)((fila.Size.X - 60f) / paso));
        int cuantos = Mathf.Min(p.Backpack.Used, caben);

        for (int i = 0; i < cuantos; i++)
        {
            var hueco = new Rect2(fila.Position.X + (i * paso), fila.Position.Y, lado, lado);
            var cosa = p.Backpack.Items[i];

            if (cosa.Kind == ItemType.Perk)
                this.PerkSlot(hueco, DataLoader.Current.Perks.Get(cosa.Id));
            else
                this.Slot(hueco, Palette.Rare);
        }

        if (cuantos == 0)
            this.EmptySlot(new Rect2(fila.Position, new Vector2(lado, lado)));

        this.Label(fila, $"{p.Backpack.Used}/{cap}", 17,
            llena ? Palette.Warning : Palette.TextSecondary, HorizontalAlignment.Right);

        // ⚠ Llena cambia la ayuda, no solo el color. «15/15» en ámbar dice que
        // algo pasa; lo que hay que saber es que el próximo drop se pierde en
        // silencio, y que la salida es MEJORAR las sobras, no tirarlas.
        UiTooltip.Help(fila, llena ? "ui.ayuda.mochila_llena" : "ui.ayuda.mochila_lugar");
    }

    // =====================================================================
    // 4. El pie
    // =====================================================================

    /// <summary>
    /// El consejo de la izquierda y la leyenda de rarezas de la derecha.
    /// </summary>
    /// <remarks>
    /// El consejo es la línea de atajos: es lo que un jugador nuevo necesita
    /// leer y lo que uno viejo deja de mirar, así que va en el texto más chico
    /// de la pantalla y en el color más apagado.
    /// </remarks>
    private void Pie()
    {
        this.Panel(UiLayout.RunFooter);

        var aviso = new Rect2(UiLayout.RunFooterTip.Position.X - 22f,
            UiLayout.RunFooterTip.Position.Y, 18f, UiLayout.RunFooterTip.Size.Y);

        DrawCircle(aviso.Position + (aviso.Size / 2f), 8f, Palette.Info);

        this.Label(aviso, "i", 13, Palette.BackgroundMain, HorizontalAlignment.Center);

        // En el celular el consejo habla de tocar y arrastrar: el de la PC nombra
        // teclas y el botón derecho, que ahí no existen.
        this.Label(UiLayout.RunFooterTip,
            $"{Loc.Get("ui.hud.consejo")}  {Loc.Get(Device.IsTouch ? "ui.hud.ayuda_tactil" : "ui.hud.ayuda")}",
            14, Palette.TextMuted);

        this.RarityLegend(UiLayout.RunFooterLegend);

        UiTooltip.Help(UiLayout.RunFooterLegend, "ui.ayuda.rarezas");
        UiTooltip.Help(UiLayout.RunFooterTip, "ui.ayuda.atajos");
    }

    /// <summary>
    /// Los botones del dedo, debajo del roster: mochila, velocidad y adelantar.
    /// </summary>
    /// <remarks>
    /// ⚠ Solo en el celular. Las tres cosas se hacían únicamente con teclado —Tab,
    /// 1-2-3 y Enter—, y sin ellas la mochila, que es donde se equipan perks y
    /// armas, quedaba inalcanzable. En la PC no se dibujan: el teclado ya las
    /// tiene y el hueco debajo del roster queda como estaba.
    /// </remarks>
    private void BotonesTactiles(Run p)
    {
        bool enCurso = p.State == RunState.InProgress;

        this.Button(UiLayout.TouchButton(0), Loc.Get("ui.hud.mochila"), Palette.BorderLight,
            relleno: _juego.Backpack is { IsOpen: true }, encima: _botonEncima == 3);

        this.Button(UiLayout.TouchButton(1), $"x{_juego.Velocidad}",
            _juego.Velocidad > 1 ? Palette.Warning : Palette.BorderLight,
            relleno: _juego.Velocidad > 1, encima: _botonEncima == 4);

        this.Button(UiLayout.TouchButton(2), Loc.Get("ui.hud.adelantar"),
            enCurso && _juego.CanCallNextWave ? Palette.Info : Palette.TextDisabled,
            relleno: false, encima: _botonEncima == 5);
    }

    /// <summary>El cartel de ganaste o perdiste, en el centro.</summary>
    private void Cartel(Run p)
    {
        bool gano = p.State == RunState.Won;

        string texto = gano ? Loc.Get("ui.hud.ganaste") : Loc.Get("ui.hud.cayo_la_torre");
        var color = gano ? Palette.Success : Palette.Danger;

        var r = new Rect2(
            (Canvas.CanvasWidth / 2f) - 300f, (Canvas.CanvasHeight / 2f) - 80f, 600f, 160f);

        this.Panel(r, elevado: true, opacidad: 0.96f);
        this.Glow(r, color, 2f, 6);
        DrawRect(r, color, filled: false, width: 3f);

        this.Label(r, texto, 54, color, HorizontalAlignment.Center);
    }

    // =====================================================================
    // El arrastre
    // =====================================================================

    /// <summary>La tarjeta de clase bajo esta posición, o −1.</summary>
    private int FichaEn(Vector2 posicion)
    {
        int cuantas = Mathf.Min(
            _juego.Run?.Roster.Classes.Count ?? 0, UiLayout.HeroCardsVisible);

        for (int i = 0; i < cuantas; i++)
        {
            if (UiLayout.HeroCard(i).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    /// <summary>El botón de la barra de arriba bajo esta posición, o −1.</summary>
    private static int BotonEn(Vector2 posicion)
        => UiLayout.SettingsButton.HasPoint(posicion) ? 0
         : UiLayout.MenuButton.HasPoint(posicion) ? 1
         : UiLayout.PauseButton.HasPoint(posicion) ? 2
         : Device.IsTouch && UiLayout.TouchButton(0).HasPoint(posicion) ? 3
         : Device.IsTouch && UiLayout.TouchButton(1).HasPoint(posicion) ? 4
         : Device.IsTouch && UiLayout.TouchButton(2).HasPoint(posicion) ? 5
         : -1;

    /// <summary>
    /// Click y arrastre sobre la columna de clases, más los tres botones.
    /// </summary>
    /// <remarks>
    /// <para>Las dos formas de poner que espera un jugador de tower defense:
    /// <b>click en la tarjeta y click en la celda</b>, o <b>arrastrar de la
    /// tarjeta a la celda</b>. Apretar sobre la tarjeta elige la clase en los dos
    /// casos; si el botón se suelta sobre el mapa, además pone.</para>
    ///
    /// <para>El evento se marca atendido para que no siga viaje a
    /// <see cref="Game"/>: sin eso, el click en una tarjeta que tapa una celda
    /// colocable pondría un defensor abajo del panel.</para>
    /// </remarks>
    public override void _UnhandledInput(InputEvent evento)
    {
        var p = _juego.Run;

        if (p is null)
            return;

        if (evento is InputEventMouseMotion movimiento)
        {
            int antes = _botonEncima;
            _botonEncima = BotonEn(movimiento.Position);

            if (antes != _botonEncima)
                QueueRedraw();

            return;
        }

        if (evento is not InputEventMouseButton { ButtonIndex: MouseButton.Left } boton)
            return;

        // Los tres botones de arriba andan SIEMPRE, hasta con la corrida
        // terminada: son la salida de la pantalla.
        if (boton.Pressed && BotonEn(boton.Position) is var cual and >= 0)
        {
            Apretar(cual);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Con un modal abierto la columna no atiende: la elección y la mochila
        // están encima y el click es para ellas.
        if (p.State != RunState.InProgress || p.WaitingForChoice)
            return;

        if (_juego.Backpack is { Visible: true })
            return;

        if (boton.Pressed)
        {
            int i = FichaEn(boton.Position);

            if (i < 0)
                return;

            _juego.SelectClass(p.Roster.Classes[i].DefenderClass);
            _arrastrando = true;
            GetViewport().SetInputAsHandled();
        }
        else if (_arrastrando)
        {
            _arrastrando = false;

            // Soltó fuera de la columna: eso es un arrastre al mapa. Si soltó
            // sobre otra tarjeta fue un click normal, y no hay que poner nada.
            if (FichaEn(boton.Position) < 0)
                _juego.PlaceAt(boton.Position);

            GetViewport().SetInputAsHandled();
        }
    }

    private void Apretar(int boton)
    {
        switch (boton)
        {
            case 0:
                _juego.Options?.Open();
                break;

            case 1:
                _juego.EscMenu?.Open();
                break;

            case 2:
                _juego.Pausado = !_juego.Pausado;
                break;

            // Los del dedo no andan con la corrida terminada, al revés de los
            // tres de arriba: ninguno es una salida de la pantalla.
            case 3 when _juego.Run is { State: RunState.InProgress }:
                _juego.ToggleBackpack();
                break;

            case 4 when _juego.Run is { State: RunState.InProgress }:
                _juego.CycleSpeed();
                break;

            case 5 when _juego.Run is { State: RunState.InProgress }:
                _juego.CallNextWave();
                break;
        }
    }

    /// <summary>
    /// El fantasma que acompaña el arrastre: el muñeco pegado al cursor.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que hace que arrastrar se sienta como <b>llevar</b> la unidad
    /// y no como mover un puntero. Se imanta al centro de la celda de abajo —el
    /// mismo redondeo que va a usar <see cref="Game.PlaceAt"/> al soltar, así lo
    /// que se ve es lo que va a pasar— y se tiñe de rojo cuando en esa celda no
    /// se puede: ruta, ocupada, bloqueada o sin oro.</para>
    ///
    /// <para>Mientras el cursor sigue sobre la columna todavía no hay celda que
    /// mostrar, y el muñeco flota en el cursor sin imantarse.</para>
    /// </remarks>
    private void DibujarFantasma(Run p)
    {
        var ficha = p.Roster.Find(_juego.ChosenClass);

        if (ficha is null)
            return;

        var cuerpo = UiWidgets.BodyOf(ficha, out var apariencia);

        if (cuerpo is null || apariencia is null)
            return;

        var mouse = GetLocalMousePosition();

        // ⚠ El fantasma lo dibuja el HUD, que está en coordenadas de PANTALLA, y
        // el defensor va a vivir en el mundo, que está corrido y a escala adentro
        // del marco. Así que el 0,5 con el que el mundo dibuja las hojas x4 se
        // multiplica por la escala del marco: sin eso el fantasma se ve al doble
        // de lo que va a medir la ficha puesta, y el jugador calcula mal el lugar.
        float s = 0.5f * UiLayout.WorldScale;

        var tinte = Colors.White.Alpha(0.72f);
        var pie = mouse + new Vector2(0, UiLayout.CellOnScreen / 2f);

        // Mientras el cursor sigue sobre la columna todavía no hay celda que
        // mostrar, y el muñeco flota en el cursor sin imantarse.
        if (FichaEn(mouse) < 0)
        {
            var mundo = UiLayout.ToWorld(mouse);

            var celda = new Cell(
                (int)(mundo.X / Canvas.CellSide),
                (int)(mundo.Y / Canvas.CellSide));

            if (UiLayout.InWorld(mouse) && p.CanPlace(_juego.ChosenClass, celda))
            {
                var (cx, cy) = Canvas.CentroDe(celda.X, celda.Y);

                pie = UiLayout.ToScreen(new Vector2(cx, cy))
                    + new Vector2(0, UiLayout.CellOnScreen / 2f);
            }
            else
            {
                tinte = Palette.Danger.Alpha(0.62f);
            }
        }

        this.Doll(cuerpo, apariencia, s, pie, tinte);
    }
}
