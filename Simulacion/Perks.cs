namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Los cuatro escalones del catálogo.
/// </summary>
/// <remarks>
/// <para><b>La línea que divide todo pasa entre Plata y Oro: Bronce y Plata son
/// cantidad, Oro y Único son comportamiento.</b> De ahí sale la regla de apilado
/// sin tener que pensarla perk por perk — "Ricochet dos veces" no quiere decir
/// nada, "+100 de daño dos veces" sí.</para>
///
/// <para>Y de ahí sale también por qué un Bronce puede decir <c>+100</c> y un
/// Único <c>+50</c> sin que esté mal: <b>no se comparan en el mismo eje</b>. El
/// Bronce escala un número que ya existía; el Único agrega algo que la clase no
/// hacía.</para>
/// </remarks>
public enum TierPerk
{
    Bronce = 0,
    Plata = 1,
    Oro = 2,
    Unico = 3,
}

/// <summary>Qué toca un perk numérico.</summary>
public enum EfectoPerk
{
    Ninguno = 0,
    Dano = 1,
    Cadencia = 2,
    Alcance = 3,

    /// <summary>Comportamiento: el disparo rebota a un segundo enemigo.</summary>
    Ricochet = 10,

    /// <summary>Comportamiento: el golpe daña también alrededor del objetivo.</summary>
    Splash = 11,

    /// <summary>Comportamiento: el objetivo queda más lento un rato.</summary>
    Ralentiza = 12,

    /// <summary>Comportamiento: cada tantos golpes, uno pega mucho más.</summary>
    GolpeCritico = 13,

    /// <summary>Comportamiento: envenena, daño por tick durante un rato.</summary>
    Veneno = 14,

    /// <summary>Comportamiento: cada tantos golpes, uno atraviesa la defensa.</summary>
    Penetrante = 15,

    // -------------------------------------------------------------------------
    //  Únicos — no caen ni se compran: salen de fusionar dos Oro con un plano
    // -------------------------------------------------------------------------

    /// <summary>Ricochet que además atraviesa.</summary>
    Perforacion = 20,

    /// <summary>Splash que además ralentiza.</summary>
    OndaDeFrio = 21,

    /// <summary>Crítico que además hace splash.</summary>
    IraDeDios = 22,

    /// <summary>Ricochet en cadena, sin tope de rebotes cercanos.</summary>
    Apocalipsis = 23,

    /// <summary>Veneno que además ralentiza.</summary>
    TerribleHambre = 24,

    /// <summary>Splash grande que pega dos veces.</summary>
    TormentaDivina = 25,
}

/// <summary>
/// Una definición del catálogo.
/// </summary>
/// <remarks>
/// ⚠ Los perks <b>no son de la unidad: son de la clase</b>. Toda unidad de esa
/// clase los tiene, la que ya estaba puesta y la que se ponga después. Un
/// defensor no guarda perks: calcula <c>base + nivel + perks + equipo</c> cada
/// vez, así que no hay estado duplicado ni desincronización posible.
/// </remarks>
public sealed class DefinicionPerk
{
    public required int Id { get; init; }

    public required string Nombre { get; init; }

    public required TierPerk Tier { get; init; }

    public required EfectoPerk Efecto { get; init; }

    /// <summary>Cuánto suma, en puntos porcentuales. Cero si es comportamiento.</summary>
    public int Magnitud { get; init; }

    /// <summary>
    /// Qué clases lo pueden equipar. Vacío = cualquiera.
    /// </summary>
    /// <remarks>
    /// <b>Que un Oro sirva para 2-3 clases es lo que hace escribible el
    /// catálogo.</b> Con todo lo interesante siendo exclusivo, la cuenta daba
    /// entre 50 y 90 definiciones; compartiendo Bronce y Plata entre las 9 y Oro
    /// entre dos o tres, quedan ~40.
    /// </remarks>
    public Clase[] Clases { get; init; } = [];

    /// <summary>
    /// Cuántas copias del mismo perk se pueden equipar en una clase.
    /// </summary>
    /// <remarks>
    /// Sale del tier por defecto —Bronce 3, Plata 2, Oro y Único 1— y solo se
    /// escribe a mano cuando un perk necesita otra cosa. La regla es la misma de
    /// arriba: <b>apilable si es un número, único si es un comportamiento</b>.
    /// </remarks>
    public int MaxApilado { get; init; }

