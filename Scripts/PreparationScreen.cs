using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// La fase entre oleadas: se mira <b>qué viene</b> y se arma el tablero.
/// </summary>
/// <remarks>
/// <para><b>El reloj está detenido.</b> Esperar dos segundos o veintinueve da
/// exactamente los mismos ticks, así que el ranking no castiga pensar. Ver
/// <see cref="Partida.EnPausa"/>.</para>
///
/// <para>⚠ <b>Es una ventana con tarjetas, y antes era una franja de texto.</b>
/// Lo que viene entraba apretado en la barra de abajo del diseño —una fila de
/// nombres con dos renglones chiquitos de aviso— y la información que más
/// importa de la fase quedaba cortada a mitad de palabra: <i>«1 con rasgo
/// (impulsivo o lí…»</i>. Contra un líder conviene concentrar fuego; contra un
/// impulsivo, tener algo listo antes de tiempo. Son dos respuestas distintas y la
/// pantalla no decía cuál.</para>
///
/// <para>La ventana usa el mismo vocabulario que el mercado —tarjeta, marco de
/// acento, icono y número— pero <b>más chica</b>: ocupa la mitad de la altura, y
/// eso es lo que la hace cerrable sin perder nada.</para>
///
/// <para>⚠ <b>Se cierra, y el tablero sigue vivo detrás.</b> Es la contracara de
/// haberla agrandado: la razón por la que esto vivía en la barra de abajo era que
/// una ventana tapa el mapa, y en esta fase el jugador mira <b>dónde queda
/// libre</b>. La salida no es achicar la información: es dejar que la ventana se
/// vaya. La barra de abajo queda con lo mínimo —cuánto oro hay, VER OLEADA,
/// MERCADO y COMENZAR— así que cerrarla nunca deja al jugador sin poder
/// arrancar.</para>
///
/// <para>Y hay botón para arrancar antes: quien acomoda el tablero en cinco
/// segundos no tiene por qué esperar mirando el techo.</para>
/// </remarks>
public partial class PreparationScreen : Control
{
    /// <summary>
    /// Cuánto dura la cuenta regresiva al apretar comenzar, en milisegundos.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No es una constante de balance y no va en <c>Balance</c>.</b>
    /// Ponerla ahí la haría entrar al hash de datos, y cambiarla invalidaría las
    /// corridas viejas por algo que la simulación no mira. Es reloj de pared: el
    /// tick sigue congelado durante la cuenta, y la acción se manda al final.</para>
    ///
    /// <para>Existe para que la oleada no aparezca de golpe encima de la mano que
    /// acaba de soltar el botón.</para>
    /// </remarks>
    private const long MsDeCuentaRegresiva = 3000;

    private readonly Game _juego;

    /// <summary>Cuándo se apretó comenzar. 0 = todavía no.</summary>
    private long _cuentaDesdeMs;

    private bool _visible;

    /// <summary>
    /// Si la ventana de «lo que viene» está desplegada.
    /// </summary>
    /// <remarks>
    /// Arranca abierta en cada oleada nueva: es información que hay que ver
    /// <b>antes</b> de decidir, y una ventana que hay que ir a buscar es una
    /// ventana que nadie abre. Cerrarla es para acomodar el tablero.
    /// </remarks>
    private bool _ventanaAbierta = true;

    private int _oleadaVista = -1;

    /// <summary>
    /// Qué página de tipos se está mirando. 0 es la primera.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Antes no había páginas: había un corte.</b> La ventana dibujaba
    /// los primeros ocho tipos y abajo decía «+1 tipos más que no entran acá»,
    /// que es un aviso honesto y perfectamente inútil — el jugador se enteraba de
    /// que existía algo que no iba a poder ver. Y el que sobraba no era el menos
    /// importante: el orden es de aparición, así que lo que quedaba afuera era el
    /// final de la oleada, que es cuando el tablero ya está gastado.</para>
    ///
    /// <para>Vuelve a cero en cada oleada, con la misma cuenta que reabre la
    /// ventana: quedarse en la página 3 de la oleada anterior mostraría una
    /// página vacía sin explicar por qué.</para>
    /// </remarks>
    private int _pagina;

    public PreparationScreen(Game juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);

