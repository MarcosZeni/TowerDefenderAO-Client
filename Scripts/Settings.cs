using Godot;

namespace TowerOfArgentum;

/// <summary>Las acciones del juego que se pueden remapear.</summary>
/// <remarks>
/// ⚠ El orden de este enum es el orden en que se listan en el menú, y los
/// nombres se guardan en el archivo <b>por su texto</b>: renombrar uno pierde
/// la tecla que el jugador le había puesto, no la rompe.
/// </remarks>
public enum Shortcut
{
    Pausa,
    Velocidad1,
    Velocidad2,
    Velocidad3,
    ClaseAnterior,
    ClaseSiguiente,
    Backpack,
    CallNextWave,
}

/// <summary>
/// Las preferencias del jugador: ventana, sonido, animaciones y teclas.
/// </summary>
/// <remarks>
/// <para>Vive en <c>user://opciones.cfg</c>, al lado del guardado de la corrida
/// y del de video. <b>No entra al hash de datos y no toca la simulación</b>: son
/// preferencias de quien juega, no entrada del juego. Bajar el volumen o cambiar
/// una tecla no puede cambiar el resultado de una corrida.</para>
///
/// <para>⚠ <b>ESC no se puede remapear.</b> Es la tecla que abre este menú, y si
/// se pudiera reasignar el jugador podría dejarse sin forma de llegar acá — que
/// es exactamente el agujero que este menú vino a tapar.</para>
/// </remarks>
public static class Settings
{
    private const string Route = "user://opciones.cfg";

    /// <summary>Volumen de la música, 0 a 100.</summary>
    public static int MusicVolume { get; set; } = 70;

    /// <summary>Volumen de los efectos, 0 a 100.</summary>
    public static int SoundVolume { get; set; } = 80;

    /// <summary>
    /// Si los defensores animan el arma al atacar.
    /// </summary>
    /// <remarks>
    /// El cuerpo y la cabeza van quietos siempre —sus cuadros son el ciclo de
    /// caminar de AO y un defensor plantado que camina en el lugar marea—. Esto
    /// apaga lo único que se mueve, para quien quiera un tablero completamente
    /// estático.
    /// </remarks>
    public static bool AnimateWeapons { get; set; } = true;

    /// <summary>Si el portal y el cristal de la Torre laten y giran.</summary>
    public static bool AnimateEffects { get; set; } = true;

    /// <summary>
    /// Si los carteles explican <b>además</b> la regla del juego que hay detrás.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Apaga la ayuda, nunca el dato.</b> Con esto en cero el cartel
    /// sigue diciendo qué perk es y cuánto pega; lo que deja de decir es por qué
    /// sacar ese perk lo destruye, o por qué la tienda no repone lo que se
    /// compró. Un juego con nueve clases, cuatro tiers de perk, una tienda que no
    /// repone y un sello que se levanta solo tiene más reglas de las que se
    /// descubren jugando, y las que no se descubren solas se pagan con una
    /// corrida entera.</para>
    ///
    /// <para>Prendido de fábrica, y esa es la decisión: el que ya sabe lo apaga
    /// una vez y no vuelve, y el que no sabe no tiene que enterarse primero de
    /// que existía una opción para enterarse.</para>
    ///
    /// <para>Vive en <see cref="Settings"/> y no en el guardado de la corrida
    /// porque es del jugador, no de la partida: no toca la simulación y no entra
    /// a ningún hash.</para>
    /// </remarks>
    public static bool ShowHelp { get; set; } = true;

    /// <summary>
    /// Si el token de la sesión se guarda en disco para no volver a entrar.
    /// </summary>
    /// <remarks>
    /// <para>Prendido de fábrica: escribir la contraseña en cada arranque es la
    /// molestia más grande del juego y la que menos aporta.</para>
    ///
    /// <para>⚠ <b>Existe apagado porque el archivo no se puede cifrar de verdad.</b>
    /// Para descifrarlo el juego necesitaría la clave, y la clave viviría al lado
    /// del archivo. En una computadora compartida, el tilde es la respuesta real;
    /// un cifrado casero sería solo una forma de decir que hay seguridad donde no
    /// la hay. Ver <see cref="SessionStore"/>.</para>
    ///
    /// <para>Lo que se guarda es el <b>token</b>, nunca la contraseña.</para>
    /// </remarks>
    public static bool RememberSession { get; set; } = true;

    /// <summary>Código del idioma: «es», «en»… Sale de Datosang.</summary>
    /// <remarks>
    /// ⚠ NO entra al hash de la corrida. Traducir no cambia la simulación: dos
    /// jugadores en idiomas distintos, con la misma seed, juegan lo mismo.
    /// </remarks>
    public static string Language { get; set; } = Loc.Reference;

    private static readonly Key[] _teclas = PorDefecto();

    private static Key[] PorDefecto() =>
    [
        Key.Space,   // Pausa
        Key.Key1,    // Velocidad1
        Key.Key2,    // Velocidad2
        Key.Key3,    // Velocidad3
        Key.Q,       // ClaseAnterior
        Key.E,       // ClaseSiguiente
        Key.Tab,     // Backpack
        Key.Enter,   // CallNextWave
    ];

