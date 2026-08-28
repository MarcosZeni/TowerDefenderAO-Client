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
public sealed class RosterClass
{
    public required DefenderClass DefenderClass { get; init; }

    /// <summary>Arranca en 1 al contratarla.</summary>
    public int Level { get; set; } = 1;

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
    public int Weapon { get; set; } = -1;

    /// <summary>
    /// Con arma <b>y</b> perk de comportamiento puestos, cuál de los dos manda.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>La ranura de comportamiento sigue siendo UNA.</b> Esto no
    /// permite tener los dos: permite <b>elegir</b> cuál está activo. Dejar
    /// convivir un Oro y una habilidad de arma multiplicaría la cantidad de
    /// combinaciones a balancear por el catálogo entero de armas.</para>
    ///
    /// <para><b>Los stats del arma se conservan siempre</b>, gane quien gane el
    /// comportamiento: el +20% de daño y el +15% de cadencia no dependen de esta
    /// decisión. Sin eso, encontrar un arma excelente se sentía como un castigo
    /// —obligaba a apagar un Único que costó dos jefes y un plano— y el jugador la
    /// dejaba sin equipar, que es lo peor que puede pasarle a un drop.</para>
    ///
    /// <para>Arranca en <c>false</c>: el arma gana, que es lo que el juego hacía
    /// antes de que la decisión existiera.</para>
    /// </remarks>
    public bool PreferPerkBehavior { get; set; }

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
    public int VisualTier => Level <= 2 ? 1 : Level <= 4 ? 2 : 3;

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
    public int OpenSlots => Ints.Clamp(Level, 1, MaxLevel);

    public bool HasFreeSlot => Perks.Count < OpenSlots;

    /// <summary>
    /// Nivel máximo, que es también la cantidad de slots. Sale de
    /// <c>clases.json</c> y lo pone <see cref="Roster.Contratar"/>.
    /// </summary>
    public required int MaxLevel { get; init; }
}

/// <summary>
/// El roster del jugador: qué clases tiene y cómo están.
/// </summary>
public sealed class Roster(BalanceData datos)
{
    private readonly BalanceData _datos = datos;
    private readonly List<RosterClass> _clases = [];

    public IReadOnlyList<RosterClass> Classes => _clases;

    public bool Has(DefenderClass clase) => Find(clase) is not null;

    public RosterClass? Find(DefenderClass clase)
    {
        foreach (var c in _clases)
            if (c.DefenderClass == clase)
                return c;

        return null;
    }

    public void Hire(DefenderClass clase)
    {
        if (Has(clase))
            return;

        _clases.Add(new RosterClass { DefenderClass = clase, MaxLevel = _datos.Classes.PerkSlotsPerClass });
    }

    public void LevelUp(DefenderClass clase)
    {
        var c = Find(clase);
        if (c is not null)
            c.Level++;
    }

