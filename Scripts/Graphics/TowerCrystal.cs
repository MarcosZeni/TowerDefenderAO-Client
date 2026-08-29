using Godot;
using TowerOfArgentum.Simulation;

namespace TowerOfArgentum;

/// <summary>
/// La Torre: un cristal flotante, dibujado en código.
/// </summary>
/// <remarks>
/// <para><b>Es un prisma hexagonal que gira de verdad.</b> Las seis aristas viven
/// en un círculo y giran con el reloj; de cada cara se calcula su normal y se
/// dibuja solo si mira al jugador, con el brillo que le da el ángulo contra la
/// luz. Eso es lo que hace que el cristal se lea como un volumen y no como un
/// dibujo plano girando.</para>
///
/// <para>Vale más que un sprite por tres razones concretas: <b>no hay hoja que
/// traer</b>, gira sin cuadros de animación, y —lo que decide— <b>el color es el
/// medidor de HP</b>. Un cristal que pasa de celeste a rojo y late cada vez más
/// rápido dice cuánta vida le queda a la Torre sin que el jugador tenga que
/// mirar el HUD, que es justo lo que no puede hacer cuando lo están atacando.</para>
///
/// <para>⚠ <b>Decoración pura.</b> Lee <see cref="Torre.Hp"/> y nada más; no
/// escribe, no toca el tick y anda con el reloj de pared. Ver
/// <see cref="EntryPortal"/>, que es el otro extremo de la ruta.</para>
/// </remarks>
public partial class TowerCrystal : Node2D
{
    /// <summary>Radio del prisma, en píxeles.</summary>
    private const float Radio = 30f;

    /// <summary>Alto de la cintura: de acá para arriba y para abajo van las puntas.</summary>
    private const float Cintura = 18f;

    private const float PuntaArriba = 60f;
    private const float PuntaAbajo = 50f;

    /// <summary>De dónde viene la luz, en radianes. Arriba y a la izquierda.</summary>
    private const float Luz = -0.9f;

    private readonly Game _juego;

    public TowerCrystal(Game juego)
    {
        _juego = juego;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var torre = _juego.Run.Tower;

        float vida = torre.MaxHp <= 0
            ? 0f
            : Mathf.Clamp((float)torre.Hp / torre.MaxHp, 0f, 1f);

        if (!torre.Standing)
        {
            // Caído: no flota, no late, no gira. Queda gris y hundido, que es lo
            // que hay que ver de un vistazo cuando la corrida terminó.
            DrawCrystal(0f, 0f, new Color(0.34f, 0.32f, 0.36f), 0f);
            return;
        }

        // Con las animaciones apagadas el cristal queda quieto y de tres cuartos,
        // pero conserva el COLOR: el color es el medidor de HP, o sea información,
        // no adorno. Lo que se apaga es el giro, el flote y el latido.
        if (!Settings.AnimateEffects)
        {
            DrawCrystal(0.6f, 0f, ColorDeVida(vida), 0.5f);
            return;
        }

        float t = FineClock.Ms / 1000f;

        // Late más rápido cuanto menos vida: a full es un respiro, en rojo es una
        // alarma. La frecuencia es lo que se percibe antes que el color.
        float ritmo = Mathf.Lerp(6.5f, 1.6f, vida);
        float latido = 0.5f + (0.5f * Mathf.Sin(t * ritmo));

        // Flota. Dos senos de período distinto para que no se sienta un metrónomo.
        float flote = (Mathf.Sin(t * 1.3f) * 5f) + (Mathf.Sin(t * 0.51f) * 2.5f);

        DrawCrystal(t * 0.55f, flote, ColorDeVida(vida), latido);
    }

    /// <summary>
    /// De celeste a rojo, pasando por ámbar. El color ES el medidor de HP.
    /// </summary>
    /// <remarks>
    /// Tres tramos y no un degradé de dos puntas: el celeste al rojo directo pasa
    /// por un violeta sucio que no dice nada. El ámbar del medio es el aviso.
    /// </remarks>
    private static Color ColorDeVida(float vida)
    {
        // Los mismos tres de la barra de vida del HUD, para que el cristal y la
        // barra digan lo mismo con el mismo color. El celeste del sano es el Info
        // de la paleta: la Torre no es un enemigo, y el verde ya es «se puede
        // poner acá».
        var sano = Ui.Palette.Info;
        var aviso = Ui.Palette.Warning;
        var grave = Ui.Palette.Danger;

        return vida > 0.5f
            ? aviso.Lerp(sano, (vida - 0.5f) / 0.5f)
            : grave.Lerp(aviso, vida / 0.5f);
    }

