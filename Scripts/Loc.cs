using System.Collections.Generic;
using System.Text.Json;
using Godot;
using TowerOfArgentum.Simulation;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// Los textos, por idioma. <c>Loc.Get("perk.filo.name")</c>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Los idiomas NO entran al hash de <c>/run/start</c>.</b> Es la única
/// carpeta de <c>Data\</c> que queda afuera, y es a propósito: traducir no
/// cambia la simulación —un jugador en inglés y uno en español juegan
/// exactamente la misma corrida con la misma seed—. Si entrara al hash, corregir
/// una tilde invalidaría el ranking de la temporada.</para>
///
/// <para><b>Y por eso mismo vive acá y no en <c>Simulation\</c>:</b> el servidor
/// revalida corridas y no necesita saber en qué idioma se jugaron.</para>
///
/// <para>⚠ <b>Una clave que falta se muestra cruda</b> —<c>perk.filo.name</c>—
/// en vez de caer a un texto vacío o a un guion. Es feo a propósito: un texto sin
/// traducir tiene que verse en la primera pasada, no descubrirse cuando lo
/// reporta un jugador.</para>
/// </remarks>
public static class Loc
{
    private const string Carpeta = "res://Data/lang";

    /// <summary>
    /// El idioma al que se cae cuando falta una clave.
    /// </summary>
    /// <remarks>
    /// Español porque es el idioma en el que se escribe el juego: cuando alguien
    /// agrega un texto nuevo, lo agrega acá primero, y los demás idiomas lo
    /// heredan hasta que alguien los traduzca.
    /// </remarks>
    public const string Reference = "es";

    private static readonly Dictionary<string, string> _actual = [];
    private static readonly Dictionary<string, string> _respaldo = [];

    /// <summary>El código del idioma cargado.</summary>
    public static string Code { get; private set; } = "";

    /// <summary>Los idiomas que hay en disco, ordenados. Para el menú de opciones.</summary>
    public static List<(string Code, string Nombre)> Disponibles()
    {
        var salida = new List<(string, string)>();

        using var dir = DirAccess.Open(Carpeta);

        if (dir is null)
            return salida;

        var nombres = new List<string>(dir.GetFiles());
        nombres.Sort(System.StringComparer.Ordinal);

        foreach (string archivo in nombres)
        {
            if (!archivo.EndsWith(".json", System.StringComparison.Ordinal))
                continue;

            string codigo = archivo[..^5];
            var (nombre, _) = LeerArchivo(codigo);

            if (nombre.Length > 0)
                salida.Add((codigo, nombre));
        }

        return salida;
    }

    /// <summary>
    /// Carga un idioma. Si no está, se queda con el de referencia.
    /// </summary>
    public static void Load(string codigo)
    {
        _respaldo.Clear();
        _actual.Clear();

        var (_, textosRef) = LeerArchivo(Reference);

        foreach (var (k, v) in textosRef)
            _respaldo[k] = v;

        if (string.Equals(codigo, Reference, System.StringComparison.Ordinal))
        {
            Code = Reference;
            GD.Print($"Idioma: {Reference} · {_respaldo.Count} textos");
            return;
        }

        var (nombre, textos) = LeerArchivo(codigo);

        if (textos.Count == 0)
        {
            GD.PushWarning($"El idioma «{codigo}» no se pudo leer. Se usa «{Reference}».");
            Code = Reference;
            return;
        }

        foreach (var (k, v) in textos)
            _actual[k] = v;

        Code = codigo;

        // Cuántas claves le faltan al idioma respecto del de referencia. Sale por
        // consola y no como error: jugar con la mitad traducida es válido, pero
        // el que traduce necesita el número.
        int faltan = 0;

        foreach (var k in _respaldo.Keys)
        {
            if (!_actual.ContainsKey(k))
                faltan++;
        }

        GD.Print(
            $"Idioma: {nombre} ({codigo}) · {_actual.Count} textos" +
            (faltan > 0 ? $" · {faltan} sin traducir, se ven en {Reference}" : ""));
    }

    /// <summary>
    /// El texto de esa clave. Si no existe en ningún idioma, devuelve la clave.
    /// </summary>
    public static string Get(string clave)
    {
        if (_actual.TryGetValue(clave, out string? v) && v.Length > 0)
            return v;

        if (_respaldo.TryGetValue(clave, out string? r) && r.Length > 0)
            return r;

        // ⚠ La clave cruda, no un vacío. Un texto faltante tiene que verse.
        return clave;
    }

    /// <summary>El texto con <c>{m}</c> reemplazado por un número.</summary>
    public static string Get(string clave, int magnitud)
        => Get(clave).Replace(
            "{m}",
            magnitud.ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.StringComparison.Ordinal);

