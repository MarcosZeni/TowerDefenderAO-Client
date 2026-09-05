using System.Collections.Generic;
using System.Text.Json;
using Godot;

using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum.Audio;

/// <summary>Cómo suena un evento: las variantes y sus números.</summary>
/// <param name="Files">Rutas completas de las variantes, ya resueltas.</param>
/// <param name="VolumeDb">Decibeles sobre el bus.</param>
/// <param name="PitchPercent">Variación de tono, en centésimas (8 = ±8 %).</param>
/// <param name="ThrottleMs">Milisegundos mínimos entre dos reproducciones.</param>
/// <param name="Voices">Voces simultáneas como máximo.</param>
public sealed record SoundEntry(
    string[] Files,
    float VolumeDb,
    int PitchPercent,
    int ThrottleMs,
    int Voices);

/// <summary>Una pista de música.</summary>
public sealed record MusicEntry(string File, float VolumeDb, bool Loop);

/// <summary>
/// Lee <c>Data/audio/audio.json</c> y entrega los <c>AudioStream</c> ya cargados.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Este archivo NO entra al hash de <c>/run/start</c>.</b> El hash lo
/// arma <see cref="DataLoader"/> sobre una lista fija de siete archivos de
/// balance más los layouts, y <c>audio.json</c> no está ahí. Es deliberado y es
/// la misma razón que deja afuera a <c>Data/lang/</c>: el volumen de un sonido no
/// cambia la simulación, y si entrara, corregir un decibel invalidaría la
/// temporada del ranking (regla de oro 5).</para>
///
/// <para><b>Por qué el audio falta en silencio y el balance no.</b>
/// <see cref="DataLoader"/> tira si le falta un archivo, porque sin bestiario no
/// hay juego. Acá al revés: un juego sin un sonido se juega igual, y caerse al
/// arranque por un <c>.ogg</c> que no se copió sería peor que la falta. Lo que
/// falta se anuncia una vez por <see cref="GD.PushWarning"/> y el evento queda
/// mudo.</para>
///
/// <para><b>Dos caminos para cargar, y los dos hacen falta.</b> Lo normal es
/// <see cref="ResourceLoader"/>: los archivos viven adentro del proyecto Godot,
/// el editor les genera su <c>.import</c> y viajan en el <c>.pck</c> ya
/// convertidos. Pero un archivo recién copiado todavía no está importado —y las
/// herramientas headless corren sin editor—, así que hay un segundo camino que
/// lee los bytes crudos y arma el stream en memoria. Es la misma solución que
/// usa <see cref="ArtPack.LoadImage"/> con los PNG, por el mismo motivo.</para>
/// </remarks>
public static class AudioBank
{
    private const string Carpeta = "res://Data/audio";
    private const string Indice = Carpeta + "/audio.json";

    private static readonly Dictionary<SoundId, SoundEntry> _sfx = [];
    private static readonly Dictionary<MusicTrack, MusicEntry> _musica = [];

    /// <summary>Streams ya cargados, por ruta. Un sonido se lee del disco una vez.</summary>
    private static readonly Dictionary<string, AudioStream?> _cache = [];

    private static bool _cargado;

    /// <summary>Qué salió mal al leer el índice, si salió algo. Para el diagnóstico.</summary>
    public static string Problem { get; private set; } = "";

    /// <summary>Cuántos eventos quedaron definidos. 0 significa que no hay audio.</summary>
    public static int Count => _sfx.Count;

    /// <summary>Vuelve a leer el índice. La usan las herramientas offline.</summary>
    public static void Forget()
    {
        _sfx.Clear();
        _musica.Clear();
        _cache.Clear();
        _cargado = false;
    }

    /// <summary>La definición de un evento, o <c>null</c> si no está en el índice.</summary>
    public static SoundEntry? Sound(SoundId id)
    {
        Cargar();
        return _sfx.TryGetValue(id, out var e) ? e : null;
    }

    /// <summary>La definición de una pista, o <c>null</c>.</summary>
    public static MusicEntry? Music(MusicTrack track)
    {
        Cargar();
        return _musica.TryGetValue(track, out var e) ? e : null;
    }

    /// <summary>
    /// El stream de una ruta, cacheado.
    /// </summary>
    /// <remarks>
    /// Devuelve <c>null</c> —y avisa una sola vez— si el archivo no está o si el
    /// formato no se reconoce. El <c>null</c> también se cachea: si no, un
    /// archivo que falta se intentaría abrir en cada disparo.
    /// </remarks>
    public static AudioStream? Stream(string ruta)
    {
        if (_cache.TryGetValue(ruta, out var listo))
            return listo;

        var stream = Abrir(ruta);
        _cache[ruta] = stream;

        if (stream is null)
            GD.PushWarning($"[audio] no se pudo cargar {ruta}. Ese evento queda mudo.");

        return stream;
    }

