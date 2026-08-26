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
public static class RelojFino
{
    private static readonly System.Diagnostics.Stopwatch _fino =
        System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Milisegundos desde que arrancó el proceso, con resolución de 1 ms.</summary>
    public static long Ms => _fino.ElapsedMilliseconds;
}
