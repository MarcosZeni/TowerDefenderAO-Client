namespace TowerOfArgentum.Simulacion;

/// <summary>Lo que se compró para una oleada, en el orden en que entra.</summary>
public sealed class BuiltWave
{
    public required int Number { get; init; }

    /// <summary>Los tipos, en orden de entrada. El jefe va último.</summary>
    public required List<NpcPurchase> Npcs { get; init; }

    /// <summary>Ticks entre un spawn y el siguiente, en promedio.</summary>
    /// <remarks>
    /// «En promedio» porque la formación reparte esos ticks de otra manera. Ver
    /// <see cref="Formation"/>.
    /// </remarks>
    public required int SpawnIntervalTicks { get; init; }

    /// <summary>
    /// Con qué forma entra: pareja, de a dos, o en ráfagas de cinco.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Conserva el promedio del intervalo</b>, así que la oleada dura lo
    /// mismo y esto no es una palanca de dificultad. Lo que cambia es el valor
    /// <b>relativo</b> del catálogo: en ráfaga splash, cadena y quemadura valen el
    /// doble; en uniforme toman valor Concentración y el targeting «más fuerte».
    /// </remarks>
    public required SpawnFormation Formation { get; init; }

    /// <summary>Oro que se lleva el jugador por limpiarla.</summary>
    public required int BonusOro { get; init; }
}

/// <summary>Un NPC comprado, con los perks y rasgos que le tocaron.</summary>
/// <param name="Impulsive">
/// Al bajar del umbral de vida acelera, una sola vez. Ver <see cref="EnemyTraits"/>.
/// </param>
/// <param name="Leader">
/// Le da un aura de velocidad a los que van cerca por ruta, y el aura muere con
/// él: es un objetivo prioritario sin una sola resistencia ni inmunidad.
/// </param>
public readonly record struct NpcPurchase(
    int TypeId, int HealthPerk, int SpeedPerk, bool IsBoss,
    bool Impulsive = false, bool Leader = false);

/// <summary>
/// La receta de una oleada: cuánto se puede gastar y en qué.
/// </summary>
/// <remarks>
/// La arma <see cref="Balance.RecetaDe"/> con las curvas de <c>curvas.json</c>.
/// </remarks>
public sealed class WaveRecipe
{
    public required int Number { get; init; }

    /// <summary>La palanca principal de dificultad.</summary>
    public required int Budget { get; init; }

    /// <summary>Tipos que pueden salir, con su peso entero.</summary>
    public required (int TypeId, int Weight)[] Allowed { get; init; }

    public int BossTypeId { get; init; } = -1;

    /// <summary>Porcentaje entero: cuántos NPCs salen con perk.</summary>
    public int NpcPerkChance { get; init; }

    public int SpawnIntervalTicks { get; init; } = 30;

    public int BonusOro { get; init; }
}

/// <summary>
/// Arma una oleada gastando su presupuesto.
/// </summary>
/// <remarks>
/// <para><b>Una oleada no es una lista de bichos escrita a mano.</b> Es un
/// presupuesto y una lista de permitidos, y el RNG sembrado con la seed del
/// servidor lo gasta comprando. De ahí salen tres cosas:</para>
///
/// <list type="bullet">
/// <item>La seed hace trabajo real: dos corridas tienen la misma dificultad y
/// distinta composición.</item>
/// <item>El ranking sigue comparable, porque el presupuesto es idéntico para
/// todos. <b>Rerollear la seed no sirve</b>: no hay una tirada floja que
/// buscar.</item>
/// <item>El balance se toca en un número, no reescribiendo veinte listas.</item>
/// </list>
/// </remarks>
public static class WaveBuilder
{
    // ⚠ Acá ya no hay números. Todos viven en `curvas.json` («perksNpc»,
    // «oleada», «jefe») y llegan por DatosBalance. Lo que queda son las reglas
    // de cómo se gasta el presupuesto, que es código y no configuración.
    //
    // Lo que esos números significan, y por qué son esos, está escrito en el
    // propio curvas.json: es el lugar donde los va a leer el que balancea.