    /// <summary>Las clases que el jugador puede poner en el mapa, para la UI.</summary>
    public List<DefenderClass> Available()
    {
        var lista = new List<DefenderClass>(_clases.Count);
        foreach (var c in _clases)
            lista.Add(c.DefenderClass);

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
    public EffectiveStats StatsOf(DefenderClass clase, Backpack mochila)
    {
        var b = _datos.StatsOf(clase);
        var ficha = Find(clase);

        // ⚠ Incluso sin ficha se devuelve en la escala INTERNA. La UI llama acá
        // para pintar el alcance del cursor antes de contratar la clase, y si un
        // camino devolviera puntos y el otro centésimas, el círculo del cursor
        // mediría cien veces menos que el real.
        if (ficha is null)
            return new EffectiveStats(
                Ints.ToHundredths(b.Damage), Ints.ToThousandths(b.RangeCells), b.CadenceTicks,
                PerkEffect.None, 0, 0, WeaponAbility.None, 0);

        // Cada nivel sube los stats base, aparte de abrir un slot.
        int porNivel = (ficha.Level - 1) * _datos.Classes.ExtraDamagePerLevel;

        int extraDano = porNivel;
        int extraCadencia = 0;
        int extraAlcance = 0;

        // El comportamiento que gana es el del perk de tier más alto equipado.
        // Único le gana a Oro; entre dos del mismo tier, el primero equipado —que
        // es una regla arbitraria pero fija, y fija es lo que hace falta.
        var comportamiento = PerkEffect.None;
        var tierComportamiento = PerkTier.Bronze;
        int magnitudComportamiento = 0;
        int magnitudComportamientoB = 0;

        foreach (int idPerk in ficha.Perks)
        {
            var p = _datos.Perks.Get(idPerk);

            switch (p.Effect)
            {
                case PerkEffect.Damage: extraDano += p.Magnitude; break;
                case PerkEffect.Cadence: extraCadencia += p.Magnitude; break;
                case PerkEffect.Range: extraAlcance += p.Magnitude; break;

                default:
                    // ⚠ Desde Drops_v2 la mochila NO deja equipar un segundo
                    // comportamiento, así que este desempate no debería hacer
                    // falta nunca. Se deja igual, porque un log viejo o un dato
                    // manipulado pueden traer dos: mejor que gane el más alto en
                    // silencio que que el resultado dependa del orden de la lista.
                    if (comportamiento == PerkEffect.None || p.Tier > tierComportamiento)
                    {
                        comportamiento = p.Effect;
                        tierComportamiento = p.Tier;
                        magnitudComportamiento = p.Magnitude;
                        magnitudComportamientoB = p.MagnitudeB;
                    }
                    break;
            }
        }

        // El arma es el ÚNICO ítem que aporta números. Armadura, casco y escudo
        // son el frame que cambia con el nivel y no suman nada.
        var arma = ficha.Weapon >= 0 ? mochila.WeaponById(ficha.Weapon) : null;

        if (arma is not null)
        {
            extraDano += arma.Damage;
            extraCadencia += arma.Cadence;
            extraAlcance += arma.Range;
        }

        // ⚠ LA RANURA DE COMPORTAMIENTO ES UNA, Y AHORA EL JUGADOR ELIGE CUÁL.
        //
        // Las dos no se suman ni se pueden tener juntas: sumarlas haría que un
        // defensor con perk Y arma tuviera dos efectos encima, que es exactamente
        // lo que la regla "los comportamientos no se apilan" existe para evitar, y
        // multiplicaría las combinaciones a balancear por el catálogo de armas.
        //
        // Lo que cambió es quién gana. Antes ganaba el arma siempre, y eso hacía
        // que encontrar una excelente se sintiera como un CASTIGO: obligaba a
        // apagar un Único que costó dos jefes y un plano, así que el jugador
        // terminaba no equipándola — lo peor que le puede pasar a un drop.
        //
        // ⚠ LOS STATS DEL ARMA SE CONSERVAN EN LOS DOS CASOS. El +20% de daño y el
        // +15% de cadencia ya se sumaron arriba y no dependen de esta decisión:
        // solo cambia el comportamiento activo.
        bool armaTieneHabilidad = arma is not null && arma.Ability != WeaponAbility.None;
        bool usarElPerk = ficha.PreferPerkBehavior && comportamiento != PerkEffect.None;

        if (armaTieneHabilidad && !usarElPerk)
        {
            comportamiento = PerkEffect.None;
            magnitudComportamiento = 0;
            magnitudComportamientoB = 0;
        }

        return new EffectiveStats(
            // ⚠ Se escala PRIMERO y se aplican los porcentajes DESPUÉS. Al revés
            // —porcentajes sobre puntos enteros y después ×100— el truncamiento
            // volvería a pasar exactamente donde pasaba antes, y la escala no
            // habría servido de nada.
            Damage: Ints.WithPercents(Ints.ToHundredths(b.Damage), extraDano),

            // El alcance vive en MILÉSIMAS de celda (propuesta V2). Antes se
            // redondeaba a celdas enteras y un +15% sobre alcance 2 daba 2,3 que
            // truncaba de vuelta a 2: los dos perks de alcance de bronce no hacían
            // nada en 8 de las 9 clases, y Halcón —plata, 240 de oro, uno de los
            // seis slots— no hacía nada en las cuatro cuerpo a cuerpo.
            //
            // El piso es una celda entera: menos que eso no cubre ni la celda de
            // al lado y el defensor no dispararía nunca.
            RangeMilli: System.Math.Max(
                Ints.ThousandthsPerCell,
                Ints.WithPercents(Ints.ToThousandths(b.RangeCells), extraAlcance)),

            // ⚠ La cadencia va al REVÉS: menos ticks es más rápido. Un +25% de
            // cadencia tiene que DIVIDIR el intervalo, no multiplicarlo. Es el
            // error de signo más fácil de cometer en todo el balance, y el
            // síntoma sería un perk que dice "más rápido" y hace más lento.
            CadenceTicks: System.Math.Max(
                1, b.CadenceTicks * 100 / System.Math.Max(1, 100 + extraCadencia)),

            Behavior: comportamiento,
            BehaviorMagnitude: magnitudComportamiento,
            BehaviorMagnitudeB: magnitudComportamientoB,

            // ⚠ Si el jugador eligió el perk, la habilidad del arma queda MUDA:
            // los dos campos nunca están activos a la vez, y el tick puede
            // aplicarlos en cualquier orden sin que el resultado cambie.
            Ability: usarElPerk ? WeaponAbility.None : arma?.Ability ?? WeaponAbility.None,
            AbilityMagnitude: usarElPerk ? 0 : arma?.Magnitude ?? 0);
    }
}

/// <summary>Lo que un defensor de esa clase hace realmente, ahora.</summary>
/// <remarks>
/// <para>Comportamiento y Habilidad son dos cosas distintas y nunca están las dos:
/// el primero viene de un perk equipado, la segunda del arma, y el arma gana. Se
/// devuelven por separado igual porque la UI muestra de dónde salió cada efecto.</para>
///
/// <para>⚠ <b>Los dos números están en escala interna, no en la de los datos.</b>
/// <c>Damage</c> son centésimas de punto y <c>RangeMilli</c> milésimas de celda.
/// Mostrarlos crudos dice "1040 de daño" cuando el defensor pega 10,4: para la UI
/// están <c>Loc.Damage</c> y <c>Loc.Range</c>.</para>
/// </remarks>
public readonly record struct EffectiveStats(
    int Damage,
    int RangeMilli,
    int CadenceTicks,
    PerkEffect Behavior,
    int BehaviorMagnitude,

    /// <summary>
    /// El segundo número del comportamiento, para los que combinan dos cosas.
    /// </summary>
    /// <remarks>
    /// Cero en los que hacen una sola. Lo usan todos los Únicos —cada uno es la
    /// fusión de dos Oro rebajados— y las cadenas y quemaduras, que pegan
    /// distinto al objetivo y a los secundarios.
    /// </remarks>
    int BehaviorMagnitudeB,

    WeaponAbility Ability,
    int AbilityMagnitude);
