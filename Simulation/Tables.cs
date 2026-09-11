using System.Text.Json;

namespace TowerOfArgentum.Simulation;

/// <summary>
/// Un archivo de datos que no se pudo leer o que dice algo imposible.
/// </summary>
/// <remarks>
/// Excepción y no un valor por defecto silencioso: un número de balance que se
/// cae a un default es una corrida que mide otra cosa que las demás, y el
/// jugador no se entera hasta que el ranking no le cierra. <b>Mejor no arrancar
/// que arrancar midiendo mal.</b>
/// </remarks>
public sealed class InvalidBalanceDataException(string mensaje) : System.Exception(mensaje);

// =============================================================================
//  Las formas de las curvas
// =============================================================================

/// <summary>Una recta <c>base + porOleada × n</c>.</summary>
public readonly record struct LinearCurve(int Base, int PerWave)
{
    public int At(int oleada) => Base + (oleada * PerWave);
}

/// <summary>
/// Una chance que sube con el presupuesto y está acotada:
/// <c>limitar(presupuesto / divisor, min, max)</c>.
/// </summary>
public readonly record struct ChanceRange(int Divisor, int Min, int Max)
{
    public int At(int presupuesto)
        => Ints.Clamp(Divisor == 0 ? Min : presupuesto / Divisor, Min, Max);
}

/// <summary>
/// La magnitud de una habilidad: <c>limitar(base + presupuesto / divisor, min, max)</c>.
/// </summary>
/// <remarks>
/// <c>divisor = 0</c> quiere decir valor fijo, no escala. Es el caso de Doble,
/// que dispara dos veces y no hay medio disparo.
/// </remarks>
public readonly record struct MagnitudeFormula(int Base, int Divisor, int Min, int Max)
{
    public int At(int presupuesto)
        => Ints.Clamp(Divisor == 0 ? Base : Base + (presupuesto / Divisor), Min, Max);
}

// =============================================================================
//  curvas.json
// =============================================================================

public sealed record TowerData(int HpInicial, int Damage, int CadenceTicks);

public sealed record RunData(
    int WavesPerRun,
    int AreaCount,
    int[] LastWavePerArea,
    int WavesBetweenChoices,
    int PreparationSeconds,
    int FreeClasses,
    int InitialClassOffers,
    int SealedUnlockWave,
    int ChoiceDiscountPercent);

public sealed record DefenderData(
    int BackpackSlots,
    int GearSlotsPerClass,
    int DefenderSellPercent,
    int WeaponSellPercent);

public sealed record EconomyData(
    int StartingGoldTokens,
    int GoldPerCalledEnemy,
    int CallGoldCap,
    int LevelUpPriceBase,
    int[] GoldMultiplierPerArea)
{
    /// <summary>
    /// El porcentaje de oro de kill que paga esa área.
    /// </summary>
    /// <remarks>
    /// <para>Desacopla el escalado de los enemigos del escalado del dinero: el
    /// presupuesto de oleada es cuadrático, así que sin esto el oro crecía con él
    /// y en las áreas tardías el jugador terminaba con el tablero lleno y oro
    /// sobrando — un recurso que dejó de ser un recurso.</para>
    ///
    /// <para>Es el patrón de BTD6: bajar el ingreso mantiene legibles los precios
    /// de la tienda; subir los precios los vuelve una tabla que hay que releer.</para>
    /// </remarks>
    public int GoldPercentOfArea(int area)
        => area >= 1 && area <= GoldMultiplierPerArea.Length
            ? GoldMultiplierPerArea[area - 1]
            : 100;

    /// <summary>
    /// Lo que cuesta comprar el nivel <paramref name="nivelDestino"/> de una
    /// clase: <c>levelUpPriceBase × nivelDestino²</c>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es el sumidero de oro de la corrida.</b> Medido con
    /// <c>--balance 24</c>, el jugador imaginario terminaba con ~127.000 sin
    /// gastar, el tablero lleno y la mochila llena: el tope de lo que se puede
    /// <i>tener</i> llegaba mucho antes que el de lo que se puede <i>pagar</i>.
    /// Recortar el ingreso no lo arreglaba —con la mitad del oro seguían sobrando
    /// 60.000— porque el problema no era cuánto entra sino que no había en qué
    /// gastarlo.</para>
    ///
    /// <para>⚠ <b>Cuadrática y no lineal</b>, y esa es la parte que protege la
    /// elección de cada tres oleadas. Con precio plano, comprar el nivel sería
    /// barato desde el arranque y la pantalla de elección perdería la mitad de su
    /// peso: para qué gastar la elección en levelear si se compra. Con el
    /// cuadrado, el primer nivel comprado cuesta más de lo que hay en el bolsillo
    /// en el área 1 y el sexto cuesta como una oleada entera de ingreso tardío.
    /// Temprano el oro se va al tablero, tarde se va acá.</para>
    ///
    /// <para><b>La elección sigue siendo la única forma de CONTRATAR.</b> El oro
    /// compra profundidad, no ancho: engorda lo que ya tenés, no te trae un rol
    /// nuevo. Esa mitad de la decisión queda intacta.</para>
    ///
    /// <para>Uniforme para todas las clases a propósito. Atarlo al costo de la
    /// clase sería más justo en teoría —un nivel de cazador vale más que uno de
    /// ladrón— y en la práctica obliga al jugador a leer dos tablas para decidir
    /// una cosa.</para>
    /// </remarks>
    public int LevelUpPrice(int nivelDestino)
        => LevelUpPriceBase * nivelDestino * nivelDestino;
}

public sealed record BudgetData(
    int Base,
    int Quadratic,
    int EquivalentCapMin,
    int EquivalentCapMax)
{
    /// <summary>La cuadrática, antes del tope por pool.</summary>
    public int Uncapped(int oleada) => Base + (Quadratic * oleada * oleada);
}

public sealed record PerWaveCurves(
    LinearCurve ClearGoldBonus,
    LinearCurve BossDropBudget,
    LinearCurve ShopItemBudget,
    int IntervalBase,
    int IntervalDivisor,
    int IntervalMin,
    int IntervalMax,
    int PerkChanceDivisor,
    int PerkChanceMin,
    int PerkChanceMax)
{
    public int SpawnInterval(int oleada) => Ints.Clamp(
        IntervalBase - (IntervalDivisor == 0 ? 0 : oleada / IntervalDivisor),
        IntervalMin, IntervalMax);

    public int NpcPerkChance(int oleada) => Ints.Clamp(
        PerkChanceDivisor == 0 ? 0 : oleada / PerkChanceDivisor,
        PerkChanceMin, PerkChanceMax);
}

/// <summary>
/// La tienda rotativa. Ver <c>curvas.json</c> §shop.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Abre junto a la pantalla de elección</b>, cada
/// <see cref="IntervalWaves"/> oleadas, y no después de cada una. Que las dos
/// decisiones caigan juntas es lo que las pone a competir: la elección da poder
/// gratis, la tienda lo cobra.</para>
///
/// <para>⚠ <b>Se sortea primero la categoría y después el objeto.</b> En una sola
/// bolsa uniforme los Oro serían comunísimos —hay 22 nombres distintos contra 9
/// clases— y agregar un perk nuevo cambiaría en silencio la probabilidad de todas
/// las demás categorías.</para>
/// </remarks>
public sealed record ShopData(
    int Slots,
    int IntervalWaves,
    int ClassWeight,
    int BronzeWeight,
    int SilverWeight,
    int GoldWeight,
    int BlueprintWeight,
    int WeaponWeight,
    int MaxLevelOnSale,
    int MaxGoldOffers,
    int MaxBlueprintOffers,
    int MinUsableOffers,
    int SilverMinWave,
    int GoldMinWave,
    int BlueprintMinWave,
    int RecipeWeightMultiplier,
    LinearCurve BronzePerkPrice,
    LinearCurve SilverPerkPrice,
    LinearCurve GoldPerkPrice,
    LinearCurve BlueprintPrice,
    LinearCurve ClassPrice,
    int ClassCostMultiplier)
{
    /// <summary>
    /// El precio de un perk de ese tier en esa oleada.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Uniforme dentro del tier, no por perk.</b> Si Concentración se compra
    /// el 90% de las veces y Preparación el 5% al mismo precio, eso dice que los
    /// perks necesitan rebalance; con precios distintos ese síntoma queda tapado.
    /// Los Únicos no se venden nunca, así que caen al precio de Oro solo por
    /// completitud del <c>switch</c>.
    /// </remarks>
    public int PerkPrice(PerkTier tier, int oleada) => tier switch
    {
        PerkTier.Bronze => BronzePerkPrice.At(oleada),
        PerkTier.Silver => SilverPerkPrice.At(oleada),
        _ => GoldPerkPrice.At(oleada),
    };

    /// <summary>
    /// Lo que sale <b>reclutar</b> una clase: no un defensor puesto.
    /// </summary>
    /// <remarks>
    /// Después de reclutarla, cada ficha de esa clase se sigue pagando a su costo
    /// base. El término × costo base existe para que un mago no salga lo mismo que
    /// un ladrón, y pesa poco a propósito: con un multiplicador grande las clases
    /// caras quedarían fuera del mercado y la tienda dejaría de ofrecerlas de
    /// hecho.
    /// </remarks>
    public int ClassRecruitPrice(int oleada, int costoBase)
        => ClassPrice.At(oleada) + (ClassCostMultiplier * costoBase);

    /// <summary>¿Esta oleada abre tienda?</summary>
    public bool OpensAt(int oleada)
        => IntervalWaves > 0 && oleada > 0 && oleada % IntervalWaves == 0;
}

/// <summary>Cómo caen los perks. Ver <c>curvas.json</c> §drops.</summary>
/// <remarks>
/// ⚠ <b>El jefe ofrece varios y el jugador elige uno</b> (Drops_v2). Con un solo
/// Oro al azar por jefe y ocho jefes por corrida, ver los <b>dos</b> componentes
/// de una receta concreta era cuestión de suerte y los Únicos eran una lotería.
/// Ofrecer varios y dejar elegir da agencia <b>sin regalar poder</b>.
///
/// ⚠ <b>Desde <see cref="BossGoldLateArea"/> el jugador se lleva DOS</b>, y ahí
/// sí sube el poder total de la corrida. Es a propósito: en el área 5 la mochila
/// ya está llena de Oro que no se puede equipar —la ranura de comportamiento es
/// una— y el jefe entregaba una carta más para una pila que no entraba. Dos
/// permiten cerrar una receta en un solo jefe.
/// </remarks>
public sealed record DropData(
    int CommonChance,
    int BronzeChance,
    int BossGoldOptions,
    int BossGoldLateArea,
    int BossGoldOptionsLate,
    int BossGoldPicksLate,
    int SynergyWeight,
    int GoldPoolSize,
    int GoldPoolPerNewClass,
    bool FinalBossDrops)
{
    /// <summary>Cuántos Oro se le ofrecen al jefe de este área.</summary>
    public int OptionsForArea(int area)
        => area >= BossGoldLateArea ? BossGoldOptionsLate : BossGoldOptions;

    /// <summary>Cuántos de esos se puede quedar el jugador.</summary>
    public int PicksForArea(int area)
        => area >= BossGoldLateArea ? BossGoldPicksLate : 1;
}

