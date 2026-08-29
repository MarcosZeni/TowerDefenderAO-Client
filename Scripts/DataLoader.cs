using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using TowerOfArgentum.Simulation;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// Lee los JSON de <c>Data\</c> y arma el <see cref="DatosBalance"/>.
/// </summary>
/// <remarks>
/// <para>Está del lado del cliente y no en <c>Simulation\</c> a propósito: leer
/// archivos necesita Godot, y la simulación no puede depender del motor. La
/// simulación recibe los datos ya cargados, así que el mismo código corre en el
/// servidor —que los va a leer de otro lado— sin cambiar una línea.</para>
/// </remarks>
public static class DataLoader
{
    private const string RutaEnemigos = "res://Data/enemigos.json";
    private const string RutaClases = "res://Data/clases.json";
    private const string RutaPerks = "res://Data/perks.json";
    private const string RutaPlanos = "res://Data/planos.json";
    private const string RutaArmas = "res://Data/generador-armas.json";
    private const string RutaCurvas = "res://Data/curvas.json";
    private const string RutaCombate = "res://Data/combate.json";

    /// <summary>
    /// Todo lo que hay que leer, EN ESTE ORDEN.
    /// </summary>
    /// <remarks>
    /// ⚠ El orden es parte del hash: se concatenan los textos tal como salen de
    /// acá. Reordenar la lista cambia el hash sin cambiar un solo número, y el
    /// servidor rechazaría a media población por nada.
    /// </remarks>
    private static readonly string[] _archivos =
    [
        RutaEnemigos, RutaClases, RutaPerks, RutaPlanos, RutaArmas, RutaCurvas, RutaCombate,
    ];

    private static BalanceData? _cache;

    /// <summary>El último error de carga, para que la UI lo pueda mostrar.</summary>
    public static string Problem { get; private set; } = "";

    /// <summary>Los datos de la temporada actual.</summary>
    public static BalanceData Current => _cache ??= Load();

    /// <summary>Vuelve a leer todo del disco. La usan las herramientas offline.</summary>
    public static void Forget() => _cache = null;

    private static BalanceData Load()
    {
        Problem = "";

        try
        {
            return CargarOTirar();
        }
        catch (InvalidBalanceDataException e)
        {
            // ⚠ No se cae a valores por defecto. Un número de balance que se cae
            // a un default es una corrida que mide otra cosa que las demás, y el
            // jugador no se entera hasta que el ranking no le cierra. El hash
            // queda en "sin-datos" y el servidor la rechaza AL ABRIRLA, que es
            // infinitamente mejor que rechazarla después de tres horas.
            Problem = e.Message;
            GD.PushError($"Datos inválidos: {e.Message}");

            return Vacio();
        }
    }

    private static BalanceData CargarOTirar()
    {
        var textos = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var crudo = new StringBuilder();

        foreach (string ruta in _archivos)
        {
            string texto = LeerTexto(ruta);

            if (string.IsNullOrEmpty(texto))
            {
                throw new InvalidBalanceDataException(
                    $"no se pudo leer {ruta}. Si falta enemigos.json, corré el importador:\n" +
                    "  Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --importar-npcs");
            }

            textos[ruta] = texto;
            crudo.Append(texto);
        }

        var perks = TableReader.Perks(textos[RutaPerks]);
        var (layouts, textoLayouts) = CargarLayouts();

        var datos = new BalanceData
        {
            Version = VersionDe(textos[RutaEnemigos]),

            // ⚠ El hash cubre TODO lo que es entrada de la simulación: el
            // bestiario, las seis tablas y los layouts.
            //
            // Un layout es entrada igual que un enemigo —cambiarle una celda
            // cambia el recorrido, el tiempo de tiro y el resultado— y una curva
            // es entrada igual que un layout. Lo que no entra al hash es lo que
            // permite que dos corridas midan cosas distintas sin que nadie se
            // entere.
            //
            // ⚠ Se hashea el TEXTO CRUDO, comentarios incluidos: cambiar un
            // comentario cambia el hash. Es más estricto de lo necesario y está
            // bien que lo sea — el costo de un falso positivo es reimportar; el
            // de un falso negativo es un ranking que no mide lo mismo.
            Hash = HashDe(crudo.ToString() + textoLayouts),

            Enemies = EnemigosDe(textos[RutaEnemigos]),
            Layouts = layouts,

            Classes = TableReader.Classes(textos[RutaClases]),
            Perks = perks,
            Blueprints = TableReader.Blueprints(textos[RutaPlanos], perks),
            Weapons = TableReader.Weapons(textos[RutaArmas]),
            Curves = TableReader.Curves(textos[RutaCurvas]),
            Combat = TableReader.Combat(textos[RutaCombate]),
        };

        datos.BuildIndexes();

        GD.Print(
            $"Datos v{datos.Version} · {datos.Enemies.Count} enemigos · " +
            $"{datos.Classes.All.Count} clases · {datos.Perks.All.Count} perks · " +
            $"{datos.Blueprints.All.Count} planos · hash {datos.Hash[..12]}…");

        return datos;
    }

