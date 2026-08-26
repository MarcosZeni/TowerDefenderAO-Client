namespace TowerOfArgentum.Simulacion;

/// <summary>Lo que se compró para una oleada, en el orden en que entra.</summary>
public sealed class OleadaArmada
{
    public required int Numero { get; init; }

    /// <summary>Los tipos, en orden de entrada. El jefe va último.</summary>
    public required List<CompraNpc> Npcs { get; init; }

    /// <summary>Ticks entre un spawn y el siguiente.</summary>
    public required int IntervaloSpawnTicks { get; init; }

    /// <summary>Oro que se lleva el jugador por limpiarla.</summary>
    public required int BonusOro { get; init; }
}

/// <summary>Un NPC comprado, con los perks que le tocaron.</summary>
public readonly record struct CompraNpc(int TipoId, int PerkVida, int PerkVelocidad, bool EsJefe);

/// <summary>
/// La receta de una oleada: cuánto se puede gastar y en qué.
/// </summary>
/// <remarks>
/// Provisional en <see cref="Balance"/>; la versión final sale de
/// <c>oleadas.json</c> por área (OLEADAS.md).
/// </remarks>
public sealed class RecetaOleada
{
    public required int Numero { get; init; }

    /// <summary>La palanca principal de dificultad.</summary>
    public required int Presupuesto { get; init; }

    /// <summary>Tipos que pueden salir, con su peso entero.</summary>
    public required (int TipoId, int Peso)[] Permitidos { get; init; }

    public int JefeTipoId { get; init; } = -1;

    /// <summary>Porcentaje entero: cuántos NPCs salen con perk.</summary>
    public int ChancePerkNpc { get; init; }

    public int IntervaloSpawnTicks { get; init; } = 30;

    public int BonusOro { get; init; }
}

/// <summary>
/// Arma una oleada gastando su presupuesto.
/// </summary>
/// <remarks>
/// <para><b>Una oleada no es una lista de bichos escrita a mano.</b> Es un
/// presupuesto y una lista de permitidos, y el RNG sembrado con la seed del
/// servidor lo gasta comprando. De ahí salen tres cosas:</para>
///
/// <list type="bullet">
/// <item>La seed hace trabajo real: dos corridas tienen la misma dificultad y
/// distinta composición.</item>
/// <item>El ranking sigue comparable, porque el presupuesto es idéntico para
/// todos. <b>Rerollear la seed no sirve</b>: no hay una tirada floja que
/// buscar.</item>
/// <item>El balance se toca en un número, no reescribiendo veinte listas.</item>
/// </list>
/// </remarks>
public static class ArmadorOleadas
{
    /// <summary>
    /// Lo que cuesta ponerle un perk a un NPC, en puntos de presupuesto.
    /// </summary>
    /// <remarks>
    /// <b>Que el perk se pague del mismo presupuesto es lo que impide que
    /// buffear descontrole la dificultad</b>: una oleada con perks trae MENOS
    /// bichos, no los mismos más fuertes. Así el presupuesto sigue siendo el
    /// único número que hay que mirar para saber cuán dura es una oleada.
    /// </remarks>
    private const int CostoPerkVida = 12;

    private const int CostoPerkVelocidad = 10;

    /// <summary>
    /// Tope de NPCs por oleada.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe porque una oleada de mil bichos no es difícil: es
    /// larga.</b> El presupuesto crece con el cuadrado de la oleada —de 59 en la
    /// primera a 90.050 en la última, unas 1.500 veces— y el costo del bicho más
    /// caro del tramo sube apenas unas pocas. Sin tope, la oleada 55 compraba
    /// más de mil enemigos y tardaba media hora en desfilar, con la Torre
    /// intacta todo el tiempo.</para>
    ///
    /// <para>Medido con el corredor headless: las corridas se quedaban clavadas
    /// en la oleada 55 contra el techo de 700.000 ticks.</para>
    /// </remarks>
    private const int MaxNpcsPorOleada = 60;

