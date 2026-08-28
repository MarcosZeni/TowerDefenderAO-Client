using Godot;
using TowerOfArgentum.Simulacion;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El HUD de la corrida, con la disposición del panel del diseñador.
/// </summary>
/// <remarks>
/// <para>Las medidas salen todas de <see cref="UiLayout"/>, que copia
/// <c>Resources\ui\tower_of_argentum_ui_mockup_spec.json</c>. Acá no hay ni un
/// número de posición suelto, y esa es la regla: si algo hay que mover, se mueve
/// en el layout y se mueve para todas las pantallas a la vez.</para>
///
/// <para>De las siete zonas del diseño, el HUD dibuja las cuatro que conviven
/// con el mundo:</para>
///
/// <list type="bullet">
/// <item><b>La barra de arriba</b> — oleada, vida de la Torre, oro y los tres
/// botones.</item>
/// <item><b>La columna de la izquierda</b> — el roster, de donde se arrastran
/// las clases al mapa.</item>
/// <item><b>La barra de abajo</b> — la build de la clase elegida de un
/// vistazo.</item>
/// <item><b>El pie</b> — el consejo y la leyenda de rarezas.</item>
/// </list>
///
/// <para>Las tres del medio —inventario, detalle y tienda— son overlays y viven
/// en <see cref="PantallaMochila"/> y <see cref="PantallaTienda"/>, en esas
/// mismas coordenadas.</para>
///
/// <para>⚠ <b>El roster se mudó de la derecha a la izquierda.</b> Estaba a la
/// derecha desde antes del diseño; el panel del diseñador lo pone a la
/// izquierda y la disposición se copia entera o no se copia. Consecuencia para
/// los layouts: la zona de HUD ahora es la <b>columna izquierda</b> más las dos
/// barras horizontales, así que ningún mapa puede poner ruta ni celda colocable
/// ahí (<c>INTERFAZ.md</c> §3).</para>
/// </remarks>
public partial class Hud : Control
{
    private readonly Juego _juego;

    public Hud(Juego juego)
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

        BarraDeArriba(p);
        ColumnaDeClases(p);

        // ⚠ La barra de abajo la comparten dos pantallas: en combate muestra la
        // build y entre oleadas la preparación. Es el mismo rectángulo del
        // diseño, así que la que no manda se calla en vez de dibujar encima.
        if (!PantallaPreparacion.EstaAbierta(p))
            BarraDeBuild(p);

        Pie();

        if (_arrastrando)
            DibujarFantasma(p);

