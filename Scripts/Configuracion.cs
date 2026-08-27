using Godot;

namespace TowerOfArgentum;

/// <summary>Las acciones del juego que se pueden remapear.</summary>
/// <remarks>
/// ⚠ El orden de este enum es el orden en que se listan en el menú, y los
/// nombres se guardan en el archivo <b>por su texto</b>: renombrar uno pierde
/// la tecla que el jugador le había puesto, no la rompe.
/// </remarks>
public enum Atajo
{
    Pausa,
    Velocidad1,
    Velocidad2,
    Velocidad3,
    ClaseAnterior,
    ClaseSiguiente,
    Mochila,
    AdelantarOleada,
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
public static class Configuracion
{
    private const string Ruta = "user://opciones.cfg";

    /// <summary>Volumen de la música, 0 a 100.</summary>
    public static int VolumenMusica { get; set; } = 70;

    /// <summary>Volumen de los efectos, 0 a 100.</summary>
    public static int VolumenSonido { get; set; } = 80;

    /// <summary>
    /// Si los defensores animan el arma al atacar.
    /// </summary>
    /// <remarks>
    /// El cuerpo y la cabeza van quietos siempre —sus cuadros son el ciclo de
    /// caminar de AO y un defensor plantado que camina en el lugar marea—. Esto
    /// apaga lo único que se mueve, para quien quiera un tablero completamente
    /// estático.
    /// </remarks>
    public static bool AnimarArmas { get; set; } = true;

    /// <summary>Si el portal y el cristal de la Torre laten y giran.</summary>
    public static bool AnimarEfectos { get; set; } = true;

    private static readonly Key[] _teclas = PorDefecto();

    private static Key[] PorDefecto() =>
    [
        Key.Space,   // Pausa
        Key.Key1,    // Velocidad1
        Key.Key2,    // Velocidad2
        Key.Key3,    // Velocidad3
        Key.Q,       // ClaseAnterior
        Key.E,       // ClaseSiguiente
        Key.Tab,     // Mochila
        Key.Enter,   // AdelantarOleada
    ];

    public static Key TeclaDe(Atajo atajo) => _teclas[(int)atajo];

    /// <summary>
    /// Qué acción tiene asignada esta tecla, o <c>null</c>.
    /// </summary>
    public static Atajo? AtajoDe(Key tecla)
    {
        for (int i = 0; i < _teclas.Length; i++)
        {
            if (_teclas[i] == tecla)
                return (Atajo)i;
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
    public static void Asignar(Atajo atajo, Key tecla)
    {
        if (tecla == Key.Escape || tecla == Key.None)
            return;

        for (int i = 0; i < _teclas.Length; i++)
        {
            if (_teclas[i] == tecla)
                _teclas[i] = Key.None;
        }

        _teclas[(int)atajo] = tecla;
        Guardar();
    }

    public static void RestaurarTeclas()
    {
        var fabrica = PorDefecto();

        for (int i = 0; i < _teclas.Length; i++)
            _teclas[i] = fabrica[i];

        Guardar();
    }

    /// <summary>El nombre legible de una acción, para el menú.</summary>
    public static string NombreDe(Atajo atajo) => atajo switch
    {
        Atajo.Pausa => "Pausar",
        Atajo.Velocidad1 => "Velocidad x1",
        Atajo.Velocidad2 => "Velocidad x2",
        Atajo.Velocidad3 => "Velocidad x4",
        Atajo.ClaseAnterior => "Clase anterior",
        Atajo.ClaseSiguiente => "Clase siguiente",
        Atajo.Mochila => "Abrir la mochila",
        Atajo.AdelantarOleada => "Adelantar la oleada",
        _ => "?",
    };

    /// <summary>El nombre de una tecla como para mostrarlo.</summary>
    public static string NombreDeTecla(Key tecla)
        => tecla == Key.None ? "—" : OS.GetKeycodeString(tecla);

    public static void Cargar()
    {
        var config = new ConfigFile();

        if (config.Load(Ruta) != Error.Ok)
        {
            AplicarVolumenes();
            return;
        }

        VolumenMusica = Mathf.Clamp((int)config.GetValue("sonido", "musica", 70), 0, 100);
        VolumenSonido = Mathf.Clamp((int)config.GetValue("sonido", "efectos", 80), 0, 100);

        AnimarArmas = (bool)config.GetValue("animaciones", "armas", true);
        AnimarEfectos = (bool)config.GetValue("animaciones", "efectos", true);

        for (int i = 0; i < _teclas.Length; i++)
        {
            string nombre = ((Atajo)i).ToString();
            _teclas[i] = (Key)(int)config.GetValue("teclas", nombre, (int)_teclas[i]);
        }

        AplicarVolumenes();
    }

    public static void Guardar()
    {
        var config = new ConfigFile();

        config.SetValue("sonido", "musica", VolumenMusica);
        config.SetValue("sonido", "efectos", VolumenSonido);

        config.SetValue("animaciones", "armas", AnimarArmas);
        config.SetValue("animaciones", "efectos", AnimarEfectos);

        for (int i = 0; i < _teclas.Length; i++)
            config.SetValue("teclas", ((Atajo)i).ToString(), (int)_teclas[i]);

        config.Save(Ruta);
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
        Aplicar("Musica", VolumenMusica);
        Aplicar("Sonido", VolumenSonido);

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
