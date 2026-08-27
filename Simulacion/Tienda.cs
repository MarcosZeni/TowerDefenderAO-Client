namespace TowerOfArgentum.Simulacion;

/// <summary>Algo que se vende en la tienda de la corrida.</summary>
public readonly record struct ShopOffer(ItemType Kind, int Id, int Price, bool Sold);

/// <summary>
/// La tienda que aparece dentro de la corrida.
/// </summary>
/// <remarks>
/// <para><b>El stock lo decide la seed</b>, igual que la composición de las
/// oleadas y los drops. Por eso no va al log: el servidor lo regenera. Lo que sí
/// va es la <b>compra</b>, que es una decisión del jugador.</para>
///
/// <para>Es la tercera vez que aparece el mismo patrón —presupuesto de oleada,
/// presupuesto de ítem, stock de tienda— y no es casualidad: un presupuesto es la
/// única forma de que el contenido generado tenga un techo conocido.</para>
/// </remarks>
public sealed class Shop
{
    private readonly List<ShopOffer> _stock = [];

    public IReadOnlyList<ShopOffer> Stock => _stock;

    /// <summary>En qué oleada se renovó el stock por última vez.</summary>
    public int OleadaDelStock { get; private set; }

    /// <summary>
    /// Arma el stock de la oleada.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Se llama en un punto fijo de la corrida —al lanzar una oleada— y
    /// consume números del RNG. Moverla de lugar corre toda la secuencia
    /// posterior: los drops, los perks de NPC y las opciones de elección saldrían
    /// distintos, y la revalidación fallaría.</para>
    ///
    /// <para>El stock <b>no se acumula</b>: lo que no se compró desaparece. Sin
    /// eso, la tienda se vuelve una lista creciente que hay que revisar entera
    /// cada vez, y elegir deja de ser una decisión para volverse una tarea.</para>
    /// </remarks>
    public void Refresh(int oleada, Roster roster, Backpack mochila, Rng rng, BalanceData datos)
    {
        _stock.Clear();
        OleadaDelStock = oleada;

        var clases = roster.Available();

        if (clases.Count == 0)
            return;

        var cfg = datos.Curves.Shop;

        // Cuántos entran en pantalla sin scroll y se pueden comparar de un
        // vistazo. Sale de curvas.json («tienda»).
        for (int i = 0; i < cfg.PerksInStock; i++)
        {
            // ⚠ La tienda NO vende Oro: el jefe es la única fuente. Si lo
            // vendiera, matar al jefe dejaría de ser especial y el jugador con oro
            // podría saltear la parte difícil del juego.
            var tier = rng.Happens(cfg.BronzeChance) ? PerkTier.Bronze : PerkTier.Silver;
            var perk = datos.Perks.Roll(tier, clases, rng);

            if (perk is not null)
                _stock.Add(new ShopOffer(ItemType.Perk, perk.Id, cfg.PerkPrice(tier, oleada), false));
        }

        for (int i = 0; i < cfg.WeaponsInStock; i++)
        {
            var arma = WeaponGenerator.Generate(
                datos.Curves.PorOleada.ShopItemBudget.En(oleada),
                clases, rng, mochila.NextWeaponId, datos.Weapons);

            int id = mochila.RegisterWeapon(arma);

            // El precio sale del valor del arma, que sale de su presupuesto y de
            // su rareza: una legendaria cuesta lo que vale sin que haya que
            // decidirlo aparte.
            _stock.Add(new ShopOffer(ItemType.Weapon, id, arma.Value * datos.Weapons.ShopPriceMultiplier, false));
        }
    }

    /// <summary>
    /// Marca una oferta como vendida.
    /// </summary>
    /// <remarks>
    /// Se marca en vez de sacarse de la lista para que los índices no se muevan:
    /// el log guarda "compré la oferta 2", y si la lista se achicara al comprar,
    /// re-simular ese log compraría otra cosa.
    /// </remarks>
    public bool Sell(int indice)
    {
        if (indice < 0 || indice >= _stock.Count || _stock[indice].Sold)
            return false;

        _stock[indice] = _stock[indice] with { Sold = true };
        return true;
    }

}