    // -------------------------------------------------------------------------
    //  La escala interna, de vuelta a números que se leen
    // -------------------------------------------------------------------------
    //
    //  ⚠ Desde la propuesta V2 la simulación NO trabaja en las unidades de los
    //  datos: el daño y el HP van en centésimas de punto y el alcance en
    //  milésimas de celda, para que un +8% de nivel o un +15% de alcance no se
    //  trunquen a cero. Eso es correcto adentro y es ilegible afuera: un defensor
    //  que pega 10,4 no puede decir «1040 de daño» en pantalla.
    //
    //  Estos tres traducen. Van acá y no en `Ints` porque necesitan el separador
    //  decimal del idioma, y `Simulation\` no sabe en qué idioma se juega.

    /// <summary>Puntos enteros de un valor interno en centésimas. Para el HP.</summary>
    /// <remarks>
    /// El HP va sin decimal a propósito: la Torre tiene 1000 y un enemigo 8400,
    /// así que la centésima no aporta nada y solo agrega ruido a la barra.
    /// </remarks>
    public static string Points(int centesimas)
        => Simulation.Ints.ToPoints(centesimas)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Daño con un decimal: <c>1040</c> se lee «10,4».</summary>
    public static string Damage(int centesimas) => UnDecimal(centesimas, 100);

    /// <summary>Alcance en celdas con un decimal: <c>2800</c> se lee «2,8».</summary>
    public static string Range(int milesimas) => UnDecimal(milesimas, 1000);

    /// <summary>
    /// Un decimal, con enteros y con el separador del idioma.
    /// </summary>
    /// <remarks>
    /// Redondea a la décima más cercana sumando media décima antes de dividir. Es
    /// la misma cuenta que haría un <c>ToString("F1")</c> y no arrastra coma
    /// flotante, que es la regla que este proyecto sostiene incluso donde no haría
    /// daño: si acá se cuela una, mañana se cuela en la simulación por copiar y
    /// pegar.
    /// </remarks>
    private static string UnDecimal(int valor, int escala)
    {
        int decimas = ((valor * 10) + (escala / 2)) / escala;
        string separador = Get("ui.format.decimal");

        return $"{decimas / 10}{separador}{decimas % 10}";
    }

    // -------------------------------------------------------------------------
    //  Atajos para lo que sale de los datos
    // -------------------------------------------------------------------------

    public static string Perk(PerkDefinition p) => Get($"perk.{p.Key}.name");

    public static string Class(DefenderClass c) => Get($"class.{c}.name");

    /// <summary>El oficio de una clase: «Tanque», «Control», «Soporte».</summary>
    /// <remarks>
    /// Es la línea de abajo del nombre en la tarjeta del roster, que el diseño
    /// pide. No es un dato de balance —no toca el tick— y por eso vive en
    /// <c>lang\</c> y no en <c>clases.json</c>.
    /// </remarks>
    public static string Role(DefenderClass c) => Get($"class.{c}.role");

    public static string Tier(PerkTier t) => Get($"tier.{t}.name");

    public static string Effect(PerkEffect e) => Get($"effect.{e}.name");

    public static string Ability(WeaponAbility a)
        => a == WeaponAbility.None ? "" : Get($"ability.{a}.name");

    /// <summary>Qué hace la habilidad, en una línea, con su magnitud puesta.</summary>
    public static string AbilityText(WeaponAbility a, int magnitud)
        => a == WeaponAbility.None ? "" : Get($"ability.{a}.text", magnitud);

    /// <summary>
    /// El nombre de un arma generada.
    /// </summary>
    /// <remarks>
    /// Se arma acá y no en el generador porque es texto: el generador vive en
    /// <c>Simulation\</c>, corre igual en el servidor y no sabe de idiomas. Lo
    /// que guarda el arma son las tres cosas de las que sale el nombre —qué
    /// número quedó más alto, la rareza y la habilidad—, no una cadena.
    /// </remarks>
    public static string Weapon(Simulation.Weapon arma)
    {
        string baseNombre = Get($"weapon.base.{arma.BaseKind}");

        if (arma.Ability != WeaponAbility.None)
            return $"{baseNombre} {Ability(arma.Ability)}";

        return arma.Rarity == WeaponRarity.Common
            ? baseNombre
            : $"{baseNombre} {Get($"rarity.{arma.Rarity}.suffix")}";
    }

    // -------------------------------------------------------------------------

    private static (string Nombre, Dictionary<string, string> Textos) LeerArchivo(string codigo)
    {
        var textos = new Dictionary<string, string>(System.StringComparer.Ordinal);

        using var f = ArchivoGodot.Open($"{Carpeta}/{codigo}.json", ArchivoGodot.ModeFlags.Read);
        string crudo = f?.GetAsText() ?? "";

        if (crudo.Length == 0)
            return ("", textos);

        try
        {
            // Las mismas opciones que las tablas de balance: comentarios y comas
            // colgando. Son archivos que edita una persona.
            using var doc = TableReader.ParseJson(crudo, $"lang/{codigo}.json");
            var raiz = doc.RootElement;

            string nombre = raiz.TryGetProperty("language", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? codigo
                : codigo;

            if (raiz.TryGetProperty("strings", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in s.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        textos[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            return (nombre, textos);
        }
        catch (InvalidBalanceDataException e)
        {
            GD.PushWarning($"lang/{codigo}.json: {e.Message}");
            return ("", textos);
        }
    }
}
