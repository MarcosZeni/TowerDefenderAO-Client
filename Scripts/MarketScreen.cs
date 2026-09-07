using System.Collections.Generic;
using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El mercado: la elección y la tienda, en <b>una sola ventana</b>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Eran dos pantallas y se juntaron.</b> La elección abría un modal
/// centrado, el jugador elegía, y recién <i>después</i> aparecía la tienda en la
/// columna del marco. O sea que la decisión que <b>regala</b> poder y la que lo
/// <b>cobra</b> nunca se veían juntas: cuando la tienda aparecía, la elección ya
/// estaba tomada, y «¿me conviene el Oro de 1.900 o desbloquear el druida?» era
/// una pregunta que el juego hacía imposible responder.</para>
///
/// <para>Juntarlas costó cambiar el orden de la simulación —la preparación se
/// arma antes de abrir la elección, ver <c>Partida.FaseChequeos</c>— y ese es el
/// precio real de la mecánica. La pantalla es solo la consecuencia.</para>
///
/// <para><b>Tres zonas, todo tarjeta:</b></para>
///
/// <list type="table">
/// <item><term>Personajes</term><description>las opciones de contratar o
/// levelear, gratis, y las clases que vende la tienda.</description></item>
/// <item><term>Perks</term><description>los perks de la tienda, los planos, y el
/// Oro que ofrece un jefe.</description></item>
/// <item><term>Armas</term><description>las armas generadas de la
/// tienda.</description></item>
/// </list>
///
/// <para>Las tarjetas <b>gratis</b> —las de la elección— llevan el marco
/// iluminado y la palabra GRATIS donde las otras llevan el precio. Es lo que hace
/// legible la comparación: dos cosas al lado, una cuesta oro y la otra no.</para>
///
/// <para>⚠ <b>Modal solo mientras haya elección pendiente.</b> Después se cierra
/// con Esc y el jugador vuelve al tablero; la tienda sigue viva hasta que lance
/// la oleada, y se reabre desde el botón de la barra de abajo. Sin eso, comprar
/// obligaría a decidir todo antes de ver el tablero, que es exactamente al revés
/// de cómo se juega la preparación.</para>
///
/// <para>⚠ <b>Información total.</b> Cada tarjeta dice exactamente qué se lleva:
/// qué clases pueden usar el perk, qué piezas le faltan al plano, cuánto pega la
/// clase que se recluta y cuánto sale <i>además</i> ponerla en el tablero. La
/// dificultad tiene que estar en decidir si vale la pena, no en recordar algo que
/// la pantalla no muestra.</para>
///
/// <para>⚠ <b>La tienda no se repone.</b> Comprar deja el hueco vacío hasta la
/// próxima: el pie lo dice, y el botón «actualizar tienda» del diseño no existe
/// en el juego — ponerlo cambiaría el balance, no la pantalla.</para>
/// </remarks>
public partial class MarketScreen : Control
{
    private readonly Game _juego;

    /// <summary>
    /// Qué es una tarjeta: el índice de una oferta del stock.
    /// </summary>
    /// <remarks>
    /// ⚠ Tenía un <c>bool Gratis</c> para distinguir una opción de la pantalla de
    /// elección de una oferta de la tienda. La elección se fusionó con la tienda,
    /// así que las dos cosas son lo mismo y lo gratis es <c>ShopOffer.Free</c>: un
    /// precio, no otra clase de tarjeta.
    /// </remarks>
    private readonly record struct Ficha(int Indice);

    /// <summary>Las tres zonas, en el orden en que se dibujan de izquierda a derecha.</summary>
    private readonly List<Ficha>[] _columnas = [[], [], []];

    private (int Columna, int Fila) _bajoElCursor = (-1, -1);

    /// <summary>
    /// La tarjeta que está a un click de comprarse. <c>null</c> = ninguna.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Comprar son DOS clicks</b>, igual que vender un defensor o
    /// comprar un plano con la billetera. Con un solo click, pasar el mouse por
    /// la ventana mientras se decide y apretar sin querer <b>gasta el oro y
    /// consume la oferta</b> —la tienda no se repone—, así que el error no se
    /// puede deshacer ni esperando: esa oferta ya no vuelve en toda la corrida.
    /// </para>
    ///
    /// <para>El primer click arma y el segundo confirma. Se desarma solo al
    /// apuntar a otra tarjeta, al cerrar la ventana y al cambiar el stock: un
    /// «armado» que sobrevive a cualquiera de esas tres cosas es exactamente el
    /// click accidental que esto vino a evitar, con un paso más.</para>
    /// </remarks>
    private Ficha? _armado;

    /// <summary>
    /// El jugador cerró la ventana a mano y no quiere verla hasta la próxima.
    /// </summary>
    /// <remarks>
    /// Se resetea solo cuando cambia el stock: una tienda nueva es una decisión
    /// nueva, y arrastrar el «cerrado» de la anterior la escondería sin avisar.
    /// </remarks>
    private bool _cerrada;

    private int _stockVisto = -1;

    public MarketScreen(Game juego)
    {
        _juego = juego;
    }

    // =====================================================================
    // Cuándo se ve
    // =====================================================================

    // ⚠ ACÁ VIVÍA `HayEleccion`, y con él lo único que hacía modal a esta
    // pantalla. Ya no hay nada que el jugador esté obligado a resolver antes de
    // seguir: contratar y subir de nivel son ofertas que se compran o se dejan, y
    // el Oro del jefe —lo único que todavía frena la corrida— tiene pantalla
    // propia en BossDropScreen.
    //
    // Consecuencia directa: el mercado SIEMPRE se puede cerrar con Esc, y con eso
    // se fueron el botón PASAR y el cartel de «elegí o pasá».

