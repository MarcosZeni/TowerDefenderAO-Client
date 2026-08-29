namespace TowerOfArgentum.Simulation;

/// <summary>
/// Qué categoría de cosa es una oferta de la tienda.
/// </summary>
/// <remarks>
/// <para>⚠ <b>No es <see cref="ItemType"/>, y no puede serlo.</b> Aquel enumera lo
/// que entra a la mochila —perks y armas—; esto enumera lo que la tienda vende, y
/// dos de las cuatro cosas <b>no ocupan lugar de mochila</b>: reclutar una clase
/// toca el roster y aprender un plano toca el conocimiento de la corrida.
/// Mezclarlos haría que comprar una clase con la mochila llena rebotara, que es
/// exactamente el bug que nadie encuentra.</para>
///
/// <para>⚠ <b>El Único no está y no va a estar.</b> La tienda vende ingredientes y
/// conocimiento; el producto final se fabrica. Venderlo borraría de un saque
/// buscar los componentes, comprar el plano y decidir si conservarlos.</para>
/// </remarks>
public enum ShopKind
{
    /// <summary>Un perk de Bronce, Plata u Oro. Va a la mochila.</summary>
    Perk = 0,

    /// <summary>Un arma generada. Va a la mochila.</summary>
    Weapon = 1,

    /// <summary>
    /// Reclutar una clase: la suma al roster en nivel 1.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No se vende «un defensor puesto».</b> Se vende el desbloqueo, y
    /// después las fichas de esa clase se siguen pagando a su costo base. Vender
    /// la unidad convertiría la tienda en un segundo botón de colocar.</para>
    ///
    /// <para>Esto <b>rompe</b> la vieja regla de que el oro compra profundidad y
    /// no ancho, y es deliberado: era la regla la que dejaba a la economía sin
    /// nada que comprar. Lo que la reemplaza es el precio —450 + 8n + 2×costo, o
    /// sea unas ocho fichas— más que la elección de cada tres oleadas sigue
    /// regalando una clase gratis, así que comprar nunca es la vía barata.</para>
    /// </remarks>
    Class = 2,

    /// <summary>
    /// Aprender la receta de un Único, <b>para esta corrida</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No es la compra de la billetera y no reemplaza a la tienda del
    /// menú principal.</b> Lo comprado acá se paga con el oro de la corrida y
    /// muere con ella; el plano comprado con la billetera queda aprendido para
    /// siempre en la cuenta. Los planos siguen siendo la única progresión
    /// permanente del proyecto — si esta compra persistiera, una corrida
    /// afortunada regalaría progresión de cuenta y el precio de la billetera
    /// dejaría de significar nada.</para>
    ///
    /// <para>Es la apuesta de la tienda: no da poder hoy, y para cobrarlo todavía
    /// hacen falta sus dos Oro. Por eso cuesta un tercio de un Oro.</para>
    /// </remarks>
    Blueprint = 3,

    /// <summary>
    /// Subir de nivel una clase que ya está en el roster.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Esto era la pantalla de elección.</b> Cada tres oleadas se abría
    /// un modal con tres opciones —contratar o levelear—, se tomaba UNA y las
    /// otras dos <b>desaparecían</b>. Al lado, la tienda ofrecía cuatro cosas de
    /// las que solo se consumía la comprada. Dos ventanas con dos reglas opuestas
    /// pegadas en la misma pantalla: elegir un nivel borraba las otras opciones y
    /// se leía como un bug de la tienda, porque para el jugador era la misma
    /// tienda.</para>
    ///
    /// <para>Ahora es una oferta más. Comprar una no toca a las otras, que es la
    /// regla que la tienda ya tenía.</para>
    ///
    /// <para>⚠ <b>Al mismo precio que la mochila</b> —<c>300 × nivel²</c>—, sin
    /// descuento. La elección los regalaba, y un nivel gratis cada tres oleadas
    /// vaciaba de sentido al sumidero de oro de la corrida: abre un slot de perk,
    /// sube los stats de todas las fichas de esa clase que ya estén puestas, y no
    /// costaba una moneda. Lo que la tienda aporta no es un descuento, es que el
    /// nivel compita <b>de frente</b> contra un Oro y contra el tablero.</para>
    /// </remarks>
    Level = 4,
}

