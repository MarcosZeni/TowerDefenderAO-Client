using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Ui;

/// <summary>
/// Los tokens de color de la interfaz. <b>Única fuente de verdad.</b>
/// </summary>
/// <remarks>
/// <para>Salen tal cual del panel de diseño que está en
/// <c>Resources\ui\preview.png</c> y de su especificación
/// <c>Resources\ui\tower_of_argentum_ui_mockup_spec.json</c>, bloque
/// <c>palette</c>. Los hex se copian <b>sin retocar</b>: si un color se ve mal
/// en pantalla se cambia el diseño y se vuelve a copiar, no se ajusta acá.</para>
///
/// <para>El motivo de que exista este archivo es que antes cada pantalla
/// inventaba sus propios <c>new Color(0.06f, 0.06f, 0.09f, 0.72f)</c>, y eran
/// distintos entre sí: el HUD, la mochila y la tienda no compartían un solo
/// valor. Con la interfaz dibujada a mano en <c>_Draw</c> —que es como está
/// hecha— la consistencia no la da el motor, la da esto.</para>
///
/// <para>⚠ Ningún <c>new Color(...)</c> literal fuera de acá en las pantallas.
/// Si hace falta un color que no está, se agrega como token con nombre.</para>
/// </remarks>
public static class Palette
{
    // --- Fondos. Del más hundido al más elevado. ---

    /// <summary>El fondo de la pantalla entera, detrás de todo.</summary>
    public static readonly Color BackgroundMain = new("#0F131A");

    /// <summary>Fondo de segundo nivel: hueco de grilla, riel de barra.</summary>
    public static readonly Color BackgroundAlt = new("#161C24");

    /// <summary>El relleno de un panel normal.</summary>
    public static readonly Color Panel = new("#1C2430");

    /// <summary>El relleno de lo que está por encima: tarjeta, pestaña activa.</summary>
    public static readonly Color PanelElevated = new("#243041");

    // --- Bordes. Tres pesos. ---

    /// <summary>Divisiones internas y separadores.</summary>
    public static readonly Color BorderSoft = new("#334155");

    /// <summary>El borde de un panel.</summary>
    public static readonly Color BorderNormal = new("#46566F");

    /// <summary>El brillo del metal: esquinas, remaches, aristas iluminadas.</summary>
    public static readonly Color BorderLight = new("#6A7C98");

    // --- Texto. Cuatro escalones de jerarquía. ---

    /// <summary>Números y nombres. Lo que el jugador viene a leer.</summary>
    public static readonly Color TextPrimary = new("#EAF2FF");

    /// <summary>Descripciones y etiquetas de stat.</summary>
    public static readonly Color TextSecondary = new("#B7C4D8");

    /// <summary>Títulos de sección, unidades, texto de apoyo.</summary>
    public static readonly Color TextMuted = new("#7E8CA3");

    /// <summary>Lo que no se puede usar ahora.</summary>
    public static readonly Color TextDisabled = new("#5B6575");

    // --- Estados funcionales. ---

    /// <summary>Se puede, alcanza el oro, salió bien.</summary>
    public static readonly Color Success = new("#45D26B");

    /// <summary>Ojo con esto: mochila casi llena, vida a la mitad.</summary>
    public static readonly Color Warning = new("#F7B844");

    /// <summary>Dato neutro resaltado.</summary>
    public static readonly Color Info = new("#45C7FF");

    /// <summary>Se perdió, la Torre está por caer, no alcanza.</summary>
    public static readonly Color Danger = new("#E04A4A");

    // --- Rarezas. Seis marcos. ---

    /// <summary>Vacío, bloqueado, sin dueño.</summary>
    public static readonly Color Common = new("#8A97AC");

    public static readonly Color Uncommon = new("#46C96B");
    public static readonly Color Rare = new("#3EA1FF");
    public static readonly Color Epic = new("#A95CFF");
    public static readonly Color Legendary = new("#F0B63C");
    public static readonly Color Unique = new("#FF4B47");

