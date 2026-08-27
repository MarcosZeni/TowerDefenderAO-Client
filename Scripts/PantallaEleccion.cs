using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// El modal de "levelear o contratar". <b>No se puede cerrar sin elegir.</b>
/// </summary>
/// <remarks>
/// <para>Overlay sobre el mundo oscurecido, no una pantalla aparte: el jugador
/// tiene que seguir viendo el estado de su defensa mientras decide (INTERFAZ.md
/// §3).</para>
///
/// <para>⚠ <b>Muestra qué desbloquea levelear.</b> Las dos opciones dan +1 slot
/// de perk; si eso no se ve, contratar parece siempre mejor —trae un rol nuevo—
/// y la decisión "ancho o profundo" se cae sola.</para>
/// </remarks>
public partial class PantallaEleccion : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;
    private int _bajoElCursor = -1;

    private const int AnchoTarjeta = 460;
    private const int AltoTarjeta = 340;
    private const int Separacion = 40;

    public PantallaEleccion(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Stop y no Ignore: mientras el modal está abierto se come los clicks,
        // así el jugador no pone un defensor sin querer al apuntar a una tarjeta.
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        bool abierta = _juego.Partida?.EsperandoEleccion ?? false;

        Visible = abierta;
        MouseFilter = abierta ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        if (abierta)
            QueueRedraw();
    }

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, y es una cicatriz.
    /// </summary>
    /// <remarks>
    /// <para>Con <c>_GuiInput</c> el modal quedaba <b>sordo</b>: el click en la
    /// tarjeta no hacía nada, sin error y sin pista — el jugador no podía ni
    /// elegir su primera clase. Y las teclas 1-2-3 tampoco: <c>_GuiInput</c> solo
    /// recibe teclado con foco, y este Control nunca lo toma.</para>
    ///
    /// <para><c>_Input</c> llega siempre, antes que la GUI y que el resto. La
    /// posición se toma de <see cref="CanvasItem.GetGlobalMousePosition"/> y no
    /// del evento: la del evento viene en píxeles de la ventana, y con
    /// <c>stretch = keep</c> eso no coincide con el lienzo si la ventana no es
    /// exactamente 1920×1080.</para>
    ///
    /// <para>Mientras el modal está abierto, <b>todo click se marca atendido</b>,
    /// caiga donde caiga: es un modal, y un click que se escapa al mapa pondría
    /// un defensor sin querer.</para>
    /// </remarks>
    public override void _Input(InputEvent evento)
    {
        var partida = _juego.Partida;
        if (partida is null || !partida.EsperandoEleccion)
            return;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = TarjetaEn(GetGlobalMousePosition(), partida.OpcionesAbiertas.Length);
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                int i = TarjetaEn(GetGlobalMousePosition(), partida.OpcionesAbiertas.Length);
                if (i >= 0)
                    _juego.Elegir(i);

                GetViewport().SetInputAsHandled();
                break;
            }

            // Las teclas 1-2-3 hacen lo mismo que el click. Con una corrida de
            // horas, poder elegir sin soltar el mouse importa. Se marcan
            // atendidas para que el 1-2-3 no cambie además la velocidad.
            case InputEventKey { Pressed: true, Echo: false } tecla:
            {
                int i = tecla.Keycode switch
                {
                    Key.Key1 => 0,
                    Key.Key2 => 1,
                    Key.Key3 => 2,
                    _ => -1,
                };

                if (i >= 0 && i < partida.OpcionesAbiertas.Length)
                {
                    _juego.Elegir(i);
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

    private static Rect2 RectDe(int i, int cantidad)
    {
        int anchoTotal = (cantidad * AnchoTarjeta) + ((cantidad - 1) * Separacion);
        int x = ((Escala.LienzoAncho - anchoTotal) / 2) + (i * (AnchoTarjeta + Separacion));
        int y = (Escala.LienzoAlto - AltoTarjeta) / 2;

        return new Rect2(x, y, AnchoTarjeta, AltoTarjeta);
    }

    public override void _Draw()
    {
        var partida = _juego.Partida;
        if (partida is null || !partida.EsperandoEleccion)
            return;

        var opciones = partida.OpcionesAbiertas;

        // El mundo se oscurece pero se sigue viendo: el jugador decide mirando su
        // defensa, no de memoria.
        DrawRect(new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto), new Color(0, 0, 0, 0.72f));

        Texto("ELEGÍ UNA", Escala.LienzoAncho / 2 - 130, 210, 44, new Color(0.95f, 0.95f, 0.92f));
        Texto("no se puede cerrar sin elegir",
            Escala.LienzoAncho / 2 - 150, 250, 20, new Color(0.6f, 0.6f, 0.58f));

        for (int i = 0; i < opciones.Length; i++)
            DibujarTarjeta(i, opciones[i], opciones.Length, partida);
    }

    private void DibujarTarjeta(int i, Opcion opcion, int cantidad, Partida partida)
    {
        var r = RectDe(i, cantidad);
        bool resaltada = i == _bajoElCursor;

        bool contrata = opcion.Tipo == TipoOpcion.Contratar;

        var acento = contrata
            ? new Color(0.35f, 0.65f, 0.95f)
            : new Color(0.95f, 0.72f, 0.25f);

        DrawRect(r, new Color(0.09f, 0.09f, 0.13f, 0.97f));
        DrawRect(r, resaltada ? acento : new Color(1, 1, 1, 0.15f), filled: false, width: resaltada ? 4f : 2f);

        // Franja de color arriba: contratar y levelear se distinguen de un
        // vistazo, antes de leer una palabra.
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 8), acento);

        float x = r.Position.X + 32;
        float y = r.Position.Y + 70;

        Texto(contrata ? "CONTRATAR" : "SUBIR DE NIVEL", x, y, 22, acento);

        y += 56;
        Texto(NombreDe(opcion.Clase), x, y, 40, new Color(0.95f, 0.95f, 0.92f));

        y += 50;
        var stats = CargadorDatos.Actual.StatsDe(opcion.Clase);
        Texto($"{stats.Dano} de daño · alcance {stats.AlcanceCeldas} · {stats.Costo} de oro",
            x, y, 19, new Color(0.7f, 0.7f, 0.68f));

        y += 54;

        if (contrata)
        {
            Texto("Rol nuevo, nivel 1.", x, y, 21, new Color(0.82f, 0.82f, 0.8f));
            y += 30;
            Texto("Arranca con 1 slot de perk.", x, y, 21, new Color(0.82f, 0.82f, 0.8f));
        }
        else
        {
            var ficha = partida.Roster.Buscar(opcion.Clase);
            int slotsAntes = ficha?.SlotsAbiertos ?? 1;

            Texto($"Nivel {opcion.NivelResultante - 1} → {opcion.NivelResultante}",
                x, y, 21, new Color(0.82f, 0.82f, 0.8f));

            y += 30;

            // ⚠ Esta línea es la que hace que la elección sea una elección. Sin
            // ella, contratar parece siempre mejor porque trae algo visiblemente
            // nuevo, y levelear parece "solo números".
            int slotsDespues = System.Math.Min(opcion.NivelResultante, CargadorDatos.Actual.Clases.SlotsPerkPorClase);

            Texto(slotsDespues > slotsAntes
                    ? $"Abre el slot de perk {slotsDespues} de {CargadorDatos.Actual.Clases.SlotsPerkPorClase}."
                    : "Ya tiene los 6 slots abiertos.",
                x, y, 21, new Color(0.95f, 0.85f, 0.5f));
        }

        Texto($"[{i + 1}]", r.Position.X + r.Size.X - 70, r.Position.Y + r.Size.Y - 28, 24,
            new Color(0.5f, 0.5f, 0.5f));
    }

    /// <summary>
    /// ⚠ Cazador, nunca "arquero". Es el error de nombre más fácil de cometer en
    /// este proyecto porque "arquero" es lo que uno esperaría de un tower defense,
    /// pero en Argentum Online esa clase no existe.
    /// </summary>
    public static string NombreDe(Clase clase) => clase switch
    {
        Clase.Mago => "Mago",
        Clase.Clerigo => "Clérigo",
        Clase.Guerrero => "Guerrero",
        Clase.Asesino => "Asesino",
        Clase.Ladron => "Ladrón",
        Clase.Bardo => "Bardo",
        Clase.Druida => "Druida",
        Clase.Paladin => "Paladín",
        Clase.Cazador => "Cazador",
        _ => "?",
    };

    private void Texto(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