/// <summary>Algo que se vende en la tienda de la corrida.</summary>
/// <param name="Id">
/// Qué es, según <paramref name="Kind"/>: id de perk, id de arma de la mochila,
/// el valor de <see cref="DefenderClass"/>, o el índice del plano en
/// <c>planos.json</c>.
/// </param>
/// <param name="Free">
/// Se lleva sin pagar. <see cref="Price"/> sigue siendo el precio real.
/// </param>
/// <remarks>
/// <para>⚠ <b><paramref name="Free"/> no es «precio cero».</b> El precio real se
/// conserva porque las ofertas gratis <b>no desaparecen al usarse el cupo</b>:
/// pasan a costar lo que valen. Esa es toda la diferencia con la pantalla de
/// elección que esto reemplazó — ahí tomar una borraba las otras, acá siguen
/// estando y ahora hay que pagarlas.</para>
/// </remarks>
public readonly record struct ShopOffer(
    ShopKind Kind, int Id, int Price, bool Sold, bool Free = false)
{
    /// <summary>Lo que sale de verdad quedarse con esto, hoy.</summary>
    public int Cost => Free ? 0 : Price;
}

/// <summary>
/// La tienda rotativa que aparece dentro de la corrida.
/// </summary>
/// <remarks>
/// <para><b>El stock lo decide la seed</b>, igual que la composición de las
/// oleadas y los drops. Por eso no va al log: el servidor lo regenera. Lo que sí
/// va es la <b>compra</b>, que es una decisión del jugador.</para>
///
/// <para>⚠ <b>Abre junto a la pantalla de elección</b>, cada <c>intervalWaves</c>
/// oleadas —la 3, la 6, la 9…—, no después de cada una. Son unas 33 tiendas en
/// una corrida completa. Que las dos decisiones caigan juntas es lo que las pone
/// a competir: la elección da poder gratis, la tienda lo cobra, y el jugador
/// tiene que decidir si el oro se va al tablero, a un nivel o a una carta.</para>
///
/// <para>⚠ <b>Cuatro ofertas, y no se reponen.</b> Comprar una deja el hueco
/// vacío hasta la próxima tienda. Sin esa regla, un jugador rico compra hasta
/// vaciar el pool entero en una sola visita y la tienda deja de ser una elección
/// para volverse una lista de compras.</para>
///
/// <para>Es la cuarta vez que aparece el mismo patrón —presupuesto de oleada,
/// presupuesto de ítem, pool de Oro, stock de tienda— y no es casualidad: un
/// presupuesto acotado es la única forma de que el contenido generado tenga un
/// techo conocido.</para>
/// </remarks>
public sealed class Shop
{
    private readonly List<ShopOffer> _stock = [];

    public IReadOnlyList<ShopOffer> Stock => _stock;

    /// <summary>En qué oleada se renovó el stock por última vez.</summary>
    public int StockWave { get; private set; }

    /// <summary>
    /// Las categorías, en el orden fijo en que se sortean.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>El orden está congelado</b>, igual que el de las fases del tick.
    /// El sorteo por peso recorre esta lista restando: reordenarla cambia qué
    /// categoría sale con la misma tirada del RNG, y con eso toda la corrida.
    /// </remarks>
    private enum Categoria
    {
        Clase = 0,
        Bronce = 1,
        Plata = 2,
        Gold = 3,
        Blueprint = 4,
        Arma = 5,
    }

    /// <summary>
    /// Arma el stock de la tienda.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Se llama en un punto fijo de la corrida —al abrir la preparación de
    /// una oleada de tienda— y consume números del RNG. Moverla de lugar corre
    /// toda la secuencia posterior: los drops, los perks de NPC y las opciones de
    /// elección saldrían distintos, y la revalidación fallaría.</para>
    ///
    /// <para>El stock <b>no se acumula</b>: lo que no se compró desaparece. Sin
    /// eso, la tienda se vuelve una lista creciente que hay que revisar entera
    /// cada vez, y elegir deja de ser una decisión para volverse una tarea.</para>
    /// </remarks>
    public void Refresh(
        int oleada, Roster roster, Backpack mochila,
        IReadOnlySet<string> planosConocidos, Rng rng, BalanceData datos)
    {
        _stock.Clear();
        StockWave = oleada;

        var clases = roster.Available();
        var cfg = datos.Curves.Shop;

        // ⚠ CON EL ROSTER VACÍO LA TIENDA IGUAL TIENE QUE ABRIR, y antes se
        // rendía acá.
        //
        // Esto pasaba una sola vez —antes de la pantalla de elección inicial— y
        // ahora es el arranque de toda corrida: la elección desapareció y la
        // primera clase sale de la primera tienda. Sin roster no se puede filtrar
        // un perk ni generar un arma —serían cartas muertas—, así que la tienda
        // del arranque es solo de clases, y una viene gratis.
        if (clases.Count == 0)
        {
            ArmarTiendaInicial(oleada, cfg, rng, datos);
            return;
        }

        int oroPuestos = 0;
        int planosPuestos = 0;

        for (int slot = 0; slot < cfg.Slots; slot++)
        {
            // ⚠ Las categorías se descartan POR SLOT, no globalmente: si en este
            // slot salió «Oro» y no quedaba ningún Oro ofrecible, se vuelve a
            // sortear entre las demás en vez de dejar el hueco vacío. Una tienda
            // con dos ranuras en blanco no es una decisión más difícil, es una
            // pantalla más pobre.
            var descartadas = new List<Categoria>();

            for (int intento = 0; intento < 6; intento++)
            {
                var cat = SortearCategoria(
                    oleada, cfg, descartadas, oroPuestos, planosPuestos, rng);

                if (cat is null)
                    break;

                var oferta = Build(
                    cat.Value, oleada, roster, clases, mochila, planosConocidos, rng, datos);

                if (oferta is null)
                {
                    descartadas.Add(cat.Value);
                    continue;
                }

                if (cat.Value == Categoria.Gold)
                    oroPuestos++;

                if (cat.Value == Categoria.Blueprint)
                    planosPuestos++;

                _stock.Add(oferta.Value);
                break;
            }
        }

        GarantizarUtiles(oleada, roster, clases, rng, datos);
    }

