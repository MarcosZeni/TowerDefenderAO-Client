using System.Collections.Generic;
using System.IO;
using Godot;

namespace TowerOfArgentum.Tools;

/// <summary>
/// Mete <c>Resources\</c> en un <c>arte.pck</c>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Existe por Android.</b> El juego leía las hojas con
/// <c>Image.LoadFromFile</c> sobre <c>res://../Resources</c>, que es una carpeta
/// del disco hermana del proyecto. En escritorio eso se arregla copiando la
/// carpeta al lado del ejecutable; en un teléfono no hay carpeta al lado del
/// ejecutable y no hay nada que copiar. Un <c>.pck</c> viaja adentro del APK y se
/// monta sobre <c>res://</c> como si siempre hubiera estado ahí.</para>
///
/// <para><b>Los PNG entran crudos, sin importar.</b> Es la diferencia con meter el
/// arte adentro del proyecto Godot: un PNG importado se vuelve una textura
/// comprimida para la GPU, y <see cref="GraphicsLoader.SheetImage"/> —que hornea
/// el piso pegando cientos de tiles con <see cref="Image.BlitRect"/>— necesita
/// píxeles RGBA de verdad, no bloques ETC2. Empaquetar y no importar deja las dos
/// cosas: el arte viaja adentro y sigue siendo un PNG.</para>
///
/// <para>El paquete NO se versiona: es un artefacto, y la fuente son los PNG de
/// <c>Resources\</c>. Vale la misma regla que para el atlas.</para>
///
/// <code>
/// --empaquetar-arte                  → ..\build\arte.pck   (escritorio)
/// --empaquetar-arte res://arte.pck   → Client\arte.pck     (para que entre al APK)
/// </code>
/// </remarks>
public static class ArtPacker
{
    /// <summary>La carpeta del arte, hermana de <c>Client\</c>.</summary>
    private static readonly string CarpetaResources =
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "Resources"));

    /// <summary>Dónde queda el paquete si no se pide otra cosa.</summary>
    private const string DestinoPorDefecto = "res://../build/arte.pck";

    /// <summary>
    /// Lo único que entra al paquete.
    /// </summary>
    /// <remarks>
    /// ⚠ Es una lista blanca, no una negra. <c>Resources\</c> tiene material de
    /// trabajo que el juego no lee —los <c>.orig</c> 1x, los <c>.md</c> de las
    /// carpetas de iconos, un <c>.editorconfig</c>, un <c>.ps1</c>— y con lista
    /// negra cada archivo suelto que aparezca mañana se cuela en el APK sin que
    /// nadie se entere. Los PNG son el arte y los JSON son los dos índices,
    /// <c>graficos.json</c> e <c>iconos.json</c>.
    /// </remarks>
    private static readonly string[] ExtensionesQueEntran = [".png", ".json"];

    public static void Execute(string destino)
    {
        GD.Print("── Empaquetado del arte ───────────────────────────");

        if (!Directory.Exists(CarpetaResources))
        {
            GD.PrintErr($"  ✗ no está la carpeta {CarpetaResources}");
            return;
        }

        if (string.IsNullOrWhiteSpace(destino))
            destino = DestinoPorDefecto;

        string rutaFinal = ProjectSettings.GlobalizePath(destino);
        string carpetaFinal = Path.GetDirectoryName(rutaFinal) ?? "";

        if (carpetaFinal.Length > 0)
            Directory.CreateDirectory(carpetaFinal);

        var packer = new PckPacker();

        // ⚠ El destino va en ruta GLOBAL, no res://. Si el paquete se escribe
        // adentro del proyecto —el caso de Android—, res:// apuntaría al mismo
        // .pck que se está escribiendo.
        if (packer.PckStart(rutaFinal) != Error.Ok)
        {
            GD.PrintErr($"  ✗ no se pudo crear {rutaFinal}");
            return;
        }

        var porExtension = new Dictionary<string, (int Archivos, long Bytes)>(
            System.StringComparer.OrdinalIgnoreCase);

        int total = 0;
        long bytes = 0;

        foreach (string archivo in Directory.EnumerateFiles(
                     CarpetaResources, "*", SearchOption.AllDirectories))
        {
            string relativa = Path.GetRelativePath(CarpetaResources, archivo).Replace('\\', '/');

            // El .git de Resources\ pesa más que el arte entero. Y los archivos de
            // git sueltos —.gitignore, .gitattributes— tampoco pintan nada adentro
            // del juego.
            if (relativa.StartsWith(".git", System.StringComparison.OrdinalIgnoreCase))
                continue;

            string extension = Path.GetExtension(relativa);

            if (!System.Array.Exists(
                    ExtensionesQueEntran,
                    e => e.Equals(extension, System.StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // La ruta virtual adentro del paquete. Tiene que coincidir con
            // ArtPack.MountPoint o el juego monta el pck y no encuentra nada.
            string virtual_ = $"{ArtPack.MountPoint}/{relativa}";

            if (packer.AddFile(virtual_, archivo) != Error.Ok)
            {
                GD.PrintErr($"  ✗ no se pudo agregar {relativa}");
                continue;
            }

            long largo = new FileInfo(archivo).Length;

            porExtension.TryGetValue(extension, out var acumulado);
            porExtension[extension] = (acumulado.Archivos + 1, acumulado.Bytes + largo);

            total++;
            bytes += largo;
        }

        if (total == 0)
        {
            GD.PrintErr("  ✗ no había nada para empaquetar");
            return;
        }

        if (packer.Flush(verbose: false) != Error.Ok)
        {
            GD.PrintErr("  ✗ falló el cierre del paquete");
            return;
        }

        foreach (var (extension, dato) in porExtension)
            GD.Print($"  {extension,-8} {dato.Archivos,5} archivos   {Mb(dato.Bytes),8}");

        long enDisco = File.Exists(rutaFinal) ? new FileInfo(rutaFinal).Length : 0;

        GD.Print("───────────────────────────────────────────────────");
        GD.Print($"  {total} archivos, {Mb(bytes)} de arte");
        GD.Print($"  → {rutaFinal}  ({Mb(enDisco)})");
        GD.Print($"  se monta en {ArtPack.MountPoint}");
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} MB";
}