    public static BuiltWave Armar(WaveRecipe receta, Rng rng, BalanceData datos)
    {
        var cfg = datos.Curves.PerksNpc;
        var npcs = new List<NpcPurchase>();
        int restante = receta.Budget;

        // Regla 3: el jefe se descuenta ANTES de comprar el resto, y aparece
        // último. Al revés, una oleada con jefe traería la misma cantidad de
        // bichos más el jefe, y sería un salto de dificultad y no un cambio de
        // forma.
        bool hayJefe = receta.BossTypeId >= 0;
        if (hayJefe)
            restante -= Cost(receta.BossTypeId, datos);

        int masBarato = int.MaxValue;
        foreach (var (tipoId, _) in receta.Allowed)
            masBarato = System.Math.Min(masBarato, Cost(tipoId, datos));

        // ⚠ Tope de líderes por oleada. El aura no apila, así que diez líderes no
        // dan +100%, pero sí garantizan que TODA la oleada tenga el aura puesta
        // todo el tiempo — y ahí deja de ser un objetivo prioritario para volverse
        // un +10% de velocidad global disfrazado.
        int lideres = 0;

        // Regla 1: se gasta hasta que no alcance para el más barato — ahora con
        // el tope de cantidad. Lo que sobre se convierte en perks más abajo.
        while (restante >= masBarato && npcs.Count < datos.Curves.Wave.MaxNpcs)
        {
            int tipoId = ElegirPorPeso(receta.Allowed, rng);
            int costo = Cost(tipoId, datos);

            if (costo > restante)
            {
                // Salió uno que no entra en lo que queda. Se descarta la compra
                // y se sigue: el bucle corta solo cuando ni el más barato entra.
                // Elegir "el más caro que entre" sería más eficiente y menos
                // variado — todas las oleadas terminarían igual.
                continue;
            }

            restante -= costo;

            // Regla 4: cada NPC tira contra chancePerkNpc, y el perk se paga del
            // mismo presupuesto. Si no alcanza, no lo recibe.
            int perkVida = 0;
            int perkVelocidad = 0;

            if (rng.Happens(receta.NpcPerkChance))
            {
                if (rng.Happens(cfg.HealthChanceInRoll) && restante >= cfg.HealthCost)
                {
                    perkVida = cfg.HealthStep;
                    restante -= cfg.HealthCost;
                }
                else if (restante >= cfg.SpeedCost)
                {
                    perkVelocidad = cfg.SpeedStep;
                    restante -= cfg.SpeedCost;
                }
            }

            // ⚠ LOS RASGOS SE TIRAN APARTE Y SE PAGAN IGUAL. Vida y velocidad
            // suben una estadística; impulsivo y líder cambian una SITUACIÓN, así
            // que no comparten la tirada de arriba —si la compartieran, un bicho
            // que gana el perk numérico nunca podría ser líder— pero sí el
            // presupuesto, que es lo que impide que agregarlos descontrole la
            // dificultad: una oleada con rasgos trae MENOS bichos.
            //
            // Chance baja y costo alto a propósito: son la sal de la oleada. Un
            // líder en cada oleada deja de ser un objetivo prioritario y se vuelve
            // otro número de velocidad.
            bool impulsivo = restante >= cfg.ImpulsiveCost && rng.Happens(cfg.ImpulsiveChance);

            if (impulsivo)
                restante -= cfg.ImpulsiveCost;

            bool lider = lideres < cfg.MaxLeadersPerWave
                         && restante >= cfg.LeaderCost
                         && rng.Happens(cfg.LeaderChance);

            if (lider)
            {
                restante -= cfg.LeaderCost;
                lideres++;
            }

            npcs.Add(new NpcPurchase(tipoId, perkVida, perkVelocidad, IsBoss: false, impulsivo, lider));
        }

        // El presupuesto que no entró en NPCs se convierte en perks sobre los ya
        // comprados. Es la misma regla de siempre —el perk se paga del
        // presupuesto— aplicada al sobrante: en las oleadas tardías eso es casi
        // todo el presupuesto, y de ahí sale la dificultad.
        restante = SpreadLeftoverOnPerks(npcs, restante, rng, cfg);

        if (hayJefe)
        {
            // El jefe se lleva parte de lo que quedó, como vida extra — CON TECHO
            // (propuesta V2): el jefe es fuerte por diseño, no por residuo del
            // presupuesto. Antes era sobrante/4 sin tope y en las áreas tardías
            // salían jefes con miles de por ciento de HP.
            var j = datos.Curves.Boss;
            int vidaExtraJefe = System.Math.Min(restante / j.ExtraHealthDivisor, j.MaxExtraHealth);
            npcs.Add(new NpcPurchase(receta.BossTypeId, vidaExtraJefe, 0, IsBoss: true));
        }

        // ⚠ LA FORMACIÓN SE SORTEA ÚLTIMO, después del jefe. Es un punto fijo de
        // la secuencia del RNG: moverlo antes correría todas las compras de la
        // oleada y la revalidación fallaría.
        //
        // Va acá y no en la receta porque no depende del presupuesto: es cómo se
        // reparten los ticks de entrada, no cuántos bichos entran.
        return new BuiltWave
        {
            Number = receta.Number,
            Npcs = npcs,
            SpawnIntervalTicks = receta.SpawnIntervalTicks,
            BonusOro = receta.BonusOro,
            Formation = SortearFormacion(datos.Curves.Wave.Formations, rng),
        };
    }