    /// <summary>
    /// La tienda del arranque: solo clases, y una gratis.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Al menos una ofensiva, garantizado</b>, y es la que sale gratis.
    /// No es una concesión: con el reparto libre, un arranque de clérigo + bardo +
    /// druida es una corrida perdida en la oleada 1 sin que el jugador haya hecho
    /// nada mal. Medido con el corredor headless: dos de diez seeds terminaban
    /// así. Perder por una decisión propia es el juego; perder por el sorteo de la
    /// primera pantalla, antes de tocar nada, es un bug de diseño.</para>
    ///
    /// <para>⚠ <b>Las otras NO son «las que no elegiste».</b> Quedan a su precio y
    /// se compran con el oro inicial que entrega la primera clase. Esa es la
    /// diferencia con la pantalla de elección: ahí tomar una borraba el resto.</para>
    ///
    /// <para>Solo clases porque sin roster no hay con qué filtrar un perk ni para
    /// quién generar un arma: cualquier otra cosa sería una carta muerta.</para>
    /// </remarks>
    private void ArmarTiendaInicial(int oleada, ShopData cfg, Rng rng, BalanceData datos)
    {
        var ofensivas = datos.Classes.Offensive();

        // La garantizada sale primero, para que el resto se sortee sin ella.
        var garantizada = ofensivas[rng.Index(ofensivas.Length)];

        var pozo = new List<DefenderClass>(ChoiceBuilder.All);
        pozo.Remove(garantizada);

        int cuantas = System.Math.Min(cfg.Slots, ChoiceBuilder.All.Length);
        var salida = new List<DefenderClass>(cuantas) { garantizada };

        for (int i = 1; i < cuantas; i++)
        {
            int j = rng.Index(pozo.Count);
            salida.Add(pozo[j]);
            pozo.RemoveAt(j);
        }

        // ⚠ La gratis se sortea entre las ofensivas de la lista, no es «la
        // primera». Si fuera fija, el jugador aprendería que la de arriba es la
        // buena y la garantía se volvería visible como un truco.
        var candidatasGratis = new List<int>();

        for (int i = 0; i < salida.Count; i++)
        {
            if (System.Array.IndexOf(ofensivas, salida[i]) >= 0)
                candidatasGratis.Add(i);
        }

        int gratis = candidatasGratis.Count == 0
            ? 0
            : candidatasGratis[rng.Index(candidatasGratis.Count)];

        for (int i = 0; i < salida.Count; i++)
        {
            _stock.Add(new ShopOffer(
                ShopKind.Class,
                (int)salida[i],
                cfg.ClassRecruitPrice(oleada, datos.StatsOf(salida[i]).Cost),
                false,
                i == gratis));
        }
    }

    // ⚠ ACÁ VIVÍA `RepartirGratis`, que marcaba una oferta sin cobrar en CADA
    // tienda. Era un invento: la tienda no regala nada. Lo único gratis de toda
    // la corrida es la clase del arranque, que la marca ArmarTiendaInicial y
    // reemplaza a la pantalla de elección inicial.

    /// <summary>
    /// Se tomó la clase del arranque: las otras de esa tienda pasan a costar.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No se retiran.</b> Es la regla entera de por qué la elección se
    /// fusionó con la tienda: comprar una cosa no puede hacer desaparecer a las
    /// otras. La oferta sigue en la pantalla, con su precio, y el jugador decide
    /// si además la paga con el oro inicial que acaba de entrar.</para>
    ///
    /// <para>Solo tiene efecto en la tienda del arranque: es la única que marca
    /// ofertas gratis. <b>La tienda no regala nada más en toda la corrida.</b></para>
    /// </remarks>
    public void ConsumeFree()
    {
        for (int i = 0; i < _stock.Count; i++)
        {
            if (_stock[i].Free)
                _stock[i] = _stock[i] with { Free = false };
        }
    }

