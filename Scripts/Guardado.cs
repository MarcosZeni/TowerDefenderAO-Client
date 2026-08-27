using Godot;
using TowerOfArgentum.Simulacion;

// ⚠ Alias porque `FileAccess` existe en Godot Y en System.IO, y con
// `using Godot;` arriba el nombre corto es ambiguo. Es el segundo choque de este
// tipo en el proyecto —el otro es HttpClient, en Red\ApiCliente.cs— y en los dos
// casos la respuesta es la misma: se usa el de Godot y se dice cuál.
//
// Acá va el de Godot y no el de .NET porque este archivo escribe en `user://`,
// que es una ruta virtual del motor: en Windows apunta a %APPDATA%, y una build
// instalada en Archivos de Programa no puede escribir al lado del ejecutable.
using ArchivoGodot = Godot.FileAccess;

namespace TowerOfArgentum;

/// <summary>
/// Guardar y continuar una corrida.
/// </summary>
/// <remarks>
/// <para><b>Esto sale gratis del determinismo, y es una de las mejores
/// consecuencias del diseño.</b> Como la simulación arranca de una seed y avanza
/// determinísticamente, no hay que serializar el estado del juego:</para>
///
/// <list type="bullet">
/// <item><b>Guardar</b> es escribir la seed y el log de acciones.</item>
/// <item><b>Cargar</b> es re-simular desde el tick 0 a máxima velocidad.</item>
/// </list>
///
/// <para>Lo que se evita con eso: un formato de save aparte que hay que versionar,
/// un serializador para cada entidad nueva, y —sobre todo— la posibilidad de que
/// el save quede desincronizado del código. <b>Si el save carga, es porque el
/// código lo reprodujo exactamente.</b> Es el mismo mecanismo que usa el servidor
/// para revalidar, corriendo del lado del cliente.</para>
///
/// <para>Hace falta porque una corrida son 100 oleadas y puede durar horas. Sin
/// esto, dejarla por la mitad sería perderla — y el <c>runId</c> no caduca por
/// tiempo justamente para que se pueda retomar.</para>
/// </remarks>
public static class Guardado
{
    /// <summary>
    /// <c>user://</c> y no una ruta al lado del ejecutable: en Windows es
    /// <c>%APPDATA%</c>, y una build instalada en Archivos de Programa no puede
    /// escribir en su propia carpeta.
    /// </summary>
    private const string Route = "user://corrida.guardada";

    /// <summary>
    /// v2 agrega el <b>tick del momento de guardar</b>. La v1 re-simulaba hasta
    /// la última acción del log, y guardar en medio de una oleada —Alt+F4, cierre
    /// de la ventana— retomaba REBOBINADO a la última acción: los segundos de
    /// combate posteriores se perdían y se podían volver a jugar distinto.
    /// </summary>
    private const byte Version = 2;

    public static bool Existe() => ArchivoGodot.FileExists(Route);

    public static void Borrar()
    {
        if (Existe())
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Route));
    }

    /// <summary>Escribe la corrida en curso, parada en <paramref name="tick"/>.</summary>
    public static bool Store(ulong seed, long runId, ActionLog log, int tick)
    {
        using var f = ArchivoGodot.Open(Route, ArchivoGodot.ModeFlags.Write);

        if (f is null)
        {
            GD.PushWarning($"No se pudo escribir {Route}: {ArchivoGodot.GetOpenError()}");
            return false;
        }

        f.Store8(Version);
        f.Store64(seed);

        // El runId va en el guardado porque la corrida sigue abierta del lado del
        // servidor: al terminarla hay que cerrar ESA, no abrir una nueva. Sin
        // esto, retomar una partida guardada dejaría la corrida original colgada y
        // abriría otra, perdiendo el progreso reportable.
        f.Store64((ulong)runId);

        // El tick del momento de guardar. Es lo que permite retomar EXACTO aunque
        // el cierre haya caído en medio de una oleada.
        f.Store32((uint)tick);

        byte[] blob = BinaryLog.Escribir(log);
        f.Store32((uint)blob.Length);
        f.StoreBuffer(blob);

        return true;
    }

    /// <summary>
    /// Lee el guardado y <b>re-simula la corrida entera</b> hasta el punto donde
    /// estaba.
    /// </summary>
    /// <remarks>
    /// Re-simular ~300.000 ticks sin render tarda del orden de un segundo, así
    /// que no hace falta ni barra de progreso. Si algún día tardara, la salida es
    /// hacerlo en un hilo aparte —no cachear estado, que es lo que traería de
    /// vuelta todos los problemas que este diseño evita.
    /// </remarks>
    public static (Run? Run, ActionLog? Log, ulong Seed, long RunId) Cargar()
    {
        using var f = ArchivoGodot.Open(Route, ArchivoGodot.ModeFlags.Read);

        if (f is null)
            return (null, null, 0, 0);

        byte version = f.Get8();

        if (version != Version)
        {
            // Un guardado de otra versión del formato se descarta. Es aceptable
            // perderlo —es una partida a medias, no el historial— y mucho mejor
            // que cargar algo que se re-simula distinto.
            GD.PushWarning($"El guardado es de la versión {version} y esta build lee la {Version}.");
            return (null, null, 0, 0);
        }

        ulong seed = f.Get64();
        long runId = (long)f.Get64();
        int tick = (int)f.Get32();

        uint largo = f.Get32();
        byte[] blob = f.GetBuffer((long)largo);

        var log = BinaryLog.Leer(blob);

        // El mapa sale de la seed, igual que en una corrida nueva: guardarlo
        // sería guardar algo que la seed ya determina.
        var mapa = Layouts.Choose(seed, CargadorDatos.Actual);

        // Acá pasa todo: se re-simula desde cero con el mismo log y la misma seed.
        //
        // ⚠ Con los MISMOS datos de balance. La v1 pasaba `datos: null` y la
        // re-simulación corría con los números de fábrica en vez de con los de
        // Datos\*.json — un guardado hecho jugando no se reproducía igual.
        var partida = new Run(seed, mapa, log, CargadorDatos.Actual);
        partida.Empezar();

        // Hasta el tick guardado, y ni uno más: correr de largo seguiría la
        // simulación sin el jugador.
        while (partida.State == RunState.InProgress)
        {
            // La corrida quedó en una pausa sin acciones pendientes —la pantalla
            // de preparación, casi siempre—. Acá el tick está congelado, así que
            // sin este corte el bucle gira para siempre.
            if (partida.EsperaAlJugador)
                break;

            if (!partida.Paused && partida.Tick >= tick)
                break;

            partida.Advance();
        }

        GD.Print($"RunCurve retomada · seed {seed} · run {runId} · " +
                 $"tick {partida.Tick} · oleada {partida.Wave}");

        return (partida, log, seed, runId);
    }
}