    public bool SirveA(Clase clase) => Clases.Length == 0 || System.Array.IndexOf(Clases, clase) >= 0;
}

/// <summary>El catálogo, escrito a mano.</summary>
/// <remarks>
/// <para><b>Los perks no se generan</b>, a diferencia del equipo: se elige uno de
/// esta lista filtrando por tier y por las clases que el jugador tiene. Un perk
/// generado al azar sería un número sin nombre, y el Oro y el Único son
/// comportamientos — no hay forma de generar un comportamiento.</para>
///
/// <para>Los nombres de los Únicos salen de los 41 hechizos reales de
/// <c>Dat\Hechizos.dat</c> de Austral.</para>
/// </remarks>
public static class CatalogoPerks
{
    private static readonly DefinicionPerk[] _todos = Construir();

    public static IReadOnlyList<DefinicionPerk> Todos => _todos;

    public static DefinicionPerk Por(int id) => _todos[id];

    /// <summary>
    /// Elige un perk al azar del tier pedido, entre los que le sirven a alguna
    /// de las clases que el jugador tiene.
    /// </summary>
    /// <remarks>
    /// Filtrar por las clases que tiene, y no por todo el catálogo, es lo que
    /// evita el drop muerto: un Único de druida cuando no hay druida en el roster
    /// es un lugar de mochila gastado en nada.
    /// </remarks>
    public static DefinicionPerk? Tirar(TierPerk tier, IReadOnlyList<Clase> clasesDelJugador, Aleatorio rng)
    {
        var candidatos = new List<DefinicionPerk>();

        foreach (var p in _todos)
        {
            // ⚠ Los Únicos NO caen y NO se compran: se fusionan. Que aparezca uno
            // en una tirada de drop es un bug de datos.
            if (p.Tier == TierPerk.Unico || p.Tier != tier)
                continue;

            foreach (var c in clasesDelJugador)
            {
                if (p.SirveA(c))
                {
                    candidatos.Add(p);
                    break;
                }
            }
        }

        if (candidatos.Count == 0)
            return null;

        return candidatos[rng.Indice(candidatos.Count)];
    }

    private static int MaxPorTier(TierPerk tier) => tier switch
    {
        TierPerk.Bronce => 3,
        TierPerk.Plata => 2,
        _ => 1,
    };

    private static DefinicionPerk Nuevo(
        int id, string nombre, TierPerk tier, EfectoPerk efecto, int magnitud, params Clase[] clases)
        => new()
        {
            Id = id,
            Nombre = nombre,
            Tier = tier,
            Efecto = efecto,
            Magnitud = magnitud,
            Clases = clases,
            MaxApilado = MaxPorTier(tier),
        };

