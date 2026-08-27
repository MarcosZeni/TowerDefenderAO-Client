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

        var hojas = new List<(string Nombre, List<object[]> Filas)>
        {
            ("Clases", Clases()),
            ("Progresion", Progresion()),
            ("Enemigos", Enemigos(datos)),
            ("Curvas", Curvas(datos)),
            ("Perks", Perks()),
            ("Planos", HojaPlanos()),
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

    private static readonly Clase[] _clases = ArmadorEleccion.Todas;

    private static List<object[]> Clases()
    {
        List<object[]> filas =
        [
            [
                "Nombre", "Id", "CostoOro", "DanoBase", "AlcanceCeldas",
                "CadenciaTicks", "AtaquesPorSegundo", "DPS_nivel1",
                "DPS_por_100_oro", "GolpesParaRata20hp", "EsSoporte",
                // Columnas del template que este juego NO tiene. Vacías a
                // propósito: BALANCE_CONTEXT.md explica por qué.
                "HP", "Mana", "DanoMin", "DanoMax", "Precision", "Evasion",
                "Defensa", "ResistFisica", "ResistMagica", "Critico",
                "MultCritico", "VelocidadMovimiento",
            ],
        ];

        foreach (var c in _clases)
        {
            var s = CargadorDatos.Actual.StatsDe(c);

            double aps = 30.0 / s.CadenciaTicks;
            double dps = s.Dano * aps;

            filas.Add(
            [
                c.ToString(), (int)c, s.Costo, s.Dano, s.AlcanceCeldas,
                s.CadenciaTicks, Red(aps), Red(dps),
                Red(dps * 100.0 / s.Costo),
                (19 / s.Dano) + 1,
                EsSoporte(c) ? "si" : "no",
                "", "", "", "", "", "", "", "", "", "", "", "",
            ]);
        }

        return filas;
    }

    /// <summary>Sale de la marca «soporte» de <c>clases.json</c>.</summary>
    private static bool EsSoporte(Clase c) => CargadorDatos.Actual.Clases.De(c).Soporte;

    // -------------------------------------------------------------------------
    //  Hoja 2 — Progresión por nivel (1 a 6)
    // -------------------------------------------------------------------------

    private static List<object[]> Progresion()
    {
        List<object[]> filas =
        [
            [
                "Clase", "Nivel", "ExtraDanoPorc", "DanoEfectivo", "DPS",
                "SlotsPerkAbiertos", "TierVisual",
            ],
        ];

        foreach (var c in _clases)
        {
            var s = CargadorDatos.Actual.StatsDe(c);

            for (int nivel = 1; nivel <= CargadorDatos.Actual.Clases.SlotsPerkPorClase; nivel++)
            {
                // La MISMA cuenta que Roster.StatsDe: (nivel-1)*8 puntos
                // porcentuales, sumados antes de multiplicar (Enteros).
                int extra = (nivel - 1) * CargadorDatos.Actual.Clases.ExtraDanoPorNivel;
                int dano = Enteros.ConPorcentajes(s.Dano, extra);

                int slots = Enteros.Limitar(nivel, 1, CargadorDatos.Actual.Clases.SlotsPerkPorClase);
                int tier = nivel <= 2 ? 1 : nivel <= 4 ? 2 : 3;

                filas.Add(
                [
                    c.ToString(), nivel, extra, dano,
                    Red(dano * 30.0 / s.CadenciaTicks), slots, tier,
                ]);
            }
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  Hoja 3 — Enemigos (el bestiario cargado, no el provisional)
    // -------------------------------------------------------------------------

    private static List<object[]> Enemigos(DatosBalance datos)
    {
        List<object[]> filas =
        [
            [
                "Id", "Nombre", "Area", "EsJefeDeSuArea", "Hp", "DanoMin",
                "DanoMax", "DanoEfectivo", "VelocidadMilesimasPorTick",
                "CeldasPorSegundo", "CadenciaTicks", "DPS_contraTorre",
                "OroQueSuelta", "CostoPresupuesto", "SegundosRuta29Celdas",
            ],
        ];

        var cb = datos.Combate;

        foreach (var e in datos.Enemigos)
        {
            bool esJefe = datos.JefeDe(e.Area)?.Id == e.Id;

            // Las mismas cuentas que la simulación, pero LLAMÁNDOLAS, no
            // transcriptas: el costo sale del mismo método que usa el armador de
            // oleadas y el daño efectivo de las mismas constantes que el tick.
            int danoEfectivo = System.Math.Max(cb.DanoMinimoEnemigo, (e.DanoMin + e.DanoMax) / cb.DivisorPromedioDanoEnemigo);
            int costo = ArmadorOleadas.Costo(e.Id, datos);

            double celdasSeg = e.Velocidad * 30.0 / 1000.0;

            filas.Add(
            [
                e.Id, e.Nombre, e.Area, esJefe ? "si" : "no", e.Hp, e.DanoMin,
                e.DanoMax, danoEfectivo, e.Velocidad, Red(celdasSeg),
                e.CadenciaTicks, Red(danoEfectivo * 30.0 / e.CadenciaTicks),
                e.Oro, costo,
                celdasSeg > 0 ? Red(29.0 / celdasSeg) : 0,
            ]);
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  Hoja 4 — Curvas por oleada, las 100
    // -------------------------------------------------------------------------

    private static List<object[]> Curvas(DatosBalance datos)
    {
        List<object[]> filas =
        [
            [
                "Oleada", "Area", "TieneJefe", "PresupuestoOleada",
                "BonusOroLimpiar", "IntervaloSpawnTicks", "ChancePerkNpcPorc",
                "PresupuestoDropJefe", "PresupuestoItemTienda",
                "PrecioPerkBronceTienda", "PrecioPerkPlataTienda",
            ],
        ];

        for (int oleada = 1; oleada <= CargadorDatos.Actual.Curvas.Corrida.OleadasPorCorrida; oleada++)
        {
            var receta = Balance.RecetaDe(oleada, datos);

            filas.Add(
            [
                oleada, CargadorDatos.Actual.Areas.DeOleada(oleada), CargadorDatos.Actual.Areas.TieneJefe(oleada) ? "si" : "no",
                receta.Presupuesto, receta.BonusOro, receta.IntervaloSpawnTicks,
                receta.ChancePerkNpc,
                CargadorDatos.Actual.Curvas.PorOleada.PresupuestoDropJefe.En(oleada),
                CargadorDatos.Actual.Curvas.PorOleada.PresupuestoItemTienda.En(oleada),
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
                "Id", "Nombre", "Tier", "Efecto", "MagnitudPorc", "MaxApilado",
                "Clases", "Fuente",
            ],
        ];

        var cd = CargadorDatos.Actual.Curvas;

        foreach (var p in CargadorDatos.Actual.Perks.Todos)
        {
            string fuente = p.Tier switch
            {
                TierPerk.Bronce => $"drop comun {cd.Drops.ChanceBronce}% / tienda {cd.Tienda.ChanceBronce}%",
                TierPerk.Plata => $"drop comun {100 - cd.Drops.ChanceBronce}% / tienda {100 - cd.Tienda.ChanceBronce}%",
                TierPerk.Oro => $"SOLO el jefe de area ({cd.Corrida.CantidadAreas} por corrida)",
                _ => "SOLO fusion de dos Oro + plano",
            };

            filas.Add(
            [
                p.Id, p.Nombre, p.Tier.ToString(), p.Efecto.ToString(),
                p.Magnitud, p.MaxApilado, ListaClases(p.Clases), fuente,
            ]);
        }

        return filas;
    }

    private static string ListaClases(Clase[] clases)
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
            ["Nombre", "OroA", "OroB", "UnicoResultante", "ConocidoDeArranque"],
        ];

        foreach (var p in CargadorDatos.Actual.Planos.Todos)
        {
            filas.Add(
            [
                p.Nombre,
                p.OroA >= 0 ? CargadorDatos.Actual.Perks.Por(p.OroA).Nombre : "?",
                p.OroB >= 0 ? CargadorDatos.Actual.Perks.Por(p.OroB).Nombre : "?",
                p.UnicoResultante >= 0 ? CargadorDatos.Actual.Perks.Por(p.UnicoResultante).Nombre : "?",
                p.Inicial ? "si" : "no",
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
                "Habilidad", "Pool", "MagnitudFormula", "MagnitudMin",
                "MagnitudMax", "QueHace",
            ],
        ];

        // ⚠ Sale de la tabla viva. Antes estaba transcripta 1:1 y ya mentía en
        // dos filas: Doble figuraba en el pool épico cuando había pasado a
        // legendaria, y Cadena/Codicia tenían los topes viejos (70 y 200).
        var tabla = CargadorDatos.Actual.Armas;

        foreach (var h in tabla.Habilidades)
        {
            var m = h.Magnitud;

            string formula = m.Divisor == 0
                ? $"{m.Base} fijo"
                : $"{m.Base} + p/{m.Divisor}";

            filas.Add(
            [
                h.Habilidad.ToString(),
                PoolDe(h.Habilidad, tabla),
                formula, m.Min, m.Max,
                h.Explica.Replace("{m}", "M", System.StringComparison.Ordinal),
            ]);
        }

        return filas;
    }

    private static string PoolDe(HabilidadArma h, TablaArmas tabla)
    {
        if (System.Array.IndexOf(tabla.PoolRompen, h) >= 0) return "rompe (legendaria)";
        if (System.Array.IndexOf(tabla.PoolRaro, h) >= 0) return "rara (epica)";
        if (System.Array.IndexOf(tabla.PoolComun, h) >= 0) return "comun";

        return "ninguno (no la tira nadie)";
    }

    // -------------------------------------------------------------------------
    //  Hoja 8 — Generación de armas (el único "equipamiento" del juego)
    // -------------------------------------------------------------------------

    private static List<object[]> GeneracionArmas_()
    {
        var a = CargadorDatos.Actual.Armas;
        var c = CargadorDatos.Actual.Curvas;

        string Chance(RangoChance r) => $"clamp(presupuesto/{r.Divisor}, {r.Min}, {r.Max})%";

        return
        [
            ["Parametro", "Valor", "Notas"],
            ["PasoAfijo", a.Paso, "los numeros se compran de a esto, para que queden redondos"],
            ["CostoPuntoDano", a.CostoDano, $"1 punto porcentual de daño cuesta esto (se compra de a {a.Paso})"],
            ["CostoPuntoCadencia", a.CostoCadencia, "idem"],
            ["CostoPuntoAlcance", a.CostoAlcance, "el mas caro: el alcance es el stat mas valioso de un TD"],
            ["ChanceLegendaria", Chance(a.ChanceLegendaria), "tiradas ENCADENADAS, no exclusivas"],
            ["ChanceEpica", Chance(a.ChanceEpica), "solo si fallo la legendaria"],
            ["ChanceRara", Chance(a.ChanceRara), "solo si fallaron las dos de arriba"],
            ["PresupuestoAHabilidad_Legendaria", $"{a.PorcentajeLegendaria}%", "se aparta ANTES de comprar numeros"],
            ["PresupuestoAHabilidad_Epica", $"{a.PorcentajeEpica}%", ""],
            ["PresupuestoAHabilidad_Rara", $"{a.PorcentajeRara}%", ""],
            ["PresupuestoAHabilidad_Comun", $"{a.PorcentajeComun}%", "las comunes no tienen habilidad"],
            ["ValorDeVenta", $"presupuesto/{a.DivisorValor} * (1 + rareza)", "rareza: comun 0, rara 1, epica 2, legendaria 3"],
            ["PrecioEnTienda", $"Valor * {a.MultiplicadorPrecioTienda}", ""],
            ["PresupuestoDropJefe", Recta(c.PorOleada.PresupuestoDropJefe), "n = oleada"],
            ["PresupuestoItemTienda", Recta(c.PorOleada.PresupuestoItemTienda), "n = oleada"],
            ["StockTienda", $"{c.Tienda.PerksEnStock} perks + {c.Tienda.ArmasEnStock} armas", "se renueva por oleada, no se acumula"],
            ["PoolComun", string.Join(", ", a.PoolComun), ""],
            ["PoolRaro", string.Join(", ", a.PoolRaro), "solo epicas"],
            ["PoolRompen", string.Join(", ", a.PoolRompen), "solo legendarias"],
            ["ClasesDelArma", 1, "una sola, sorteada del roster: un arma para una clase que no tenes es mochila tirada"],
            ["TiendaVendeOro", "NO", "el jefe es la unica fuente de perks Oro"],
            ["ArmasSonPorcentuales", "si", "Dano/Cadencia/Alcance del arma son puntos % sobre la base de la clase"],
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
        var c = d.Curvas;
        var k = d.Combate;

        return
        [
            ["Constante", "Valor", "Archivo", "Notas"],

            ["TicksPorSegundo", Enteros.TicksPorSegundo, "Enteros.cs", "paso fijo, congelado; NO es configurable"],
            ["MilesimasPorCelda", Enteros.MilesimasPorCelda, "Enteros.cs", "unidad de toda distancia de ruta"],
            ["DatosVersion", d.Version, "enemigos.json", "la temporada del ranking"],
            ["DatosHash", d.Hash, "todos", "cubre bestiario + 6 tablas + layouts"],

            // --- La Torre ---
            ["TorreHpInicial", c.Torre.HpInicial, "curvas.json", ""],
            ["TorreDano", c.Torre.Dano, "curvas.json", "al MAS VIEJO de los que la muerden, uno por turno"],
            ["TorreCadenciaTicks", c.Torre.CadenciaTicks, "curvas.json", "el cooldown solo corre si hay a quien pegarle"],

            // --- La corrida ---
            ["OleadasPorCorrida", c.Corrida.OleadasPorCorrida, "curvas.json", "TERMINA ahi, sin modo sin fin"],
            ["CantidadAreas", c.Corrida.CantidadAreas, "curvas.json", ""],
            ["UltimaOleadaPorArea", string.Join(",", c.Corrida.UltimaOleadaPorArea), "curvas.json", "cada una trae jefe"],
            ["OleadasEntreElecciones", c.Corrida.OleadasEntreElecciones, "curvas.json", ""],
            ["SegundosDePreparacion", c.Corrida.SegundosDePreparacion, "curvas.json", "reloj de pared, no ticks de simulacion"],
            ["NombreJefeFinal", c.Jefe.NombreJefeFinal, "curvas.json", "el jefe del area final se elige por nombre, no por HP"],

            // --- Defensores ---
            ["SlotsPerkPorClase", d.Clases.SlotsPerkPorClase, "clases.json", "tambien es el nivel maximo"],
            ["ExtraDanoPorNivel", d.Clases.ExtraDanoPorNivel, "clases.json", "puntos porcentuales por nivel arriba de 1"],
            ["LugaresMochila", c.Defensores.LugaresMochila, "curvas.json", "compartidos perks + armas"],
            ["SlotsEquipoPorClase", c.Defensores.SlotsEquipoPorClase, "curvas.json", "solo el arma"],
            ["PorcentajeVentaDefensor", c.Defensores.PorcentajeVentaDefensor, "curvas.json", "vender defensor devuelve esto del costo"],
            ["PorcentajeVentaArma", c.Defensores.PorcentajeVentaArma, "curvas.json", "vender arma de la mochila devuelve esto del Valor"],

            // --- Economia ---
            ["FichasDeOroInicial", c.Economia.FichasDeOroInicial, "curvas.json", "oro inicial = esto x costo de la clase que toco primero"],
            ["OroPorEnemigoAdelantado", c.Economia.OroPorEnemigoAdelantado, "curvas.json", ""],
            ["TopeOroPorAdelantar", c.Economia.TopeOroPorAdelantar, "curvas.json", "por llamada"],

            // --- Presupuesto de oleada ---
            ["PresupuestoFormula", $"{c.Presupuesto.Base} + {c.Presupuesto.Cuadratico}*n*n", "curvas.json", "antes del tope por pool"],
            ["CapEquivalenteMin", c.Presupuesto.CapEquivalenteMin, "curvas.json", "bichos promedio del pool al empezar el area"],
            ["CapEquivalenteMax", c.Presupuesto.CapEquivalenteMax, "curvas.json", "al terminarla; sube lineal"],
            ["MaxNpcsPorOleada", c.Oleada.MaxNpcs, "curvas.json", "el presupuesto sobrante va a perks de NPC"],
            ["PesoAreaActual", c.Oleada.PesoAreaActual, "curvas.json", "peso de sorteo por bicho"],
            ["PesoAreaAnterior", c.Oleada.PesoAreaAnterior, "curvas.json", ""],
            ["CostoNpcFormula",
                $"hp/{c.Oleada.DivisorHp} + danoMax/{c.Oleada.DivisorDanoMax} + velocidad/{c.Oleada.DivisorVelocidad}",
                "curvas.json", "entero, trunca"],

            // --- Curvas por oleada ---
            ["BonusOroFormula", Recta(c.PorOleada.BonusOroLimpiar), "curvas.json", "por limpiar la oleada"],
            ["PresupuestoDropJefeFormula", Recta(c.PorOleada.PresupuestoDropJefe), "curvas.json", "LINEAL contra la cuadratica de la oleada, a proposito"],
            ["PresupuestoItemTiendaFormula", Recta(c.PorOleada.PresupuestoItemTienda), "curvas.json", "mas chico que el del jefe"],
            ["IntervaloSpawnFormula",
                $"clamp({c.PorOleada.IntervaloBase} - n/{c.PorOleada.IntervaloDivisor}, {c.PorOleada.IntervaloMin}, {c.PorOleada.IntervaloMax}) ticks",
                "curvas.json", ""],
            ["ChancePerkNpcFormula",
                $"clamp(n/{c.PorOleada.ChancePerkDivisor}, {c.PorOleada.ChancePerkMin}, {c.PorOleada.ChancePerkMax}) %",
                "curvas.json", ""],

            // --- Perks de NPC ---
            ["CostoPerkVidaNpc", c.PerksNpc.CostoVida, "curvas.json", "del presupuesto de la oleada"],
            ["CostoPerkVelocidadNpc", c.PerksNpc.CostoVelocidad, "curvas.json", ""],
            ["EscalonPerkVidaNpc", c.PerksNpc.EscalonVida, "curvas.json", "puntos porcentuales de HP por escalon"],
            ["EscalonPerkVelocidadNpc", c.PerksNpc.EscalonVelocidad, "curvas.json", ""],
            ["MaxPerkVidaNpc", c.PerksNpc.MaxVida, "curvas.json", "techo por NPC"],
            ["MaxPerkVelocidadNpc", c.PerksNpc.MaxVelocidad, "curvas.json", "un bicho que vuela no se puede matar"],
            ["ChanceVidaEnLaTiradaNpc", c.PerksNpc.ChanceVidaEnLaTirada, "curvas.json", "% que va a vida; el resto a velocidad"],
            ["ChanceVelocidadEnSobranteNpc", c.PerksNpc.ChanceVelocidadEnSobrante, "curvas.json", "al repartir el presupuesto no gastado"],
            ["VidaExtraJefe",
                $"min(sobrante/{c.Jefe.DivisorVidaExtra}, {c.Jefe.MaxVidaExtra}) en % de HP",
                "curvas.json", "CON TECHO: el jefe es fuerte por diseno, no por residuo"],

            // --- Drops y tienda ---
            ["ChanceDropComun", c.Drops.ChanceComun, "curvas.json", "%; no sube con la oleada, sube la CALIDAD"],
            ["ChanceBronceEnDrop", c.Drops.ChanceBronce, "curvas.json", "% bronce; el resto plata"],
            ["ChanceBronceEnTienda", c.Tienda.ChanceBronce, "curvas.json", ""],
            ["PerksEnStock", c.Tienda.PerksEnStock, "curvas.json", "no se acumula entre oleadas"],
            ["ArmasEnStock", c.Tienda.ArmasEnStock, "curvas.json", ""],
            ["PrecioPerkBronceFormula", Recta(c.Tienda.PrecioPerkBronce), "curvas.json", ""],
            ["PrecioPerkPlataFormula", Recta(c.Tienda.PrecioPerkPlata), "curvas.json", "el triple: se paga el SLOT, no solo el numero"],

            // --- El tick ---
            ["DanoEnemigoFormula",
                $"max({k.DanoMinimoEnemigo}, (danoMin + danoMax)/{k.DivisorPromedioDanoEnemigo})",
                "combate.json", "promedio, no tirada: una tirada por golpe correría el RNG"],
            ["SlowTope", k.TopeSlowPorciento, "combate.json", "% max; no apila, gana el mas fuerte"],
            ["SlowDuracionTicks", k.DuracionSlowTicks, "combate.json", "TEMPORAL; cada aplicacion refresca"],
            ["VentanaSaltoRicochet", $"{k.VentanaSaltoRicochetCeldas} celdas DE RUTA", "combate.json", "del objetivo ANTERIOR, no del defensor"],
            ["RadioSplash", $"{k.RadioSplashCeldas} celdas DE RUTA", "combate.json", "distancia recorrida, no euclidea"],
            ["RebotesRicochet", k.RebotesRicochet, "combate.json", "saltos extra ademas del golpe directo"],
            ["RebotesPerforacion", k.RebotesPerforacion, "combate.json", ""],
            ["RebotesApocalipsis", k.RebotesApocalipsis, "combate.json", ""],
            ["MaxSecundariosSplashOro", k.MaxSecundariosSplashOro, "combate.json", ""],
            ["MaxSecundariosOndaDeFrio", k.MaxSecundariosOndaDeFrio, "combate.json", ""],
            ["MaxSecundariosIraDeDiosTormentaDivina", k.MaxSecundariosDivino, "combate.json", ""],
            ["SlowTerribleHambre", k.SlowTerribleHambre, "combate.json", "el Unico envenena y ademas ralentiza"],
            ["PenetranteDivisor", k.DivisorPenetrante, "combate.json", "magnitud/N mientras no exista defensa que penetrar"],
            ["CriticoCadaN", k.GolpesPorCritico, "combate.json", "contador POR DEFENSOR, no Tick % N"],

            // --- Habilidades de arma ---
            ["MaxSecundariosSplashArma", k.MaxSecundariosSplashArma, "combate.json", ""],
            ["RebotesReboteArma", k.RebotesReboteArma, "combate.json", ""],
            ["MaxSecundariosCadena", k.MaxSecundariosCadena, "combate.json", ""],
            ["SedTecho", k.TopeBonusSed, "combate.json", "% max ACUMULADO por oleada; se reinicia al terminarla"],
            ["UmbralEjecutarTropa", k.UmbralEjecutarTropa, "combate.json", "TECHO: el umbral real es min(magnitud, esto)"],
            ["UmbralEjecutarJefe", k.UmbralEjecutarJefe, "combate.json", ""],
            ["JefeFinalInmuneAEjecutar", k.JefeFinalInmuneAEjecutar ? "si" : "no", "combate.json", "matarlo es como se gana la corrida"],
            ["CooldownVampirismoTicks", k.CooldownVampirismoTicks, "combate.json", "un proc por CLASE, no por defensor"],
            ["DuracionMarcaCodiciaTicks", k.DuracionMarcaCodiciaTicks, "combate.json", "se cobra al morir, no al golpear"],

            // --- Reglas que no son un numero ---
            ["AlcanceAlCuadrado", "si", "todo el combate", "sin raices; alcance 1 NO cubre diagonales"],
            ["ComportamientosNoApilan", "si", "Roster.StatsDe", "gana el tier mas alto; el arma pisa al perk"],
        ];
    }

    private static string Recta(CurvaLineal c) => $"{c.Base} + {c.PorOleada}*n";

    // -------------------------------------------------------------------------
    //  Hoja 10 — Matchups: clase (nivel 1 y 6, sin perks) contra cada enemigo
    // -------------------------------------------------------------------------

    private static List<object[]> Matchups(DatosBalance datos)
    {
        List<object[]> filas =
        [
            [
                "Clase", "Enemigo", "AreaEnemigo", "HpEnemigo",
                "GolpesParaMatar_nv1", "SegundosParaMatar_nv1",
                "GolpesParaMatar_nv6", "SegundosParaMatar_nv6",
                "SegundosEnAlcance_aprox", "LoMataSolo_nv1",
            ],
        ];

        foreach (var c in _clases)
        {
            var s = CargadorDatos.Actual.StatsDe(c);
            int danoNv6 = Enteros.ConPorcentajes(s.Dano, (CargadorDatos.Actual.Clases.SlotsPerkPorClase - 1) * CargadorDatos.Actual.Clases.ExtraDanoPorNivel);

            foreach (var e in datos.Enemigos)
            {
                int golpes1 = ((e.Hp - 1) / s.Dano) + 1;
                int golpes6 = ((e.Hp - 1) / danoNv6) + 1;

                // Ticks del primer golpe al último: (golpes-1) * cadencia. El
                // primero sale con cooldown en cero.
                double seg1 = (golpes1 - 1) * s.CadenciaTicks / 30.0;
                double seg6 = (golpes6 - 1) * s.CadenciaTicks / 30.0;

                // Cuánto tiempo pasa el enemigo dentro del alcance: la cuerda del
                // círculo sobre una ruta recta que pasa a 1 celda del defensor,
                // aproximada como 2*alcance celdas. Es una COTA, no una medición:
                // la ruta serpentea y el defensor bien puesto cubre varias pasadas.
                double celdasSeg = e.Velocidad * 30.0 / 1000.0;
                double segAlcance = celdasSeg > 0 ? 2.0 * s.AlcanceCeldas / celdasSeg : 0;

                filas.Add(
                [
                    c.ToString(), e.Nombre, e.Area, e.Hp,
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

    private static void EscribirXlsx(string ruta, List<(string Nombre, List<object[]> Filas)> hojas)
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
            libro.Append($"<sheet name=\"{Xml(hojas[i - 1].Nombre)}\" sheetId=\"{i}\" r:id=\"rId{i}\"/>");
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
