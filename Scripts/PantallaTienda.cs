using Godot;
using TowerOfArgentum.Simulacion;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// La tienda rotativa de la corrida: cuatro ofertas, cada tres oleadas.
/// </summary>
/// <remarks>
/// <para>Ocupa la <b>columna derecha</b> del diseño, con las cuatro ofertas en
/// dos por dos (<c>shop_card_1..4</c> de
/// <c>tower_of_argentum_ui_mockup_spec.json</c>). Cuatro es un número fijo del
/// juego, no una lista que crece, así que las cuatro posiciones son constantes y
/// no un bucle con separación.</para>
///
/// <para>⚠ <b>Aparece en la preparación, inmediatamente después de la pantalla de
/// elección</b>, y no literalmente al lado de ella. El diseño pedía las dos en la
/// misma pantalla y no se puede: el stock de la tienda lo arma la seed <b>al
/// cerrar</b> la elección —FaseChequeos abre la elección, el jugador elige, y
/// recién en el tick siguiente se arma la oleada y se renueva la tienda—. Sacar
/// el sorteo antes movería el punto de consumo del RNG y todas las corridas
/// viejas dejarían de reproducirse.</para>
///
/// <para>La tensión que el diseño buscaba se conserva entera igual, porque es
/// económica y no espacial: las dos decisiones caen en la <b>misma oleada</b> y
/// compiten por el <b>mismo oro</b>. La elección regala poder, la tienda lo cobra,
/// y lo que se gasta acá no se gasta en el tablero ni en un nivel.</para>
///
/// <para>⚠ <b>Información total.</b> Cada carta dice exactamente qué se compra:
/// qué clases pueden usar el perk, qué piezas le faltan al plano, cuánto pega la
/// clase que se recluta. La dificultad tiene que estar en decidir si vale la pena,
/// no en recordar una receta que el juego no muestra.</para>
///
/// <para>⚠ <b>No se repone.</b> Comprar deja el hueco vacío hasta la próxima
/// tienda: sin eso, un jugador rico vacía el catálogo en una sola visita. Por eso
/// el pie de la columna dice <i>cuándo</i> llega la próxima y no ofrece pagar por
/// renovar: <b>el botón «actualizar tienda» del diseño no existe en el juego</b>,
/// y ponerlo cambiaría el balance, no la pantalla.</para>
/// </remarks>
public partial class PantallaTienda : Control
{
    private readonly Juego _juego;
    private int _bajoElCursor = -1;

    public PantallaTienda(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);

        // ⚠ Ignore y no Stop, igual que la preparación: durante esta fase el
        // jugador TIENE que poder clickear el mapa para poner defensores. Los
        // clicks sobre las tarjetas se atienden en _Input, uno por uno.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// ¿Hay tienda que mostrar ahora?
    /// </summary>
    /// <remarks>
    /// Se muestra solo si el stock es <b>de la oleada que viene</b>. El stock no se
    /// acumula, así que el de la tienda anterior sigue en memoria hasta que se
    /// renueve: dibujarlo sería ofrecer algo que ya no está a la venta.
    /// </remarks>
    private bool Abierta()
    {
        var partida = _juego.Run;

        return partida is { State: RunState.InProgress, WaitingForWaveStart: true }
               && !partida.WaitingForChoice
               && partida.Shop.OleadaDelStock == partida.ProximaOleada
               && partida.Shop.Stock.Count > 0;
    }

    public override void _Process(double delta)
    {
        Visible = Abierta();

        if (Visible)
            QueueRedraw();
    }

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la misma cicatriz que el resto de
    /// las pantallas: con el hook de GUI el control queda sordo porque nunca toma
    /// el foco. La posición sale de <see cref="CanvasItem.GetGlobalMousePosition"/>
    /// y no del evento, que viene en píxeles de ventana y con <c>stretch = keep</c>
    /// no coincide con el lienzo.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        if (!Abierta())
            return;

        var stock = _juego.Run!.Shop.Stock;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = TarjetaEn(GetGlobalMousePosition(), stock.Count);
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                int i = TarjetaEn(GetGlobalMousePosition(), stock.Count);

                // ⚠ Solo se marca atendido el click que CAYÓ en una tarjeta. Esta
                // pantalla no es modal: un click en el mapa tiene que seguir
                // poniendo un defensor.
                if (i >= 0)
                {
                    _juego.Comprar(i);
                    GetViewport().SetInputAsHandled();
                }