    /// <summary>
    /// Cuánto suma cada escalón de perk de vida, en puntos porcentuales.
    /// </summary>
    /// <remarks>
    /// Los escalones se acumulan sobre el mismo NPC: es la válvula por donde
    /// sale el presupuesto que no entra en NPCs nuevos. Una oleada tardía trae
    /// <b>los mismos 60 bichos, pero muy buffeados</b>, en vez de una marea.
    /// </remarks>
    private const int EscalonPerkVida = 50;

    private const int EscalonPerkVelocidad = 10;

    /// <summary>Techo del perk de velocidad: un bicho que vuela no se puede matar.</summary>
    private const int MaxPerkVelocidad = 120;

    public static OleadaArmada Armar(RecetaOleada receta, Aleatorio rng, DatosBalance datos)
    {
        var npcs = new List<CompraNpc>();
        int restante = receta.Presupuesto;

        // Regla 3: el jefe se descuenta ANTES de comprar el resto, y aparece
        // último. Al revés, una oleada con jefe traería la misma cantidad de
        // bichos más el jefe, y sería un salto de dificultad y no un cambio de
        // forma.
        bool hayJefe = receta.JefeTipoId >= 0;
        if (hayJefe)
            restante -= Costo(receta.JefeTipoId, datos);

        int masBarato = int.MaxValue;
        foreach (var (tipoId, _) in receta.Permitidos)
            masBarato = System.Math.Min(masBarato, Costo(tipoId, datos));

        // Regla 1: se gasta hasta que no alcance para el más barato — ahora con
        // el tope de cantidad. Lo que sobre se convierte en perks más abajo.
        while (restante >= masBarato && npcs.Count < MaxNpcsPorOleada)
        {
            int tipoId = ElegirPorPeso(receta.Permitidos, rng);
            int costo = Costo(tipoId, datos);

            if (costo > restante)
            {
                // Salió uno que no entra en lo que queda. Se descarta la compra
                // y se sigue: el bucle corta solo cuando ni el más barato entra.
                // Elegir "el más caro que entre" sería más eficiente y menos
                // variado — todas las oleadas terminarían igual.
                continue;
            }

            restante -= costo;

            // Regla 4: cada NPC tira contra chancePerkNpc, y el perk se paga del
            // mismo presupuesto. Si no alcanza, no lo recibe.
            int perkVida = 0;
            int perkVelocidad = 0;

            if (rng.Ocurre(receta.ChancePerkNpc))
            {
                if (rng.Ocurre(50) && restante >= CostoPerkVida)
                {
                    perkVida = 50;
                    restante -= CostoPerkVida;
                }
                else if (restante >= CostoPerkVelocidad)
                {
                    perkVelocidad = 30;
                    restante -= CostoPerkVelocidad;
                }
            }

            npcs.Add(new CompraNpc(tipoId, perkVida, perkVelocidad, EsJefe: false));
        }

        // El presupuesto que no entró en NPCs se convierte en perks sobre los ya
        // comprados. Es la misma regla de siempre —el perk se paga del
        // presupuesto— aplicada al sobrante: en las oleadas tardías eso es casi
        // todo el presupuesto, y de ahí sale la dificultad.
        restante = RepartirSobranteEnPerks(npcs, restante, rng);

        if (hayJefe)
        {
            // El jefe se lleva lo que quedó sin gastar, como vida extra. Si no,
            // el sobrante se tiraría y una oleada con jefe sería más fácil que la
            // anterior sin él.
            int vidaExtraJefe = restante / 4;
            npcs.Add(new CompraNpc(receta.JefeTipoId, vidaExtraJefe, 0, EsJefe: true));
        }

        return new OleadaArmada
        {
            Numero = receta.Numero,
            Npcs = npcs,
            IntervaloSpawnTicks = receta.IntervaloSpawnTicks,
            BonusOro = receta.BonusOro,
        };
    }