    /// <summary>
    /// Con qué forma entra la oleada.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No toca HP, ni oro, ni presupuesto.</b> Las tres formas conservan
    /// el promedio del intervalo, así que la oleada dura aproximadamente lo mismo:
    /// no es una palanca de dificultad encubierta. El cargador lo verifica al
    /// leer <c>curvas.json</c> y tira si algún promedio no da 100.</para>
    ///
    /// <para>Lo que sí cambia es el valor <b>relativo</b> del catálogo. Es la
    /// lección de Rogue Tower: que los enemigos lleguen juntos o separados altera
    /// qué torres sirven sin tocar una sola estadística — y esa es exactamente la
    /// clase de profundidad que hacía falta para que jugar mejor se note.</para>
    /// </remarks>
    private static SpawnFormation SortearFormacion(SpawnFormations cfg, Rng rng)
    {
        int total = cfg.UniformWeight + cfg.PairsWeight + cfg.BurstWeight;

        if (total <= 0)
            return SpawnFormation.Uniform;

        int tirada = rng.Between(0, total);

        if (tirada < cfg.UniformWeight)
            return SpawnFormation.Uniform;

        return tirada < cfg.UniformWeight + cfg.PairsWeight
            ? SpawnFormation.Pairs
            : SpawnFormation.Burst;
    }

    /// <summary>
    /// Gasta el presupuesto que sobró subiéndoles perks a los NPCs comprados.
    /// </summary>
    /// <remarks>
    /// <para>Reparte en pasadas sobre la lista, <b>en orden de compra</b>, en vez
    /// de volcárselo todo al primero. Así el buff queda parejo y la oleada no se
    /// convierte en "un monstruo y cincuenta ratas".</para>
    ///
    /// <para>Las pasadas alternan vida y velocidad, y la velocidad tiene techo:
    /// un enemigo lo bastante rápido cruza el mapa entre dos disparos y no hay
    /// defensa que lo toque. La vida no necesita techo — más vida siempre es
    /// solo más tiempo bajo fuego.</para>
    /// </remarks>
    private static int SpreadLeftoverOnPerks(
        List<NpcPurchase> npcs, int restante, Rng rng, NpcPerkData cfg)
    {
        if (npcs.Count == 0)
            return restante;

        // El jefe todavía no está en la lista, así que esto solo toca a la tropa.
        while (restante >= cfg.HealthCost)
        {
            bool gastoAlgo = false;

            for (int i = 0; i < npcs.Count && restante >= cfg.HealthCost; i++)
            {
                var npc = npcs[i];

                // La velocidad sale a veces y con techo; la vida es el destino
                // por defecto del presupuesto.
                bool aVelocidad =
                    npc.SpeedPerk < cfg.MaxSpeed &&
                    restante >= cfg.SpeedCost &&
                    rng.Happens(cfg.SpeedChanceInLeftover);

                // ⚠ La vida también tiene techo ahora (propuesta V2): un NPC
                // topeado en las dos cosas se saltea, y si TODOS están topeados
                // gastoAlgo queda en falso y el sobrante va al jefe.
                if (!aVelocidad && npc.HealthPerk >= cfg.MaxHealth)
                    continue;

                npcs[i] = aVelocidad
                    ? npc with { SpeedPerk = npc.SpeedPerk + cfg.SpeedStep }
                    : npc with { HealthPerk = npc.HealthPerk + cfg.HealthStep };

                restante -= aVelocidad ? cfg.SpeedCost : cfg.HealthCost;
                gastoAlgo = true;
            }

            if (!gastoAlgo)
                break;
        }

        return restante;
    }

