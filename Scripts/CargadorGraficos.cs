using System.Collections.Generic;
using System.Text.Json;
using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>Una región de una hoja: qué recortar y cuántos cuadros tiene.</summary>
/// <remarks>
/// <para><b><see cref="Regiones"/> manda sobre X/Y/Ancho/Alto.</b> Los sueltos son
/// los del primer cuadro y están siempre, para quien solo quiere una imagen fija;
/// la animación se dibuja con el array.</para>
///
/// <para>⚠ <b>Los cuadros NO tienen todos el mismo ancho.</b> En el arte de
/// Austral un mismo movimiento cambia de ancho entre cuadro y cuadro, así que
/// "correr la región de a Ancho píxeles" corta mal a partir del segundo. Por eso
/// cada cuadro trae su propio rectángulo.</para>
/// </remarks>
public sealed record Graphic(
    string Name, int FileNum, int X, int Y, int Width, int Height, int Frames, int MsPorFrame,
    Rect2I[] Regiones, GrillaTiles? Grilla = null);

/// <summary>Una hoja de tiles de piso: todos del mismo lado, en grilla regular.</summary>
/// <remarks>
/// Las hojas de piso de Austral son grillas perfectas —la de pasto son 16 columnas
/// × 4 filas, 64 tiles—, así que entran al índice como <b>una</b> entrada con tres
/// números en vez de 64 entradas sueltas. Con seis paletas eso es la diferencia
/// entre 6 entradas y 400.
/// </remarks>
public sealed record GrillaTiles(
    int Columnas, int Filas, int Count, int Lado, int[]? Usables = null);

/// <summary>
/// Lee <c>Resources\graficos.json</c> y resuelve nombre → textura recortada.
/// </summary>
/// <remarks>
/// <para>⚠ <c>Resources\</c> es <b>hermana</b> de <c>Client\</c>, no está adentro
/// del proyecto Godot. Es a propósito: así Godot no la importa, no genera
/// <c>.import</c> ahí, el <c>.pck</c> queda chico y el arte se puede parchear
/// aparte. La contra es que no se puede usar <c>res://</c> y hay que cargar los
/// PNG a mano desde disco.</para>
///
/// <para>Un <c>N.PNG</c> no es un sprite: es una <b>hoja</b> con 8,7 regiones en
/// promedio. Por eso cada gráfico es una <see cref="AtlasTexture"/> sobre la hoja
/// compartida — cargar la hoja una vez y recortar N veces, en vez de N imágenes.</para>
/// </remarks>
public static class CargadorGraficos
{
    /// <summary>
    /// Dónde está el arte, relativo al ejecutable.
    /// </summary>
    /// <remarks>
    /// En el editor, <c>res://</c> apunta a <c>Client\</c>, así que
    /// <c>../Resources</c> es la carpeta hermana. En una build exportada el arte
    /// se copia al lado del ejecutable.
    /// </remarks>
    private const string Carpeta = "res://../Resources";

    private static readonly Dictionary<string, Graphic> _indice = new(System.StringComparer.Ordinal);
    private static readonly Dictionary<int, Texture2D> _hojas = [];
    private static readonly Dictionary<string, AtlasTexture> _recortes = new(System.StringComparer.Ordinal);

    private static bool _cargado;

    public static IReadOnlyDictionary<string, Graphic> Index
    {
        get
        {
            Cargar();
            return _indice;
        }
    }

    /// <summary>
    /// La textura de un gráfico, ya recortada de su hoja.
    /// </summary>
    /// <returns><c>null</c> si el nombre no está en el índice o falta el archivo.</returns>
    public static Texture2D? Textura(string nombre)
    {
        Cargar();
        return Textura(nombre, 0);
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
    public static Texture2D? Textura(string nombre, int cuadro)
    {
        Cargar();

        if (!_indice.TryGetValue(nombre, out var g))
            return null;

        int cuantos = g.Regiones.Length;

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

        var r = g.Regiones[i];

        var recorte = new AtlasTexture
        {
            Atlas = hoja,
            Region = new Rect2(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y),
        };

        _recortes[clave] = recorte;
        return recorte;
    }

    /// <summary>Los datos de un gráfico, sin cargar su textura.</summary>
    public static Graphic? Datos(string nombre)
    {
        Cargar();
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
        Cargar();

        if (!_indice.TryGetValue(tileset, out var g) || g.Grilla is not { } grilla)
            return null;

        if (grilla.Count <= 0 || grilla.Columnas <= 0)
            return null;

        int i;

        if (grilla.Usables is { Length: > 0 } usables)
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

        int columna = i % grilla.Columnas;
        int fila = i / grilla.Columnas;

        var recorte = new AtlasTexture
        {
            Atlas = hoja,

            // La grilla arranca donde arranca el primer tile, no siempre en (0,0).
            Region = new Rect2(
                g.X + (columna * grilla.Lado),
                g.Y + (fila * grilla.Lado),
                grilla.Lado,
                grilla.Lado),
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
    public static Image? ImagenDeHoja(int fileNum)
    {
        Cargar();
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

            if (!ArchivoGodot.FileExists(ruta))
                continue;

            var imagen = Image.LoadFromFile(ProjectSettings.GlobalizePath(ruta));

            if (imagen is null)
                continue;

            var textura = ImageTexture.CreateFromImage(imagen);
            _hojas[fileNum] = textura;

            return textura;
        }

        GD.PushWarning($"Falta la hoja {fileNum}.PNG en {Carpeta}");
        return null;
    }

    private static void Cargar()
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

        if (archivo?.Graficos is null)
            return;

        foreach (var (nombre, g) in archivo.Graficos)
        {
            Rect2I[] regiones;

            if (g.Regiones is { Count: > 0 })
            {
                regiones = new Rect2I[g.Regiones.Count];

                for (int i = 0; i < g.Regiones.Count; i++)
                {
                    int[] r = g.Regiones[i];

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
                g.MsPorFrame > 0 ? g.MsPorFrame : 120,
                regiones,
                g.Grilla is null
                    ? null
                    : new GrillaTiles(
                        g.Grilla.Columnas, g.Grilla.Filas, g.Grilla.Count, g.Grilla.Lado,
                        g.Grilla.Usables?.ToArray()));
        }

        GD.Print($"Gráficos: {_indice.Count} entradas en el índice");
    }

    private sealed class GrillaJson
    {
        public int Columnas { get; set; }
        public int Filas { get; set; }
        public int Count { get; set; }
        public int Lado { get; set; }

        /// <summary>Casillas con dibujo. Falta cuando la hoja está entera.</summary>
        public List<int>? Usables { get; set; }
    }

    private sealed class ArchivoGraficos
    {
        public int Version { get; set; }
        public Dictionary<string, GraficoJson>? Graficos { get; set; }
    }

    private sealed class GraficoJson
    {
        public int FileNum { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public int Frames { get; set; }
        public int MsPorFrame { get; set; }

        /// <summary>Un <c>[x, y, w, h]</c> por cuadro. Puede no estar: entonces hay uno solo.</summary>
        public List<int[]>? Regiones { get; set; }

        /// <summary>Solo en los tilesets de piso: columnas, filas, cantidad y lado.</summary>
        public GrillaJson? Grilla { get; set; }
    }
}
