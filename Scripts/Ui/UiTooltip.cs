using Godot;

namespace TowerOfArgentum.Ui;

/// <summary>
/// El cartelito que explica qué es un icono, mientras el cursor está encima.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Existe porque los iconos reemplazaron palabras.</b> Cambiar
/// «daño 7» por una espada y un 7 alinea las columnas y saca el largo del texto
/// de la ecuación, pero le pide al jugador que <b>ya sepa</b> qué significa cada
/// dibujo. Un reloj puede ser cadencia, duración o tiempo restante. El cartel es
/// lo que hace que el icono no sea un acertijo la primera vez.</para>
///
/// <para><b>Cómo funciona con dibujo inmediato:</b> la interfaz se redibuja
/// entera cada cuadro, así que no hay nodos a los que colgarles un tooltip. En
/// vez de eso, cada pieza que quiere explicarse <b>declara su rectángulo</b>
/// mientras se dibuja, y el que gana es el último que contiene al cursor — que,
/// por el orden de dibujo, es también el que está más arriba. La pantalla llama
/// <see cref="Comenzar"/> al empezar su <c>_Draw</c> y <see cref="Dibujar"/> al
/// terminarlo, para que el cartel quede por encima de todo lo demás.</para>
///
/// <para>⚠ <b>Es estático y eso está bien acá.</b> Vive un solo cuadro: se
/// resetea al empezar cada <c>_Draw</c>, así que dos pantallas abiertas no se
/// pisan — la de arriba dibuja última y su cartel es el que queda. Guardar
/// estado entre cuadros sería lo que dejaría un tooltip pegado después de cerrar
/// la pantalla que lo había puesto.</para>
/// </remarks>
public static class UiTooltip
{
    private static Vector2 _cursor;
    private static string _texto = "";
    private static Rect2 _ancla;

    /// <summary>
    /// Arranca el cuadro. Se llama al principio del <c>_Draw</c> de la pantalla.
    /// </summary>
    /// <param name="cursor">
    /// La posición del mouse <b>en el mismo espacio que los rectángulos</b>. Para
    /// las pantallas eso es <c>GetGlobalMousePosition()</c>; el mundo, que está
    /// corrido y a escala adentro del marco, no usa esto.
    /// </param>
    public static void Comenzar(Vector2 cursor)
    {
        _cursor = cursor;
        _texto = "";
    }

    /// <summary>Declara que este rectángulo se explica con este texto.</summary>
    /// <remarks>
    /// Gana el <b>último</b> que contenga al cursor. Como el orden de las
    /// llamadas es el orden de dibujo, el último es también el que está encima —
    /// que es exactamente el que el jugador cree estar señalando.
    /// </remarks>
    public static void Sobre(Rect2 r, string texto)
    {
        if (texto.Length == 0 || !r.HasPoint(_cursor))
            return;

        _texto = texto;
        _ancla = r;
    }

    /// <summary>
    /// Dibuja el cartel, si hay. Se llama al final del <c>_Draw</c>.
    /// </summary>
    /// <remarks>
    /// <para>Va <b>arriba</b> de lo señalado y no abajo: abajo lo tapa el cursor,
    /// que es una flecha que apunta hacia arriba y a la izquierda. Si no entra
    /// arriba —el icono está pegado al borde superior— se da vuelta.</para>
    ///
    /// <para>Y se recorta contra el lienzo a los costados. Un cartel que se sale
    /// por la derecha es peor que uno corrido, porque la mitad que falta es
    /// justo el final de la frase.</para>
    /// </remarks>
    public static void Dibujar(CanvasItem ci)
    {
        if (_texto.Length == 0)
            return;

        const int Tamano = 14;
        const float Pad = 10f;

        float ancho = UiStyle.Width(_texto, Tamano) + (Pad * 2f);
        float alto = Tamano + (Pad * 1.4f);

        float x = _ancla.Position.X + (_ancla.Size.X / 2f) - (ancho / 2f);
        float y = _ancla.Position.Y - alto - 6f;

        if (y < 4f)
            y = _ancla.End.Y + 6f;

        x = Mathf.Clamp(x, 4f, Escala.LienzoAncho - ancho - 4f);

        var caja = new Rect2(x, y, ancho, alto);

        ci.DrawRect(caja, Palette.BackgroundMain.Alpha(0.97f));
        ci.DrawRect(caja, Palette.BorderLight, filled: false, width: 1.5f);
        ci.Corners(caja, Palette.BorderLight.Alpha(0.9f), 1.5f);

        ci.Label(caja, _texto, Tamano, Palette.TextPrimary, HorizontalAlignment.Center);
    }
}