    // =========================================================================
    //  Paso 1 — la categoría
    // =========================================================================

    /// <summary>
    /// Sortea la categoría de una oferta, con los pesos de <c>curvas.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Primero la categoría y después el objeto.</b> En una sola bolsa
    /// uniforme los Oro serían comunísimos —22 nombres distintos contra 9 clases—,
    /// los planos aparecerían antes de tener sentido, las clases casi
    /// desaparecerían, y agregar un perk nuevo cambiaría en silencio la
    /// probabilidad de todas las demás categorías.</para>
    ///
    /// <para>⚠ <b>El peso de clase ya no se apaga ni se reparte.</b> Antes, con
    /// el roster completo, la categoría se quedaba sin candidatas y su peso se
    /// volcaba a Bronce y Plata. Hoy esa ranura tiene siempre algo que ofrecer:
    /// reclutar si la clase falta, subirla si está, bloqueada si llegó al tope.
    /// Es la misma razón por la que no existe una categoría «nivel» aparte.</para>
    /// </remarks>
    private static Categoria? SortearCategoria(
        int oleada, ShopData cfg,
        List<Categoria> descartadas, int oroPuestos, int planosPuestos, Rng rng)
    {
        // ⚠ Los mínimos por oleada existen porque un Oro en la oleada 3 es
        // económicamente imposible y el jugador todavía no tiene con qué armar una
        // build alrededor. Coinciden con el primer jefe (oleada 12): es cuando el
        // sistema de comportamientos existe de verdad.
        bool hayPlata = oleada >= cfg.SilverMinWave;
        bool hayOro = oleada >= cfg.GoldMinWave && oroPuestos < cfg.MaxGoldOffers;
        bool hayPlano = oleada >= cfg.BlueprintMinWave && planosPuestos < cfg.MaxBlueprintOffers;

        var pesos = new (Categoria Cat, int Peso)[]
        {
            (Categoria.Clase, cfg.ClassWeight),
            (Categoria.Bronce, cfg.BronzeWeight + (hayPlata ? 0 : cfg.SilverWeight)),
            (Categoria.Plata, hayPlata ? cfg.SilverWeight : 0),
            (Categoria.Gold, hayOro ? cfg.GoldWeight : 0),
            (Categoria.Blueprint, hayPlano ? cfg.BlueprintWeight : 0),
            (Categoria.Arma, cfg.WeaponWeight),
        };

        int total = 0;

        foreach (var (cat, peso) in pesos)
        {
            if (peso > 0 && !descartadas.Contains(cat))
                total += peso;
        }

        if (total <= 0)
            return null;

        int tirada = rng.Between(0, total);

        foreach (var (cat, peso) in pesos)
        {
            if (peso <= 0 || descartadas.Contains(cat))
                continue;

            tirada -= peso;

            if (tirada < 0)
                return cat;
        }

        return null;
    }

    // =========================================================================
    //  Paso 2 — el objeto de esa categoría
    // =========================================================================

    private ShopOffer? Build(
        Categoria cat, int oleada, Roster roster, IReadOnlyList<DefenderClass> clases,
        Backpack mochila, IReadOnlySet<string> planosConocidos, Rng rng, BalanceData datos)
        => cat switch
        {
            Categoria.Clase => ArmarClase(oleada, roster, rng, datos),
            Categoria.Bronce => ArmarPerk(PerkTier.Bronze, oleada, roster, planosConocidos, mochila, rng, datos),
            Categoria.Plata => ArmarPerk(PerkTier.Silver, oleada, roster, planosConocidos, mochila, rng, datos),
            Categoria.Gold => ArmarPerk(PerkTier.Gold, oleada, roster, planosConocidos, mochila, rng, datos),
            Categoria.Blueprint => ArmarPlano(oleada, roster, planosConocidos, mochila, rng, datos),
            _ => ArmarArma(oleada, clases, mochila, rng, datos),
        };

