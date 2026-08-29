namespace TowerOfArgentum.Simulation;

/// <summary>
/// Lo que pasó adentro de una corrida, contado mientras pasa.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Esto NO es entrada de la simulación y no entra al hash.</b> Es
/// salida: nada de acá se lee para decidir nada, así que contar de más no puede
/// cambiar una corrida. Si algún día un número de acá alimentara una decisión del
/// tick, dejaría de ser una métrica y pasaría a ser estado — y habría que
/// serializarlo, revalidarlo y hashearlo.</para>
///
/// <para>Existe porque <b>tres veces seguidas razonar sobre el balance dio la
/// conclusión equivocada y medirlo dio la correcta</b>. Con la tienda rotativa el
/// problema se agrava: ahora el oro compra poder horizontal —clases—, vertical
/// —perks— y habilitadores de build —planos—, y sin desglose «el jugador terminó
/// con 12.000 sin gastar» no dice si sobra oro, si los precios están mal o si no
/// había nada que valiera la pena.</para>
///
/// <para>Las preguntas que esto tiene que poder contestar, y que la exportación
/// vieja no podía: cuántos Oro se ven por corrida y cuántos se compran, cuánto oro
/// hay antes y después de cada tienda, qué porcentaje de tiendas ofrece algo que
/// el jugador realmente quiere, cuántos planos se compran y jamás se completan, y
/// en qué oleada aparece el primer Único.</para>
/// </remarks>
public sealed class RunMetrics
{
    /// <summary>Cuántas veces se renovó el stock de la tienda.</summary>
    public int ShopsVisited { get; set; }

    /// <summary>
    /// Oro en el bolsillo al abrir cada tienda, sumado.
    /// </summary>
    /// <remarks>
    /// Va como suma y no como lista para que el tamaño sea constante: son 33
    /// tiendas por corrida y N corridas por medición. El promedio sale de dividir
    /// por <see cref="ShopsVisited"/>, que es lo que se mira.
    /// </remarks>
    public long GoldBeforeShopTotal { get; set; }

    /// <summary>Oro al lanzar la oleada siguiente, o sea después de comprar.</summary>
    public long GoldAfterShopTotal { get; set; }

    /// <summary>Oro sin gastar cuando la corrida terminó, de la forma que sea.</summary>
    public int GoldAtEnd { get; set; }

    /// <summary>
    /// Suma de los niveles del roster al terminar, y cuántas clases llegaron al
    /// máximo.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Son los dos números que dicen si el sumidero de oro se agotó.</b>
    /// Sin ellos, «terminó con 29.000 sin gastar» tiene dos lecturas opuestas que
    /// piden arreglos contrarios: o sobra ingreso —y hay que recortarlo— o se
    /// acabó en qué gastarlo —y hay que abrir un sumidero—.</para>
    ///
    /// <para>El nivel cuesta <c>300 × nivelDestino²</c>, o sea 27.000 llevar una
    /// clase de 1 a 6 y 243.000 las nueve. Si el roster termina lejos del máximo
    /// <b>y</b> sobra oro, el sumidero no se agotó: lo que falta es que el jugador
    /// tenga motivo para usarlo.</para>
    /// </remarks>
    public int RosterLevelTotal { get; set; }

    public int ClassesAtMaxLevel { get; set; }

    // -------------------------------------------------------------------------
    //  Las ofertas: qué VIO el jugador
    // -------------------------------------------------------------------------
    //
    //  ⚠ Hay que contar lo ofrecido además de lo comprado, o el porcentaje de
    //  compra no se puede calcular. «Se compraron 3 Oro» no dice nada; «se
    //  compraron 3 de los 11 que aparecieron» dice que el precio está en el rango
    //  que el diseño busca (15-35%).

    public int OffersClass { get; set; }

    public int OffersBronze { get; set; }

    public int OffersSilver { get; set; }

    public int OffersGold { get; set; }

    public int OffersBlueprint { get; set; }

    public int OffersWeapon { get; set; }

    /// <summary>Tiendas donde al menos una oferta le servía al roster de ese momento.</summary>
    /// <remarks>
    /// Si este número se despega de <see cref="ShopsVisited"/>, la garantía de
    /// «al menos dos utilizables» no está alcanzando y la tienda está frenando la
    /// corrida sin ofrecer nada.
    /// </remarks>
    public int ShopsWithSomethingUsable { get; set; }

    // -------------------------------------------------------------------------
    //  Las compras y el gasto
    // -------------------------------------------------------------------------

    public int BoughtClass { get; set; }

    public int BoughtBronze { get; set; }

    public int BoughtSilver { get; set; }

    public int BoughtGold { get; set; }

    public int BoughtBlueprint { get; set; }

    public int BoughtWeapon { get; set; }

    public long GoldSpentClasses { get; set; }

    public long GoldSpentBronze { get; set; }

    public long GoldSpentSilver { get; set; }

    public long GoldSpentGoldPerks { get; set; }

    public long GoldSpentBlueprints { get; set; }

