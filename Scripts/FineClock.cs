namespace TowerOfArgentum;

/// <summary>
/// Reloj de milisegundos de alta resolución.
/// </summary>
/// <remarks>
/// <para>Copiado del cliente de Austral, sin la parte del anclaje: allá el reloj
/// tiene que convivir con sellos de tiempo viejos escritos contra
/// <c>Environment.TickCount64</c>, y acá no hay nada de eso que respetar.</para>
///
/// <para><b>Por qué no <c>TickCount64</c>:</b> en Windows avanza de a ~15,6 ms,
/// porque el temporizador del sistema corre a 64 Hz. Un tick de simulación son
/// 33,3 ms, así que medir el acumulador con esa resolución daría a veces 0 ticks
/// y a veces 2 — el juego se vería a los saltos aunque los FPS estuvieran
/// perfectos.</para>
///
/// <para><b>Por qué no <c>Time.GetTicksMsec()</c> de Godot:</b> esto se puede
/// llamar desde cualquier hilo, y la API de Godot no.</para>
///
/// <para>⚠ Este reloj <b>no entra a la simulación</b>. Decide cuántos ticks
/// correr, nunca qué pasa adentro de uno. La simulación no sabe qué hora es: sabe
/// en qué número de tick va.</para>
/// </remarks>
public static class FineClock
{
    private static readonly System.Diagnostics.Stopwatch _fino =
        System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Ticks del cronómetro por segundo. En Windows, 10.000.000.</summary>
    private static readonly long _frecuencia = System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Milisegundos desde que arrancó el proceso, con resolución de 1 ms.</summary>
    public static long Ms => _fino.ElapsedMilliseconds;

    /// <summary>Microsegundos desde que arrancó el proceso.</summary>
    /// <remarks>
    /// <para><b>Por qué no alcanza el milisegundo.</b> Un frame a 60 Hz dura
    /// 16,67 ms y uno a 144 Hz dura 6,94 ms. Medido en milisegundos enteros ese
    /// frame llega a veces como 16 y a veces como 17, y ese error se convierte en
    /// un tick de más o de menos: el acumulador late en vez de avanzar parejo.
    /// Con microsegundos el resto de cada frame se conserva, y la fracción de
    /// tick que usa la interpolación del render es continua.</para>
    ///
    /// <para>La cuenta se parte en cociente y resto para no desbordar:
    /// <c>ElapsedTicks * 1.000.000</c> a pelo se pasa de <c>long</c> después de
    /// unos días de proceso.</para>
    ///
    /// <para>⚠ Vale lo mismo que para <see cref="Ms"/>: <b>este reloj no entra a
    /// la simulación.</b> Decide cuántos ticks correr y en qué punto entre dos
    /// ticks se dibuja; nunca qué pasa adentro de uno.</para>
    /// </remarks>
    public static long Us
    {
        get
        {
            long t = _fino.ElapsedTicks;

            return ((t / _frecuencia) * 1_000_000)
                 + ((t % _frecuencia) * 1_000_000 / _frecuencia);
        }
    }
}