    /// <summary>
    /// La ranura de clase: <b>reclutarla si no la tenés, subirla si la tenés</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es UNA categoría con dos caras, no dos categorías.</b> Lo que
    /// rota es la <b>clase</b>; qué te ofrece de ella lo decide tu roster en el
    /// momento de armar la tienda:</para>
    ///
    /// <list type="bullet">
    /// <item>no está en el roster → <c>ShopKind.Class</c>, el precio de reclutar;</item>
    /// <item>está y puede subir → <c>ShopKind.Level</c>, <c>300 × nivelDestino²</c>;</item>
    /// <item>está y llegó al tope → <c>ShopKind.Level</c> igual, y la ranura
    /// aparece <b>bloqueada</b>. No se retira: el jugador tiene que poder ver que
    /// esa clase ya no tiene adónde subir, que es información de su roster, no un
    /// hueco.</item>
    /// </list>
    ///
    /// <para>El bloqueado no se deriva de un campo guardado sino del roster de
    /// ahora: si el jugador sube esa clase desde la mochila con la tienda abierta,
    /// la ranura se bloquea sola. Un <c>bool</c> en la oferta se habría quedado
    /// viejo justo en ese caso.</para>
    ///
    /// <para>Regla 3: nada repetido dentro de la misma tienda. Se mira contra las
    /// <b>dos</b> caras, o la misma clase podría salir como reclutar y como nivel
    /// en la misma pantalla.</para>
    /// </remarks>
    private ShopOffer? ArmarClase(
        int oleada, Roster roster, Rng rng, BalanceData datos)
    {
        var candidatas = new List<DefenderClass>();

        foreach (var c in ChoiceBuilder.All)
        {
            if (YaOfrecida(ShopKind.Class, (int)c) || YaOfrecida(ShopKind.Level, (int)c))
                continue;

            candidatas.Add(c);
        }

        if (candidatas.Count == 0)
            return null;

        var elegida = candidatas[rng.Index(candidatas.Count)];
        var ficha = roster.Find(elegida);

        if (ficha is null)
        {
            return new ShopOffer(
                ShopKind.Class,
                (int)elegida,
                datos.Curves.Shop.ClassRecruitPrice(oleada, datos.StatsOf(elegida).Cost),
                false);
        }

        // ⚠ El precio se congela con el nivel de AHORA. Si el jugador sube esa
        // clase desde la mochila mientras la tienda está abierta, la oferta queda
        // vieja: Run.Buy la rechaza comparando el nivel y no cobra nada.
        // Recalcularlo al apretar lo ataría al momento del click, y la tienda no
        // tiene límite de tiempo.
        return new ShopOffer(
            ShopKind.Level,
            (int)elegida,
            datos.Curves.Economy.LevelUpPrice(ficha.Level + 1),
            false);
    }

    /// <summary>
    /// Un perk del tier pedido que <b>alguna clase del roster pueda equipar de
    /// verdad</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ No alcanza con que la clase esté en la lista del perk: hay que
    /// mirar el <b>apilado</b>. Un Bronce que las tres clases que lo aceptan ya
    /// tienen tres veces es una carta muerta que igual ocupa una de las cuatro
    /// ranuras.</para>
    ///
    /// <para>El peso de sinergia empuja hacia la pieza que le falta a un plano que
    /// el jugador conoce y tiene a medias. ×2 y no ×10: la build se puede
    /// <b>perseguir</b>, no garantizar — si la garantizara, el plano dejaría de
    /// ser una apuesta y pasaría a ser un trámite.</para>
    /// </remarks>
    private ShopOffer? ArmarPerk(
        PerkTier tier, int oleada, Roster roster, IReadOnlySet<string> planosConocidos,
        Backpack mochila, Rng rng, BalanceData datos)
    {
        var candidatos = new List<PerkDefinition>();

        foreach (var p in datos.Perks.All)
        {
            if (p.Tier != tier)
                continue;

            // Regla 3: nada repetido dentro de la misma tienda.
            if (YaOfrecida(ShopKind.Perk, p.Id))
                continue;

            // ⚠ Un Oro duplicado en la mochila es peso muerto: la fusión pide dos
            // Oro DISTINTOS, y la ranura de comportamiento es una sola. Vender el
            // que ya está esperando es cobrarle al jugador por nada.
            if (tier == PerkTier.Gold && EnLaMochila(mochila, p.Id))
                continue;

            if (!AlgunaLoPuedeEquipar(roster, p))
                continue;

            candidatos.Add(p);
        }

        if (candidatos.Count == 0)
            return null;

        var elegido = tier == PerkTier.Gold
            ? SortearConSinergia(candidatos, roster, planosConocidos, mochila, datos, rng)
            : candidatos[rng.Index(candidatos.Count)];

        return new ShopOffer(
            ShopKind.Perk, elegido.Id, datos.Curves.Shop.PerkPrice(tier, oleada), false);
    }