/// <summary>
/// Cuántos perks de un tier hacen uno del tier de arriba. Ver
/// <c>curvas.json</c> §upgrades.
/// </summary>
/// <remarks>
/// <para>⚠ <b><see cref="MaxCraftedGoldPerRun"/> es lo que sostiene la economía
/// entera del Oro.</b> El resto del juego está construido sobre «el jefe es la
/// fuente de Oro, ocho por corrida, y la tienda como mucho uno por visita»: los
/// Únicos cuestan dos Oro justamente porque hay pocos. Sin tope, con
/// <c>4 × 3 = 12</c> Bronce por Oro y del orden de 100 Bronce cayendo en una corrida, la
/// mejora entregaría tanto Oro como los ocho jefes juntos y los Únicos dejarían de
/// costar nada.</para>
///
/// <para>Bronce → Plata <b>no</b> lleva tope: Bronce y Plata son números, no
/// comportamientos, y su abundancia no es lo que el balance protege.</para>
///
/// <para>Un contador en <c>0</c> apaga esa mitad de la mecánica sin tocar
/// código; el cargador exige 2 o más para que exista.</para>
/// </remarks>
public sealed record UpgradeData(
    int BronzeToSilver,
    int SilverToGold,
    int MaxCraftedGoldPerRun)
{
    /// <summary>Cuántos hacen falta para subir desde ese tier. 0 = no se puede.</summary>
    public int CostOf(PerkTier tier) => tier switch
    {
        PerkTier.Bronze => BronzeToSilver,
        PerkTier.Silver => SilverToGold,
        _ => 0,
    };
}

/// <summary>
/// Cómo entra la oleada: la separación temporal entre NPCs, conservando el
/// promedio.
/// </summary>
/// <remarks>
/// <para>⚠ <b>No toca HP, ni oro, ni presupuesto.</b> Solo cambia el ritmo, y por
/// eso los promedios de las tres formas <b>tienen que dar 100</b>: si no dan, la
/// formación se vuelve una oleada más corta o más larga, o sea dificultad
/// encubierta que no aparece en el presupuesto.</para>
///
/// <para>Lo que sí cambia es el valor <b>relativo</b> del catálogo: en ráfaga
/// splash, cadena y quemadura valen el doble; en uniforme toman valor
/// Concentración y el targeting «más fuerte». Es la lección de Rogue Tower —que
/// los enemigos lleguen juntos o separados altera qué torres sirven, sin tocar
/// una estadística.</para>
/// </remarks>
public sealed record SpawnFormations(
    int UniformWeight,
    int PairsWeight,
    int BurstWeight,
    int PairShortPercent,
    int PairLongPercent,
    int BurstSize,
    int BurstShortPercent,
    int BurstLongPercent)
{
    /// <summary>
    /// El porcentaje del intervalo base que le toca al spawn número
    /// <paramref name="indice"/> (0 el primero).
    /// </summary>
    public int PercentAt(SpawnFormation forma, int indice) => forma switch
    {
        SpawnFormation.Pairs => indice % 2 == 0 ? PairShortPercent : PairLongPercent,

        SpawnFormation.Burst => BurstSize <= 1
            ? 100
            : (indice % BurstSize == BurstSize - 1 ? BurstLongPercent : BurstShortPercent),

        _ => 100,
    };
}

/// <summary>Las tres formas en que puede entrar una oleada.</summary>
public enum SpawnFormation
{
    /// <summary>Parejo. El ritmo neutro.</summary>
    Uniform = 0,

    /// <summary>De a dos pegados y después un hueco.</summary>
    Pairs = 1,

    /// <summary>Cinco muy juntos y después un hueco grande.</summary>
    Burst = 2,
}

public sealed record WaveData(
    int MaxNpcs,
    int CurrentAreaWeight,
    int PreviousAreaWeight,
    int PreviousAreaWeightFirst,
    int PreviousAreaFadeWaves,
    int HpDivisor,
    int MaxDamageDivisor,
    int SpeedDivisor,
    int DefaultSpawnInterval,
    SpawnFormations Formations);

/// <summary>
/// Lo que el presupuesto de la oleada le puede comprar a un NPC.
/// </summary>
/// <remarks>
/// Vida y velocidad suben una estadística. <b>Impulsivo y Líder cambian una
/// situación</b>, y por eso se tiran aparte, una sola vez por NPC, con chance
/// baja y costo alto: son la sal de la oleada. Un líder en cada oleada deja de
/// ser un objetivo prioritario y se vuelve otro número de velocidad.
/// </remarks>
public sealed record NpcPerkData(
    int HealthCost,
    int SpeedCost,
    int HealthStep,
    int SpeedStep,
    int MaxHealth,
    int MaxSpeed,
    int HealthChanceInRoll,
    int SpeedChanceInLeftover,
    int ImpulsiveCost,
    int ImpulsiveChance,
    int LeaderCost,
    int LeaderChance,
    int MaxLeadersPerWave);

public sealed record BossData(int ExtraHealthDivisor, int MaxExtraHealth, string FinalBossName);

/// <summary>Todo lo de <c>curvas.json</c>.</summary>
public sealed record CurveData(
    TowerData Tower,
    RunData RunCurve,
    DefenderData Defenders,
    EconomyData Economy,
    BudgetData Budget,
    PerWaveCurves PerWave,
    ShopData Shop,
    DropData Drops,
    WaveData Wave,
    NpcPerkData PerksNpc,
    BossData Boss,
    UpgradeData Upgrades);

// =============================================================================
//  combate.json
// =============================================================================

/// <summary>El daño del enemigo, derivado de su rango del bestiario.</summary>
public sealed record EnemyCombat(int DamageAverageDivisor, int MinimumDamage);

/// <summary>
/// Cuánto control aguanta un jefe.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Es la regla que hace que los jefes sigan siendo jefes.</b> Sin ella
/// un druida con Raíces y un mago con Escarcha dejan al jefe de área clavado y el
/// combate deja de existir. Con ella el control <b>sigue sirviendo</b> —vale la
/// mitad, no cero— y la pelea se gana pegando.</para>
///
/// <para>Dos números cubren todos los estados: la potencia se recorta parejo y el
/// control duro se recorta por duración. Escribir un valor de jefe por perk sería
/// veinte números que se desincronizan de a uno.</para>
/// </remarks>
public sealed record BossControl(
    int PowerPercent,
    int HardCcDurationPercent,
    int PushPercent,
    int HardCcExtraImmunityTicks,
    int MaxPushPerBoss,
    int FinalBossPowerPercent,
    bool FinalBossImmuneToHardCc,
    bool FinalBossImmuneToPush,
    bool LootIgnoresBosses);

/// <summary>Cuánto dura y hasta dónde llega cada estado.</summary>
/// <remarks>
/// ⚠ Ninguno apila: gana la potencia más fuerte y cada aplicación refresca la
/// duración. Los DoT son la excepción, y con su propia regla —conservan
/// Potencia × Duración—. Ver <see cref="DotState"/>.
/// </remarks>
public sealed record StateRules(
    int SlowCapPercent,
    int SlowDurationTicks,
    int SlowMaxDurationTicks,
    int VulnerableTicks,
    int WeakenTicks,
    int HuntMarkTicks,
    int RootTicks,
    int RootImmunityTicks,
    int RootMaxPerEnemy,
    int PushMaxPerEnemy,
    int InspireTicks,
    int InspireRadiusCells,
    int LootTicks,
    int LootMaxGoldPerEnemy,
    int LootMaxGoldPerWave,
    int DotTicks,
    int DotMaxTicks);

/// <summary>
/// Los disparadores y los alcances de cada comportamiento de Oro.
/// </summary>
/// <remarks>
/// La <b>magnitud</b> no está acá: vive en <c>perks.json</c>, al lado del perk,
/// que es donde la busca el que balancea. Acá quedan los números que el jugador
/// no ve en la ficha —cada cuántos golpes dispara, a qué distancia alcanza,
/// cuántos secundarios toca— y que son justamente los más difíciles de balancear.
/// </remarks>
public sealed record BehaviorRules(
    int RicochetBounces,
    int RicochetWindowThousandths,
    int ChainMaxJumps,
    int ChainWindowThousandths,
    int SplashMaxSecondaries,
    int SplashRadiusThousandths,
    int SplashTotalCapPercent,
    int BurnMaxSecondaries,
    int BurnRadiusThousandths,
    int HitsPerCritical,
    int FocusMaxStacks,
    int FocusResetTicks,
    int SunderEveryHits,
    int RootEveryHits,
    int SeismicEveryHits,
    int InspireEveryHits,
    int EchoEveryHits,
    int EchoDelayTicks,
    int EchoSearchThousandths,
    int OverpenCapPercent,
    int OverpenRadiusThousandths,
    int OpportunistThresholdPercent,
    int FirstStrikeThresholdPercent,
    int ChargeIdleTicks,

    // Los cinco que antes solo existían como habilidad de arma. Los topes son
    // PROPIOS y no se comparten con los de WeaponRules: rebalancear el arma no
    // tiene por qué mover el perk, y en el caso de Drenaje compartir el
    // acumulador de oleada haría que un arma le comiera el techo al perk.
    int RangePortionCap,
    int RangePortionScale,
    int BloodthirstBonusCap,
    int SiphonCooldownTicks,
    int SiphonMaxHealingPerWave);

/// <summary>Los topes de los Únicos que no entran en sus dos magnitudes.</summary>
public sealed record UniqueRules(
    int FrostWaveMaxSecondaries,
    int FrostWaveSlowTicks,
    int PerforationRadiusThousandths,
    int InvisibilityMaxGoldPerEnemy,
    int InvisibilityMaxGoldPerWave,
    int TerribleHungerRootTicks,
    int StrengthMaxPushPerEnemy);

/// <summary>Los topes de las habilidades de arma que no son la magnitud.</summary>
/// <remarks>
/// ⚠ Los cuatro últimos son los techos que faltaban. <b>Doble</b> pegaba un golpe
/// entero —o sea +100% de daño directo— y a ese precio ninguna otra habilidad
/// competía por la ranura. <b>Cadena</b> escalaba lineal con la densidad, así que
/// su poder lo decidía la formación de la oleada y no el jugador. Y las dos
/// económicas —<b>Codicia</b> y <b>Vampirismo</b>— no tenían techo, que en un
/// sistema con realimentación positiva no las hace fuertes: las hace obligatorias.
/// </remarks>
public sealed record WeaponRules(
    int MaxSplashSecondaries,
    int BounceBounces,
    int MaxChainSecondaries,
    int ThirstBonusCap,
    int RangePortionCap,
    int RangePortionScale,
    int ExecuteThresholdTroop,
    int ExecuteThresholdBoss,
    bool FinalBossImmuneToExecute,
    int VampirismCooldownTicks,
    int GreedMarkDurationTicks,
    int DoubleShotPercent,
    int ChainSecondaryCapPercent,
    int GreedMaxExtraPerEnemy,
    int GreedMaxExtraPerWave,
    int VampirismMaxHealingPerWave);

/// <summary>
/// Los tres rasgos que cambian una situación en vez de una estadística.
/// </summary>
/// <remarks>
/// <para>Ninguno agrega resistencias, inmunidades, curación ni destrucción de
/// defensores, y <b>ninguno da daño extra</b>: un enemigo que además pega más
/// fuerte al enfurecer convierte una filtración en una derrota instantánea que el
/// jugador no tenía cómo prever. Solo hacen más urgente el último tramo.</para>
///
/// <para>El aura del Líder <b>no apila</b>, igual que todos los estados del
/// juego: gana la potencia más fuerte. Sin la regla, tres líderes juntos son un
/// +30% que ningún número del catálogo anticipa.</para>
/// </remarks>
public sealed record EnemyTraits(
    int EnrageHpPercent,
    int EnrageSpeedPercent,
    int EnrageTicks,
    int FinalBossEnrageSpeedPercent,
    int ImpulsiveHpPercent,
    int ImpulsiveSpeedPercent,
    int LeaderSpeedPercent,
    int LeaderRadiusThousandths);

