using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using TowerOfArgentum.Audio;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El menú fuera de la corrida: jugar, tienda de planos, ranking y billetera.
/// </summary>
/// <remarks>
/// <para>Los cuatro paneles tocan el servidor, y esa es la diferencia con todo lo
/// demás: dentro de la corrida no hay red, todo pasa en el cliente. Acá es al
/// revés (INTERFAZ.md §4).</para>
///
/// <para>Usa la misma disposición que el resto (<see cref="UiLayout"/>): la barra
/// de arriba con el título y la billetera, la <b>columna izquierda</b> con los
/// botones —donde en la corrida está el roster— y el resto del ancho como panel
/// de contenido, que es donde se pintan el ranking y la tienda de planos.</para>
/// </remarks>
public partial class MainScreen : Control
{
    private Label _billetera = null!;
    private Label _mensaje = null!;
    private VBoxContainer _panel = null!;
    private Button _jugar = null!;

    private string[] _planos = [];
    private long _oro;

    /// <summary>Qué se está mostrando en el panel de contenido.</summary>
    /// <remarks>
    /// ⚠ <b>El ranking es de nodos y la tienda es dibujada.</b> No es inconsistencia
    /// por descuido: el ranking son veinte renglones de texto monoespaciado que un
    /// <c>VBoxContainer</c> resuelve solo, y la tienda son nueve tarjetas con
    /// icono, receta y precio, que es exactamente lo que <see cref="UiStyle"/>
    /// sabe dibujar y un <c>Label</c> no. Lo que sí importa es que <b>nunca se
    /// vean las dos</b>, y de eso se ocupa esto.
    /// </remarks>
    private enum Modo { Nada, Ranking, Blueprints }

    private Modo _modo = Modo.Nada;

    /// <summary>
    /// Los precios, tal como los mandó el servidor. Vacío = todavía no llegaron.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>El precio no puede vivir en el cliente.</b> <c>Data\planos.json</c>
    /// entra al hash de <c>/run/start</c>, así que corregir un precio ahí
    /// invalidaría la temporada del ranking. Se piden una vez por sesión a
    /// <c>GET /tienda/planos</c> y se guardan acá.
    /// </remarks>
    private readonly Dictionary<string, int> _precios = new(System.StringComparer.Ordinal);

    /// <summary>La tarjeta bajo el cursor, o -1.</summary>
    private int _bajoElCursor = -1;

    /// <summary>
    /// El plano cuyo botón ya se apretó una vez y espera la confirmación.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Dos clics, como vender un defensor.</b> Esto gasta oro de la
    /// <b>billetera</b>, que es progresión permanente y tarda corridas enteras en
    /// juntarse; un click de más sobre la tarjeta equivocada no se puede deshacer
    /// —un plano comprado no se vende— así que el segundo click no es una molestia,
    /// es lo único que hay entre el jugador y un error irreversible.
    /// </remarks>
    private string? _armado;

    /// <summary>Hay una compra en vuelo: no se aceptan más clics.</summary>
    private bool _comprando;

    /// <summary>Se dispara al empezar una corrida, con el id y la seed del servidor.</summary>
    public event System.Action<long, ulong>? StartRun;

    /// <summary>Hay un guardado y el jugador quiere retomarlo.</summary>
    public event System.Action? ContinueRun;

    private Button _continuar = null!;

    /// <summary>El panel de opciones. El mismo que abre el menú de ESC.</summary>
    private OptionsScreen _opciones = null!;

    /// <summary>
    /// El panel de contenido: desde la columna del inventario hasta el borde.
    /// </summary>
    /// <remarks>
    /// Ocupa las tres columnas de la derecha del diseño juntas. El ranking es una
    /// lista de veinte líneas anchas y la tienda de planos son nueve filas con un
    /// botón: ninguna de las dos entra en una columna de 350 px, y partirlas en
    /// tres sería inventar una disposición que el diseño no tiene.
    /// </remarks>
    private static Rect2 PanelDeContenido()
        => new(
            UiLayout.InventoryPanel.Position,
            new Vector2(UiLayout.ShopPanel.End.X - UiLayout.InventoryPanel.Position.X,
                UiLayout.InventoryPanel.Size.Y));

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = UiTheme.Current;