    /// <summary>
    /// Un plano que el jugador todavía no conozca y que le sirva a su roster.
    /// </summary>
    /// <remarks>
    /// <para>Los que traen <c>initial: true</c> se saltean: ya están aprendidos
    /// desde el arranque, así que venderlos sería cobrar por nada.</para>
    ///
    /// <para>El peso ×2 va al plano cuyos <b>dos</b> Oro el jugador ya tiene. Es
    /// la otra mitad de la sinergia: si la primera empuja los Oro hacia la receta
    /// conocida, esta empuja la receta hacia los Oro que ya están.</para>
    /// </remarks>
    private ShopOffer? ArmarPlano(
        int oleada, Roster roster, IReadOnlySet<string> planosConocidos,
        Backpack mochila, Rng rng, BalanceData datos)
    {
        var candidatos = new List<int>();
        var pesos = new List<int>();

        var todos = datos.Blueprints.All;

        for (int i = 0; i < todos.Count; i++)
        {
            var plano = todos[i];

            // Regla 4: un plano ya aprendido es una carta muerta.
            if (plano.Initial || planosConocidos.Contains(plano.Key))
                continue;

            if (YaOfrecida(ShopKind.Blueprint, i))
                continue;

            // Y un plano cuyo Único no lo puede equipar nadie del roster tampoco
            // sirve: el jugador pagaría por una receta que nunca va a poder cerrar.
            var unico = datos.Perks.Get(plano.ResultingUnique);
            bool alguna = false;

            foreach (var ficha in roster.Classes)
                alguna |= unico.WorksFor(ficha.DefenderClass);

            if (!alguna)
                continue;

            bool tieneLosDos = Tiene(roster, mochila, plano.GoldA) && Tiene(roster, mochila, plano.GoldB);

            candidatos.Add(i);
            pesos.Add(tieneLosDos ? datos.Curves.Shop.RecipeWeightMultiplier : 1);
        }

        if (candidatos.Count == 0)
            return null;

        int indice = SortearPorPeso(candidatos, pesos, rng);

        return new ShopOffer(
            ShopKind.Blueprint, indice, datos.Curves.Shop.BlueprintPrice.At(oleada), false);
    }

    /// <summary>
    /// Un arma generada con el presupuesto de la oleada.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Esta categoría no sale del análisis de la tienda rotativa</b>, que no
    /// sabía que la tienda ya vendía armas. Sacarlas dejaría al generador entero
    /// en ocho ítems por corrida —solo los del jefe— y todo el sistema de
    /// habilidades de arma se volvería contenido muerto. Los pesos son relativos,
    /// así que agregar la sexta categoría no altera la proporción entre las cinco
    /// del análisis.
    /// </remarks>
    private ShopOffer ArmarArma(
        int oleada, IReadOnlyList<DefenderClass> clases, Backpack mochila, Rng rng, BalanceData datos)
    {
        var arma = WeaponGenerator.Generate(
            datos.Curves.PerWave.ShopItemBudget.At(oleada),
            clases, rng, mochila.NextWeaponId, datos.Weapons);

        int id = mochila.RegisterWeapon(arma);

        // El precio sale del valor del arma, que sale de su presupuesto y de su
        // rareza: una legendaria cuesta lo que vale sin que haya que decidirlo
        // aparte.
        return new ShopOffer(
            ShopKind.Weapon, id, arma.Value * datos.Weapons.ShopPriceMultiplier, false);
    }

    // =========================================================================
    //  Paso 3 — la garantía de que la pantalla sirva para algo
    // =========================================================================

    /// <summary>
    /// Se asegura de que al menos <c>minUsableOffers</c> de las cartas le sirvan
    /// al roster actual.
    /// </summary>
    /// <remarks>
    /// <para>No hace falta que las cuatro sean perfectas para la build: la
    /// incertidumbre es parte de la decisión, y una tienda que siempre trae
    /// exactamente lo que uno busca deja de ser una tienda. Pero cuatro cartas
    /// muertas es una pantalla que frena la corrida sin ofrecer nada, y eso pasa
    /// más de lo que parece con un roster chico.</para>
    ///
    /// <para>Se reemplaza <b>desde el final</b> y solo por perks utilizables: es
    /// una operación acotada —a lo sumo <c>slots</c> pasadas— y determinista, que
    /// es lo único que la revalidación necesita.</para>
    /// </remarks>
    private void GarantizarUtiles(
        int oleada, Roster roster, IReadOnlyList<DefenderClass> clases, Rng rng, BalanceData datos)
    {
        var cfg = datos.Curves.Shop;

        for (int vuelta = 0; vuelta < cfg.Slots; vuelta++)
        {
            int utiles = 0;

            foreach (var o in _stock)
            {
                if (EsUtil(o, roster, clases, datos))
                    utiles++;
            }

            if (utiles >= cfg.MinUsableOffers)
                return;

            // El último inútil de la lista. Reemplazar desde el final deja intactas
            // las primeras cartas, que es lo que el jugador mira primero.
            int aCambiar = -1;

            for (int i = _stock.Count - 1; i >= 0; i--)
            {
                if (!EsUtil(_stock[i], roster, clases, datos))
                {
                    aCambiar = i;
                    break;
                }
            }

            if (aCambiar < 0)
                return;

            // Un Bronce siempre que se pueda: es la compra menor, la que no rompe
            // un plan económico, y por lo tanto el relleno honesto.
            var repuesto = ArmarPerkUtil(PerkTier.Bronze, oleada, roster, rng, datos)
                           ?? ArmarPerkUtil(PerkTier.Silver, oleada, roster, rng, datos);

            if (repuesto is null)
                return;

            _stock[aCambiar] = repuesto.Value;
        }
    }

