namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Las nueve clases de combate de Argentum Online.
///
/// <para>⚠ <b>No existe "arquero"</b>: el que tira flechas es el
/// <see cref="Cazador"/>. Es el error de nombre más fácil de cometer en este
/// proyecto porque "arquero" es lo que uno esperaría de un tower defense.</para>
///
/// <para><b>Las trabajadoras quedan afuera</b> —leñador, minero, pescador,
/// herrero, carpintero, sastre—: no pelean, y este juego es solo pelear.</para>
/// </summary>
public enum DefenderClass
{
    Mage = 0,
    Cleric = 1,
    Warrior = 2,
    Assassin = 3,
    Thief = 4,
    Bard = 5,
    Druid = 6,
    Paladin = 7,
    Hunter = 8,
}

/// <summary>
/// A quién le apunta un defensor cuando tiene varios en alcance.
/// </summary>
/// <remarks>
/// <para><b>Es la mecánica de más profundidad por menos complejidad que existe en
/// el género.</b> No toca una sola estadística: cambia tácticamente el resultado
/// con los mismos números, que es exactamente lo que hacía falta para que jugar
/// mejor se note. Bloons usa First / Last / Close / Strong desde hace años por lo
/// mismo.</para>
///
/// <para>Cuatro y no diez. Con diez modos la pantalla se vuelve un menú que hay
/// que estudiar, y la mitad no cambian nada en la práctica.</para>
///
/// <para>⚠ <b>Es del DEFENSOR, no de la clase.</b> Es la única cosa que no sigue
/// la regla «todo es de la clase»: dos paladines de la misma ficha quieren
/// targeting distinto según dónde estén parados —el de la entrada abre la oleada,
/// el de la Torre contiene filtraciones—, y esa decisión de colocación es la
/// mitad del valor de la mecánica. No es un stat: es memoria de esta unidad,
/// igual que <see cref="Defender.FocusStacks"/>.</para>
///
/// <para>⚠ El desempate es <b>siempre</b> por <see cref="Enemy.SpawnId"/> menor,
/// en los cuatro modos. Los empates son frecuentísimos —dos bichos iguales con un
/// tick de diferencia van juntos toda la ruta y tienen el mismo HP— así que sin
/// una regla explícita el objetivo lo decidiría el orden de la lista.</para>
/// </remarks>
public enum TargetingMode
{
    /// <summary>El más adelantado en la ruta. El de siempre, y el que viene por defecto.</summary>
    First = 0,

    /// <summary>El de más vida actual: tanques y jefes.</summary>
    Strongest = 1,

    /// <summary>El de menos vida actual: rematar.</summary>
    Weakest = 2,

    /// <summary>
    /// El que esté físicamente más cerca de la Torre.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>No es lo mismo que <see cref="First"/>.</b> Primero mide avance sobre
    /// la ruta; esto mide distancia a la Torre en el mapa. En un layout que
    /// serpentea, el tramo final puede pasar al lado de un tramo temprano, y ahí
    /// los dos modos eligen enemigos distintos. Es el modo para contener
    /// filtraciones.
    /// </remarks>
    NearestTower = 3,
}

/// <summary>
/// Un enemigo caminando la ruta.
/// </summary>
/// <remarks>
/// <para><b>Es una clase y no un struct a propósito.</b> La simulación guarda la
/// lista y la recorre siempre en orden de spawn, mutando en el lugar; con struct
/// cada acceso sería una copia y habría que escribirla de vuelta al array, que
/// es justo el tipo de código donde se cuela un bug de "modifiqué la copia".</para>
///
/// <para><b>No tiene <c>(x, y)</c>.</b> Tiene <see cref="DistanciaRecorrida"/>,
/// un solo número. Ver <see cref="Ruta"/>.</para>
/// </remarks>
public sealed class Enemy
{
    /// <summary>
    /// Orden de aparición dentro de la corrida. <b>Nunca se reusa y nunca se
    /// reordena.</b>
    ///
    /// <para>Es el desempate de todo: a quién le pega un defensor cuando dos
    /// enemigos están igual de adelantados, en qué orden se tiran los drops, en
    /// qué orden se resuelven las muertes. Si el orden cambia, la corrida da
    /// distinto y la revalidación acusa de trampa a quien no hizo nada.</para>
    /// </summary>
    public required int SpawnId { get; init; }

    /// <summary>Qué bicho es. Índice en el bestiario de <c>enemigos.json</c>.</summary>
    public required int TypeId { get; init; }