        var columna = UiLayout.HeroesPanel;

        var caja = new VBoxContainer
        {
            Position = new Vector2(columna.Position.X + 16f, UiLayout.HeroCard(0).Position.Y),
            CustomMinimumSize = new Vector2(columna.Size.X - 32f, 0),
        };

        caja.AddThemeConstantOverride("separation", 10);
        AddChild(caja);

        // La billetera es un nodo y no dibujo a mano porque su texto llega del
        // servidor y cambia solo: dejarla como Label evita tener que redibujar
        // la pantalla entera cuando contesta /perfil/me.
        _billetera = new Label { Text = "…" };
        _billetera.AddThemeColorOverride("font_color", Palette.Gold);
        _billetera.AddThemeFontSizeOverride("font_size", 22);
        _billetera.Position = new Vector2(UiLayout.TowerHp.Position.X, UiLayout.TowerHp.Position.Y + 14f);
        AddChild(_billetera);

        // Continuar arriba de Jugar: si hay una corrida a medias, retomarla es
        // lo que el jugador vino a hacer — son horas de partida. Y Jugar con un
        // guardado presente ABANDONA esa corrida, así que el orden también avisa.
        _continuar = Boton(Loc.Get("ui.principal.continuar"), () => ContinueRun?.Invoke());
        caja.AddChild(_continuar);

        _jugar = Boton(Loc.Get("ui.principal.jugar"), () => _ = JugarAsync());
        caja.AddChild(_jugar);

        caja.AddChild(Boton(Loc.Get("ui.principal.tienda"), () => _ = MostrarTiendaAsync()));
        caja.AddChild(Boton(Loc.Get("ui.principal.ranking"), () => _ = MostrarRankingAsync()));
        caja.AddChild(Boton(Loc.Get("ui.principal.opciones"), () => _opciones.Open()));
        caja.AddChild(Boton(Loc.Get("ui.principal.salir_cuenta"), () => _ = SalirAsync()));

        // ⚠ Cerrar el juego desde el menú. Antes solo se salía con la X o con
        // Alt+F4; que un juego de pantalla completa no tenga salida propia es de
        // las cosas que más rápido se notan.
        caja.AddChild(Boton(Loc.Get("ui.principal.salir_juego"), () => GetTree().Quit()));

        // El mensaje va al pie, que es donde el diseño pone el consejo: es el
        // mismo tipo de texto —una línea de estado que se lee de reojo— y así no
        // corre los botones cada vez que aparece.
        _mensaje = new Label { Text = "" };
        _mensaje.AddThemeColorOverride("font_color", Palette.TextMuted);
        _mensaje.AddThemeFontSizeOverride("font_size", 15);
        _mensaje.Position = UiLayout.FooterTip.Position + new Vector2(0, 4f);
        AddChild(_mensaje);

        // El panel de la derecha: acá se pinta el ranking o la tienda, sin
        // navegar a otra pantalla. Es lo que hace que las "cuatro pantallas" del
        // diseño sean en realidad una sola con paneles.
        var contenido = PanelDeContenido();

        _panel = new VBoxContainer
        {
            Position = contenido.Position + new Vector2(24f, 20f),
            CustomMinimumSize = new Vector2(contenido.Size.X - 48f, 0),
        };

        _panel.AddThemeConstantOverride("separation", 6);
        AddChild(_panel);