    /// <summary>Un perk de ese tier que alguna clase del roster pueda equipar hoy.</summary>
    private ShopOffer? ArmarPerkUtil(
        PerkTier tier, int oleada, Roster roster, Rng rng, BalanceData datos)
    {
        var candidatos = new List<PerkDefinition>();

        foreach (var p in datos.Perks.All)
        {
            if (p.Tier != tier || YaOfrecida(ShopKind.Perk, p.Id))
                continue;

            if (AlgunaLoPuedeEquipar(roster, p))
                candidatos.Add(p);
        }

        if (candidatos.Count == 0)
            return null;

        var elegido = candidatos[rng.Index(candidatos.Count)];

        return new ShopOffer(
            ShopKind.Perk, elegido.Id, datos.Curves.Shop.PerkPrice(tier, oleada), false);
    }

    /// <summary>¿Esta oferta le aporta algo al roster de hoy?</summary>
    /// <remarks>
    /// Reclutar y las armas cuentan siempre: la clase <b>expande</b> el roster, y
    /// el generador de armas ya genera filtrando por las clases activas. Los perks
    /// y los planos son los que pueden salir muertos.
    /// </remarks>
    private static bool EsUtil(
        ShopOffer oferta, Roster roster, IReadOnlyList<DefenderClass> clases, BalanceData datos)
    {
        switch (oferta.Kind)
        {
            case ShopKind.Class:
            case ShopKind.Weapon:
                return true;

            case ShopKind.Perk:
                return AlgunaLoPuedeEquipar(roster, datos.Perks.Get(oferta.Id));

            default:
            {
                if (oferta.Id < 0 || oferta.Id >= datos.Blueprints.All.Count)
                    return false;

                var unico = datos.Perks.Get(datos.Blueprints.All[oferta.Id].ResultingUnique);

                foreach (var c in clases)
                {
                    if (unico.WorksFor(c))
                        return true;
                }

                return false;
            }
        }
    }

    // =========================================================================
    //  Ayudantes
    // =========================================================================

    private bool YaOfrecida(ShopKind tipo, int id)
    {
        foreach (var o in _stock)
        {
            if (o.Kind == tipo && o.Id == id)
                return true;
        }

        return false;
    }

    /// <summary>
    /// ¿Alguna clase del roster puede equipar este perk <b>hoy</b>?
    /// </summary>
    /// <remarks>
    /// Mira el apilado, no solo la lista de clases: un Bronce que las tres clases
    /// que lo aceptan ya tienen tres veces está tan muerto como uno de una clase
    /// que no se contrató. <b>No</b> mira los slots libres a propósito — un perk
    /// que espera a que la clase suba de nivel es una compra legítima, y el nivel
    /// se puede comprar.
    /// </remarks>
    private static bool AlgunaLoPuedeEquipar(Roster roster, PerkDefinition def)
    {
        foreach (var ficha in roster.Classes)
        {
            if (!def.WorksFor(ficha.DefenderClass))
                continue;

            int yaTiene = 0;

            foreach (int id in ficha.Perks)
            {
                if (id == def.Id)
                    yaTiene++;
            }

            if (yaTiene < def.MaxStacked)
                return true;
        }

        return false;
    }

    private static bool EnLaMochila(Backpack mochila, int idPerk)
    {
        foreach (var cosa in mochila.Items)
        {
            if (cosa.Kind == ItemType.Perk && cosa.Id == idPerk)
                return true;
        }

        return false;
    }