    private void DrawCrystal(float giro, float flote, Color color, float latido)
    {
        var centro = new Vector2(0, flote);

        DibujarSombra(flote);

        // Las seis aristas del prisma, en un círculo que gira. Se guarda el seno
        // para la pantalla y el coseno para saber si la cara mira al jugador.
        var x = new float[7];
        var z = new float[7];

        for (int i = 0; i <= 6; i++)
        {
            float a = giro + (i * Mathf.Pi / 3f);
            x[i] = Mathf.Sin(a) * Radio;
            z[i] = Mathf.Cos(a);
        }

        for (int i = 0; i < 6; i++)
        {
            // La normal de la cara apunta al medio de sus dos aristas. Si mira
            // para atrás, la cara está del otro lado del cristal: no se dibuja.
            float normal = giro + ((i + 0.5f) * Mathf.Pi / 3f);

            if (Mathf.Cos(normal) <= 0f)
                continue;

            // Difuso de toda la vida, con piso: una cara de canto no queda negra,
            // queda apenas más oscura. Un cristal no absorbe luz.
            float brillo = 0.46f + (0.54f * Mathf.Max(0f, Mathf.Cos(normal - Luz)));

            var cara = Tenir(color, brillo, 0.90f);

            DrawColoredPolygon(
                [
                    centro + new Vector2(x[i], -Cintura),
                    centro + new Vector2(x[i + 1], -Cintura),
                    centro + new Vector2(x[i + 1], Cintura),
                    centro + new Vector2(x[i], Cintura),
                ],
                cara);

            // Las puntas: la de arriba recibe más luz que la cintura, la de abajo
            // menos. Sin esa diferencia el cristal parece un tubo.
            DrawColoredPolygon(
                [
                    centro + new Vector2(0, -PuntaArriba),
                    centro + new Vector2(x[i], -Cintura),
                    centro + new Vector2(x[i + 1], -Cintura),
                ],
                Tenir(color, brillo + 0.22f, 0.92f));

            DrawColoredPolygon(
                [
                    centro + new Vector2(0, PuntaAbajo),
                    centro + new Vector2(x[i], Cintura),
                    centro + new Vector2(x[i + 1], Cintura),
                ],
                Tenir(color, brillo - 0.20f, 0.92f));
        }

        DibujarNucleo(centro, color, latido);
    }

    /// <summary>
    /// El corazón que late, adentro del cristal.
    /// </summary>
    /// <remarks>
    /// Va <b>después</b> de las caras y semitransparente: así se lo ve a través
    /// del cristal, que es lo que lo hace parecer adentro y no pegado encima.
    /// </remarks>
    private void DibujarNucleo(Vector2 centro, Color color, float latido)
    {
        float radio = Radio * (0.34f + (0.16f * latido));

        for (int i = 3; i >= 1; i--)
        {
            DrawCircle(
                centro,
                radio * i / 2f,
                new Color(
                    Mathf.Min(1f, color.R + 0.35f),
                    Mathf.Min(1f, color.G + 0.35f),
                    Mathf.Min(1f, color.B + 0.35f),
                    0.30f / i));
        }
    }

    /// <summary>
    /// La sombra en el piso. Se achica y se aclara cuando el cristal sube.
    /// </summary>
    /// <remarks>
    /// Es lo único que dice a qué altura está. Sin sombra un objeto que sube y
    /// baja se lee como uno que se agranda y se achica.
    /// </remarks>
    private void DibujarSombra(float flote)
    {
        // flote va de −7,5 a 7,5: se normaliza a 0..1 para no repetir el número.
        float altura = Mathf.Clamp((flote + 7.5f) / 15f, 0f, 1f);

        float ancho = Radio * (1.05f - (0.22f * altura));
        float alfa = 0.34f - (0.12f * altura);

        DrawSetTransform(new Vector2(0, PuntaAbajo + 6f), 0f, new Vector2(1f, 0.34f));
        DrawCircle(Vector2.Zero, ancho, new Color(0, 0, 0, alfa));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private static Color Tenir(Color color, float brillo, float alfa)
        => new(
            Mathf.Clamp(color.R * brillo, 0f, 1f),
            Mathf.Clamp(color.G * brillo, 0f, 1f),
            Mathf.Clamp(color.B * brillo, 0f, 1f),
            alfa);
}