        // ⚠ Ignore y no Stop, al revés que el mercado.
        //
        // Durante la preparación el jugador TIENE que poder clickear el mapa para
        // poner defensores. Comerse los clicks acá dejaría la fase sin su razón de
        // ser. Los botones se atienden uno por uno en _UnhandledInput.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Si la preparación está a la vista.
    /// </summary>
    /// <remarks>
    /// La consulta el HUD: mientras esto se dibuja, la barra de build se calla,
    /// porque las dos usan el mismo rectángulo del diseño.
    /// </remarks>
    public static bool IsOpen(Run? partida)
        => partida is { State: RunState.InProgress, WaitingForWaveStart: true }
           && !partida.WaitingForChoice;

    /// <summary>Si la ventana grande se está dibujando ahora.</summary>
    /// <remarks>
    /// ⚠ Cede ante el mercado y ante la mochila: las tres son ventanas y solo una
    /// puede mandar. El mercado gana porque frena la corrida cuando hay elección
    /// pendiente, y esto no frena nada.
    /// </remarks>
    private bool VentanaVisible
        => _visible && _ventanaAbierta
           && _juego.Market is not { IsOpen: true }
           && _juego.Backpack is not { IsOpen: true };

    public override void _Process(double delta)
    {
        bool deberia = IsOpen(_juego.Run);

        if (deberia != _visible)
        {
            _visible = deberia;

            // Cada vez que se abre, la cuenta arranca de cero.
            _cuentaDesdeMs = 0;
            QueueRedraw();
        }

        if (!_visible)
            return;

        // Oleada nueva, ventana nueva. Sin esto, cerrarla una vez la dejaría
        // cerrada para el resto de la corrida y el jugador no volvería a ver qué
        // viene — que es la información de la que depende toda la fase.
        if (_juego.Run.NextWave != _oleadaVista)
        {
            _oleadaVista = _juego.Run.NextWave;
            _ventanaAbierta = true;
            _pagina = 0;
        }

        // ⚠ La preparación NO tiene límite de tiempo, y es a propósito.
        //
        // Es el punto de descanso de la corrida: acá se cobran las recompensas,
        // se compra, se sube de nivel y se acomoda el tablero mirando qué viene.
        // Con el tick congelado, quedarse una hora cuesta exactamente lo mismo que
        // quedarse dos segundos, así que un reloj encima solo apuraría sin motivo.
        if (_cuentaDesdeMs > 0 && FineClock.Ms - _cuentaDesdeMs >= MsDeCuentaRegresiva)
        {
            _cuentaDesdeMs = 0;
            _juego.StartWave();
        }

        QueueRedraw();
    }

    /// <summary>Segundos que faltan de la cuenta regresiva. 0 = no arrancó.</summary>
    private int Account()
    {
        if (_cuentaDesdeMs == 0)
            return 0;

        long faltan = MsDeCuentaRegresiva - (FineClock.Ms - _cuentaDesdeMs);

        return (int)System.Math.Max(0, (faltan + 999) / 1000);
    }

    // =====================================================================
    // Geometría
    // =====================================================================

    /// <summary>
    /// La ventana de «lo que viene».
    /// </summary>
    /// <remarks>
    /// ⚠ Deliberadamente <b>más chica que el mercado</b>: 1.180 × 470 contra los
    /// 1.808 × 796 de aquél. El mercado frena la corrida y se lo puede permitir;
    /// esto convive con un tablero que el jugador está acomodando, así que se
    /// para arriba y deja ver las filas de abajo del mapa, que es donde queda la
    /// Torre en casi todos los layouts.
    /// </remarks>
    private static Rect2 Ventana()
        => new((Canvas.CanvasWidth - 1180f) / 2f, 150f, 1180f, 490f);

    private static Rect2 RectCerrarVentana()
    {
        var v = Ventana();

        return new Rect2(v.End.X - 150f, v.Position.Y + 14f, 136f, 34f);
    }

    /// <summary>El botón de comenzar que vive adentro de la ventana.</summary>
    private static Rect2 BotonDeVentana()
    {
        var v = Ventana();

        return new Rect2(v.Position.X + (v.Size.X / 2f) - 200f, v.End.Y - 68f, 400f, 52f);
    }

