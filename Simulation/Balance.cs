namespace TowerOfArgentum.Simulation;

/// <summary>
/// Cómo se arma la receta de una oleada a partir de los datos de la temporada.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Acá ya no hay números.</b> Antes esta clase era una pila de
/// constantes; ahora todo eso vive en <c>Client\Data\*.json</c> y llega por
/// <see cref="DatosBalance"/>. Lo que queda son las <b>fórmulas</b>: cómo se
/// combinan esos números, que es código y no configuración.</para>
///
/// <para>La distinción importa. Un número de balance lo cambia el que balancea,
/// sin compilar y sin saber C#; una fórmula la cambia el que programa, y cambiarla
/// es un cambio de reglas. Mezclarlas es lo que hace que nadie se anime a tocar
/// ninguna de las dos.</para>
///
/// <para>⚠ Cambiar un número de esos JSON vuelve irreplicables las corridas
/// viejas: la misma seed y el mismo log dan otro resultado. Por eso cada
/// rebalanceo es una <b>temporada</b> del ranking (DATOS.md §7).</para>
/// </remarks>
public static class Balance
{
    /// <summary>
    /// La receta de una oleada.
    /// </summary>
    /// <remarks>
    /// <para><b>La curva del presupuesto es cuadrática.</b> Lineal es aburrido
    /// —la oleada 80 se siente igual que la 40— y exponencial se descontrola: a
    /// la oleada 100 el número deja de tener sentido y no hay defensa que
    /// alcance.</para>
    ///
    /// <para>⚠ Encima de la cuadrática va el <b>tope por pool</b>: la curva cruza
    /// muy pronto lo que <c>maxNpcs</c> bichos pueden absorber, y todo el exceso
    /// se volvía HP —un muro que crecía sin techo—. El tope es «lo que cuestan
    /// <c>capEq</c> bichos promedio del pool», con <c>capEq</c> subiendo lineal
    /// dentro de cada área: la dificultad sigue subiendo, pero atada a lo que el
    /// área vende.</para>
    /// </remarks>
    public static WaveRecipe RecipeOf(int oleada, BalanceData datos)
    {
        var c = datos.Curves;
        int area = datos.Areas.OfWave(oleada);
        int presupuesto = c.Budget.Uncapped(oleada);

        var permitidos = AllowedIn(oleada, area, datos);
        int tope = PoolCap(oleada, area, permitidos, datos);

        if (tope >= 0 && tope < presupuesto)
            presupuesto = tope;

        // El jefe cierra el área. Son ocho por corrida, y como el jefe es la
        // única fuente de perks de Oro, eso le pone techo a cuántos puede juntar
        // un jugador — que es lo que hace que fusionar sea una decisión.
        int jefe = -1;

        if (datos.Areas.HasBoss(oleada))
            jefe = datos.BossOf(area)?.Id ?? -1;

        return new WaveRecipe
        {
            Number = oleada,
            Budget = presupuesto,
            Allowed = permitidos,
            BossTypeId = jefe,
            NpcPerkChance = c.PerWave.NpcPerkChance(oleada),
            SpawnIntervalTicks = c.PerWave.SpawnInterval(oleada),
            GoldBonus = c.PerWave.ClearGoldBonus.At(oleada),
        };
    }

    /// <summary>
    /// El techo del presupuesto: lo que cuestan <c>capEq</c> bichos promedio del
    /// pool de esa área. Devuelve <c>-1</c> si el pool no tiene ningún bicho
    /// comprable, y entonces no hay tope que aplicar.
    /// </summary>
    private static int PoolCap(
        int oleada, int area, (int TypeId, int Weight)[] permitidos, BalanceData datos)
    {
        long sumaPesoCosto = 0;
        long sumaPeso = 0;

        foreach (var (tipoId, peso) in permitidos)
        {
            int costo = WaveBuilder.Cost(tipoId, datos);

            if (costo == int.MaxValue)
                continue;

            sumaPesoCosto += (long)peso * costo;
            sumaPeso += peso;
        }

        if (sumaPeso <= 0)
            return -1;

        var p = datos.Curves.Budget;

        int primera = datos.Areas.FirstWaveOf(area);
        int oleadasDelArea = datos.Areas.LastWaveOf(area) - primera + 1;
        int w = oleada - primera + 1;
        int rampa = p.EquivalentCapMax - p.EquivalentCapMin;

        // Sube lineal de min a max dentro del área, con redondeo a mitad para
        // arriba y sin punto flotante (la simulación es entera; JUGABILIDAD.md §4).
        int capEq = oleadasDelArea <= 1
            ? p.EquivalentCapMax
            : p.EquivalentCapMin + ((((w - 1) * rampa * 2) + (oleadasDelArea - 1)) / (2 * (oleadasDelArea - 1)));

        long tope = sumaPesoCosto * capEq / sumaPeso;

        return tope > int.MaxValue ? int.MaxValue : (int)tope;
    }

