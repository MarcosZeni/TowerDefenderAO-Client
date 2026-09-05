namespace TowerOfArgentum.Audio;

/// <summary>
/// Cada evento del juego que suena. <b>Es la clave, no el archivo.</b>
/// </summary>
/// <remarks>
/// <para>El código llama a <c>Sfx.Play(SoundId.TowerPlace)</c> y nunca nombra un
/// archivo. Qué wav suena, a qué volumen y con cuánta variación lo decide
/// <c>Data/audio/audio.json</c>, y por eso cambiar un sonido —o agregarle una
/// variante— es copiar un archivo y tocar una línea de JSON, sin recompilar.</para>
///
/// <para>⚠ <b>El nombre del valor ES la clave del JSON.</b> Renombrar acá sin
/// renombrar allá deja el evento mudo, y el cargador lo dice al arrancar en vez
/// de callarse. Es la misma costura que la de <see cref="Loc"/> con las claves de
/// texto: un evento sin sonido tiene que verse en la primera pasada.</para>
///
/// <para>Los identificadores van en inglés, como todo lo que se nombra en el
/// proyecto (regla de oro 4).</para>
/// </remarks>
public enum SoundId
{
    // Ataques: uno por familia de daño, no uno por clase. Nueve clases mapean a
    // estos nueve a través de `AudioCues.SonidoDeAtaque`.
    AttackBow,
    AttackSword,
    AttackMagic,
    AttackFire,
    AttackIce,
    AttackLightning,
    AttackPoison,
    AttackCannon,
    AttackMelee,

    // Impactos: suenan cuando el proyectil dibujado llega, no cuando se cobró el
    // daño. Ver `AudioCues` y `Game.TicksDeVuelo`.
    ImpactArrow,
    ImpactSword,
    ImpactMagic,
    ImpactFire,
    ImpactIce,
    ImpactSoft,

    // Defensores. ⚠ Se llaman «Tower» por la categoría del género —el jugador
    // dice «poner una torre»— pero lo que se pone es un DEFENSOR. La distinción
    // que importa es la de los tipos: `Tower` es lo que se defiende y `Defender`
    // lo que ataca (CLAUDE.md). Acá no hay tipos, solo un nombre de evento.
    TowerPlace,
    TowerUpgrade,
    TowerSell,
    TowerUnlock,
    TowerSelect,

    // Enemigos.
    EnemyHurt,
    EnemyDeath,
    EnemyBoss,
    EnemyLeak,

    // Interfaz.
    UiClick,
    UiHover,
    UiConfirm,
    UiCancel,
    UiError,
    UiBuy,
    UiCoins,
    UiReward,
    UiMenuOpen,
    UiMenuClose,

    // Ritmo de la corrida.
    WaveStart,
    WaveEnd,
    SkillReady,
    CooldownReady,
}

/// <summary>Las siete pistas de música.</summary>
/// <remarks>
/// El mismo trato que <see cref="SoundId"/>: el nombre del valor es la clave de
/// <c>Data/audio/audio.json</c>, y el archivo se decide ahí.
/// </remarks>
public enum MusicTrack
{
    /// <summary>Ninguna. Corta lo que esté sonando.</summary>
    None = -1,

    Menu = 0,
    Calm = 1,
    Normal = 2,
    Intense = 3,
    Boss = 4,
    Victory = 5,
    Defeat = 6,
}
