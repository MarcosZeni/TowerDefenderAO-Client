using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>Una región de una hoja: qué recortar y cuántos cuadros tiene.</summary>
/// <remarks>
/// <para><b><see cref="Regions"/> manda sobre X/Y/Ancho/Alto.</b> Los sueltos son
/// los del primer cuadro y están siempre, para quien solo quiere una imagen fija;
/// la animación se dibuja con el array.</para>
///
/// <para>⚠ <b>Los cuadros NO tienen todos el mismo ancho.</b> En el arte de
/// Austral un mismo movimiento cambia de ancho entre cuadro y cuadro, así que
/// "correr la región de a Ancho píxeles" corta mal a partir del segundo. Por eso
/// cada cuadro trae su propio rectángulo.</para>
/// </remarks>
public sealed record Graphic(
    string Name, int FileNum, int X, int Y, int Width, int Height, int Frames, int MsPerFrame,
    Rect2I[] Regions, TileGrid? Grid = null, int Scale = 4, int AnchorY = 0);

/// <summary>Una hoja de tiles de piso: todos del mismo lado, en grilla regular.</summary>
/// <remarks>
/// Las hojas de piso de Austral son grillas perfectas —la de pasto son 16 columnas
/// × 4 filas, 64 tiles—, así que entran al índice como <b>una</b> entrada con tres
/// números en vez de 64 entradas sueltas. Con seis paletas eso es la diferencia
/// entre 6 entradas y 400.
/// </remarks>
public sealed record TileGrid(
    int Columns, int Rows, int Count, int Side, int[]? Usable = null);

/// <summary>
/// Lee <c>Resources\graficos.json</c> y resuelve nombre → textura recortada.
/// </summary>
/// <remarks>
/// <para>⚠ <c>Resources\</c> es <b>hermana</b> de <c>Client\</c>, no está adentro
/// del proyecto Godot. Es a propósito: así Godot no la importa, no genera
/// <c>.import</c> ahí, el <c>.pck</c> principal queda chico y el arte se puede
/// parchear aparte. La contra es que los PNG se cargan a mano, nunca con
/// <c>GD.Load</c>: en el editor salen de la carpeta suelta y en una build
/// exportada del <c>arte.pck</c> que monta <see cref="ArtPack"/>.</para>
///
/// <para>Un <c>N.PNG</c> no es un sprite: es una <b>hoja</b> con 8,7 regiones en
/// promedio. Por eso cada gráfico es una <see cref="AtlasTexture"/> sobre la hoja
/// compartida — cargar la hoja una vez y recortar N veces, en vez de N imágenes.</para>
/// </remarks>
public static class GraphicsLoader
{
    /// <summary>
    /// Dónde está el arte. Lo decide <see cref="ArtPack"/> al arrancar.
    /// </summary>
    /// <remarks>
    /// En el editor es <c>res://../Resources</c>, la carpeta hermana del proyecto.
    /// En una build exportada es <c>res://Art</c>, el punto de montaje del
    /// <c>arte.pck</c> — y ahí las dos son indistinguibles para esta clase, que es
    /// justamente lo que hace que Android funcione sin tocar nada de acá abajo.
    /// </remarks>
    private static string Carpeta => ArtPack.Root;

    private static readonly Dictionary<string, Graphic> _indice = new(System.StringComparer.Ordinal);
    private static readonly Dictionary<int, Texture2D> _hojas = [];
    private static readonly Dictionary<string, AtlasTexture> _recortes = new(System.StringComparer.Ordinal);

    private static bool _cargado;

    public static IReadOnlyDictionary<string, Graphic> Index
    {
        get
        {
            Load();
            return _indice;
        }
    }

    /// <summary>
    /// La textura de un gráfico, ya recortada de su hoja.
    /// </summary>
    /// <returns><c>null</c> si el nombre no está en el índice o falta el archivo.</returns>
    public static Texture2D? Texture(string nombre)
    {
        Load();
        return Texture(nombre, 0);
    }

    /// <summary>
    /// Un cuadro concreto de un gráfico animado.
    /// </summary>
    /// <remarks>
    /// El índice se toma <b>módulo la cantidad de cuadros</b>, así quien dibuja
    /// puede pasarle un contador que crece sin freno y no tiene que acordarse de
    /// cuántos cuadros tiene cada bicho.
    /// </remarks>
    /// <returns><c>null</c> si el nombre no está en el índice o falta el archivo.</returns>
    public static Texture2D? Texture(string nombre, int cuadro)
    {
        Load();

        if (!_indice.TryGetValue(nombre, out var g))
            return null;

        int cuantos = g.Regions.Length;

        if (cuantos == 0)
            return null;

        // El resto de C# es negativo para negativos, y un contador que va para
        // atrás daría índice fuera de rango.
        int i = ((cuadro % cuantos) + cuantos) % cuantos;

        string clave = i == 0 ? nombre : $"{nombre}#{i}";

        if (_recortes.TryGetValue(clave, out var listo))
            return listo;

        var hoja = Hoja(g.FileNum);

        if (hoja is null)
            return null;

        var r = g.Regions[i];

        var recorte = new AtlasTexture
        {
            Atlas = hoja,
            Region = new Rect2(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y),
        };

        _recortes[clave] = recorte;
        return recorte;
    }

