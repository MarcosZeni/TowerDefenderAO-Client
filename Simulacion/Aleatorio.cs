namespace TowerOfArgentum.Simulacion;

/// <summary>
/// El generador de números al azar de la corrida. <b>xorshift128+</b>, escrito
/// acá y no tomado de la plataforma.
///
/// <para><b>Por qué no <c>System.Random</c>:</b> cambió de algoritmo en .NET 6 y
/// no promete estabilidad entre versiones. El servidor revalida una corrida
/// re-simulándola; si lo hace con otro runtime y el generador da otra secuencia,
/// sale otra corrida y la revalidación acusa de tramposo a quien no hizo
/// nada.</para>
///
/// <para><b>Hay uno solo por corrida</b>, sembrado con la seed que manda el
/// servidor. Nadie más pide números al azar: ni el render, ni la UI, ni un
/// efecto de partículas. Un segundo generador escondido en algún lado es
/// exactamente el bug que hace fallar la revalidación sin dejar rastro.</para>
/// </summary>
public sealed class Rng
{
    private ulong _s0;
    private ulong _s1;

    /// <summary>
    /// Siembra el generador desde la seed de la corrida.
    /// </summary>
    /// <remarks>
    /// El estado se expande con <b>splitmix64</b> en vez de copiar la seed tal
    /// cual, por dos motivos: xorshift128+ con estado todo en cero se queda
    /// pegado en cero para siempre, y sembrado con un número chico arrastra
    /// varios valores pobres antes de mezclarse bien. splitmix64 resuelve las
    /// dos cosas en dos líneas.
    /// </remarks>
    public Rng(ulong seed)
    {
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);

        // Imposible en la práctica, pero un estado cero es absorbente: se
        // chequea porque el costo es una comparación y la consecuencia sería
        // una corrida entera con el mismo número.
        if (_s0 == 0 && _s1 == 0)
            _s1 = 0x9E3779B97F4A7C15UL;
    }

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Siguiente número crudo de 64 bits.</summary>
    public ulong Next()
    {
        ulong s1 = _s0;
        ulong s0 = _s1;
        ulong resultado = s0 + s1;

        _s0 = s0;
        s1 ^= s1 << 23;
        _s1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);

        return resultado;
    }

    /// <summary>
    /// Un entero en <c>[minInclusivo, maxExclusivo)</c>.
    /// </summary>
    /// <remarks>
    /// Usa el método de Lemire: multiplica y se queda con la parte alta, en vez
    /// del <c>% n</c> de manual. El módulo tiene sesgo cuando el rango no divide
    /// exacto a 2^64 —los primeros valores del rango salen un poco más seguido—
    /// y acá eso se acumula sobre cientos de miles de tiradas por corrida.
    /// Además el módulo es una división, que es de las operaciones más caras que
    /// hay, y esto se llama muchísimo.
    /// </remarks>
    public int Between(int minInclusivo, int maxExclusivo)
    {
        if (maxExclusivo <= minInclusivo)
            return minInclusivo;

        ulong ancho = (ulong)(maxExclusivo - minInclusivo);
        ulong producto = MultiplicarAlto(Next(), ancho);

        return minInclusivo + (int)producto;
    }

    /// <summary>Los 64 bits altos de multiplicar dos números de 64 bits.</summary>
    private static ulong MultiplicarAlto(ulong a, ulong b)
    {
        return System.Math.BigMul(a, b, out _);
    }

    /// <summary>
    /// <c>true</c> con probabilidad <paramref name="porcentaje"/> sobre 100.
    /// </summary>
    public bool Happens(int porcentaje)
    {
        if (porcentaje <= 0) return false;
        if (porcentaje >= 100) return true;
        return Between(0, 100) < porcentaje;
    }

    /// <summary>
    /// <c>true</c> con probabilidad <paramref name="porMil"/> sobre 1000, para
    /// cosas que pasan menos de una vez cada cien: el drop de un jefe, un crítico
    /// raro. Con <see cref="Ocurre"/> la resolución mínima es 1%, que para eso es
    /// demasiado gruesa.
    /// </summary>
    public bool OcurrePorMil(int porMil)
    {
        if (porMil <= 0) return false;
        if (porMil >= 1000) return true;
        return Between(0, 1000) < porMil;
    }

    /// <summary>
    /// Un elemento al azar de la lista, devuelto por índice.
    /// </summary>
    public int Index(int cantidad) => cantidad <= 0 ? -1 : Between(0, cantidad);
}
