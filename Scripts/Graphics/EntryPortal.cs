using Godot;

namespace TowerOfArgentum;

/// <summary>
/// El portal por donde entran los enemigos, hecho de partículas.
/// </summary>
/// <remarks>
/// <para><b>No hay gráfico y es a propósito.</b> Un sprite de portal habría que
/// traerlo, animarlo y hacerlo pegar con ocho biomas distintos; unas partículas
/// se ven vivas sobre cualquier piso, no necesitan hoja en
/// <c>graficos.json</c> y no compiten con los bichos, que son lo que el jugador
/// tiene que mirar.</para>
///
/// <para>Son dos cosas: un <b>remolino</b> —velocidad tangencial con una pizca de
/// atracción al centro, que es lo que hace la espiral— y un <b>resplandor</b>
/// que late abajo de todo para apoyar el remolino en el piso. Sin el
/// resplandor las partículas quedan flotando sobre la nada.</para>
///
/// <para>⚠ <b>Esto es decoración pura.</b> No sabe de ticks, no toca la
/// simulación y usa el reloj de pared: el latido se ve igual de lindo en pausa,
/// y que el portal siga vivo mientras el jugador piensa es justamente lo que
/// hace que la pantalla de preparación no parezca colgada.</para>
/// </remarks>
public partial class EntryPortal : Node2D
{
    /// <summary>Radio del remolino, en píxeles. Poco menos de media celda.</summary>
    private const float Radio = 40f;

    private static ImageTexture? _mota;

    private readonly Color _color;

    public EntryPortal(Color color)
    {
        _color = color;
    }

    public override void _Ready()
    {
        // Sin animaciones el portal es solo el resplandor quieto: sigue marcando
        // de dónde salen los bichos, que es su trabajo.
        if (!Settings.AnimateEffects)
            return;

        var brasas = new CpuParticles2D
        {
            Amount = 72,
            Lifetime = 1.7,

            // Sin esto el portal arranca vacío y se llena en dos segundos: la
            // primera imagen que ve el jugador sería la peor.
            Preprocess = 1.7,

            Texture = Mota(),
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = Radio,

            // Nada de gravedad: esto no cae, gira.
            Gravity = Vector2.Zero,

            Spread = 180f,
            InitialVelocityMin = 6f,
            InitialVelocityMax = 18f,

            // El remolino: tangencial empuja alrededor, radial negativo tira para
            // adentro. La espiral sale de que las dos actúen a la vez.
            TangentialAccelMin = 60f,
            TangentialAccelMax = 130f,
            RadialAccelMin = -55f,
            RadialAccelMax = -25f,

            ScaleAmountMin = 0.18f,
            ScaleAmountMax = 0.42f,

            Color = _color,
            ColorRamp = Desvanecido(_color),
        };

        AddChild(brasas);
    }

    public override void _Draw()
    {
        // Late despacio, con el reloj de pared. Dos senos de período distinto para
        // que el latido no se sienta metronómico.
        float latido = 0.82f;

        if (Settings.AnimateEffects)
        {
            float t = FineClock.Ms / 1000f;
            latido += (0.12f * Mathf.Sin(t * 2.1f)) + (0.06f * Mathf.Sin(t * 0.7f));
        }

        // De adentro para afuera, cada anillo más tenue: un degradé dibujado a
        // mano sale más barato que una textura de resplandor y no hay que traerla.
        for (int i = 5; i >= 1; i--)
        {
            float radio = Radio * latido * i / 4f;
            float alfa = 0.16f / i;

            DrawCircle(Vector2.Zero, radio, new Color(_color.R, _color.G, _color.B, alfa));
        }
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>
    /// Una mota redonda y difusa, dibujada en código.
    /// </summary>
    /// <remarks>
    /// Sin textura, <see cref="CpuParticles2D"/> dibuja cuadraditos duros y el
    /// portal parece confeti. Se genera una vez y la comparten todos los portales.
    /// </remarks>
    private static ImageTexture Mota()
    {
        if (_mota is not null)
            return _mota;

        const int Side = 32;

        var imagen = Image.CreateEmpty(Side, Side, false, Image.Format.Rgba8);
        var centro = new Vector2(Side / 2f, Side / 2f);

        for (int y = 0; y < Side; y++)
        {
            for (int x = 0; x < Side; x++)
            {
                float d = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(centro) / (Side / 2f);

                // Al cuadrado: la caída lineal deja un borde que se nota.
                float a = Mathf.Clamp(1f - d, 0f, 1f);

                imagen.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        }

        _mota = ImageTexture.CreateFromImage(imagen);
        return _mota;
    }

    /// <summary>Del color pleno al transparente, para que las motas se apaguen.</summary>
    /// <remarks>
    /// ⚠ Un <see cref="Gradient"/> recién creado <b>ya trae dos puntos</b>, negro
    /// en 0 y blanco en 1. Hay que pisarlos, no agregar encima: agregando queda un
    /// punto blanco opaco en el final y las motas terminan en blanco.
    /// </remarks>
    private static Gradient Desvanecido(Color color)
    {
        var invisible = new Color(color.R, color.G, color.B, 0f);
        var rampa = new Gradient();

        rampa.SetOffset(0, 0f);
        rampa.SetColor(0, invisible);

        rampa.SetOffset(1, 1f);
        rampa.SetColor(1, invisible);

        rampa.AddPoint(0.25f, color);

        return rampa;
    }
}