    /// <summary>
    /// Si es el jefe que cierra un área.
    /// </summary>
    /// <remarks>
    /// Va como bandera y no se deduce del HP —"si tiene más de 400 es jefe"— por
    /// dos motivos: el HP del jefe cambia con el sobrante del presupuesto, y un
    /// bicho común muy buffeado de un área tardía puede pasar cualquier umbral.
    /// Sin la bandera, ese bicho soltaría un perk de Oro, y <b>el jefe es la única
    /// fuente de Oro</b>.
    /// </remarks>
    public bool IsBoss { get; init; }

    /// <summary>
    /// Vida máxima, en <b>centésimas de punto</b> (<see cref="Ints.HundredthsPerPoint"/>).
    /// </summary>
    /// <remarks>
    /// ⚠ No es el número del bestiario: <c>enemigos.json</c> lo escribe en puntos
    /// enteros y la fase de spawn lo escala. Compararlo contra un HP de ahí da un
    /// factor 100 de diferencia.
    /// </remarks>
    public required int MaxHp { get; init; }

    /// <summary>Vida actual, en centésimas de punto.</summary>
    public int Hp { get; set; }

    /// <summary>Milésimas de celda por tick.</summary>
    public required int Speed { get; init; }

    /// <summary>Daño que le hace a la Torre por golpe, en centésimas de punto.</summary>
    public required int Damage { get; init; }

    /// <summary>Ticks entre golpe y golpe a la Torre.</summary>
    public required int CadenceTicks { get; init; }

    /// <summary>Oro que suelta al morir.</summary>
    public required int Gold { get; init; }

    /// <summary>Cuánto avanzó por la ruta, en milésimas de celda.</summary>
    public long DistanceTravelled { get; set; }

    /// <summary>
    /// Ya llegó al final y le está pegando a la Torre. Deja de moverse.
    /// </summary>
    public bool AttackingTower { get; set; }

    /// <summary>Ticks que faltan para su próximo golpe a la Torre.</summary>
    public int AttackCooldown { get; set; }

    /// <summary>
    /// Oro extra que va a soltar, en porcentaje. Lo pone la habilidad Codicia.
    /// </summary>
    /// <remarks>
    /// Se marca al golpear y se cobra en la fase de muertes, cuando se sabe si el
    /// golpe mató. Cobrarlo al golpear daría oro por pegarle a algo que sobrevive.
    /// </remarks>
    public int ExtraGoldPercent { get; set; }

    /// <summary>Cuándo vence la marca de Codicia. La limpia la fase 3.</summary>
    /// <remarks>
    /// Antes la marca duraba para siempre (propuesta V2): un solo golpe de un
    /// arma de Codicia convertía a un tanque en alcancía aunque lo terminara
    /// matando otro, minutos después.
    /// </remarks>
    public int ExtraGoldExpiryTick { get; set; }

    /// <summary>
    /// Modificadores que le tocaron al spawnear, en puntos porcentuales.
    ///
    /// <para>Los paga el presupuesto de la oleada, y <b>tienen que verse en
    /// pantalla</b>: un lobo blindado que se ve igual que un lobo normal es una
    /// mecánica invisible.</para>
    /// </summary>
    public int ExtraHealthPerk { get; set; }

    public int ExtraSpeedPerk { get; set; }

    /// <summary>Ralentización activa, en puntos porcentuales POSITIVOS (0-60).</summary>
    /// <remarks>
    /// ⚠ Separada del buff de velocidad y TEMPORAL (propuesta V2): antes el slow
    /// restaba directo en <see cref="PerkVelocidadExtra"/> y era permanente —
    /// dos fuentes dejaban a toda la oleada al 20% de velocidad para siempre.
    /// No apila: prevalece la potencia más fuerte, y cada aplicación refresca
    /// los 90 ticks. La vence la fase 3.
    /// </remarks>
    public int SlowPercent { get; set; }

    public int SlowExpiryTick { get; set; }

    // -------------------------------------------------------------------------
    //  Estados de Estados_v2
    // -------------------------------------------------------------------------
    //
    //  ⚠ TODOS siguen las mismas dos reglas, y por eso no hay una lista de
    //  excepciones que mantener:
    //
    //    1. NO APILAN. Gana la potencia más fuerte y cada aplicación refresca la
    //       duración. Sumarlas hace que la estrategia óptima sea "poné diez del
    //       mismo", que es lo contrario de lo que un roguelike de composición
    //       quiere premiar.
    //    2. LOS APLICA SOLO EL DAÑO DIRECTO. Ver DamageSource.
    //
    //  Los DoT son la excepción a la primera, y con su propia regla: conservan
    //  Potencia × Duración en vez de pisarse. Ver DotState.