    /// <summary>
    /// Gasta el presupuesto que sobró subiéndoles perks a los NPCs comprados.
    /// </summary>
    /// <remarks>
    /// <para>Reparte en pasadas sobre la lista, <b>en orden de compra</b>, en vez
    /// de volcárselo todo al primero. Así el buff queda parejo y la oleada no se
    /// convierte en "un monstruo y cincuenta ratas".</para>
    ///
    /// <para>Las pasadas alternan vida y velocidad, y la velocidad tiene techo:
    /// un enemigo lo bastante rápido cruza el mapa entre dos disparos y no hay
    /// defensa que lo toque. La vida no necesita techo — más vida siempre es
    /// solo más tiempo bajo fuego.</para>
    /// </remarks>
    private static int RepartirSobranteEnPerks(List<CompraNpc> npcs, int restante, Aleatorio rng)
    {
        if (npcs.Count == 0)
            return restante;

        // El jefe todavía no está en la lista, así que esto solo toca a la tropa.
        while (restante >= CostoPerkVida)
        {
            bool gastoAlgo = false;

            for (int i = 0; i < npcs.Count && restante >= CostoPerkVida; i++)
            {
                var npc = npcs[i];

                // La velocidad sale a veces y con techo; la vida es el destino
                // por defecto del presupuesto.
                bool aVelocidad =
                    npc.PerkVelocidad < MaxPerkVelocidad &&
                    restante >= CostoPerkVelocidad &&
                    rng.Ocurre(25);

                npcs[i] = aVelocidad
                    ? npc with { PerkVelocidad = npc.PerkVelocidad + EscalonPerkVelocidad }
                    : npc with { PerkVida = npc.PerkVida + EscalonPerkVida };

                restante -= aVelocidad ? CostoPerkVelocidad : CostoPerkVida;
                gastoAlgo = true;
            }

            if (!gastoAlgo)
                break;
        }

        return restante;
    }

    /// <summary>
    /// Elige un tipo con pesos <b>enteros</b>.
    /// </summary>
    /// <remarks>
    /// Enteros y no probabilidades en coma flotante, por la misma razón que todo
    /// lo demás: un peso de <c>0.3</c> no se representa exacto en binario, y dos
    /// máquinas podrían caer de lados distintos del corte.
    /// </remarks>
    private static int ElegirPorPeso((int TipoId, int Peso)[] permitidos, Aleatorio rng)
    {
        int total = 0;
        foreach (var (_, peso) in permitidos)
            total += peso;

        if (total <= 0)
            return permitidos[0].TipoId;

        int tirada = rng.Rango(0, total);

        foreach (var (tipoId, peso) in permitidos)
        {
            tirada -= peso;
            if (tirada < 0)
                return tipoId;
        }

        return permitidos[^1].TipoId;
    }

    /// <summary>
    /// Cuánto cuesta un NPC, en puntos de presupuesto.
    /// </summary>
    /// <remarks>
    /// <para>Derivado de los stats en vez de escrito a mano por criatura: si la
    /// fórmula es buena, <b>agregar un bicho nuevo no obliga a rebalancear
    /// nada</b>. Con un número a mano por criatura, cada alta del bestiario es
    /// una decisión de balance más.</para>
    ///
    /// <para>El HP pesa más que el daño porque en un tower defense aguantar es
    /// lo que cuesta matar; el daño solo importa si llega a la Torre.</para>
    /// </remarks>
    private static int Costo(int tipoId, DatosBalance datos)
    {
        var e = datos.Por(tipoId);

        if (e is null)
            return int.MaxValue;   // no existe: que no lo compre nadie

        // La velocidad entra porque un bicho rápido pasa menos tiempo bajo fuego,
        // y eso lo hace más difícil sin tocarle una estadística de combate.
        return (e.Hp / 4) + (e.DanoMax / 2) + (e.Velocidad / 8);
    }
}

/// <summary>
/// Una fila del avance de la próxima oleada, para la pantalla de preparación.
/// </summary>
/// <remarks>
/// Agrupa por tipo: al jugador le sirve «12 hormigas, 3 con perk», no doce
/// renglones iguales. <b>No es entrada de la simulación</b> — se deriva de la
/// oleada ya armada y no vuelve al tick.
/// </remarks>
public sealed class AvanceDeOleada
{
    public required int TipoId { get; init; }

    public required string Nombre { get; init; }

    /// <summary>La clave del sprite, para dibujarlo al lado del nombre.</summary>
    public required string Grafico { get; init; }

    public required bool EsJefe { get; init; }

    public int Cuantos { get; set; }

    /// <summary>Cuántos de esos vienen buffeados. Tiene que verse.</summary>
    public int ConPerk { get; set; }
}