    /// <summary>
    /// El bestiario, tal como sale del importador.
    /// </summary>
    /// <remarks>
    /// Es el único de los siete que sigue leyéndose con el deserializador de
    /// objetos: lo escribe una máquina —el importador— así que no lleva
    /// comentarios ni hace falta el control fino de errores que sí necesitan los
    /// archivos que edita una persona.
    /// </remarks>
    private static List<EnemyData> EnemigosDe(string json)
    {
        var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        ArchivoEnemigos? archivo;

        try
        {
            archivo = JsonSerializer.Deserialize<ArchivoEnemigos>(json, opciones);
        }
        catch (JsonException e)
        {
            throw new InvalidBalanceDataException($"{RutaEnemigos} no es JSON válido: {e.Message}");
        }

        if (archivo?.Enemies is null || archivo.Enemies.Count == 0)
            throw new InvalidBalanceDataException($"{RutaEnemigos} no trae enemigos.");

        var enemigos = new List<EnemyData>(archivo.Enemies.Count);

        foreach (var e in archivo.Enemies)
        {
            enemigos.Add(new EnemyData
            {
                Id = e.Id,
                Name = e.Name ?? "?",
                Area = e.Area,
                Hp = e.ScaledHp,
                MinDamage = e.MinDamage,
                MaxDamage = e.MaxDamage,
                Speed = e.Speed,
                CadenceTicks = e.CadenceTicks,
                Gold = e.Gold,
                Body = e.Body,
                VisualScale = e.VisualScale,
                Graphic = e.Graphic ?? "",
            });
        }

        return enemigos;
    }

    private static int VersionDe(string json)
    {
        try
        {
            var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<ArchivoEnemigos>(json, opciones)?.Version ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// El hash de los datos, que viaja en <c>/run/start</c>.
    /// </summary>
    /// <remarks>
    /// <para>Se calcula sobre el <b>texto crudo</b> del archivo y no sobre los
    /// objetos ya parseados: así cualquier cambio —un número, un nombre, hasta un
    /// espacio— cambia el hash. Es más estricto de lo necesario, y está bien que
    /// lo sea: el costo de un falso positivo es que el jugador reimporte los
    /// datos; el de un falso negativo es un ranking donde dos corridas no midieron
    /// lo mismo.</para>
    ///
    /// <para>⚠ Se normalizan los saltos de línea antes de hashear. Sin eso, el
    /// mismo archivo clonado en Windows y en Linux daría hashes distintos por el
    /// CRLF, y el servidor rechazaría a media población.</para>
    /// </remarks>
    private static string HashDe(string contenido)
    {
        string normalizado = contenido.Replace("\r\n", "\n", System.StringComparison.Ordinal);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizado));

        return System.Convert.ToHexStringLower(hash);
    }

    private static string LeerTexto(string ruta)
    {
        using var f = ArchivoGodot.Open(ruta, ArchivoGodot.ModeFlags.Read);
        return f?.GetAsText() ?? "";
    }