    public static Key KeyFor(Shortcut atajo) => _teclas[(int)atajo];

    /// <summary>
    /// Qué acción tiene asignada esta tecla, o <c>null</c>.
    /// </summary>
    public static Shortcut? ShortcutFor(Key tecla)
    {
        for (int i = 0; i < _teclas.Length; i++)
        {
            if (_teclas[i] == tecla)
                return (Shortcut)i;
        }

        return null;
    }

    /// <summary>
    /// Asigna una tecla a una acción. Si estaba en otra, esa <b>queda libre</b>.
    /// </summary>
    /// <remarks>
    /// Se libera en vez de rechazar el cambio: rechazar obligaría al jugador a
    /// adivinar cuál era el conflicto y a desarmarlo primero. Así reasigna, ve el
    /// hueco, y le pone lo que quiera.
    /// </remarks>
    public static void Assign(Shortcut atajo, Key tecla)
    {
        if (tecla == Key.Escape || tecla == Key.None)
            return;

        for (int i = 0; i < _teclas.Length; i++)
        {
            if (_teclas[i] == tecla)
                _teclas[i] = Key.None;
        }

        _teclas[(int)atajo] = tecla;
        Store();
    }

    public static void ResetKeys()
    {
        var fabrica = PorDefecto();

        for (int i = 0; i < _teclas.Length; i++)
            _teclas[i] = fabrica[i];

        Store();
    }

    /// <summary>El nombre legible de una acción, para el menú.</summary>
    /// <remarks>
    /// La clave sale del nombre del enum, igual que <c>class.&lt;Clase&gt;.name</c>:
    /// un atajo nuevo se traduce agregando su renglón a los idiomas y nada más.
    /// Un atajo sin traducir muestra la clave cruda, que es lo que hace
    /// <see cref="Loc.Get(string)"/> a propósito.
    /// </remarks>
    public static string NameOf(Shortcut atajo) => Loc.Get($"ui.atajo.{atajo}");

    /// <summary>El nombre de una tecla como para mostrarlo.</summary>
    public static string KeyName(Key tecla)
        => tecla == Key.None ? "—" : OS.GetKeycodeString(tecla);

    public static void Load()
    {
        var config = new ConfigFile();

        if (config.Load(Route) != Error.Ok)
        {
            AplicarVolumenes();
            return;
        }

        MusicVolume = Mathf.Clamp((int)config.GetValue("sonido", "musica", 70), 0, 100);
        SoundVolume = Mathf.Clamp((int)config.GetValue("sonido", "efectos", 80), 0, 100);

        AnimateWeapons = (bool)config.GetValue("animaciones", "armas", true);
        AnimateEffects = (bool)config.GetValue("animaciones", "efectos", true);
        ShowHelp = (bool)config.GetValue("ayudas", "mostrar", true);
        Language = (string)config.GetValue("idioma", "codigo", Loc.Reference);
        RememberSession = (bool)config.GetValue("cuenta", "recordar", true);

        for (int i = 0; i < _teclas.Length; i++)
        {
            string nombre = ((Shortcut)i).ToString();
            _teclas[i] = (Key)(int)config.GetValue("teclas", nombre, (int)_teclas[i]);
        }

        AplicarVolumenes();
    }

    public static void Store()
    {
        var config = new ConfigFile();

        config.SetValue("sonido", "musica", MusicVolume);
        config.SetValue("sonido", "efectos", SoundVolume);

        config.SetValue("animaciones", "armas", AnimateWeapons);
        config.SetValue("animaciones", "efectos", AnimateEffects);
        config.SetValue("ayudas", "mostrar", ShowHelp);
        config.SetValue("idioma", "codigo", Language);
        config.SetValue("cuenta", "recordar", RememberSession);

        for (int i = 0; i < _teclas.Length; i++)
            config.SetValue("teclas", ((Shortcut)i).ToString(), (int)_teclas[i]);

        config.Save(Route);
        AplicarVolumenes();
    }

    /// <summary>
    /// Lleva los volúmenes a los buses de audio.
    /// </summary>
    /// <remarks>
    /// <para>Todavía no hay ni música ni efectos: esto prepara el terreno y hace
    /// que la preferencia ya se guarde. Los buses <c>Musica</c> y <c>Sonido</c>
    /// se buscan por nombre y, si no existen, no pasa nada — cuando se creen, el
    /// volumen guardado se aplica solo.</para>
    ///
    /// <para>⚠ El volumen va en <b>decibeles</b>, no en porcentaje: la percepción
    /// del volumen es logarítmica, y un 50 % lineal suena casi igual que el 100 %.
    /// <c>LinearToDb</c> hace esa conversión. Y 0 no es −∞ dB sino silencio de
    /// verdad, por eso el bus se muda a mute.</para>
    /// </remarks>
    private static void AplicarVolumenes()
    {
        Aplicar("Musica", MusicVolume);
        Aplicar("Sonido", SoundVolume);

        static void Aplicar(string bus, int porcentaje)
        {
            int i = AudioServer.GetBusIndex(bus);

            if (i < 0)
                return;

            AudioServer.SetBusMute(i, porcentaje <= 0);
            AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(porcentaje / 100f));
        }
    }
}