    // --- El oro. Es el único número que tiene color propio en todas las
    //     pantallas, así que tiene token en vez de reusar Legendary. ---

    /// <summary>El color de la moneda, en el HUD y en cada precio.</summary>
    public static readonly Color Gold = new("#F0B63C");

    // --- Los metales de los tiers ---------------------------------------------
    //
    // ⚠ EL TIER SE LLAMA BRONCE Y TIENE QUE VERSE DE BRONCE. Antes tomaba el
    // verde y el azul de la escala de rareza del panel del diseñador, y eso
    // producía la contradicción más fácil de ver del juego: una tarjeta que dice
    // BRONCE con todo el marco azul. Que el rótulo lo aclare no alcanza — el
    // color se lee antes que la palabra, y en una grilla de veinte huecos el
    // jugador NUNCA lee la palabra.
    //
    // El verde y el azul no se tiran: siguen siendo la escala de rareza de las
    // ARMAS, que es otra cosa y ya estaba escrito que tenía que distinguirse.

    /// <summary>Cobre quemado. El tier Bronce.</summary>
    public static readonly Color Bronze = new("#C87A3C");

    /// <summary>Plata fría, tirando a azul para que no se confunda con el gris.</summary>
    public static readonly Color Silver = new("#C6D4E4");

    /// <summary>
    /// El color de rareza de un tier de perk.
    /// </summary>
    /// <remarks>
    /// <para>El diseño trae <b>seis</b> marcos y el juego tiene <b>cuatro</b>
    /// tiers (<see cref="PerkTier"/>), así que sobran dos. No se descartan: se
    /// les da el trabajo que el juego sí necesita.</para>
    ///
    /// <list type="bullet">
    /// <item><b>Común</b> (gris) — la ranura vacía y lo bloqueado. Un slot libre
    /// tiene que verse como un slot, no como un agujero.</item>
    /// <item><b>Épico</b> (violeta) — el <b>arma con habilidad</b>. Pelea por la
    /// misma ranura de comportamiento que un Oro o un Único
    /// (<c>JUGABILIDAD.md</c>), y el jugador elige cuál de los dos habla: si
    /// compartiera color con el Oro, esa competencia no se vería.</item>
    /// </list>
    ///
    /// <para>⚠ <b>Los cuatro tiers usan sus METALES, no la escala de rareza.</b>
    /// Iban en verde → azul → dorado → rojo, la escala que un ARPG usa, y eso
    /// producía la contradicción más fácil de ver del juego: una tarjeta que dice
    /// <b>BRONCE</b> con todo el marco <b>azul</b>. Que el rótulo lo aclare no
    /// alcanza — el color se lee antes que la palabra, y en una grilla de veinte
    /// huecos el jugador nunca llega a la palabra.</para>
    ///
    /// <para>El verde y el azul no se tiran: quedan para la escala de rareza de
    /// las <b>armas</b>, que es otra escala y ya estaba escrito que tenía que
    /// distinguirse de esta.</para>
    /// </remarks>
    public static Color Rarity(PerkTier tier) => tier switch
    {
        PerkTier.Bronze => Bronze,
        PerkTier.Silver => Silver,
        PerkTier.Gold => Legendary,
        PerkTier.Unique => Unique,
        _ => Common,
    };

    /// <summary>El mismo color con otra transparencia. Para relleno y glow.</summary>
    public static Color Alpha(this Color color, float a) => new(color.R, color.G, color.B, a);

    /// <summary>
    /// El color de una barra de vida según lo que queda.
    /// </summary>
    /// <remarks>
    /// Verde arriba del 50 %, ámbar arriba del 20 %, rojo abajo. El color dice la
    /// urgencia antes de que el jugador llegue a leer el número, que es lo único
    /// que puede hacer mientras le están pegando a la Torre.
    /// </remarks>
    public static Color Health(int porcentaje)
        => porcentaje > 50 ? Success
         : porcentaje > 20 ? Warning
         : Danger;
}
