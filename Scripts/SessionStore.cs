using Godot;

namespace TowerOfArgentum;

/// <summary>
/// La sesión abierta, guardada en disco para no volver a escribir la contraseña.
/// </summary>
/// <remarks>
/// <para>Vive en <c>user://sesion.cfg</c>, al lado de las opciones y del guardado
/// de la corrida.</para>
///
/// <para>⚠ <b>Se guarda el TOKEN, nunca la contraseña.</b> Es la diferencia que
/// importa: el token es un permiso de 32 bytes que el servidor emitió, que
/// caduca, y que <c>/auth/logout</c> puede matar del otro lado. Una contraseña
/// guardada no caduca, no se puede revocar, y es la misma que la persona
/// probablemente usa en otro lado. Guardar la contraseña para "no tener que
/// escribirla" es cambiar una molestia por un problema.</para>
///
/// <para>⚠ <b>El archivo no está cifrado, y no puede estarlo de verdad.</b> Para
/// descifrarlo el juego necesitaría la clave, así que la clave viviría al lado
/// del archivo: cualquier cifrado acá es ofuscación, y llamarla seguridad sería
/// peor que no tenerla. El token vale exactamente lo que vale una cookie de
/// sesión de un navegador —quien tenga acceso a la carpeta del usuario tiene la
/// sesión— y por eso existe el <b>tilde de recordarme</b>, que es la respuesta
/// real al caso de la computadora compartida.</para>
///
/// <para>Se engancha a <see cref="Net.ApiClient.SessionChanged"/> una sola vez, en
/// el arranque. Así todo el disco vive acá y el cliente de red sigue siendo lo
/// que dice ser: HTTP y JSON, nada más.</para>
/// </remarks>
public static class SessionStore
{
    private const string Route = "user://sesion.cfg";

    /// <summary>
    /// El último nick que entró, para dejarlo escrito en el campo.
    /// </summary>
    /// <remarks>
    /// Sobrevive a que caduque el token y a salir de la cuenta: no es un secreto,
    /// y volver a escribirlo cada vez es la mitad de la molestia de entrar.
    /// </remarks>
    public static string RememberedNick { get; private set; } = "";

    private static bool _enganchada;

    /// <summary>
    /// Escucha los cambios de sesión del cliente de red. Se llama una sola vez.
    /// </summary>
    /// <remarks>
    /// ⚠ Con un evento y no llamando a <see cref="Store"/> desde
    /// <c>ApiClient</c>: el 401 que vacía el token está enterrado en el
    /// transporte, y un token muerto que sigue en disco es exactamente el bug que
    /// deja al jugador viendo fallar todo sin entender que tiene que volver a
    /// entrar. Enganchado al evento, <b>toda</b> forma de perder la sesión borra
    /// el archivo, incluidas las que se escriban mañana.
    /// </remarks>
    public static void Hook()
    {
        if (_enganchada)
            return;

        _enganchada = true;
        Net.ApiClient.SessionChanged += AlCambiar;
    }

    private static void AlCambiar()
    {
        if (!Net.ApiClient.LoggedIn)
        {
            Delete();
            return;
        }

        Store();
    }

    /// <summary>
    /// Lee el archivo y, si el token sigue vivo, se lo pasa al cliente de red.
    /// </summary>
    /// <returns><c>true</c> si quedó una sesión que vale la pena probar.</returns>
    /// <remarks>
    /// <para>⚠ <b>Devolver <c>true</c> no quiere decir que la sesión sirva.</b>
    /// Un token puede estar sin caducar acá y muerto del otro lado —la cuenta
    /// cerró sesión desde otra máquina, la base se reinició, el servidor lo
    /// revocó—. Lo único que dice esto es «hay algo que probar»; quien decide es
    /// el servidor, y por eso la pantalla de login lo valida contra
    /// <c>/perfil/me</c> antes de dar por abierta la sesión.</para>
    ///
    /// <para>La caducidad se chequea igual, para no gastar un viaje al servidor
    /// —y una espera— con un token que ya sabemos vencido.</para>
    /// </remarks>
    public static bool Load()
    {
        var config = new ConfigFile();

        if (config.Load(Route) != Error.Ok)
            return false;

        RememberedNick = (string)config.GetValue("sesion", "nick", "");

        string token = (string)config.GetValue("sesion", "token", "");
        long expira = (long)config.GetValue("sesion", "expira", 0L);

        if (token.Length == 0)
            return false;

        if (expira > 0 && Ahora() >= expira)
        {
            // Vencido: se borra en vez de dejarlo. Un archivo con un token muerto
            // hace que cada arranque gaste un viaje al servidor para que le digan
            // que no.
            Delete();
            return false;
        }

        Net.ApiClient.Restore(token, RememberedNick, expira);
        return true;
    }

    /// <summary>Guarda la sesión que tiene abierta el cliente de red.</summary>
    public static void Store()
    {
        if (!Net.ApiClient.LoggedIn)
            return;

        // El nick se recuerda SIEMPRE, aunque el jugador no quiera guardar la
        // sesión: es lo que deja el campo escrito la próxima vez, y no es un
        // secreto. Lo que el tilde decide es si se guarda el TOKEN.
        RememberedNick = Net.ApiClient.Nick ?? RememberedNick;

        var config = new ConfigFile();

        config.SetValue("sesion", "nick", RememberedNick);

        if (Settings.RememberSession)
        {
            config.SetValue("sesion", "token", Net.ApiClient.Token ?? "");
            config.SetValue("sesion", "expira", Net.ApiClient.ExpiresUnix);
        }

        config.Save(Route);
    }

    /// <summary>
    /// Borra el token del disco. <b>El nick se conserva.</b>
    /// </summary>
    /// <remarks>
    /// Salir de la cuenta no es olvidar quién sos: el campo queda escrito y solo
    /// hace falta la contraseña. Borrar las dos cosas convertiría cada logout en
    /// un login desde cero.
    /// </remarks>
    public static void Delete()
    {
        var config = new ConfigFile();

        config.SetValue("sesion", "nick", RememberedNick);
        config.Save(Route);
    }

    private static long Ahora() => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
