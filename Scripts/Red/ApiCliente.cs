using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;

namespace TowerOfArgentum.Red;

// -----------------------------------------------------------------------------
//  Las respuestas del GameApi.
// -----------------------------------------------------------------------------
//
//  ⚠ EL NOMBRE DE C# Y EL NOMBRE DE LA RED SON DOS COSAS DISTINTAS, y acá se
//  ven las dos: la propiedad está en inglés como todo el cliente, y el
//  [JsonPropertyName] fija el nombre que viaja por el cable, que sigue en
//  español porque así lo emite el servidor.
//
//  Sin el atributo esto funcionaría igual —el cliente deserializa con
//  PropertyNameCaseInsensitive— pero solo por casualidad: al renombrar la
//  propiedad, el campo dejaría de matchear y llegaría en CERO, sin error y sin
//  aviso. Un ranking que muestra oleada 0 para todos es mucho peor que uno que
//  no carga.
//
//  ⚠ CAMBIAR UN NOMBRE DE ACÁ NO ES UN RENOMBRE: es tocar el contrato de la API,
//  y hay que hacerlo del lado del servidor primero. Los tres repos se despliegan
//  por separado.

public sealed record Session(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("nick")] string Nick,
    [property: JsonPropertyName("oro")] long Gold,
    [property: JsonPropertyName("expiraEnSegundos")] int ExpiresInSeconds);

public sealed record RunStarted(
    [property: JsonPropertyName("runId")] long RunId,
    [property: JsonPropertyName("seed")] ulong Seed,
    [property: JsonPropertyName("datosVersion")] int DataVersion,
    [property: JsonPropertyName("abandonada")] long? Abandoned);

public sealed record RunFinished(
    [property: JsonPropertyName("oroGanado")] long GoldEarned,
    [property: JsonPropertyName("oroTotal")] long GoldTotal,
    [property: JsonPropertyName("oleada")] int Wave,
    [property: JsonPropertyName("gano")] bool Won);

public sealed record Profile(
    [property: JsonPropertyName("nick")] string Nick,
    [property: JsonPropertyName("oro")] long Gold,
    [property: JsonPropertyName("planos")] string[] Blueprints);

public sealed record RankingRow(
    [property: JsonPropertyName("puesto")] int Position,
    [property: JsonPropertyName("nick")] string Nick,
    [property: JsonPropertyName("gano")] bool Won,
    [property: JsonPropertyName("oleada")] int Wave,
    [property: JsonPropertyName("area")] int Area,
    [property: JsonPropertyName("ticks")] int Ticks,
    [property: JsonPropertyName("validado")] bool Validated);