    /// <summary>Vulnerabilidad activa: recibe este % extra de daño DIRECTO.</summary>
    public int VulnerablePercent { get; set; }

    public int VulnerableExpiryTick { get; set; }

    /// <summary>Debilidad: le hace este % MENOS de daño a la Torre si llega.</summary>
    public int WeakenPercent { get; set; }

    public int WeakenExpiryTick { get; set; }

    /// <summary>Marca de caza: las OTRAS clases le pegan este % más fuerte.</summary>
    public int MarkPercent { get; set; }

    public int MarkExpiryTick { get; set; }

    /// <summary>
    /// Qué clase puso la marca. <b>Esa clase no cobra el bonus.</b>
    /// </summary>
    /// <remarks>
    /// Sin esta exclusión, Marca de Caza sería un +10% propio disfrazado de
    /// soporte, y dos cazadores serían mejor que un cazador y otra cosa — que es
    /// justo la composición que el perk existe para romper.
    /// </remarks>
    public DefenderClass MarkOwner { get; set; }

    /// <summary>Hasta qué tick está inmovilizado. No se mueve mientras dure.</summary>
    public int RootedUntilTick { get; set; }

    /// <summary>
    /// Hasta qué tick es inmune a que lo vuelvan a enraizar.
    /// </summary>
    /// <remarks>
    /// ⚠ El período refractario es lo que separa "control duro" de "perma-stun".
    /// Sin él, tres druidas alcanzan para que nada camine nunca más, y la
    /// dificultad deja de existir. La idea es del rework de aturdimiento de
    /// Infinitode 2: inmunidad al salir, más un tope de veces por enemigo.
    /// </remarks>
    public int RootImmuneUntilTick { get; set; }

    /// <summary>Cuántas veces lo enraizaron. Tiene tope.</summary>
    public int RootCount { get; set; }

    /// <summary>Cuántas veces lo empujaron hacia atrás. Tiene tope.</summary>
    public int PushCount { get; set; }

    /// <summary>
    /// Los tres daños por tiempo. Índice por <see cref="DotKind"/>.
    /// </summary>
    /// <remarks>
    /// Array de tres y no tres campos sueltos para que la fase de estados los
    /// cobre en un <c>for</c>: con campos sueltos, agregar un DoT nuevo obliga a
    /// tocar el bucle, y ese es el olvido que deja un efecto que se aplica y
    /// nunca cobra.
    /// </remarks>
    public DotState[] Dots { get; } = new DotState[3];

    /// <summary>
    /// Tope en ORO del extra que puede soltar por la marca, o 0 si no tiene.
    /// </summary>
    /// <remarks>
    /// Codicia —habilidad de arma legendaria— no lleva tope: es el premio de una
    /// corrida. Saqueo —perk de Oro— sí, y además cuenta contra un tope por
    /// oleada. Sin los dos topes, una build de economía se vuelve obligatoria.
    /// </remarks>
    public int ExtraGoldMax { get; set; }

    /// <summary>
    /// Tope en ORO de lo que esta marca puede aportar en TODA la oleada, o 0.
    /// </summary>
    /// <remarks>
    /// Es el segundo techo de Saqueo, y hace falta además del de por enemigo: sin
    /// él, una oleada de sesenta bichos marcados paga sesenta veces el tope
    /// individual y la build de economía vuelve a ser obligatoria.
    /// </remarks>
    public int ExtraGoldWaveCap { get; set; }

    // -------------------------------------------------------------------------
    //  Los tres rasgos: enfurecer, impulsivo y líder
    // -------------------------------------------------------------------------
    //
    //  ⚠ NINGUNO DA DAÑO EXTRA, y ninguno cura, teletransporta, se vuelve inmune
    //  ni destruye defensores. Los tres hacen lo mismo: mueven la VELOCIDAD, que
    //  es el único eje que hace más urgente un tramo sin volverlo impredecible.
    //  Un enemigo que además pega más fuerte al enfurecer convierte una
    //  filtración en una derrota instantánea que el jugador no tenía cómo prever.

    /// <summary>
    /// Rasgo de tropa: al bajar del umbral de vida acelera, <b>una sola vez</b>.
    /// </summary>
    /// <remarks>
    /// Lo decide el armador de la oleada y se paga del presupuesto, igual que los
    /// buffs de vida y velocidad. Tiene varias respuestas —Oportunista, concentrar
    /// fuego, slow, burst, defensa cerca de la Torre— así que no es un enemigo
    /// «lock-and-key»: la recomendación de Defender's Quest es justamente evitar
    /// los bichos que admiten una única respuesta correcta.
    /// </remarks>
    public bool Impulsive { get; init; }

