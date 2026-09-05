using Godot;

namespace TowerOfArgentum.Audio;

/// <summary>
/// La música de fondo, con cruce entre pistas.
/// </summary>
/// <remarks>
/// <para>Dos reproductores sobre el bus <c>Musica</c>: mientras uno baja, el otro
/// sube. Cortar en seco y arrancar la siguiente se oye como un error del juego,
/// y en un tower defense el cambio de pista pasa seguido —cada oleada puede
/// mover la intensidad—.</para>
///
/// <para>⚠ <b>Pedir la pista que ya está sonando no hace nada.</b> Es la regla
/// que permite llamar a <see cref="Play"/> desde el bucle de render sin
/// condicionarlo: <c>AudioCues</c> decide la pista que corresponde en cada tick y
/// la pide, y el cruce arranca solo cuando de verdad cambió. Sin esta guarda, la
/// música se reiniciaría 60 veces por segundo.</para>
///
/// <para>El volumen de cada pista sale de <c>Data/audio/audio.json</c> y va muy
/// por debajo del de los efectos. Es un tower defense: la música es fondo y los
/// efectos son información.</para>
/// </remarks>
public partial class Music : Node
{
    /// <summary>
    /// Cuánto dura el cruce.
    /// </summary>
    /// <remarks>
    /// Un segundo y medio: alcanza para que no se oiga el corte y es corto para
    /// que entrar a la pelea del jefe se sienta como un cambio y no como una
    /// transición de ascensor.
    /// </remarks>
    private const float CruceSegundos = 1.5f;

    /// <summary>Por debajo de esto un bus es silencio, no un volumen bajito.</summary>
    private const float SilencioDb = -60f;

    private static Music? _yo;

    private AudioStreamPlayer _a = null!;
    private AudioStreamPlayer _b = null!;

    /// <summary>Cuál de los dos tiene la pista actual.</summary>
    private bool _usandoA = true;

    private float _volumenObjetivo;

    /// <summary>Si la pista actual loopea. Una que no, se «apaga» sola al terminar.</summary>
    private bool _loopea = true;

    /// <summary>Qué está sonando. <see cref="MusicTrack.None"/> es silencio.</summary>
    public static MusicTrack Current { get; private set; } = MusicTrack.None;

    /// <summary>Arma los dos reproductores. Se llama una vez, desde <c>Main._Ready</c>.</summary>
    public static void Init(Node padre)
    {
        if (_yo is not null)
            return;

        _yo = new Music { Name = "Musica", ProcessMode = ProcessModeEnum.Always };
        padre.AddChild(_yo);
    }

    /// <summary>
    /// Cambia de pista, con cruce. Pedir la que ya suena no hace nada.
    /// </summary>
    public static void Play(MusicTrack track)
    {
        if (_yo is null || track == Current)
            return;

        if (track == MusicTrack.None)
        {
            _yo.Fundir(null, 0f, false);
            Current = MusicTrack.None;
            return;
        }

        if (AudioBank.Music(track) is not { } def)
            return;

        var stream = AudioBank.Stream(def.File);

        if (stream is null)
            return;

        AudioBank.SetLoop(stream, def.Loop);

        _yo.Fundir(stream, def.VolumeDb, def.Loop);
        Current = track;
    }

    /// <summary>Corta la música. Al salir de una corrida al menú.</summary>
    public static void Stop() => Play(MusicTrack.None);

    public override void _Ready()
    {
        _a = Nuevo("MusicaA");
        _b = Nuevo("MusicaB");

        AddChild(_a);
        AddChild(_b);

        static AudioStreamPlayer Nuevo(string nombre) => new()
        {
            Name = nombre,
            Bus = "Musica",
            VolumeDb = SilencioDb,
            ProcessMode = ProcessModeEnum.Always,
        };
    }

    /// <summary>
    /// El cruce, un frame por vez.
    /// </summary>
    /// <remarks>
    /// ⚠ En decibeles y no en lineal. Un cruce lineal deja un pozo audible en la
    /// mitad —las dos pistas al 50 % suenan menos que una al 100 %— y el jugador
    /// oye un bajón en cada cambio de oleada.
    /// </remarks>
    public override void _Process(double delta)
    {
        float paso = (float)delta / CruceSegundos * (0f - SilencioDb);

        var entra = _usandoA ? _a : _b;
        var sale = _usandoA ? _b : _a;

        if (entra.VolumeDb < _volumenObjetivo)
            entra.VolumeDb = Mathf.Min(_volumenObjetivo, entra.VolumeDb + paso);

        if (sale.Playing)
        {
            sale.VolumeDb -= paso;

            // Cuando la saliente llega al silencio se detiene de verdad: un
            // reproductor a -60 dB sigue decodificando, y son dos MP3 en paralelo
            // toda la corrida.
            if (sale.VolumeDb <= SilencioDb)
            {
                sale.VolumeDb = SilencioDb;
                sale.Stop();
                sale.Stream = null;
            }
        }

        // Una pista que no loopea —victoria, derrota— deja de ser «la actual»
        // cuando se termina, para que la siguiente pueda entrar sin que nadie
        // tenga que pedir None en el medio. Se mira acá y no con la señal
        // `Finished` porque para cuando esa señal llega, el cruce ya pudo haber
        // cambiado cuál de los dos reproductores es el actual.
        if (!_loopea && !entra.Playing && Current is not MusicTrack.None)
            Current = MusicTrack.None;
    }

    private void Fundir(AudioStream? stream, float volumenDb, bool loop)
    {
        _volumenObjetivo = volumenDb;
        _loopea = loop;

        // El que estaba sonando pasa a ser el saliente y el otro toma la nueva.
        _usandoA = !_usandoA;

        var entra = _usandoA ? _a : _b;

        // ⚠ El entrante puede venir de una pista anterior a medio apagar: se lo
        // baja al silencio antes de arrancar, o el cruce empezaría a mitad de
        // camino y la pista nueva entraría de golpe.
        entra.Stop();
        entra.VolumeDb = SilencioDb;
        entra.Stream = stream;

        if (stream is null)
            return;

        entra.Play();
    }
}
