namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Una clase contratada: su nivel, sus slots de perk y su equipo.
/// </summary>
/// <remarks>
/// <b>Todo lo que afecta a un defensor vive acá, no en el defensor.</b> Un
/// cazador puesto en la oleada 3 y otro puesto en la 40 pegan exactamente igual,
/// porque los dos leen de esta misma ficha. Esa regla es la que hace que no
/// existan unidades "viejas" que se quedan atrás, y también la que hace la
/// revalidación trivial: cero estado duplicado.
/// </remarks>
public sealed class ClaseEnRoster
{
    public required Clase Clase { get; init; }

    /// <summary>Arranca en 1 al contratarla.</summary>
    public int Nivel { get; set; } = 1;

    /// <summary>
    /// Los perks equipados. La cantidad de slots <b>abiertos</b> la da el nivel.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Sacar un perk de acá lo destruye</b>: no vuelve a la mochila. La
    /// mochila es una sala de espera, no una bóveda. Sin esa regla, equipar
    /// dejaría de ser una decisión — probás uno, si no gusta lo volvés a guardar,
    /// y la elección se vuelve reversible y por lo tanto gratis.
    /// </remarks>
    public List<int> Perks { get; } = [];

    /// <summary>
    /// El arma equipada, o <c>-1</c>. <b>Un solo slot.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Armadura, casco y escudo NO existen como ítem.</b> No dan
    /// stats: son el frame que cambia cuando la clase sube de nivel, y salen de
    /// <see cref="TierVisual"/>.</para>
    ///
    /// <para>Concentrar todo el poder del equipo en una sola pieza hace dos cosas
    /// buenas. La decisión de equipar se vuelve nítida —hay un solo lugar, y lo
    /// que entra saca lo que estaba— y las armas pueden ser mucho más locas sin
    /// desbalancear: con cuatro piezas con habilidades habría que pensar cada
    /// combinación de cuatro.</para>
    /// </remarks>
    public int Arma { get; set; } = -1;

    /// <summary>
    /// Qué juego de armadura, casco y escudo se le dibuja, de 1 a 3.
    /// </summary>
    /// <remarks>
    /// <para>Sale del nivel y de nada más: <b>es cosmético</b>. Nivel 1-2 el tier
    /// 1, 3-4 el tier 2, 5-6 el tier 3.</para>
    ///
    /// <para>Los tres juegos existen porque son los que equipan los bots de
    /// Austral en sus tres escalones —nivel 36, 42 y 47 de <c>Bots.dat</c>—, o sea
    /// una progresión visual que ya eligió gente que jugó el juego.</para>
    ///
    /// <para>Que subir de nivel <b>se vea</b> es la mitad del punto de subir de
    /// nivel. La otra mitad —el slot de perk que se abre— no se ve en el mapa.</para>
    /// </remarks>
    public int TierVisual => Nivel <= 2 ? 1 : Nivel <= 4 ? 2 : 3;

    /// <summary>
    /// Slots abiertos, de 1 a 6. Se abre uno por nivel.
    /// </summary>
    /// <remarks>
    /// <para><b>Esto es lo que hace que la elección "levelear o contratar" sea
    /// una elección.</b> Sin el desbloqueo progresivo, contratar una clase nueva
    /// regalaría 6 slots vacíos de una —capacidad de perk gratis— y contratar
    /// sería siempre mejor que levelear. La cuenta se caería sola en el primer
    /// cálculo que haga un jugador.</para>
    ///
    /// <para>Con slots que se abren de a uno, las dos opciones dan <b>+1 slot</b>
    /// y se diferencian en lo demás: contratar trae un rol nuevo pero arranca en
    /// cero; levelear engorda algo que ya funciona.</para>
    ///
    /// <para>Del nivel 7 en adelante ya no abre nada: para meter otro perk hay
    /// que reemplazar, y el reemplazado se destruye.</para>
    /// </remarks>
    public int SlotsAbiertos => Enteros.Limitar(Nivel, 1, Balance.SlotsPerkPorClase);

    public bool TieneSlotLibre => Perks.Count < SlotsAbiertos;
}

/// <summary>
/// El roster del jugador: qué clases tiene y cómo están.
/// </summary>
public sealed class Roster
{
    private readonly List<ClaseEnRoster> _clases = [];

    public IReadOnlyList<ClaseEnRoster> Clases => _clases;

    public bool Tiene(Clase clase) => Buscar(clase) is not null;

    public ClaseEnRoster? Buscar(Clase clase)
    {
        foreach (var c in _clases)
            if (c.Clase == clase)
                return c;

        return null;
    }

    public void Contratar(Clase clase)
    {
        if (Tiene(clase))
            return;

        _clases.Add(new ClaseEnRoster { Clase = clase });
    }

    public void Levelear(Clase clase)
    {
        var c = Buscar(clase);
        if (c is not null)
            c.Nivel++;
    }

    /// <summary>Las clases que el jugador puede poner en el mapa, para la UI.</summary>
    public List<Clase> Disponibles()
    {
        var lista = new List<Clase>(_clases.Count);
        foreach (var c in _clases)
            lista.Add(c.Clase);

        return lista;
    }

