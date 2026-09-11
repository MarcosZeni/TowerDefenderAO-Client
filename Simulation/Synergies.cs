namespace TowerOfArgentum.Simulation;

/// <summary>
/// Qué sube una sinergia. Deliberadamente corto.
/// </summary>
/// <remarks>
/// <para>⚠ <b>No es <see cref="PerkEffect"/> y no tiene que serlo.</b> Un perk de
/// Oro es un COMPORTAMIENTO que se le equipa a una clase; una sinergia es un
/// número que sale de cómo está compuesto el tablero. Reusar aquel enum acá
/// dejaría escribir «sinergia que da Ricochet», que es otro sistema entero —quién
/// procea, contra qué, con qué tope— y no está pensado.</para>
///
/// <para>⚠ <b>NO EXISTE «Cadence», y es la omisión más importante del archivo.</b>
/// El daño va en centésimas de punto y el alcance en milésimas de celda, los dos
/// escalados adentro de la simulación; la cadencia sigue en <b>ticks enteros</b>,
/// de 18 a 30. Un tick es un escalón del 3,3 % al 5,6 %: un +5 % sobre cadencia 18
/// da 17,14 y trunca a 17, y cualquier bonus más chico que el escalón hace
/// exactamente cero. Es la anomalía P0 de la propuesta V2, viva todavía acá. Para
/// que una sinergia pueda dar velocidad de ataque hay que escalar la cadencia
/// primero, igual que se hizo con el daño y el alcance.</para>
/// </remarks>
public enum SynergyEffect
{
    None = 0,

    /// <summary>Puntos porcentuales de daño.</summary>
    Damage = 1,

    /// <summary>Puntos porcentuales de alcance.</summary>
    Range = 2,

    // -------------------------------------------------------------------------
    //  Al matar. Son los únicos que no son un número sobre el defensor.
    // -------------------------------------------------------------------------
    //
    //  ⚠ Se resuelven en la FASE DE MUERTES y solo para el defensor cuyo GOLPE
    //  DIRECTO mató: una muerte por splash, cadena o veneno no tiene dueño y no
    //  procea nada. Es la primera regla del sistema de estados —solo el golpe
    //  directo procea— aplicada a la muerte, y es lo que impide que una build de
    //  área convierta cada oleada en una alcancía.
    //
    //  ⚠ No compiten con la ranura de comportamiento del perk de Oro: aquella es
    //  «qué hace mi golpe», esto es «qué pasa cuando mato». Un Ladrón con Saqueo
    //  equipado Y la sinergia cobra las dos cosas, y está bien que las cobre —son
    //  dos decisiones distintas, una del roster y otra del tablero—. Lo que las
    //  acota es el TOPE POR OLEADA, que es obligatorio.

    /// <summary>
    /// Al matar, <c>magnitude</c> % de chance de que el enemigo suelte el DOBLE de
    /// oro. <c>cap</c> es el oro extra máximo por oleada.
    /// </summary>
    DoubleGoldOnKill = 10,

    /// <summary>
    /// Al matar, la Torre se cura <c>magnitude</c> PUNTOS de vida. <c>cap</c> son
    /// los puntos máximos por oleada.
    /// </summary>
    TowerHealOnKill = 11,
}

/// <summary>Lo que hay que saber de un efecto de sinergia sin mirar el enum.</summary>
public static class SynergyEffects
{
    /// <summary>¿Se resuelve al matar, en vez de ser un número del defensor?</summary>
    public static bool IsOnKill(this SynergyEffect e)
        => e is SynergyEffect.DoubleGoldOnKill or SynergyEffect.TowerHealOnKill;
}

/// <summary>A quién le llega el bonus de una sinergia.</summary>
public enum SynergyScope
{
    /// <summary>
    /// Solo a los defensores de las clases del conjunto. Concentrar paga.
    /// </summary>
    Members = 0,

    /// <summary>
    /// A todo el tablero, sea de la clase que sea. Es mucho más fuerte que
    /// <see cref="Members"/>: la misma magnitud rinde el doble o el triple según
    /// cuántas unidades haya afuera del conjunto.
    /// </summary>
    Board = 1,
}