    private static AudioStream? Abrir(string ruta)
    {
        // Camino normal: el archivo está importado y viaja convertido en el .pck.
        if (ResourceLoader.Exists(ruta) && ResourceLoader.Load(ruta) is AudioStream importado)
            return importado;

        // Camino de respaldo: bytes crudos. Sirve con el archivo recién copiado
        // —todavía sin .import— y en las herramientas headless.
        if (!ArchivoGodot.FileExists(ruta))
            return null;

        byte[] bytes = ArchivoGodot.GetFileAsBytes(ruta);

        if (bytes.Length == 0)
            return null;

        return ruta.GetExtension().ToLowerInvariant() switch
        {
            "ogg" => AudioStreamOggVorbis.LoadFromBuffer(bytes),
            "mp3" => AudioStreamMP3.LoadFromBuffer(bytes),
            "wav" => AudioStreamWav.LoadFromBuffer(bytes),
            _ => null,
        };
    }

    /// <summary>
    /// Pone o saca el loop de un stream, sea del formato que sea.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Los tres formatos lo dicen distinto —<c>Loop</c> booleano en OGG y
    /// MP3, <c>LoopMode</c> enumerado en WAV— y el que se olvida es el que deja
    /// la música del menú cortándose a los tres minutos.</para>
    ///
    /// <para>⚠ <b>Y modifica el recurso compartido.</b> Un stream cargado por
    /// <see cref="ResourceLoader"/> es el MISMO objeto para todo el que lo pida,
    /// así que esto se llama solo sobre pistas de música —que se usan de a una y
    /// siempre con el mismo criterio—, nunca sobre un efecto.</para>
    /// </remarks>
    public static void SetLoop(AudioStream stream, bool loop)
    {
        switch (stream)
        {
            case AudioStreamOggVorbis ogg:
                ogg.Loop = loop;
                break;

            case AudioStreamMP3 mp3:
                mp3.Loop = loop;
                break;

            case AudioStreamWav wav:
                wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;

                // ⚠ Sin `LoopEnd` el WAV vuelve al principio en el sample 0 y se
                // queda ahí: el default es 0, no «hasta el final».
                if (loop && wav.LoopEnd <= 0)
                    wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2);

                break;
        }
    }

    private static void Cargar()
    {
        if (_cargado)
            return;

        _cargado = true;

        using var f = ArchivoGodot.Open(Indice, ArchivoGodot.ModeFlags.Read);

        if (f is null)
        {
            Problem = $"no se pudo leer {Indice}";
            GD.PushWarning($"[audio] {Problem}. El juego corre mudo.");
            return;
        }

        try
        {
            LeerIndice(f.GetAsText());
        }
        catch (System.Exception e)
        {
            Problem = e.Message;
            GD.PushError($"[audio] {Indice} no se pudo leer: {e.Message}");
        }
    }

    private static void LeerIndice(string texto)
    {
        // Los JSON de Data\ llevan comentarios adentro, y eso no se saca: el
        // porqué de cada número vive al lado del número (CLAUDE.md).
        var opciones = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        using var doc = JsonDocument.Parse(texto, opciones);
        var raiz = doc.RootElement;

        if (raiz.TryGetProperty("sfx", out var sfx))
        {
            foreach (var prop in sfx.EnumerateObject())
            {
                if (!System.Enum.TryParse<SoundId>(prop.Name, out var id))
                {
                    GD.PushWarning($"[audio] «{prop.Name}» no es un SoundId. Sobra en audio.json.");
                    continue;
                }

                _sfx[id] = LeerSonido(prop.Value);
            }
        }

        if (raiz.TryGetProperty("music", out var musica))
        {
            foreach (var prop in musica.EnumerateObject())
            {
                if (!System.Enum.TryParse<MusicTrack>(prop.Name, out var track))
                {
                    GD.PushWarning($"[audio] «{prop.Name}» no es un MusicTrack. Sobra en audio.json.");
                    continue;
                }

                _musica[track] = new MusicEntry(
                    $"{Carpeta}/music/{prop.Value.GetProperty("file").GetString()}",
                    Numero(prop.Value, "vol", 0f),
                    !prop.Value.TryGetProperty("loop", out var l) || l.GetBoolean());
            }
        }

        // Lo que está en el enum y no en el JSON queda mudo, y hay que verlo.
        foreach (SoundId id in System.Enum.GetValues<SoundId>())
        {
            if (!_sfx.ContainsKey(id))
                GD.PushWarning($"[audio] SoundId.{id} no está en audio.json: ese evento no suena.");
        }
    }

    private static SoundEntry LeerSonido(JsonElement e)
    {
        var archivos = new List<string>();

        if (e.TryGetProperty("files", out var lista))
        {
            foreach (var item in lista.EnumerateArray())
            {
                if (item.GetString() is { Length: > 0 } nombre)
                    archivos.Add($"{Carpeta}/sfx/{nombre}");
            }
        }

        return new SoundEntry(
            [.. archivos],
            Numero(e, "vol", 0f),
            (int)Numero(e, "pitch", 0f),
            (int)Numero(e, "throttle", 0f),
            System.Math.Max(1, (int)Numero(e, "voices", 2f)));
    }

    private static float Numero(JsonElement e, string clave, float porDefecto) =>
        e.TryGetProperty(clave, out var v) && v.TryGetSingle(out float n) ? n : porDefecto;
}