    /// <summary>Los datos de un gráfico, sin cargar su textura.</summary>
    public static Graphic? Data(string nombre)
    {
        Load();
        return _indice.TryGetValue(nombre, out var g) ? g : null;
    }

    /// <summary>
    /// Un tile de piso de un tileset, por número.
    /// </summary>
    /// <remarks>
    /// <para>El índice se toma <b>módulo la cantidad</b>, igual que los cuadros de
    /// una animación: quien decora el mapa puede tirar un número cualquiera y no
    /// tiene que saber cuántos tiles trae cada paleta.</para>
    ///
    /// <para>⚠ <b>Los tiles vacíos no se devuelven nunca.</b> Hay hojas de Austral
    /// con media grilla sin dibujar —la de arena tiene 96 casillas y 62 con
    /// contenido—, y el importador deja anotadas cuáles sirven. Acá se traduce el
    /// número pedido a una casilla con dibujo, así el que decora no puede pintar un
    /// agujero en el piso ni tiene que saber que el problema existe.</para>
    /// </remarks>
    /// <returns><c>null</c> si ese nombre no es un tileset o falta el archivo.</returns>
    public static Texture2D? Tile(string tileset, int indice)
    {
        Load();

        if (!_indice.TryGetValue(tileset, out var g) || g.Grid is not { } grilla)
            return null;

        if (grilla.Count <= 0 || grilla.Columns <= 0)
            return null;

        int i;

        if (grilla.Usable is { Length: > 0 } usables)
        {
            int u = ((indice % usables.Length) + usables.Length) % usables.Length;
            i = usables[u];
        }
        else
        {
            i = ((indice % grilla.Count) + grilla.Count) % grilla.Count;
        }

        string clave = $"{tileset}#t{i}";

        if (_recortes.TryGetValue(clave, out var listo))
            return listo;

        var hoja = Hoja(g.FileNum);

        if (hoja is null)
            return null;

        int columna = i % grilla.Columns;
        int fila = i / grilla.Columns;

        var recorte = new AtlasTexture
        {
            Atlas = hoja,

            // La grilla arranca donde arranca el primer tile, no siempre en (0,0).
            Region = new Rect2(
                g.X + (columna * grilla.Side),
                g.Y + (fila * grilla.Side),
                grilla.Side,
                grilla.Side),
        };

        _recortes[clave] = recorte;
        return recorte;
    }

    /// <summary>
    /// La hoja entera como <see cref="Image"/>, para hornear en CPU.
    /// </summary>
    /// <remarks>
    /// <para>Existe porque una <see cref="AtlasTexture"/> sirve para dibujar pero
    /// no para <b>componer</b>: el piso se arma una vez pegando cientos de tiles
    /// en un lienzo, y eso se hace con <see cref="Image.BlitRect"/> sobre píxeles,
    /// no con llamadas de dibujo.</para>
    ///
    /// <para>Devuelve una copia, así que el que hornea la usa y la suelta. La hoja
    /// en sí queda cacheada como textura, que es lo que ocupa lugar.</para>
    /// </remarks>
    public static Image? SheetImage(int fileNum)
    {
        Load();
        return Hoja(fileNum)?.GetImage();
    }

    /// <summary>Carga una hoja de disco, una sola vez.</summary>
    private static Texture2D? Hoja(int fileNum)
    {
        if (_hojas.TryGetValue(fileNum, out var cacheada))
            return cacheada;

        // ⚠ Se prueban las dos capitalizaciones. De las 1312 hojas de la fuente,
        // 41 terminan en .png minúscula: en Windows da igual, en Linux son
        // archivos distintos y la carga falla sin decir por qué.
        foreach (string extension in new[] { ".PNG", ".png" })
        {
            string ruta = $"{Carpeta}/{fileNum}{extension}";

            // ⚠ Va por ArtPack y no por Image.LoadFromFile: adentro de un .pck no
            // hay ruta del sistema de archivos que darle.
            var imagen = ArtPack.LoadImage(ruta);

            if (imagen is null)
                continue;

            var textura = ImageTexture.CreateFromImage(imagen);
            _hojas[fileNum] = textura;

            return textura;
        }

        GD.PushWarning($"Falta la hoja {fileNum}.PNG en {Carpeta}");
        return null;
    }