/// <summary>
/// Los números del tick, los que antes eran literales dentro de
/// <see cref="Run"/>.
/// </summary>
/// <remarks>
/// Va partido en seis grupos y no como una lista plana de sesenta parámetros:
/// una lista posicional de ese largo es una invitación a cruzar dos números al
/// agregar el sesenta y uno, y el síntoma sería un radio de splash usado como
/// duración de veneno.
/// </remarks>
public sealed record CombatData(
    EnemyCombat Enemy,
    BossControl Boss,
    StateRules States,
    BehaviorRules Behaviors,
    UniqueRules Uniques,
    WeaponRules Weapons,
    EnemyTraits Traits);

// =============================================================================
//  clases.json
// =============================================================================

/// <summary>Los stats base de una clase, a nivel 1 y sin nada equipado.</summary>
public sealed record ClassData(
    DefenderClass DefenderClass,
    int Cost,
    int Damage,
    int RangeCells,
    int CadenceTicks,
    bool Support);

/// <summary>El roster completo: las nueve clases y la curva de nivel.</summary>
public sealed class ClassTable
{
    private readonly ClassData[] _todas;
    private readonly ClassData?[] _porClase = new ClassData?[9];

    public ClassTable(ClassData[] todas, int extraDanoPorNivel, int slotsPerkPorClase)
    {
        _todas = todas;
        ExtraDamagePerLevel = extraDanoPorNivel;
        PerkSlotsPerClass = slotsPerkPorClase;

        foreach (var c in todas)
        {
            int i = (int)c.DefenderClass;

            if (i >= 0 && i < _porClase.Length)
                _porClase[i] = c;
        }
    }

    public IReadOnlyList<ClassData> All => _todas;

    /// <summary>Puntos porcentuales de daño por cada nivel arriba del 1.</summary>
    public int ExtraDamagePerLevel { get; }

    /// <summary>Slots de perk por clase, que es también el nivel máximo.</summary>
    public int PerkSlotsPerClass { get; }

    /// <summary>
    /// Los stats de una clase.
    /// </summary>
    /// <remarks>
    /// Tira si la clase no está en el archivo, en vez de devolver un default: una
    /// clase sin stats es una clase que pega lo que se le ocurra al fallback, y
    /// eso no se nota hasta que el ranking no cierra.
    /// </remarks>
    public ClassData De(DefenderClass clase)
        => _porClase[(int)clase]
           ?? throw new InvalidBalanceDataException($"clases.json no define la clase «{clase}»");

    /// <summary>Las que pegan lo suficiente para sostener solas una defensa.</summary>
    /// <remarks>
    /// ⚠ Al menos una de las tres opciones del arranque sale de acá. No es una
    /// concesión: con el reparto libre, un arranque de clérigo + bardo + druida
    /// es una corrida perdida en la oleada 1 sin que el jugador haya hecho nada
    /// mal. Perder por una decisión propia es el juego; perder por el sorteo de
    /// la pantalla inicial, antes de tocar nada, es un bug de diseño.
    /// </remarks>
    public DefenderClass[] Offensive()
    {
        var lista = new List<DefenderClass>();

        foreach (var c in _todas)
        {
            if (!c.Support)
                lista.Add(c.DefenderClass);
        }

        return [.. lista];
    }
}

// =============================================================================
//  El reparto de las 100 oleadas entre las 8 áreas
// =============================================================================

/// <summary>
/// El reparto de las oleadas entre las áreas.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Las oleadas se numeran 1 a 100 corridas, no por área.</b> El área
/// es una propiedad de la oleada, no un contador que se reinicia. Numerarlas por
/// área haría imposible leer el ranking: "oleada 7" no diría nada sin saber de
/// qué área.</para>
///
/// <para>Antes era una clase estática con el array escrito en código. Ahora sale
/// de <c>curvas.json</c>, así que es una instancia y cuelga de
/// <see cref="BalanceData"/> como todo lo demás que es entrada de la
/// simulación.</para>
/// </remarks>
public sealed class AreaTable(int[] ultimaOleada)
{
    private readonly int[] _ultimaOleada = ultimaOleada;

    public int Count => _ultimaOleada.Length;

    public int OfWave(int oleada)
    {
        for (int i = 0; i < _ultimaOleada.Length; i++)
        {
            if (oleada <= _ultimaOleada[i])
                return i + 1;
        }

        return _ultimaOleada.Length;
    }

    /// <summary>
    /// ¿Esta oleada cierra un área? Entonces trae jefe.
    /// </summary>
    /// <remarks>
    /// <b>El jefe es la única fuente de perks de Oro</b>, así que hay exactamente
    /// ocho oportunidades por corrida de conseguir uno. Eso es lo que hace que
    /// matar al jefe valga más que sobrevivirlo, y lo que le pone techo a cuántos
    /// Oro puede juntar un jugador — que a su vez es lo que hace que la fusión
    /// sea una decisión y no un trámite.
    /// </remarks>
    public bool HasBoss(int oleada) => System.Array.IndexOf(_ultimaOleada, oleada) >= 0;

    public int LastWaveOf(int area) => _ultimaOleada[area - 1];

    public int FirstWaveOf(int area) => area <= 1 ? 1 : _ultimaOleada[area - 2] + 1;
}

// =============================================================================
//  perks.json  y  planos.json
// =============================================================================

/// <summary>
/// El catálogo de perks, ya cargado.
/// </summary>
/// <remarks>
/// <para><b>Los perks no se generan</b>, a diferencia del equipo: se elige uno de
/// esta lista filtrando por tier y por las clases que el jugador tiene. Un perk
/// generado al azar sería un número sin nombre, y el Oro y el Único son
/// comportamientos — no hay forma de generar un comportamiento.</para>
/// </remarks>
public sealed class PerkTable
{
    private readonly PerkDefinition[] _todos;
    private readonly Dictionary<int, PerkDefinition> _porId = [];

    public PerkTable(PerkDefinition[] todos)
    {
        _todos = todos;

        foreach (var p in todos)
            _porId[p.Id] = p;
    }

    public IReadOnlyList<PerkDefinition> All => _todos;

    /// <summary>
    /// El perk de ese id.
    /// </summary>
    /// <remarks>
    /// ⚠ Por id y no por posición. Los ids se escriben a mano en
    /// <c>perks.json</c> justamente para que insertar uno en el medio no corra a
    /// todos los de abajo: la mochila y el log guardan ids.
    /// </remarks>
    public PerkDefinition Get(int id)
        => _porId.TryGetValue(id, out var p)
            ? p
            : throw new InvalidBalanceDataException($"no existe el perk con id {id}");

    public PerkDefinition? ByKey(string key)
    {
        foreach (var p in _todos)
        {
            if (string.Equals(p.Key, key, System.StringComparison.Ordinal))
                return p;
        }

        return null;
    }

    /// <summary>
    /// Elige un perk al azar del tier pedido, entre los que le sirven a alguna de
    /// las clases que el jugador tiene.
    /// </summary>
    /// <remarks>
    /// Filtrar por las clases que tiene, y no por todo el catálogo, es lo que
    /// evita el drop muerto: un Único de druida cuando no hay druida en el roster
    /// es un lugar de mochila gastado en nada.
    /// </remarks>
    public PerkDefinition? Roll(PerkTier tier, IReadOnlyList<DefenderClass> clasesDelJugador, Rng rng)
    {
        var candidatos = new List<PerkDefinition>();

        foreach (var p in _todos)
        {
            // ⚠ Los Únicos NO caen y NO se compran: se fusionan. Que aparezca uno
            // en una tirada de drop es un bug de datos.
            if (p.Tier == PerkTier.Unique || p.Tier != tier)
                continue;

            foreach (var c in clasesDelJugador)
            {
                if (p.WorksFor(c))
                {
                    candidatos.Add(p);
                    break;
                }
            }
        }

        if (candidatos.Count == 0)
            return null;

        return candidatos[rng.Index(candidatos.Count)];
    }
}

/// <summary>Las recetas de fusión, ya resueltas a ids.</summary>
public sealed class BlueprintTable(Blueprint[] todos)
{
    public IReadOnlyList<Blueprint> All { get; } = todos;

    /// <summary>Busca la receta que hacen esos dos Oro. El orden no importa.</summary>
    public Blueprint? Find(int perkA, int perkB)
    {
        foreach (var p in All)
        {
            if ((p.GoldA == perkA && p.GoldB == perkB) ||
                (p.GoldA == perkB && p.GoldB == perkA))
            {
                return p;
            }
        }

        return null;
    }
}

// =============================================================================
//  generador-armas.json
// =============================================================================

public sealed record WeaponAbilityData(WeaponAbility Ability, MagnitudeFormula Magnitude);

/// <summary>Los parámetros del generador de armas.</summary>
/// <remarks>
/// ⚠ <b>No es un catálogo de ítems.</b> DATOS.md §2 prohíbe <c>armas.json</c> —
/// un catálogo de piezas existe en Austral porque su muñeco se compone por
/// capas, y acá un defensor es un sprite. Las armas se <b>generan</b>: esto son
/// los parámetros del generador, no una lista de cosas.
/// </remarks>
public sealed class WeaponTable
{
    private readonly Dictionary<WeaponAbility, WeaponAbilityData> _porHabilidad = [];

    public WeaponTable(
        int paso, int costoDano, int costoCadencia, int costoAlcance,
        ChanceRange chanceLegendaria, ChanceRange chanceEpica, ChanceRange chanceRara,
        int porcLegendaria, int porcEpica, int porcRara, int porcComun,
        int divisorValor, int multiplicadorPrecioTienda,
        WeaponAbility[] poolComun, WeaponAbility[] poolRaro, WeaponAbility[] poolRompen,
        WeaponAbilityData[] habilidades)
    {
        Step = paso;
        DamageCost = costoDano;
        CadenceCost = costoCadencia;
        RangeCost = costoAlcance;
        LegendaryChance = chanceLegendaria;
        EpicChance = chanceEpica;
        RareChance = chanceRara;
        LegendaryPercent = porcLegendaria;
        EpicPercent = porcEpica;
        RarePercent = porcRara;
        CommonPercent = porcComun;
        ValueDivisor = divisorValor;
        ShopPriceMultiplier = multiplicadorPrecioTienda;
        CommonPool = poolComun;
        RarePool = poolRaro;
        BreakerPool = poolRompen;
        Abilities = habilidades;

        foreach (var h in habilidades)
            _porHabilidad[h.Ability] = h;
    }

    public int Step { get; }
    public int DamageCost { get; }
    public int CadenceCost { get; }
    public int RangeCost { get; }

    public ChanceRange LegendaryChance { get; }
    public ChanceRange EpicChance { get; }
    public ChanceRange RareChance { get; }

    public int LegendaryPercent { get; }
    public int EpicPercent { get; }
    public int RarePercent { get; }
    public int CommonPercent { get; }

    public int ValueDivisor { get; }
    public int ShopPriceMultiplier { get; }

    public WeaponAbility[] CommonPool { get; }
    public WeaponAbility[] RarePool { get; }
    public WeaponAbility[] BreakerPool { get; }

    public IReadOnlyList<WeaponAbilityData> Abilities { get; }

    public WeaponAbilityData? Get(WeaponAbility h) => _porHabilidad.GetValueOrDefault(h);

    public int AbilityPercent(WeaponRarity r) => r switch
    {
        WeaponRarity.Legendary => LegendaryPercent,
        WeaponRarity.Epic => EpicPercent,
        WeaponRarity.Rare => RarePercent,
        _ => CommonPercent,
    };

    public WeaponAbility[] PoolOf(WeaponRarity r) => r switch
    {
        WeaponRarity.Legendary => BreakerPool,
        WeaponRarity.Epic => RarePool,
        _ => CommonPool,
    };

    public int MagnitudeOf(WeaponAbility h, int presupuesto)
        => Get(h)?.Magnitude.At(presupuesto) ?? 0;

}

// =============================================================================
//  El lector: JSON de texto -> las tablas de arriba
// =============================================================================

