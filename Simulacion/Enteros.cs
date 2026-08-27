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
public static class Ints
{
    /// <summary>Ticks por segundo. Fijo y congelado (JUGABILIDAD.md §4.1).</summary>
    public const int TicksPerSecond = 30;

    /// <summary>
    /// Milésimas de celda por celda. La posición de un enemigo en la ruta es un
    /// <c>long</c> en esta unidad, así que mover es una suma y nada más.
    /// </summary>
    /// <remarks>
    /// ⚠ Desde la propuesta V2 el <b>alcance</b> también vive en esta unidad. Con
    /// alcance en celdas enteras, un perk de +15% sobre un alcance de 2 daba 2,3
    /// y truncaba de vuelta a 2: el perk no hacía absolutamente nada, y el
    /// jugador gastaba un slot y un drop en un número que no se movía. Con
    /// milésimas, 2000 + 15% son 2300 y el círculo de alcance crece de verdad.
    /// </remarks>
    public const int ThousandthsPerCell = 1000;

    /// <summary>
    /// Centésimas de punto por punto. <b>La unidad interna del daño y del HP.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠ Es la anomalía P0 de la propuesta V2, y el motivo es el mismo que
    /// el del alcance: con daño entero y chico, el truncamiento se comía mejoras
    /// enteras. Un cazador de daño base 4 no subía nada en los niveles 2, 3, 4 ni
    /// 6 —4 × 1,08 = 4,32 → 4— así que subirlo de nivel era tirar la elección.
    /// Medido: 4 de 5 niveles mudos en Cazador, 3 de 5 en Clérigo, Guerrero,
    /// Ladrón y Bardo.</para>
    ///
    /// <para><b>La salida NO es punto flotante: es una unidad más chica.</b> Los
    /// datos siguen escritos en puntos enteros —que es como se leen y se
    /// balancean— y la simulación multiplica por esto al construir la entidad.
    /// Adentro todo es centésimas, así que un +8% de 400 son 432 y se nota.</para>
    ///
    /// <para>⚠ La escala vale para <b>los dos lados a la vez</b>: daño del
    /// defensor, HP del enemigo, HP y daño de la Torre, daño del enemigo a la
    /// Torre y la cura de Vampirismo. Escalar uno solo cambiaría el balance en
    /// vez de afinarlo. Lo que NO se escala es todo lo que se mide en porcentaje
    /// —perks, habilidades, críticos— porque un porcentaje no tiene unidad, ni el
    /// costo de los NPCs del presupuesto de oleada, que se calcula sobre el
    /// bestiario en puntos enteros.</para>
    /// </remarks>
    public const int HundredthsPerPoint = 100;

    /// <summary>
    /// Pasa puntos enteros —como están escritos en los datos— a la escala interna.
    /// </summary>
    public static int ToHundredths(int puntos) => Trim((long)puntos * HundredthsPerPoint);

    /// <summary>
    /// Vuelve de la escala interna a puntos enteros, para mostrar. <b>Trunca.</b>
    /// </summary>
    public static int ToPoints(int centesimas) => centesimas / HundredthsPerPoint;

    /// <summary>Pasa celdas enteras a milésimas, la unidad interna del alcance.</summary>
    public static int ToThousandths(int celdas) => Trim((long)celdas * ThousandthsPerCell);

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
    public static int WithPercents(int baseValor, params ReadOnlySpan<int> porcentajesExtra)
    {
        int total = 100;
        foreach (int p in porcentajesExtra)
            total += p;

        // Un stack de reducciones no puede dar daño negativo: eso convertiría un
        // ataque en una curación, que no es lo que quiso decir ningún perk.
        if (total < 0)
            total = 0;

        return MultiplyPercent(baseValor, total);
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
    public static int MultiplyPercent(int valor, int porcentaje)
    {
        long resultado = (long)valor * porcentaje / 100;
        return Trim(resultado);
    }

    /// <summary>
    /// Baja un <c>long</c> a <c>int</c> sin dar la vuelta: satura en los
    /// extremos. Se prefiere un número absurdo pero acotado a uno que cambió de
    /// signo.
    /// </summary>
    public static int Trim(long v)
    {
        if (v > int.MaxValue) return int.MaxValue;
        if (v < int.MinValue) return int.MinValue;
        return (int)v;
    }

    /// <summary>Deja <paramref name="v"/> adentro de <c>[min, max]</c>.</summary>
    public static int Clamp(int v, int min, int max)
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
    public static long SquaredDistance(int ax, int ay, int bx, int by)
    {
        long dx = ax - bx;
        long dy = ay - by;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// Lo mismo, pero en <b>milésimas de celda al cuadrado</b>: la unidad en la
    /// que está el alcance de un defensor.
    /// </summary>
    /// <remarks>
    /// <para>Las celdas siguen siendo enteras —un defensor ocupa una celda y un
    /// enemigo está en la celda que le toca de la ruta—, así que la distancia se
    /// calcula igual y se convierte al final. Convertir después y no antes evita
    /// multiplicar dos números grandes: el cuadrado de la diferencia nunca pasa
    /// de unos cientos, y recién ahí se escala.</para>
    ///
    /// <para>El peor caso son 20×11 celdas: 521 × 1.000.000 = 5,21e8, muy adentro
    /// de <c>long</c>.</para>
    /// </remarks>
    public static long SquaredDistanceThousandths(int ax, int ay, int bx, int by)
        => SquaredDistance(ax, ay, bx, by) * ThousandthsPerCell * ThousandthsPerCell;

    /// <summary>
    /// Cuántas celdas enteras hay que mirar para cubrir un alcance en milésimas.
    /// </summary>
    /// <remarks>
    /// Redondea <b>para arriba</b>: la grilla espacial devuelve candidatos, y
    /// quedarse corto acá haría que un enemigo dentro del alcance real no llegue
    /// nunca a compararse. Un candidato de más solo cuesta una resta.
    /// </remarks>
    public static int ThousandthsToCellsUp(int milesimas)
        => (milesimas + ThousandthsPerCell - 1) / ThousandthsPerCell;

    /// <summary>Convierte segundos a ticks.</summary>
    public static int SecondsToTicks(int segundos) => segundos * TicksPerSecond;

    /// <summary>
    /// Convierte décimas de segundo a ticks. A 30 Hz una décima son 3 ticks
    /// exactos, así que los cooldowns se pueden escribir en los JSON de balance
    /// en una unidad que se lee, sin perder precisión.
    /// </summary>
    public static int DecimasATicks(int decimas) => decimas * TicksPerSecond / 10;
}
