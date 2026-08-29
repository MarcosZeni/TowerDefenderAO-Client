using System.Collections.Generic;
using Godot;
using TowerOfArgentum.Simulacion;
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
public partial class PantallaMercado : Control
{
    private readonly Juego _juego;

    /// <summary>Qué es una tarjeta: una opción gratis, o una oferta de la tienda.</summary>
    private readonly record struct Ficha(bool Gratis, int Indice);

    /// <summary>Las tres zonas, en el orden en que se dibujan de izquierda a derecha.</summary>
    private readonly List<Ficha>[] _columnas = [[], [], []];

    private (int Columna, int Fila) _bajoElCursor = (-1, -1);

    /// <summary>
    /// El jugador cerró la ventana a mano y no quiere verla hasta la próxima.
    /// </summary>
    /// <remarks>
    /// Se resetea solo cuando cambia el stock: una tienda nueva es una decisión
    /// nueva, y arrastrar el «cerrado» de la anterior la escondería sin avisar.
    /// </remarks>
    private bool _cerrada;

    private int _stockVisto = -1;

    public PantallaMercado(Juego juego)
    {
        _juego = juego;
    }

    // =====================================================================
    // Cuándo se ve
    // =====================================================================

    /// <summary>Hay una elección pendiente: no se puede cerrar.</summary>
    private bool HayEleccion => _juego.Run?.WaitingForChoice ?? false;