        if (p.State != RunState.InProgress)
            Cartel(p);
    }

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
        this.Panel(UiLayout.TopBar);

        // --- Oleada. El «/ 100» va escrito a propósito: la corrida TERMINA en
        //     la 100, y eso hay que comunicarlo desde la oleada 1, no
        //     descubrirlo al llegar.
        var oleada = UiLayout.WaveStatus;
        int total = CargadorDatos.Actual.Curves.RunCurve.WavesPerRun;

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

        // --- Oro. ⚠ NO es la billetera: la billetera es progresión permanente y
        //     vive en la cuenta; esto muere con la corrida.
        var oro = UiLayout.GoldStatus;

        this.Coin(new Vector2(oro.Position.X + 20f, oro.Position.Y + (oro.Size.Y / 2f)), 16f);

        this.Label(oro.Row(2f, 20f), Loc.Get("ui.hud.oro"), 14, Palette.TextMuted,
            HorizontalAlignment.Left, EmblemaAncho);

        this.Label(oro.Row(20f, 30f), UiStyle.Thousands(p.Gold), 27, Palette.Gold,
            HorizontalAlignment.Left, EmblemaAncho);

        // --- Los tres botones.
        this.Button(UiLayout.SettingsButton, "", Palette.BorderLight,
            relleno: false, encima: _botonEncima == 0);
        Engranaje(UiLayout.SettingsButton);

        this.Button(UiLayout.MenuButton, Loc.Get("ui.hud.menu"), Palette.BorderLight,
            relleno: false, encima: _botonEncima == 1);

        this.Button(UiLayout.PauseButton, "", _juego.Pausado ? Palette.Warning : Palette.BorderLight,
            relleno: _juego.Pausado, encima: _botonEncima == 2);
        Pausa(UiLayout.PauseButton);
    }

    /// <summary>Cuánto lugar deja el icono a la izquierda de un chip de la barra.</summary>
    private const float EmblemaAncho = 44f;

    /// <summary>La almena de la oleada. Tres dientes y un cuerpo.</summary>
    private void Emblema(Rect2 r, Color color)
    {
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

    /// <summary>El corazón de la vida. Dos círculos y un triángulo.</summary>
    private void Corazon(Rect2 r, Color color)
    {
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
        this.Panel(UiLayout.HeroesPanel);
        this.SectionTitle(UiLayout.HeroesTitle, Loc.Get("ui.hud.heroes"));

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
            int costo = CargadorDatos.Actual.StatsOf(ficha.DefenderClass).Cost;

            int puestas = 0;

            foreach (var d in p.Defenders)
            {
                if (d.DefenderClass == ficha.DefenderClass)
                    puestas++;
            }

            this.ClassCard(UiLayout.HeroCard(i), ficha, costo,
                alcanza: p.Gold >= costo,
                elegida: ficha.DefenderClass == _juego.ClaseElegida,
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

        this.Label(new Rect2(resto.Position.X, resto.End.Y + 2f, resto.Size.X, 20f),
            $"+{clases.Count - UiLayout.HeroCardsVisible}", 15, Palette.TextMuted,
            HorizontalAlignment.Right);
    }

    // =====================================================================
    // 3. La barra de la build
    // =====================================================================

    /// <summary>
    /// La build de la clase elegida, de un vistazo: perks, arma, planos, mochila.
    /// </summary>
    /// <remarks>
    /// <para>El diseño pone acá cuatro bloques y el cuarto son consumibles.
    /// <b>Este juego no tiene consumibles</b> —no existen en
    /// <c>JUGABILIDAD.md</c> ni en los datos— así que ese bloque muestra la
    /// <b>mochila</b>, que es lo que ocupa el mismo lugar en la cabeza del
    /// jugador: lo que tiene y todavía no puso.</para>
    ///
    /// <para>Es de la <b>clase elegida</b>, no del roster entero: todo en este
    /// juego es de la clase y nunca de la unidad, así que una barra de build que
    /// mezclara las nueve no diría nada.</para>
    /// </remarks>
    private void BarraDeBuild(Run p)
    {
        this.Panel(UiLayout.BuildPanel);

        var ficha = p.Roster.Find(_juego.ClaseElegida);

        Bloque(UiLayout.EquippedPerks, Loc.Get("ui.hud.perks_equipados"));
        Bloque(UiLayout.EquippedWeapon, Loc.Get("ui.hud.arma_equipada"));
        Bloque(UiLayout.ActiveBlueprints, Loc.Get("ui.hud.planos_activos"));
        Bloque(UiLayout.Consumables, Loc.Get("ui.hud.mochila"));

        if (ficha is not null)
        {
            PerksEquipados(UiLayout.EquippedPerks, ficha);
            ArmaEquipada(UiLayout.EquippedWeapon, p, ficha);
        }

        PlanosActivos(UiLayout.ActiveBlueprints, p);
        Mochila(UiLayout.Consumables, p);
    }

    /// <summary>El título y la separación de un bloque de la barra de build.</summary>
    private void Bloque(Rect2 r, string titulo)
    {
        this.Label(r.Row(0f, 22f), titulo.ToUpperInvariant(), 14, Palette.TextMuted);
        this.Rule(r, 24f);
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
                var perk = CargadorDatos.Actual.Perks.Get(ficha.Perks[i]);

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

        var todos = CargadorDatos.Actual.Blueprints.All;
        int caben = Mathf.Max(1, (int)((fila.Size.X - 34f) / paso));

        // ⚠ Se cuentan los tres orígenes juntos —el de `initial`, el comprado con
        // la billetera y el comprado en la tienda de esta corrida— porque es lo
        // que le importa a la pantalla: adentro de la corrida los tres se usan
        // igual. `Run.ConocePlano` ya resuelve eso.
        int conocidos = 0;
        int dibujados = 0;

        for (int i = 0; i < todos.Count; i++)
        {
            if (!p.ConocePlano(i))
                continue;

            conocidos++;

            if (dibujados >= caben)
                continue;

            var hueco = new Rect2(fila.Position.X + (dibujados * paso), fila.Position.Y, lado, lado);
            var salida = CargadorDatos.Actual.Perks.Get(todos[i].ResultingUnique);

            // El plano se pinta con el color de lo que produce: un Único.
            this.Slot(hueco, Palette.Unique);

            if (!CargadorIconos.Dibujar(this, salida.Key,
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
        int cap = CargadorDatos.Actual.Curves.Defenders.BackpackSlots;
        bool llena = p.Backpack.Ocupados >= cap;

        float lado = fila.Size.Y;
        float paso = lado + 8f;
        int caben = Mathf.Max(1, (int)((fila.Size.X - 60f) / paso));
        int cuantos = Mathf.Min(p.Backpack.Ocupados, caben);

        for (int i = 0; i < cuantos; i++)
        {
            var hueco = new Rect2(fila.Position.X + (i * paso), fila.Position.Y, lado, lado);
            var cosa = p.Backpack.Items[i];

            if (cosa.Kind == ItemType.Perk)
                this.PerkSlot(hueco, CargadorDatos.Actual.Perks.Get(cosa.Id));
            else
                this.Slot(hueco, Palette.Rare);
        }

        if (cuantos == 0)
            this.EmptySlot(new Rect2(fila.Position, new Vector2(lado, lado)));

        this.Label(fila, $"{p.Backpack.Ocupados}/{cap}", 17,
            llena ? Palette.Warning : Palette.TextSecondary, HorizontalAlignment.Right);
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
        this.Panel(UiLayout.FooterBar, opacidad: 0.85f);

        var aviso = new Rect2(UiLayout.FooterTip.Position.X - 22f,
            UiLayout.FooterTip.Position.Y, 18f, UiLayout.FooterTip.Size.Y);

        DrawCircle(aviso.Position + (aviso.Size / 2f), 8f, Palette.Info);

        this.Label(aviso, "i", 13, Palette.BackgroundMain, HorizontalAlignment.Center);

        this.Label(UiLayout.FooterTip,
            $"{Loc.Get("ui.hud.consejo")}  {Loc.Get("ui.hud.ayuda")}", 14, Palette.TextMuted);

        this.RarityLegend(UiLayout.FooterLegend);
    }

    /// <summary>El cartel de ganaste o perdiste, en el centro.</summary>
    private void Cartel(Run p)
    {
        bool gano = p.State == RunState.Won;

        string texto = gano ? Loc.Get("ui.hud.ganaste") : Loc.Get("ui.hud.cayo_la_torre");
        var color = gano ? Palette.Success : Palette.Danger;

        var r = new Rect2(
            (Escala.LienzoAncho / 2f) - 300f, (Escala.LienzoAlto / 2f) - 80f, 600f, 160f);

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
    /// <see cref="Juego"/>: sin eso, el click en una tarjeta que tapa una celda
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

            _juego.SeleccionarClase(p.Roster.Classes[i].DefenderClass);
            _arrastrando = true;
            GetViewport().SetInputAsHandled();
        }
        else if (_arrastrando)
        {
            _arrastrando = false;

            // Soltó fuera de la columna: eso es un arrastre al mapa. Si soltó
            // sobre otra tarjeta fue un click normal, y no hay que poner nada.
            if (FichaEn(boton.Position) < 0)
                _juego.PonerEn(boton.Position);

            GetViewport().SetInputAsHandled();
        }
    }

    private void Apretar(int boton)
    {
        switch (boton)
        {
            case 0:
                _juego.Opciones?.Abrir();
                break;

            case 1:
                _juego.EscMenu?.Abrir();
                break;

            case 2:
                _juego.Pausado = !_juego.Pausado;
                break;
        }
    }

    /// <summary>
    /// El fantasma que acompaña el arrastre: el muñeco pegado al cursor.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que hace que arrastrar se sienta como <b>llevar</b> la unidad
    /// y no como mover un puntero. Se imanta al centro de la celda de abajo —el
    /// mismo redondeo que va a usar <see cref="Juego.PonerEn"/> al soltar, así lo
    /// que se ve es lo que va a pasar— y se tiñe de rojo cuando en esa celda no
    /// se puede: ruta, ocupada, bloqueada o sin oro.</para>
    ///
    /// <para>Mientras el cursor sigue sobre la columna todavía no hay celda que
    /// mostrar, y el muñeco flota en el cursor sin imantarse.</para>
    /// </remarks>
    private void DibujarFantasma(Run p)
    {
        var ficha = p.Roster.Find(_juego.ClaseElegida);

        if (ficha is null)
            return;

        var cuerpo = UiWidgets.BodyOf(ficha, out var apariencia);

        if (cuerpo is null || apariencia is null)
            return;

        var mouse = GetLocalMousePosition();

        // Mismo 0,5 con el que el mundo dibuja las hojas x4: el fantasma mide lo
        // que va a medir el defensor puesto.
        const float s = 0.5f;

        var tinte = Colors.White.Alpha(0.72f);
        var pie = mouse + new Vector2(0, Escala.LadoCelda / 2f);

        if (FichaEn(mouse) < 0)
        {
            var celda = new Cell(
                (int)(mouse.X / Escala.LadoCelda),
                (int)(mouse.Y / Escala.LadoCelda));

            if (p.PuedePoner(_juego.ClaseElegida, celda))
            {
                var (cx, cy) = Escala.CentroDe(celda.X, celda.Y);
                pie = new Vector2(cx, cy + (Escala.LadoCelda / 2f));
            }
            else
            {
                tinte = Palette.Danger.Alpha(0.62f);
            }
        }

        this.Doll(cuerpo, apariencia, s, pie, tinte);
    }
}
