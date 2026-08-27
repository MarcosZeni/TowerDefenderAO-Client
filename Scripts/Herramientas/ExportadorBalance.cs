using System.IO.Compression;
using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Exporta TODO el balance a un XLSX, para analizarlo con otras herramientas.
/// </summary>
/// <remarks>
/// <para><b>Los números salen de los datos vivos, no de una transcripción.</b>
/// Cada celda se calcula leyendo <see cref="CargadorDatos.Actual"/> en runtime:
/// si un número cambia en <c>Datos\*.json</c>, el próximo export lo trae solo.
/// Un documento escrito a mano se desincroniza en la primera semana — esta hoja
/// llegó a exportar un <c>RalentizarPiso -80</c> que ya no existía.</para>
///
/// <para>El XLSX se arma a mano —es un zip con XML adentro— porque el proyecto no
/// tiene ni va a tener una dependencia de Office: son ~80 líneas de escritura y
/// cero paquetes nuevos.</para>
///
/// <para>Las fórmulas y reglas que no entran en una tabla van en
/// <c>BALANCE_CONTEXT.md</c>, que se escribe aparte (a mano, en la raíz del
/// proyecto): este exportador es para los NÚMEROS.</para>
/// </remarks>
public static class ExportadorBalance
{
    public static void Correr(string destino)
    {
        if (string.IsNullOrWhiteSpace(destino))
            destino = ProjectSettings.GlobalizePath("res://../BALANCE.xlsx");

        var datos = CargadorDatos.Actual;

        var hojas = new List<(string Name, List<object[]> Filas)>
        {
            ("Classes", Classes()),
            ("Progresion", Progresion()),
            ("Enemies", Enemies(datos)),
            ("Curves", Curves(datos)),
            ("Perks", Perks()),
            ("Blueprints", HojaPlanos()),
            ("HabilidadesArma", HabilidadesArma()),
            ("GeneracionArmas", GeneracionArmas_()),
            ("Constantes", Constantes()),
            ("Matchups", Matchups(datos)),
        };

        try
        {
            EscribirXlsx(destino, hojas);
        }
        catch (System.Exception e)
        {
            GD.PushError($"No se pudo escribir {destino}: {e.Message}");
            return;
        }

        foreach (var (nombre, filas) in hojas)
            GD.Print($"  · {nombre}: {filas.Count - 1} filas");

        GD.Print($"── {destino} · {hojas.Count} hojas ──");
    }

    // -------------------------------------------------------------------------
    //  Hoja 1 — Clases (stats base, nivel 1)
    // -------------------------------------------------------------------------

    private static readonly DefenderClass[] _clases = ChoiceBuilder.All;

    private static List<object[]> Classes()
    {
        List<object[]> filas =
        [
            [
                "Name", "Id", "CostoOro", "DanoBase", "RangeCells",
                "CadenceTicks", "AtaquesPorSegundo", "DPS_nivel1",
                "DPS_por_100_oro", "GolpesParaRata20hp", "EsSoporte",
                // Columnas del template que este juego NO tiene. Vacías a
                // propósito: BALANCE_CONTEXT.md explica por qué.
                "HP", "Mana", "MinDamage", "MaxDamage", "Precision", "Evasion",
                "Defensa", "ResistFisica", "ResistMagica", "Critical",
                "MultCritico", "VelocidadMovimiento",
            ],
        ];

        foreach (var c in _clases)
        {
            var s = CargadorDatos.Actual.StatsOf(c);

            double aps = 30.0 / s.CadenceTicks;
            double dps = s.Damage * aps;

            filas.Add(
            [
                c.ToString(), (int)c, s.Cost, s.Damage, s.RangeCells,
                s.CadenceTicks, Red(aps), Red(dps),
                Red(dps * 100.0 / s.Cost),
                (19 / s.Damage) + 1,
                EsSoporte(c) ? "si" : "no",
                "", "", "", "", "", "", "", "", "", "", "", "",
            ]);
        }

        return filas;
    }

    /// <summary>Sale de la marca «soporte» de <c>clases.json</c>.</summary>
    private static bool EsSoporte(DefenderClass c) => CargadorDatos.Actual.Classes.De(c).Support;

    // -------------------------------------------------------------------------
    //  Hoja 2 — Progresión por nivel (1 a 6)
    // -------------------------------------------------------------------------

