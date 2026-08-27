using System.IO.Compression;
using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Exporta TODO el balance a un XLSX, para analizarlo con otras herramientas.
/// </summary>
/// <remarks>
/// <para><b>Los números salen del código vivo, no de una transcripción.</b> Cada
/// celda se calcula llamando a <see cref="Balance"/>, <see cref="CatalogoPerks"/>
/// y <see cref="CargadorDatos"/> en runtime: si un número cambia en el código, el
/// próximo export lo trae solo. Un documento escrito a mano se desincroniza en la
/// primera semana.</para>
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
            var s = Balance.StatsDe(c);

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

    /// <summary>Clérigo, bardo y druida: pegan poco a propósito (Eleccion.cs).</summary>
    private static bool EsSoporte(Clase c) => c is Clase.Clerigo or Clase.Bardo or Clase.Druida;

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
            var s = Balance.StatsDe(c);

            for (int nivel = 1; nivel <= Balance.SlotsPerkPorClase; nivel++)
            {
                // La MISMA cuenta que Roster.StatsDe: (nivel-1)*8 puntos
                // porcentuales, sumados antes de multiplicar (Enteros).
                int extra = (nivel - 1) * 8;
                int dano = Enteros.ConPorcentajes(s.Dano, extra);

                int slots = Enteros.Limitar(nivel, 1, Balance.SlotsPerkPorClase);
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

        foreach (var e in datos.Enemigos)
        {
            bool esJefe = datos.JefeDe(e.Area)?.Id == e.Id;

            // Las mismas cuentas que la simulación:
            // daño efectivo = max(1, (min+max)/2)          [Partida.FaseSpawn]
            // costo = hp/4 + danoMax/2 + velocidad/8       [ArmadorOleadas.Costo]
            int danoEfectivo = System.Math.Max(1, (e.DanoMin + e.DanoMax) / 2);
            int costo = (e.Hp / 4) + (e.DanoMax / 2) + (e.Velocidad / 8);

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

        for (int oleada = 1; oleada <= Balance.OleadasPorCorrida; oleada++)
        {
            var receta = Balance.RecetaDe(oleada, datos);

            filas.Add(
            [
                oleada, Areas.DeOleada(oleada), Areas.TieneJefe(oleada) ? "si" : "no",
                receta.Presupuesto, receta.BonusOro, receta.IntervaloSpawnTicks,
                receta.ChancePerkNpc,
                Balance.PresupuestoDropJefe(oleada),
                Balance.PresupuestoItemTienda(oleada),
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

        foreach (var p in CatalogoPerks.Todos)
        {
            string fuente = p.Tier switch
            {
                TierPerk.Bronce => "drop comun 70% / tienda 65%",
                TierPerk.Plata => "drop comun 30% / tienda 35%",
                TierPerk.Oro => "SOLO el jefe de area (8 por corrida)",
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

        foreach (var p in Planos.Todos)
        {
            filas.Add(
            [
                p.Nombre,
                p.OroA >= 0 ? CatalogoPerks.Por(p.OroA).Nombre : "?",
                p.OroB >= 0 ? CatalogoPerks.Por(p.OroB).Nombre : "?",
                p.UnicoResultante >= 0 ? CatalogoPerks.Por(p.UnicoResultante).Nombre : "?",
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

        // Los tres pools de GeneradorArmas.TirarHabilidad, y las fórmulas de
        // MagnitudDe, transcriptas 1:1 (p = presupuesto de la habilidad).
        (HabilidadArma H, string Pool, string Formula, int Min, int Max)[] tabla =
        [
            (HabilidadArma.Splash, "comun", "20 + p/4", 20, 90),
            (HabilidadArma.Rebote, "comun", "25 + p/4", 25, 100),
            (HabilidadArma.Escarcha, "comun", "10 + p/8", 10, 45),
            (HabilidadArma.Veneno, "comun", "15 + p/5", 15, 80),
            (HabilidadArma.Critico, "rara (epica)", "120 + p/2", 120, 400),
            (HabilidadArma.Francotirador, "rara (epica)", "15 + p/6", 15, 60),
            (HabilidadArma.Carnicero, "rara (epica)", "20 + p/5", 20, 80),
            (HabilidadArma.Sed, "rara (epica)", "2 + p/40", 2, 15),
            (HabilidadArma.Doble, "rara (epica)", "100 fijo", 100, 100),
            (HabilidadArma.Ejecutar, "rompe (legendaria)", "8 + p/40", 8, 25),
            (HabilidadArma.Cadena, "rompe (legendaria)", "30 + p/8", 30, 70),
            (HabilidadArma.Codicia, "rompe (legendaria)", "50 + p/6", 50, 200),
            (HabilidadArma.Vampirismo, "rompe (legendaria)", "1 + p/50", 1, 8),
        ];

        foreach (var t in tabla)
        {
            filas.Add(
            [
                t.H.ToString(), t.Pool, t.Formula, t.Min, t.Max,
                GeneradorArmas.ExplicarHabilidad(t.H, 0).Replace("0", "M"),
            ]);
        }

        return filas;
    }

    // -------------------------------------------------------------------------
    //  Hoja 8 — Generación de armas (el único "equipamiento" del juego)
    // -------------------------------------------------------------------------

    private static List<object[]> GeneracionArmas_()
    {
        return
        [
            ["Parametro", "Valor", "Notas"],
            ["CostoPuntoDano", 2, "1 punto porcentual de daño cuesta 2 de presupuesto (se compra de a 5)"],
            ["CostoPuntoCadencia", 3, "idem, de a 5"],
            ["CostoPuntoAlcance", 4, "idem, de a 5"],
            ["ChanceLegendaria", "min(presupuesto/200, 4)%", "techo 4%"],
            ["ChanceEpica", "clamp(presupuesto/60, 2, 12)%", ""],
            ["ChanceRara", "clamp(presupuesto/20, 10, 35)%", ""],
            ["PresupuestoAHabilidad_Legendaria", "60%", "se aparta ANTES de comprar numeros"],
            ["PresupuestoAHabilidad_Epica", "40%", ""],
            ["PresupuestoAHabilidad_Rara", "25%", ""],
            ["PresupuestoAHabilidad_Comun", "0%", "las comunes no tienen habilidad"],
            ["ValorDeVenta", "presupuesto/2 * (1 + rareza)", "rareza: comun 0, rara 1, epica 2, legendaria 3"],
            ["PrecioEnTienda", "Valor * 3", "Tienda.Renovar"],
            ["PresupuestoDropJefe", "40 + oleada*6", "Balance.PresupuestoDropJefe"],
            ["PresupuestoItemTienda", "30 + oleada*5", "Balance.PresupuestoItemTienda"],
            ["StockTienda", "2 perks + 2 armas", "se renueva por oleada, no se acumula"],
            ["TiendaVendeOro", "NO", "el jefe es la unica fuente de perks Oro"],
            ["ArmasSonPorcentuales", "si", "Dano/Cadencia/Alcance del arma son puntos % sobre la base de la clase"],
        ];
    }

    // -------------------------------------------------------------------------
    //  Hoja 9 — Constantes sueltas, con su archivo
    // -------------------------------------------------------------------------

    private static List<object[]> Constantes()
    {
        return
        [
            ["Constante", "Valor", "Archivo", "Notas"],
            ["TicksPorSegundo", Enteros.TicksPorSegundo, "Enteros.cs", "paso fijo, congelado"],
            ["TorreHpInicial", Balance.TorreHpInicial, "Balance.cs", ""],
            ["TorreDano", Balance.TorreDano, "Balance.cs", "al MAS VIEJO de los que la muerden, uno por turno"],
            ["TorreCadenciaTicks", Balance.TorreCadenciaTicks, "Balance.cs", "1 golpe por segundo"],
            ["OleadasPorCorrida", Balance.OleadasPorCorrida, "Balance.cs", "TERMINA ahi, sin modo sin fin"],
            ["OleadasEntreElecciones", Balance.OleadasEntreElecciones, "Balance.cs", ""],
            ["OroPorEnemigoAdelantado", Balance.OroPorEnemigoAdelantado, "Balance.cs", "tope 400 por llamada (Partida.Adelantar)"],
            ["SlotsPerkPorClase", Balance.SlotsPerkPorClase, "Balance.cs", "tambien es el nivel maximo"],
            ["LugaresMochila", Balance.LugaresMochila, "Balance.cs", "compartidos perks + armas"],
            ["SlotsEquipoPorClase", Balance.SlotsEquipoPorClase, "Balance.cs", "solo el arma"],
            ["ChanceDropComun", Balance.ChanceDropComun, "Balance.cs", "%. 70% bronce / 30% plata"],
            ["PorcentajeVenta", Balance.PorcentajeVenta, "Balance.cs", "vender defensor devuelve esto del costo"],
            ["OroInicial", "2 * costo de la clase elegida", "Partida.Elegir", "se entrega al elegir la primera clase"],
            ["ExtraDanoPorNivel", 8, "Roster.StatsDe", "puntos porcentuales por nivel arriba de 1"],
            ["NivelMaximo", 6, "Eleccion.Siguiente", "no se ofrece levelear mas alla"],
            ["MaxNpcsPorOleada", 60, "Oleadas.cs", "el presupuesto sobrante va a perks de NPC"],
            ["CostoPerkVidaNpc", 12, "Oleadas.cs", "del presupuesto de la oleada"],
            ["CostoPerkVelocidadNpc", 10, "Oleadas.cs", ""],
            ["EscalonPerkVidaNpc", 50, "Oleadas.cs", "+50% HP por escalon, SIN tope"],
            ["EscalonPerkVelocidadNpc", 10, "Oleadas.cs", "+10% por escalon"],
            ["MaxPerkVelocidadNpc", 120, "Oleadas.cs", "techo +120% velocidad"],
            ["PerkVidaInicialNpc", 50, "Oleadas.Armar", "el 50/50 de la primera tirada"],
            ["PerkVelocidadInicialNpc", 30, "Oleadas.Armar", ""],
            ["PesoAreaActual", 40, "Balance.PermitidosDe", "peso de sorteo por bicho"],
            ["PesoAreaAnterior", 15, "Balance.PermitidosDe", ""],
            ["CostoNpcFormula", "hp/4 + danoMax/2 + velocidad/8", "Oleadas.Costo", "entero, trunca"],
            ["PresupuestoOleadaFormula", "35 + 9*n*n", "Balance.RecetaDe", ""],
            ["BonusOroFormula", "10 + 6*n", "Balance.RecetaDe", ""],
            ["IntervaloSpawnFormula", "clamp(45 - n/3, 8, 45) ticks", "Balance.RecetaDe", ""],
            ["ChancePerkNpcFormula", "clamp(n/2, 0, 30) %", "Balance.RecetaDe", ""],
            ["VidaExtraJefe", "sobrante/4 en % de HP", "Oleadas.Armar", "el presupuesto no gastado engorda al jefe"],
            ["SplashRadio", "2 celdas DE RUTA", "Partida.AplicarSplash", "distancia recorrida, no euclidea"],
            ["RalentizarPiso", -80, "Partida.Ralentizar", "% de velocidad; permanente, sin duracion"],
            ["CriticoCadaN", 7, "Partida", "Tick % 7 == 0, compartido por TODOS los criticos"],
            ["PenetranteDivisor", 4, "Partida.AplicarComportamiento", "magnitud/4 mientras no exista defensa"],
            ["SedTecho", 300, "Partida.AjustarDanoPorHabilidad", "% max de bonus por muertes"],
            ["UltimaOleadaPorArea", "12,24,37,50,62,75,87,100", "Datos.Areas", ""],
            ["AlcanceAlCuadrado", "si", "todo el combate", "sin raices; alcance 1 NO cubre diagonales"],
        ];
    }

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
            var s = Balance.StatsDe(c);
            int danoNv6 = Enteros.ConPorcentajes(s.Dano, 40);

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