    /// <summary>Hay stock de la tienda de esta preparación.</summary>
    private bool HayTienda
    {
        get
        {
            var p = _juego.Run;

            return p is { State: RunState.InProgress, WaitingForWaveStart: true }
                   && p.Shop.StockWave == p.NextWave
                   && p.Shop.Stock.Count > 0;
        }
    }

    /// <summary>
    /// Si la ventana se ve ahora.
    /// </summary>
    /// <remarks>
    /// ⚠ Cede ante la mochila, aunque haya elección pendiente. Las dos son
    /// ventanas grandes y el mercado se dibuja después, así que sin esto abrir la
    /// mochila —para ver si un perk de la tienda entra— la taparía entera. La
    /// elección no se pierde: sigue pendiente y la ventana vuelve al cerrarla.
    /// </remarks>
    public bool IsOpen
        => _juego.Backpack is not { IsOpen: true }
           && _juego.Run is not { BossPicksLeft: > 0 }
           && HayTienda && !_cerrada;

    /// <summary>La abre el botón MERCADO de la barra de preparación.</summary>
    public void Open() => _cerrada = false;

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        var p = _juego.Run;

        // Stock nuevo, ventana nueva: el «cerrada» de la tienda anterior no se
        // arrastra a la siguiente.
        if (p is not null && p.Shop.StockWave != _stockVisto)
        {
            _stockVisto = p.Shop.StockWave;
            _cerrada = false;
            _armado = null;
        }

        bool abierta = IsOpen;

        Visible = abierta;

