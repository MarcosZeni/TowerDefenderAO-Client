using Godot;
using TowerOfArgentum.Simulacion;
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
public partial class PantallaPreparacion : Control
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

    private readonly Juego _juego;

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

    public PantallaPreparacion(Juego juego)
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
    public static bool EstaAbierta(Run? partida)
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
           && _juego.Mercado is not { Abierta: true }
           && _juego.Backpack is not { Abierta: true };

    public override void _Process(double delta)
    {
        bool deberia = EstaAbierta(_juego.Run);

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
        if (_juego.Run.ProximaOleada != _oleadaVista)
        {
            _oleadaVista = _juego.Run.ProximaOleada;
            _ventanaAbierta = true;
        }

        // ⚠ La preparación NO tiene límite de tiempo, y es a propósito.
        //
        // Es el punto de descanso de la corrida: acá se cobran las recompensas,
        // se compra, se sube de nivel y se acomoda el tablero mirando qué viene.
        // Con el tick congelado, quedarse una hora cuesta exactamente lo mismo que
        // quedarse dos segundos, así que un reloj encima solo apuraría sin motivo.
        if (_cuentaDesdeMs > 0 && RelojFino.Ms - _cuentaDesdeMs >= MsDeCuentaRegresiva)
        {
            _cuentaDesdeMs = 0;
            _juego.ComenzarOleada();
        }

        QueueRedraw();
    }

    /// <summary>Segundos que faltan de la cuenta regresiva. 0 = no arrancó.</summary>
    private int Cuenta()
    {
        if (_cuentaDesdeMs == 0)
            return 0;

        long faltan = MsDeCuentaRegresiva - (RelojFino.Ms - _cuentaDesdeMs);

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
        => new((Escala.LienzoAncho - 1180f) / 2f, 150f, 1180f, 490f);

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
    private static Rect2 TarjetaRect(int i, int cuantas)
    {
        var v = Ventana();

        const float Margen = 20f;
        const float Hueco = 12f;
        const float Techo = 210f;

        float disponible = v.Size.X - (Margen * 2f);
        float ancho = Mathf.Min(Techo, (disponible - (Hueco * (cuantas - 1))) / cuantas);

        // Centradas: con tres tarjetas de 210 sobra media ventana, y pegarlas a
        // la izquierda deja un hueco que se lee como que falta algo.
        float total = (ancho * cuantas) + (Hueco * (cuantas - 1));
        float x0 = v.Position.X + ((v.Size.X - total) / 2f);

        return new Rect2(x0 + (i * (ancho + Hueco)), v.Position.Y + 62f, ancho, 330f);
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
        => _juego.Mercado is { Abierta: false } && _juego.Run is { } p
           && p.Shop.OleadaDelStock == p.ProximaOleada && p.Shop.Stock.Count > 0;

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
            _juego.Mercado!.Abrir();
            return true;
        }

        if (Boton().HasPoint(posicion))
        {
            Arrancar();
            return true;
        }

        return false;
    }

    private void Arrancar()
    {
        if (_cuentaDesdeMs == 0)
            _cuentaDesdeMs = RelojFino.Ms;
    }

    // =====================================================================
    // Dibujo
    // =====================================================================

    public override void _Draw()
    {
        if (!_visible)
            return;

        var partida = _juego.Run;

        UiTooltip.Comenzar(GetGlobalMousePosition());

        BarraDeAbajo(partida);

        if (VentanaVisible)
            DibujarVentana(partida);

        UiTooltip.Dibujar(this);
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
        int total = CargadorDatos.Actual.Curves.RunCurve.WavesPerRun;

        this.Label(r.Row(0f, 18f), Loc.Get("ui.preparacion.wave").ToUpperInvariant(), 13,
            Palette.TextMuted);

        this.Label(r.Row(16f, 32f),
            $"{partida.ProximaOleada} {Loc.Get("ui.preparacion.de")} {total}", 28,
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
        var filas = partida.AvanceProximaOleada();

        // Oscurece el mundo detrás de la ventana, no la pantalla entera: la
        // ventana no frena nada y el tablero de los costados se sigue usando.
        DrawRect(v.Grow(18f), Palette.BackgroundMain.Alpha(0.55f));

        this.Panel(v, elevado: true, opacidad: 1f);
        this.Glow(v, Palette.Warning, 1.2f, 5);
        DrawRect(v.Row(0f, 3f), Palette.Warning);

        this.SectionTitle(v.Row(12f, 34f), Loc.Get("ui.preparacion.avance"), Palette.Warning);

        this.Button(RectCerrarVentana(), Loc.Get("ui.mercado.cerrar"), Palette.BorderNormal,
            relleno: false);

        int cuantas = System.Math.Min(filas.Count, 8);

        for (int i = 0; i < cuantas; i++)
            DibujarTarjeta(TarjetaRect(i, cuantas), filas[i]);

        // ⚠ Si la oleada trae más tipos de los que entran, se dice. Un corte
        // silencioso en la pantalla que existe para avisar qué viene sería el
        // peor lugar posible para uno.
        if (filas.Count > cuantas)
        {
            this.Label(new Rect2(v.Position.X, v.End.Y - 96f, v.Size.X, 20f),
                $"+{filas.Count - cuantas} {Loc.Get("ui.preparacion.mas_tipos")}", 14,
                Palette.TextMuted, HorizontalAlignment.Center);
        }

        DibujarBotonDeVentana();
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

        var textura = CargadorGraficos.Textura($"{fila.Graphic}.sur");

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

        this.Label(chapa, $"{fila.Cuantos}×", 17, Palette.BackgroundMain,
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
            UiStyle.Icono.Damage, Loc.Damage(fila.MaxDamage), null,
            Loc.Get("ui.preparacion.stat_dano"));

        // La velocidad viene en milésimas de celda por tick; en celdas por
        // segundo se lee como lo que es: cuánto avanza mientras un defensor
        // recarga.
        this.IconRow(new Rect2(r.Position.X + 10f, y + 48f, r.Size.X - 20f, 22f),
            UiStyle.Icono.Cadence, Loc.Range(fila.Speed * Ints.TicksPerSecond), null,
            Loc.Get("ui.preparacion.stat_velocidad"));

        this.Rule(r, y + 76f - r.Position.Y);

        // --- Lo que traen encima. Cada uno en su renglón y con su color.
        float yr = y + 84f;

        Renglon(ref yr, fila.ConVida, "ui.preparacion.con_vida", Palette.Warning);
        Renglon(ref yr, fila.ConVelocidad, "ui.preparacion.con_velocidad", Palette.Warning);
        Renglon(ref yr, fila.Impulsivos, "ui.preparacion.impulsivos", Palette.Epic);
        Renglon(ref yr, fila.Lideres, "ui.preparacion.lideres", Palette.Epic);

        if (fila.ConPerk == 0 && fila.ConRasgo == 0)
        {
            this.Label(new Rect2(r.Position.X + 10f, yr, r.Size.X - 20f, 20f),
                Loc.Get("ui.preparacion.sin_nada"), 12, Palette.TextDisabled);
        }

        void Renglon(ref float py, int cuantos, string clave, Color color)
        {
            if (cuantos == 0)
                return;

            this.Label(new Rect2(r.Position.X + 10f, py, r.Size.X - 20f, 20f),
                $"{cuantos} {Loc.Get(clave)}", 12, color);

            py += 20f;
        }
    }

    private void DibujarBotonDeVentana()
    {
        var caja = BotonDeVentana();
        int cuenta = Cuenta();

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
        int cuenta = Cuenta();

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
