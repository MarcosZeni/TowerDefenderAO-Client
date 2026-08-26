namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Aritmética de la simulación. <b>Todo entero, siempre.</b>
///
/// <para>Adentro de este proyecto están prohibidos <c>float</c>, <c>double</c>,
/// <c>Math.Sin</c>, <c>Math.Cos</c>, <c>Math.Atan2</c>, <c>Math.Pow</c>,
/// <c>Math.Log</c> y <c>System.Random</c>. En .NET moderno <c>+ - * /</c> son
/// reproducibles entre plataformas, pero las funciones trascendentes las
/// resuelve la libm de cada sistema y ahí no hay garantía. Con enteros la
/// pregunta ni se abre, y no hay deriva acumulada a lo largo de los ~216.000
/// ticks de una corrida larga.</para>
/// </summary>
public static class Enteros
{
    /// <summary>Ticks por segundo. Fijo y congelado (JUGABILIDAD.md §4.1).</summary>
    public const int TicksPorSegundo = 30;

    /// <summary>
    /// Milésimas de celda por celda. La posición de un enemigo en la ruta es un
    /// <c>long</c> en esta unidad, así que mover es una suma y nada más.
    /// </summary>
    public const int MilesimasPorCelda = 1000;

    /// <summary>
    /// Aplica una lista de modificadores porcentuales a un valor base.
    ///
    /// <para><b>Este método existe para que el orden de las operaciones sea uno
    /// solo en todo el proyecto.</b> Con enteros, <c>(a * p) / 100</c> y
    /// <c>a * (p / 100)</c> dan resultados distintos, y con varios perks encima
    /// las diferencias de redondeo se acumulan. La regla, escrita una vez y
    /// usada siempre: <b>sumar todos los porcentajes primero, multiplicar una
    /// vez, dividir al final</b>.</para>
    ///
    /// <para>Aplicarlos de a uno —<c>x = x * 110 / 100</c> tres veces— pierde
    /// una fracción en cada división, y dos defensores con los mismos perks en
    /// distinto orden terminarían pegando distinto.</para>
    /// </summary>
    /// <param name="baseValor">El valor sin modificar.</param>
    /// <param name="porcentajesExtra">
    /// Los aportes, en puntos porcentuales sobre 100. Un perk de "+15% de daño"
    /// aporta <c>15</c>, no <c>115</c>: acá se suman los extras y el 100 base lo
    /// pone este método.
    /// </param>
    public static int ConPorcentajes(int baseValor, params ReadOnlySpan<int> porcentajesExtra)
    {
        int total = 100;
        foreach (int p in porcentajesExtra)
            total += p;

        // Un stack de reducciones no puede dar daño negativo: eso convertiría un
        // ataque en una curación, que no es lo que quiso decir ningún perk.
        if (total < 0)
            total = 0;

        return MultiplicarPorcentaje(baseValor, total);
    }

    /// <summary>
    /// <c>valor * porcentaje / 100</c>, en <c>long</c> por dentro.
    /// </summary>
    /// <remarks>
    /// El paso por <c>long</c> no es exceso de cuidado: el jefe final tiene
    /// <c>MaxHIT</c> de 5.000 y el Golem 23.000 HP, y un HP alto con un
    /// porcentaje alto desborda <c>int</c> antes de la división. Desbordar en la
    /// simulación no tira excepción —en C# el <c>int</c> da la vuelta en
    /// silencio— así que el bug aparecería como un enemigo que se cura al
    /// recibir un golpe muy fuerte.
    /// </remarks>
    public static int MultiplicarPorcentaje(int valor, int porcentaje)
    {
        long resultado = (long)valor * porcentaje / 100;
        return Recortar(resultado);
    }

    /// <summary>
    /// Baja un <c>long</c> a <c>int</c> sin dar la vuelta: satura en los
    /// extremos. Se prefiere un número absurdo pero acotado a uno que cambió de
    /// signo.
    /// </summary>
    public static int Recortar(long v)
    {
        if (v > int.MaxValue) return int.MaxValue;
        if (v < int.MinValue) return int.MinValue;
        return (int)v;
    }

    /// <summary>Deja <paramref name="v"/> adentro de <c>[min, max]</c>.</summary>
    public static int Limitar(int v, int min, int max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    /// <summary>
    /// Cuadrado de la distancia entre dos celdas.
    /// </summary>
    /// <remarks>
    /// <b>Al cuadrado a propósito: no hay raíces en la simulación.</b> Para
    /// saber si algo está en alcance alcanza con comparar contra el alcance
    /// también al cuadrado, y así la operación es una resta, dos multiplicaciones
    /// y una suma — todo exacto, sin decimales y sin depender de la libm.
    /// </remarks>
    public static long DistanciaCuadrado(int ax, int ay, int bx, int by)
    {
        long dx = ax - bx;
        long dy = ay - by;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Convierte segundos a ticks.</summary>
    public static int SegundosATicks(int segundos) => segundos * TicksPorSegundo;

    /// <summary>
    /// Convierte décimas de segundo a ticks. A 30 Hz una décima son 3 ticks
    /// exactos, así que los cooldowns se pueden escribir en los JSON de balance
    /// en una unidad que se lee, sin perder precisión.
    /// </summary>
    public static int DecimasATicks(int decimas) => decimas * TicksPorSegundo / 10;
}