    public long GoldSpentWeapons { get; set; }

    /// <summary>Lo gastado en el sumidero cuadrático de niveles.</summary>
    public long GoldSpentLevels { get; set; }

    /// <summary>Lo gastado en poner fichas en el tablero.</summary>
    public long GoldSpentDefenders { get; set; }

    // -------------------------------------------------------------------------
    //  La build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Perks de Oro conseguidos por cualquier vía: elección de jefe, tienda o
    /// mejora.
    /// </summary>
    /// <remarks>
    /// Contra <see cref="UniquesCrafted"/> dice si los Oro se están convirtiendo
    /// en build o solo acumulando: ocho Oro y cero Únicos quiere decir que la
    /// fusión existe y no se juega.
    /// </remarks>
    public int GoldPerksObtained { get; set; }

    public int UniquesCrafted { get; set; }

    /// <summary>
    /// Cuántas mejoras se hicieron: Bronce → Plata y Plata → Oro juntas.
    /// </summary>
    /// <remarks>
    /// Es la métrica que dice si la mochila dejó de ser un basurero. En cero
    /// quiere decir que el jugador sigue tirando drops o que la receta pide
    /// demasiado; muy alta, que el Bronce cae de más.
    /// </remarks>
    public int PerksUpgraded { get; set; }

    /// <summary>
    /// Cuántos de los Oro de la corrida salieron de mejorar, no de un jefe.
    /// </summary>
    /// <remarks>
    /// ⚠ Contra <see cref="GoldPerksObtained"/> dice si el tope
    /// —<c>maxCraftedGoldPerRun</c>— sigue haciendo su trabajo. Si esta pasa a
    /// ser la mitad de la otra, la mejora dejó de ser la salida de las sobras y
    /// se volvió el camino principal al Oro, que es exactamente lo que el tope
    /// existe para evitar.
    /// </remarks>
    public int GoldFromUpgrades { get; set; }

    /// <summary>
    /// En qué oleada se fabricó el primer Único, o 0 si nunca.
    /// </summary>
    /// <remarks>
    /// Es la métrica que dice si la composición es alcanzable. Un primer Único en
    /// la oleada 90 quiere decir que el sistema existe pero no llega a jugarse; en
    /// la 20, que llega demasiado fácil.
    /// </remarks>
    public int WaveOfFirstUnique { get; set; }

    // -------------------------------------------------------------------------
    //  La eficiencia del combate
    // -------------------------------------------------------------------------
    //
    //  ⚠ Todo en la escala INTERNA: centésimas de punto. Mostrarlo crudo dice
    //  «1.040.000 de daño» cuando fueron 10.400.

    /// <summary>Daño directo por clase, indexado por <see cref="DefenderClass"/>.</summary>
    public long[] DamageByClass { get; } = new long[9];

    /// <summary>Muertes por clase. La atribución es del golpe que mató.</summary>
    public int[] KillsByClass { get; } = new int[9];

    /// <summary>
    /// Daño secundario: splash, cadena, rebote, eco, DoT y overkill.
    /// </summary>
    /// <remarks>
    /// Si esto se acerca al daño directo total, los comportamientos dejaron de ser
    /// condimento y pasaron a ser la fuente de daño — que es el síntoma de que un
    /// tope está mal puesto.
    /// </remarks>
    public long SecondaryDamage { get; set; }

    /// <summary>Daño desperdiciado en enemigos que ya estaban muertos.</summary>
    public long OverkillDamage { get; set; }

    /// <summary>Lo que la Torre aguantó en toda la corrida.</summary>
    public long TowerDamageTaken { get; set; }

    // -------------------------------------------------------------------------

    /// <summary>Account una compra de perk en el casillero de su tier.</summary>
    public void CountPurchase(PerkTier tier, int precio)
    {
        switch (tier)
        {
            case PerkTier.Bronze:
                BoughtBronze++;
                GoldSpentBronze += precio;
                break;

            case PerkTier.Silver:
                BoughtSilver++;
                GoldSpentSilver += precio;
                break;

            default:
                BoughtGold++;
                GoldSpentGoldPerks += precio;
                GoldPerksObtained++;
                break;
        }
    }

    /// <summary>Account las ofertas de un stock recién armado.</summary>
    public void CountOffers(IReadOnlyList<ShopOffer> stock, BalanceData datos)
    {
        ShopsVisited++;

        foreach (var o in stock)
        {
            switch (o.Kind)
            {
                case ShopKind.Class:
                    OffersClass++;
                    break;

                case ShopKind.Weapon:
                    OffersWeapon++;
                    break;

                case ShopKind.Blueprint:
                    OffersBlueprint++;
                    break;

                default:
                    switch (datos.Perks.Get(o.Id).Tier)
                    {
                        case PerkTier.Bronze: OffersBronze++; break;
                        case PerkTier.Silver: OffersSilver++; break;
                        default: OffersGold++; break;
                    }

                    break;
            }
        }
    }
}