    /// <summary>
    /// Los stats efectivos de una clase: <c>base + nivel + perks + equipo</c>.
    /// </summary>
    /// <remarks>
    /// <para>Se recalcula cada vez que se lo pide, en vez de guardarse. Es más
    /// trabajo por llamada y muchísimo menos riesgo: un stat cacheado se
    /// desincroniza en cuanto alguien equipa algo y se olvida de invalidarlo, y
    /// ese bug en una simulación determinista es de los peores que hay —la
    /// corrida sigue andando, pero da distinto que la revalidación.</para>
    ///
    /// <para>Los porcentajes se suman <b>todos</b> antes de multiplicar, por
    /// <see cref="Enteros.ConPorcentajes"/>: aplicarlos de a uno perdería una
    /// fracción en cada división, y dos defensores con los mismos perks en
    /// distinto orden pegarían distinto.</para>
    /// </remarks>
    public StatsEfectivos StatsDe(Clase clase, Mochila mochila)
    {
        var b = Balance.StatsDe(clase);
        var ficha = Buscar(clase);

        if (ficha is null)
            return new StatsEfectivos(
                b.Dano, b.AlcanceCeldas, b.CadenciaTicks,
                EfectoPerk.Ninguno, 0, HabilidadArma.Ninguna, 0);

        // Cada nivel sube los stats base, aparte de abrir un slot.
        int porNivel = (ficha.Nivel - 1) * 8;

        int extraDano = porNivel;
        int extraCadencia = 0;
        int extraAlcance = 0;

        // El comportamiento que gana es el del perk de tier más alto equipado.
        // Único le gana a Oro; entre dos del mismo tier, el primero equipado —que
        // es una regla arbitraria pero fija, y fija es lo que hace falta.
        var comportamiento = EfectoPerk.Ninguno;
        var tierComportamiento = TierPerk.Bronce;
        int magnitudComportamiento = 0;

        foreach (int idPerk in ficha.Perks)
        {
            var p = CatalogoPerks.Por(idPerk);

            switch (p.Efecto)
            {
                case EfectoPerk.Dano: extraDano += p.Magnitud; break;
                case EfectoPerk.Cadencia: extraCadencia += p.Magnitud; break;
                case EfectoPerk.Alcance: extraAlcance += p.Magnitud; break;

                default:
                    if (comportamiento == EfectoPerk.Ninguno || p.Tier > tierComportamiento)
                    {
                        comportamiento = p.Efecto;
                        tierComportamiento = p.Tier;
                        magnitudComportamiento = p.Magnitud;
                    }
                    break;
            }
        }

        // El arma es el ÚNICO ítem que aporta números. Armadura, casco y escudo
        // son el frame que cambia con el nivel y no suman nada.
        var arma = ficha.Arma >= 0 ? mochila.ArmaPorId(ficha.Arma) : null;

        if (arma is not null)
        {
            extraDano += arma.Dano;
            extraCadencia += arma.Cadencia;
            extraAlcance += arma.Alcance;
        }

        // ⚠ El comportamiento del ARMA le gana al del perk.
        //
        // Un perk de comportamiento se eligió y ocupa un slot; una habilidad de
        // arma vino en el drop. Que gane el arma es lo que hace que encontrar una
        // buena cambie cómo juega ese defensor — que es todo el punto de que las
        // armas tengan habilidades.
        //
        // Las dos no se suman: son la misma ranura de comportamiento, y sumarlas
        // haría que un defensor con perk Y arma tuviera dos efectos encima, que
        // es exactamente lo que la regla "los comportamientos no se apilan"
        // existe para evitar.
        if (arma is not null && arma.Habilidad != HabilidadArma.Ninguna)
        {
            comportamiento = EfectoPerk.Ninguno;
            magnitudComportamiento = 0;
        }

        return new StatsEfectivos(
            Dano: Enteros.ConPorcentajes(b.Dano, extraDano),

            // El alcance se redondea a celdas enteras: la grilla no tiene medias
            // celdas, así que un +15% que no llega a sumar una celda no hace nada
            // hasta que se apila con otro. Es intencional y hay que verlo en la UI.
            AlcanceCeldas: System.Math.Max(1, Enteros.ConPorcentajes(b.AlcanceCeldas, extraAlcance)),

            // ⚠ La cadencia va al REVÉS: menos ticks es más rápido. Un +25% de
            // cadencia tiene que DIVIDIR el intervalo, no multiplicarlo. Es el
            // error de signo más fácil de cometer en todo el balance, y el
            // síntoma sería un perk que dice "más rápido" y hace más lento.
            CadenciaTicks: System.Math.Max(
                1, b.CadenciaTicks * 100 / System.Math.Max(1, 100 + extraCadencia)),

            Comportamiento: comportamiento,
            MagnitudComportamiento: magnitudComportamiento,

            Habilidad: arma?.Habilidad ?? HabilidadArma.Ninguna,
            MagnitudHabilidad: arma?.Magnitud ?? 0);
    }
}

/// <summary>Lo que un defensor de esa clase hace realmente, ahora.</summary>
/// <remarks>
/// Comportamiento y Habilidad son dos cosas distintas y nunca están las dos: el
/// primero viene de un perk equipado, la segunda del arma, y el arma gana. Se
/// devuelven por separado igual porque la UI muestra de dónde salió cada efecto.
/// </remarks>
public readonly record struct StatsEfectivos(
    int Dano,
    int AlcanceCeldas,
    int CadenciaTicks,
    EfectoPerk Comportamiento,
    int MagnitudComportamiento,
    HabilidadArma Habilidad,
    int MagnitudHabilidad);