    /// <summary>Hay stock de la tienda de esta preparación.</summary>
    private bool HayTienda
    {
        get
        {
            var p = _juego.Run;

            return p is { State: RunState.InProgress, WaitingForWaveStart: true }
                   && p.Shop.OleadaDelStock == p.ProximaOleada
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
    public bool Abierta
        => _juego.Backpack is not { Abierta: true }
           && (HayEleccion || (HayTienda && !_cerrada));

    /// <summary>La abre el botón MERCADO de la barra de preparación.</summary>
    public void Abrir() => _cerrada = false;

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
        if (p is not null && p.Shop.OleadaDelStock != _stockVisto)
        {
            _stockVisto = p.Shop.OleadaDelStock;
            _cerrada = false;
        }

        bool abierta = Abierta;

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
    /// <para>El Oro que ofrece un jefe cae en la zona de <b>perks</b> y no en la de
    /// personajes, aunque venga por el mismo camino que contratar: lo que se elige
    /// es un perk, y ponerlo entre las clases obligaría a leer el rótulo para
    /// saber qué es.</para>
    /// </remarks>
    private void Repartir(Run p)
    {
        foreach (var c in _columnas)
            c.Clear();

        for (int i = 0; i < p.OpenChoices.Length; i++)
        {
            int zona = p.OpenChoices[i].Kind == ChoiceType.Perk ? 1 : 0;
            _columnas[zona].Add(new Ficha(true, i));
        }

        if (!HayTienda)
            return;

        var stock = p.Shop.Stock;

        for (int i = 0; i < stock.Count; i++)
        {
            int zona = stock[i].Kind switch
            {
                ShopKind.Class => 0,
                ShopKind.Weapon => 2,
                _ => 1,
            };

            _columnas[zona].Add(new Ficha(false, i));
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
        float ancho = (Escala.LienzoAncho - (MargenX * 2f) - (Hueco * 2f)) / 3f;

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
        => new(Escala.LienzoAncho - MargenX - 200f, 92f, 200f, 44f);

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
        if (!Abierta)
            return;

        var p = _juego.Run!;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = TarjetaEn(GetGlobalMousePosition());
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                var posicion = GetGlobalMousePosition();

                if (!HayEleccion && RectCerrar().HasPoint(posicion))
                {
                    _cerrada = true;
                    GetViewport().SetInputAsHandled();
                    break;
                }

                if (HayQuePagar() && RectPasar().HasPoint(posicion))
                {
                    _juego.SaltearEleccion();
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
    /// Las teclas 1-2-3 y el Esc.
    /// </summary>
    /// <remarks>
    /// ⚠ Los números eligen <b>lo gratis</b>, no la tarjeta N de la pantalla. Con
    /// una corrida de horas, resolver la elección sin soltar el mouse importa; y
    /// que el 2 comprara a veces un arma de 900 sería el peor atajo posible.
    /// </remarks>
    private void AtenderTecla(Run p, InputEventKey tecla)
    {
        if (tecla.Keycode == Key.Escape)
        {
            if (HayEleccion)
                return;

            _cerrada = true;
            GetViewport().SetInputAsHandled();
            return;
        }

        int i = tecla.Keycode switch
        {
            Key.Key1 => 0,
            Key.Key2 => 1,
            Key.Key3 => 2,
            _ => -1,
        };

        if (i >= 0 && i < p.OpenChoices.Length)
        {
            _juego.Choose(i);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Apretar(Ficha ficha)
    {
        if (!ficha.Gratis)
        {
            _juego.Comprar(ficha.Indice);
            return;
        }

        // ⚠ Lo que no se puede pagar no se manda. La simulación lo rebota igual
        // —y sin cerrar la pantalla, a propósito— pero un click que no hace nada
        // ni dice por qué se lee como que la pantalla está rota.
        if (_juego.Run!.OpenChoices[ficha.Indice].Price <= _juego.Run.Gold)
            _juego.Choose(ficha.Indice);
    }

    /// <summary>
    /// El botón de saltear la elección.
    /// </summary>
    /// <remarks>
    /// ⚠ Aparece <b>solo cuando la elección cuesta</b>. Mientras regala no hay
    /// razón para no llevarse algo, y un botón de pasar ahí sería una trampa; en
    /// cuanto cobra, no tenerlo dejaría a un jugador sin oro trabado en un modal
    /// que no se cierra y con el reloj congelado.
    /// </remarks>
    private bool HayQuePagar()
    {
        var p = _juego.Run;

        if (p is null || !p.WaitingForChoice)
            return false;

        foreach (var o in p.OpenChoices)
        {
            if (o.Price > 0)
                return true;
        }

        return false;
    }

    private static Rect2 RectPasar()
        => new(Escala.LienzoAncho - MargenX - 200f, 92f, 200f, 44f);

    // =====================================================================
    // Dibujo
    // =====================================================================

    public override void _Draw()
    {
        var p = _juego.Run;

        if (p is null || !Abierta)
            return;

        UiTooltip.Comenzar(GetGlobalMousePosition());

        // El mundo se oscurece pero se sigue viendo: las dos decisiones se toman
        // mirando la defensa, no de memoria.
        this.Scrim();

        Cabecera(p);

        for (int c = 0; c < _columnas.Length; c++)
            DibujarColumna(p, c);

        Pie(p);

        // Último de todo: el cartel tiene que quedar encima de las tarjetas.
        UiTooltip.Dibujar(this);
    }

    private void Cabecera(Run p)
    {
        var titulo = new Rect2(0, 44f, Escala.LienzoAncho, 48f);

        this.Label(titulo, Loc.Get("ui.mercado.titulo"), 40, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(titulo.Row(50f, 26f),
            HayEleccion
                ? Loc.Get(HayQuePagar() ? "ui.mercado.elegi_o_pasa" : "ui.eleccion.no_se_puede_cerrar")
                : Loc.Get("ui.mercado.esc_para_cerrar"),
            18, HayEleccion ? Palette.Warning : Palette.TextMuted, HorizontalAlignment.Center);

        // El oro va arriba a la izquierda, del tamaño del título: es el número
        // contra el que se lee cada precio de la pantalla.
        this.Price(new Rect2(MargenX, 92f, 260f, 44f), p.Gold, alcanza: true);

        if (!HayEleccion)
        {
            this.Button(RectCerrar(), Loc.Get("ui.mercado.cerrar"), Palette.BorderNormal,
                relleno: false);
        }
        else if (HayQuePagar())
        {
            this.Button(RectPasar(), Loc.Get("ui.mercado.pasar"), Palette.Danger,
                relleno: false);
        }
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

        this.SectionTitle(col.Row(8f, 34f), Loc.Get($"ui.mercado.zona{c}"), ColorDeZona(c));

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
        var pie = new Rect2(MargenX, Escala.LienzoAlto - 84f,
            Escala.LienzoAncho - (MargenX * 2f), 56f);

        this.Panel(pie, opacidad: 1f);

        this.Label(pie.Inset(14f), Loc.Get("ui.tienda.no_se_repone"), 14, Palette.TextMuted);

        this.Label(pie.Inset(14f),
            $"{Loc.Get("ui.tienda.proximas_ofertas")} {p.Shop.OleadaDelStock + 3}",
            14, Palette.TextMuted, HorizontalAlignment.Right);
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
        var datos = CargadorDatos.Actual;

        if (ficha.Gratis)
        {
            DibujarEleccion(p, r, ficha.Indice, encima, datos);
            return;
        }

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

            return;
        }

        bool alcanza = p.Gold >= oferta.Price;
        var acento = AcentoDe(oferta, datos);

        this.Slot(r, acento, elegido: encima && alcanza);

        var (icono, titulo, nombre, cuerpo) = Partes(r);

        this.Label(titulo, EtiquetaDe(oferta).ToUpperInvariant(), 13, acento);

        switch (oferta.Kind)
        {
            case ShopKind.Class:
                DibujarClase(p, (DefenderClass)oferta.Id, icono, nombre, cuerpo, datos);
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
        this.Price(new Rect2(nombre.Position.X, r.End.Y - 30f, nombre.Size.X, 24f),
            oferta.Price, alcanza, HorizontalAlignment.Right);
    }

    /// <summary>Las cuatro partes de una tarjeta apaisada.</summary>
    private static (Rect2 Icono, Rect2 Titulo, Rect2 Nombre, Rect2 Cuerpo) Partes(Rect2 r)
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

    /// <summary>
    /// Una opción de la elección: gratis, y hay que tomar una.
    /// </summary>
    /// <remarks>
    /// ⚠ El marco va <b>siempre iluminado</b>, no solo bajo el cursor: son las
    /// tarjetas que frenan la corrida, y tienen que separarse de las que se pueden
    /// ignorar sin leer el rótulo.
    /// </remarks>
    private void DibujarEleccion(Run p, Rect2 r, int indice, bool encima, BalanceData datos)
    {
        var opcion = p.OpenChoices[indice];

        bool contrata = opcion.Kind == ChoiceType.Hire;
        bool esPerk = opcion.Kind == ChoiceType.Perk;

        var acento =
            esPerk ? Palette.Rarity(datos.Perks.Get(opcion.PerkId).Tier)
            : contrata ? Palette.Info
            : Palette.Warning;

        bool alcanza = opcion.Price <= p.Gold;

        this.Slot(r, alcanza ? acento : Palette.TextDisabled, elegido: alcanza);
        this.Glow(r, acento, encima && alcanza ? 3f : 1.5f);

        var (icono, titulo, nombre, cuerpo) = Partes(r);

        this.Label(titulo,
            (opcion.Price > 0 ? "" : Loc.Get("ui.mercado.gratis") + " · ") +
            Loc.Get(esPerk ? "ui.eleccion.elegi_una"
                : contrata ? "ui.eleccion.contratar"
                : "ui.eleccion.subir_de_nivel").ToUpperInvariant(),
            13, acento);

        if (esPerk)
        {
            DibujarPerk(datos.Perks.Get(opcion.PerkId), icono, nombre, cuerpo, acento);
        }
        else
        {
            // ⚠ Sin el renglón del costo de colocación: esto es un desbloqueo
            // gratis, y el cuerpo de la tarjeta lo necesita entero para decir qué
            // abre —que es la información que hace que la elección sea elección—.
            DibujarClase(p, opcion.DefenderClass, icono, nombre, cuerpo, datos, conCosto: false);
            DibujarQueAbre(p, opcion, contrata, cuerpo);
        }

        // ⚠ El precio va en el MISMO lugar que en las tarjetas de la tienda, y
        // dice GRATIS solo cuando de verdad lo es. Mientras el roster no llega a
        // «freeClasses» la elección regala; pasado eso cobra, con descuento, y
        // esta es la línea donde eso se ve.
        var pie = new Rect2(nombre.Position.X, r.End.Y - 30f, nombre.Size.X, 24f);

        if (opcion.Price > 0)
            this.Price(pie, opcion.Price, alcanza, HorizontalAlignment.Right);
        else
            this.Label(pie, Loc.Get("ui.mercado.gratis").ToUpperInvariant(), 18,
                Palette.Success, HorizontalAlignment.Right);

        // El número de la tecla, abajo a la izquierda del icono.
        var tecla = new Rect2(icono.Position.X, r.End.Y - 30f, 28f, 24f);

        DrawRect(tecla, Palette.BackgroundMain.Alpha(0.9f));
        DrawRect(tecla, Palette.BorderSoft, filled: false, width: 1f);

        this.Label(tecla, $"{indice + 1}", 15, Palette.TextMuted, HorizontalAlignment.Center);
    }

    /// <summary>
    /// Qué desbloquea la opción. <b>Es lo que hace que la elección sea una elección.</b>
    /// </summary>
    /// <remarks>
    /// Sin esta línea contratar parece siempre mejor —trae un rol nuevo— y
    /// levelear parece "solo números". Las dos dan +1 slot de perk, y si eso no se
    /// ve, la decisión «ancho o profundo» se cae sola.
    /// </remarks>
    private void DibujarQueAbre(Run p, Choice opcion, bool contrata, Rect2 cuerpo)
    {
        var fila = cuerpo.Row(42f, 20f);

        if (contrata)
        {
            this.Label(fila, Loc.Get("ui.eleccion.arranca_con_slot"), 14, Palette.TextSecondary);
            return;
        }

        int slotsAntes = p.Roster.Find(opcion.DefenderClass)?.OpenSlots ?? 1;
        int tope = CargadorDatos.Actual.Classes.PerkSlotsPerClass;
        int slotsDespues = System.Math.Min(opcion.ResultingLevel, tope);

        this.Label(fila,
            slotsDespues > slotsAntes
                ? $"{Loc.Get("ui.eleccion.level")} {opcion.ResultingLevel - 1} → " +
                  $"{opcion.ResultingLevel} · {Loc.Get("ui.eleccion.abre_slot")} " +
                  $"{slotsDespues} {Loc.Get("ui.eleccion.de")} {tope}"
                : Loc.Get("ui.eleccion.slots_completos"),
            14, slotsDespues > slotsAntes ? Palette.Legendary : Palette.TextMuted);
    }

    // =====================================================================
    // Los cuatro contenidos
    // =====================================================================

    /// <summary>
    /// El color de cada categoría. Se distinguen antes de leer una palabra.
    /// </summary>
    private static Color AcentoDe(ShopOffer oferta, BalanceData datos) => oferta.Kind switch
    {
        ShopKind.Class => Palette.Info,
        ShopKind.Blueprint => Palette.Epic,
        ShopKind.Weapon => Palette.Common,
        _ => Palette.Rarity(datos.Perks.Get(oferta.Id).Tier),
    };

    private static string EtiquetaDe(ShopOffer oferta) => oferta.Kind switch
    {
        ShopKind.Class => Loc.Get("ui.tienda.reclutar"),
        ShopKind.Blueprint => Loc.Get("ui.tienda.plano"),
        ShopKind.Weapon => Loc.Get("ui.tienda.arma"),
        _ => Loc.Tier(CargadorDatos.Actual.Perks.Get(oferta.Id).Tier),
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

        if (!CargadorIconos.Dibujar(this, def.Key,
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

        if (!CargadorIconos.Dibujar(this, unico.Key,
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