    /// <summary>¿Tiene este perk, equipado o en la mochila?</summary>
    private static bool Tiene(Roster roster, Backpack mochila, int idPerk)
    {
        if (EnLaMochila(mochila, idPerk))
            return true;

        foreach (var ficha in roster.Classes)
        {
            foreach (int p in ficha.Perks)
            {
                if (p == idPerk)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sortea un Oro dando más peso a la pieza que le falta a un plano conocido.
    /// </summary>
    /// <remarks>
    /// Es la misma idea que el sorteo del jefe, con el mismo cuidado: ×2 hace que
    /// la receta se pueda perseguir, ×10 la garantizaría y el plano dejaría de ser
    /// una apuesta.
    /// </remarks>
    private static PerkDefinition SortearConSinergia(
        List<PerkDefinition> candidatos, Roster roster, IReadOnlySet<string> planosConocidos,
        Backpack mochila, BalanceData datos, Rng rng)
    {
        int mult = datos.Curves.Shop.RecipeWeightMultiplier;

        if (mult <= 1 || candidatos.Count == 1)
            return candidatos[rng.Index(candidatos.Count)];

        var pesos = new List<int>(candidatos.Count);

        foreach (var p in candidatos)
        {
            bool completa = false;

            foreach (var plano in datos.Blueprints.All)
            {
                if (!plano.Initial && !planosConocidos.Contains(plano.Key))
                    continue;

                if (plano.GoldA == p.Id && Tiene(roster, mochila, plano.GoldB))
                    completa = true;

                if (plano.GoldB == p.Id && Tiene(roster, mochila, plano.GoldA))
                    completa = true;
            }

            pesos.Add(completa ? mult : 1);
        }

        int total = 0;

        foreach (int w in pesos)
            total += w;

        int tirada = rng.Between(0, total);

        for (int i = 0; i < candidatos.Count; i++)
        {
            tirada -= pesos[i];

            if (tirada < 0)
                return candidatos[i];
        }

        return candidatos[^1];
    }

    /// <summary>Sorteo por peso sobre dos listas paralelas. Enteros, sin coma flotante.</summary>
    private static int SortearPorPeso(List<int> valores, List<int> pesos, Rng rng)
    {
        int total = 0;

        foreach (int w in pesos)
            total += w;

        if (total <= 0)
            return valores[rng.Index(valores.Count)];

        int tirada = rng.Between(0, total);

        for (int i = 0; i < valores.Count; i++)
        {
            tirada -= pesos[i];

            if (tirada < 0)
                return valores[i];
        }

        return valores[^1];
    }

    /// <summary>
    /// Marca una oferta como vendida.
    /// </summary>
    /// <remarks>
    /// Se marca en vez de sacarse de la lista para que los índices no se muevan:
    /// el log guarda "compré la oferta 2", y si la lista se achicara al comprar,
    /// re-simular ese log compraría otra cosa. Y además <b>no se repone</b>: el
    /// hueco queda vacío hasta la próxima tienda.
    /// </remarks>
    public bool Sell(int indice)
    {
        if (indice < 0 || indice >= _stock.Count || _stock[indice].Sold)
            return false;

        _stock[indice] = _stock[indice] with { Sold = true };
        return true;
    }

    /// <summary>
    /// Retira la oferta de reclutar una clase que el jugador acaba de conseguir.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Existe porque <b>la tienda ahora se arma ANTES de la elección</b>,
    /// para que las dos decisiones se vean juntas. El precio de ese orden es que
    /// el stock puede ofrecer «reclutar Cazador» y que la elección, dos segundos
    /// después, regale Cazador.</para>
    ///
    /// <para>Antes ese caso <b>colgaba el juego</b>: comprarla rebotaba sin
    /// marcarse vendida, y un bot que reintenta con el reloj congelado no termina
    /// nunca. Medido: <c>--balance 24</c> sin terminar en 30 minutos.</para>
    ///
    /// <para>Se marca vendida y no se cobra nada. No consume RNG, así que
    /// llamarla no mueve la secuencia.</para>
    /// </remarks>
    public void WithdrawClass(DefenderClass clase)
    {
        for (int i = 0; i < _stock.Count; i++)
        {
            if (_stock[i].Kind == ShopKind.Class && !_stock[i].Sold && _stock[i].Id == (int)clase)
                _stock[i] = _stock[i] with { Sold = true };
        }
    }

    /// <summary>
    /// Cierra la tienda: lo que no se compró desaparece.
    /// </summary>
    /// <remarks>
    /// <para>Se llama al <b>lanzar la oleada</b>, que es cuando el panel deja de
    /// verse. Sin esto, el stock sobrevive hasta la próxima renovación —dos oleadas
    /// más— y queda comprable por un camino que el jugador no ve: la interfaz dice
    /// que la tienda cerró y la simulación sigue aceptando compras. <b>Que las dos
    /// mitades discrepen es de los peores bugs de este proyecto</b>, porque el
    /// síntoma aparece en la revalidación y no en la partida.</para>
    ///
    /// <para>Y hay un caso donde eso se vuelve un cuelgue: una oferta de «reclutar
    /// Cazador» que sobrevive a que el jugador ya tenga Cazador. Comprarla rebota
    /// sin marcarse vendida, así que un bot que reintenta con el reloj congelado no
    /// termina nunca. Medido: <c>--balance 24</c> sin terminar en 30 minutos.</para>
    ///
    /// <para>No consume RNG y no toca la mochila: las armas ya registradas siguen
    /// existiendo por su id.</para>
    /// </remarks>
    public void Close()
    {
        _stock.Clear();
        StockWave = 0;
    }
}