    /// <summary>
    /// Rasgo de tropa: le da un aura de velocidad a los que van cerca por ruta.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>El aura muere con él.</b> Eso es lo que lo vuelve un objetivo
    /// prioritario sin agregar una sola resistencia ni inmunidad: matarlo primero
    /// frena a todo el grupo.
    /// </remarks>
    public bool Leader { get; init; }

    /// <summary>Ya disparó el acelerón. Vale para impulsivo y para el enfurecer del jefe.</summary>
    public bool SpeedTraitFired { get; set; }

    /// <summary>
    /// Acelerón temporal activo, en puntos porcentuales. Lo vence la fase 3.
    /// </summary>
    /// <remarks>
    /// Impulsivo lo deja permanente —<see cref="TraitSpeedExpiryTick"/> en
    /// <c>int.MaxValue</c>—; el enfurecer del jefe lo deja por unos ticks. Es el
    /// mismo campo porque son el mismo efecto con distinta duración, y dos campos
    /// separados serían dos lugares donde olvidarse de sumar uno.
    /// </remarks>
    public int TraitSpeedPercent { get; set; }

    public int TraitSpeedExpiryTick { get; set; }

    /// <summary>
    /// Aura de velocidad recibida de un Líder cercano, en puntos porcentuales.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>No apila</b>, igual que todos los estados del juego: gana la potencia
    /// más fuerte. Sin la regla, tres líderes juntos son un +30% que ningún número
    /// del catálogo anticipa. La recalcula la fase 3 desde cero en cada tick, así
    /// que no hace falta vencerla: si el líder murió, el aura ya no está.
    /// </remarks>
    public int LeaderAuraPercent { get; set; }

    public bool Alive => Hp > 0;

    /// <summary>
    /// La velocidad con la que camina este tick.
    /// </summary>
    /// <remarks>
    /// ⚠ Todos los aportes se suman <b>antes</b> de multiplicar, por
    /// <see cref="Ints.WithPercents"/>: aplicarlos de a uno perdería una fracción
    /// en cada división y dos enemigos con los mismos efectos en distinto orden
    /// caminarían distinto.
    /// </remarks>
    public int EffectiveSpeed => Ints.WithPercents(
        Speed, ExtraSpeedPerk + TraitSpeedPercent + LeaderAuraPercent - SlowPercent);

    /// <summary>¿Está inmovilizado ahora?</summary>
    public bool Rooted(int tick) => tick < RootedUntilTick;
}

/// <summary>
/// Un defensor puesto en el mapa.
/// </summary>
/// <remarks>
/// ⚠ <b>Un defensor NO es la Torre.</b> La Torre es lo que se defiende. Esta es
/// la unidad que pega. El defensor no tiene stats propios: los saca de su
/// <see cref="Clase"/>, que es donde viven el nivel, los perks y el equipo. Dos
/// cazadores puestos en celdas distintas pegan exactamente igual.
/// </remarks>
public sealed class Defender
{
    public required int PlacementId { get; init; }

    public required DefenderClass DefenderClass { get; init; }

    /// <summary>
    /// Dónde está parado. <b>Se puede mover</b> con el reloj congelado.
    /// </summary>
    /// <remarks>
    /// ⚠ Era <c>init</c>. Dejó de serlo para que el jugador pueda <b>arrastrar la
    /// ficha por el tablero</b> durante la preparación: antes acomodar una
    /// colocación mal puesta era vender y volver a comprar, y a 60% de devolución
    /// convenía dejarla mal. La clase, en cambio, sigue siendo <c>init</c>: mover
    /// una ficha no la convierte en otra cosa.
    /// </remarks>
    public required Cell Cell { get; set; }

    /// <summary>Ticks que faltan para poder disparar de nuevo.</summary>
    public int RemainingCooldown { get; set; }

    /// <summary>
    /// Cuántos enemigos mató en la oleada actual. Lo usa la habilidad Sed.
    /// </summary>
    /// <remarks>
    /// Se reinicia al terminar cada oleada. Sin eso, en una corrida de 100
    /// oleadas el contador crecería sin freno y el bonus sería infinito: el techo
    /// real tiene que ser cuánto se puede matar de una vez, no cuánto se lleva
    /// matado.
    /// </remarks>
    public int KillsInWave { get; set; }

    /// <summary>Golpes que tiró desde que existe. Lo usan los críticos.</summary>
    /// <remarks>
    /// ⚠ Contador POR DEFENSOR (propuesta V2): el crítico era <c>Tick % 7</c>
    /// global, y con eso criticaban todos juntos en el mismo tick — y un
    /// defensor cuya cadencia fuera múltiplo de 7 criticaba siempre o nunca
    /// según la fase. Cada 7.º ataque DE ESTE defensor es el mismo promedio,
    /// sin la sincronización.
    /// </remarks>
    public int HitCounter { get; set; }

