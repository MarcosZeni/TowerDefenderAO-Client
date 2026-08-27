using System.Text.Json;

namespace TowerOfArgentum.Simulacion;

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
public readonly record struct LinearCurve(int Base, int PorOleada)
{
    public int En(int oleada) => Base + (oleada * PorOleada);
}

/// <summary>
/// Una chance que sube con el presupuesto y está acotada:
/// <c>limitar(presupuesto / divisor, min, max)</c>.
/// </summary>
public readonly record struct ChanceRange(int Divisor, int Min, int Max)
{
    public int En(int presupuesto)
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
    public int En(int presupuesto)
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
    int PreparationSeconds);

public sealed record DefenderData(
    int BackpackSlots,
    int GearSlotsPerClass,
    int DefenderSellPercent,
    int WeaponSellPercent);

public sealed record EconomyData(
    int StartingGoldTokens,
    int GoldPerCalledEnemy,
    int CallGoldCap);

public sealed record BudgetData(
    int Base,
    int Cuadratico,
    int EquivalentCapMin,
    int EquivalentCapMax)
{
    /// <summary>La cuadrática, antes del tope por pool.</summary>
    public int Uncapped(int oleada) => Base + (Cuadratico * oleada * oleada);
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
    public int IntervaloSpawn(int oleada) => Ints.Clamp(
        IntervalBase - (IntervalDivisor == 0 ? 0 : oleada / IntervalDivisor),
        IntervalMin, IntervalMax);

    public int NpcPerkChance(int oleada) => Ints.Clamp(
        PerkChanceDivisor == 0 ? 0 : oleada / PerkChanceDivisor,
        PerkChanceMin, PerkChanceMax);
}

public sealed record ShopData(
    int PerksInStock,
    int WeaponsInStock,
    int BronzeChance,
    LinearCurve BronzePerkPrice,
    LinearCurve SilverPerkPrice)
{
    public int PerkPrice(PerkTier tier, int oleada)
        => tier == PerkTier.Bronze ? BronzePerkPrice.En(oleada) : SilverPerkPrice.En(oleada);
}

public sealed record DropData(int CommonChance, int BronzeChance);

public sealed record WaveData(
    int MaxNpcs,
    int CurrentAreaWeight,
    int PreviousAreaWeight,
    int HpDivisor,
    int MaxDamageDivisor,
    int SpeedDivisor,
    int DefaultSpawnInterval);

public sealed record NpcPerkData(
    int HealthCost,
    int SpeedCost,
    int HealthStep,
    int SpeedStep,
    int MaxHealth,
    int MaxSpeed,
    int HealthChanceInRoll,
    int SpeedChanceInLeftover);

public sealed record BossData(int ExtraHealthDivisor, int MaxExtraHealth, string FinalBossName);

/// <summary>Todo lo de <c>curvas.json</c>.</summary>
public sealed record CurveData(
    TowerData Tower,
    RunData RunCurve,
    DefenderData Defenders,
    EconomyData Economy,
    BudgetData Budget,
    PerWaveCurves PorOleada,
    ShopData Shop,
    DropData Drops,
    WaveData Wave,
    NpcPerkData PerksNpc,
    BossData Boss);

// =============================================================================
//  combate.json
// =============================================================================

/// <summary>
/// Los números del tick, los que antes eran literales dentro de
/// <see cref="Run"/>.
/// </summary>
public sealed record CombatData(
    int EnemyDamageAverageDivisor,
    int EnemyMinimumDamage,
    int SlowCapPercent,
    int SlowDurationTicks,
    int RicochetJumpWindowCells,
    int SplashRadiusCells,
    int RicochetBounces,
    int PerforationBounces,
    int ApocalypseBounces,
    int MaxGoldSplashSecondaries,
    int MaxFrostWaveSecondaries,
    int MaxDivineSecondaries,
    int TerribleHungerSlow,
    int PiercingDivisor,
    int HitsPerCritical,
    int MaxWeaponSplashSecondaries,
    int WeaponBounceBounces,
    int MaxChainSecondaries,
    int ThirstBonusCap,
    int RangePortionCap,
    int RangePortionScale,
    int ExecuteThresholdTroop,
    int ExecuteThresholdBoss,
    bool FinalBossImmuneToExecute,
    int VampirismCooldownTicks,
    int GreedMarkDurationTicks);

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
        Habilidades = habilidades;

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

    public IReadOnlyList<WeaponAbilityData> Habilidades { get; }

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
        => Get(h)?.Magnitude.En(presupuesto) ?? 0;

}

// =============================================================================
//  El lector: JSON de texto -> las tablas de arriba
// =============================================================================