    /// <summary>
    /// Lee los layouts de <c>Data/layouts/</c> y los arma.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Ordenados por nombre de archivo, siempre.</b> La seed elige por
    /// índice, así que si el orden dependiera de cómo el sistema de archivos
    /// devuelve la carpeta, la misma seed daría otro mapa en otra máquina y la
    /// revalidación acusaría de trampa a quien no hizo nada.</para>
    ///
    /// <para>El que no arma se saltea con un aviso y los demás siguen: un layout
    /// roto es un problema de datos, y quedarse sin juego por eso es peor que
    /// jugar con once mapas.</para>
    /// </remarks>
    private static (List<Map> Mapas, string Text) CargarLayouts()
    {
        const string carpeta = "res://Data/layouts";

        var mapas = new List<Map>();
        var texto = new StringBuilder();

        using var dir = DirAccess.Open(carpeta);

        if (dir is null)
        {
            GD.PushError($"No se pudo abrir {carpeta}. Corré:\n" +
                         "  Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --generar-layouts");

            return (mapas, "");
        }

        var nombres = new List<string>(dir.GetFiles());
        nombres.Sort(System.StringComparer.Ordinal);

        foreach (string nombre in nombres)
        {
            // Godot renombra a .import lo que empaqueta; en una build exportada el
            // .json llega como .json.remap y GetFiles lo lista así.
            if (!nombre.EndsWith(".json", System.StringComparison.Ordinal))
                continue;

            string contenido = LeerTexto($"{carpeta}/{nombre}");

            if (contenido.Length == 0)
                continue;

            var filas = FilasDe(contenido, out string motivo);

            if (filas is null)
            {
                GD.PushWarning($"{nombre}: {motivo}, se saltea");
                continue;
            }

            var mapa = AsciiLayout.Compose(filas);

            if (mapa is null)
            {
                GD.PushWarning($"{nombre}: el camino no cierra, se saltea");
                continue;
            }

            mapas.Add(mapa);
            texto.Append(contenido);
        }

        GD.Print($"Layouts: {mapas.Count} de {nombres.Count} archivo(s)");

        return (mapas, texto.ToString());
    }