/// <summary>
/// Un escalón: con <paramref name="Need"/> clases distintas del conjunto puestas
/// en el tablero, el bonus es este.
/// </summary>
/// <param name="Only">
/// A qué clases del conjunto les llega. Vacío = a todas las del conjunto. Es lo
/// que permite una sinergia ASIMÉTRICA: «Ladrón + Bardo: el Ladrón roba». Sin
/// esto, el único modo de que una clase gane algo distinto de sus compañeras
/// sería un conjunto de una, que el lector rechaza.
/// </param>
/// <param name="Cap">
/// Tope por oleada para los efectos al matar: oro extra máximo, o puntos de
/// vida máximos. Cero en los efectos numéricos, que no lo usan.
/// </param>
public sealed record SynergyTier(
    int Need, SynergyEffect Effect, int Magnitude, DefenderClass[] Only, int Cap)
{
    /// <summary>¿Este escalón le llega a esa clase?</summary>
    public bool Reaches(DefenderClass clase)
    {
        if (Only.Length == 0)
            return true;

        foreach (var c in Only)
        {
            if (c == clase)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Una sinergia: un conjunto de clases y qué se gana por juntarlas.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Los conjuntos SE PISAN a propósito.</b> Una clase puede estar en
/// varias sinergias. Con particiones disjuntas —tres grupos de tres— el espacio de
/// diseño es una sola tabla y no hay nada que descubrir; con conjuntos que se
/// solapan, «Mago + Clérigo + Paladín» es una combinación posible aunque cada una
/// viva además en su propio trío, y el jugador tiene que leer la acumulación.</para>
///
/// <para>Eso es también lo que hace que el sistema necesite validación: es fácil
/// agregar una sinergia que en el papel se ve chica y que, sumada a las que ya
/// pisaban esas clases, duplica el daño de un defensor sin que nadie lo note.
/// <c>--validar</c> reporta el máximo acumulable por clase.</para>
/// </remarks>
public sealed record Synergy(
    string Key,
    DefenderClass[] Classes,
    int MinUnitsPerClass,
    SynergyScope Scope,
    SynergyTier[] Tiers)
{
    /// <summary>
    /// El escalón que está activo con esa cantidad de clases distintas, o
    /// <c>null</c> si no llega al primero.
    /// </summary>
    /// <remarks>
    /// Gana el más alto que se alcance, no se suman los escalones: si hubiera dos
    /// activos a la vez, «completar» valdría lo de completar MÁS lo de ir por la
    /// mitad, y el segundo escalón dejaría de ser una decisión para volverse un
    /// piso. Es la misma regla que ya rige los estados: gana la potencia más
    /// fuerte, no se apilan.
    /// </remarks>
    public SynergyTier? TierFor(int clasesDistintas)
    {
        SynergyTier? mejor = null;

        foreach (var t in Tiers)
        {
            if (clasesDistintas >= t.Need && (mejor is null || t.Need > mejor.Need))
                mejor = t;
        }

        return mejor;
    }

    /// <summary>
    /// Cuántas clases del conjunto CUENTAN con ese tablero: las que llegan a
    /// <see cref="MinUnitsPerClass"/> defensores puestos.
    /// </summary>
    /// <remarks>
    /// Es la única cuenta que decide si la sinergia se activa, y por eso vive acá
    /// y la usan tanto <see cref="SynergyTable.ActiveFor"/> como el HUD: si la
    /// pantalla contara por su lado, podría decir «2 de 3» donde la simulación ve
    /// una sola porque un Cazador suelto no llega al mínimo.
    /// </remarks>
    public int CountPresent(IReadOnlyList<int> unidadesPorClase)
    {
        int distintas = 0;

        foreach (var c in Classes)
        {
            int i = (int)c;

            if (i >= 0 && i < unidadesPorClase.Count && unidadesPorClase[i] >= MinUnitsPerClass)
                distintas++;
        }

        return distintas;
    }

    /// <summary>¿Esta clase pertenece al conjunto?</summary>
    public bool Contains(DefenderClass clase)
    {
        foreach (var c in Classes)
        {
            if (c == clase)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Un bonus ya resuelto: qué sinergia lo dio, con qué escalón.
/// </summary>
public readonly record struct SynergyBonus(
    string Key, SynergyScope Scope, SynergyTier Tier)
{
    public SynergyEffect Effect => Tier.Effect;
    public int Magnitude => Tier.Magnitude;

    /// <summary>¿Le llega a esa clase, mirando el scope Y el <c>only</c> del escalón?</summary>
    /// <remarks>
    /// ⚠ Es el ÚNICO lugar donde se decide, y por eso está acá y no repetido en
    /// cada consumidor: si <see cref="SynergyTable.BonusFor"/> filtrara por scope
    /// y <see cref="SynergyTable.OnKillFor"/> se olvidara del <c>only</c>, el
    /// mismo Ladrón robaría en el cliente y no en la revalidación del servidor.
    /// </remarks>
    public bool Reaches(DefenderClass clase, Synergy dueña)
    {
        if (Scope == SynergyScope.Members && !dueña.Contains(clase))
            return false;

        return Tier.Reaches(clase);
    }
}

/// <summary>
/// ⚠ CÓMO SE ACUMULAN, que son tres reglas distintas y conviene leerlas juntas
/// (decidido el 2026-09-09).
/// </summary>
/// <remarks>
/// <para><b>1. Entre escalones de la MISMA sinergia: no acumulan.</b> Gana el más
/// alto alcanzado, que es lo que hace <see cref="Synergy.TierFor"/>. Si se
/// sumaran, completar valdría lo de completar MÁS lo de ir por la mitad, y el
/// segundo escalón dejaría de ser una decisión para volverse un piso.</para>
///
/// <para><b>2. Entre sinergias DISTINTAS: acumulan, sumándose entre ellas.</b> Un
/// Paladín en «guerreros» (+25 de daño) y «acero» (+12) lleva +37.</para>
///
/// <para>⚠ <b>NO se componen, y esta es la regla que sostiene todo el sistema.</b>
/// Con las cinco sinergias de hoy la diferencia parece chica —componer da 1,51
/// contra 1,45 de sumar— y esa cifra engaña: lo que importa no es cuánto se
/// despegan hoy, es que <b>componer crece con la cantidad de sinergias y sumar
/// no</b>. Cinco de +12 % sobre una clase dan 1,60 sumando contra 1,76
/// componiendo; ocho dan 1,96 contra 2,48.</para>
///
/// <para><c>sinergias.json</c> existe para que agregar sinergias sea barato.
/// Componiendo, cada sinergia nueva no suma su bonus: <b>multiplica todos los
/// anteriores</b>, así que el costo de agregar la número diez depende de las nueve
/// que ya estaban y nadie puede razonar sobre una sola línea del JSON. Sumando,
/// agregar una sinergia cuesta exactamente lo que dice su magnitud.</para>
///
/// <para><b>3. ⚠ Contra el resto del build: MULTIPLICAN, y se aplican AL FINAL.</b>
/// El nivel, los perks y el arma se suman entre sí en un solo acumulador de puntos
/// porcentuales y se aplican de una vez —<see cref="Ints.WithPercents"/>—; el total
/// de sinergia multiplica ESE resultado, no entra a ese acumulador.</para>
///
/// <para>Es la única de las tres que no sigue el idioma que ya usaba el proyecto,
/// y es deliberada: <b>la composición del tablero escala con el build en vez de
/// competir con él</b>. Un +25 % vale más en la oleada 60, con nivel 6 y arma
/// épica, que en la 15 — así componer bien el tablero es una decisión que madura
/// en vez de diluirse.</para>
///
/// <para>Numéricamente es seguro por la escala interna: son dos
/// <see cref="Ints.MultiplyPercent"/> encadenados y cada uno trunca, pero con el
/// daño en centésimas de punto la pérdida es del orden de 0,005 puntos. Sin la
/// escala ×100 esto no se podría hacer — es la misma razón por la que existe.</para>
///
/// <para>⚠ Tiene dos consecuencias que hay que mirar al calibrar, y las dos
/// apuntan al mismo lado: multiplicar <b>ensancha la brecha entre clases</b>. Con
/// un build de +90 % encima, la ventaja del Paladín sobre el Guerrero pasa de
/// ×1,86 sin sinergias a ×2,03 sumando y a <b>×2,16 multiplicando</b>.</para>
///
/// <para>⚠ <b>La brecha que hay que mirar hoy es la de «caceria»</b>, no la del
/// Paladín: el catálogo vigente deja al Paladín en +37, parejo con el Guerrero y
/// el Asesino. Lo que <c>--validar</c> señala es que adentro de «caceria» —el
/// conjunto escrito PARA el Cazador, que es la clase más floja— el Asesino junta
/// +37 y el Cazador +12, porque al Asesino lo empujan «guerreros» y «acero» desde
/// afuera. Multiplicar la ensancha todavía más.</para>
/// </remarks>
public static class SynergyStacking
{
    /// <summary>
    /// El total de una lista de bonus para un efecto, en puntos porcentuales.
    /// </summary>
    /// <remarks>
    /// Suma —regla 2—. Quien lo aplique tiene que MULTIPLICAR con este total
    /// encima del resultado ya calculado, no meterlo al acumulador del nivel y los
    /// perks: eso sería la regla aditiva, que es la que se descartó.
    /// </remarks>
    public static int Total(IReadOnlyList<SynergyBonus> activas, SynergyEffect efecto)
    {
        int total = 0;

        foreach (var b in activas)
        {
            if (b.Effect == efecto)
                total += b.Magnitude;
        }

        return total;
    }
}

/// <summary>
/// Todas las sinergias de la temporada.
/// </summary>
public sealed class SynergyTable(Synergy[] todas, int maxRosterClasses)
{
    public IReadOnlyList<Synergy> All { get; } = todas;

    /// <summary>
    /// Contra cuántas clases se valida que una sinergia sea alcanzable.
    /// </summary>
    /// <remarks>
    /// Con el roster limitado, una sinergia que pide más clases distintas de las
    /// que caben no se puede completar nunca: es contenido muerto. El cargador lo
    /// rechaza, y por eso el número tiene que estar en los datos.
    /// </remarks>
    public int MaxRosterClasses { get; } = maxRosterClasses;

    /// <summary>
    /// Las sinergias activas para un tablero dado.
    /// </summary>
    /// <param name="unidadesPorClase">
    /// Cuántos defensores hay de cada clase. Índice = <see cref="DefenderClass"/>.
    /// </param>
    /// <remarks>
    /// ⚠ Cuenta <b>clases distintas que llegan a <c>MinUnitsPerClass</c></b>, no
    /// unidades. Sin el mínimo, un defensor suelto de la clase más barata compra
    /// el escalón entero y la sinergia deja de costar una decisión de tablero.
    ///
    /// <para>No consume RNG y no toca estado: es una función del tablero, así que
    /// se puede llamar desde el renderer para dibujar el panel y desde el
    /// validador para medir, sin ensuciar la simulación.</para>
    /// </remarks>
    public List<SynergyBonus> ActiveFor(IReadOnlyList<int> unidadesPorClase)
    {
        var activas = new List<SynergyBonus>();

        foreach (var s in All)
        {
            if (s.TierFor(s.CountPresent(unidadesPorClase)) is { } tier)
                activas.Add(new SynergyBonus(s.Key, s.Scope, tier));
        }

        return activas;
    }

    /// <summary>
    /// Los efectos AL MATAR que le tocan a una clase con ese tablero.
    /// </summary>
    /// <remarks>
    /// Separado de <see cref="BonusFor"/> porque se consume en otro lado y en
    /// otro momento: aquel entra a los stats de cada golpe, esto se resuelve en
    /// la fase de muertes y solo para el defensor cuyo golpe directo mató. Un
    /// escalón al matar no tiene que sumarse a nada: cada uno tira o cura por su
    /// cuenta, contra su propio tope.
    /// </remarks>
    public List<SynergyBonus> OnKillFor(
        DefenderClass clase, IReadOnlyList<int> unidadesPorClase)
    {
        var lista = new List<SynergyBonus>();

        foreach (var b in ActiveFor(unidadesPorClase))
        {
            if (b.Effect.IsOnKill() && b.Reaches(clase, ByKey(b.Key)!))
                lista.Add(b);
        }

        return lista;
    }

    /// <summary>
    /// El bonus que le toca a UNA clase con ese tablero, en puntos porcentuales.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es el único lugar donde se decide qué bonus alcanza a quién</b>,
    /// y por eso devuelve el total ya sumado en vez de la lista: si cada llamador
    /// filtrara por <see cref="SynergyScope"/> a mano, alcanzaría con que uno se
    /// olvidara para que el mismo defensor pegue distinto en el cliente y en la
    /// revalidación del servidor.</para>
    ///
    /// <para>Suma entre sinergias distintas —regla 2 de
    /// <see cref="SynergyStacking"/>—. Lo que devuelve <b>multiplica</b> al build
    /// ya calculado; no se suma al acumulador del nivel y los perks.</para>
    ///
    /// <para>No consume RNG ni toca estado, así que el renderer la puede llamar
    /// para pintar el panel sin ensuciar la simulación.</para>
    /// </remarks>
    public (int Damage, int Range) BonusFor(
        DefenderClass clase, IReadOnlyList<int> unidadesPorClase)
    {
        var activas = ActiveFor(unidadesPorClase);

        // Se filtra sobre la misma lista, hacia atrás, para no alocar una segunda:
        // esto corre dos veces por defensor por tick.
        for (int i = activas.Count - 1; i >= 0; i--)
        {
            if (!activas[i].Reaches(clase, ByKey(activas[i].Key)!))
                activas.RemoveAt(i);
        }

        // ⚠ La suma la hace SynergyStacking y no este método: la regla 2 tiene que
        // estar escrita en un solo lugar. Tenerla acá a mano y además en
        // `SynergyStacking.Total` es cómo una de las dos copias se queda vieja.
        return (SynergyStacking.Total(activas, SynergyEffect.Damage),
                SynergyStacking.Total(activas, SynergyEffect.Range));
    }

    /// <summary>La sinergia con esa key, o <c>null</c>.</summary>
    public Synergy? ByKey(string key)
    {
        foreach (var s in All)
        {
            if (string.Equals(s.Key, key, System.StringComparison.Ordinal))
                return s;
        }

        return null;
    }

    /// <summary>
    /// Cuánto puede acumular UNA clase si todas sus sinergias están al máximo, por
    /// efecto. Es la cuenta que dice si el catálogo se fue de escala.
    /// </summary>
    /// <remarks>
    /// <para>Suma entre sinergias distintas, que es la regla 2 de
    /// <see cref="SynergyStacking"/>. El número que devuelve es el que después
    /// MULTIPLICA al build, así que un +37 acá no es «+37 % más de daño del
    /// total»: es un ×1,37 encima de lo que el defensor ya pegaba con nivel,
    /// perks y arma.</para>
    ///
    /// <para>⚠ Es un <b>techo teórico</b>, no lo que va a pasar: supone que el
    /// jugador completó a la vez todos los conjuntos que tocan esa clase, lo que
    /// el tope del roster puede volver imposible —o no: el +37 del Paladín sale
    /// de «guerreros» y «acero», que son tres clases en total y entran de sobra—.
    /// Sirve para lo que sirve: ver de un vistazo si agregar una sinergia nueva
    /// duplicó el bonus de alguien sin querer.</para>
    /// </remarks>
    public (int Damage, int Range, List<SynergyEffect> OnKill) MaxStackFor(DefenderClass clase)
    {
        int dano = 0;
        int alcance = 0;
        var alMatar = new List<SynergyEffect>();

        foreach (var s in All)
        {
            if (s.Scope == SynergyScope.Members && !s.Contains(clase))
                continue;

            // El escalón más alto que LE LLEGA a esta clase: con `only`, el tope
            // de una sinergia puede ser de otra clase del mismo conjunto.
            SynergyTier? tope = null;

            foreach (var t in s.Tiers)
            {
                if (!t.Reaches(clase))
                    continue;

                if (tope is null || t.Need > tope.Need)
                    tope = t;
            }

            if (tope is null)
                continue;

            if (tope.Effect == SynergyEffect.Damage)
                dano += tope.Magnitude;
            else if (tope.Effect == SynergyEffect.Range)
                alcance += tope.Magnitude;
            else if (tope.Effect.IsOnKill())
                alMatar.Add(tope.Effect);
        }

        return (dano, alcance, alMatar);
    }
}