/// <summary>
/// Convierte el texto de los JSON de <c>Datos\</c> en las tablas que usa la
/// simulación.
/// </summary>
/// <remarks>
/// <para>⚠ Vive en <c>Simulacion\</c> y <b>no lee disco</b>: recibe strings. Leer
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
                Int(corrida, "preparationSeconds", archivo)),

            new DefenderData(
                Int(defensores, "backpackSlots", archivo),
                Int(defensores, "gearSlotsPerClass", archivo),
                Int(defensores, "defenderSellPercent", archivo),
                Int(defensores, "weaponSellPercent", archivo)),

            new EconomyData(
                Int(economia, "startingGoldTokens", archivo),
                Int(economia, "goldPerCalledEnemy", archivo),
                Int(economia, "callGoldCap", archivo)),

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
                Int(tienda, "perksInStock", archivo),
                Int(tienda, "weaponsInStock", archivo),
                Int(tienda, "bronzeChance", archivo),
                Curve(tienda, "bronzePerkPrice", archivo),
                Curve(tienda, "silverPerkPrice", archivo)),

            new DropData(
                Int(drops, "commonChance", archivo),
                Int(drops, "bronzeChance", archivo)),

            new WaveData(
                Int(oleada, "maxNpcs", archivo),
                Int(oleada, "currentAreaWeight", archivo),
                Int(oleada, "previousAreaWeight", archivo),
                Int(costoNpc, "hpDivisor", archivo),
                Int(costoNpc, "maxDamageDivisor", archivo),
                Int(costoNpc, "speedDivisor", archivo),
                Int(oleada, "defaultSpawnInterval", archivo)),

            new NpcPerkData(
                Int(perksNpc, "healthCost", archivo),
                Int(perksNpc, "speedCost", archivo),
                Int(perksNpc, "healthStep", archivo),
                Int(perksNpc, "speedStep", archivo),
                Int(perksNpc, "maxHealth", archivo),
                Int(perksNpc, "maxSpeed", archivo),
                Int(perksNpc, "healthChanceInRoll", archivo),
                Int(perksNpc, "speedChanceInLeftover", archivo)),

            new BossData(
                Int(jefe, "extraHealthDivisor", archivo),
                Int(jefe, "maxExtraHealth", archivo),
                OptionalText(jefe, "finalBossName")));

        // ⚠ Sin tope de NPCs una oleada tardía compra miles de bichos: no es más
        // difícil, es más larga. Medido: corridas clavadas contra el techo de
        // 700.000 ticks en la oleada 55.
        if (curvas.Wave.MaxNpcs < 1)
            throw new InvalidBalanceDataException($"{archivo}: «maxNpcs» tiene que ser 1 o más");

        if (curvas.Wave.HpDivisor == 0 || curvas.Wave.MaxDamageDivisor == 0 || curvas.Wave.SpeedDivisor == 0)
            throw new InvalidBalanceDataException($"{archivo}: ningún divisor de «costoNpc» puede ser 0");

        if (curvas.Boss.ExtraHealthDivisor == 0)
            throw new InvalidBalanceDataException($"{archivo}: «divisorVidaExtra» no puede ser 0");

        if (curvas.Tower.CadenceTicks < 1)
            throw new InvalidBalanceDataException($"{archivo}: la cadencia de la Tower tiene que ser 1 tick o más");

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
        var slow = Obj(raiz, "slow", archivo);
        var comp = Obj(raiz, "behaviors", archivo);
        var arma = Obj(raiz, "weaponAbilities", archivo);

        var combate = new CombatData(
            Int(enemigo, "damageAverageDivisor", archivo),
            Int(enemigo, "minimumDamage", archivo),

            Int(slow, "capPercent", archivo),
            Int(slow, "durationTicks", archivo),

            Int(comp, "ricochetJumpWindowCells", archivo),
            Int(comp, "splashRadiusCells", archivo),
            Int(comp, "ricochetBounces", archivo),
            Int(comp, "perforationBounces", archivo),
            Int(comp, "apocalypseBounces", archivo),
            Int(comp, "maxGoldSplashSecondaries", archivo),
            Int(comp, "maxFrostWaveSecondaries", archivo),
            Int(comp, "maxDivineSecondaries", archivo),
            Int(comp, "terribleHungerSlow", archivo),
            Int(comp, "piercingDivisor", archivo),
            Int(comp, "hitsPerCritical", archivo),

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
            Int(arma, "greedMarkDurationTicks", archivo));

        // ⚠ Un slow del 100% no es un enemigo lento: es un enemigo parado, y un
        // enemigo parado fuera del alcance de todos clava la oleada para siempre.
        if (combate.SlowCapPercent is < 0 or > 99)
            throw new InvalidBalanceDataException($"{archivo}: «topePorciento» del slow tiene que estar entre 0 y 99");

        if (combate.SlowDurationTicks < 1)
            throw new InvalidBalanceDataException($"{archivo}: el slow tiene que durar 1 tick o más. Si dura 0 no hace nada; si no vence, es permanente.");

        if (combate.HitsPerCritical < 1)
            throw new InvalidBalanceDataException($"{archivo}: «golpesPorCritico» tiene que ser 1 o más");

        if (combate.PiercingDivisor == 0 || combate.EnemyDamageAverageDivisor == 0)
            throw new InvalidBalanceDataException($"{archivo}: ningún divisor puede ser 0");

        if (combate.RangePortionScale == 0)
            throw new InvalidBalanceDataException($"{archivo}: «escalaPorcionAlcance» no puede ser 0");

        return combate;
    }
}