        // Stop mientras se ve: es una ventana grande y un click que se escape al
        // mapa pondría un defensor atrás de ella.
        MouseFilter = abierta ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        if (abierta)
        {
            Repartir(p!);
            QueueRedraw();
        }
    }

    /// <summary>
    /// Reparte lo que hay en las tres zonas.
    /// </summary>
    /// <remarks>
    /// <para>Las opciones <b>gratis</b> van primero dentro de su zona: son las que
    /// frenan la corrida y las que hay que resolver sí o sí.</para>
    ///
    /// <para>⚠ <b>El Oro del jefe ya no entra acá.</b> Caía en la columna de
    /// perks, al lado de las ofertas de bronce que se compran con oro, así que el
    /// premio de matar al jefe —una de las ocho veces en toda la corrida en que
    /// aparece un comportamiento gratis— se veía igual que cualquier otra carta.
    /// Ahora tiene pantalla propia: ver <see cref="BossDropScreen"/>.</para>
    /// </remarks>
    private void Repartir(Run p)
    {
        foreach (var c in _columnas)
            c.Clear();

        if (!HayTienda)
            return;

        var stock = p.Shop.Stock;

        for (int i = 0; i < stock.Count; i++)
        {
            // Subir de nivel va con los personajes, no con los perks: lo que se
            // compra es una clase que ya está, y ponerlo entre los perks obligaría
            // a leer el rótulo para saber qué es.
            int zona = stock[i].Kind switch
            {
                ShopKind.Class or ShopKind.Level => 0,
                ShopKind.Weapon => 2,
                _ => 1,
            };

            _columnas[zona].Add(new Ficha(i));
        }
    }

    // =====================================================================
    // Geometría
    // =====================================================================

    private const float MargenX = 56f;
    private const float Hueco = 22f;
    private const float YColumnas = 176f;
    private const float AltoColumnas = 796f;

    /// <summary>
    /// Cuántas ranuras tiene una columna como mínimo.
    /// </summary>
    /// <remarks>
    /// ⚠ El alto de la tarjeta sale del <b>máximo</b> entre esto y lo que haya, no
    /// de la cantidad: con una sola oferta, dividir la columna por uno daría una
    /// tarjeta de 800 px de alto al lado de otras de 180. Las tres zonas tienen
    /// que verse como la misma grilla aunque tengan cantidades distintas.
    /// </remarks>
    private const int RanurasMinimas = 4;

    private static Rect2 Columna(int i)
    {
        float ancho = (Canvas.CanvasWidth - (MargenX * 2f) - (Hueco * 2f)) / 3f;

        return new Rect2(MargenX + (i * (ancho + Hueco)), YColumnas, ancho, AltoColumnas);
    }

    private Rect2 TarjetaRect(int columna, int fila)
    {
        var col = Columna(columna);

        float y0 = col.Position.Y + 46f;
        float disponible = col.End.Y - y0;

        int ranuras = System.Math.Max(RanurasMinimas, _columnas[columna].Count);
        float alto = (disponible - (Hueco * (ranuras - 1))) / ranuras;

        return new Rect2(col.Position.X, y0 + (fila * (alto + Hueco)), col.Size.X, alto);
    }

    private (int Columna, int Fila) TarjetaEn(Vector2 posicion)
    {
        for (int c = 0; c < _columnas.Length; c++)
        {
            for (int f = 0; f < _columnas[c].Count; f++)
            {
                if (TarjetaRect(c, f).HasPoint(posicion))
                    return (c, f);
            }
        }

        return (-1, -1);
    }

    private static Rect2 RectCerrar()
        => new(Canvas.CanvasWidth - MargenX - 200f, 92f, 200f, 44f);

    // =====================================================================
    // Entrada
    // =====================================================================

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la cicatriz de siempre: con el
    /// hook de GUI el control queda sordo porque nunca toma el foco, y la posición
    /// del evento viene en píxeles de ventana, que con <c>stretch = keep</c> no
    /// coincide con el lienzo.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        if (!IsOpen)
            return;

        var p = _juego.Run!;

        switch (evento)
        {
            case InputEventMouseMotion:
            {
                var antes = _bajoElCursor;

                _bajoElCursor = TarjetaEn(GetGlobalMousePosition());

                // Salir de la tarjeta armada la desarma. Sin esto, el jugador
                // arma una, se va a mirar otra, vuelve a apretar en cualquier
                // lado y compra la primera.
                if (_bajoElCursor != antes)
                    _armado = null;

                QueueRedraw();
                break;
            }

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                var posicion = GetGlobalMousePosition();

                if (RectCerrar().HasPoint(posicion))
                {
                    _cerrada = true;
                    _armado = null;
                    GetViewport().SetInputAsHandled();
                    break;
                }

                var (c, f) = TarjetaEn(posicion);

                if (c >= 0)
                    Apretar(_columnas[c][f]);

                // ⚠ Todo click se marca atendido, caiga donde caiga: la ventana es
                // grande y opaca, y uno que se escape al mapa pondría un defensor
                // atrás de ella.
                GetViewport().SetInputAsHandled();
                break;
            }

            case InputEventKey { Pressed: true, Echo: false } tecla:
                AtenderTecla(p, tecla);
                break;
        }
    }

    /// <summary>
    /// El Esc, que ahora siempre cierra.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Y estaban las teclas 1-2-3</b>, que resolvían la elección sin soltar
    /// el mouse. Se fueron con ella: acá ya no hay nada gratis y fijo a lo que
    /// apuntar, y un atajo numérico sobre siete ofertas <b>que se compran con
    /// oro</b> sería el peor atajo posible — el 2 gastaría a veces un arma de 900.
    /// </remarks>
    private void AtenderTecla(Run p, InputEventKey tecla)
    {
        if (tecla.Keycode != Key.Escape)
            return;

        _cerrada = true;
        _armado = null;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Atiende el click sobre una tarjeta: el primero arma, el segundo compra.
    /// </summary>
    /// <remarks>
    /// ⚠ Lo que <b>no se puede pagar ni siquiera se arma</b>. Antes se mandaba y
    /// la simulación lo rebotaba —sin cerrar la pantalla, a propósito—, pero un
    /// click que no hace nada ni dice por qué se lee como que la pantalla está
    /// rota. Y armar algo impagable dejaría un cartel de «confirmar» sobre una
    /// compra que nunca va a salir.
    /// </remarks>
    private void Apretar(Ficha ficha)
    {
        if (!Alcanza(ficha))
            return;

        // Primer click: se arma y no se compra nada.
        if (_armado != ficha)
        {
            _armado = ficha;
            QueueRedraw();
            return;
        }

        _armado = null;
        _juego.Buy(ficha.Indice);
    }

    /// <summary>Si el jugador puede pagar esta tarjeta.</summary>
    /// <remarks>
    /// ⚠ Contra <c>Cost</c> y no contra <c>Price</c>: con el cupo gratis el precio
    /// real sigue guardado en <c>Price</c>, y comparar contra él haría imposible
    /// de armar la única oferta que el jugador puede llevarse con cero oro.
    /// </remarks>
    private bool Alcanza(Ficha ficha)
        => _juego.Run!.Shop.Stock[ficha.Indice] is { Sold: false } o
           && !Bloqueada(_juego.Run, o)
           && o.Cost <= _juego.Run.Gold;

    /// <summary>
    /// Una oferta de nivel sobre una clase que <b>ya llegó al tope</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Se dibuja, no desaparece.</b> Que una clase no tenga adónde
    /// subir es información del roster del jugador, no un hueco: verla bloqueada
    /// dice «esta ya está terminada», y un hueco no dice nada.</para>
    ///
    /// <para>Se deriva del roster de AHORA y no de un campo de la oferta. Así, si
    /// el jugador sube esa clase desde la mochila con la tienda abierta, la
    /// ranura se bloquea sola; un <c>bool</c> guardado al armar la tienda se
    /// habría quedado viejo justo en ese caso.</para>
    /// </remarks>
    private static bool Bloqueada(Run p, ShopOffer oferta)
        => oferta.Kind == ShopKind.Level
           && p.Roster.Find((DefenderClass)oferta.Id) is { } f
           && f.Level >= DataLoader.Current.Curves.Shop.MaxLevelOnSale;

    // ⚠ ACÁ VIVÍAN `HayQuePagar` y `RectPasar`, que dibujaban el botón PASAR.
    //
    // Existía porque la elección se cobraba y un jugador sin oro se quedaba
    // trabado en un modal que no se cerraba, con el reloj congelado. Sin elección
    // no hay a qué saltear: si el jugador no puede pagar nada, cierra la tienda y
    // sigue jugando. La acción `SkipChoice` del log sobrevive y la sigue usando la
    // pantalla del jefe.

    // =====================================================================
    // Dibujo
    // =====================================================================

    public override void _Draw()
    {
        var p = _juego.Run;

        if (p is null || !IsOpen)
            return;

        UiTooltip.Begin(GetGlobalMousePosition());

        // El mundo se oscurece pero se sigue viendo: las dos decisiones se toman
        // mirando la defensa, no de memoria.
        this.Scrim();

        Cabecera(p);

        for (int c = 0; c < _columnas.Length; c++)
            DibujarColumna(p, c);

        Pie(p);

        // Último de todo: el cartel tiene que quedar encima de las tarjetas.
        UiTooltip.Draw(this);
    }

    private void Cabecera(Run p)
    {
        var titulo = new Rect2(0, 44f, Canvas.CanvasWidth, 48f);

        this.Label(titulo, Loc.Get("ui.mercado.titulo"), 40, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(titulo.Row(50f, 26f), Loc.Get("ui.mercado.esc_para_cerrar"), 18,
            Palette.TextMuted, HorizontalAlignment.Center);

        // El oro va arriba a la izquierda, del tamaño del título: es el número
        // contra el que se lee cada precio de la pantalla.
        var billetera = new Rect2(MargenX, 92f, 260f, 44f);

        this.Price(billetera, p.Gold, alcanza: true);

        UiTooltip.Over(billetera, UiStyle.Thousands(p.Gold), "ui.ayuda.oro");
        UiTooltip.Help(titulo, "ui.ayuda.tienda");

        this.Button(RectCerrar(), Loc.Get("ui.mercado.cerrar"), Palette.BorderNormal,
            relleno: false);

        // Declara su propio cartel aunque sea obvio: el rótulo de arriba abarca
        // el ancho entero de la pantalla, así que sin esto el botón de cerrar
        // mostraría la explicación de la tienda.
        UiTooltip.Over(RectCerrar(), Loc.Get("ui.mercado.esc_para_cerrar"));
    }

    /// <summary>
    /// Una zona: su rótulo y sus tarjetas.
    /// </summary>
    /// <remarks>
    /// Las ranuras vacías se dibujan igual, apagadas. El hueco <b>es</b>
    /// información: dice que esta tienda no trajo armas, y que la que no está no
    /// va a aparecer más tarde — la tienda no se repone.
    /// </remarks>
    private void DibujarColumna(Run p, int c)
    {
        var col = Columna(c);

        this.Panel(col, opacidad: 1f);

        var rotulo = col.Row(8f, 34f);

        this.SectionTitle(rotulo, Loc.Get($"ui.mercado.zona{c}"), ColorDeZona(c));

        // Las tres columnas son tres decisiones distintas —ancho de tablero,
        // poder de una clase, equipo— y no tres cajones de una misma bolsa. La
        // clave sale del índice, igual que el rótulo.
        UiTooltip.Over(rotulo, Loc.Get($"ui.mercado.zona{c}"), $"ui.ayuda.zona{c}");

        int ranuras = System.Math.Max(RanurasMinimas, _columnas[c].Count);

        for (int f = 0; f < ranuras; f++)
        {
            var r = TarjetaRect(c, f).Inset(10f);

            if (f >= _columnas[c].Count)
            {
                this.EmptySlot(r);
                continue;
            }

            DibujarTarjeta(p, r, _columnas[c][f], c == _bajoElCursor.Columna && f == _bajoElCursor.Fila);
        }
    }

    private static Color ColorDeZona(int c) => c switch
    {
        0 => Palette.Info,
        1 => Palette.Legendary,
        _ => Palette.Epic,
    };

    private void Pie(Run p)
    {
        var pie = new Rect2(MargenX, Canvas.CanvasHeight - 84f,
            Canvas.CanvasWidth - (MargenX * 2f), 56f);

        this.Panel(pie, opacidad: 1f);

        this.Label(pie.Inset(14f), Loc.Get("ui.tienda.no_se_repone"), 14, Palette.TextMuted);

        this.Label(pie.Inset(14f),
            $"{Loc.Get("ui.tienda.proximas_ofertas")} {p.Shop.StockWave + 3}",
            14, Palette.TextMuted, HorizontalAlignment.Right);

        // ⚠ «No se repone» es la regla que más plata cuesta ignorar: lo que no se
        // compró hoy no vuelve, y lo que se compró no deja un hueco que se
        // rellene. El renglón del pie lo dice; el cartel dice qué implica.
        UiTooltip.Help(pie, "ui.ayuda.stock");

        AvisoDelSello(p, pie);
    }

    /// <summary>
    /// El cartel del sello: en el arranque explica la regla, y en la tienda que
    /// lo levanta anuncia que la clase ya se puede poner.
    /// </summary>
    /// <remarks>
    /// <para>Va en el centro del pie y no en un modal aparte. Un modal al empezar
    /// la corrida se cierra sin leer —es lo primero que aparece y lo único que
    /// separa al jugador del juego—, y el texto que hace falta recordar es
    /// justamente el que hay que tener a la vista <b>mientras</b> se elige.</para>
    ///
    /// <para>⚠ Los dos casos son excluyentes y en este orden: mientras quede cupo
    /// gratis, la regla; después, el desbloqueo. Nunca los dos, porque nunca pasan
    /// juntos —el cupo se agota en el arranque y el sello se levanta tres oleadas
    /// más tarde.</para>
    /// </remarks>
    private void AvisoDelSello(Run p, Rect2 pie)
    {
        if (p.Shop.FreeLeft > 0)
        {
            this.Label(pie.Inset(14f),
                Loc.Get("ui.tienda.elegi_gratis", p.Shop.FreeLeft),
                15, Palette.Warning, HorizontalAlignment.Center);

            // Pisa la ayuda del stock a propósito: en la primera tienda, lo que
            // hay que entender no es que el stock no se repone, es que estas dos
            // clases son lo ÚNICO gratis de la corrida entera y que la segunda
            // entra sellada hasta la oleada 3.
            UiTooltip.Help(pie, "ui.ayuda.cupo_gratis");
            return;
        }

        if (p.UnsealedNow.Count == 0)
            return;

        var nombres = new System.Text.StringBuilder();

        foreach (var clase in p.UnsealedNow)
        {
            if (nombres.Length > 0)
                nombres.Append(" · ");

            nombres.Append(Loc.Class(clase));
        }

        this.Label(pie.Inset(14f),
            $"{nombres} — {Loc.Get("ui.tienda.sello_abierto")}",
            15, Palette.Success, HorizontalAlignment.Center);

        UiTooltip.Help(pie, "ui.ayuda.sello_abierto");
    }

    // =====================================================================
    // La tarjeta
    // =====================================================================

    /// <summary>
    /// Una tarjeta, apaisada: icono, qué es, cómo se llama, qué hace y el precio.
    /// </summary>
    /// <remarks>
    /// <para>El precio va <b>siempre en el mismo lugar</b> —abajo a la derecha— en
    /// las doce tarjetas de la pantalla. Es lo que permite compararlos de un
    /// vistazo en vez de buscarlos.</para>
    ///
    /// <para>Y donde las de la tienda dicen un número, las gratis dicen
    /// <b>GRATIS</b>. Esa es la comparación entera que la pantalla vino a hacer
    /// posible.</para>
    /// </remarks>
    private void DibujarTarjeta(Run p, Rect2 r, Ficha ficha, bool encima)
    {
        var datos = DataLoader.Current;

        var oferta = p.Shop.Stock[ficha.Indice];

        // ⚠ Vendida se dibuja igual, apagada, en vez de desaparecer. Una clase
        // que la elección acaba de regalar cae también acá: se retiró sin
        // cobrarse, y el hueco dice por qué ya no está.
        if (oferta.Sold)
        {
            this.EmptySlot(r);

            bool yaLaTiene = oferta.Kind == ShopKind.Class && p.Roster.Has((DefenderClass)oferta.Id);

            this.Label(r,
                Loc.Get(yaLaTiene ? "ui.mercado.ya_reclutada" : "ui.tienda.vendido").ToUpperInvariant(),
                18, Palette.TextDisabled, HorizontalAlignment.Center);

            // ⚠ Una carta apagada es una pregunta —«¿se repone? ¿vuelve?»— y la
            // respuesta es la regla del stock. La ranura queda vacía hasta la
            // próxima tienda: eso es lo que hace que comprar sea una decisión.
            UiTooltip.Over(r,
                Loc.Get(yaLaTiene ? "ui.mercado.ya_reclutada" : "ui.tienda.vendido"),
                yaLaTiene ? "ui.ayuda.ya_reclutada" : "ui.ayuda.vendido");

            return;
        }

        // ⚠ Contra el precio EFECTIVO. Con el cupo gratis, `Price` es lo que vale
        // y `Cost` lo que sale hoy: comparar contra Price pintaría de rojo —«no te
        // alcanza»— la única oferta que el jugador puede llevarse con cero oro.
        // ⚠ Una ranura de nivel sobre una clase al tope se dibuja BLOQUEADA, no
        // vendida ni ausente: es la única forma de decir «esta clase ya está
        // terminada» sin que el jugador tenga que abrir la mochila a contar.
        if (Bloqueada(p, oferta))
        {
            this.EmptySlot(r);

            var (ic, _, nom, _) = Partes(r);

            this.Portrait(ic, p.Roster.Find((DefenderClass)oferta.Id)!);

            this.Label(nom, Loc.Class((DefenderClass)oferta.Id).ToUpperInvariant(), 22,
                Palette.TextDisabled);

            this.Label(new Rect2(nom.Position.X, r.End.Y - 44f, nom.Size.X, 22f),
                Loc.Get("ui.tienda.nivel_maximo").ToUpperInvariant(), 15, Palette.TextDisabled);

            UiTooltip.Over(r, Loc.Get("ui.tienda.nivel_maximo"), "ui.ayuda.nivel_maximo");

            return;
        }

        bool alcanza = p.Gold >= oferta.Cost;
        var acento = AcentoDe(oferta, datos);

        this.Slot(r, acento, elegido: encima && alcanza);

        var (icono, titulo, nombre, cuerpo) = Partes(r);

        this.Label(titulo, EtiquetaDe(oferta).ToUpperInvariant(), 13, acento);

        // ⚠ Cada tipo de oferta explica SU regla, no «esto es una tienda». Las
        // cinco cosas que se venden se compran por motivos distintos: un nivel
        // abre slots, un plano solo vale esta corrida, un arma pelea con el perk
        // de comportamiento. Sin eso, siete cartas con precio se leen como siete
        // formas de gastar lo mismo.
        UiTooltip.Over(r, EtiquetaDe(oferta), AyudaDeOferta(oferta));

        switch (oferta.Kind)
        {
            case ShopKind.Class:
                DibujarClase(p, (DefenderClass)oferta.Id, icono, nombre, cuerpo, datos);
                break;

            case ShopKind.Level:
                DibujarNivel(p, (DefenderClass)oferta.Id, icono, nombre, cuerpo, datos);
                break;

            case ShopKind.Blueprint:
                DibujarPlano(oferta, icono, nombre, cuerpo, acento, p, datos);
                break;

            case ShopKind.Weapon:
                DibujarArma(oferta, icono, nombre, cuerpo, acento, p);
                break;

            default:
                DibujarPerk(datos.Perks.Get(oferta.Id), icono, nombre, cuerpo, acento);
                break;
        }

        // ⚠ El precio en rojo cuando no alcanza, y la carta igual se ve entera.
        // Saber que existe un Oro que hoy no se puede pagar es lo que hace que
        // guardar oro sea una decisión.
        // ⚠ Lo gratis se dice con la palabra, no con un «0». Un cero al lado de
        // una moneda se lee como «sin precio cargado», que es exactamente lo que
        // pasaba en la tienda de planos del menú principal.
        if (oferta.Free)
        {
            this.Label(new Rect2(nombre.Position.X, r.End.Y - 30f, nombre.Size.X, 24f),
                Loc.Get("ui.tienda.gratis").ToUpperInvariant(), 18, Palette.Success,
                HorizontalAlignment.Right);
        }
        else
        {
            this.Price(new Rect2(nombre.Position.X, r.End.Y - 30f, nombre.Size.X, 24f),
                oferta.Price, alcanza, HorizontalAlignment.Right);
        }

        DibujarArmado(r, ficha);
    }

    /// <summary>
    /// El aviso de la tarjeta armada: un click más y se compra.
    /// </summary>
    /// <remarks>
    /// <para>Tapa el precio a propósito. El precio ya se leyó —fue lo que hizo
    /// apretar la primera vez— y lo que hay que leer ahora es que el próximo
    /// click gasta el oro.</para>
    ///
    /// <para>Va en ámbar y con el marco entero encendido, que es el mismo
    /// vocabulario que usa el juego para «esto está por pasar»: la cuenta
    /// regresiva de la oleada y el segundo click de vender un defensor.</para>
    /// </remarks>
    private void DibujarArmado(Rect2 r, Ficha ficha)
    {
        if (_armado != ficha)
            return;

        DrawRect(r, Palette.Warning, filled: false, width: 2.5f);

        var banda = new Rect2(r.Position.X + 2f, r.End.Y - 32f, r.Size.X - 4f, 30f);

        DrawRect(banda, Palette.Warning.Alpha(0.92f));

        this.Label(banda, Loc.Get("ui.mercado.confirmar").ToUpperInvariant(), 14,
            Palette.BackgroundMain, HorizontalAlignment.Center);
    }

    /// <summary>Las cuatro partes de una tarjeta apaisada.</summary>
    private static (Rect2 Icono, Rect2 Titulo, Rect2 Nombre, Rect2 Body) Partes(Rect2 r)
    {
        float lado = r.Size.Y - 20f;
        var icono = new Rect2(r.Position.X + 10f, r.Position.Y + 10f, lado, lado);

        float x = icono.End.X + 14f;
        float ancho = r.End.X - x - 12f;

        return (
            icono,
            new Rect2(x, r.Position.Y + 8f, ancho, 18f),
            new Rect2(x, r.Position.Y + 26f, ancho, 28f),
            new Rect2(x, r.Position.Y + 58f, ancho, r.Size.Y - 92f));
    }

    // ⚠ ACÁ VIVÍAN `DibujarEleccion` y `DibujarQueAbre`, las tarjetas de la
    // pantalla de elección. Lo que dibujaban ahora sale por `DibujarClase` y
    // `DibujarNivel`, que son ofertas de la tienda como cualquier otra.

    /// <summary>
    /// Una oferta de subir de nivel: el muñeco, del nivel N al N+1, y el slot.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Lo que se compra es el SLOT, no los stats.</b> Los stats suben
    /// un 8% y eso no decide nada; la ranura de perk sí, porque es la que permite
    /// equipar lo que la mochila ya tiene esperando. Por eso el renglón del slot
    /// va en dorado y el trío de stats abajo, en gris.</para>
    ///
    /// <para>Y cuando el nivel <b>no</b> abre ranura —del 7 en adelante— se dice.
    /// La tienda no debería ofrecerlo, pero una oferta que quedó vieja porque el
    /// jugador subió esa clase desde la mochila sí puede llegar acá.</para>
    /// </remarks>
    private void DibujarNivel(
        Run p, DefenderClass clase, Rect2 icono, Rect2 nombre, Rect2 cuerpo, BalanceData datos)
    {
        var ficha = p.Roster.Find(clase);

        // Sin la clase en el roster la oferta quedó vieja: la simulación la retira
        // sin cobrar. Se dibuja lo que se pueda en vez de explotar.
        if (ficha is null)
        {
            this.Label(nombre, Loc.Class(clase).ToUpperInvariant(), 24, Palette.TextDisabled);
            return;
        }

        this.Portrait(icono, ficha);

        this.Label(nombre, Loc.Class(clase).ToUpperInvariant(), 24, Palette.TextPrimary);

        int destino = ficha.Level + 1;
        int tope = datos.Classes.PerkSlotsPerClass;

        int slotsAntes = ficha.OpenSlots;
        int slotsDespues = System.Math.Min(destino, tope);

        this.Label(cuerpo.Row(0f, 20f),
            $"{Loc.Get("ui.eleccion.level")} {ficha.Level} → {destino}", 16, Palette.Warning);

        this.Label(cuerpo.Row(22f, 20f),
            slotsDespues > slotsAntes
                ? $"{Loc.Get("ui.eleccion.abre_slot")} {slotsDespues} " +
                  $"{Loc.Get("ui.eleccion.de")} {tope}"
                : Loc.Get("ui.eleccion.slots_completos"),
            14, slotsDespues > slotsAntes ? Palette.Legendary : Palette.TextMuted);

        // ⚠ Cuántas fichas de esta clase están puestas, porque el nivel las sube a
        // TODAS. Un nivel con seis magos en el tablero y uno con un mago solo son
        // dos compras muy distintas, y es la única de las siete cartas cuyo valor
        // depende de lo que hay puesto.
        int puestos = 0;

        foreach (var d in p.Defenders)
        {
            if (d.DefenderClass == clase)
                puestos++;
        }

        this.Label(cuerpo.Row(44f, 20f),
            $"{puestos} {Loc.Get("ui.tienda.en_el_tablero")}", 13,
            puestos > 0 ? Palette.TextSecondary : Palette.TextMuted);
    }

    // =====================================================================
    // Los contenidos
    // =====================================================================

    /// <summary>
    /// El color de cada categoría. Se distinguen antes de leer una palabra.
    /// </summary>
    private static Color AcentoDe(ShopOffer oferta, BalanceData datos) => oferta.Kind switch
    {
        ShopKind.Class => Palette.Info,
        ShopKind.Level => Palette.Warning,
        ShopKind.Blueprint => Palette.Epic,
        ShopKind.Weapon => Palette.Common,
        _ => Palette.Rarity(datos.Perks.Get(oferta.Id).Tier),
    };

    // ⚠ Level TIENE que estar en los dos switch. El `_ =>` va a buscar un PERK con
    // el Id de la oferta, y en una de nivel ese Id es una CLASE: Perks.Get(3) por
    // «Asesino» devuelve un perk cualquiera y la tarjeta miente sin fallar.
    /// <summary>Qué regla explica cada tipo de oferta.</summary>
    /// <remarks>
    /// El perk se parte por tier y no queda en una sola ayuda: un Bronce es un
    /// número que se apila y un Oro es un comportamiento que pelea por la única
    /// ranura de la clase. Comprar un segundo Oro sin saberlo es tirar el oro
    /// más caro de la tienda.
    /// </remarks>
    private static string AyudaDeOferta(ShopOffer oferta) => oferta.Kind switch
    {
        ShopKind.Class => "ui.ayuda.oferta_clase",
        ShopKind.Level => "ui.ayuda.oferta_nivel",
        ShopKind.Blueprint => "ui.ayuda.oferta_plano",
        ShopKind.Weapon => "ui.ayuda.oferta_arma",
        _ => DataLoader.Current.Perks.Get(oferta.Id).Tier == PerkTier.Gold
            ? "ui.ayuda.perk_oro"
            : "ui.ayuda.perk_numero",
    };

    private static string EtiquetaDe(ShopOffer oferta) => oferta.Kind switch
    {
        ShopKind.Class => Loc.Get("ui.tienda.reclutar"),
        ShopKind.Level => Loc.Get("ui.tienda.levelear"),
        ShopKind.Blueprint => Loc.Get("ui.tienda.plano"),
        ShopKind.Weapon => Loc.Get("ui.tienda.arma"),
        _ => Loc.Tier(DataLoader.Current.Perks.Get(oferta.Id).Tier),
    };

    /// <summary>
    /// Una clase. Dice el costo de <b>colocación</b> además del de reclutamiento.
    /// </summary>
    /// <remarks>
    /// Son dos gastos distintos y el segundo se paga muchas veces. Sin esa línea,
    /// reclutar un mago por 656 parece caro hasta que uno descubre que además cada
    /// ficha cuesta 100.
    /// </remarks>
    private void DibujarClase(
        Run p, DefenderClass clase, Rect2 icono, Rect2 nombre, Rect2 cuerpo, BalanceData datos,
        bool conCosto = true)
    {
        var s = datos.StatsOf(clase);

        // El muñeco y no un icono: es lo que se va a ver en el mapa, y
        // reconocerlo importa más que el nombre.
        var ficha = p.Roster.Find(clase) ?? new RosterClass { DefenderClass = clase, MaxLevel = 1 };

        this.Portrait(icono, ficha);

        this.Label(nombre, Loc.Class(clase).ToUpperInvariant(), 24, Palette.TextPrimary);

        this.Label(cuerpo.Row(0f, 18f), Loc.Role(clase), 13, Palette.TextMuted);

        // ⚠ Los tres números en UN renglón, con icono. Antes eran tres filas de
        // «palabra … número» más una cuarta para el costo de colocación: cuatro
        // renglones para cuatro cifras de un dígito, y el texto se salía del
        // recuadro. Ver UiStyle.StatTrio.
        this.StatTrio(cuerpo.Row(20f, 20f),
            s.Damage.ToString(), s.RangeCells.ToString(), s.CadenceTicks.ToString());

        if (!conCosto)
            return;

        // ⚠ El costo de COLOCACIÓN, que es otro gasto y se paga muchas veces.
        // Sin esta línea, reclutar un mago por 656 parece caro hasta que uno
        // descubre que además cada ficha cuesta 100.
        var fila = cuerpo.Row(42f, 20f);
        float ancho = UiStyle.Width(Loc.Get("ui.tienda.costo_colocacion"), 12) + 10f;

        this.Label(fila, Loc.Get("ui.tienda.costo_colocacion"), 12, Palette.TextMuted);

        this.Price(new Rect2(fila.Position.X + ancho, fila.Position.Y,
                fila.Size.X - ancho, fila.Size.Y),
            s.Cost);
    }

    /// <summary>
    /// Un perk. Dice <b>a qué clases les sirve</b>, no solo el nombre.
    /// </summary>
    /// <remarks>
    /// Un Oro es un comportamiento que solo pueden usar dos o tres de las nueve
    /// clases. Comprarlo sin saber quién lo puede equipar es comprarlo a ciegas, y
    /// a 1.900 de oro eso es una corrida perdida por falta de un renglón.
    /// </remarks>
    private void DibujarPerk(PerkDefinition def, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento)
    {
        this.Slot(icono, acento);

        if (!IconLoader.Draw(this, def.Key,
                icono.Position.X + 8f, icono.Position.Y + 8f, icono.Size.X - 16f))
        {
            this.Label(icono, Loc.Perk(def)[..1].ToUpperInvariant(),
                (int)(icono.Size.X * 0.5f), acento, HorizontalAlignment.Center);
        }

        this.Label(nombre, Loc.Perk(def).ToUpperInvariant(), 24, Palette.TextPrimary);

        this.EffectRow(cuerpo.Row(0f, 20f), def, acento);

        // ⚠ Sin el rótulo «le sirve a»: la lista de clases en gris ya se lee como
        // lo que es, y ese renglón le comía la mitad del lugar a lo único que no
        // se puede deducir mirando el icono. Un Oro que solo pueden usar dos de
        // las nueve clases, comprado para la que no es, es oro tirado.
        var sirve = new System.Text.StringBuilder();

        foreach (var c in ChoiceBuilder.All)
        {
            if (!def.WorksFor(c))
                continue;

            if (sirve.Length > 0)
                sirve.Append(" · ");

            sirve.Append(Loc.Class(c));
        }

        this.Paragraph(cuerpo.Row(22f, cuerpo.Size.Y - 22f),
            sirve.Length == 0 ? Loc.Get("ui.tienda.cualquier_clase") : sirve.ToString(),
            13, Palette.TextMuted);
    }

    /// <summary>
    /// Un plano: la receta de un Único, con <b>tilde o cruz</b> en cada pieza.
    /// </summary>
    /// <remarks>
    /// <para>Un plano no da poder hoy: lo que se compra es la posibilidad de
    /// cerrar una receta más adelante, y esa decisión es imposible sin saber
    /// cuánto falta. Ocultar los componentes convertiría la dificultad en
    /// memorización.</para>
    ///
    /// <para>⚠ Lo comprado acá vale <b>para esta corrida</b>. El permanente se
    /// compra con la billetera desde el menú principal.</para>
    /// </remarks>
    private void DibujarPlano(
        ShopOffer oferta, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento,
        Run p, BalanceData datos)
    {
        if (oferta.Id < 0 || oferta.Id >= datos.Blueprints.All.Count)
            return;

        var plano = datos.Blueprints.All[oferta.Id];
        var unico = datos.Perks.Get(plano.ResultingUnique);

        this.Slot(icono, Palette.Unique);

        if (!IconLoader.Draw(this, unico.Key,
                icono.Position.X + 8f, icono.Position.Y + 8f, icono.Size.X - 16f))
        {
            this.Label(icono, Loc.Perk(unico)[..1].ToUpperInvariant(),
                (int)(icono.Size.X * 0.5f), Palette.Unique, HorizontalAlignment.Center);
        }

        this.Label(nombre, Loc.Perk(unico).ToUpperInvariant(), 22, Palette.TextPrimary);

        Marca(cuerpo.Row(0f, 19f), Tiene(p, plano.GoldA), Loc.Perk(datos.Perks.Get(plano.GoldA)));
        Marca(cuerpo.Row(20f, 19f), Tiene(p, plano.GoldB), Loc.Perk(datos.Perks.Get(plano.GoldB)));

        this.Label(cuerpo.Row(44f, 18f), Loc.Get("ui.tienda.solo_esta_corrida"), 12, acento);
    }

    private void Marca(Rect2 r, bool tiene, string nombre)
    {
        var color = tiene ? Palette.Success : Palette.Danger;

        // ⚠ La cruz es «×» y no «✗»: IBM Plex Mono no trae la segunda, y un
        // glifo que la fuente no tiene se dibuja como un rectángulo vacío — que
        // en una receta se lee como «no sé», no como «falta».
        this.Label(r, tiene ? "✓" : "×", 14, color);

        this.Label(new Rect2(r.Position.X + 18f, r.Position.Y, r.Size.X - 18f, r.Size.Y),
            nombre, 13, tiene ? Palette.TextSecondary : Palette.TextMuted);
    }

    private static bool Tiene(Run p, int idPerk)
    {
        foreach (var cosa in p.Backpack.Items)
        {
            if (cosa.Kind == ItemType.Perk && cosa.Id == idPerk)
                return true;
        }

        foreach (var ficha in p.Roster.Classes)
        {
            foreach (int perk in ficha.Perks)
            {
                if (perk == idPerk)
                    return true;
            }
        }

        return false;
    }

    private void DibujarArma(
        ShopOffer oferta, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento, Run p)
    {
        var arma = p.Backpack.WeaponById(oferta.Id);

        if (arma is null)
            return;

        // ⚠ Un arma con habilidad va en violeta: pelea por la ranura de
        // comportamiento contra un Oro o un Único, y el jugador elige cuál habla.
        var color = arma.Ability == WeaponAbility.None ? acento : Palette.Epic;

        this.Slot(icono, color);
        this.Icon(icono.Inset(icono.Size.X * 0.22f), UiStyle.Icono.Weapon, color);

        this.Label(nombre, Loc.Weapon(arma).ToUpperInvariant(), 21, Palette.TextPrimary);

        this.StatTrio(cuerpo.Row(0f, 20f),
            $"+{arma.Damage}%", $"+{arma.Range}%", $"+{arma.Cadence}%");

        // La habilidad se queda con el resto del cuerpo entero: es la línea que
        // decide si el arma vale, y la que más se cortaba cuando arriba había
        // tres renglones de stats.
        this.Paragraph(cuerpo.Row(24f, cuerpo.Size.Y - 24f),
            arma.Ability == WeaponAbility.None
                ? Loc.Get("ui.tienda.sin_habilidad")
                : Loc.AbilityText(arma.Ability, arma.Magnitude),
            13, arma.Ability == WeaponAbility.None ? Palette.TextDisabled : Palette.Epic);
    }
}
