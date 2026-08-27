using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// La fase entre oleadas: se mira qué viene y se arma el tablero.
/// </summary>
/// <remarks>
/// <para><b>El reloj está detenido.</b> Esperar dos segundos o veintinueve da
/// exactamente los mismos ticks, así que el ranking no castiga pensar. Ver
/// <see cref="Partida.EnPausa"/>.</para>
///
/// <para>El tope de 30 segundos vive <b>acá</b> y no en la simulación: es reloj
/// de pared, no entra al tick y el servidor ni se entera. Existe para que la
/// corrida no se quede detenida para siempre si alguien se levanta de la silla,
/// no para apurar a nadie.</para>
///
/// <para>Y hay botón para arrancar antes: quien acomoda el tablero en cinco
/// segundos no tiene por qué esperar veinticinco mirando el techo.</para>
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
        SetAnchorsPreset(LayoutPreset.FullRect);

        // ⚠ Ignore y no Stop, al revés que el modal de elección.
        //
        // Durante la preparación el jugador TIENE que poder clickear el mapa para
        // poner defensores. Comerse los clicks acá dejaría la fase sin su razón de
        // ser. El botón se atiende aparte, en _UnhandledInput.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        var partida = _juego.Partida;

        bool deberia = partida is { Estado: EstadoPartida.EnCurso, EsperandoInicioDeOleada: true }
                       && !partida.EsperandoEleccion;

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

    private static Rect2 Boton()
        => new(Escala.LienzoAncho - 360, Escala.LienzoAlto - 120, 320, 72);

    public override void _Draw()
    {
        if (!_visible)
            return;

        var partida = _juego.Partida;
        var fuente = ThemeDB.FallbackFont;

        const int Alto = 210;
        int y0 = Escala.LienzoAlto - Alto;

        // ⚠ Panel abajo y sobre el mundo, no una ventana modal que lo tape.
        //
        // Lo que el jugador tiene que mirar en esta fase es EL MAPA: dónde queda
        // libre, por dónde pasa la ruta, qué cubre lo que ya puso. Un modal
        // centrado le taparía justo eso y lo obligaría a cerrarlo para decidir.
        DrawRect(new Rect2(0, y0, Escala.LienzoAncho, Alto), new Color(0.05f, 0.06f, 0.09f, 0.90f));
        DrawRect(new Rect2(0, y0, Escala.LienzoAncho, 3), new Color(0.85f, 0.72f, 0.30f));

        DrawString(fuente, new Vector2(28, y0 + 40),
            $"Oleada {partida.ProximaOleada} de {CargadorDatos.Actual.Curvas.Corrida.OleadasPorCorrida}",
            HorizontalAlignment.Left, -1, 30, new Color(0.92f, 0.90f, 0.84f));

        DrawString(fuente, new Vector2(28, y0 + 72),
            $"oro {partida.Oro}    ·    poné defensores, equipá y comprá — el reloj está detenido",
            HorizontalAlignment.Left, -1, 18, new Color(0.58f, 0.63f, 0.70f));

        DibujarAvance(fuente, y0);
        DibujarBoton(fuente);
    }

    /// <summary>
    /// Lo que viene: cuántos de cada bicho, y cuántos con perk.
    /// </summary>
    /// <remarks>
    /// Sin esto el jugador arma el tablero a ciegas, y contra un jefe conviene lo
    /// contrario que contra treinta hormigas: daño concentrado o daño en área. Es
    /// la información que convierte la preparación en una decisión.
    /// </remarks>
    private void DibujarAvance(Font fuente, int y0)
    {
        var avance = _juego.Partida.AvanceProximaOleada();

        float x = 28;
        float y = y0 + 118;

        foreach (var fila in avance)
        {
            var textura = CargadorGraficos.Textura($"{fila.Grafico}.sur");

            if (textura is not null)
            {
                var tamano = textura.GetSize() * 0.30f;
                DrawTextureRect(textura, new Rect2(x, y - tamano.Y + 46, tamano), tile: false);
                x += tamano.X + 6;
            }

            var color = fila.EsJefe
                ? new Color(0.98f, 0.62f, 0.25f)
                : new Color(0.86f, 0.88f, 0.92f);

            string etiqueta = $"{fila.Cuantos}× {fila.Nombre}" + (fila.EsJefe ? "  (JEFE)" : "");

            DrawString(fuente, new Vector2(x, y + 26), etiqueta,
                HorizontalAlignment.Left, -1, 20, color);

            float ancho = fuente.GetStringSize(etiqueta, HorizontalAlignment.Left, -1, 20).X;

            // Los buffeados se marcan en ámbar. INTERFAZ.md §5: «un NPC con perk
            // tiene que distinguirse de un vistazo o el jugador pierde sin
            // entender por qué».
            if (fila.ConPerk > 0)
            {
                DrawString(fuente, new Vector2(x, y + 48), $"{fila.ConPerk} con perk",
                    HorizontalAlignment.Left, -1, 16, new Color(0.98f, 0.80f, 0.30f));

                ancho = System.Math.Max(ancho,
                    fuente.GetStringSize($"{fila.ConPerk} con perk", HorizontalAlignment.Left, -1, 16).X);
            }

            x += ancho + 34;

            if (x > Escala.LienzoAncho - 420)
                break;
        }
    }

    private void DibujarBoton(Font fuente)
    {
        var caja = Boton();
        int cuenta = Cuenta();

        if (cuenta > 0)
        {
            DrawRect(caja, new Color(0.42f, 0.30f, 0.12f));
            DrawRect(caja, new Color(0.95f, 0.75f, 0.35f), filled: false, width: 2f);

            DrawString(fuente, new Vector2(caja.Position.X + 22, caja.Position.Y + 46),
                $"AHÍ VIENEN...  {cuenta}", HorizontalAlignment.Left, -1, 30,
                new Color(0.99f, 0.88f, 0.60f));

            return;
        }

        DrawRect(caja, new Color(0.16f, 0.42f, 0.24f));
        DrawRect(caja, new Color(0.45f, 0.85f, 0.55f), filled: false, width: 2f);

        DrawString(fuente, new Vector2(caja.Position.X + 22, caja.Position.Y + 32),
            "COMENZAR OLEADA", HorizontalAlignment.Left, -1, 24,
            new Color(0.90f, 0.98f, 0.92f));

        DrawString(fuente, new Vector2(caja.Position.X + 22, caja.Position.Y + 58),
            "espacio · sin apuro, el reloj está detenido", HorizontalAlignment.Left, -1, 15,
            new Color(0.62f, 0.78f, 0.68f));
    }
}
