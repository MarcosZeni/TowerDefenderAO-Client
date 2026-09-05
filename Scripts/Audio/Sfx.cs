using System.Collections.Generic;
using Godot;

namespace TowerOfArgentum.Audio;

/// <summary>
/// Los efectos de sonido. <c>Sfx.Play(SoundId.TowerPlace)</c> y listo.
/// </summary>
/// <remarks>
/// <para>Un pool de reproductores sobre el bus <c>Sonido</c>, sin posición. Que
/// no sea posicional es a propósito: la cámara es fija y el campo entra entero en
/// pantalla (<c>Doc/Interfaz/01-la-ventana-y-la-escala.md</c>), así que un
/// paneo por celda no agrega información y en auriculares marea.</para>
///
/// <para>⚠ <b>Nada de acá entra a la simulación.</b> Se llama desde el render y
/// desde la interfaz, usa <see cref="GD.Randf"/> y mide el tiempo en milisegundos
/// de reloj de pared. Si alguna vez un <c>Sfx.Play</c> aparece adentro de
/// <c>Simulation\</c>, es un bug: la simulación no sabe que existe un motor, y el
/// guardia de compilación del <c>.csproj</c> lo va a rebotar por el
/// <c>using Godot</c>.</para>
///
/// <para><b>Las tres defensas contra la fatiga auditiva</b>, que en un tower
/// defense no son un lujo sino la diferencia entre jugable e insoportable:</para>
///
/// <list type="number">
/// <item><b>Variantes.</b> Cada evento tiene 2-3 archivos y se elige uno al
/// azar.</item>
/// <item><b>Pitch al azar.</b> ±8-14 % según el evento. Con dos variantes ya
/// suenan como seis.</item>
/// <item><b>Throttle por clave.</b> El que de verdad salva: con nueve defensores
/// disparando a 30 Hz hay picos de 40 sonidos por segundo, y sin un mínimo entre
/// dos disparos de la misma clave el resultado es ruido blanco.</item>
/// </list>
/// </remarks>
public static class Sfx
{
    /// <summary>
    /// Cuántos reproductores tiene el pool.
    /// </summary>
    /// <remarks>
    /// 24 alcanza con margen: el throttle ya limita cuántas voces de la misma
    /// clave hay a la vez, así que el pico real es «cuántos eventos DISTINTOS
    /// pasan en la misma ventana de 100 ms», que en la práctica no pasa de una
    /// docena. Cuando se acaban, el sonido nuevo se descarta en vez de cortar uno
    /// que está sonando: cortar se oye peor que faltar.
    /// </remarks>
    private const int Reproductores = 24;

    private static readonly List<AudioStreamPlayer> _pool = [];

    /// <summary>De qué clave es lo que está sonando en cada reproductor.</summary>
    private static readonly SoundId?[] _sonando = new SoundId?[Reproductores];

    /// <summary>Cuándo sonó por última vez cada clave, en ms de reloj de pared.</summary>
    private static readonly Dictionary<SoundId, ulong> _ultima = [];

    private static Node? _raiz;

    /// <summary>Si el pool ya está armado. Sin esto, <see cref="Play"/> no hace nada.</summary>
    public static bool Ready => _raiz is not null;

    /// <summary>
    /// Arma el pool. Se llama una vez, desde <c>Main._Ready</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ Va DESPUÉS de <c>Settings.Load()</c>, que es lo que aplica el volumen
    /// guardado a los buses. Al revés, el primer sonido saldría al volumen que
    /// tenía el bus por defecto.
    /// </remarks>
    public static void Init(Node padre)
    {
        if (_raiz is not null)
            return;

        _raiz = new Node { Name = "Sfx" };
        padre.AddChild(_raiz);

        for (int i = 0; i < Reproductores; i++)
        {
            var p = new AudioStreamPlayer
            {
                Name = $"Sfx{i}",
                Bus = "Sonido",

                // ⚠ El pool sobrevive al cambio de pantalla y a la pausa del menú
                // de ESC. Sin esto, abrir el menú con el juego pausado cortaría
                // el click que lo abrió.
                ProcessMode = Node.ProcessModeEnum.Always,
            };

            _raiz.AddChild(p);
            _pool.Add(p);
        }
    }

    /// <summary>
    /// Suena un evento, si el throttle lo deja.
    /// </summary>
    /// <param name="id">Qué pasó, no qué archivo.</param>
    /// <param name="volumeOffsetDb">
    /// Ajuste puntual sobre el volumen del índice. Lo usa el único caso donde el
    /// mismo evento tiene que sonar distinto según el contexto: el impacto de un
    /// golpe que mata suena un poco más que el que no.
    /// </param>
    public static void Play(SoundId id, float volumeOffsetDb = 0f)
    {
        if (_raiz is null || AudioBank.Sound(id) is not { Files.Length: > 0 } def)
            return;

        ulong ahora = Time.GetTicksMsec();

        if (_ultima.TryGetValue(id, out ulong previa) &&
            def.ThrottleMs > 0 &&
            ahora - previa < (ulong)def.ThrottleMs)
        {
            return;
        }

        // El tope de voces se cuenta sobre lo que está sonando de ESTA clave, no
        // sobre el pool entero: un evento que se desmadra no puede dejar sin
        // reproductor al aviso de que se filtró un bicho.
        int libre = -1;
        int suyas = 0;

        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].Playing)
            {
                _sonando[i] = null;

                if (libre < 0)
                    libre = i;

                continue;
            }

            if (_sonando[i] == id)
                suyas++;
        }

        if (libre < 0 || suyas >= def.Voices)
            return;

        var stream = AudioBank.Stream(def.Files[Elegir(def.Files.Length)]);

        if (stream is null)
            return;

        var player = _pool[libre];

        player.Stream = stream;
        player.VolumeDb = def.VolumeDb + volumeOffsetDb;

        // El pitch al azar, centrado en 1. `pitch: 10` da 0,90 a 1,10.
        player.PitchScale = def.PitchPercent <= 0
            ? 1f
            : 1f + (((GD.Randf() * 2f) - 1f) * def.PitchPercent / 100f);

        _sonando[libre] = id;
        _ultima[id] = ahora;

        player.Play();
    }

    /// <summary>Atajo para el sonido de click, que es el que más se llama.</summary>
    public static void Click() => Play(SoundId.UiClick);

    /// <summary>Corta todo. Al terminar una corrida o al volver al menú.</summary>
    public static void StopAll()
    {
        foreach (var p in _pool)
            p.Stop();

        for (int i = 0; i < _sonando.Length; i++)
            _sonando[i] = null;
    }

    private static int Elegir(int cuantos) => cuantos <= 1 ? 0 : (int)(GD.Randi() % (uint)cuantos);
}