    private static void Load()
    {
        if (_cargado)
            return;

        _cargado = true;

        string ruta = $"{Carpeta}/graficos.json";

        using var f = ArchivoGodot.Open(ruta, ArchivoGodot.ModeFlags.Read);

        if (f is null)
        {
            GD.PushWarning($"No se pudo leer {ruta}. El juego dibuja con primitivas.");
            return;
        }

        ArchivoGraficos? archivo;

        try
        {
            archivo = JsonSerializer.Deserialize<ArchivoGraficos>(
                f.GetAsText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException e)
        {
            GD.PushError($"{ruta} no es JSON válido: {e.Message}");
            return;
        }

        if (archivo?.Graphics is null)
            return;

        foreach (var (nombre, g) in archivo.Graphics)
        {
            Rect2I[] regiones;

            if (g.Regions is { Count: > 0 })
            {
                regiones = new Rect2I[g.Regions.Count];

                for (int i = 0; i < g.Regions.Count; i++)
                {
                    int[] r = g.Regions[i];

                    // Un cuadro mal escrito se salta y se usa el primero: mejor
                    // una animación que se traba que un recorte de tamaño cero,
                    // que en pantalla es un sprite invisible sin ningún aviso.
                    regiones[i] = r.Length == 4
                        ? new Rect2I(r[0], r[1], r[2], r[3])
                        : new Rect2I(g.X, g.Y, g.W, g.H);
                }
            }
            else
            {
                regiones = [new Rect2I(g.X, g.Y, g.W, g.H)];
            }

            _indice[nombre] = new Graphic(
                nombre, g.FileNum, g.X, g.Y, g.W, g.H,
                regiones.Length,
                g.MsPerFrame > 0 ? g.MsPerFrame : 120,
                regiones,
                g.Grid is null
                    ? null
                    : new TileGrid(
                        g.Grid.Columns, g.Grid.Rows, g.Grid.Count, g.Grid.Side,
                        g.Grid.Usable?.ToArray()),
                g.Scale > 0 ? g.Scale : 4,
                g.AnchorY);
        }

        GD.Print($"Gráficos: {_indice.Count} entradas en el índice");
    }

    // ⚠ LOS [JsonPropertyName] SON OBLIGATORIOS ACÁ, no cosmética.
    //
    // `Resources\graficos.json` es un índice de DATOS y sus claves están en
    // español —«graficos», «regiones», «msPorFrame», «grilla», «lado»—. Las
    // propiedades pasaron a inglés como el resto del código, y sin el atributo el
    // binder no matchea: una animación se quedaría en un solo cuadro y un tileset
    // sin grilla, sin error, sin excepción y sin una línea en la consola.
    //
    // Es el peor modo de fallar que tiene este archivo, y es exactamente por lo
    // que el índice se lee con nombres explícitos y no por convención.
    private sealed class GrillaJson
    {
        [JsonPropertyName("columnas")]
        public int Columns { get; set; }

        [JsonPropertyName("filas")]
        public int Rows { get; set; }

        [JsonPropertyName("cantidad")]
        public int Count { get; set; }

        [JsonPropertyName("lado")]
        public int Side { get; set; }

        /// <summary>Casillas con dibujo. Falta cuando la hoja está entera.</summary>
        [JsonPropertyName("usables")]
        public List<int>? Usable { get; set; }
    }

    private sealed class ArchivoGraficos
    {
        public int Version { get; set; }

        [JsonPropertyName("graficos")]
        public Dictionary<string, GraficoJson>? Graphics { get; set; }
    }

    private sealed class GraficoJson
    {
        public int FileNum { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public int Frames { get; set; }

        [JsonPropertyName("msPorFrame")]
        public int MsPerFrame { get; set; }

        /// <summary>Un <c>[x, y, w, h]</c> por cuadro. Puede no estar: entonces hay uno solo.</summary>
        [JsonPropertyName("regiones")]
        public List<int[]>? Regions { get; set; }

        /// <summary>Solo en los tilesets de piso: columnas, filas, cantidad y lado.</summary>
        [JsonPropertyName("grilla")]
        public GrillaJson? Grid { get; set; }

        /// <summary>
        /// Cuánto bajar el sprite al dibujarlo, en píxeles de la hoja.
        /// </summary>
        /// <remarks>
        /// <para>El sprite se ancla al borde <b>inferior</b> de su región, que es
        /// como se para un muñeco de Argentum Online sobre su tile. Con la caja ya
        /// recortada al dibujo eso deja al bicho apoyado, pero no siempre <b>se
        /// ve</b> apoyado: una criatura de perfil es ancha y baja, y parada en el
        /// borde de abajo de la celda su masa queda por encima de la línea del
        /// camino. La Tortuga Gigante yendo de izquierda a derecha fue el
        /// caso.</para>
        ///
        /// <para>Es una corrección de <b>render</b> y por entrada —o sea, por
        /// dirección—, porque el problema es por dirección: de frente el mismo
        /// bicho se ve bien. Positivo baja. Se escribe con
        /// <c>--anclar-grafico</c> y se tantea en <c>Scenes\SpritePreview.tscn</c>
        /// con <c>[</c> y <c>]</c>.</para>
        /// </remarks>
        [JsonPropertyName("anclaY")]
        public int AnchorY { get; set; }

        /// <summary>
        /// En qué escala está guardado el arte de esta entrada: 4 = x4, 2 = x2.
        /// </summary>
        /// <remarks>
        /// ⚠ Sin el campo vale 4, que es cómo entró todo el arte hasta que
        /// <c>--reescalar-hoja</c> empezó a fabricar hojas más chicas. Quien
        /// dibuja tiene que compensarla o el sprite sale a la mitad de tamaño.
        /// </remarks>
        [JsonPropertyName("escala")]
        public int Scale { get; set; } = 4;
    }
}