/// <summary>
/// Convierte el texto de los JSON de <c>Data\</c> en las tablas que usa la
/// simulación.
/// </summary>
/// <remarks>
/// <para>⚠ Vive en <c>Simulation\</c> y <b>no lee disco</b>: recibe strings. Leer
/// archivos es del cliente, que sabe de Godot. Así el servidor puede revalidar
/// con exactamente el mismo parser, que es la única forma de garantizar que las
/// dos mitades entienden lo mismo por un archivo.</para>
///
/// <para><b>Los JSON admiten comentarios</b> (<c>//</c> y <c>/* */</c>) y comas
/// colgando. Son archivos que edita una persona: un número de balance sin la
/// explicación de por qué es ese número es un número que nadie se anima a
/// tocar.</para>
/// </remarks>
public static class TableReader
{
    private static readonly JsonDocumentOptions _opciones = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonDocument ParseJson(string texto, string archivo)
    {
        try
        {
            return JsonDocument.Parse(texto, _opciones);
        }
        catch (JsonException e)
        {
            throw new InvalidBalanceDataException($"{archivo}: no es JSON válido — {e.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Ayudantes. Todos tiran con el nombre del archivo y el del campo: un
    //  «Object reference not set» no le dice a nadie qué número falta.
    // -------------------------------------------------------------------------

    private static JsonElement Obj(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.Object)
            throw new InvalidBalanceDataException($"{archivo}: falta el objeto «{prop}»");

        return e;
    }

    private static int Int(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.Number)
            throw new InvalidBalanceDataException($"{archivo}: falta el número «{prop}»");

        if (!e.TryGetInt32(out int v))
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «{prop}» tiene que ser un entero. " +
                "Dentro de la simulación no existen los decimales (JUGABILIDAD.md §4).");
        }

