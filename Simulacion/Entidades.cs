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
public enum Clase
{
    Mago = 0,
    Clerigo = 1,
    Guerrero = 2,
    Asesino = 3,
    Ladron = 4,
    Bardo = 5,
    Druida = 6,
    Paladin = 7,
    Cazador = 8,
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
public sealed class Enemigo
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
    public required int IdSpawn { get; init; }

    /// <summary>Qué bicho es. Índice en el bestiario de <c>enemigos.json</c>.</summary>
    public required int TipoId { get; init; }

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
    public bool EsJefe { get; init; }

    public required int HpMaximo { get; init; }

    public int Hp { get; set; }

    /// <summary>Milésimas de celda por tick.</summary>
    public required int Velocidad { get; init; }

    /// <summary>Daño que le hace a la Torre por golpe.</summary>
    public required int Dano { get; init; }

    /// <summary>Ticks entre golpe y golpe a la Torre.</summary>
    public required int CadenciaTicks { get; init; }

    /// <summary>Oro que suelta al morir.</summary>
    public required int Oro { get; init; }

    /// <summary>Cuánto avanzó por la ruta, en milésimas de celda.</summary>
    public long DistanciaRecorrida { get; set; }

    /// <summary>
    /// Ya llegó al final y le está pegando a la Torre. Deja de moverse.
    /// </summary>
    public bool AtacandoTorre { get; set; }

    /// <summary>Ticks que faltan para su próximo golpe a la Torre.</summary>
    public int CooldownAtaque { get; set; }

    /// <summary>
    /// Oro extra que va a soltar, en porcentaje. Lo pone la habilidad Codicia.
    /// </summary>
    /// <remarks>
    /// Se marca al golpear y se cobra en la fase de muertes, cuando se sabe si el
    /// golpe mató. Cobrarlo al golpear daría oro por pegarle a algo que sobrevive.
    /// </remarks>
    public int OroExtraPorciento { get; set; }

    /// <summary>
    /// Modificadores que le tocaron al spawnear, en puntos porcentuales.
    ///
    /// <para>Los paga el presupuesto de la oleada, y <b>tienen que verse en
    /// pantalla</b>: un lobo blindado que se ve igual que un lobo normal es una
    /// mecánica invisible.</para>
    /// </summary>
    public int PerkVidaExtra { get; set; }

    public int PerkVelocidadExtra { get; set; }

    public bool Vivo => Hp > 0;

    /// <summary>Velocidad ya con los modificadores aplicados.</summary>
    public int VelocidadEfectiva => Enteros.ConPorcentajes(Velocidad, PerkVelocidadExtra);
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
public sealed class Defensor
{
    public required int IdColocacion { get; init; }

    public required Clase Clase { get; init; }

    public required Celda Celda { get; init; }

    /// <summary>Ticks que faltan para poder disparar de nuevo.</summary>
    public int CooldownRestante { get; set; }

    /// <summary>
    /// Cuántos enemigos mató en la oleada actual. Lo usa la habilidad Sed.
    /// </summary>
    /// <remarks>
    /// Se reinicia al terminar cada oleada. Sin eso, en una corrida de 100
    /// oleadas el contador crecería sin freno y el bonus sería infinito: el techo
    /// real tiene que ser cuánto se puede matar de una vez, no cuánto se lleva
    /// matado.
    /// </remarks>
    public int MuertesEnLaOleada { get; set; }

    /// <summary>
    /// El tick en que se lo puso. Va al log de acciones y sirve para la
    /// devolución del 50% al venderlo.
    /// </summary>
    public required int TickColocacion { get; init; }
}

/// <summary>
/// Lo que se defiende. Si su HP llega a cero, se terminó la corrida.
/// </summary>
public sealed class Torre
{
    public required int HpMaximo { get; init; }

    /// <summary>Ticks que le faltan para volver a pegar.</summary>
    /// <remarks>
    /// ⚠ La Torre <b>se defiende</b>. No es un blanco quieto: un enemigo que
    /// llega y se queda mordiéndola termina muriendo solo, aunque el jugador no
    /// tenga a nadie cerca. Sin esto, un bicho fuera del alcance de todos frena
    /// la oleada para siempre y la corrida se vuelve una derrota en cámara
    /// lenta. Pega parejo y despacio: es una red de seguridad, no un defensor.
    /// </remarks>
    public int CooldownAtaque { get; set; }

    public int Hp { get; set; }

    public required Celda Celda { get; init; }

    public bool EnPie => Hp > 0;

    public void Recibir(int dano)
    {
        Hp -= dano;
        if (Hp < 0) Hp = 0;
    }
}
