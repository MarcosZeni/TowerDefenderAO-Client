using System.Collections.Generic;
using System.IO;
using Godot;

namespace TowerOfArgentum.Tools;

/// <summary>
/// Mete <c>Data\audio\</c> en un <c>audio.pck</c>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Los archivos entran CRUDOS, sin pasar por el importador.</b> Es lo
/// mismo que hace <see cref="ArtPacker"/> con los PNG, pero por otro motivo: allá
/// es obligatorio —una textura comprimida para la GPU no se puede blitear—, acá es
/// una consecuencia. Un OGG crudo y uno importado son el mismo archivo, así que no
/// se pierde nada; el que sí perdía era el WAV, que el importador comprimía a QOA,
/// y por eso los doce WAV se convirtieron a OGG antes de armar esto.</para>
///
/// <para><b>Por qué existe.</b> El audio pesaba 22,6 MB adentro del ejecutable y
/// obligaba a re-exportar 128 MB para cambiar un sonido. Afuera, parchear la banda
/// sonora es reemplazar un archivo. ⚠ Para que el ejecutable de verdad adelgace,
/// el preset de exportación tiene que EXCLUIR <c>Data/audio/*</c>: si no, el audio
/// queda en los dos lados y el paquete solo agrega peso.</para>
///
/// <para>El paquete NO se versiona: es un artefacto, y la fuente es
/// <c>Data\audio\</c>. Vale la misma regla que para el arte y el atlas.</para>
///
/// <code>
/// --empaquetar-audio                   → ..\build\audio.pck   (escritorio)
/// --empaquetar-audio res://audio.pck   → Client\audio.pck     (para que entre al APK)
/// </code>
/// </remarks>
public static class AudioPacker
{
    /// <summary>La carpeta del audio, adentro del proyecto.</summary>
    private const string CarpetaAudio = "res://Data/audio";

    /// <summary>Dónde queda el paquete si no se pide otra cosa.</summary>
    private const string DestinoPorDefecto = "res://../build/audio.pck";

    /// <summary>
    /// Lo único que entra al paquete.
    /// </summary>
    /// <remarks>
    /// ⚠ Lista blanca, como en <see cref="ArtPacker"/>. <c>Data\audio\</c> tiene
    /// los <c>.import</c> que genera Godot y un <c>LICENSES.md</c>: ninguno de los
    /// dos pinta nada adentro del juego —el <c>.import</c> apunta a un recurso que
    /// no viaja, y la licencia va suelta al lado del ejecutable, que es donde
    /// alguien la puede leer—.
    /// </remarks>
    private static readonly string[] ExtensionesQueEntran = [".ogg", ".mp3", ".wav", ".json"];

    public static void Execute(string destino)
    {
        GD.Print("── Empaquetado del audio ──────────────────────────");

        string carpeta = ProjectSettings.GlobalizePath(CarpetaAudio);

        if (!Directory.Exists(carpeta))
        {
            GD.PrintErr($"  ✗ no está la carpeta {carpeta}");
            return;
        }

        if (string.IsNullOrWhiteSpace(destino))
            destino = DestinoPorDefecto;

        string rutaFinal = ProjectSettings.GlobalizePath(destino);
        string carpetaFinal = Path.GetDirectoryName(rutaFinal) ?? "";

        if (carpetaFinal.Length > 0)
            Directory.CreateDirectory(carpetaFinal);

        var packer = new PckPacker();

        // ⚠ El destino va en ruta GLOBAL, no res://: si el paquete se escribe
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
        bool hayIndice = false;

        foreach (string archivo in Directory.EnumerateFiles(
                     carpeta, "*", SearchOption.AllDirectories))
        {
            string relativa = Path.GetRelativePath(carpeta, archivo).Replace('\\', '/');
            string extension = Path.GetExtension(relativa);

            if (!System.Array.Exists(
                    ExtensionesQueEntran,
                    e => e.Equals(extension, System.StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // La ruta virtual adentro del paquete. Tiene que coincidir con
            // AudioPack.MountPoint o el juego monta el pck y no encuentra nada.
            string virtual_ = $"{AudioPack.MountPoint}/{relativa}";

            if (packer.AddFile(virtual_, archivo) != Error.Ok)
            {
                GD.PrintErr($"  ✗ no se pudo agregar {relativa}");
                continue;
            }

            if (relativa == "audio.json")
                hayIndice = true;

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

        // ⚠ Sin el índice el paquete es inservible y AudioPack lo rechaza al
        // montarlo. Mejor fallar acá, donde se ve, que en el arranque del juego.
        if (!hayIndice)
        {
            GD.PrintErr("  ✗ falta audio.json: el paquete no se podría montar");
            return;
        }

        if (packer.Flush(verbose: false) != Error.Ok)
        {
            GD.PrintErr("  ✗ falló el cierre del paquete");
            return;
        }

        foreach (var (extension, acumulado) in porExtension)
        {
            GD.Print($"  {extension,-6} {acumulado.Archivos,5} archivos  " +
                     $"{acumulado.Bytes / 1048576.0,6:0.0} MB");
        }

        GD.Print("───────────────────────────────────────────────────");
        GD.Print($"  {total} archivos, {bytes / 1048576.0:0.0} MB de audio");
        GD.Print($"  → {rutaFinal}  ({new FileInfo(rutaFinal).Length / 1048576.0:0.0} MB)");
        GD.Print($"  se monta en {AudioPack.MountPoint}");
    }
}