    private static List<object[]> Progresion()
    {
        List<object[]> filas =
        [
            [
                "DefenderClass", "Level", "ExtraDanoPorc", "DanoEfectivo", "DPS",
                "SlotsPerkAbiertos", "VisualTier",
            ],
        ];

        foreach (var c in _clases)
        {
            var s = CargadorDatos.Actual.StatsOf(c);

            for (int nivel = 1; nivel <= CargadorDatos.Actual.Classes.PerkSlotsPerClass; nivel++)
            {
                // La MISMA cuenta que Roster.StatsDe: (nivel-1)*8 puntos
                // porcentuales, sumados antes de multiplicar (Enteros).
                int extra = (nivel - 1) * CargadorDatos.Actual.Classes.ExtraDamagePerLevel;
                int dano = Ints.WithPercents(s.Damage, extra);

                int slots = Ints.Clamp(nivel, 1, CargadorDatos.Actual.Classes.PerkSlotsPerClass);
                int tier = nivel <= 2 ? 1 : nivel <= 4 ? 2 : 3;

                filas.Add(
                [
                    c.ToString(), nivel, extra, dano,
                    Red(dano * 30.0 / s.CadenceTicks), slots, tier,
                ]);
            }
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  Hoja 3 — Enemigos (el bestiario cargado, no el provisional)
    // -------------------------------------------------------------------------

    private static List<object[]> Enemies(BalanceData datos)
    {
        List<object[]> filas =
        [
            [
                "Id", "Name", "Area", "EsJefeDeSuArea", "Hp", "MinDamage",
                "MaxDamage", "DanoEfectivo", "VelocidadMilesimasPorTick",
                "CeldasPorSegundo", "CadenceTicks", "DPS_contraTorre",
                "OroQueSuelta", "CostoPresupuesto", "SegundosRuta29Celdas",
            ],
        ];

        var cb = datos.Combat;

        foreach (var e in datos.Enemies)
        {
            bool esJefe = datos.BossOf(e.Area)?.Id == e.Id;

            // Las mismas cuentas que la simulación, pero LLAMÁNDOLAS, no
            // transcriptas: el costo sale del mismo método que usa el armador de
            // oleadas y el daño efectivo de las mismas constantes que el tick.
            int danoEfectivo = System.Math.Max(cb.Enemy.MinimumDamage, (e.MinDamage + e.MaxDamage) / cb.Enemy.DamageAverageDivisor);
            int costo = WaveBuilder.Cost(e.Id, datos);

            double celdasSeg = e.Speed * 30.0 / 1000.0;

            filas.Add(
            [
                e.Id, e.Name, e.Area, esJefe ? "si" : "no", e.Hp, e.MinDamage,
                e.MaxDamage, danoEfectivo, e.Speed, Red(celdasSeg),
                e.CadenceTicks, Red(danoEfectivo * 30.0 / e.CadenceTicks),
                e.Gold, costo,
                celdasSeg > 0 ? Red(29.0 / celdasSeg) : 0,
            ]);
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  Hoja 4 — Curvas por oleada, las 100
    // -------------------------------------------------------------------------

    private static List<object[]> Curves(BalanceData datos)
    {
        List<object[]> filas =
        [
            [
                "Wave", "Area", "HasBoss", "WaveBudget",
                "ClearGoldBonus", "SpawnIntervalTicks", "ChancePerkNpcPorc",
                "BossDropBudget", "ShopItemBudget",
                "PrecioPerkBronceTienda", "PrecioPerkPlataTienda",
            ],
        ];

        for (int oleada = 1; oleada <= CargadorDatos.Actual.Curves.RunCurve.WavesPerRun; oleada++)
        {
            var receta = Balance.RecipeOf(oleada, datos);

            filas.Add(
            [
                oleada, CargadorDatos.Actual.Areas.OfWave(oleada), CargadorDatos.Actual.Areas.HasBoss(oleada) ? "si" : "no",
                receta.Budget, receta.BonusOro, receta.SpawnIntervalTicks,
                receta.NpcPerkChance,
                CargadorDatos.Actual.Curves.PorOleada.BossDropBudget.En(oleada),
                CargadorDatos.Actual.Curves.PorOleada.ShopItemBudget.En(oleada),
                80 + (oleada * 6), 240 + (oleada * 18),
            ]);
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  Hoja 5 — El catálogo de perks completo
    // -------------------------------------------------------------------------

    private static List<object[]> Perks()
    {
        List<object[]> filas =
        [
            [
                "Id", "Name", "Tier", "Effect", "MagnitudPorc", "MaxStacked",
                "Classes", "Fuente",
            ],
        ];

        var cd = CargadorDatos.Actual.Curves;

        foreach (var p in CargadorDatos.Actual.Perks.All)
        {
            string fuente = p.Tier switch
            {
                PerkTier.Bronze => $"drop comun {cd.Drops.BronzeChance}% / tienda {cd.Shop.BronzeChance}%",
                PerkTier.Silver => $"drop comun {100 - cd.Drops.BronzeChance}% / tienda {100 - cd.Shop.BronzeChance}%",
                PerkTier.Gold => $"SOLO el jefe de area ({cd.RunCurve.AreaCount} por corrida)",
                _ => "SOLO fusion de dos Gold + plano",
            };

            filas.Add(
            [
                p.Id, p.Key, p.Tier.ToString(), p.Effect.ToString(),
                p.Magnitude, p.MaxStacked, ListaClases(p.Classes), fuente,
            ]);
        }

        return filas;
    }

    private static string ListaClases(DefenderClass[] clases)
    {
        if (clases.Length == 0)
            return "todas";

        var sb = new System.Text.StringBuilder();

        foreach (var c in clases)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(c);
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    //  Hoja 6 — Planos de fusión
    // -------------------------------------------------------------------------

    private static List<object[]> HojaPlanos()
    {
        List<object[]> filas =
        [
            ["Name", "GoldA", "GoldB", "ResultingUnique", "ConocidoDeArranque"],
        ];

        foreach (var p in CargadorDatos.Actual.Blueprints.All)
        {
            filas.Add(
            [
                p.Key,
                p.GoldA >= 0 ? CargadorDatos.Actual.Perks.Get(p.GoldA).Key : "?",
                p.GoldB >= 0 ? CargadorDatos.Actual.Perks.Get(p.GoldB).Key : "?",
                p.ResultingUnique >= 0 ? CargadorDatos.Actual.Perks.Get(p.ResultingUnique).Key : "?",
                p.Initial ? "si" : "no",
            ]);
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  Hoja 7 — Habilidades de arma
    // -------------------------------------------------------------------------

    private static List<object[]> HabilidadesArma()
    {
        List<object[]> filas =
        [
            [
                "Ability", "Pool", "MagnitudFormula", "MagnitudMin",
                "MagnitudMax", "QueHace",
            ],
        ];

        // ⚠ Sale de la tabla viva. Antes estaba transcripta 1:1 y ya mentía en
        // dos filas: Doble figuraba en el pool épico cuando había pasado a
        // legendaria, y Cadena/Codicia tenían los topes viejos (70 y 200).
        var tabla = CargadorDatos.Actual.Weapons;

        foreach (var h in tabla.Habilidades)
        {
            var m = h.Magnitude;

            string formula = m.Divisor == 0
                ? $"{m.Base} fijo"
                : $"{m.Base} + p/{m.Divisor}";

            filas.Add(
            [
                h.Ability.ToString(),
                PoolOf(h.Ability, tabla),
                formula, m.Min, m.Max,
                Loc.Get($"ability.{h.Ability}.text"),
            ]);
        }

        return filas;
    }

    private static string PoolOf(WeaponAbility h, WeaponTable tabla)
    {
        if (System.Array.IndexOf(tabla.BreakerPool, h) >= 0) return "rompe (legendaria)";
        if (System.Array.IndexOf(tabla.RarePool, h) >= 0) return "rara (epica)";
        if (System.Array.IndexOf(tabla.CommonPool, h) >= 0) return "comun";

        return "ninguno (no la tira nadie)";
    }

    // -------------------------------------------------------------------------
    //  Hoja 8 — Generación de armas (el único "equipamiento" del juego)
    // -------------------------------------------------------------------------

    private static List<object[]> GeneracionArmas_()
    {
        var a = CargadorDatos.Actual.Weapons;
        var c = CargadorDatos.Actual.Curves;

        string Chance(ChanceRange r) => $"clamp(presupuesto/{r.Divisor}, {r.Min}, {r.Max})%";

        return
        [
            ["Parametro", "Value", "Notas"],
            ["PasoAfijo", a.Step, "los numeros se compran de a esto, para que queden redondos"],
            ["CostoPuntoDano", a.DamageCost, $"1 punto porcentual de daño cuesta esto (se compra de a {a.Step})"],
            ["CostoPuntoCadencia", a.CadenceCost, "idem"],
            ["CostoPuntoAlcance", a.RangeCost, "el mas caro: el alcance es el stat mas valioso de un TD"],
            ["LegendaryChance", Chance(a.LegendaryChance), "tiradas ENCADENADAS, no exclusivas"],
            ["EpicChance", Chance(a.EpicChance), "solo si fallo la legendaria"],
            ["RareChance", Chance(a.RareChance), "solo si fallaron las dos de arriba"],
            ["PresupuestoAHabilidad_Legendaria", $"{a.LegendaryPercent}%", "se aparta ANTES de comprar numeros"],
            ["PresupuestoAHabilidad_Epica", $"{a.EpicPercent}%", ""],
            ["PresupuestoAHabilidad_Rara", $"{a.RarePercent}%", ""],
            ["PresupuestoAHabilidad_Comun", $"{a.CommonPercent}%", "las comunes no tienen habilidad"],
            ["ValorDeVenta", $"presupuesto/{a.ValueDivisor} * (1 + rareza)", "rareza: comun 0, rara 1, epica 2, legendaria 3"],
            ["PrecioEnTienda", $"Value * {a.ShopPriceMultiplier}", ""],
            ["BossDropBudget", Recta(c.PorOleada.BossDropBudget), "n = oleada"],
            ["ShopItemBudget", Recta(c.PorOleada.ShopItemBudget), "n = oleada"],
            ["StockTienda", $"{c.Shop.PerksInStock} perks + {c.Shop.WeaponsInStock} armas", "se renueva por oleada, no se acumula"],
            ["CommonPool", string.Join(", ", a.CommonPool), ""],
            ["RarePool", string.Join(", ", a.RarePool), "solo epicas"],
            ["BreakerPool", string.Join(", ", a.BreakerPool), "solo legendarias"],
            ["ClasesDelArma", 1, "una sola, sorteada del roster: un arma para una clase que no tenes es mochila tirada"],
            ["TiendaVendeOro", "NO", "el jefe es la unica fuente de perks Gold"],
            ["ArmasSonPorcentuales", "si", "Damage/Cadence/Range del arma son puntos % sobre la base de la clase"],
        ];
    }

    // -------------------------------------------------------------------------
    //  Hoja 9 — Constantes sueltas, con su archivo
    // -------------------------------------------------------------------------

    /// <remarks>
    /// ⚠ <b>Cada fila sale del dato vivo, ninguna está escrita a mano.</b> La
    /// versión anterior de esta hoja transcribía los números y se desincronizó en
    /// una semana: llegó a exportar un <c>RalentizarPiso -80</c> que ya no existía
    /// y un techo de perk de velocidad de 120 cuando el real era 64. La columna
    /// «Archivo» apunta al JSON que hay que editar, no al <c>.cs</c>.
    /// </remarks>
    private static List<object[]> Constantes()
    {
        var d = CargadorDatos.Actual;
        var c = d.Curves;
        var k = d.Combat;

        return
        [
            ["Constante", "Value", "Archivo", "Notas"],

            ["TicksPerSecond", Ints.TicksPerSecond, "Ints.cs", "paso fijo, congelado; NO es configurable"],
            ["ThousandthsPerCell", Ints.ThousandthsPerCell, "Ints.cs", "unidad de toda distancia de ruta"],
            ["DatosVersion", d.Version, "enemigos.json", "la temporada del ranking"],
            ["DatosHash", d.Hash, "todos", "cubre bestiario + 6 tablas + layouts"],

            // --- La Torre ---
            ["TorreHpInicial", c.Tower.HpInicial, "curvas.json", ""],
            ["TorreDano", c.Tower.Damage, "curvas.json", "al MAS VIEJO de los que la muerden, uno por turno"],
            ["TorreCadenciaTicks", c.Tower.CadenceTicks, "curvas.json", "el cooldown solo corre si hay a quien pegarle"],

            // --- La corrida ---
            ["WavesPerRun", c.RunCurve.WavesPerRun, "curvas.json", "TERMINA ahi, sin modo sin fin"],
            ["AreaCount", c.RunCurve.AreaCount, "curvas.json", ""],
            ["LastWavePerArea", string.Join(",", c.RunCurve.LastWavePerArea), "curvas.json", "cada una trae jefe"],
            ["WavesBetweenChoices", c.RunCurve.WavesBetweenChoices, "curvas.json", ""],
            ["PreparationSeconds", c.RunCurve.PreparationSeconds, "curvas.json", "reloj de pared, no ticks de simulacion"],
            ["FinalBossName", c.Boss.FinalBossName, "curvas.json", "el jefe del area final se elige por nombre, no por HP"],

            // --- Defensores ---
            ["PerkSlotsPerClass", d.Classes.PerkSlotsPerClass, "clases.json", "tambien es el nivel maximo"],
            ["ExtraDamagePerLevel", d.Classes.ExtraDamagePerLevel, "clases.json", "puntos porcentuales por nivel arriba de 1"],
            ["BackpackSlots", c.Defenders.BackpackSlots, "curvas.json", "compartidos perks + armas"],
            ["GearSlotsPerClass", c.Defenders.GearSlotsPerClass, "curvas.json", "solo el arma"],
            ["DefenderSellPercent", c.Defenders.DefenderSellPercent, "curvas.json", "vender defensor devuelve esto del costo"],
            ["WeaponSellPercent", c.Defenders.WeaponSellPercent, "curvas.json", "vender arma de la mochila devuelve esto del Value"],

            // --- Economia ---
            ["StartingGoldTokens", c.Economy.StartingGoldTokens, "curvas.json", "oro inicial = esto x costo de la clase que toco primero"],
            ["GoldPerCalledEnemy", c.Economy.GoldPerCalledEnemy, "curvas.json", ""],
            ["CallGoldCap", c.Economy.CallGoldCap, "curvas.json", "por llamada"],

            // --- Presupuesto de oleada ---
            ["PresupuestoFormula", $"{c.Budget.Base} + {c.Budget.Cuadratico}*n*n", "curvas.json", "antes del tope por pool"],
            ["EquivalentCapMin", c.Budget.EquivalentCapMin, "curvas.json", "bichos promedio del pool al empezar el area"],
            ["EquivalentCapMax", c.Budget.EquivalentCapMax, "curvas.json", "al terminarla; sube lineal"],
            ["MaxNpcsPerWave", c.Wave.MaxNpcs, "curvas.json", "el presupuesto sobrante va a perks de NPC"],
            ["CurrentAreaWeight", c.Wave.CurrentAreaWeight, "curvas.json", "peso de sorteo por bicho"],
            ["PreviousAreaWeight", c.Wave.PreviousAreaWeight, "curvas.json", ""],
            ["PreviousAreaWeightFirst", c.Wave.PreviousAreaWeightFirst, "curvas.json", "peso en la PRIMERA oleada del area; se desvanece hasta PreviousAreaWeight"],
            ["PreviousAreaFadeWaves", c.Wave.PreviousAreaFadeWaves, "curvas.json", "en cuantas oleadas cae"],
            ["PrecioNivelFormula", $"{c.Economy.LevelUpPriceBase} * nivelDestino^2", "curvas.json", "el sumidero de oro de la corrida; solo sube clases que ya estan"],
            ["CostoNpcFormula",
                $"hp/{c.Wave.HpDivisor} + danoMax/{c.Wave.MaxDamageDivisor} + velocidad/{c.Wave.SpeedDivisor}",
                "curvas.json", "entero, trunca"],

            // --- Curvas por oleada ---
            ["BonusOroFormula", Recta(c.PorOleada.ClearGoldBonus), "curvas.json", "por limpiar la oleada"],
            ["PresupuestoDropJefeFormula", Recta(c.PorOleada.BossDropBudget), "curvas.json", "LINEAL contra la cuadratica de la oleada, a proposito"],
            ["PresupuestoItemTiendaFormula", Recta(c.PorOleada.ShopItemBudget), "curvas.json", "mas chico que el del jefe"],
            ["IntervaloSpawnFormula",
                $"clamp({c.PorOleada.IntervalBase} - n/{c.PorOleada.IntervalDivisor}, {c.PorOleada.IntervalMin}, {c.PorOleada.IntervalMax}) ticks",
                "curvas.json", ""],
            ["ChancePerkNpcFormula",
                $"clamp(n/{c.PorOleada.PerkChanceDivisor}, {c.PorOleada.PerkChanceMin}, {c.PorOleada.PerkChanceMax}) %",
                "curvas.json", ""],

            // --- Perks de NPC ---
            ["CostoPerkVidaNpc", c.PerksNpc.HealthCost, "curvas.json", "del presupuesto de la oleada"],
            ["CostoPerkVelocidadNpc", c.PerksNpc.SpeedCost, "curvas.json", ""],
            ["EscalonPerkVidaNpc", c.PerksNpc.HealthStep, "curvas.json", "puntos porcentuales de HP por escalon"],
            ["EscalonPerkVelocidadNpc", c.PerksNpc.SpeedStep, "curvas.json", ""],
            ["MaxPerkVidaNpc", c.PerksNpc.MaxHealth, "curvas.json", "techo por NPC"],
            ["MaxPerkVelocidadNpc", c.PerksNpc.MaxSpeed, "curvas.json", "un bicho que vuela no se puede matar"],
            ["ChanceVidaEnLaTiradaNpc", c.PerksNpc.HealthChanceInRoll, "curvas.json", "% que va a vida; el resto a velocidad"],
            ["ChanceVelocidadEnSobranteNpc", c.PerksNpc.SpeedChanceInLeftover, "curvas.json", "al repartir el presupuesto no gastado"],
            ["VidaExtraJefe",
                $"min(sobrante/{c.Boss.ExtraHealthDivisor}, {c.Boss.MaxExtraHealth}) en % de HP",
                "curvas.json", "CON TECHO: el jefe es fuerte por diseno, no por residuo"],

            // --- Drops y tienda ---
            ["ChanceDropComun", c.Drops.CommonChance, "curvas.json", "%; no sube con la oleada, sube la CALIDAD"],
            ["ChanceBronceEnDrop", c.Drops.BronzeChance, "curvas.json", "% bronce; el resto plata"],
            ["ChanceBronceEnTienda", c.Shop.BronzeChance, "curvas.json", ""],
            ["PerksInStock", c.Shop.PerksInStock, "curvas.json", "no se acumula entre oleadas"],
            ["WeaponsInStock", c.Shop.WeaponsInStock, "curvas.json", ""],
            ["PrecioPerkBronceFormula", Recta(c.Shop.BronzePerkPrice), "curvas.json", ""],
            ["PrecioPerkPlataFormula", Recta(c.Shop.SilverPerkPrice), "curvas.json", "el triple: se paga el SLOT, no solo el numero"],

            // --- La escala interna ---
            ["EscalaDanoYHp", Ints.HundredthsPerPoint, "Ints.HundredthsPerPoint", "centesimas de punto; los JSON van en puntos enteros"],
            ["EscalaAlcance", Ints.ThousandthsPerCell, "Ints.ThousandthsPerCell", "milesimas de celda; sin esto los perks de alcance truncaban a cero"],

            // --- El tick ---
            ["DanoEnemigoFormula",
                $"max({k.Enemy.MinimumDamage}, (danoMin + danoMax)/{k.Enemy.DamageAverageDivisor})",
                "combate.json", "promedio, no tirada: una tirada por golpe correría el RNG"],

            // --- Estados (Estados_v2) ---
            ["SlowTope", k.States.SlowCapPercent, "combate.json", "% max; no apila, gana el mas fuerte"],
            ["SlowDuracionTicks", k.States.SlowDurationTicks, "combate.json", "TEMPORAL; cada aplicacion refresca"],
            ["SlowDuracionMaxTicks", k.States.SlowMaxDurationTicks, "combate.json", "techo del refresco acumulado"],
            ["VulnerableTicks", k.States.VulnerableTicks, "combate.json", "+% dano DIRECTO recibido; no apila"],
            ["DebilidadTicks", k.States.WeakenTicks, "combate.json", "-% dano a la Torre; no sube el DPS de nadie"],
            ["MarcaCazaTicks", k.States.HuntMarkTicks, "combate.json", "la clase que marca NO cobra el bonus"],
            ["RaizTicks", k.States.RootTicks, "combate.json", "unico control DURO del juego"],
            ["RaizInmunidadTicks", k.States.RootImmunityTicks, "combate.json", "periodo refractario: sin esto es perma-stun"],
            ["RaizMaxPorEnemigo", k.States.RootMaxPerEnemy, "combate.json", "tope de veces por bicho"],
            ["EmpujeMaxPorEnemigo", k.States.PushMaxPerEnemy, "combate.json", "presupuesto finito; nunca detras del spawn"],
            ["AuraTicks", k.States.InspireTicks, "combate.json", "no apila: 3 bardos siguen dando +10%"],
            ["AuraRadioCeldas", k.States.InspireRadiusCells, "combate.json", "no afecta al emisor"],
            ["BotinTicks", k.States.LootTicks, "combate.json", ""],
            ["BotinMaxPorEnemigo", k.States.LootMaxGoldPerEnemy, "combate.json", "oro, no %"],
            ["BotinMaxPorOleada", k.States.LootMaxGoldPerWave, "combate.json", "el segundo techo: sin el, 60 bichos pagan 60 veces"],
            ["DotTicks", k.States.DotTicks, "combate.json", "presupuesto: conserva Potencia x Duracion al mezclar"],
            ["DotTicksMax", k.States.DotMaxTicks, "combate.json", "si no entra, SUBE el ritmo en vez de tirar dano"],

            // --- Resistencia de jefe ---
            ["JefePotenciaPorciento", k.Boss.PowerPercent, "combate.json", "el control SIGUE SIRVIENDO: vale la mitad, no cero"],
            ["JefeControlDuroPorciento", k.Boss.HardCcDurationPercent, "combate.json", "% de la duracion de la raiz"],
            ["JefeEmpujePorciento", k.Boss.PushPercent, "combate.json", ""],
            ["JefeMaxEmpujes", k.Boss.MaxPushPerBoss, "combate.json", ""],
            ["JefeFinalPotenciaPorciento", k.Boss.FinalBossPowerPercent, "combate.json", "el Dragon es un escalon aparte"],
            ["JefeFinalInmuneControlDuro", k.Boss.FinalBossImmuneToHardCc ? "si" : "no", "combate.json", "matarlo es como se gana la corrida"],
            ["JefeFinalInmuneEmpuje", k.Boss.FinalBossImmuneToPush ? "si" : "no", "combate.json", ""],

            // --- Comportamientos ---
            ["VentanaSaltoRicochet", $"{k.Behaviors.RicochetWindowThousandths} milesimas DE RUTA", "combate.json", "del objetivo ANTERIOR, no del defensor"],
            ["RicochetBounces", k.Behaviors.RicochetBounces, "combate.json", "saltos extra ademas del golpe directo"],
            ["CadenaMaxSaltos", k.Behaviors.ChainMaxJumps, "combate.json", "cada salto pega menos que el anterior"],
            ["CadenaVentana", $"{k.Behaviors.ChainWindowThousandths} milesimas DE RUTA", "combate.json", ""],
            ["RadioSplash", $"{k.Behaviors.SplashRadiusThousandths} milesimas DE RUTA", "combate.json", "distancia recorrida, no euclidea"],
            ["SplashMaxSecundarios", k.Behaviors.SplashMaxSecondaries, "combate.json", ""],
            ["SplashTechoTotal", $"{k.Behaviors.SplashTotalCapPercent}% del golpe", "combate.json", "el segundo techo: acota el dano SUMADO"],
            ["QuemaduraMaxSecundarios", k.Behaviors.BurnMaxSecondaries, "combate.json", ""],
            ["QuemaduraRadio", $"{k.Behaviors.BurnRadiusThousandths} milesimas", "combate.json", ""],
            ["CriticoCadaN", k.Behaviors.HitsPerCritical, "combate.json", "contador POR DEFENSOR, no Tick % N"],
            ["QuebrantoCadaN", k.Behaviors.SunderEveryHits, "combate.json", ""],
            ["RaicesCadaN", k.Behaviors.RootEveryHits, "combate.json", ""],
            ["SismicoCadaN", k.Behaviors.SeismicEveryHits, "combate.json", ""],
            ["InspiracionCadaN", k.Behaviors.InspireEveryHits, "combate.json", ""],
            ["EcoCadaN", k.Behaviors.EchoEveryHits, "combate.json", ""],
            ["EcoRetrasoTicks", k.Behaviors.EchoDelayTicks, "combate.json", "si el objetivo murio, busca un vecino de ruta"],
            ["EcoRadioBusqueda", $"{k.Behaviors.EchoSearchThousandths} milesimas", "combate.json", ""],
            ["FocoMaxStacks", k.Behaviors.FocusMaxStacks, "combate.json", "golpes seguidos al MISMO objetivo"],
            ["FocoReinicioTicks", k.Behaviors.FocusResetTicks, "combate.json", "se pierde al cambiar de objetivo"],
            ["SobrepenetracionTecho", $"{k.Behaviors.OverpenCapPercent}% del golpe", "combate.json", "una sola transferencia; no encadena"],
            ["SobrepenetracionRadio", $"{k.Behaviors.OverpenRadiusThousandths} milesimas", "combate.json", ""],
            ["OportunistaUmbral", $"{k.Behaviors.OpportunistThresholdPercent}% HP o menos", "combate.json", "remata; NO ejecuta"],
            ["PrimerImpactoUmbral", $"mas de {k.Behaviors.FirstStrikeThresholdPercent}% HP", "combate.json", "el opuesto de Oportunista"],
            ["PreparacionTicksSinObjetivo", k.Behaviors.ChargeIdleTicks, "combate.json", "una sola carga guardada"],

            // --- Unicos ---
            ["OndaDeFrioMaxSecundarios", k.Uniques.FrostWaveMaxSecondaries, "combate.json", ""],
            ["OndaDeFrioSlowTicks", k.Uniques.FrostWaveSlowTicks, "combate.json", "mas corto que Escarcha suelta"],
            ["PerforacionRadio", $"{k.Uniques.PerforationRadiusThousandths} milesimas", "combate.json", ""],
            ["InvisibilidadMaxOroPorEnemigo", k.Uniques.InvisibilityMaxGoldPerEnemy, "combate.json", "mas bajo que Saqueo suelto"],
            ["InvisibilidadMaxOroPorOleada", k.Uniques.InvisibilityMaxGoldPerWave, "combate.json", ""],
            ["TerribleHambreRaizTicks", k.Uniques.TerribleHungerRootTicks, "combate.json", "mas corta que Raices suelta"],
            ["FuerzaMaxEmpujes", k.Uniques.StrengthMaxPushPerEnemy, "combate.json", ""],

            // --- Habilidades de arma ---
            ["MaxWeaponSplashSecondaries", k.Weapons.MaxSplashSecondaries, "combate.json", ""],
            ["WeaponBounceBounces", k.Weapons.BounceBounces, "combate.json", ""],
            ["MaxChainSecondaries", k.Weapons.MaxChainSecondaries, "combate.json", ""],
            ["SedTecho", k.Weapons.ThirstBonusCap, "combate.json", "% max ACUMULADO por oleada; se reinicia al terminarla"],
            ["ExecuteThresholdTroop", k.Weapons.ExecuteThresholdTroop, "combate.json", "TECHO: el umbral real es min(magnitud, esto)"],
            ["ExecuteThresholdBoss", k.Weapons.ExecuteThresholdBoss, "combate.json", ""],
            ["FinalBossImmuneToExecute", k.Weapons.FinalBossImmuneToExecute ? "si" : "no", "combate.json", "matarlo es como se gana la corrida"],
            ["VampirismCooldownTicks", k.Weapons.VampirismCooldownTicks, "combate.json", "un proc por CLASE, no por defensor"],
            ["GreedMarkDurationTicks", k.Weapons.GreedMarkDurationTicks, "combate.json", "se cobra al morir, no al golpear"],

            // --- Reglas que no son un numero ---
            ["AlcanceAlCuadrado", "si", "todo el combate", "sin raices; alcance 1 NO cubre diagonales"],
            ["SecundariosNoProccean", "si", "DamageSource", "splash/cadena/eco/DoT/overkill solo restan vida"],
            ["UnaSolaRanuraDeComportamiento", "si", "Run.EquipPerk", "la mochila rebota el segundo Oro; el arma tapa al perk"],
        ];
    }

    private static string Recta(LinearCurve c) => $"{c.Base} + {c.PorOleada}*n";

    // -------------------------------------------------------------------------
    //  Hoja 10 — Matchups: clase (nivel 1 y 6, sin perks) contra cada enemigo
    // -------------------------------------------------------------------------

    private static List<object[]> Matchups(BalanceData datos)
    {
        List<object[]> filas =
        [
            [
                "DefenderClass", "Enemy", "AreaEnemigo", "HpEnemigo",
                "GolpesParaMatar_nv1", "SegundosParaMatar_nv1",
                "GolpesParaMatar_nv6", "SegundosParaMatar_nv6",
                "SegundosEnAlcance_aprox", "LoMataSolo_nv1",
            ],
        ];

        foreach (var c in _clases)
        {
            var s = CargadorDatos.Actual.StatsOf(c);
            int danoNv6 = Ints.WithPercents(s.Damage, (CargadorDatos.Actual.Classes.PerkSlotsPerClass - 1) * CargadorDatos.Actual.Classes.ExtraDamagePerLevel);

            foreach (var e in datos.Enemies)
            {
                int golpes1 = ((e.Hp - 1) / s.Damage) + 1;
                int golpes6 = ((e.Hp - 1) / danoNv6) + 1;

                // Ticks del primer golpe al último: (golpes-1) * cadencia. El
                // primero sale con cooldown en cero.
                double seg1 = (golpes1 - 1) * s.CadenceTicks / 30.0;
                double seg6 = (golpes6 - 1) * s.CadenceTicks / 30.0;

                // Cuánto tiempo pasa el enemigo dentro del alcance: la cuerda del
                // círculo sobre una ruta recta que pasa a 1 celda del defensor,
                // aproximada como 2*alcance celdas. Es una COTA, no una medición:
                // la ruta serpentea y el defensor bien puesto cubre varias pasadas.
                double celdasSeg = e.Speed * 30.0 / 1000.0;
                double segAlcance = celdasSeg > 0 ? 2.0 * s.RangeCells / celdasSeg : 0;

                filas.Add(
                [
                    c.ToString(), e.Name, e.Area, e.Hp,
                    golpes1, Red(seg1), golpes6, Red(seg6),
                    Red(segAlcance), seg1 <= segAlcance ? "si" : "no",
                ]);
            }
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  El XLSX: un zip con XML adentro, sin dependencias
    // -------------------------------------------------------------------------

    private static double Red(double v) => System.Math.Round(v, 2);

    private static void EscribirXlsx(string ruta, List<(string Name, List<object[]> Filas)> hojas)
    {
        string? carpeta = Path.GetDirectoryName(ruta);

        if (!string.IsNullOrEmpty(carpeta))
            Directory.CreateDirectory(carpeta);

        using var archivo = new FileStream(ruta, System.IO.FileMode.Create);
        using var zip = new ZipArchive(archivo, ZipArchiveMode.Create);

        var tipos = new System.Text.StringBuilder();
        tipos.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        tipos.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        tipos.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        tipos.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");

        for (int i = 1; i <= hojas.Count; i++)
        {
            tipos.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        tipos.Append("</Types>");
        Entrada(zip, "[Content_Types].xml", tipos.ToString());

        Entrada(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        var libro = new System.Text.StringBuilder();
        libro.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        libro.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");

        var rels = new System.Text.StringBuilder();
        rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

        for (int i = 1; i <= hojas.Count; i++)
        {
            libro.Append($"<sheet name=\"{Xml(hojas[i - 1].Name)}\" sheetId=\"{i}\" r:id=\"rId{i}\"/>");
            rels.Append($"<Relationship Id=\"rId{i}\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" " +
                $"Target=\"worksheets/sheet{i}.xml\"/>");
        }

        libro.Append("</sheets></workbook>");
        rels.Append("</Relationships>");

        Entrada(zip, "xl/workbook.xml", libro.ToString());
        Entrada(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

        for (int i = 1; i <= hojas.Count; i++)
            Entrada(zip, $"xl/worksheets/sheet{i}.xml", Hoja(hojas[i - 1].Filas));
    }

    private static string Hoja(List<object[]> filas)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        for (int f = 0; f < filas.Count; f++)
        {
            sb.Append($"<row r=\"{f + 1}\">");

            for (int c = 0; c < filas[f].Length; c++)
            {
                string celda = $"{Columna(c)}{f + 1}";
                object v = filas[f][c];

                // Números como números —para que la otra herramienta pueda
                // operar—, todo lo demás como texto en línea.
                if (v is int or long or double)
                {
                    string n = System.Convert.ToString(
                        v, System.Globalization.CultureInfo.InvariantCulture) ?? "0";
                    sb.Append($"<c r=\"{celda}\"><v>{n}</v></c>");
                }
                else
                {
                    string s = v?.ToString() ?? "";

                    if (s.Length == 0)
                        continue;

                    sb.Append($"<c r=\"{celda}\" t=\"inlineStr\"><is><t>{Xml(s)}</t></is></c>");
                }
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    /// <summary>A, B, … Z, AA, AB, …</summary>
    private static string Columna(int indice)
    {
        string s = "";

        indice++;
        while (indice > 0)
        {
            indice--;
            s = (char)('A' + (indice % 26)) + s;
            indice /= 26;
        }

        return s;
    }

    private static string Xml(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void Entrada(ZipArchive zip, string nombre, string contenido)
    {
        var entrada = zip.CreateEntry(nombre, CompressionLevel.Optimal);
        using var w = new StreamWriter(entrada.Open(), new System.Text.UTF8Encoding(false));
        w.Write(contenido);
    }
}
