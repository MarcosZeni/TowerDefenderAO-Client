namespace TowerOfArgentum.Simulation;

/// <summary>
/// De dónde salió un golpe.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Es la regla número uno del sistema de estados.</b> Solo el daño
/// <see cref="Direct"/> puede disparar un comportamiento de perk, aplicar un
/// estado o contar para un contador. Todo lo demás —eco, splash, cadena, rebote,
/// DoT, transferencia de overkill— es <see cref="Secondary"/> y solo resta vida.
/// </para>
///
/// <para>Sin esta separación, Cadena crea Cadena, el eco dispara otro eco y una
/// oleada apretada multiplica el daño por diez sin que ningún número del catálogo
/// lo anticipe. Es lo que hace <b>acotable</b> a todo el sistema: el techo de un
/// perk es su magnitud por su cantidad de secundarios, y nada más.</para>
///
/// <para>Va como parámetro explícito y no como una bandera del enemigo o del
/// defensor a propósito: así el compilador obliga a decidirlo en cada lugar que
/// hace daño, y agregar un efecto nuevo no puede olvidarse de marcarlo.</para>
/// </remarks>
public enum DamageSource
{
    Direct = 0,
    Secondary = 1,
}

/// <summary>
/// Los tres daños por tiempo, cada uno con su propia cuenta.
/// </summary>
/// <remarks>
/// Son tres y no uno solo porque provienen de perks distintos y se apilan entre
/// sí: un asesino sangrando y un druida quemando al mismo bicho son dos efectos,
/// no uno que pisa al otro. Dos fuentes del <b>mismo</b> DoT sí se funden, y eso
/// lo resuelve <see cref="DotState.Add"/>.
/// </remarks>
public enum DotKind
{
    Bleed = 0,
    Poison = 1,
    Burn = 2,
}

/// <summary>
/// Un daño por tiempo, guardado como <b>presupuesto</b> y no como porcentaje.
/// </summary>
/// <remarks>
/// <para>La idea sale del algoritmo de apilado de estados de Defender's Quest:
/// lo que se guarda es <b>cuánto daño falta</b> y <b>a qué ritmo sale</b>. Al
/// mezclar dos aplicaciones del mismo DoT se conserva <b>Potencia × Duración</b>:
/// el daño pendiente se suma y el ritmo se queda con el más alto de los dos.</para>
///
/// <para>Es lo que permite apilar inversión sin que el DPS suba solo. Dos golpes
/// de 30 a 10 por segundo dan 10 por segundo <b>durante más tiempo</b>, no 20 por
/// segundo: el jugador cobra todo lo que puso, pero no antes.</para>
///
/// <para>⚠ Todo acá está en <b>centésimas de punto</b>, la escala interna del
/// daño.</para>
/// </remarks>
public struct DotState
{
    /// <summary>Daño que todavía falta cobrar, en centésimas de punto.</summary>
    public int Pending;

    /// <summary>Cuánto sale por tick, en centésimas de punto.</summary>
    public int PerTick;

    /// <summary>Tick en el que se apaga aunque quede pendiente.</summary>
    public int ExpiryTick;

    public readonly bool Active => Pending > 0 && PerTick > 0;

    /// <summary>
    /// Suma una aplicación nueva, conservando Potencia × Duración.
    /// </summary>
    /// <param name="tick">El tick actual.</param>
    /// <param name="danoTotal">Lo que aporta esta aplicación, en centésimas.</param>
    /// <param name="ticks">En cuántos ticks lo repartiría por sí sola.</param>
    /// <param name="ticksMaximos">
    /// Techo de duración. Si el pendiente no entra, <b>se sube el ritmo</b> en vez
    /// de tirar daño: el jugador ya lo pagó con sus golpes.
    /// </param>
    public void Add(int tick, int danoTotal, int ticks, int ticksMaximos)
    {
        if (danoTotal <= 0 || ticks <= 0)
            return;

        // Lo que quedaba sin cobrar se conserva. Es la diferencia entre "refresca"
        // y "pisa": con pisar, golpear a un bicho ya envenenado TIRA el veneno
        // anterior, y apilar el mismo perk sería contraproducente.
        Pending += danoTotal;

        int ritmoNuevo = (danoTotal + ticks - 1) / ticks;

        if (ritmoNuevo > PerTick)
            PerTick = ritmoNuevo;

        if (PerTick <= 0)
            PerTick = 1;

        int faltan = (Pending + PerTick - 1) / PerTick;

        if (faltan > ticksMaximos)
        {
            faltan = ticksMaximos;
            PerTick = (Pending + faltan - 1) / faltan;
        }

        ExpiryTick = tick + faltan;
    }

    /// <summary>
    /// Cobra un tick. Devuelve cuánto daño sale, en centésimas de punto.
    /// </summary>
    public int Tick(int tick)
    {
        if (!Active)
            return 0;

        if (tick >= ExpiryTick)
        {
            // Se apaga y se pierde lo que quedaba. El techo de duración es lo que
            // impide que un DoT muy cargado siga cobrando media corrida después.
            Pending = 0;
            PerTick = 0;
            return 0;
        }

        int sale = PerTick < Pending ? PerTick : Pending;
        Pending -= sale;

        if (Pending <= 0)
        {
            Pending = 0;
            PerTick = 0;
        }

        return sale;
    }
}

/// <summary>
/// Un eco: daño que sale unos ticks DESPUÉS del golpe que lo creó.
/// </summary>
/// <remarks>
/// <para>Es el único efecto diferido del juego que no es un DoT, y por eso tiene
/// su propia cola en vez de un campo en el enemigo: el objetivo puede estar muerto
/// cuando llega el momento, y ahí el eco busca un vecino de ruta.</para>
///
/// <para>⚠ Guarda el <b>id de spawn</b> y no una referencia al enemigo. Una
/// referencia mantendría vivo un objeto que la fase 8 ya sacó de la lista, y el
/// eco le pegaría a un muerto sin que nadie se entere.</para>
///
/// <para>Y guarda además la <b>distancia recorrida</b>: cuando el objetivo
/// original ya murió, esa es la única pista que queda de dónde estaba, y sin ella
/// no habría forma de buscarle un vecino.</para>
/// </remarks>
public readonly record struct PendingEcho(
    int Tick,
    int SpawnId,
    long Distance,
    int Damage,
    int RadiusThousandths);