        return v;
    }

    private static int OptionalInt(JsonElement padre, string prop, int porDefecto)
        => padre.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number
           && e.TryGetInt32(out int v) ? v : porDefecto;

    private static bool Bool(JsonElement padre, string prop, bool porDefecto)
        => padre.TryGetProperty(prop, out var e)
            ? e.ValueKind == JsonValueKind.True
            : porDefecto;

    private static string Text(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.String)
            throw new InvalidBalanceDataException($"{archivo}: falta el texto «{prop}»");

        return e.GetString() ?? "";
    }

    private static string OptionalText(JsonElement padre, string prop)
        => padre.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? ""
            : "";

    private static JsonElement ArrayOf(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.Array)
            throw new InvalidBalanceDataException($"{archivo}: falta la lista «{prop}»");

        return e;
    }

    private static int[] IntArrayOf(JsonElement padre, string prop, string archivo)
    {
        var arr = ArrayOf(padre, prop, archivo);
        var salida = new int[arr.GetArrayLength()];
        int i = 0;

        foreach (var v in arr.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out salida[i]))
                throw new InvalidBalanceDataException($"{archivo}: «{prop}[{i}]» no es un entero");

            i++;
        }

        return salida;
    }

    private static T Enum_<T>(string valor, string prop, string archivo) where T : struct, System.Enum
    {
        if (System.Enum.TryParse<T>(valor, ignoreCase: true, out var v))
            return v;

        throw new InvalidBalanceDataException(
            $"{archivo}: «{prop}» dice «{valor}», que no es un {typeof(T).Name} válido. " +
            $"Valores posibles: {string.Join(", ", System.Enum.GetNames<T>())}.");
    }

    private static DefenderClass[] ClassesOf(JsonElement padre, string archivo)
    {
        if (!padre.TryGetProperty("classes", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var lista = new List<DefenderClass>();

        foreach (var v in arr.EnumerateArray())
            lista.Add(Enum_<DefenderClass>(v.GetString() ?? "", "classes", archivo));

        return [.. lista];
    }

    private static LinearCurve Curve(JsonElement padre, string prop, string archivo)
    {
        var e = Obj(padre, prop, archivo);
        return new LinearCurve(Int(e, "base", archivo), Int(e, "perWave", archivo));
    }

    private static ChanceRange Between(JsonElement padre, string prop, string archivo)
    {
        var e = Obj(padre, prop, archivo);
        return new ChanceRange(Int(e, "divisor", archivo), Int(e, "min", archivo), Int(e, "max", archivo));
    }

    private static WeaponAbility[] Pool(JsonElement padre, string prop, string archivo)
    {
        var arr = ArrayOf(padre, prop, archivo);
        var lista = new List<WeaponAbility>();

        foreach (var v in arr.EnumerateArray())
            lista.Add(Enum_<WeaponAbility>(v.GetString() ?? "", prop, archivo));

        if (lista.Count == 0)
            throw new InvalidBalanceDataException($"{archivo}: el pool «{prop}» está vacío");

        return [.. lista];
    }

    // -------------------------------------------------------------------------
    //  clases.json
    // -------------------------------------------------------------------------

    public static ClassTable Classes(string texto)
    {
        const string archivo = "clases.json";

        using var doc = ParseJson(texto, archivo);
        var raiz = doc.RootElement;

        var lista = new List<ClassData>();
        var vistas = new HashSet<DefenderClass>();

        foreach (var c in ArrayOf(raiz, "classes", archivo).EnumerateArray())
        {
            var clase = Enum_<DefenderClass>(Text(c, "class", archivo), "class", archivo);

            if (!vistas.Add(clase))
                throw new InvalidBalanceDataException($"{archivo}: la clase «{clase}» está dos veces");

            int alcance = Int(c, "range", archivo);

            // ⚠ Con alcance 1 y distancia euclídea, una celda EN DIAGONAL queda a
            // √2 ≈ 1,41 y no entra: cubriría solo las cuatro ortogonales. Un
            // defensor puesto en diagonal respecto del camino no llegaría a tocar
            // nada, y el jugador no tendría forma de saber por qué.
            if (alcance < 2)
            {
                throw new InvalidBalanceDataException(
                    $"{archivo}: «{clase}» tiene alcance {alcance}. El mínimo es 2: " +
                    "con 1 no entran las diagonales y el defensor no toca nada.");
            }

            int cadencia = Int(c, "cadence", archivo);

            if (cadencia < 1)
                throw new InvalidBalanceDataException($"{archivo}: «{clase}» tiene cadencia {cadencia}, tiene que ser 1 o más");

            lista.Add(new ClassData(
                clase,
                Int(c, "cost", archivo),
                Int(c, "damage", archivo),
                alcance,
                cadencia,
                Bool(c, "support", false)));
        }

        var tabla = new ClassTable(
            [.. lista],
            Int(raiz, "extraDamagePerLevel", archivo),
            Int(raiz, "perkSlotsPerClass", archivo));

        // ⚠ Sin al menos una ofensiva, la elección inicial puede repartir tres
        // soportes y la corrida está perdida antes de empezar.
        if (tabla.Offensive().Length == 0)
            throw new InvalidBalanceDataException($"{archivo}: todas las clases son «soporte». Al menos una tiene que poder pelear sola.");

        return tabla;
    }

    // -------------------------------------------------------------------------
    //  perks.json
    // -------------------------------------------------------------------------

    public static PerkTable Perks(string texto)
    {
        const string archivo = "perks.json";

        using var doc = ParseJson(texto, archivo);
        var raiz = doc.RootElement;

        var apilado = Obj(raiz, "maxStackedPerTier", archivo);

        int MaxDe(PerkTier t) => t switch
        {
            PerkTier.Bronze => Int(apilado, "bronze", archivo),
            PerkTier.Silver => Int(apilado, "silver", archivo),
            PerkTier.Gold => Int(apilado, "gold", archivo),
            _ => Int(apilado, "unique", archivo),
        };

        var lista = new List<PerkDefinition>();
        var ids = new HashSet<int>();
        var claves = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var p in ArrayOf(raiz, "perks", archivo).EnumerateArray())
        {
            int id = Int(p, "id", archivo);
            string clave = Text(p, "key", archivo);

            if (!ids.Add(id))
            {
                throw new InvalidBalanceDataException(
                    $"{archivo}: el id {id} está repetido (segundo: «{clave}»). " +
                    "Los ids se escriben a mano y los guarda la mochila: tienen que ser únicos.");
            }

            if (!claves.Add(clave))
            {
                throw new InvalidBalanceDataException(
                    $"{archivo}: la key «{clave}» está repetida. " +
                    "planos.json y los archivos de idioma referencian por key.");
            }

            var tier = Enum_<PerkTier>(Text(p, "tier", archivo), "tier", archivo);
            var efecto = Enum_<PerkEffect>(Text(p, "effect", archivo), "effect", archivo);

            lista.Add(new PerkDefinition
            {
                Id = id,
                Key = clave,
                Tier = tier,
                Effect = efecto,
                Magnitude = Int(p, "magnitude", archivo),

                // Opcional: solo los perks que combinan dos cosas lo traen. Los
                // Únicos lo usan siempre —son la fusión de dos Oro—, y Cadena y
                // Quemadura para el número del segundo salto y del secundario.
                MagnitudeB = OptionalInt(p, "magnitudeB", 0),

                Classes = ClassesOf(p, archivo),
                MaxStacked = OptionalInt(p, "maxStacked", MaxDe(tier)),
            });
        }

        if (lista.Count == 0)
            throw new InvalidBalanceDataException($"{archivo}: no trae perks");

        return new PerkTable([.. lista]);
    }

    // -------------------------------------------------------------------------
    //  planos.json
    // -------------------------------------------------------------------------

    public static BlueprintTable Blueprints(string texto, PerkTable perks)
    {
        const string archivo = "planos.json";

        using var doc = ParseJson(texto, archivo);
        var raiz = doc.RootElement;

        var lista = new List<Blueprint>();

        int IdOf(string clave, string campo)
        {
            var p = perks.ByKey(clave)
                ?? throw new InvalidBalanceDataException(
                    $"{archivo}: «{campo}» dice «{clave}», que no es la key de ningún perk de perks.json");

            return p.Id;
        }

        foreach (var e in ArrayOf(raiz, "blueprints", archivo).EnumerateArray())
        {
            string clave = Text(e, "key", archivo);
            string oroA = Text(e, "goldA", archivo);
            string oroB = Text(e, "goldB", archivo);

            if (string.Equals(oroA, oroB, System.StringComparison.Ordinal))
                throw new InvalidBalanceDataException($"{archivo}: «{clave}» fusiona «{oroA}» consigo mismo");

            int idA = IdOf(oroA, "goldA");
            int idB = IdOf(oroB, "goldB");
            int idResultante = IdOf(clave, "key");

            // ⚠ Dos Oro dan un Único. Cualquier otra combinación es un bug de
            // datos: un plano que consume un Bronce vaciaría el sentido de que el
            // jefe sea la única fuente de Oro.
            if (perks.Get(idA).Tier != PerkTier.Gold || perks.Get(idB).Tier != PerkTier.Gold)
                throw new InvalidBalanceDataException($"{archivo}: «{clave}» tiene que fusionar dos perks de tier «gold»");

            if (perks.Get(idResultante).Tier != PerkTier.Unique)
                throw new InvalidBalanceDataException($"{archivo}: «{clave}» tiene que producir un perk de tier «unique»");

            lista.Add(new Blueprint(clave, idA, idB, idResultante, Bool(e, "initial", false)));
        }

        // ⚠ Sin al menos una receta conocida de arranque, un jugador nuevo nunca
        // ve un Único y la fusión —la salida de los Oro repetidos— le resulta
        // invisible hasta que gasta oro en algo que no entiende para qué sirve.
        bool hayInicial = false;

        foreach (var p in lista)
            hayInicial |= p.Initial;

        if (lista.Count > 0 && !hayInicial)
            throw new InvalidBalanceDataException($"{archivo}: ningún plano tiene «initial: true». Tiene que haber al menos uno.");

        return new BlueprintTable([.. lista]);
    }

    // -------------------------------------------------------------------------
    //  sinergias.json
    // -------------------------------------------------------------------------

    /// <summary>
    /// Las sinergias: conjuntos de clases y qué se gana por juntarlas.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Los conjuntos SE PISAN a propósito</b>, así que casi todo lo que
    /// chequea este lector es «esto se puede alcanzar de verdad». Con conjuntos
    /// disjuntos alcanzaba con mirar cada uno por separado; con solapamiento, una
    /// sinergia mal escrita no rompe nada al cargar y simplemente no se activa
    /// nunca — que es la peor clase de bug de datos, el que no se ve.</para>
    /// </remarks>
    public static SynergyTable Synergies(string texto)
    {
        const string archivo = "sinergias.json";

        using var doc = ParseJson(texto, archivo);
        var raiz = doc.RootElement;

        int maxRoster = Int(raiz, "maxRosterClasses", archivo);

        if (maxRoster < 2)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «maxRosterClasses» es {maxRoster}. Con menos de 2 clases en el " +
                "roster no hay sinergia posible y el archivo entero sobra.");
        }

        var lista = new List<Synergy>();
        var vistas = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var e in ArrayOf(raiz, "synergies", archivo).EnumerateArray())
        {
            string clave = Text(e, "key", archivo);

            if (!vistas.Add(clave))
                throw new InvalidBalanceDataException($"{archivo}: «{clave}» está dos veces");

            var clases = ClassesOf(e, archivo);

            // Una sinergia de una sola clase es un buff de clase disfrazado: iría
            // en clases.json, donde se lee al lado del resto de sus números.
            if (clases.Length < 2)
            {
                throw new InvalidBalanceDataException(
                    $"{archivo}: «{clave}» tiene {clases.Length} clase(s). Una sinergia junta " +
                    "dos o más; para subirle un número a una sola clase está clases.json.");
            }

            var repetidas = new HashSet<DefenderClass>();

            foreach (var c in clases)
            {
                if (!repetidas.Add(c))
                    throw new InvalidBalanceDataException($"{archivo}: «{clave}» repite la clase «{c}»");
            }

            // ⚠ El chequeo que justifica que «maxRosterClasses» viva en los datos.
            // Una sinergia que pide más clases de las que entran en el roster no se
            // completa NUNCA: carga bien, no rompe nada y no se activa jamás.
            if (clases.Length > maxRoster)
            {
                throw new InvalidBalanceDataException(
                    $"{archivo}: «{clave}» junta {clases.Length} clases y el roster solo tiene " +
                    $"{maxRoster} lugares. Nunca se podría completar.");
            }

            // Opcional: por defecto alcanza con un defensor para que la clase cuente.
            int minUnidades = e.TryGetProperty("minUnitsPerClass", out var mu)
                ? Int(e, "minUnitsPerClass", archivo)
                : 1;

            if (minUnidades < 1)
                throw new InvalidBalanceDataException($"{archivo}: «{clave}» tiene «minUnitsPerClass» en {minUnidades}, y el mínimo es 1");

            var alcance = e.TryGetProperty("scope", out var sc)
                ? Enum_<SynergyScope>(sc.GetString() ?? "", "scope", archivo)
                : SynergyScope.Members;

            var escalones = new List<SynergyTier>();
            int anterior = 0;
            SynergyEffect? efectoAnterior = null;
            int magnitudAnterior = 0;

            foreach (var t in ArrayOf(e, "tiers", archivo).EnumerateArray())
            {
                int necesita = Int(t, "need", archivo);

                if (necesita < 2)
                    throw new InvalidBalanceDataException($"{archivo}: «{clave}» tiene un escalón que pide {necesita} clase(s). El mínimo es 2.");

                if (necesita > clases.Length)
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: «{clave}» tiene un escalón que pide {necesita} clases y el " +
                        $"conjunto solo tiene {clases.Length}. Ese escalón no se alcanza nunca.");
                }

                // De menor a mayor: así `TierFor` puede quedarse con el más alto y
                // el que lee el archivo ve la progresión en el orden en que sube.
                if (necesita <= anterior)
                    throw new InvalidBalanceDataException($"{archivo}: los escalones de «{clave}» tienen que ir de menor a mayor «need»");

                anterior = necesita;

                string nombreEfecto = Text(t, "effect", archivo);

                // ⚠ Se caza ACÁ, antes del parse genérico, para que el mensaje
                // ENSEÑE. `Enum_` diría «no es un SynergyEffect válido» y el que
                // lo intente asumiría que falta implementarlo — cuando en realidad
                // está sacado a propósito y agregarlo silenciosamente no anda.
                if (string.Equals(nombreEfecto, "Cadence", System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: «{clave}» pide «Cadence», y no existe A PROPÓSITO. El daño va " +
                        "en centésimas de punto y el alcance en milésimas de celda, pero la cadencia " +
                        "sigue en TICKS ENTEROS (18 a 30): un tick es un escalón del 3,3% al 5,6%, " +
                        "así que un +5% sobre cadencia 18 da 17,14 y trunca a 17, y cualquier bonus " +
                        "más chico que el escalón hace exactamente cero. Para que una sinergia dé " +
                        "velocidad de ataque hay que escalar la cadencia primero, igual que se hizo " +
                        "con el daño y el alcance.");
                }

                var efecto = Enum_<SynergyEffect>(nombreEfecto, "effect", archivo);

                // ⚠ La cadencia sigue en TICKS ENTEROS (18 a 30), así que un tick es
                // un escalón del 3,3% al 5,6% y cualquier bonus más chico hace CERO.
                // El enum no tiene Cadence, pero el mensaje tiene que decir por qué
                // y no solo «no es un valor válido».
                if (efecto == SynergyEffect.None)
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: «{clave}» tiene un escalón con «effect» en None. " +
                        "Los efectos posibles son Damage, Range, DoubleGoldOnKill y TowerHealOnKill. " +
                        "NO hay Cadence a propósito: la cadencia está en ticks enteros y un bonus " +
                        "porcentual chico se trunca a nada.");
                }

                int magnitud = Int(t, "magnitude", archivo);

                if (magnitud <= 0)
                    throw new InvalidBalanceDataException($"{archivo}: «{clave}» tiene un escalón con magnitud {magnitud}. Tiene que ser positiva.");

                // Una chance de más de 100 se lee como «siempre» y esconde un typo.
                if (efecto == SynergyEffect.DoubleGoldOnKill && magnitud > 100)
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: «{clave}» tiene una chance de doble oro de {magnitud}%. " +
                        "Es una probabilidad: como mucho 100.");
                }

                // "only": a qué clases del conjunto les llega este escalón. Tiene
                // que ser un SUBCONJUNTO de "classes": una clase de afuera no
                // cuenta para activar la sinergia y tampoco puede cobrarla.
                var solo = new List<DefenderClass>();

                if (t.TryGetProperty("only", out var arrSolo))
                {
                    // ⚠ Escrito mal, «only» se ignoraba en silencio y el escalón
                    // pasaba a llegarle a TODO el conjunto: exactamente el bonus
                    // que la restricción existía para no dar. Un campo que está
                    // pero no restringe se rechaza, como el `cap` de más abajo.
                    if (arrSolo.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidBalanceDataException(
                            $"{archivo}: «{clave}» tiene un escalón con «only» que no es una lista. " +
                            "Va entre corchetes aunque sea una sola clase: [\"Thief\"].");
                    }

                    if (arrSolo.GetArrayLength() == 0)
                    {
                        throw new InvalidBalanceDataException(
                            $"{archivo}: «{clave}» tiene un escalón con «only» vacío. " +
                            "Un «only» vacío no restringe nada: sacalo, o nombrá las clases que cobran.");
                    }

                    foreach (var v in arrSolo.EnumerateArray())
                    {
                        var c = Enum_<DefenderClass>(v.GetString() ?? "", "only", archivo);

                        if (System.Array.IndexOf(clases, c) < 0)
                        {
                            throw new InvalidBalanceDataException(
                                $"{archivo}: «{clave}» tiene un escalón con «only» que nombra a {c}, " +
                                "que no está en «classes». Solo puede restringir a clases del conjunto.");
                        }

                        if (!solo.Contains(c))
                            solo.Add(c);
                    }
                }

                // ⚠ "cap": obligatorio en los efectos al matar y PROHIBIDO en los
                // numéricos. Obligatorio por la lección de Saqueo —sin tope por
                // oleada una build de economía se vuelve obligatoria—; prohibido
                // porque un número que no hace nada en un archivo de balance es
                // un número que alguien va a tocar creyendo que hace algo.
                bool tieneCap = t.TryGetProperty("cap", out _);
                int cap = 0;

                if (efecto.IsOnKill())
                {
                    if (!tieneCap)
                    {
                        throw new InvalidBalanceDataException(
                            $"{archivo}: «{clave}» tiene un escalón «{efecto}» sin «cap». Los efectos al " +
                            "matar llevan tope por oleada OBLIGATORIO —oro extra máximo, o puntos de vida " +
                            "máximos—: sin él una build de economía deja de ser una decisión.");
                    }

                    cap = Int(t, "cap", archivo);

                    if (cap <= 0)
                        throw new InvalidBalanceDataException($"{archivo}: «{clave}» tiene un «cap» de {cap}. Tiene que ser positivo.");
                }
                else if (tieneCap)
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: «{clave}» tiene «cap» en un escalón «{efecto}», y ahí no hace nada. " +
                        "El tope por oleada es solo de los efectos al matar.");
                }

                // ⚠ «only» con «scope: board» no significa nada: board ya quiere
                // decir «le llega a todo el tablero», y el lector obliga a que
                // «only» sea subconjunto de «classes», así que el par degenera en
                // un «members» disfrazado. Dos formas de escribir lo mismo es cómo
                // el chequeo de huérfanas del validador termina contando mal.
                if (alcance == SynergyScope.Board && solo.Count > 0)
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: «{clave}» combina «scope: board» con «only», y eso no existe. " +
                        "«board» le llega a todo el tablero; si el escalón solo lo cobran algunas " +
                        "clases del conjunto, el scope es «members».");
                }

                // ⚠ Todos los escalones de una sinergia dan el MISMO efecto, y de
                // menor a mayor. `TierFor` se queda con el más alto alcanzado, así
                // que un escalón superior más flojo —o de otro efecto— hace que
                // COMPLETAR el conjunto empeore al defensor, en silencio y sin que
                // ningún número del catálogo lo anticipe.
                if (efectoAnterior is { } previo && previo != efecto)
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: los escalones de «{clave}» mezclan «{previo}» y «{efecto}». " +
                        "Un conjunto da un solo efecto: al subir de escalón el más alto TAPA al " +
                        "anterior, así que completarlo cambiaría un efecto por otro. Si querés las " +
                        "dos cosas, son dos sinergias sobre las mismas clases.");
                }

                if (efectoAnterior is not null && magnitud <= magnitudAnterior)
                {
                    throw new InvalidBalanceDataException(
                        $"{archivo}: el escalón «need {necesita}» de «{clave}» da {magnitud} y el " +
                        $"anterior daba {magnitudAnterior}. Los escalones suben: como el más alto " +
                        "tapa al anterior, uno más flojo castiga por completar el conjunto.");
                }

                efectoAnterior = efecto;
                magnitudAnterior = magnitud;

                escalones.Add(new SynergyTier(necesita, efecto, magnitud, [.. solo], cap));
            }

            if (escalones.Count == 0)
                throw new InvalidBalanceDataException($"{archivo}: «{clave}» no tiene ningún escalón en «tiers»");

            lista.Add(new Synergy(clave, clases, minUnidades, alcance, [.. escalones]));
        }

        return new SynergyTable([.. lista], maxRoster);
    }

    // -------------------------------------------------------------------------
    //  generador-armas.json
    // -------------------------------------------------------------------------

    public static WeaponTable Weapons(string texto)
    {
        const string archivo = "generador-armas.json";

        using var doc = ParseJson(texto, archivo);
        var raiz = doc.RootElement;

        var afijos = Obj(raiz, "affixes", archivo);
        var rareza = Obj(raiz, "rarity", archivo);
        var porc = Obj(raiz, "abilityPercent", archivo);
        var valor = Obj(raiz, "value", archivo);
        var pools = Obj(raiz, "pools", archivo);

        var habilidades = new List<WeaponAbilityData>();
        var vistas = new HashSet<WeaponAbility>();

        foreach (var h in ArrayOf(raiz, "abilities", archivo).EnumerateArray())
        {
            var cual = Enum_<WeaponAbility>(Text(h, "ability", archivo), "ability", archivo);

            if (!vistas.Add(cual))
                throw new InvalidBalanceDataException($"{archivo}: la habilidad «{cual}» está dos veces");

            habilidades.Add(new WeaponAbilityData(
                cual,
                new MagnitudeFormula(
                    Int(h, "base", archivo),
                    Int(h, "divisor", archivo),
                    Int(h, "min", archivo),
                    Int(h, "max", archivo))));
        }

        var tabla = new WeaponTable(
            Int(afijos, "step", archivo),
            Int(afijos, "damagePointCost", archivo),
            Int(afijos, "cadencePointCost", archivo),
            Int(afijos, "rangePointCost", archivo),
            Between(rareza, "legendary", archivo),
            Between(rareza, "epic", archivo),
            Between(rareza, "rare", archivo),
            Int(porc, "legendary", archivo),
            Int(porc, "epic", archivo),
            Int(porc, "rare", archivo),
            Int(porc, "common", archivo),
            Int(valor, "valueDivisor", archivo),
            Int(valor, "shopPriceMultiplier", archivo),
            Pool(pools, "common", archivo),
            Pool(pools, "rare", archivo),
            Pool(pools, "breakers", archivo),
            [.. habilidades]);

        // Toda habilidad que un pool puede tirar necesita su fórmula, o el arma
        // saldría con magnitud 0 y la habilidad no haría nada.
        foreach (var pool in new[] { tabla.CommonPool, tabla.RarePool, tabla.BreakerPool })
        {
            foreach (var h in pool)
            {
                if (tabla.Get(h) is null)
                    throw new InvalidBalanceDataException($"{archivo}: «{h}» está en un pool pero no tiene fórmula en «habilidades»");
            }
        }

        if (tabla.ValueDivisor == 0)
            throw new InvalidBalanceDataException($"{archivo}: «divisorValor» no puede ser 0");

        if (tabla.Step < 1)
            throw new InvalidBalanceDataException($"{archivo}: «paso» tiene que ser 1 o más");

        return tabla;
    }

    // -------------------------------------------------------------------------
    //  curvas.json
    // -------------------------------------------------------------------------

    public static CurveData Curves(string texto)
    {
        const string archivo = "curvas.json";

        using var doc = ParseJson(texto, archivo);
        var raiz = doc.RootElement;

        var torre = Obj(raiz, "tower", archivo);
        var corrida = Obj(raiz, "run", archivo);
        var defensores = Obj(raiz, "defenders", archivo);
        var economia = Obj(raiz, "economy", archivo);
        var presupuesto = Obj(raiz, "budget", archivo);
        var porOleada = Obj(raiz, "perWaveCurves", archivo);
        var tienda = Obj(raiz, "shop", archivo);
        var drops = Obj(raiz, "drops", archivo);
        var oleada = Obj(raiz, "wave", archivo);
        var perksNpc = Obj(raiz, "npcPerks", archivo);
        var jefe = Obj(raiz, "boss", archivo);
        var mejoras = Obj(raiz, "upgrades", archivo);

        int oleadas = Int(corrida, "wavesPerRun", archivo);
        int[] ultimaPorArea = IntArrayOf(corrida, "lastWavePerArea", archivo);
        int areas = Int(corrida, "areaCount", archivo);

        if (ultimaPorArea.Length != areas)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «cantidadAreas» dice {areas} pero «ultimaOleadaPorArea» trae {ultimaPorArea.Length} números");
        }

        if (areas == 0)
            throw new InvalidBalanceDataException($"{archivo}: «cantidadAreas» no puede ser 0");

        // ⚠ La corrida son 100 oleadas y TERMINA ahí, matando al jefe final del
        // área 8. Si la última del reparto no coincide con el total, o queda una
        // corrida que nunca termina o queda un área a la que no se llega nunca.
        if (ultimaPorArea[^1] != oleadas)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: la última área termina en la oleada {ultimaPorArea[^1]} pero la corrida son {oleadas}. " +
                "Tienen que coincidir: no hay modo sin fin.");
        }

        for (int i = 1; i < ultimaPorArea.Length; i++)
        {
            if (ultimaPorArea[i] <= ultimaPorArea[i - 1])
                throw new InvalidBalanceDataException($"{archivo}: «ultimaOleadaPorArea» tiene que ir en aumento (área {i + 1})");
        }

        var intervalo = Obj(porOleada, "spawnIntervalTicks", archivo);
        var chancePerk = Obj(porOleada, "npcPerkChance", archivo);
        var costoNpc = Obj(oleada, "npcCost", archivo);
        var formaciones = Obj(oleada, "spawnFormations", archivo);

        var curvas = new CurveData(
            new TowerData(
                Int(torre, "initialHp", archivo),
                Int(torre, "damage", archivo),
                Int(torre, "cadenceTicks", archivo)),

            new RunData(
                oleadas,
                areas,
                ultimaPorArea,
                Int(corrida, "wavesBetweenChoices", archivo),
                Int(corrida, "preparationSeconds", archivo),
                Int(corrida, "freeClasses", archivo),
                Int(corrida, "initialClassOffers", archivo),
                Int(corrida, "sealedUnlockWave", archivo),
                Int(corrida, "choiceDiscountPercent", archivo)),

            new DefenderData(
                Int(defensores, "backpackSlots", archivo),
                Int(defensores, "gearSlotsPerClass", archivo),
                Int(defensores, "defenderSellPercent", archivo),
                Int(defensores, "weaponSellPercent", archivo)),

            new EconomyData(
                Int(economia, "startingGoldTokens", archivo),
                Int(economia, "goldPerCalledEnemy", archivo),
                Int(economia, "callGoldCap", archivo),
                Int(economia, "levelUpPriceBase", archivo),
                IntArrayOf(economia, "goldMultiplierPerArea", archivo)),

            new BudgetData(
                Int(presupuesto, "base", archivo),
                Int(presupuesto, "quadratic", archivo),
                Int(presupuesto, "equivalentCapMin", archivo),
                Int(presupuesto, "equivalentCapMax", archivo)),

            new PerWaveCurves(
                Curve(porOleada, "clearGoldBonus", archivo),
                Curve(porOleada, "bossDropBudget", archivo),
                Curve(porOleada, "shopItemBudget", archivo),
                Int(intervalo, "base", archivo),
                Int(intervalo, "divisor", archivo),
                Int(intervalo, "min", archivo),
                Int(intervalo, "max", archivo),
                Int(chancePerk, "divisor", archivo),
                Int(chancePerk, "min", archivo),
                Int(chancePerk, "max", archivo)),

            new ShopData(
                Int(tienda, "slots", archivo),
                Int(tienda, "intervalWaves", archivo),
                Int(tienda, "classWeight", archivo),
                Int(tienda, "bronzeWeight", archivo),
                Int(tienda, "silverWeight", archivo),
                Int(tienda, "goldWeight", archivo),
                Int(tienda, "blueprintWeight", archivo),
                Int(tienda, "weaponWeight", archivo),
                Int(tienda, "maxLevelOnSale", archivo),
                Int(tienda, "maxGoldOffers", archivo),
                Int(tienda, "maxBlueprintOffers", archivo),
                Int(tienda, "minUsableOffers", archivo),
                Int(tienda, "silverMinWave", archivo),
                Int(tienda, "goldMinWave", archivo),
                Int(tienda, "blueprintMinWave", archivo),
                Int(tienda, "recipeWeightMultiplier", archivo),
                Curve(tienda, "bronzePerkPrice", archivo),
                Curve(tienda, "silverPerkPrice", archivo),
                Curve(tienda, "goldPerkPrice", archivo),
                Curve(tienda, "blueprintPrice", archivo),
                Curve(tienda, "classPrice", archivo),
                Int(Obj(tienda, "classPrice", archivo), "classCostMultiplier", archivo)),

            new DropData(
                Int(drops, "commonChance", archivo),
                Int(drops, "bronzeChance", archivo),
                Int(drops, "bossGoldOptions", archivo),
                Int(drops, "bossGoldLateArea", archivo),
                Int(drops, "bossGoldOptionsLate", archivo),
                Int(drops, "bossGoldPicksLate", archivo),
                Int(drops, "synergyWeight", archivo),
                Int(drops, "goldPoolSize", archivo),
                Int(drops, "goldPoolPerNewClass", archivo),
                Bool(drops, "finalBossDrops", false)),

            new WaveData(
                Int(oleada, "maxNpcs", archivo),
                Int(oleada, "currentAreaWeight", archivo),
                Int(oleada, "previousAreaWeight", archivo),
                Int(oleada, "previousAreaWeightFirst", archivo),
                Int(oleada, "previousAreaFadeWaves", archivo),
                Int(costoNpc, "hpDivisor", archivo),
                Int(costoNpc, "maxDamageDivisor", archivo),
                Int(costoNpc, "speedDivisor", archivo),
                Int(oleada, "defaultSpawnInterval", archivo),
                new SpawnFormations(
                    Int(formaciones, "uniformWeight", archivo),
                    Int(formaciones, "pairsWeight", archivo),
                    Int(formaciones, "burstWeight", archivo),
                    Int(formaciones, "pairShortPercent", archivo),
                    Int(formaciones, "pairLongPercent", archivo),
                    Int(formaciones, "burstSize", archivo),
                    Int(formaciones, "burstShortPercent", archivo),
                    Int(formaciones, "burstLongPercent", archivo))),

            new NpcPerkData(
                Int(perksNpc, "healthCost", archivo),
                Int(perksNpc, "speedCost", archivo),
                Int(perksNpc, "healthStep", archivo),
                Int(perksNpc, "speedStep", archivo),
                Int(perksNpc, "maxHealth", archivo),
                Int(perksNpc, "maxSpeed", archivo),
                Int(perksNpc, "healthChanceInRoll", archivo),
                Int(perksNpc, "speedChanceInLeftover", archivo),
                Int(perksNpc, "impulsiveCost", archivo),
                Int(perksNpc, "impulsiveChance", archivo),
                Int(perksNpc, "leaderCost", archivo),
                Int(perksNpc, "leaderChance", archivo),
                Int(perksNpc, "maxLeadersPerWave", archivo)),

            new BossData(
                Int(jefe, "extraHealthDivisor", archivo),
                Int(jefe, "maxExtraHealth", archivo),
                OptionalText(jefe, "finalBossName")),

            new UpgradeData(
                Int(mejoras, "bronzeToSilver", archivo),
                Int(mejoras, "silverToGold", archivo),
                Int(mejoras, "maxCraftedGoldPerRun", archivo)));

        // ⚠ Sin tope de NPCs una oleada tardía compra miles de bichos: no es más
        // difícil, es más larga. Medido: corridas clavadas contra el techo de
        // 700.000 ticks en la oleada 55.
        if (curvas.Wave.MaxNpcs < 1)
            throw new InvalidBalanceDataException($"{archivo}: «maxNpcs» tiene que ser 1 o más");

        if (curvas.Wave.HpDivisor == 0 || curvas.Wave.MaxDamageDivisor == 0 || curvas.Wave.SpeedDivisor == 0)
            throw new InvalidBalanceDataException($"{archivo}: ningún divisor de «costoNpc» puede ser 0");

        // ⚠ El desvanecido del área anterior es lo que borra el MURO DEL BORDE.
        // Sin él, la primera oleada de un área saltaba entre +19% y +56% de
        // presupuesto de una oleada a la otra, porque el costo medio del pool
        // cambia de golpe. Un fade de menos de una oleada no desvanece nada, y
        // un peso inicial menor que el de régimen sería un fade al revés.
        if (curvas.Wave.PreviousAreaFadeWaves < 1)
            throw new InvalidBalanceDataException($"{archivo}: «previousAreaFadeWaves» tiene que ser 1 o más");

        if (curvas.Wave.PreviousAreaWeightFirst < curvas.Wave.PreviousAreaWeight)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «previousAreaWeightFirst» ({curvas.Wave.PreviousAreaWeightFirst}) no puede ser " +
                $"menor que «previousAreaWeight» ({curvas.Wave.PreviousAreaWeight}): el área anterior se " +
                "desvanece, no aparece");
        }


        if (curvas.Boss.ExtraHealthDivisor == 0)
            throw new InvalidBalanceDataException($"{archivo}: «divisorVidaExtra» no puede ser 0");

        if (curvas.Tower.CadenceTicks < 1)
            throw new InvalidBalanceDataException($"{archivo}: la cadencia de la Tower tiene que ser 1 tick o más");

        // ⚠ Un precio de nivel en 0 haría gratis el único sumidero de oro de la
        // corrida, y en silencio: el jugador compraría los seis niveles de las
        // nueve clases en la primera preparación y no habría con qué medir la
        // economía nunca más.
        if (curvas.Economy.LevelUpPriceBase < 1)
            throw new InvalidBalanceDataException($"{archivo}: «levelUpPriceBase» tiene que ser 1 o más");

        // ⚠ Un multiplicador por área que no cubra todas las áreas dejaría al
        // resto pagando el 100% en silencio, que es el error que no se ve: la
        // economía tardía volvería a descontrolarse sin que nadie tocara nada.
        if (curvas.Economy.GoldMultiplierPerArea.Length != areas)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «goldMultiplierPerArea» trae {curvas.Economy.GoldMultiplierPerArea.Length} " +
                $"números pero hay {areas} áreas");
        }

        // ⚠ Con cero clases gratis, la pantalla inicial no puede regalar la
        // primera y la corrida arranca sin nada en el tablero: no hay con qué
        // matar al primer bicho y no hay oro para comprar una clase todavía.
        if (curvas.RunCurve.FreeClasses < 1)
            throw new InvalidBalanceDataException($"{archivo}: «freeClasses» tiene que ser 1 o más");

        // ⚠ No se puede regalar más clases que las que la pantalla ofrece: el
        // cupo sobrante quedaría vivo en la tienda siguiente, que NO regala nada,
        // y la primera oferta pagada de la corrida saldría gratis sin que nada lo
        // explique.
        if (curvas.RunCurve.InitialClassOffers < curvas.RunCurve.FreeClasses)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «initialClassOffers» ({curvas.RunCurve.InitialClassOffers}) no puede ser " +
                $"menor que «freeClasses» ({curvas.RunCurve.FreeClasses})");
        }

        // ⚠ Y ofrecer exactamente las que se regalan no es elegir, es repartir.
        if (curvas.RunCurve.FreeClasses > 1 &&
            curvas.RunCurve.InitialClassOffers <= curvas.RunCurve.FreeClasses)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «initialClassOffers» tiene que ser mayor que «freeClasses» " +
                $"para que el arranque sea una elección");
        }

        // ⚠ La clase sellada tiene que abrirse ANTES de que termine la corrida, y
        // en una oleada que abra tienda: el sello se levanta cuando la tienda
        // abre, así que un número que no sea múltiplo de «wavesBetweenChoices» se
        // redondearía solo hacia la tienda siguiente sin que nadie lo pidiera.
        if (curvas.RunCurve.FreeClasses > 1)
        {
            int desbloqueo = curvas.RunCurve.SealedUnlockWave;

            if (desbloqueo < 1 || desbloqueo >= curvas.RunCurve.WavesPerRun)
            {
                throw new InvalidBalanceDataException(
                    $"{archivo}: «sealedUnlockWave» tiene que estar entre 1 y " +
                    $"{curvas.RunCurve.WavesPerRun - 1}");
            }

            if (curvas.RunCurve.WavesBetweenChoices > 0 &&
                desbloqueo % curvas.RunCurve.WavesBetweenChoices != 0)
            {
                throw new InvalidBalanceDataException(
                    $"{archivo}: «sealedUnlockWave» ({desbloqueo}) tiene que ser múltiplo de " +
                    $"«wavesBetweenChoices» ({curvas.RunCurve.WavesBetweenChoices}): " +
                    $"el sello se levanta cuando abre la tienda");
            }
        }

        // ⚠ Un jefe que ofrece cero Oro rompe la única fuente de Oro del juego.
        if (curvas.Drops.BossGoldOptions < 1)
            throw new InvalidBalanceDataException($"{archivo}: «bossGoldOptions» tiene que ser 1 o más");

        // ⚠ Llevarse tantos como se ofrecen no es una elección, es un reparto: la
        // pantalla del jefe pediría elegir y no habría nada que decidir.
        if (curvas.Drops.BossGoldPicksLate >= curvas.Drops.BossGoldOptionsLate)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «bossGoldPicksLate» tiene que ser menor que «bossGoldOptionsLate»");
        }

        if (curvas.Drops.BossGoldPicksLate < 1)
            throw new InvalidBalanceDataException($"{archivo}: «bossGoldPicksLate» tiene que ser 1 o más");

        if (curvas.Drops.GoldPoolSize < 1)
            throw new InvalidBalanceDataException($"{archivo}: «goldPoolSize» tiene que ser 1 o más");

        // ⚠ La acción Upgrade guarda la selección del jugador como una máscara de
        // bits en un `int`, así que el lugar 31 de la mochila no tendría bit. Con
        // 32 lugares el bit 31 es el de signo y la máscara se leería negativa.
        if (curvas.Defenders.BackpackSlots is < 1 or > 31)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «backpackSlots» tiene que estar entre 1 y 31 — la acción Upgrade " +
                "guarda la selección como máscara de bits en un int (Acciones.cs)");
        }

        // Uno o cero no es una receta: sería un botón que cambia un perk por
        // otro mejor sin costo. Dos es el mínimo que representa un intercambio.
        if (curvas.Upgrades.BronzeToSilver < 2 || curvas.Upgrades.SilverToGold < 2)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «bronzeToSilver» y «silverToGold» tienen que ser 2 o más");
        }

        // ⚠ Sin tope, la mejora fabrica tanto Oro como los ocho jefes juntos y los
        // Únicos dejan de costar algo. Cero apaga esa mitad de la mecánica.
        if (curvas.Upgrades.MaxCraftedGoldPerRun < 0)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «maxCraftedGoldPerRun» no puede ser negativo");
        }

        // La mochila tiene que poder sostener una receta: si entran menos perks
        // que los que la receta pide, la mejora es inalcanzable y no se ve por
        // qué. Hace falta uno más, además, para que quepa el resultado.
        int masCara = System.Math.Max(curvas.Upgrades.BronzeToSilver, curvas.Upgrades.SilverToGold);

        if (masCara > curvas.Defenders.BackpackSlots)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: una receta de mejora pide {masCara} perks y la mochila tiene " +
                $"{curvas.Defenders.BackpackSlots} lugares");
        }

        // ⚠ Una tienda de 0 ranuras no es «tienda apagada»: es una pantalla vacía
        // que igual frena la corrida cada tres oleadas.
        if (curvas.Shop.Slots < 1)
            throw new InvalidBalanceDataException($"{archivo}: «shop.slots» tiene que ser 1 o más");

        // Con intervalo 0 la tienda no abriría nunca y todo el sistema quedaría
        // muerto en silencio, sin un error que lo diga.
        if (curvas.Shop.IntervalWaves < 1)
            throw new InvalidBalanceDataException($"{archivo}: «shop.intervalWaves» tiene que ser 1 o más");

        int pesoTotal = curvas.Shop.ClassWeight + curvas.Shop.BronzeWeight + curvas.Shop.SilverWeight
                        + curvas.Shop.GoldWeight + curvas.Shop.BlueprintWeight + curvas.Shop.WeaponWeight;

        if (pesoTotal <= 0)
            throw new InvalidBalanceDataException($"{archivo}: los pesos de «shop» suman 0. La tienda no podría sortear nada.");

        // ⚠ Sin tope de Oro por tienda, una pantalla Oro/Oro/Oro/Oro convierte una
        // visita en un salto de poder enorme y arruina la comparación entre
        // corridas — que es todo lo que un ranking global mide.
        if (curvas.Shop.MaxGoldOffers < 0 || curvas.Shop.MaxBlueprintOffers < 0)
            throw new InvalidBalanceDataException($"{archivo}: los topes de «shop» no pueden ser negativos");

        // ⚠ Con el tope en 0 la ranura de clase saldría siempre bloqueada para
        // todo lo que ya esté en el roster, y eso saca de la corrida la mitad de
        // lo que era la pantalla de elección: «ancho o profundo» sin el profundo.
        if (curvas.Shop.MaxLevelOnSale < 1)
            throw new InvalidBalanceDataException($"{archivo}: «shop.maxLevelOnSale» tiene que ser 1 o más");

        if (curvas.Shop.MinUsableOffers > curvas.Shop.Slots)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «shop.minUsableOffers» ({curvas.Shop.MinUsableOffers}) no puede pasar " +
                $"de «shop.slots» ({curvas.Shop.Slots}): la garantía sería imposible de cumplir");
        }

        // ⚠ Los promedios de las tres formaciones TIENEN que dar 100. Si no dan,
        // la formación se convierte en una oleada más corta o más larga, o sea
        // dificultad encubierta que no aparece en el presupuesto — que es el único
        // número que se mira para saber cuán dura es una oleada.
        var f = curvas.Wave.Formations;

        if (f.UniformWeight + f.PairsWeight + f.BurstWeight <= 0)
            throw new InvalidBalanceDataException($"{archivo}: los pesos de «spawnFormations» suman 0");

        if (f.BurstSize < 1)
            throw new InvalidBalanceDataException($"{archivo}: «burstSize» tiene que ser 1 o más");

        if (f.PairShortPercent + f.PairLongPercent != 200)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: la formación «parejas» promedia " +
                $"{(f.PairShortPercent + f.PairLongPercent) / 2}% en vez de 100%. " +
                "Una formación que no conserva el promedio es dificultad encubierta.");
        }

        int totalRafaga = (f.BurstShortPercent * (f.BurstSize - 1)) + f.BurstLongPercent;

        if (totalRafaga != f.BurstSize * 100)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: la formación «ráfaga» promedia {totalRafaga / f.BurstSize}% en vez de 100%. " +
                "Una formación que no conserva el promedio es dificultad encubierta.");
        }

        return curvas;
    }

    // -------------------------------------------------------------------------
    //  combate.json
    // -------------------------------------------------------------------------

    public static CombatData Combat(string texto)
    {
        const string archivo = "combate.json";

        using var doc = ParseJson(texto, archivo);
        var raiz = doc.RootElement;

        var enemigo = Obj(raiz, "enemy", archivo);
        var jefe = Obj(raiz, "bossControl", archivo);
        var estados = Obj(raiz, "states", archivo);
        var comp = Obj(raiz, "behaviors", archivo);
        var unicos = Obj(raiz, "uniques", archivo);
        var arma = Obj(raiz, "weaponAbilities", archivo);
        var rasgos = Obj(raiz, "enemyTraits", archivo);

        var slow = Obj(estados, "slow", archivo);
        var raiz2 = Obj(estados, "root", archivo);
        var empuje = Obj(estados, "push", archivo);
        var aura = Obj(estados, "inspire", archivo);
        var botin = Obj(estados, "loot", archivo);
        var dot = Obj(estados, "dot", archivo);

        var ricochet = Obj(comp, "ricochet", archivo);
        var cadena = Obj(comp, "chain", archivo);
        var splash = Obj(comp, "splash", archivo);
        var quemar = Obj(comp, "burn", archivo);
        var foco = Obj(comp, "focus", archivo);
        var sobrepen = Obj(comp, "overpenetration", archivo);
        var eco = Obj(comp, "arcaneEcho", archivo);
        var escalaAlcance = Obj(comp, "rangeScaling", archivo);
        var drenaje = Obj(comp, "siphon", archivo);

        // "cada cuántos golpes" de los estados periódicos. Van sueltos y no en un
        // objeto común porque cada uno se balancea contra su propio efecto: cambiar
        // el de Raíces no tiene nada que ver con el de Inspiración.
        int CadaCuantos(string prop) => Int(Obj(comp, prop, archivo), "everyHits", archivo);

        var combate = new CombatData(
            new EnemyCombat(
                Int(enemigo, "damageAverageDivisor", archivo),
                Int(enemigo, "minimumDamage", archivo)),

            new BossControl(
                Int(jefe, "powerPercent", archivo),
                Int(jefe, "hardCcDurationPercent", archivo),
                Int(jefe, "pushPercent", archivo),
                Int(jefe, "hardCcExtraImmunityTicks", archivo),
                Int(jefe, "maxPushPerBoss", archivo),
                Int(jefe, "finalBossPowerPercent", archivo),
                Bool(jefe, "finalBossImmuneToHardCc", true),
                Bool(jefe, "finalBossImmuneToPush", true),
                Bool(jefe, "lootIgnoresBosses", true)),

            new StateRules(
                Int(slow, "capPercent", archivo),
                Int(slow, "durationTicks", archivo),
                Int(slow, "maxDurationTicks", archivo),
                Int(Obj(estados, "vulnerable", archivo), "durationTicks", archivo),
                Int(Obj(estados, "weaken", archivo), "durationTicks", archivo),
                Int(Obj(estados, "huntMark", archivo), "durationTicks", archivo),
                Int(raiz2, "durationTicks", archivo),
                Int(raiz2, "immunityTicks", archivo),
                Int(raiz2, "maxPerEnemy", archivo),
                Int(empuje, "maxPerEnemy", archivo),
                Int(aura, "durationTicks", archivo),
                Int(aura, "radiusCells", archivo),
                Int(botin, "durationTicks", archivo),
                Int(botin, "maxGoldPerEnemy", archivo),
                Int(botin, "maxGoldPerWave", archivo),
                Int(dot, "durationTicks", archivo),
                Int(dot, "maxDurationTicks", archivo)),

            new BehaviorRules(
                Int(ricochet, "bounces", archivo),
                Int(ricochet, "jumpWindowThousandths", archivo),
                Int(cadena, "maxJumps", archivo),
                Int(cadena, "jumpWindowThousandths", archivo),
                Int(splash, "maxSecondaries", archivo),
                Int(splash, "radiusThousandths", archivo),
                Int(splash, "totalCapPercent", archivo),
                Int(quemar, "maxSecondaries", archivo),
                Int(quemar, "radiusThousandths", archivo),
                CadaCuantos("criticalHit"),
                Int(foco, "maxStacks", archivo),
                Int(foco, "resetTicks", archivo),
                CadaCuantos("sunder"),
                CadaCuantos("root"),
                CadaCuantos("seismic"),
                CadaCuantos("inspire"),
                Int(eco, "everyHits", archivo),
                Int(eco, "delayTicks", archivo),
                Int(eco, "searchRadiusThousandths", archivo),
                Int(sobrepen, "capPercentOfDamage", archivo),
                Int(sobrepen, "radiusThousandths", archivo),
                Int(Obj(comp, "opportunist", archivo), "hpThresholdPercent", archivo),
                Int(Obj(comp, "firstStrike", archivo), "hpThresholdPercent", archivo),
                Int(Obj(comp, "charge", archivo), "idleTicks", archivo),
                Int(escalaAlcance, "portionCap", archivo),
                Int(escalaAlcance, "portionScale", archivo),
                Int(Obj(comp, "bloodthirst", archivo), "bonusCap", archivo),
                Int(drenaje, "cooldownTicks", archivo),
                Int(drenaje, "maxHealingPerWave", archivo)),

            new UniqueRules(
                Int(unicos, "frostWaveMaxSecondaries", archivo),
                Int(unicos, "frostWaveSlowTicks", archivo),
                Int(unicos, "perforationRadiusThousandths", archivo),
                Int(unicos, "invisibilityMaxGoldPerEnemy", archivo),
                Int(unicos, "invisibilityMaxGoldPerWave", archivo),
                Int(unicos, "terribleHungerRootTicks", archivo),
                Int(unicos, "strengthMaxPushPerEnemy", archivo)),

            new WeaponRules(
                Int(arma, "maxSplashSecondaries", archivo),
                Int(arma, "bounceBounces", archivo),
                Int(arma, "maxChainSecondaries", archivo),
                Int(arma, "thirstBonusCap", archivo),
                Int(arma, "rangePortionCap", archivo),
                Int(arma, "rangePortionScale", archivo),
                Int(arma, "executeThresholdTroop", archivo),
                Int(arma, "executeThresholdBoss", archivo),
                Bool(arma, "finalBossImmuneToExecute", true),
                Int(arma, "vampirismCooldownTicks", archivo),
                Int(arma, "greedMarkDurationTicks", archivo),
                Int(arma, "doubleShotPercent", archivo),
                Int(arma, "chainSecondaryCapPercent", archivo),
                Int(arma, "greedMaxExtraPerEnemy", archivo),
                Int(arma, "greedMaxExtraPerWave", archivo),
                Int(arma, "vampirismMaxHealingPerWave", archivo)),

            new EnemyTraits(
                Int(rasgos, "enrageHpPercent", archivo),
                Int(rasgos, "enrageSpeedPercent", archivo),
                Int(rasgos, "enrageTicks", archivo),
                Int(rasgos, "finalBossEnrageSpeedPercent", archivo),
                Int(rasgos, "impulsiveHpPercent", archivo),
                Int(rasgos, "impulsiveSpeedPercent", archivo),
                Int(rasgos, "leaderSpeedPercent", archivo),
                Int(rasgos, "leaderRadiusThousandths", archivo)));

        // ⚠ Un slow del 100% no es un enemigo lento: es un enemigo parado, y un
        // enemigo parado fuera del alcance de todos clava la oleada para siempre.
        // El control duro que SÍ para —Raíces— es otra cosa: dura poco, deja
        // inmunidad al salir y tiene tope de veces por bicho.
        if (combate.States.SlowCapPercent is < 0 or > 99)
            throw new InvalidBalanceDataException($"{archivo}: «capPercent» del slow tiene que estar entre 0 y 99");

        if (combate.States.SlowDurationTicks < 1)
            throw new InvalidBalanceDataException($"{archivo}: el slow tiene que durar 1 tick o más. Si dura 0 no hace nada; si no vence, es permanente.");

        // ⚠ Sin período refractario, varias fuentes de Raíces son perma-stun y la
        // dificultad deja de existir. Sin tope por enemigo, tampoco alcanza: seis
        // raíces seguidas ya son cuatro segundos parado.
        if (combate.States.RootImmunityTicks <= combate.States.RootTicks)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: la inmunidad a raíz ({combate.States.RootImmunityTicks} ticks) tiene que ser MAYOR " +
                $"que la raíz ({combate.States.RootTicks}). Si no, se puede encadenar sin fin.");
        }

        if (combate.States.RootMaxPerEnemy < 1 || combate.States.PushMaxPerEnemy < 1)
            throw new InvalidBalanceDataException($"{archivo}: los topes por enemigo de raíz y empuje tienen que ser 1 o más");

        // Los DoT guardan presupuesto: sin techo de duración, uno muy cargado
        // sigue cobrando media corrida después del golpe que lo puso.
        if (combate.States.DotMaxTicks < combate.States.DotTicks || combate.States.DotTicks < 1)
            throw new InvalidBalanceDataException($"{archivo}: la duración del DoT tiene que ser 1 o más, y el techo no puede ser menor");

        foreach (var (nombre, cada) in new (string, int)[]
        {
            ("criticalHit", combate.Behaviors.HitsPerCritical),
            ("sunder", combate.Behaviors.SunderEveryHits),
            ("root", combate.Behaviors.RootEveryHits),
            ("seismic", combate.Behaviors.SeismicEveryHits),
            ("inspire", combate.Behaviors.InspireEveryHits),
            ("arcaneEcho", combate.Behaviors.EchoEveryHits),
        })
        {
            if (cada < 1)
                throw new InvalidBalanceDataException($"{archivo}: «{nombre}.everyHits» tiene que ser 1 o más");
        }

        if (combate.Behaviors.FocusMaxStacks < 1)
            throw new InvalidBalanceDataException($"{archivo}: «focus.maxStacks» tiene que ser 1 o más");

        if (combate.Enemy.DamageAverageDivisor == 0)
            throw new InvalidBalanceDataException($"{archivo}: ningún divisor puede ser 0");

        if (combate.Weapons.RangePortionScale == 0)
            throw new InvalidBalanceDataException($"{archivo}: «rangePortionScale» no puede ser 0");

        // ⚠ Un segundo disparo del 100% es duplicar el daño directo, y a ese
        // precio ninguna otra habilidad compite por la misma ranura. Arriba de
        // 100 sería directamente mejor que dos armas.
        if (combate.Weapons.DoubleShotPercent is < 1 or > 100)
        {
            throw new InvalidBalanceDataException(
                $"{archivo}: «doubleShotPercent» tiene que estar entre 1 y 100. " +
                "Con 100 el segundo golpe es entero y ninguna otra habilidad puede competir.");
        }

        // Sin techo, el daño de Cadena escala lineal con la densidad de la
        // oleada: su poder lo decidiría la formación y no el jugador.
        if (combate.Weapons.ChainSecondaryCapPercent < 1)
            throw new InvalidBalanceDataException($"{archivo}: «chainSecondaryCapPercent» tiene que ser 1 o más");

        // ⚠ Los dos topes de Codicia hacen falta juntos: sin el de oleada,
        // sesenta bichos marcados pagan sesenta veces el tope individual y la
        // build de economía vuelve a ser obligatoria.
        if (combate.Weapons.GreedMaxExtraPerEnemy < 1 || combate.Weapons.GreedMaxExtraPerWave < 1)
            throw new InvalidBalanceDataException($"{archivo}: los topes de Codicia tienen que ser 1 o más");

        if (combate.Weapons.VampirismMaxHealingPerWave < 1)
            throw new InvalidBalanceDataException($"{archivo}: «vampirismMaxHealingPerWave» tiene que ser 1 o más");

        // ⚠ Un umbral de enfurecer del 100% quiere decir «desde el primer golpe»,
        // que no es un tramo final: es el jefe entero acelerado.
        if (combate.Traits.EnrageHpPercent is < 1 or > 99)
            throw new InvalidBalanceDataException($"{archivo}: «enrageHpPercent» tiene que estar entre 1 y 99");

        if (combate.Traits.ImpulsiveHpPercent is < 1 or > 99)
            throw new InvalidBalanceDataException($"{archivo}: «impulsiveHpPercent» tiene que estar entre 1 y 99");

        if (combate.Traits.EnrageTicks < 1)
            throw new InvalidBalanceDataException($"{archivo}: «enrageTicks» tiene que ser 1 o más");

        return combate;
    }
}