    /// <summary>
    /// Elige un tipo con pesos <b>enteros</b>.
    /// </summary>
    /// <remarks>
    /// Enteros y no probabilidades en coma flotante, por la misma razón que todo
    /// lo demás: un peso de <c>0.3</c> no se representa exacto en binario, y dos
    /// máquinas podrían caer de lados distintos del corte.
    /// </remarks>
    private static int ElegirPorPeso((int TypeId, int Weight)[] permitidos, Rng rng)
    {
        int total = 0;
        foreach (var (_, peso) in permitidos)
            total += peso;

        if (total <= 0)
            return permitidos[0].TypeId;

        int tirada = rng.Between(0, total);

        foreach (var (tipoId, peso) in permitidos)
        {
            tirada -= peso;
            if (tirada < 0)
                return tipoId;
        }

        return permitidos[^1].TypeId;
    }

    /// <summary>
    /// Cuánto cuesta un NPC, en puntos de presupuesto.
    /// </summary>
    /// <remarks>
    /// <para>Derivado de los stats en vez de escrito a mano por criatura: si la
    /// fórmula es buena, <b>agregar un bicho nuevo no obliga a rebalancear
    /// nada</b>. Con un número a mano por criatura, cada alta del bestiario es
    /// una decisión de balance más.</para>
    ///
    /// <para>El HP pesa más que el daño porque en un tower defense aguantar es
    /// lo que cuesta matar; el daño solo importa si llega a la Torre.</para>
    /// </remarks>
    internal static int Cost(int tipoId, BalanceData datos)
    {
        var e = datos.Get(tipoId);

        if (e is null)
            return int.MaxValue;   // no existe: que no lo compre nadie

        // La velocidad entra porque un bicho rápido pasa menos tiempo bajo fuego,
        // y eso lo hace más difícil sin tocarle una estadística de combate.
        var c = datos.Curves.Wave;

        return (e.Hp / c.HpDivisor) + (e.MaxDamage / c.MaxDamageDivisor) + (e.Speed / c.SpeedDivisor);
    }
}

/// <summary>
/// Una fila del avance de la próxima oleada, para la pantalla de preparación.
/// </summary>
/// <remarks>
/// Agrupa por tipo: al jugador le sirve «12 hormigas, 3 con perk», no doce
/// renglones iguales. <b>No es entrada de la simulación</b> — se deriva de la
/// oleada ya armada y no vuelve al tick.
/// </remarks>
public sealed class WavePreview
{
    public required int TypeId { get; init; }

    public required string Name { get; init; }

    /// <summary>La clave del sprite, para dibujarlo al lado del nombre.</summary>
    public required string Graphic { get; init; }

    public required bool IsBoss { get; init; }

    public int Cuantos { get; set; }

    /// <summary>Cuántos de esos vienen buffeados. Tiene que verse.</summary>
    public int ConPerk { get; set; }

    /// <summary>
    /// Cuántos traen un <b>rasgo</b>: impulsivo o líder.
    /// </summary>
    /// <remarks>
    /// ⚠ Va aparte de <see cref="ConPerk"/> y no sumado, porque no es lo mismo:
    /// vida y velocidad suben un número que el jugador ya sabe leer, y los rasgos
    /// cambian una situación. Un líder que se ve igual que un lobo normal es una
    /// mecánica invisible — y la regla del proyecto es que <b>los modificadores
    /// tienen que verse en pantalla</b>, porque un jugador que pierde contra algo
    /// que no sabía que estaba ahí no aprende nada.
    /// </remarks>
    public int ConRasgo { get; set; }
}