    /// <summary>
    /// La tarjeta de un tipo de bicho.
    /// </summary>
    /// <remarks>
    /// El ancho se reparte entre los que haya, con un techo: con dos tipos, dos
    /// tarjetas de 500 px dejarían el sprite nadando en el medio. Con más de
    /// seis, se achican hasta que entren — una oleada rara con ocho tipos tiene
    /// que verse igual, aunque apretada.
    /// </remarks>
    private static Rect2 TarjetaRect(int i, int cuantas, bool conFlechas)
    {
        var v = Ventana();

        const float Margen = 20f;
        const float Hueco = 12f;
        const float Techo = 210f;

        // Con flechas el ancho útil se achica: las flechas viven adentro de la
        // ventana, no colgando de los bordes, para que el cursor no tenga que
        // salirse de lo que está mirando.
        float disponible = v.Size.X - (Margen * 2f) - (conFlechas ? AnchoFlecha * 2f : 0f);
        float ancho = Mathf.Min(Techo, (disponible - (Hueco * (cuantas - 1))) / cuantas);

        // Centradas: con tres tarjetas de 210 sobra media ventana, y pegarlas a
        // la izquierda deja un hueco que se lee como que falta algo.
        float total = (ancho * cuantas) + (Hueco * (cuantas - 1));
        float x0 = v.Position.X + ((v.Size.X - total) / 2f);

        return new Rect2(x0 + (i * (ancho + Hueco)), v.Position.Y + 62f, ancho, AltoTarjeta);
    }

    // --- La paginación de tipos ----------------------------------------------

    /// <summary>Cuántos tipos entran en una página.</summary>
    /// <remarks>
    /// Seis y no ocho. Con ocho las tarjetas bajan a 133 px de ancho y el nombre
    /// del bicho —«Murcielago», «Escorpion»— empieza a cortarse; con seis quedan
    /// en el techo de 210 y entra todo lo que la tarjeta tiene que decir. Lo que
    /// no entra ahora tiene adónde ir, que es la diferencia.
    /// </remarks>
    private const int PorPagina = 6;

    private const float AnchoFlecha = 40f;
    private const float AltoTarjeta = 330f;

    private static int Paginas(int tipos)
        => System.Math.Max(1, ((tipos - 1) / PorPagina) + 1);

    private static Rect2 Flecha(bool izquierda)
    {
        var v = Ventana();

        float y = v.Position.Y + 62f + ((AltoTarjeta - 72f) / 2f);

        return izquierda
            ? new Rect2(v.Position.X + 10f, y, AnchoFlecha - 6f, 72f)
            : new Rect2(v.End.X - AnchoFlecha - 4f, y, AnchoFlecha - 6f, 72f);
    }

    // --- La barra de abajo, que queda con lo mínimo ---------------------------

    private static Rect2 Boton()
    {
        var bloque = UiLayout.RunBottomBlock(3).Inset(4f);

        return new Rect2(bloque.Position.X, bloque.Position.Y + 40f,
            bloque.Size.X, bloque.Size.Y - 40f);
    }

    /// <summary>
    /// El botón que reabre el mercado, arriba del de comenzar.
    /// </summary>
    /// <remarks>
    /// ⚠ Existe porque el mercado <b>se puede cerrar</b> una vez resuelta la
    /// elección: el jugador cierra la ventana para mirar el tablero y necesita
    /// una puerta de vuelta. Sin esto, cerrarla perdería la tienda de esa oleada
    /// —y la tienda no se repone—.
    /// </remarks>
    private static Rect2 BotonMercado()
    {
        var bloque = UiLayout.RunBottomBlock(3).Inset(4f);

        return new Rect2(bloque.Position.X, bloque.Position.Y, bloque.Size.X, 34f);
    }

    /// <summary>El botón que reabre la ventana de «lo que viene».</summary>
    private static Rect2 BotonVerOleada()
    {
        var r = UiLayout.RunBottomBlock(1);

        return new Rect2(r.Position.X, r.Position.Y + (r.Size.Y / 2f) - 22f, 260f, 44f);
    }

