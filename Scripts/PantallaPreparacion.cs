using Godot;
using TowerOfArgentum.Simulacion;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// La fase entre oleadas: se mira qué viene y se arma el tablero.
/// </summary>
/// <remarks>
/// <para><b>El reloj está detenido.</b> Esperar dos segundos o veintinueve da
/// exactamente los mismos ticks, así que el ranking no castiga pensar. Ver
/// <see cref="Partida.EnPausa"/>.</para>
///
/// <para>Ocupa la <b>barra de abajo del diseño</b> —el mismo rectángulo que en
/// combate muestra la build— y el HUD deja de dibujar la suya mientras esto está
/// a la vista. Es el lugar correcto por la misma razón que la build: es la franja
/// que no tapa el mapa, y en esta fase lo que el jugador tiene que mirar es
/// dónde queda libre.</para>
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

    public PantallaPreparacion(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);

        // ⚠ Ignore y no Stop, al revés que el modal de elección.
        //
        // Durante la preparación el jugador TIENE que poder clickear el mapa para
        // poner defensores. Comerse los clicks acá dejaría la fase sin su razón de
        // ser. El botón se atiende aparte, en _UnhandledInput.
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

        // ⚠ La preparación NO tiene límite de tiempo, y es a propósito.
        //
        // Es el punto de descanso de la corrida: acá se cobran las recompensas,
        // se compra, se sube de nivel y se acomoda el tablero mirando qué viene.
        // Con el tick congelado, quedarse una hora cuesta exactamente lo mismo que
        // quedarse dos segundos, así que un reloj encima solo apuraría sin motivo.
        //
        // Y resuelve lo que INTERFAZ.md §5 se preguntaba sobre el jugador que se
        // levanta de la silla: se va desde acá, cierra el juego y vuelve.
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

    public override void _UnhandledInput(InputEvent evento)
    {
        if (!_visible)
            return;

        bool arranca = evento switch
        {
            InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space or Key.Enter } => true,
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } clic
                => Boton().HasPoint(clic.Position),
            _ => false,
        };

        if (arranca && _cuentaDesdeMs == 0)
        {
            _cuentaDesdeMs = RelojFino.Ms;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// El botón de comenzar, en el último bloque de la barra de abajo.
    /// </summary>
    /// <remarks>
    /// Va donde en combate están los consumibles del diseño —o sea, la mochila—
    /// porque es la esquina más lejos de la ruta y la que la mano ya tiene cerca
    /// del borde de la pantalla.
    /// </remarks>
    private static Rect2 Boton() => UiLayout.Consumables.Inset(4f);

    public override void _Draw()
    {
        if (!_visible)
            return;

        var partida = _juego.Run;

        // ⚠ Panel abajo y sobre el mundo, no una ventana modal que lo tape.
        //
        // Lo que el jugador tiene que mirar en esta fase es EL MAPA: dónde queda
        // libre, por dónde pasa la ruta, qué cubre lo que ya puso. Un modal
        // centrado le taparía justo eso y lo obligaría a cerrarlo para decidir.
        this.Panel(UiLayout.BuildPanel);

        // La franja ámbar de arriba: es lo que dice, de reojo, que el juego está
        // esperando y no colgado.
        DrawRect(UiLayout.BuildPanel.Row(0f, 3f), Palette.Warning);

        Cabecera(partida);
        DibujarAvance(partida);
        DibujarBoton();
    }

    /// <summary>Qué oleada viene, cuánto oro hay y cómo se juega.</summary>
    private void Cabecera(Run partida)
    {
        var r = UiLayout.EquippedPerks;
        int total = CargadorDatos.Actual.Curves.RunCurve.WavesPerRun;

        this.Label(r.Row(0f, 22f), Loc.Get("ui.preparacion.wave").ToUpperInvariant(), 14,
            Palette.TextMuted);

        this.Label(r.Row(20f, 38f),
            $"{partida.ProximaOleada} {Loc.Get("ui.preparacion.de")} {total}", 32,
            Palette.TextPrimary);

        this.Price(r.Row(62f, 26f), partida.Gold);

        this.Paragraph(r.Row(92f, r.Size.Y - 92f), Loc.Get("ui.preparacion.instrucciones"), 13,
            Palette.TextMuted);
    }

    /// <summary>
    /// Lo que viene: cuántos de cada bicho, y cuántos con perk.
    /// </summary>
    /// <remarks>
    /// Sin esto el jugador arma el tablero a ciegas, y contra un jefe conviene lo
    /// contrario que contra treinta hormigas: daño concentrado o daño en área. Es
    /// la información que convierte la preparación en una decisión.
    /// </remarks>
    private void DibujarAvance(Run partida)
    {
        // El bloque del avance ocupa los dos del medio del diseño —el del arma y
        // el de los planos—: una fila de bichos no entra en uno solo.
        var zona = new Rect2(
            UiLayout.EquippedWeapon.Position,
            new Vector2(UiLayout.ActiveBlueprints.End.X - UiLayout.EquippedWeapon.Position.X,
                UiLayout.EquippedWeapon.Size.Y));

        this.Label(zona.Row(0f, 22f), Loc.Get("ui.preparacion.avance").ToUpperInvariant(), 14,
            Palette.TextMuted);

        this.Rule(zona, 24f);

        float x = zona.Position.X;
        float y = zona.Position.Y + 30f;
        float alto = zona.End.Y - y;

        foreach (var fila in partida.AvanceProximaOleada())
        {
            var textura = CargadorGraficos.Textura($"{fila.Graphic}.sur");

            if (textura is not null)
            {
                // El bicho se encaja en el alto disponible en vez de una escala
                // fija: los sprites van de 100 a 400 px de alto y con un 0,30
                // parejo el dragón se comía la barra entera.
                var tamano = textura.GetSize();
                float s = System.Math.Min(alto / tamano.Y, 0.34f);

                DrawTextureRect(textura,
                    new Rect2(x, zona.End.Y - (tamano.Y * s), tamano * s), tile: false);

                x += (tamano.X * s) + 6f;
            }

            var color = fila.IsBoss ? Palette.Warning : Palette.TextPrimary;

            string etiqueta = $"{fila.Cuantos}× {fila.Name}"
                + (fila.IsBoss ? $"  {Loc.Get("ui.preparacion.jefe")}" : "");

            this.Label(new Rect2(x, y, 240f, 22f), etiqueta, 18, color);

            float ancho = UiStyle.Width(etiqueta, 18);

            // Los buffeados se marcan en ámbar. INTERFAZ.md §5: «un NPC con perk
            // tiene que distinguirse de un vistazo o el jugador pierde sin
            // entender por qué».
            if (fila.ConPerk > 0)
            {
                string buffeados = $"{fila.ConPerk} {Loc.Get("ui.preparacion.con_perk")}";

                this.Label(new Rect2(x, y + 22f, 240f, 20f), buffeados, 14, Palette.Warning);
                ancho = System.Math.Max(ancho, UiStyle.Width(buffeados, 14));
            }

            // ⚠ Los rasgos —impulsivo y líder— van en su propio renglón y en otro
            // color. No son un número más de vida o velocidad: cambian una
            // situación, y contra un líder conviene concentrar fuego en él antes
            // que repartir. Sumarlos al contador de perks escondería justamente la
            // parte que exige una respuesta distinta.
            if (fila.ConRasgo > 0)
            {
                string rasgos = $"{fila.ConRasgo} {Loc.Get("ui.preparacion.con_rasgo")}";

                this.Label(new Rect2(x, y + 42f, 240f, 20f), rasgos, 14, Palette.Epic);
                ancho = System.Math.Max(ancho, UiStyle.Width(rasgos, 14));
            }

            x += ancho + 24f;

            if (x > zona.End.X - 80f)
                break;
        }
    }

    private void DibujarBoton()
    {
        var caja = Boton();
        int cuenta = Cuenta();

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