/// <summary>
/// Cliente del GameApi. <b>HTTP y JSON, nada más.</b>
/// </summary>
/// <remarks>
/// <para>⚠ <b>Nada del protocolo binario de Austral entra acá.</b> Ni sockets, ni
/// <c>TwisterCrypto</c>, ni CRC, ni opcodes, ni Latin-1. Vale la pena decirlo
/// porque <c>TwisterCrypto.cs</c> vive en la <b>misma carpeta</b> que
/// <c>LoginClient.cs</c> en el cliente de Austral, y al copiar esa pieza es muy
/// fácil llevárselo de arrastre.</para>
///
/// <para><b>El cliente no hashea la contraseña.</b> La manda tal cual sobre TLS y
/// el servidor la hashea con Argon2id y sal por usuario. Si hasheara el cliente,
/// ese hash <i>sería</i> la contraseña —quien copiara la base lo reenviaría y
/// entraría—. Y hashear con Argon2 acá tampoco serviría: la sal es por usuario y
/// vive en la base, así que habría que pedirla antes de loguear, y eso delata qué
/// cuentas existen.</para>
/// </remarks>
public static class ApiCliente
{
    // ⚠ El tipo va calificado entero porque Godot trae su PROPIO `HttpClient`, y
    // con `using Godot;` arriba el nombre corto es ambiguo. El cliente de Austral
    // lo escribe igual, por lo mismo.
    //
    // El de Godot es un cliente HTTP de bajo nivel pensado para el juego; el de
    // .NET tiene pool de conexiones, JSON y async como corresponde. Acá va el de
    // .NET: esto es HTTP/JSON común, no algo que necesite integrarse al bucle del
    // motor.
    private static readonly System.Net.Http.HttpClient _http = new()
    {
        // 30 segundos y no 100 (el default): si la API no contestó en medio
        // minuto, no va a contestar, y el jugador merece un error en vez de una
        // pantalla congelada.
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Dónde vive el servidor. En local, el 5090.</summary>
    public static string BaseUrl { get; set; } = "http://127.0.0.1:5090";

    /// <summary>El token de la sesión abierta. Va en el header de cada pedido.</summary>
    public static string? Token { get; private set; }

    /// <summary>El nick de la sesión abierta, para poder mostrarlo y recordarlo.</summary>
    public static string? Nick { get; private set; }

    /// <summary>
    /// Cuándo caduca el token, en segundos Unix UTC. 0 = no se sabe.
    /// </summary>
    /// <remarks>
    /// El servidor manda <c>expiraEnSegundos</c>, o sea un plazo; acá se guarda el
    /// <b>instante</b>. Guardar el plazo no serviría: el archivo de sesión
    /// sobrevive al cierre del juego, y un «faltan 30 días» escrito en disco sigue
    /// diciendo lo mismo dentro de un año.
    /// </remarks>
    public static long ExpiraUnix { get; private set; }

    public static bool Logueado => !string.IsNullOrEmpty(Token);

    /// <summary>
    /// Se dispara cada vez que la sesión se abre, se cierra o se pierde.
    /// </summary>
    /// <remarks>
    /// ⚠ Existe para que <see cref="Sesion"/> pueda persistirla <b>sin</b> que
    /// este archivo toque el disco. Es la línea que separa las dos cosas: acá
    /// vive HTTP y JSON, y nada más. También es lo que hace que el 401 enterrado
    /// en el transporte borre el archivo, que es el camino por el que un token
    /// muerto se quedaba pegado.
    /// </remarks>
    public static event Action? SesionCambio;

    /// <summary>
    /// Vuelve a poner una sesión guardada en disco, sin ir al servidor.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Esto no valida nada.</b> El token puede estar muerto del otro lado
    /// —la cuenta cerró sesión en otra máquina, el servidor lo revocó— y acá no
    /// hay forma de saberlo. Quien decide es el servidor: la pantalla de login
    /// pega un <c>/perfil/me</c> antes de dar la sesión por abierta.
    ///
    /// No dispara <see cref="SesionCambio"/> a propósito: viene del disco, así
    /// que volver a escribirlo sería morderse la cola.
    /// </remarks>
    public static void Restaurar(string token, string nick, long expiraUnix)
    {
        Token = token;
        Nick = nick;
        ExpiraUnix = expiraUnix;
    }

    public static void CerrarSesionLocal() => Olvidar();

    private static void Recordar(Session sesion)
    {
        Token = sesion.Token;
        Nick = sesion.Nick;

        ExpiraUnix = sesion.ExpiresInSeconds > 0
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + sesion.ExpiresInSeconds
            : 0;

        SesionCambio?.Invoke();
    }

    private static void Olvidar()
    {
        Token = null;
        ExpiraUnix = 0;

        // ⚠ El nick NO se borra: no es un secreto, y es lo que deja el campo
        // escrito la próxima vez. Borrarlo convertiría cada logout en un login
        // desde cero.
        SesionCambio?.Invoke();
    }

    public static async Task<(Session? Session, string? Error)> RegistrarAsync(
        string nick, string password, string? email)
    {
        var (ok, sesion, error) = await PostAsync<Session>(
            "/auth/registro", new { nick, password, email });

        if (ok && sesion is not null)
            Recordar(sesion);

        return (sesion, error);
    }

    public static async Task<(Session? Session, string? Error)> LoginAsync(string nick, string password)
    {
        var (ok, sesion, error) = await PostAsync<Session>("/auth/login", new { nick, password });

        if (ok && sesion is not null)
            Recordar(sesion);

        return (sesion, error);
    }

    public static async Task LogoutAsync()
    {
        await PostAsync<object>("/auth/logout", new { });
        Olvidar();
    }

    /// <summary>
    /// Abre una corrida. <b>La seed la trae el servidor.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠ Si el jugador tenía otra corrida abierta, esta la cierra como
    /// abandono y devuelve su id en <c>Abandonada</c>. La UI tiene que avisar
    /// <b>antes</b> de llamar acá: son horas de partida.</para>
    ///
    /// <para><b>Sin mapa.</b> El área no se elige: se llega o no se llega.</para>
    /// </remarks>
    public static Task<(bool Ok, RunStarted? Datos, string? Error)> IniciarCorridaAsync(string hashDatos)
        => PostAsync<RunStarted>("/run/start", new { hashDatos });

    /// <summary>
    /// Cierra la corrida con el reclamo del cliente.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>No manda oro ni precios.</b> El servidor deriva
    /// <c>oroGanado = f(gano, oleada)</c>. El log va en base64 y lleva
    /// <b>decisiones, no resultados</b>: lo que el RNG sembrado determina, el
    /// servidor lo regenera de la seed.
    /// </remarks>
    public static Task<(bool Ok, RunFinished? Datos, string? Error)> TerminarCorridaAsync(
        long runId, bool gano, int oleada, int area, int ticks, byte[] log)
        => PostAsync<RunFinished>("/run/finish", new
        {
            runId,
            gano,
            oleada,
            area,
            ticks,
            log = Convert.ToBase64String(log),
        });

    public static Task<(bool Ok, Profile? Datos, string? Error)> PerfilAsync()
        => GetAsync<Profile>("/perfil/me");

    public static Task<(bool Ok, RespuestaRanking? Datos, string? Error)> RankingAsync(int limite = 100)
        => GetAsync<RespuestaRanking>($"/ranking?limite={limite}");

    /// <summary>
    /// Compra un plano de fusión con la billetera.
    /// </summary>
    /// <remarks>
    /// ⚠ Manda <b>solo el nombre</b>. El precio sale de la lista del servidor: si
    /// lo mandara el cliente, se compraría todo a un peso.
    /// </remarks>
    public static Task<(bool Ok, object? Datos, string? Error)> ComprarPlanoAsync(string plano)
        => PostAsync<object>("/tienda/plano", new { plano });

    public sealed record RespuestaRanking(int Version, RankingRow[] Filas);

    // -------------------------------------------------------------------------
    //  Plomería
    // -------------------------------------------------------------------------

    private static async Task<(bool Ok, T? Datos, string? Error)> PostAsync<T>(string ruta, object cuerpo)
    {
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, BaseUrl + ruta)
            {
                Content = JsonContent.Create(cuerpo),
            };

            return await EnviarAsync<T>(pedido);
        }
        catch (Exception e)
        {
            return (false, default, MensajeDeRed(e));
        }
    }

    private static async Task<(bool Ok, T? Datos, string? Error)> GetAsync<T>(string ruta)
    {
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, BaseUrl + ruta);
            return await EnviarAsync<T>(pedido);
        }
        catch (Exception e)
        {
            return (false, default, MensajeDeRed(e));
        }
    }

    private static async Task<(bool Ok, T? Datos, string? Error)> EnviarAsync<T>(HttpRequestMessage pedido)
    {
        if (Token is not null)
            pedido.Headers.Add("Authorization", "Bearer " + Token);

        using var respuesta = await _http.SendAsync(pedido);
        string texto = await respuesta.Content.ReadAsStringAsync();

        if (respuesta.IsSuccessStatusCode)
        {
            if (string.IsNullOrEmpty(texto))
                return (true, default, null);

            return (true, JsonSerializer.Deserialize<T>(texto, _json), null);
        }

        // El 401 vacía el token local. Sin esto, el juego seguiría mandando un
        // token muerto en cada pedido y el jugador vería fallar todo sin entender
        // que lo que tiene que hacer es volver a entrar.
        if (respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized && Token is not null)
            Olvidar();

        return (false, default, MensajeDeError(texto, respuesta.StatusCode));
    }

    /// <summary>
    /// Saca el mensaje del <c>ErrorResponse</c> de la API.
    /// </summary>
    /// <remarks>
    /// Los mensajes del servidor ya vienen en español y escritos para una
    /// persona; reescribirlos acá sería mantener el mismo texto en dos lados.
    /// </remarks>
    private static string MensajeDeError(string texto, System.Net.HttpStatusCode codigo)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorApi>(texto, _json);

            if (!string.IsNullOrEmpty(error?.Mensaje))
                return error.Mensaje;
        }
        catch (JsonException)
        {
            // El cuerpo no era el JSON esperado: puede ser una página de error de
            // un proxy delante de la API. Cae al mensaje genérico de abajo.
        }

        return $"El servidor respondió {(int)codigo}.";
    }

    private static string MensajeDeRed(Exception e)
    {
        GD.PushWarning($"ApiCliente: {e.Message}");

        return e switch
        {
            TaskCanceledException => "El servidor tardó demasiado en responder.",
            HttpRequestException => "No se pudo conectar con el servidor.",
            _ => "Hubo un problema al hablar con el servidor.",
        };
    }

    private sealed record ErrorApi(string Codigo, string Mensaje);
}