    /// <summary>
    /// Las filas ASCII de un layout, o <c>null</c> con el motivo en
    /// <paramref name="motivo"/>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ El motivo se devuelve en vez de escribir siempre el mismo aviso.
    /// Antes un JSON roto y un archivo sin la clave <c>filas</c> devolvían los dos
    /// <c>null</c>, y el aviso decía «no tiene filas» en los dos casos: quien
    /// rompía una coma se pasaba un rato buscando una clave que estaba ahí.</para>
    ///
    /// <para>Se parsea con las mismas opciones que las tablas de balance —admite
    /// comentarios y comas colgando— por consistencia y no porque haga falta:
    /// estos archivos los escribe <c>LayoutEditor.tscn</c>, que los reescribe
    /// enteros y borraría cualquier comentario que alguien les agregue.</para>
    /// </remarks>
    private static List<string>? FilasDe(string contenido, out string motivo)
    {
        motivo = "";

        System.Text.Json.Nodes.JsonNode? json;

        try
        {
            json = System.Text.Json.Nodes.JsonNode.Parse(
                contenido,
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
        }
        catch (JsonException e)
        {
            motivo = $"no es JSON válido — {e.Message}";
            return null;
        }

        JsonArray? filas;

        try
        {
            filas = json?["rows"]?.AsArray();
        }
        catch (System.InvalidOperationException)
        {
            motivo = "«filas» no es una lista";
            return null;
        }

        if (filas is null)
        {
            motivo = "no tiene «filas»";
            return null;
        }

        var lista = new List<string>(filas.Count);

        foreach (var f in filas)
        {
            if (f is null)
            {
                motivo = "«filas» tiene un elemento nulo";
                return null;
            }

            try
            {
                lista.Add(f.GetValue<string>());
            }
            catch (System.Exception e) when (e is System.InvalidOperationException or System.FormatException)
            {
                motivo = "«filas» tiene un elemento que no es texto";
                return null;
            }
        }

        return lista;
    }

    /// <summary>
    /// Datos vacíos, para que el menú principal abra y pueda mostrar el error en
    /// vez de que el juego se cierre con una excepción.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Estos números NO son un balance de reserva.</b> Son mínimos
    /// deliberadamente absurdos —una clase, ningún perk, ningún enemigo— para que
    /// sea imposible confundirlos con los de verdad y para que cualquier intento
    /// de jugar falle rápido y fuerte.</para>
    ///
    /// <para>El hash queda en <c>"sin-datos"</c>, que no va a coincidir con el del
    /// servidor: la corrida se rechaza <b>al abrirse</b> en vez de jugarse con
    /// datos inventados y reportar un resultado sin sentido.</para>
    /// </remarks>
    private static BalanceData Vacio()
    {
        var perks = new PerkTable([]);

        var datos = new BalanceData
        {
            Version = 0,
            Hash = "sin-datos",
            Enemies = [],
            Layouts = [],

            Classes = new ClassTable(
                [new ClassData(DefenderClass.Warrior, 1, 1, 2, 30, false)],
                extraDanoPorNivel: 0,
                slotsPerkPorClase: 1),

            Perks = perks,
            Blueprints = new BlueprintTable([]),

            Weapons = new WeaponTable(
                paso: 5, costoDano: 1, costoCadencia: 1, costoAlcance: 1,
                new ChanceRange(0, 0, 0), new ChanceRange(0, 0, 0), new ChanceRange(0, 0, 0),
                porcLegendaria: 0, porcEpica: 0, porcRara: 0, porcComun: 0,
                divisorValor: 1, multiplicadorPrecioTienda: 1,
                [WeaponAbility.None], [WeaponAbility.None], [WeaponAbility.None],
                []),

            Curves = new CurveData(
                new TowerData(1, 1, 30),
                new RunData(1, 1, [1], 1, 30, 1, 100),
                new DefenderData(1, 1, 50, 100),
                new EconomyData(1, 0, 0, 1, [100]),
                new BudgetData(0, 0, 1, 1),
                new PerWaveCurves(
                    new LinearCurve(0, 0), new LinearCurve(0, 0), new LinearCurve(0, 0),
                    30, 1, 30, 30, 1, 0, 0),
                new ShopData(
                    Slots: 1, IntervalWaves: 1,
                    ClassWeight: 0, BronzeWeight: 1, SilverWeight: 0,
                    GoldWeight: 0, BlueprintWeight: 0, WeaponWeight: 0,
                    MaxGoldOffers: 0, MaxBlueprintOffers: 0, MinUsableOffers: 0,
                    SilverMinWave: 1, GoldMinWave: 1, BlueprintMinWave: 1,
                    RecipeWeightMultiplier: 1,
                    new LinearCurve(0, 0), new LinearCurve(0, 0), new LinearCurve(0, 0),
                    new LinearCurve(0, 0), new LinearCurve(0, 0), ClassCostMultiplier: 0),
                new DropData(0, 0, 1, 1, 1, 0, false),
                new WaveData(1, 1, 1, 1, 1, 1, 1, 1, 30,
                    new SpawnFormations(1, 0, 0, 50, 150, 5, 60, 260)),
                new NpcPerkData(1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0),
                new BossData(1, 0, ""),
                new UpgradeData(2, 2, 0)),

            // ⚠ Todos estos números son MÍNIMOS QUE NO ROMPEN, no valores de
            // balance: la tabla vacía existe para que el juego arranque y muestre
            // el error, no para que se pueda jugar con ella. El hash queda en
            // «sin-datos» y `Run` no acepta arrancar con esto.
            Combat = new CombatData(
                new EnemyCombat(2, 1),
                new BossControl(50, 40, 30, 0, 1, 30, true, true, true),
                new StateRules(60, 90, 180, 60, 300, 90, 15, 120, 6, 3, 90, 3, 120, 10, 60, 90, 180),
                new BehaviorRules(1, 2000, 2, 1500, 3, 1800, 105, 2, 1500, 7, 5, 60, 4, 6, 5, 8, 4, 15, 1000, 40, 1500, 25, 80, 45),
                new UniqueRules(3, 75, 2000, 8, 50, 12, 3),
                new WeaponRules(4, 1, 5, 100, 100, 100, 20, 5, true, 30, 60, 70, 150, 15, 120, 150),
                new EnemyTraits(35, 15, 90, 10, 30, 20, 10, 1500)),
        };

        datos.BuildIndexes();
        return datos;
    }

    // Las formas del JSON tal como salen del importador.
    private sealed class ArchivoEnemigos
    {
        public int Version { get; set; }
        public List<EnemigoJson>? Enemies { get; set; }
    }

    private sealed class EnemigoJson
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Area { get; set; }
        public int ScaledHp { get; set; }
        public int MinDamage { get; set; }
        public int MaxDamage { get; set; }
        public int Speed { get; set; }
        public int CadenceTicks { get; set; }
        public int Gold { get; set; }
        public int Body { get; set; }
        public int VisualScale { get; set; } = 100;
        public string? Graphic { get; set; }
    }
}