        // Último hijo: su _Input corre antes que el del resto y queda dibujado
        // encima de los botones, que es lo que un panel modal tiene que hacer.
        _opciones = new OptionsScreen { Name = "Opciones" };
        AddChild(_opciones);
    }

    public override void _Notification(int que)
    {
        // Al volver de una corrida, la billetera cambió: se recarga sola en vez
        // de mostrar un número viejo.
        if (que == NotificationVisibilityChanged && Visible)
            _ = RefreshAsync();
    }

    /// <summary>El fondo, la barra de arriba, la columna y el panel de contenido.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, Canvas.CanvasWidth, Canvas.CanvasHeight), Palette.BackgroundMain);

        this.Panel(UiLayout.TopBar, opacidad: 1f);

        this.Label(UiLayout.WaveStatus, Loc.Get("ui.principal.titulo"), 30, Palette.TextPrimary);

        this.Panel(UiLayout.HeroesPanel, opacidad: 1f);
        this.SectionTitle(UiLayout.HeroesTitle, Loc.Get("ui.principal.jugar"));

        this.Panel(PanelDeContenido(), opacidad: 1f);

        if (_modo == Modo.Blueprints)
            DibujarTienda();

        this.Panel(UiLayout.FooterBar, opacidad: 1f);
        this.RarityLegend(UiLayout.FooterLegend);
    }

    /// <summary>
    /// Un botón del menú. <b>Todos los del menú pasan por acá.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ El click suena en esta fábrica y no en cada <c>alApretar</c>, que es la
    /// única forma de que no se olvide en el próximo botón que alguien agregue. Y
    /// va antes de la acción a propósito: varias de esas acciones son
    /// <c>async</c> y abren un panel después de una vuelta al servidor —el
    /// sonido tiene que confirmar el click ya, no cuando conteste la API—.
    /// </remarks>
    private static Button Boton(string texto, System.Action alApretar)
    {
        var b = new Button
        {
            Text = texto,
            CustomMinimumSize = new Vector2(UiLayout.HeroesPanel.Size.X - 32f, 46),
        };

        b.Pressed += () =>
        {
            Sfx.Click();
            alApretar();
        };

        b.MouseEntered += () => Sfx.Play(SoundId.UiHover);

        return b;
    }

    public async Task RefreshAsync()
    {
        // El botón de continuar existe si hay algo que continuar, en disco o en la
        // nube. La consulta a la nube va después de pintar lo local: en el caso
        // normal —guardado en disco— el botón aparece sin esperar a la red.
        _continuar.Visible = SaveFile.Exists();

        if (!_continuar.Visible && Net.ApiClient.LoggedIn)
        {
            var (hayNube, _, _) = await Net.ApiClient.CurrentRunAsync();
            _continuar.Visible = hayNube;
        }

        var (ok, perfil, error) = await Net.ApiClient.ProfileAsync();

        if (!ok || perfil is null)
        {
            _billetera.Text = Loc.Get("ui.principal.sin_conexion");
            _mensaje.Text = error ?? "";
            return;
        }

        _oro = perfil.Gold;
        _planos = perfil.Blueprints;

        _billetera.Text =
            $"{perfil.Nick} · {perfil.Gold} {Loc.Get("ui.principal.de_oro")}" +
            $" · {perfil.Blueprints.Length} {Loc.Get("ui.principal.planos")}";
        _mensaje.Text = "";
    }

    private async Task JugarAsync()
    {
        _jugar.Disabled = true;
        _mensaje.Text = Loc.Get("ui.principal.abriendo");

        // ⚠ El hash de los datos viaja acá. Si el cliente juega con otros números
        // que el servidor, la corrida no sería reproducible ni comparable, y es
        // infinitamente mejor rechazarla ahora que después de tres horas.
        var (ok, datos, error) = await Net.ApiClient.StartRunAsync(
            DataLoader.Current.Hash, Device.Kind);

        _jugar.Disabled = false;

        if (!ok || datos is null)
        {
            _mensaje.Text = error ?? Loc.Get("ui.principal.error_abrir");
            return;
        }

        if (datos.Abandoned is > 0)
        {
            // Una sola corrida abierta por cuenta: abrir esta cerró la anterior.
            // Se dice, porque pueden haber sido horas de partida.
            _mensaje.Text = Loc.Get("ui.principal.corrida_abandonada", (int)datos.Abandoned.Value);
        }

        StartRun?.Invoke(datos.RunId, datos.Seed);
    }

    private async Task MostrarRankingAsync()
    {
        Limpiar();

        // La tienda es dibujada y el ranking es de nodos: apagar el modo es lo
        // que evita que la grilla de tarjetas quede pintada atrás de la tabla.
        _modo = Modo.Ranking;
        _armado = null;
        QueueRedraw();

        _mensaje.Text = Loc.Get("ui.principal.cargando_ranking");

        var (ok, datos, error) = await Net.ApiClient.RankingAsync(20);

        if (!ok || datos is null)
        {
            _mensaje.Text = error ?? Loc.Get("ui.principal.error_ranking");
            return;
        }

        _mensaje.Text = "";
        _panel.AddChild(Titulo(Loc.Get("ui.principal.ranking_temporada", datos.Version)));

        // El orden es gano DESC, oleada DESC, ticks ASC: arriba los que ganaron y
        // entre ellos el más rápido. Se muestran las dos columnas crudas, sin
        // fórmula compuesta que haya que explicar.
        foreach (var fila in datos.Rows)
        {
            int segundos = fila.Ticks / Simulation.Ints.TicksPerSecond;

            var l = new Label
            {
                Text = $"{fila.Position,3}. {fila.Nick,-20} " +
                       $"{(fila.Won ? Loc.Get("ui.principal.gano") : Loc.Get("ui.principal.oleada_corta", fila.Wave)),-8} " +
                       $"{segundos / 60:00}:{segundos % 60:00}" +
                       (fila.Validated ? "  ✓" : ""),
            };

            if (fila.Won)
                l.AddThemeColorOverride("font_color", Palette.Success);

            _panel.AddChild(l);
        }

        if (datos.Rows.Length == 0)
            _panel.AddChild(new Label { Text = Loc.Get("ui.principal.sin_corridas") });
    }

    // =====================================================================
    // La tienda de planos
    // =====================================================================

    /// <summary>
    /// Abre la tienda: pide el perfil y, la primera vez, los precios.
    /// </summary>
    /// <remarks>
    /// Los precios se piden <b>una sola vez por sesión</b>: no dependen de la
    /// cuenta ni del oro, y volver a pedirlos en cada compra sería un viaje de red
    /// para recibir lo mismo. Lo que sí se recarga en cada compra es el perfil,
    /// que es lo que cambió.
    /// </remarks>
    private async Task MostrarTiendaAsync()
    {
        Limpiar();

        _modo = Modo.Blueprints;
        _armado = null;
        _bajoElCursor = -1;
        QueueRedraw();

        await RefreshAsync();

        if (_precios.Count == 0)
        {
            var (ok, lista, error) = await Net.ApiClient.BlueprintsAsync();

            if (ok && lista?.Blueprints is not null)
            {
                foreach (var fila in lista.Blueprints)
                    _precios[fila.Key] = fila.Price;
            }
            else
            {
                // Sin precios las tarjetas se dibujan igual, apagadas y sin botón:
                // saber qué planos existen y de qué son las recetas sirve aunque
                // hoy no se pueda comprar ninguno.
                _mensaje.Text = error ?? Loc.Get("ui.principal.error_precios");
            }
        }

        QueueRedraw();
    }

    /// <summary>En qué situación está un plano para esta cuenta.</summary>
    private enum EstadoPlano
    {
        /// <summary>Se sabe de arranque. No se vende.</summary>
        Inicial,

        /// <summary>Ya comprado, para siempre.</summary>
        Aprendido,

        /// <summary>Se puede comprar: el servidor le puso precio.</summary>
        EnVenta,

        /// <summary>
        /// El cliente lo conoce pero el servidor no lo vende. Se dibuja apagado y
        /// <b>no se esconde</b>: un plano que desaparece de la lista se lee como
        /// un plano que no existe, y lo que pasa en realidad es que falta un
        /// precio del otro lado.
        /// </summary>
        SinPrecio,
    }

    private EstadoPlano EstadoDe(Blueprint plano)
    {
        if (plano.Initial)
            return EstadoPlano.Inicial;

        // ⚠ La identidad que viaja a la API es la KEY, no el nombre: el nombre
        // vive en los archivos de idioma y cambia con el idioma, así que un
        // jugador en inglés compraría un plano que el servidor no conoce.
        if (System.Array.IndexOf(_planos, plano.Key) >= 0)
            return EstadoPlano.Aprendido;

        return _precios.ContainsKey(plano.Key) ? EstadoPlano.EnVenta : EstadoPlano.SinPrecio;
    }

    private int PrecioDe(string key) => _precios.TryGetValue(key, out int precio) ? precio : 0;

    // ---------------------------------------------------------------------
    // Geometría
    // ---------------------------------------------------------------------

    private const int ColumnasPlanos = 3;
    private const float HuecoPlanos = 16f;

    /// <summary>
    /// Cuántas filas tiene la grilla como mínimo.
    /// </summary>
    /// <remarks>
    /// El alto de la tarjeta sale del <b>máximo</b> entre esto y lo que haya, igual
    /// que en el mercado: con cuatro planos, dividir el panel por dos daría
    /// tarjetas del doble de alto que las de la pantalla de al lado.
    /// </remarks>
    private const int FilasMinimas = 3;

    /// <summary>El rectángulo útil: el panel de contenido menos el margen.</summary>
    private static Rect2 CajaPlanos() => PanelDeContenido().Inset(20f);

    private static Rect2 TarjetaPlano(int i, int total)
    {
        var caja = CajaPlanos();

        // 56 = el título de la sección más el renglón de ayuda.
        float y0 = caja.Position.Y + 56f;
        float disponible = caja.End.Y - y0;

        int filas = Mathf.Max(FilasMinimas, (total + ColumnasPlanos - 1) / ColumnasPlanos);

        float ancho = (caja.Size.X - (HuecoPlanos * (ColumnasPlanos - 1))) / ColumnasPlanos;
        float alto = (disponible - (HuecoPlanos * (filas - 1))) / filas;

        return new Rect2(
            caja.Position.X + ((i % ColumnasPlanos) * (ancho + HuecoPlanos)),
            y0 + ((i / ColumnasPlanos) * (alto + HuecoPlanos)),
            ancho, alto);
    }

    /// <summary>
    /// Las cuatro partes de una tarjeta apaisada. <b>Las mismas que el mercado.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ El icono se topea en 96 px de lado y no ocupa todo el alto de la tarjeta.
    /// Con el cuadrado grande, la columna de texto quedaba en 287 px y un nombre
    /// como «Terrible Hambre de Igôr» entraba recortado con puntos suspensivos —
    /// que en una tienda es exactamente el dato que no se puede recortar.
    /// </remarks>
    private static (Rect2 Icono, Rect2 Titulo, Rect2 Nombre, Rect2 Body) PartesPlano(Rect2 r)
    {
        float lado = Mathf.Min(r.Size.Y - 20f, 96f);

        var icono = new Rect2(r.Position.X + 10f,
            r.Position.Y + ((r.Size.Y - lado) / 2f), lado, lado);

        float x = icono.End.X + 14f;
        float ancho = r.End.X - x - 12f;

        return (
            icono,
            new Rect2(x, r.Position.Y + 8f, ancho, 18f),
            new Rect2(x, r.Position.Y + 26f, ancho, 28f),
            new Rect2(x, r.Position.Y + 58f, ancho, r.Size.Y - 96f));
    }

    /// <summary>El botón de comprar de una tarjeta. Abajo a la izquierda del texto.</summary>
    private static Rect2 RectComprar(Rect2 r)
    {
        var (_, _, nombre, _) = PartesPlano(r);

        return new Rect2(nombre.Position.X, r.End.Y - 38f, 150f, 30f);
    }

    // ---------------------------------------------------------------------
    // Dibujo
    // ---------------------------------------------------------------------

    /// <summary>
    /// La tienda entera: título, ayuda y la grilla de tarjetas.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Comprados y sin comprar en la MISMA grilla</b>, y el estado se
    /// dice con el marco y con el pie, no sacando la tarjeta de la lista. Antes
    /// eran tres formatos distintos —una línea suelta para el inicial, otra para
    /// el aprendido y una fila con botón para el resto—, así que la lista cambiaba
    /// de forma según lo que el jugador ya tuviera y no se podía comparar nada
    /// contra nada.</para>
    ///
    /// <para>Y la receta va <b>en la tarjeta</b>: un plano no da poder hoy, lo que
    /// se compra es la posibilidad de cerrar una fusión más adelante. Sin los dos
    /// componentes a la vista, elegir cuál de nueve comprar con la plata de tres
    /// corridas es adivinar.</para>
    /// </remarks>
    private void DibujarTienda()
    {
        var caja = CajaPlanos();
        var todos = DataLoader.Current.Blueprints.All;

        UiTooltip.Begin(GetGlobalMousePosition());

        this.SectionTitle(caja.Row(0f, 30f), Loc.Get("ui.principal.tienda"), Palette.Unique);
        this.Label(caja.Row(32f, 20f), Loc.Get("ui.principal.tienda_ayuda"), 14, Palette.TextMuted);

        for (int i = 0; i < todos.Count; i++)
            DibujarPlano(todos[i], TarjetaPlano(i, todos.Count).Inset(4f), i == _bajoElCursor);

        // Último de todo: el cartel tiene que quedar encima de las tarjetas.
        UiTooltip.Draw(this);
    }

    /// <summary>
    /// Una tarjeta: icono del Único, nombre, qué hace, la receta y el precio.
    /// </summary>
    /// <remarks>
    /// El color dice el estado antes de leer una palabra: <b>verde</b> con tilde
    /// es «ya lo tenés», <b>violeta</b> es «se puede comprar», <b>gris con el
    /// precio en rojo</b> es «no te alcanza». Y la tarjeta se ve entera en los
    /// tres casos — saber que existe un plano que hoy no se puede pagar es lo que
    /// hace que juntar oro sea una decisión.
    /// </remarks>
    private void DibujarPlano(Blueprint plano, Rect2 r, bool encima)
    {
        var datos = DataLoader.Current;
        var unico = datos.Perks.Get(plano.ResultingUnique);

        var estado = EstadoDe(plano);
        bool tiene = estado is EstadoPlano.Inicial or EstadoPlano.Aprendido;

        int precio = PrecioDe(plano.Key);
        bool alcanza = precio > 0 && _oro >= precio;
        bool comprable = estado == EstadoPlano.EnVenta && alcanza && !_comprando;

        var acento =
            tiene ? Palette.Success
            : estado == EstadoPlano.EnVenta && alcanza ? Palette.Unique
            : Palette.TextDisabled;

        // ⚠ «elegido» solo para lo que ya se tiene: esa marca incluye un tilde, y
        // ponérsela también al hover diría «comprado» sobre algo que no lo está.
        // Lo de abajo del cursor se ilumina con el resplandor, que no afirma nada.
        this.Slot(r, acento, elegido: tiene);

        if (encima && comprable)
            this.Glow(r, Palette.Unique, 2.4f);

        var (icono, titulo, nombre, cuerpo) = PartesPlano(r);

        // El icono conserva el violeta de Único siempre: es la identidad del ítem,
        // y teñirlo de verde al comprarlo lo haría irreconocible contra la mochila.
        this.Slot(icono, Palette.Unique);

        if (!IconLoader.Draw(this, unico.Key,
                icono.Position.X + 8f, icono.Position.Y + 8f, icono.Size.X - 16f))
        {
            this.Label(icono, Loc.Perk(unico)[..1].ToUpperInvariant(),
                (int)(icono.Size.X * 0.5f), Palette.Unique, HorizontalAlignment.Center);
        }

        this.Label(titulo, Loc.Tier(PerkTier.Unique).ToUpperInvariant(), 13, Palette.Unique);
        this.Label(nombre, Loc.Perk(unico).ToUpperInvariant(), 20, Palette.TextPrimary);

        this.EffectRow(cuerpo.Row(0f, 20f), unico, tiene ? Palette.Success : Palette.Unique);

        this.Label(cuerpo.Row(23f, 16f), Loc.Get("ui.principal.receta"), 12, Palette.TextMuted);

        Componente(cuerpo.Row(40f, 19f), datos.Perks.Get(plano.GoldA));
        Componente(cuerpo.Row(60f, 19f), datos.Perks.Get(plano.GoldB));

        Pie(plano, r, nombre, estado, precio, alcanza, comprable, encima);
    }

    /// <summary>
    /// Un componente de la receta: icono del efecto y nombre, en color de Oro.
    /// </summary>
    /// <remarks>
    /// En color de Oro y no en gris porque eso <b>es</b> el dato: los dos
    /// componentes son perks de Oro, que caen de los jefes y salen carísimos en la
    /// tienda de la corrida. El color dice de dónde hay que sacarlos.
    /// </remarks>
    private void Componente(Rect2 r, PerkDefinition def)
    {
        float lado = Mathf.Min(r.Size.Y, 16f);
        var caja = new Rect2(r.Position.X, r.Position.Y + ((r.Size.Y - lado) / 2f), lado, lado);

        this.Icon(caja, UiWidgets.IconFor(def.Effect), Palette.Legendary);

        this.Label(new Rect2(caja.End.X + 8f, r.Position.Y, r.End.X - caja.End.X - 8f, r.Size.Y),
            Loc.Perk(def), 13, Palette.TextSecondary);

        UiTooltip.Over(r, $"{Loc.Perk(def)} · {Loc.Tier(PerkTier.Gold)} · {Loc.Effect(def.Effect)}");
    }

    /// <summary>
    /// El pie de la tarjeta: el botón a la izquierda y el estado a la derecha.
    /// </summary>
    /// <remarks>
    /// ⚠ El precio va <b>siempre en el mismo lugar</b> en las nueve tarjetas, como
    /// en el mercado: es lo que permite compararlos de un vistazo en vez de
    /// buscarlos.
    /// </remarks>
    private void Pie(
        Blueprint plano, Rect2 r, Rect2 nombre, EstadoPlano estado,
        int precio, bool alcanza, bool comprable, bool encima)
    {
        var pie = new Rect2(nombre.Position.X, r.End.Y - 36f, nombre.Size.X, 26f);

        switch (estado)
        {
            case EstadoPlano.Inicial:
                this.Label(pie, Loc.Get("ui.principal.plano_inicial"), 15, Palette.Success,
                    HorizontalAlignment.Right);
                return;

            case EstadoPlano.Aprendido:
                this.Label(pie, Loc.Get("ui.principal.plano_aprendido"), 16, Palette.Success,
                    HorizontalAlignment.Right);
                return;

            case EstadoPlano.SinPrecio:
                this.Label(pie, Loc.Get("ui.principal.sin_precio"), 13, Palette.TextDisabled,
                    HorizontalAlignment.Right);
                return;
        }

        this.Price(pie, precio, alcanza, HorizontalAlignment.Right);

        if (!alcanza)
            return;

        // ⚠ Armado = ya se apretó una vez. El segundo click compra. Es el mismo
        // gesto de dos pasos que vender un defensor, y por el mismo motivo: lo que
        // está del otro lado no se puede deshacer.
        bool armado = _armado == plano.Key;

        this.Button(RectComprar(r),
            Loc.Get(armado ? "ui.principal.confirmar_compra" : "ui.principal.comprar"),
            armado ? Palette.Danger : Palette.Unique,
            habilitado: comprable,
            relleno: armado,
            encima: encima && comprable);
    }

    // ---------------------------------------------------------------------
    // Entrada
    // ---------------------------------------------------------------------

    /// <summary>
    /// El hover y los clics de la tienda.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la cicatriz de
    /// <see cref="MarketScreen"/>: con el hook de GUI el control queda sordo
    /// porque nunca toma el foco, y la posición del evento viene en píxeles de
    /// ventana, que con <c>stretch = keep</c> no coincide con el lienzo.</para>
    ///
    /// <para>⚠ <b>Solo se marca atendido el click que cae en un botón de tarjeta.</b>
    /// Los botones de la columna izquierda son nodos <c>Button</c> de verdad, y
    /// <c>_Input</c> corre <i>antes</i> que el reparto de GUI: comerse todo click
    /// dejaría el menú entero sin funcionar.</para>
    /// </remarks>
    public override void _Input(InputEvent evento)
    {
        // El panel de opciones es modal y se dibuja encima. Que además esté
        // registrado como último hijo no alcanza: ahí se decide quién atiende
        // primero, no quién puede ignorar el evento.
        if (_modo != Modo.Blueprints || _opciones.Visible)
            return;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = PlanoEn(GetGlobalMousePosition());
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Apretar(GetGlobalMousePosition());
                break;
        }
    }

    private static int PlanoEn(Vector2 posicion)
    {
        int total = DataLoader.Current.Blueprints.All.Count;

        for (int i = 0; i < total; i++)
        {
            if (TarjetaPlano(i, total).Inset(4f).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    private void Apretar(Vector2 posicion)
    {
        var todos = DataLoader.Current.Blueprints.All;

        for (int i = 0; i < todos.Count; i++)
        {
            var r = TarjetaPlano(i, todos.Count).Inset(4f);

            if (!RectComprar(r).HasPoint(posicion))
                continue;

            int precio = PrecioDe(todos[i].Key);

            if (_comprando || EstadoDe(todos[i]) != EstadoPlano.EnVenta || _oro < precio)
                break;

            if (_armado == todos[i].Key)
            {
                _armado = null;
                _ = ComprarAsync(todos[i].Key);
            }
            else
            {
                _armado = todos[i].Key;
            }

            QueueRedraw();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Cualquier otro click desarma: apuntar a otra tarjeta y arrepentirse
        // tiene que ser gratis, y un botón que queda armado a la espera es una
        // trampa esperando el próximo click distraído.
        if (_armado is null)
            return;

        _armado = null;
        QueueRedraw();
    }

    private async Task ComprarAsync(string plano)
    {
        _comprando = true;
        _mensaje.Text = Loc.Get("ui.principal.comprando");
        QueueRedraw();

        // ⚠ Se manda SOLO la key. El precio sale de la lista del servidor: si lo
        // mandara el cliente, se compraría todo a un peso.
        var (ok, _, error) = await Net.ApiClient.BuyBlueprintAsync(plano);

        _comprando = false;

        // Recarga el perfil y no la tienda: lo que cambió es el oro y la lista de
        // planos de la cuenta. Los precios son los mismos.
        await RefreshAsync();

        _mensaje.Text = ok
            ? $"{Loc.Get("ui.principal.aprendiste")} {Loc.Get($"perk.{plano}.name")}"
            : error ?? Loc.Get("ui.principal.error_comprar");

        QueueRedraw();
    }

    private async Task SalirAsync()
    {
        await Net.ApiClient.LogoutAsync();
        GetTree().ReloadCurrentScene();
    }

    private static Label Titulo(string texto)
    {
        var l = new Label { Text = texto };
        l.AddThemeFontSizeOverride("font_size", 28);
        return l;
    }

    private void Limpiar()
    {
        foreach (var hijo in _panel.GetChildren())
            hijo.QueueFree();
    }
}