    /// <summary>Si hay tienda sin resolver que reabrir.</summary>
    private bool HayMercado()
        => _juego.Market is { IsOpen: false } && _juego.Run is { } p
           && p.Shop.StockWave == p.NextWave && p.Shop.Stock.Count > 0;

    // =====================================================================
    // Entrada
    // =====================================================================

    public override void _UnhandledInput(InputEvent evento)
    {
        if (!_visible)
            return;

        if (evento is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } clic)
        {
            if (Clic(clic.Position))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (evento is InputEventKey { Pressed: true, Echo: false } tecla)
        {
            // Esc cierra la ventana antes que nada. Solo si está abierta: si no,
            // el Esc es del menú, que es lo que el jugador espera.
            if (tecla.Keycode == Key.Escape && VentanaVisible)
            {
                _ventanaAbierta = false;
                GetViewport().SetInputAsHandled();
                return;
            }

            // Las flechas pasan de página, igual que los botones. Con la ventana
            // cerrada no hacen nada: no hay lista que mover.
            if (VentanaVisible && tecla.Keycode is Key.Left or Key.Right)
            {
                Pasar(tecla.Keycode == Key.Right ? 1 : -1);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (tecla.Keycode is Key.Space or Key.Enter)
                Arrancar();
        }
    }

    /// <summary>
    /// Atiende un click. Devuelve <c>true</c> si era de esta pantalla.
    /// </summary>
    /// <remarks>
    /// ⚠ El orden importa: primero lo de la ventana y después lo de la barra. Un
    /// click adentro de la ventana <b>nunca</b> tiene que llegar al mapa —pondría
    /// un defensor detrás de ella— y el de la barra tampoco.
    /// </remarks>
    private bool Clic(Vector2 posicion)
    {
        if (VentanaVisible)
        {
            if (RectCerrarVentana().HasPoint(posicion))
            {
                _ventanaAbierta = false;
                return true;
            }

            if (BotonDeVentana().HasPoint(posicion))
            {
                Arrancar();
                return true;
            }

            // Las flechas, antes que el catch-all de la ventana.
            if (HayPaginas())
            {
                if (Flecha(izquierda: true).HasPoint(posicion))
                {
                    Pasar(-1);
                    return true;
                }

                if (Flecha(izquierda: false).HasPoint(posicion))
                {
                    Pasar(1);
                    return true;
                }
            }

            // El resto de la ventana se come el click sin hacer nada: es opaca, y
            // un click que la atraviesa pondría una ficha donde el jugador no ve.
            if (Ventana().HasPoint(posicion))
                return true;
        }
        else if (BotonVerOleada().HasPoint(posicion))
        {
            _ventanaAbierta = true;
            return true;
        }

        if (HayMercado() && BotonMercado().HasPoint(posicion))
        {
            _juego.Market!.Open();
            return true;
        }

        if (Boton().HasPoint(posicion))
        {
            Arrancar();
            return true;
        }

        return false;
    }

    /// <summary>Si la oleada trae más tipos de los que entran en una página.</summary>
    private bool HayPaginas()
        => _juego.Run is { } p && p.NextWavePreview().Count > PorPagina;

    /// <summary>
    /// Mueve la lista de tipos una página.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Da la vuelta</b> en vez de frenar en los extremos. Son dos o tres
    /// páginas: con el tope duro, llegar al final y querer volver al primero
    /// obliga a hacer el camino de vuelta entero, y no hay ningún dato que se
    /// pierda por envolver — la lista es corta y el jugador la ve completa.
    /// </remarks>
    private void Pasar(int cuanto)
    {
        int total = Paginas(_juego.Run?.NextWavePreview().Count ?? 0);

        if (total <= 1)
            return;

        _pagina = ((_pagina + cuanto) % total + total) % total;
        QueueRedraw();
    }

    private void Arrancar()
    {
        if (_cuentaDesdeMs == 0)
            _cuentaDesdeMs = FineClock.Ms;
    }

    // =====================================================================
    // Dibujo
    // =====================================================================

    public override void _Draw()
    {
        if (!_visible)
            return;

        var partida = _juego.Run;

        UiTooltip.Begin(GetGlobalMousePosition());

        BarraDeAbajo(partida);

        if (VentanaVisible)
            DibujarVentana(partida);

        UiTooltip.Draw(this);
    }

    /// <summary>
    /// La barra de abajo: el estado y los botones. <b>Sin la lista de bichos.</b>
    /// </summary>
    /// <remarks>
    /// La lista se mudó a la ventana. Lo que queda es lo que hace falta con la
    /// ventana cerrada: qué oleada viene, cuánto oro hay, y las tres puertas
    /// —ver la oleada, el mercado, comenzar—.
    /// </remarks>
    private void BarraDeAbajo(Run partida)
    {
        this.Panel(UiLayout.RunBottomBar);

        // La franja ámbar de arriba: es lo que dice, de reojo, que el juego está
        // esperando y no colgado.
        DrawRect(UiLayout.RunBottomBar.Row(0f, 3f), Palette.Warning);

        var r = UiLayout.RunBottomBlock(0);
        int total = DataLoader.Current.Curves.RunCurve.WavesPerRun;

        this.Label(r.Row(0f, 18f), Loc.Get("ui.preparacion.wave").ToUpperInvariant(), 13,
            Palette.TextMuted);

        this.Label(r.Row(16f, 32f),
            $"{partida.NextWave} {Loc.Get("ui.preparacion.de")} {total}", 28,
            Palette.TextPrimary);

        this.Price(r.Row(50f, 22f), partida.Gold);

        this.Paragraph(r.Row(74f, r.Size.Y - 74f), Loc.Get("ui.preparacion.instrucciones"), 12,
            Palette.TextMuted);

        // El botón de volver a abrir la ventana, solo si está cerrada.
        if (!VentanaVisible)
        {
            this.Button(BotonVerOleada(), Loc.Get("ui.preparacion.ver_oleada"), Palette.Warning,
                relleno: false);
        }

        DibujarBoton();
    }

    /// <summary>
    /// La ventana de «lo que viene»: una tarjeta por tipo de bicho.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Sin esto el jugador arma el tablero a ciegas</b>, y contra un jefe
    /// conviene lo contrario que contra treinta hormigas: daño concentrado o daño
    /// en área. Es la información que convierte la preparación en una decisión.
    /// </remarks>
    private void DibujarVentana(Run partida)
    {
        var v = Ventana();
        var filas = partida.NextWavePreview();

        // Oscurece el mundo detrás de la ventana, no la pantalla entera: la
        // ventana no frena nada y el tablero de los costados se sigue usando.
        DrawRect(v.Grow(18f), Palette.BackgroundMain.Alpha(0.55f));

        this.Panel(v, elevado: true, opacidad: 1f);
        this.Glow(v, Palette.Warning, 1.2f, 5);
        DrawRect(v.Row(0f, 3f), Palette.Warning);

        this.SectionTitle(v.Row(12f, 34f), Loc.Get("ui.preparacion.avance"), Palette.Warning);

        this.Button(RectCerrarVentana(), Loc.Get("ui.mercado.cerrar"), Palette.BorderNormal,
            relleno: false);

        // ⚠ Paginado, no cortado. Antes se dibujaban los primeros ocho y abajo
        // decía «+1 tipos más que no entran acá»: el jugador se enteraba de que
        // existía algo que no iba a poder ver, y como el orden es de aparición,
        // lo que quedaba afuera era el final de la oleada.
        int paginas = Paginas(filas.Count);

        _pagina = System.Math.Clamp(_pagina, 0, paginas - 1);

        bool conFlechas = paginas > 1;

        int desde = _pagina * PorPagina;
        int cuantas = System.Math.Min(PorPagina, filas.Count - desde);

        for (int i = 0; i < cuantas; i++)
            DibujarTarjeta(TarjetaRect(i, cuantas, conFlechas), filas[desde + i]);

        if (conFlechas)
            DibujarFlechas(paginas, filas.Count);

        DibujarBotonDeVentana();
    }

    /// <summary>
    /// Las dos flechas y el contador de páginas.
    /// </summary>
    /// <remarks>
    /// <para>Van <b>a los costados de las tarjetas y a media altura</b>, que es
    /// donde el cursor ya está: quien quiere ver el resto de la oleada está
    /// mirando las tarjetas, no el pie de la ventana.</para>
    ///
    /// <para>La flecha se dibuja como carácter y no como icono de la fuente de
    /// RPG Awesome, porque esa fuente no trae flechas y las dos que hay —punta de
    /// lanza y punta de flecha— son armas, no direcciones.</para>
    /// </remarks>
    private void DibujarFlechas(int paginas, int tipos)
    {
        var v = Ventana();

        Boton(Flecha(izquierda: true), "‹", "ui.preparacion.tipos_anteriores");
        Boton(Flecha(izquierda: false), "›", "ui.preparacion.tipos_siguientes");

        // El contador va abajo y dice también cuántos tipos hay en total: sin el
        // total, «2 de 3» no aclara si lo que falta es un bicho o diez.
        this.Label(new Rect2(v.Position.X, v.End.Y - 96f, v.Size.X, 20f),
            $"{_pagina + 1} {Loc.Get("ui.preparacion.de")} {paginas}  ·  " +
            $"{tipos} {Loc.Get("ui.preparacion.tipos")}",
            14, Palette.TextMuted, HorizontalAlignment.Center);

        void Boton(Rect2 r, string glifo, string cartel)
        {
            bool encima = r.HasPoint(GetGlobalMousePosition());

            DrawRect(r, encima
                ? Palette.Warning.Alpha(0.22f)
                : Palette.BackgroundMain.Alpha(0.75f));

            DrawRect(r, encima ? Palette.Warning : Palette.BorderNormal,
                filled: false, width: 1.5f);

            this.Label(r, glifo, 30, encima ? Palette.Warning : Palette.TextMuted,
                HorizontalAlignment.Center);

            UiTooltip.Over(r, Loc.Get(cartel));
        }
    }

    /// <summary>
    /// La tarjeta de un tipo: sprite, cuántos, stats y lo que traen encima.
    /// </summary>
    /// <remarks>
    /// <para>El jefe va en ámbar y con el marco iluminado. Es una tarjeta entre
    /// varias iguales y tiene que separarse sin leerla: contra un jefe la
    /// respuesta es otra.</para>
    ///
    /// <para>⚠ Los perks y los rasgos van <b>separados y contados uno por uno</b>.
    /// Antes eran dos números lumpeados —«2 con perk», «1 con rasgo (impulsivo o
    /// líder)»— y el segundo ni siquiera decía cuál: contra un líder conviene
    /// concentrar fuego en él, contra un impulsivo tener algo listo antes. Son dos
    /// respuestas distintas y la pantalla obligaba a prepararse para las dos.</para>
    /// </remarks>
    private void DibujarTarjeta(Rect2 r, WavePreview fila)
    {
        var acento = fila.IsBoss ? Palette.Warning : Palette.BorderNormal;

        this.Slot(r, acento, elegido: fila.IsBoss);

        // --- El sprite, arriba y apoyado en su base.
        var marco = new Rect2(r.Position.X + 8f, r.Position.Y + 8f, r.Size.X - 16f, 120f);

        DrawRect(marco, Palette.BackgroundMain.Alpha(0.6f));

        var textura = GraphicsLoader.Texture($"{fila.Graphic}.sur");

        if (textura is not null)
        {
            var tamano = textura.GetSize();
            float s = Mathf.Min(marco.Size.Y / tamano.Y, marco.Size.X / tamano.X);

            DrawTextureRect(textura,
                new Rect2(marco.Position.X + ((marco.Size.X - (tamano.X * s)) / 2f),
                    marco.End.Y - (tamano.Y * s), tamano * s),
                tile: false);
        }

        // --- Cuántos vienen, en una chapa sobre el sprite. Es el número que se
        //     mira primero: cinco cuervos y treinta cuervos son dos oleadas.
        var chapa = new Rect2(marco.Position.X, marco.Position.Y, 54f, 26f);

        DrawRect(chapa, acento.Alpha(0.9f));

        this.Label(chapa, $"{fila.Amount}×", 17, Palette.BackgroundMain,
            HorizontalAlignment.Center);

        if (fila.IsBoss)
        {
            var jefe = new Rect2(marco.End.X - 60f, marco.Position.Y, 60f, 26f);

            DrawRect(jefe, Palette.Warning);

            this.Label(jefe, Loc.Get("ui.preparacion.jefe").ToUpperInvariant(), 13,
                Palette.BackgroundMain, HorizontalAlignment.Center);
        }

        // --- El nombre.
        this.Label(new Rect2(r.Position.X + 8f, marco.End.Y + 4f, r.Size.X - 16f, 26f),
            fila.Name, 18, fila.IsBoss ? Palette.Warning : Palette.TextPrimary,
            HorizontalAlignment.Center);

        // --- Los stats, con los mismos iconos que todo el resto del juego.
        //
        // ⚠ Vida y daño vienen en centésimas de punto y la velocidad en milésimas
        // de celda: crudos dirían «1600 de vida» cuando el bicho tiene 16.
        float y = marco.End.Y + 34f;

        this.IconRow(new Rect2(r.Position.X + 10f, y, r.Size.X - 20f, 22f),
            UiStyle.Icono.Health, Loc.Points(fila.Hp), Palette.Success,
            Loc.Get("ui.preparacion.stat_vida"));

        this.IconRow(new Rect2(r.Position.X + 10f, y + 24f, r.Size.X - 20f, 22f),
            UiStyle.Icono.Damage, Loc.Damage(fila.Damage), null,
            Loc.Get("ui.preparacion.stat_dano"));

        // La velocidad viene en milésimas de celda por tick; en celdas por
        // segundo se lee como lo que es: cuánto avanza mientras un defensor
        // recarga.
        this.IconRow(new Rect2(r.Position.X + 10f, y + 48f, r.Size.X - 20f, 22f),
            UiStyle.Icono.Cadence, Loc.Range(fila.Speed * Ints.TicksPerSecond), null,
            Loc.Get("ui.preparacion.stat_velocidad"));

        // ⚠ El oro es la otra mitad de la decisión de la fase, y no estaba.
        //
        // La preparación es donde se gasta, y el jugador estaba gastando sin
        // saber cuánto le devuelve la oleada que viene. Va en dorado, como el
        // resto del oro del juego, y multiplicado por la cantidad: lo que decide
        // es el total del tipo, no lo que suelta uno.
        this.IconRow(new Rect2(r.Position.X + 10f, y + 72f, r.Size.X - 20f, 22f),
            UiStyle.Icono.Gold, $"{fila.Gold * fila.Amount}", Palette.Gold,
            $"{Loc.Get("ui.preparacion.stat_oro")} — {fila.Gold} × {fila.Amount}");

        this.Rule(r, y + 100f - r.Position.Y);

        // --- Lo que traen encima, en DOS renglones con cartel.
        //
        // ⚠ Antes era un renglón por modificador, con el texto completo adentro
        // de una tarjeta de 210 px: «1 impulsivo: e…», «1 LÍDER: buffe…». La
        // explicación quedaba cortada justo donde empezaba a explicar, así que
        // el renglón ocupaba lugar sin decir nada.
        //
        // Ahora el renglón dice QUÉ hay y CUÁNTOS, que es lo que se lee de un
        // vistazo, y el detalle vive en el cartel. El cartel se abre con el
        // mouse encima y se va solo al salir — dura un cuadro, ver UiTooltip.
        float yr = y + 108f;

        Bullet(ref yr, fila.WithPerk, "ui.preparacion.buffs", Palette.Warning, CartelBuffs(fila));
        Bullet(ref yr, fila.WithTrait, "ui.preparacion.rasgos", Palette.Epic, CartelRasgos(fila));

        if (fila.WithPerk == 0 && fila.WithTrait == 0)
        {
            this.Label(new Rect2(r.Position.X + 10f, yr, r.Size.X - 20f, 20f),
                Loc.Get("ui.preparacion.sin_nada"), 12, Palette.TextDisabled);
        }

        void Bullet(ref float py, int cuantos, string clave, Color color, string cartel)
        {
            if (cuantos == 0)
                return;

            var caja = new Rect2(r.Position.X + 8f, py, r.Size.X - 16f, 22f);

            // Fondo tenue: es lo que dice que el renglón se puede señalar. Sin
            // él, un texto de color entre otros textos de color no invita a
            // poner el mouse encima, y el cartel no lo encuentra nadie.
            bool encima = caja.HasPoint(GetGlobalMousePosition());

            DrawRect(caja, color.Alpha(encima ? 0.28f : 0.12f));

            this.Label(new Rect2(caja.Position.X + 8f, py, caja.Size.X - 40f, 22f),
                $"• {Loc.Get(clave)}", 13, color);

            this.Label(new Rect2(caja.End.X - 34f, py, 26f, 22f),
                $"{cuantos}", 13, color, HorizontalAlignment.Right);

            UiTooltip.Over(caja, cartel);

            py += 24f;
        }
    }

    /// <summary>El detalle de los buffs de un tipo, un renglón por clase.</summary>
    /// <remarks>
    /// ⚠ Dice <b>cuánto</b> y no solo cuántos. Los perks de NPC se apilan por
    /// pasos: «2 con más vida» puede ser un +20% o un +80%, y son dos oleadas
    /// distintas contra el mismo tablero.
    /// </remarks>
    private static string CartelBuffs(WavePreview fila)
    {
        var partes = new System.Collections.Generic.List<string>(2);

        if (fila.WithHealth > 0)
        {
            partes.Add($"{fila.WithHealth} {Loc.Get("ui.preparacion.con_vida")} " +
                $"({Loc.Get("ui.preparacion.hasta")} +{fila.MaxHealthPerk}%)");
        }

        if (fila.WithSpeed > 0)
        {
            partes.Add($"{fila.WithSpeed} {Loc.Get("ui.preparacion.con_velocidad")} " +
                $"({Loc.Get("ui.preparacion.hasta")} +{fila.MaxSpeedPerk}%)");
        }

        return string.Join("\n", partes);
    }

    /// <summary>El detalle de los rasgos: impulsivo y líder, separados.</summary>
    /// <remarks>
    /// Van separados y no lumpeados porque piden respuestas opuestas: contra un
    /// líder conviene concentrar fuego en él, contra un impulsivo tener algo
    /// listo antes de tiempo.
    /// </remarks>
    private static string CartelRasgos(WavePreview fila)
    {
        var partes = new System.Collections.Generic.List<string>(2);

        if (fila.Impulsive > 0)
            partes.Add($"{fila.Impulsive} {Loc.Get("ui.preparacion.impulsivos")}");

        if (fila.Leaders > 0)
            partes.Add($"{fila.Leaders} {Loc.Get("ui.preparacion.lideres")}");

        return string.Join("\n", partes);
    }

    private void DibujarBotonDeVentana()
    {
        var caja = BotonDeVentana();
        int cuenta = Account();

        if (cuenta > 0)
        {
            this.Button(caja, $"{Loc.Get("ui.preparacion.ahi_vienen")}  {cuenta}",
                Palette.Warning, encima: true);

            return;
        }

        this.Button(caja, Loc.Get("ui.preparacion.comenzar_oleada"), Palette.Success);

        this.Label(new Rect2(caja.Position.X, caja.End.Y + 2f, caja.Size.X, 18f),
            Loc.Get("ui.preparacion.espacio_sin_apuro"), 12, Palette.TextMuted,
            HorizontalAlignment.Center);
    }

    private void DibujarBoton()
    {
        var caja = Boton();
        int cuenta = Account();

        // ⚠ El botón del mercado se dibuja EN ÁMBAR y solo si quedó tienda sin
        // resolver. Es un recordatorio: la tienda no se repone, así que lanzar la
        // oleada con ofertas sin mirar tira esa tienda entera.
        if (HayMercado())
            this.Button(BotonMercado(), Loc.Get("ui.preparacion.mercado"), Palette.Gold);

        if (cuenta > 0)
        {
            this.Button(caja, $"{Loc.Get("ui.preparacion.ahi_vienen")}  {cuenta}",
                Palette.Warning, encima: true);

            return;
        }

        this.Button(caja, Loc.Get("ui.preparacion.comenzar_oleada"), Palette.Success);

        this.Label(new Rect2(caja.Position.X, caja.End.Y - 24f, caja.Size.X, 20f),
            Loc.Get("ui.preparacion.espacio_sin_apuro"), 13, Palette.TextMuted,
            HorizontalAlignment.Center);
    }
}