    private static DefinicionPerk[] Construir()
    {
        int id = 0;

        return
        [
            // --- Bronce: números chicos, para las 9 clases. Apilan hasta 3. ---
            Nuevo(id++, "Filo",           TierPerk.Bronce, EfectoPerk.Dano, 15),
            Nuevo(id++, "Pulso",          TierPerk.Bronce, EfectoPerk.Cadencia, 12),
            Nuevo(id++, "Vista",          TierPerk.Bronce, EfectoPerk.Alcance, 20),
            Nuevo(id++, "Temple",         TierPerk.Bronce, EfectoPerk.Dano, 10),
            Nuevo(id++, "Reflejos",       TierPerk.Bronce, EfectoPerk.Cadencia, 8),
            Nuevo(id++, "Puntería",       TierPerk.Bronce, EfectoPerk.Alcance, 15),
            Nuevo(id++, "Empuñadura",     TierPerk.Bronce, EfectoPerk.Dano, 12),
            Nuevo(id++, "Muñeca",         TierPerk.Bronce, EfectoPerk.Cadencia, 10),

            // --- Plata: más grandes. Apilan hasta 2. ---
            Nuevo(id++, "Furia",          TierPerk.Plata, EfectoPerk.Dano, 35),
            Nuevo(id++, "Celeridad",      TierPerk.Plata, EfectoPerk.Cadencia, 25),
            Nuevo(id++, "Halcón",         TierPerk.Plata, EfectoPerk.Alcance, 40),
            Nuevo(id++, "Saña",           TierPerk.Plata, EfectoPerk.Dano, 30),
            Nuevo(id++, "Frenesí",        TierPerk.Plata, EfectoPerk.Cadencia, 22),
            Nuevo(id++, "Ojo de Águila",  TierPerk.Plata, EfectoPerk.Alcance, 35),
            Nuevo(id++, "Ira",            TierPerk.Plata, EfectoPerk.Dano, 28),
            Nuevo(id++, "Vendaval",       TierPerk.Plata, EfectoPerk.Cadencia, 20),

            // --- Oro: comportamiento, compartido por 2-3 clases ---
            // Es la única fuente el jefe de oleada: por eso matarlo vale más que
            // sobrevivirlo.
            // ⚠ Magnitudes de la propuesta V2 (2026-08-27) en los cuatro Oro
            // retrabajados: Ricochet 55, Onda 35, Escarcha 25, Certero 125.
            Nuevo(id++, "Ricochet",       TierPerk.Oro, EfectoPerk.Ricochet, 55,
                Clase.Cazador, Clase.Asesino),

            Nuevo(id++, "Onda Expansiva", TierPerk.Oro, EfectoPerk.Splash, 35,
                Clase.Paladin, Clase.Guerrero, Clase.Mago),

            Nuevo(id++, "Escarcha",       TierPerk.Oro, EfectoPerk.Ralentiza, 25,
                Clase.Mago, Clase.Druida, Clase.Clerigo),

            Nuevo(id++, "Golpe Certero",  TierPerk.Oro, EfectoPerk.GolpeCritico, 125,
                Clase.Asesino, Clase.Ladron, Clase.Cazador),

            Nuevo(id++, "Descarga Eléctrica", TierPerk.Oro, EfectoPerk.Ricochet, 50,
                Clase.Mago, Clase.Bardo),

            Nuevo(id++, "Tormenta de Fuego", TierPerk.Oro, EfectoPerk.Splash, 55,
                Clase.Druida, Clase.Bardo),

            Nuevo(id++, "Envenenar",      TierPerk.Oro, EfectoPerk.Veneno, 40,
                Clase.Asesino, Clase.Druida),

            Nuevo(id++, "Torpeza",        TierPerk.Oro, EfectoPerk.Ralentiza, 30,
                Clase.Bardo, Clase.Ladron),

            Nuevo(id++, "Dardo Mágico",   TierPerk.Oro, EfectoPerk.Penetrante, 120,
                Clase.Mago, Clase.Clerigo),

            Nuevo(id++, "Misil Mágico",   TierPerk.Oro, EfectoPerk.Splash, 50,
                Clase.Mago, Clase.Paladin),

            // --- Único: NO caen, NO se compran. Salen de fusionar dos Oro con
            //     un plano comprado con la billetera, y ocupan UN SOLO slot.
            //
            // Uno por clase de las que tienen Oro asociado. El nombre sale de los
            // 41 hechizos reales de Dat\Hechizos.dat de Austral.
            Nuevo(id++, "Perforación",    TierPerk.Unico, EfectoPerk.Perforacion, 90,
                Clase.Cazador),

            Nuevo(id++, "Onda de Frío",   TierPerk.Unico, EfectoPerk.OndaDeFrio, 80,
                Clase.Mago),

            Nuevo(id++, "Ira de Dios",    TierPerk.Unico, EfectoPerk.IraDeDios, 170,
                Clase.Paladin),

            Nuevo(id++, "Apocalipsis",    TierPerk.Unico, EfectoPerk.Apocalipsis, 75,
                Clase.Asesino),

            Nuevo(id++, "Terrible Hambre de Igôr", TierPerk.Unico, EfectoPerk.TerribleHambre, 55,
                Clase.Druida),

            Nuevo(id++, "Tormenta Divina", TierPerk.Unico, EfectoPerk.TormentaDivina, 95,
                Clase.Clerigo),

            Nuevo(id++, "Ceguera",        TierPerk.Unico, EfectoPerk.Ralentiza, 60,
                Clase.Bardo),

            Nuevo(id++, "Fuerza",         TierPerk.Unico, EfectoPerk.GolpeCritico, 260,
                Clase.Guerrero),

            Nuevo(id++, "Invisibilidad",  TierPerk.Unico, EfectoPerk.Penetrante, 200,
                Clase.Ladron),
        ];
    }
}