    // -------------------------------------------------------------------------
    //  Estado propio del defensor
    // -------------------------------------------------------------------------
    //
    //  ⚠ Nada de esto son STATS. Los stats siguen viviendo en la clase y se
    //  recalculan cada vez (`base + nivel + perks + equipo`); esto es memoria de
    //  lo que ESTA unidad viene haciendo, que es otra cosa. Dos cazadores con los
    //  mismos perks pegan igual; uno puede llevar cinco golpes seguidos al mismo
    //  bicho y el otro no.
    //
    //  Y va por defensor y no por tick global a propósito: con `Tick % N`,
    //  criticaban todos juntos y un defensor con cadencia múltiplo de N criticaba
    //  siempre o nunca según la fase en que se lo puso.

    /// <summary>A qué enemigo le viene pegando seguido. Lo usa Concentración.</summary>
    public int FocusTargetSpawnId { get; set; } = -1;

    /// <summary>Cuántos golpes seguidos lleva al mismo objetivo. Tiene techo.</summary>
    public int FocusStacks { get; set; }

    /// <summary>El tick del último golpe. La concentración se pierde si pasa mucho.</summary>
    public int LastHitTick { get; set; } = int.MinValue;

    /// <summary>Ticks seguidos sin nadie en alcance. Lo usa Preparación.</summary>
    public int IdleTicks { get; set; }

    /// <summary>Tiene el próximo golpe cargado. Una sola carga guardada.</summary>
    public bool Charged { get; set; }

    /// <summary>Buff de cadencia recibido de un aura aliada, en puntos porcentuales.</summary>
    /// <remarks>
    /// ⚠ <b>No apila</b>: se conserva el más fuerte y se refresca la duración.
    /// Tres bardos siguen dando +10%, no +30%. Sin la regla, spamear soportes es
    /// la mejor estrategia universal y todas las demás composiciones sobran.
    /// </remarks>
    public int CadenceBuffPercent { get; set; }

    public int CadenceBuffExpiryTick { get; set; }

    /// <summary>
    /// A quién le apunta cuando tiene varios en alcance.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es lo único del defensor que el jugador elige y que no es de la
    /// clase.</b> Y tiene que serlo: dos paladines de la misma ficha quieren
    /// targeting distinto según dónde estén parados —el de la entrada abre la
    /// oleada, el de la Torre contiene filtraciones—, y esa decisión de colocación
    /// es la mitad del valor de la mecánica.</para>
    ///
    /// <para>Arranca en <see cref="TargetingMode.First"/>, que es lo que hacía el
    /// juego antes de que el modo existiera: un defensor puesto y nunca tocado se
    /// comporta exactamente igual que siempre.</para>
    /// </remarks>
    public TargetingMode Targeting { get; set; } = TargetingMode.First;

    /// <summary>
    /// El tick en que se lo puso. Va al log de acciones y sirve para la
    /// devolución al venderlo.
    /// </summary>
    public required int PlacementTick { get; init; }
}

/// <summary>
/// Lo que se defiende. Si su HP llega a cero, se terminó la corrida.
/// </summary>
public sealed class Tower
{
    /// <summary>
    /// Vida máxima, en <b>centésimas de punto</b>, igual que la de los enemigos.
    /// </summary>
    /// <remarks>
    /// <c>curvas.json</c> dice 1000 y acá adentro son 100.000. Mostrarlo crudo en
    /// el HUD es el error obvio; para eso está <c>Loc.Points</c>.
    /// </remarks>
    public required int MaxHp { get; init; }

    /// <summary>Ticks que le faltan para volver a pegar.</summary>
    /// <remarks>
    /// ⚠ La Torre <b>se defiende</b>. No es un blanco quieto: un enemigo que
    /// llega y se queda mordiéndola termina muriendo solo, aunque el jugador no
    /// tenga a nadie cerca. Sin esto, un bicho fuera del alcance de todos frena
    /// la oleada para siempre y la corrida se vuelve una derrota en cámara
    /// lenta. Pega parejo y despacio: es una red de seguridad, no un defensor.
    /// </remarks>
    public int AttackCooldown { get; set; }

    public int Hp { get; set; }

    public required Cell Cell { get; init; }

    public bool Standing => Hp > 0;

    public void Receive(int dano)
    {
        Hp -= dano;
        if (Hp < 0) Hp = 0;
    }
}
