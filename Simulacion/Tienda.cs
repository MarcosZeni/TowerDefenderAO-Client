namespace TowerOfArgentum.Simulacion;

/// <summary>Algo que se vende en la tienda de la corrida.</summary>
public readonly record struct OfertaTienda(TipoCosa Tipo, int Id, int Precio, bool Vendida);

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
public sealed class Tienda
{
    private readonly List<OfertaTienda> _stock = [];

    public IReadOnlyList<OfertaTienda> Stock => _stock;

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
    public void Renovar(int oleada, Roster roster, Mochila mochila, Aleatorio rng)
    {
        _stock.Clear();
        OleadaDelStock = oleada;

        var clases = roster.Disponibles();

        if (clases.Count == 0)
            return;

        // Dos perks y dos piezas de equipo. Cuatro es lo que entra en pantalla sin
        // scroll y lo que se puede comparar de un vistazo.
        for (int i = 0; i < 2; i++)
        {
            // ⚠ La tienda NO vende Oro: el jefe es la única fuente. Si lo
            // vendiera, matar al jefe dejaría de ser especial y el jugador con oro
            // podría saltear la parte difícil del juego.
            var tier = rng.Ocurre(65) ? TierPerk.Bronce : TierPerk.Plata;
            var perk = CatalogoPerks.Tirar(tier, clases, rng);

            if (perk is not null)
                _stock.Add(new OfertaTienda(TipoCosa.Perk, perk.Id, PrecioDePerk(tier, oleada), false));
        }

        for (int i = 0; i < 2; i++)
        {
            var arma = GeneradorArmas.Generar(
                Balance.PresupuestoItemTienda(oleada), clases, rng, mochila.ProximoIdArma);

            int id = mochila.RegistrarArma(arma);

            // El precio sale del valor del arma, que sale de su presupuesto y de
            // su rareza: una legendaria cuesta lo que vale sin que haya que
            // decidirlo aparte.
            _stock.Add(new OfertaTienda(TipoCosa.Arma, id, arma.Valor * 3, false));
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
    public bool Vender(int indice)
    {
        if (indice < 0 || indice >= _stock.Count || _stock[indice].Vendida)
            return false;

        _stock[indice] = _stock[indice] with { Vendida = true };
        return true;
    }

    /// <summary>
    /// Cuánto cuesta un perk de la tienda.
    /// </summary>
    /// <remarks>
    /// Sube con la oleada porque el oro también: un precio fijo sería caro al
    /// principio e irrelevante al final. El Plata cuesta el triple que el Bronce,
    /// no el doble, porque además de ser más grande <b>ocupa uno de los mismos
    /// seis slots</b>: lo que se paga es el lugar, no solo el número.
    /// </remarks>
    private static int PrecioDePerk(TierPerk tier, int oleada)
    {
        int baseP = tier == TierPerk.Bronce ? 80 : 240;
        return baseP + (oleada * (tier == TierPerk.Bronce ? 6 : 18));
    }
}