/// <summary>
/// Una receta de fusión: dos Oro más un plano dan un Único.
/// </summary>
/// <remarks>
/// <para>El plano se compra <b>una vez, con la billetera, desde el menú
/// principal</b>, y queda aprendido para siempre en esa cuenta. Junto con la
/// billetera, es la única progresión permanente del proyecto.</para>
///
/// <para>La lista es <b>finita</b>, y esa es la condición que hace tolerable la
/// ventaja del veterano: tiene techo. Por eso no se agrega equipo permanente ni
/// nada que escale sin fin.</para>
/// </remarks>
public sealed record Plano(string Nombre, int OroA, int OroB, int UnicoResultante, bool Inicial);

public static class Planos
{
    /// <summary>
    /// Las recetas. <b>La lista es finita, y eso es la regla.</b>
    /// </summary>
    /// <remarks>
    /// <para>Los ids se calculan de los nombres al arrancar, en vez de escribirse
    /// a mano: con el catálogo creciendo, un número pegado quedaría apuntando a
    /// otro perk en cuanto alguien agregue uno en el medio, y el síntoma sería una
    /// fusión que produce algo que no es.</para>
    ///
    /// <para>Que la lista sea finita es la condición que hace tolerable la ventaja
    /// del veterano: tiene techo. Por eso no se agrega equipo permanente ni nada
    /// que escale sin fin.</para>
    /// </remarks>
    public static readonly Plano[] Todos = Construir();

    private static Plano[] Construir()
    {
        return
        [
            // ⚠ Una receta conocida de arranque, sin comprar nada.
            //
            // Sin al menos una, un jugador nuevo nunca ve un Único y la mecánica
            // de fusión —la que le da salida a los Oro repetidos— le resulta
            // invisible hasta que gasta oro en algo que no entiende para qué sirve.
            Receta("Perforación", "Ricochet", "Golpe Certero", inicial: true),

            Receta("Onda de Frío", "Escarcha", "Onda Expansiva", inicial: false),
            Receta("Ira de Dios", "Onda Expansiva", "Misil Mágico", inicial: false),
            Receta("Apocalipsis", "Ricochet", "Envenenar", inicial: false),
            Receta("Terrible Hambre de Igôr", "Envenenar", "Escarcha", inicial: false),
            Receta("Tormenta Divina", "Tormenta de Fuego", "Dardo Mágico", inicial: false),
            Receta("Ceguera", "Torpeza", "Descarga Eléctrica", inicial: false),
            Receta("Fuerza", "Golpe Certero", "Onda Expansiva", inicial: false),
            Receta("Invisibilidad", "Torpeza", "Golpe Certero", inicial: false),
        ];
    }

    private static Plano Receta(string resultante, string oroA, string oroB, bool inicial)
        => new(resultante, IdDe(oroA), IdDe(oroB), IdDe(resultante), inicial);

    private static int IdDe(string nombre)
    {
        foreach (var p in CatalogoPerks.Todos)
        {
            if (string.Equals(p.Nombre, nombre, System.StringComparison.Ordinal))
                return p.Id;
        }

        // -1 y no una excepción: el validador de datos lo reporta con nombre y
        // todo, que es mucho más útil que un crash al arrancar el juego.
        return -1;
    }

    /// <summary>
    /// Busca la receta que hacen esos dos Oro. El orden no importa.
    /// </summary>
    public static Plano? Buscar(int perkA, int perkB)
    {
        foreach (var p in Todos)
        {
            if ((p.OroA == perkA && p.OroB == perkB) ||
                (p.OroA == perkB && p.OroB == perkA))
            {
                return p;
            }
        }

        return null;
    }
}