    /// <summary>
    /// Los enemigos que pueden salir en una oleada, con su peso.
    /// </summary>
    /// <remarks>
    /// <para>Se toman del área actual <b>y de la anterior</b>. Que los bichos de
    /// la zona previa sigan apareciendo un rato hace dos cosas: suaviza el salto
    /// entre áreas —que si no se siente como un muro— y le da al jugador la
    /// satisfacción de ver cómo lo que antes lo mataba ahora cae de un golpe.</para>
    ///
    /// <para>⚠ El peso del área anterior <b>se desvanece</b> a lo largo de las
    /// primeras oleadas del área, no es constante. Con peso fijo el pool cambiaba
    /// de composición de una oleada a la otra, y como el tope del presupuesto es
    /// el costo medio de ese pool, el presupuesto saltaba entre +19% y +56% en el
    /// borde —el equivalente a diez o veinte oleadas de crecimiento normal,
    /// entregadas de una—. Medido: dos tercios de las corridas del bot morían
    /// exactamente en la primera oleada de un área.</para>
    ///
    /// <para>El desvanecido arregla el número y lo que se ve al mismo tiempo: la
    /// primera oleada del área nueva trae sobre todo bichos conocidos y unos
    /// pocos nuevos, que es cómo se presenta un área.</para>
    /// </remarks>
    private static (int TypeId, int Weight)[] AllowedIn(int oleada, int area, BalanceData datos)
    {
        var actual = datos.InArea(area);
        var anterior = area > 1 ? datos.InArea(area - 1) : [];

        var lista = new List<(int, int)>(actual.Count + anterior.Count);

        foreach (var e in actual)
            lista.Add((e.Id, datos.Curves.Wave.CurrentAreaWeight));

        int pesoAnterior = PreviousWeightAt(oleada, area, datos);

        foreach (var e in anterior)
            lista.Add((e.Id, pesoAnterior));

        // ⚠ Sin bestiario no hay oleada posible. Antes acá había un lobo
        // provisional escrito en el código; ahora que los datos son obligatorios,
        // un pool vacío es un error de datos y hay que decirlo, no taparlo con un
        // bicho inventado que hace que la corrida parezca funcionar.
        if (lista.Count == 0)
        {
            throw new InvalidBalanceDataException(
                $"el área {area} no tiene ningún enemigo en enemigos.json. " +
                "Corré el importador:  --importar-npcs");
        }

        return [.. lista];
    }

    /// <summary>
    /// Cuánto pesa el bestiario del área anterior en esta oleada: cae lineal de
    /// <c>previousAreaWeightFirst</c> a <c>previousAreaWeight</c> a lo largo de
    /// las primeras <c>previousAreaFadeWaves</c> oleadas del área.
    /// </summary>
    private static int PreviousWeightAt(int oleada, int area, BalanceData datos)
    {
        var p = datos.Curves.Wave;

        if (p.PreviousAreaFadeWaves <= 1)
            return p.PreviousAreaWeight;

        // Posición dentro del área, 1 en la primera oleada.
        int w = oleada - datos.Areas.FirstWaveOf(area) + 1;

        if (w >= p.PreviousAreaFadeWaves)
            return p.PreviousAreaWeight;

        int caida = p.PreviousAreaWeightFirst - p.PreviousAreaWeight;

        // Entero y sin punto flotante (JUGABILIDAD.md §4). Redondeo a mitad para
        // arriba, igual que la rampa de capEq.
        int bajado = (((w - 1) * caida * 2) + (p.PreviousAreaFadeWaves - 1))
                     / (2 * (p.PreviousAreaFadeWaves - 1));

        return p.PreviousAreaWeightFirst - bajado;
    }
}