                break;
            }
        }
    }

    private static int TarjetaEn(Vector2 posicion, int cantidad)
    {
        for (int i = 0; i < cantidad && i < UiLayout.ShopCards.Length; i++)
        {
            if (UiLayout.ShopCards[i].HasPoint(posicion))
                return i;
        }

        return -1;
    }

    public override void _Draw()
    {
        if (!Abierta())
            return;

        var partida = _juego.Run!;
        var stock = partida.Shop.Stock;

        // El panel va con opacidad: abajo hay mapa, y en esta fase el jugador
        // está eligiendo dónde poner. Taparlo del todo sería taparle la decisión.
        this.Panel(UiLayout.ShopPanel, opacidad: 0.94f);

        this.SectionTitle(UiLayout.ShopTitle, Loc.Get("ui.tienda.titulo"), Palette.Gold);

        // El diseño pone acá un reloj de cuenta regresiva. Acá la tienda no rota
        // por tiempo sino cada tres oleadas, así que dice qué oleada: es el mismo
        // dato —cuánto falta— en la unidad que este juego usa.
        this.Label(UiLayout.ShopTimer,
            $"{Loc.Get("ui.tienda.proximas_ofertas")} {partida.Shop.OleadaDelStock + 3}",
            15, Palette.TextMuted, HorizontalAlignment.Center);

        for (int i = 0; i < UiLayout.ShopCards.Length; i++)
        {
            if (i < stock.Count)
                DibujarTarjeta(i, stock[i], partida);
            else
                this.EmptySlot(UiLayout.ShopCards[i]);
        }

        // ⚠ Donde el diseño pone «actualizar tienda», va el aviso de que NO se
        // repone. Es la regla que hace que comprar sea una decisión, y el lugar
        // donde el jugador buscaría el botón de renovar es exactamente donde
        // tiene que leer que no existe.
        var aviso = new Rect2(
            UiLayout.ShopRefresh.Position,
            new Vector2(UiLayout.ShopRefreshCost.End.X - UiLayout.ShopRefresh.Position.X,
                UiLayout.ShopRefresh.Size.Y));

        this.Paragraph(aviso, Loc.Get("ui.tienda.no_se_repone"), 13, Palette.TextMuted);
    }

    /// <summary>
    /// El color de cada categoría. Se distinguen antes de leer una palabra.
    /// </summary>
    /// <remarks>
    /// Los perks toman el color de su tier, que es el mismo de los seis marcos de
    /// rareza del diseño; las otras tres categorías toman uno de los que a los
    /// perks les sobra. Reclutar una clase va en azul —lo mismo que marca la
    /// clase elegida en el HUD— y el plano en violeta, porque produce un Único.
    /// </remarks>
    private static Color AcentoDe(ShopOffer oferta, BalanceData datos) => oferta.Kind switch
    {
        ShopKind.Class => Palette.Info,
        ShopKind.Blueprint => Palette.Epic,
        ShopKind.Weapon => Palette.Common,
        _ => Palette.Rarity(datos.Perks.Get(oferta.Id).Tier),
    };

    private void DibujarTarjeta(int i, ShopOffer oferta, Run partida)
    {
        var r = UiLayout.ShopCards[i];
        var datos = CargadorDatos.Actual;

        // ⚠ Vendida se dibuja igual, apagada, en vez de desaparecer. El hueco
        // vacío ES la información: dice que la tienda no se repone, y que lo que
        // queda es todo lo que queda hasta la próxima.
        if (oferta.Sold)
        {
            this.EmptySlot(r);

            this.Label(r, Loc.Get("ui.tienda.vendido").ToUpperInvariant(), 18,
                Palette.TextDisabled, HorizontalAlignment.Center);

            return;
        }

        bool alcanza = partida.Gold >= oferta.Price;
        var acento = AcentoDe(oferta, datos);

        this.Slot(r, acento, elegido: i == _bajoElCursor && alcanza);

        // La tarjeta se lee de arriba a abajo: qué categoría, qué es, qué hace,
        // cuánto sale. El precio va último y pegado abajo, siempre en el mismo
        // lugar de las cuatro, para poder compararlos de un vistazo.
        var titulo = r.Row(10f, 22f);
        var icono = UiLayout.Square(r.Row(34f, r.Size.Y * 0.34f));
        var nombre = r.Row(34f + (r.Size.Y * 0.34f) + 4f, 26f);
        var cuerpo = new Rect2(
            r.Position.X + 10f, nombre.End.Y + 2f,
            r.Size.X - 20f, r.End.Y - nombre.End.Y - 40f);

        this.Label(titulo, EtiquetaDe(oferta).ToUpperInvariant(), 13, acento,
            HorizontalAlignment.Center);

        switch (oferta.Kind)
        {
            case ShopKind.Class:
                DibujarClase(oferta, icono, nombre, cuerpo, datos);
                break;

            case ShopKind.Blueprint:
                DibujarPlano(oferta, icono, nombre, cuerpo, acento, partida, datos);
                break;

            case ShopKind.Weapon:
                DibujarArma(oferta, icono, nombre, cuerpo, acento, partida);
                break;

            default:
                DibujarPerk(oferta, icono, nombre, cuerpo, acento, datos);
                break;
        }

        // ⚠ El precio en rojo cuando no alcanza, y la carta igual se ve entera.
        // Es información: saber que existe un Oro que hoy no se puede pagar es lo
        // que hace que guardar oro sea una decisión.
        this.Price(r.Row(r.Size.Y - 32f, 26f), oferta.Price, alcanza, HorizontalAlignment.Center);
    }

    /// <summary>La categoría, en el renglón de arriba de la tarjeta.</summary>
    private static string EtiquetaDe(ShopOffer oferta) => oferta.Kind switch
    {
        ShopKind.Class => Loc.Get("ui.tienda.reclutar"),
        ShopKind.Blueprint => Loc.Get("ui.tienda.plano"),
        ShopKind.Weapon => Loc.Get("ui.tienda.arma"),
        _ => Loc.Tier(CargadorDatos.Actual.Perks.Get(oferta.Id).Tier),
    };

    /// <summary>
    /// Reclutar una clase. <b>Es el desbloqueo, no un defensor puesto.</b>
    /// </summary>
    /// <remarks>
    /// Dice el costo de COLOCACIÓN además del de reclutamiento, porque son dos
    /// gastos distintos y el segundo se paga muchas veces. Sin esa línea, reclutar
    /// un mago por 656 parece caro hasta que uno descubre que además cada ficha
    /// cuesta 100.
    /// </remarks>
    private void DibujarClase(
        ShopOffer oferta, Rect2 icono, Rect2 nombre, Rect2 cuerpo, BalanceData datos)
    {
        var clase = (DefenderClass)oferta.Id;
        var s = datos.StatsOf(clase);

        // El muñeco de la clase, no un icono genérico: es lo que se va a ver en
        // el mapa, y reconocerlo importa más que el nombre.
        var ficha = new RosterClass { DefenderClass = clase, MaxLevel = 1 };

        this.Portrait(icono, ficha);

        this.Label(nombre, Loc.Class(clase).ToUpperInvariant(), 20, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(cuerpo.Row(0f, 18f), Loc.Role(clase), 13, Palette.TextMuted,
            HorizontalAlignment.Center);

        this.StatRow(cuerpo.Row(20f, 18f), Loc.Get("ui.tienda.dano"), s.Damage.ToString());
        this.StatRow(cuerpo.Row(38f, 18f), Loc.Get("ui.tienda.alcance"), s.RangeCells.ToString());
        this.StatRow(cuerpo.Row(56f, 18f), Loc.Get("ui.tienda.ticks"), s.CadenceTicks.ToString());

        this.StatRow(cuerpo.Row(76f, 18f), Loc.Get("ui.tienda.costo_colocacion"),
            s.Cost.ToString(), Palette.Gold);
    }

    /// <summary>
    /// Un perk. Dice <b>a qué clases les sirve</b>, no solo el nombre.
    /// </summary>
    /// <remarks>
    /// Un Oro es un comportamiento que solo pueden usar dos o tres de las nueve
    /// clases. Comprarlo sin saber quién lo puede equipar es comprarlo a ciegas, y
    /// a 1.900 de oro eso es una corrida perdida por falta de un renglón. Con la
    /// tarjeta vertical del diseño la lista no entra en un renglón, así que se
    /// parte en varios: se achica el texto, no la información.
    /// </remarks>
    private void DibujarPerk(
        ShopOffer oferta, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento, BalanceData datos)
    {
        var def = datos.Perks.Get(oferta.Id);

        this.Slot(icono, acento);

        if (!CargadorIconos.Dibujar(this, def.Key,
                icono.Position.X + 6f, icono.Position.Y + 6f, icono.Size.X - 12f))
        {
            this.Label(icono, Loc.Perk(def)[..1].ToUpperInvariant(),
                (int)(icono.Size.X * 0.5f), acento, HorizontalAlignment.Center);
        }

        this.Label(nombre, Loc.Perk(def).ToUpperInvariant(), 19, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(cuerpo.Row(0f, 18f), $"{Loc.Effect(def.Effect)} · {def.Magnitude}%", 13,
            Palette.TextSecondary, HorizontalAlignment.Center);

        var sirve = new System.Text.StringBuilder();

        foreach (var c in ChoiceBuilder.All)
        {
            if (!def.WorksFor(c))
                continue;

            if (sirve.Length > 0)
                sirve.Append(" · ");

            sirve.Append(Loc.Class(c));
        }

        this.Paragraph(cuerpo.Row(20f, cuerpo.Size.Y - 20f),
            sirve.Length == 0 ? Loc.Get("ui.tienda.cualquier_clase") : sirve.ToString(),
            12, Palette.TextMuted);
    }

    /// <summary>
    /// Un plano: la receta de un Único.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Muestra las dos piezas con tilde o cruz.</b> Un plano no da poder
    /// hoy: lo que se compra es la posibilidad de cerrar una receta más adelante, y
    /// esa decisión es imposible sin saber cuánto falta. Ocultar los componentes
    /// convertiría la dificultad en memorización, que es lo contrario de lo que la
    /// carta tiene que hacer.</para>
    ///
    /// <para>⚠ Lo comprado acá vale <b>para esta corrida</b>. El plano permanente
    /// se compra con la billetera desde el menú principal, y esa sigue siendo la
    /// única progresión permanente del juego.</para>
    /// </remarks>
    private void DibujarPlano(
        ShopOffer oferta, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento,
        Run partida, BalanceData datos)
    {
        if (oferta.Id < 0 || oferta.Id >= datos.Blueprints.All.Count)
            return;

        var plano = datos.Blueprints.All[oferta.Id];
        var unico = datos.Perks.Get(plano.ResultingUnique);

        this.Slot(icono, Palette.Unique);

        if (!CargadorIconos.Dibujar(this, unico.Key,
                icono.Position.X + 6f, icono.Position.Y + 6f, icono.Size.X - 12f))
        {
            this.Label(icono, Loc.Perk(unico)[..1].ToUpperInvariant(),
                (int)(icono.Size.X * 0.5f), Palette.Unique, HorizontalAlignment.Center);
        }

        this.Label(nombre, Loc.Perk(unico).ToUpperInvariant(), 18, Palette.TextPrimary,
            HorizontalAlignment.Center);

        Marca(cuerpo.Row(0f, 18f), Tiene(partida, plano.GoldA),
            Loc.Perk(datos.Perks.Get(plano.GoldA)));

        Marca(cuerpo.Row(19f, 18f), Tiene(partida, plano.GoldB),
            Loc.Perk(datos.Perks.Get(plano.GoldB)));

        this.Label(cuerpo.Row(42f, 18f), Loc.Get("ui.tienda.solo_esta_corrida"), 12,
            acento, HorizontalAlignment.Center);
    }

    /// <summary>Una pieza de la receta, con tilde si ya está o cruz si falta.</summary>
    private void Marca(Rect2 r, bool tiene, string nombre)
    {
        var color = tiene ? Palette.Success : Palette.Danger;

        this.Label(r, tiene ? "✓" : "✗", 14, color);

        this.Label(new Rect2(r.Position.X + 16f, r.Position.Y, r.Size.X - 16f, r.Size.Y),
            nombre, 13, tiene ? Palette.TextSecondary : Palette.TextMuted);
    }

    private static bool Tiene(Run partida, int idPerk)
    {
        foreach (var cosa in partida.Backpack.Items)
        {
            if (cosa.Kind == ItemType.Perk && cosa.Id == idPerk)
                return true;
        }

        foreach (var ficha in partida.Roster.Classes)
        {
            foreach (int p in ficha.Perks)
            {
                if (p == idPerk)
                    return true;
            }
        }

        return false;
    }

    private void DibujarArma(
        ShopOffer oferta, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento, Run partida)
    {
        var arma = partida.Backpack.WeaponById(oferta.Id);

        if (arma is null)
            return;

        // ⚠ Un arma con habilidad va en violeta: pelea por la ranura de
        // comportamiento contra un Oro o un Único, y el jugador elige cuál habla.
        var color = arma.Ability == WeaponAbility.None ? acento : Palette.Epic;

        this.Slot(icono, color);
        this.Label(icono, "⚔", (int)(icono.Size.X * 0.5f), color, HorizontalAlignment.Center);

        this.Label(nombre, Loc.Weapon(arma).ToUpperInvariant(), 17, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.StatRow(cuerpo.Row(0f, 18f), Loc.Get("ui.tienda.dano"), $"+{arma.Damage}%");
        this.StatRow(cuerpo.Row(18f, 18f), Loc.Get("ui.tienda.cadencia"), $"+{arma.Cadence}%");
        this.StatRow(cuerpo.Row(36f, 18f), Loc.Get("ui.tienda.alcance"), $"+{arma.Range}%");

        this.Paragraph(cuerpo.Row(58f, cuerpo.Size.Y - 58f),
            arma.Ability == WeaponAbility.None
                ? Loc.Get("ui.tienda.sin_habilidad")
                : Loc.AbilityText(arma.Ability, arma.Magnitude),
            12, arma.Ability == WeaponAbility.None ? Palette.TextDisabled : Palette.Epic);
    }
}
