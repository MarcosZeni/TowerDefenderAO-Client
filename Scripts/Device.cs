using Godot;

namespace TowerOfArgentum;

/// <summary>
/// Desde qué clase de aparato se está jugando.
/// </summary>
/// <remarks>
/// <para>Existe por una sola cosa: el ranking desempata por <c>ticks</c>, así que
/// con el tiempo es <b>una tabla de velocidad</b>, y ahí el control táctil contra
/// el mouse es una diferencia real de precisión y de latencia. La corrida se
/// etiqueta desde el día uno para poder <b>medir</b> esa diferencia antes de
/// decidir si el ranking se parte en dos.</para>
///
/// <para>⚠ <b>Es lo único que el servidor no puede verificar nunca.</b> El área, la
/// oleada y los ticks se re-derivan de la seed más el log; de dónde salió el
/// pedido, no. Es una declaración del cliente y quien compile el suyo la cambia.
/// Por eso hoy la plataforma es informativa y no separa tablas: partir el ranking
/// por un campo falseable es otra decisión.</para>
///
/// <para>Los valores viajan en <b>español</b> —<c>"pc"</c> y <c>"celular"</c>—
/// porque son valores de un campo de la API, y ahí manda el contrato: la columna
/// <c>runs.plataforma</c> es un <c>ENUM('pc','celular')</c>.</para>
/// </remarks>
public static class Device
{
    /// <summary>
    /// <c>"celular"</c> en Android y iOS, <c>"pc"</c> en todo lo demás.
    /// </summary>
    /// <remarks>
    /// La web queda del lado de <c>"pc"</c> a propósito: un navegador de escritorio
    /// es lo esperable, y meterla en su propia categoría partiría la tabla una vez
    /// más para una población que hoy es cero.
    /// </remarks>
    public static string Kind => SimulateMobile
        ? "celular"
        : OS.GetName() switch
        {
            "Android" or "iOS" => "celular",
            _ => "pc",
        };

    /// <summary>
    /// Fuerza <c>"celular"</c> desde la PC. Solo para probar.
    /// </summary>
    /// <remarks>
    /// Lo prende el argumento <c>--celular</c> (ver <c>Main</c>). Que exista es
    /// también la demostración de por qué la plataforma es un dato informativo y
    /// no puede separar tablas del ranking: si nosotros la falseamos con un
    /// argumento, cualquiera puede.
    /// </remarks>
    public static bool SimulateMobile { get; set; }

    /// <summary>
    /// Si se juega con el dedo: Android, iOS, o la PC con <c>--celular</c>.
    /// </summary>
    /// <remarks>
    /// Lo miran las pantallas que tienen algo que solo se hace con teclado —la
    /// mochila con Tab, la velocidad con 1-2-3, adelantar con Enter— para dibujar
    /// el botón que lo reemplaza. Incluye <see cref="SimulateMobile"/> a propósito:
    /// así esos botones se prueban desde la PC sin exportar un APK.
    /// </remarks>
    public static bool IsTouch => SimulateMobile || OS.HasFeature("mobile");
}
