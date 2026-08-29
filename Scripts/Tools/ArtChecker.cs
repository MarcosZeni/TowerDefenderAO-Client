using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum.Tools;

/// <summary>
/// Revisa el arte ya traído, mirando los píxeles.
/// </summary>
/// <remarks>
/// <para>Los chequeos del traedor son de geometría: que la región quepa, que el
/// archivo llegue completo. Este mira el <b>contenido</b>, que es lo único que
/// atrapa dos cosas que la geometría deja pasar:</para>
///
/// <list type="bullet">
/// <item><b>Regiones vacías.</b> Una región bien formada sobre una parte
/// transparente de la hoja: el sprite existe en el índice y no se ve nada.</item>
/// <item><b>Magenta de relleno.</b> Las hojas de Austral usan fucsia
/// (255,0,255) para marcar cuadros que no existen. Si una región cae encima, el
/// jugador ve un rectángulo rosa.</item>
/// </list>
///
/// <code>
/// --verificar-arte
/// </code>
/// </remarks>
public static class ArtChecker
{
    private static readonly string CarpetaResources =
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "Resources"));

    /// <summary>Cuántos píxeles opacos hacen falta para no considerarla vacía.</summary>
    private const int MinimoOpacos = 20;

    /// <summary>Porcentaje de magenta a partir del cual se reporta.</summary>
    private const int MaxMagentaPorciento = 10;

    public static void Execute()
    {
        GD.Print("── Verificación del arte ──────────────────────────");

        string rutaIndice = Path.Combine(CarpetaResources, "graficos.json");

        if (!File.Exists(rutaIndice))
        {
            GD.PrintErr("  ✗ no está graficos.json");
            return;
        }

        var json = JsonNode.Parse(File.ReadAllText(rutaIndice));
        var graficos = json?["graficos"]?.AsObject();

        if (graficos is null || graficos.Count == 0)
        {
            GD.PrintErr("  ✗ el índice está vacío");
            return;
        }

        // Las hojas se cargan una vez y se reusan: son 43 archivos de hasta 1 MB y
        // el índice tiene 113 entradas que las comparten.
        var hojas = new Dictionary<int, Image>();

        int revisadas = 0;
        int vacias = 0;
        int conMagenta = 0;
        int fuera = 0;

        foreach (var (nombre, valor) in graficos)
        {
            if (valor is null)
                continue;

            int fileNum = valor["fileNum"]!.GetValue<int>();
            var imagen = Hoja(hojas, fileNum);

            if (imagen is null)
            {
                GD.PrintErr($"  ✗ {nombre}: no se pudo abrir {fileNum}.PNG");
                fuera++;
                continue;
            }

            foreach (var (x, y, ancho, alto, cuadro) in RegionesDe(valor))
            {
                revisadas++;

                if (x < 0 || y < 0 || x + ancho > imagen.GetWidth() || y + alto > imagen.GetHeight())
                {
                    GD.PrintErr($"  ✗ {nombre}[{cuadro}]: ({x},{y}) {ancho}x{alto} " +
                                $"se sale de {imagen.GetWidth()}x{imagen.GetHeight()}");
                    fuera++;
                    continue;
                }

                var (opacos, magenta) = Contar(imagen, x, y, ancho, alto);

                if (opacos < MinimoOpacos)
                {
                    GD.PrintErr($"  ✗ {nombre}[{cuadro}]: región VACÍA ({opacos} píxeles opacos)");
                    vacias++;
                    continue;
                }

                int porciento = magenta * 100 / opacos;

                if (porciento >= MaxMagentaPorciento)
                {
                    GD.PrintErr($"  ✗ {nombre}[{cuadro}]: {porciento}% magenta — es un cuadro de relleno");
                    conMagenta++;
                }
            }
        }

        GD.Print($"\n  {revisadas} regiones revisadas en {hojas.Count} hojas");

        if (vacias + conMagenta + fuera == 0)
        {
            GD.Print("── ARTE OK ────────────────────────────────────────");
            return;
        }

        GD.Print($"  vacías {vacias} · con magenta {conMagenta} · fuera de la hoja {fuera}");
        GD.Print("── HAY QUE ARREGLAR ───────────────────────────────");
    }

    /// <summary>Todas las regiones de una entrada: la suelta o las del array.</summary>
    private static IEnumerable<(int X, int Y, int Width, int Height, int Cuadro)> RegionesDe(JsonNode entrada)
    {
        if (entrada["regiones"] is JsonArray regiones)
        {
            int i = 0;

            foreach (var r in regiones)
            {
                if (r is JsonArray a && a.Count >= 4)
                {
                    yield return (
                        a[0]!.GetValue<int>(), a[1]!.GetValue<int>(),
                        a[2]!.GetValue<int>(), a[3]!.GetValue<int>(), i);
                }

                i++;
            }

            yield break;
        }

        yield return (
            entrada["x"]!.GetValue<int>(), entrada["y"]!.GetValue<int>(),
            entrada["w"]!.GetValue<int>(), entrada["h"]!.GetValue<int>(), 0);
    }

    private static Image? Hoja(Dictionary<int, Image> cache, int fileNum)
    {
        if (cache.TryGetValue(fileNum, out var listo))
            return listo;

        string ruta = Path.Combine(CarpetaResources, fileNum + ".PNG");

        if (!File.Exists(ruta))
            return null;

        var imagen = Image.LoadFromFile(ruta);

        if (imagen is null)
            return null;

        cache[fileNum] = imagen;
        return imagen;
    }

    /// <summary>
    /// Account píxeles opacos y cuántos de esos son magenta.
    /// </summary>
    /// <remarks>
    /// Se muestrea de a 2 píxeles en cada eje: con regiones de 104x184 son 4.784
    /// lecturas por cuadro y hay más de 500 cuadros. Un cuadro de relleno es un
    /// rectángulo sólido, así que un muestreo grueso lo detecta igual.
    /// </remarks>
    private static (int Opacos, int Magenta) Contar(Image imagen, int x, int y, int ancho, int alto)
    {
        int opacos = 0;
        int magenta = 0;

        for (int j = 0; j < alto; j += 2)
        {
            for (int i = 0; i < ancho; i += 2)
            {
                var c = imagen.GetPixel(x + i, y + j);

                if (c.A < 0.5f)
                    continue;

                opacos++;

                // El fucsia de relleno de AO es (255,0,255). Se compara con
                // holgura porque el upscale x4 interpola los bordes y deja
                // píxeles que no son exactamente ese valor.
                if (c.R > 0.85f && c.G < 0.20f && c.B > 0.85f)
                    magenta++;
            }
        }

        return (opacos, magenta);
    }
}
