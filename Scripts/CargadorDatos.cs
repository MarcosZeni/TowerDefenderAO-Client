using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using TowerOfArgentum.Simulacion;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// Lee los JSON de <c>Datos\</c> y arma el <see cref="DatosBalance"/>.
/// </summary>
/// <remarks>
/// <para>Está del lado del cliente y no en <c>Simulacion\</c> a propósito: leer
/// archivos necesita Godot, y la simulación no puede depender del motor. La
/// simulación recibe los datos ya cargados, así que el mismo código corre en el
/// servidor —que los va a leer de otro lado— sin cambiar una línea.</para>
/// </remarks>
public static class CargadorDatos
{
    private const string RutaEnemigos = "res://Datos/enemigos.json";

    private static DatosBalance? _cache;

    /// <summary>Los datos de la temporada actual.</summary>
    public static DatosBalance Actual => _cache ??= Cargar();

    private static DatosBalance Cargar()
    {
        string json = LeerTexto(RutaEnemigos);

        if (string.IsNullOrEmpty(json))
        {
            GD.PushError(
                $"No se pudo leer {RutaEnemigos}. Corré el importador primero:\n" +
                "  Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --importar-npcs");

            return Vacio();
        }

        var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        ArchivoEnemigos? archivo;

        try
        {
            archivo = JsonSerializer.Deserialize<ArchivoEnemigos>(json, opciones);
        }
        catch (JsonException e)
        {
            GD.PushError($"{RutaEnemigos} no es JSON válido: {e.Message}");
            return Vacio();
        }

        if (archivo?.Enemigos is null || archivo.Enemigos.Count == 0)
        {
            GD.PushError($"{RutaEnemigos} no trae enemigos.");
            return Vacio();
        }

        var enemigos = new List<DatoEnemigo>(archivo.Enemigos.Count);

        foreach (var e in archivo.Enemigos)
        {
            enemigos.Add(new DatoEnemigo
            {
                Id = e.Id,
                Nombre = e.Nombre ?? "?",
                Area = e.Area,
                Hp = e.HpEscalado,
                DanoMin = e.DanoMin,
                DanoMax = e.DanoMax,
                Velocidad = e.Velocidad,
                CadenciaTicks = e.CadenciaTicks,
                Oro = e.Oro,
                Body = e.Body,
                EscalaVisual = e.EscalaVisual,
                Grafico = e.Grafico ?? "",
            });
        }

        var (layouts, textoLayouts) = CargarLayouts();

        var datos = new DatosBalance
        {
            Version = archivo.Version,

            // ⚠ El hash cubre el bestiario Y los layouts.
            //
            // Un layout es entrada de la simulación igual que un enemigo: cambiarle
            // una celda cambia el recorrido, el tiempo de tiro y el resultado. Si
            // no entrara al hash, el servidor revalidaría con otros mapas y
            // rechazaría corridas honestas.
            Hash = HashDe(json + textoLayouts),

            Enemigos = enemigos,
            Layouts = layouts,
        };

        datos.Indexar();

        GD.Print($"Datos v{datos.Version} · {enemigos.Count} enemigos · hash {datos.Hash[..12]}…");

        return datos;
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
    /// Lee los layouts de <c>Datos/layouts/</c> y los arma.
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
    private static (List<Mapa> Mapas, string Texto) CargarLayouts()
    {
        const string carpeta = "res://Datos/layouts";

        var mapas = new List<Mapa>();
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

            var filas = FilasDe(contenido);

            if (filas is null)
            {
                GD.PushWarning($"{nombre}: no tiene «filas»");
                continue;
            }

            var mapa = LayoutAscii.Construir(filas);

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

    private static List<string>? FilasDe(string contenido)
    {
        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(contenido);
            var filas = json?["filas"]?.AsArray();

            if (filas is null)
                return null;

            var lista = new List<string>(filas.Count);

            foreach (var f in filas)
                lista.Add(f?.GetValue<string>() ?? "");

            return lista;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Datos vacíos, para que el juego no explote si falta el archivo.
    /// </summary>
    /// <remarks>
    /// El hash queda en <c>"sin-datos"</c>, que no va a coincidir con el del
    /// servidor: la corrida se rechaza al abrirse en vez de jugarse con un
    /// bestiario vacío y reportar un resultado sin sentido.
    /// </remarks>
    private static DatosBalance Vacio()
    {
        var datos = new DatosBalance
        {
            Version = 0,
            Hash = "sin-datos",
            Enemigos = [],
            Layouts = [],
        };

        datos.Indexar();
        return datos;
    }

    // Las formas del JSON tal como salen del importador.
    private sealed class ArchivoEnemigos
    {
        public int Version { get; set; }
        public List<EnemigoJson>? Enemigos { get; set; }
    }

    private sealed class EnemigoJson
    {
        public int Id { get; set; }
        public string? Nombre { get; set; }
        public int Area { get; set; }
        public int HpEscalado { get; set; }
        public int DanoMin { get; set; }
        public int DanoMax { get; set; }
        public int Velocidad { get; set; }
        public int CadenciaTicks { get; set; }
        public int Oro { get; set; }
        public int Body { get; set; }
        public int EscalaVisual { get; set; } = 100;
        public string? Grafico { get; set; }
    }
}
