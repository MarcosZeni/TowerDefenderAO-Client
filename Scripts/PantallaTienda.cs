using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// La tienda rotativa de la corrida: cuatro ofertas, cada tres oleadas.
/// </summary>
/// <remarks>
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
/// tienda: sin eso, un jugador rico vacía el catálogo en una sola visita.</para>
/// </remarks>
public partial class PantallaTienda : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;
    private int _bajoElCursor = -1;

    private const int Ancho = 380;
    private const int Alto = 122;

    /// <summary>
    /// El lado del icono del perk en la oferta.
    /// </summary>
    /// <remarks>
    /// 52 y no más: lo que corre a la derecha se lo saca a la lista de clases que
    /// pueden equiparlo, que es el renglón que hace que comprar un Oro no sea a
    /// ciegas.
    /// </remarks>
    private const int LadoIcono = 52;
    private const int Separacion = 10;

    /// <summary>Margen inferior: arriba del panel de preparación, que mide 210.</summary>
    private const int MargenAbajo = 226;

    public PantallaTienda(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
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
        for (int i = 0; i < cantidad; i++)
        {
            if (RectDe(i, cantidad).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Columna a la derecha, apilada de abajo hacia arriba.
    /// </summary>
    /// <remarks>
    /// Va al costado y no en el centro porque el jugador está mirando <b>el
    /// mapa</b>: en esta fase decide dónde poner, y un panel centrado le taparía
    /// justo eso. Es la misma regla que llevó el panel de preparación abajo.
    /// </remarks>
    private static Rect2 RectDe(int i, int cantidad)
    {
        int x = Escala.LienzoAncho - Ancho - 24;
        int altoTotal = (cantidad * Alto) + ((cantidad - 1) * Separacion);
        int y0 = Escala.LienzoAlto - MargenAbajo - altoTotal;

        return new Rect2(x, y0 + (i * (Alto + Separacion)), Ancho, Alto);
    }

    public override void _Draw()
    {
        if (!Abierta())
            return;

        var partida = _juego.Run!;
        var stock = partida.Shop.Stock;

        var titulo = RectDe(0, stock.Count);

        Text(Loc.Get("ui.tienda.titulo"),
            titulo.Position.X, titulo.Position.Y - 34, 26, new Color(0.93f, 0.86f, 0.62f));

        Text(Loc.Get("ui.tienda.no_se_repone"),
            titulo.Position.X, titulo.Position.Y - 12, 15, new Color(0.55f, 0.58f, 0.64f));

        for (int i = 0; i < stock.Count; i++)
            DibujarTarjeta(i, stock[i], stock.Count, partida);
    }

    /// <summary>El color de cada categoría. Se distinguen antes de leer una palabra.</summary>
    private static Color AcentoDe(ShopOffer oferta, BalanceData datos) => oferta.Kind switch
    {
        ShopKind.Class => new Color(0.35f, 0.65f, 0.95f),
        ShopKind.Blueprint => new Color(0.62f, 0.45f, 0.92f),
        ShopKind.Weapon => new Color(0.70f, 0.74f, 0.80f),
        _ => datos.Perks.Get(oferta.Id).Tier switch
        {
            PerkTier.Bronze => new Color(0.80f, 0.55f, 0.35f),
            PerkTier.Silver => new Color(0.78f, 0.82f, 0.88f),
            _ => new Color(0.93f, 0.78f, 0.31f),
        },
    };

    private void DibujarTarjeta(int i, ShopOffer oferta, int cantidad, Run partida)
    {
        var r = RectDe(i, cantidad);
        var datos = CargadorDatos.Actual;

        bool alcanza = partida.Gold >= oferta.Price;
        bool resaltada = i == _bajoElCursor && !oferta.Sold && alcanza;

        // ⚠ Vendida se dibuja igual, apagada, en vez de desaparecer. El hueco
        // vacío ES la información: dice que la tienda no se repone, y que lo que
        // queda es todo lo que queda hasta la próxima.
        if (oferta.Sold)
        {
            DrawRect(r, new Color(0.06f, 0.07f, 0.09f, 0.80f));
            DrawRect(r, new Color(1, 1, 1, 0.06f), filled: false, width: 2f);

            Text(Loc.Get("ui.tienda.vendido"),
                r.Position.X + 20, r.Position.Y + 40, 22, new Color(0.38f, 0.40f, 0.44f));

            return;
        }

        var acento = AcentoDe(oferta, datos);

        DrawRect(r, new Color(0.08f, 0.09f, 0.13f, 0.94f));
        DrawRect(r, resaltada ? acento : new Color(1, 1, 1, 0.14f),
            filled: false, width: resaltada ? 3f : 2f);

        DrawRect(new Rect2(r.Position.X, r.Position.Y, 6, r.Size.Y), acento);

        float x = r.Position.X + 18;
        float y = r.Position.Y + 26;

        switch (oferta.Kind)
        {
            case ShopKind.Class: DibujarClase(oferta, x, y, acento, datos); break;
            case ShopKind.Blueprint: DibujarPlano(oferta, x, y, acento, partida, datos); break;
            case ShopKind.Weapon: DibujarArma(oferta, x, y, acento, partida); break;
            default: DibujarPerk(oferta, x, y, acento, datos); break;
        }

        // ⚠ El precio en rojo cuando no alcanza, y la carta igual se ve entera. Es
        // información: saber que existe un Oro que hoy no se puede pagar es lo que
        // hace que guardar oro sea una decisión.
        Text($"{oferta.Price}",
            r.Position.X + r.Size.X - 92, r.Position.Y + r.Size.Y - 16, 26,
            alcanza ? new Color(0.95f, 0.85f, 0.45f) : new Color(0.72f, 0.34f, 0.34f));
    }

    /// <summary>
    /// Reclutar una clase. <b>Es el desbloqueo, no un defensor puesto.</b>
    /// </summary>
    /// <remarks>
    /// Dice el costo de COLOCACIÓN además del de reclutamiento, porque son dos
    /// gastos distintos y el segundo se paga muchas veces. Sin esa línea, reclutar
    /// un mago por 656 parece caro hasta que uno descubre que además cada ficha
    /// cuesta 100.
    /// </remarks>
    private void DibujarClase(ShopOffer oferta, float x, float y, Color acento, BalanceData datos)
    {
        var clase = (DefenderClass)oferta.Id;
        var s = datos.StatsOf(clase);

        Text(Loc.Get("ui.tienda.reclutar"), x, y, 16, acento);
        Text(Loc.Class(clase), x, y + 30, 28, new Color(0.95f, 0.95f, 0.92f));

        Text($"{s.Damage} {Loc.Get("ui.tienda.dano")} · {s.RangeCells} {Loc.Get("ui.tienda.alcance")} · " +
             $"{s.CadenceTicks} {Loc.Get("ui.tienda.ticks")}",
            x, y + 56, 16, new Color(0.68f, 0.70f, 0.74f));

        Text($"{Loc.Get("ui.tienda.costo_colocacion")} {s.Cost}",
            x, y + 78, 16, new Color(0.58f, 0.62f, 0.68f));
    }

    /// <summary>
    /// Un perk. Dice <b>a qué clases les sirve</b>, no solo el nombre.
    /// </summary>
    /// <remarks>
    /// Un Oro es un comportamiento que solo pueden usar dos o tres de las nueve
    /// clases. Comprarlo sin saber quién lo puede equipar es comprarlo a ciegas, y
    /// a 1.900 de oro eso es una corrida perdida por falta de un renglón.
    /// </remarks>
    private void DibujarPerk(ShopOffer oferta, float x, float y, Color acento, BalanceData datos)
    {
        var def = datos.Perks.Get(oferta.Id);

        // ⚠ El icono va a la IZQUIERDA y corre el texto, no arriba a la derecha.
        // Ahí está el precio, y la lista de clases que sirven llega a nueve
        // nombres: cualquier cosa apoyada sobre el borde derecho se le encima.
        float texto = CargadorIconos.Dibujar(this, def.Key, x, y - 8, LadoIcono)
            ? x + LadoIcono + 12
            : x;

        Text(Loc.Tier(def.Tier), texto, y, 16, acento);
        Text(Loc.Perk(def), texto, y + 30, 26, new Color(0.95f, 0.95f, 0.92f));

        Text($"{Loc.Effect(def.Effect)} · {def.Magnitude}%",
            texto, y + 56, 16, new Color(0.68f, 0.70f, 0.74f));

        var sirve = new System.Text.StringBuilder();

        foreach (var c in ChoiceBuilder.All)
        {
            if (!def.WorksFor(c))
                continue;

            if (sirve.Length > 0)
                sirve.Append(" · ");

            sirve.Append(Loc.Class(c));
        }

        Text(sirve.Length == 0 ? Loc.Get("ui.tienda.cualquier_clase") : sirve.ToString(),
            texto, y + 78, 15, new Color(0.58f, 0.62f, 0.68f));
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
        ShopOffer oferta, float x, float y, Color acento, Run partida, BalanceData datos)
    {
        if (oferta.Id < 0 || oferta.Id >= datos.Blueprints.All.Count)
            return;

        var plano = datos.Blueprints.All[oferta.Id];
        var unico = datos.Perks.Get(plano.ResultingUnique);

        Text(Loc.Get("ui.tienda.plano"), x, y, 16, acento);
        Text(Loc.Perk(unico), x, y + 30, 26, new Color(0.95f, 0.95f, 0.92f));

        var a = datos.Perks.Get(plano.GoldA);
        var b = datos.Perks.Get(plano.GoldB);

        bool tieneA = Tiene(partida, plano.GoldA);
        bool tieneB = Tiene(partida, plano.GoldB);

        Marca(x, y + 56, tieneA, Loc.Perk(a));
        Marca(x + 180, y + 56, tieneB, Loc.Perk(b));

        Text(Loc.Get("ui.tienda.solo_esta_corrida"),
            x, y + 78, 15, new Color(0.58f, 0.62f, 0.68f));
    }

    private void Marca(float x, float y, bool tiene, string nombre)
    {
        Text(tiene ? "✓" : "✗", x, y, 16,
            tiene ? new Color(0.50f, 0.85f, 0.55f) : new Color(0.72f, 0.40f, 0.40f));

        Text(nombre, x + 18, y, 15,
            tiene ? new Color(0.82f, 0.88f, 0.82f) : new Color(0.62f, 0.62f, 0.62f));
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

    private void DibujarArma(ShopOffer oferta, float x, float y, Color acento, Run partida)
    {
        var arma = partida.Backpack.WeaponById(oferta.Id);

        if (arma is null)
            return;

        Text(Loc.Get("ui.tienda.arma"), x, y, 16, acento);
        Text(Loc.Weapon(arma), x, y + 30, 24, new Color(0.95f, 0.95f, 0.92f));

        Text($"+{arma.Damage}% {Loc.Get("ui.tienda.dano")} · " +
             $"+{arma.Cadence}% {Loc.Get("ui.tienda.cadencia")} · " +
             $"+{arma.Range}% {Loc.Get("ui.tienda.alcance")}",
            x, y + 56, 16, new Color(0.68f, 0.70f, 0.74f));

        Text(arma.Ability == WeaponAbility.None
                ? Loc.Get("ui.tienda.sin_habilidad")
                : Loc.AbilityText(arma.Ability, arma.Magnitude),
            x, y + 78, 15, new Color(0.85f, 0.80f, 0.60f));
    }

    private void Text(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
