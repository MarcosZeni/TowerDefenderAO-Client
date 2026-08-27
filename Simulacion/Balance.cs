namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Cómo se arma la receta de una oleada a partir de los datos de la temporada.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Acá ya no hay números.</b> Antes esta clase era una pila de
/// constantes; ahora todo eso vive en <c>Client\Datos\*.json</c> y llega por
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
    public static RecetaOleada RecetaDe(int oleada, DatosBalance datos)
    {
        var c = datos.Curvas;
        int area = datos.Areas.DeOleada(oleada);
        int presupuesto = c.Presupuesto.SinTope(oleada);

        var permitidos = PermitidosDe(area, datos);
        int tope = TopePorPool(oleada, area, permitidos, datos);

        if (tope >= 0 && tope < presupuesto)
            presupuesto = tope;

        // El jefe cierra el área. Son ocho por corrida, y como el jefe es la
        // única fuente de perks de Oro, eso le pone techo a cuántos puede juntar
        // un jugador — que es lo que hace que fusionar sea una decisión.
        int jefe = -1;

        if (datos.Areas.TieneJefe(oleada))
            jefe = datos.JefeDe(area)?.Id ?? -1;

        return new RecetaOleada
        {
            Numero = oleada,
            Presupuesto = presupuesto,
            Permitidos = permitidos,
            JefeTipoId = jefe,
            ChancePerkNpc = c.PorOleada.ChancePerkNpc(oleada),
            IntervaloSpawnTicks = c.PorOleada.IntervaloSpawn(oleada),
            BonusOro = c.PorOleada.BonusOroLimpiar.En(oleada),
        };
    }

    /// <summary>
    /// El techo del presupuesto: lo que cuestan <c>capEq</c> bichos promedio del
    /// pool de esa área. Devuelve <c>-1</c> si el pool no tiene ningún bicho
    /// comprable, y entonces no hay tope que aplicar.
    /// </summary>
    private static int TopePorPool(
        int oleada, int area, (int TipoId, int Peso)[] permitidos, DatosBalance datos)
    {
        long sumaPesoCosto = 0;
        long sumaPeso = 0;

        foreach (var (tipoId, peso) in permitidos)
        {
            int costo = ArmadorOleadas.Costo(tipoId, datos);

            if (costo == int.MaxValue)
                continue;

            sumaPesoCosto += (long)peso * costo;
            sumaPeso += peso;
        }

        if (sumaPeso <= 0)
            return -1;

        var p = datos.Curvas.Presupuesto;

        int primera = datos.Areas.PrimeraOleadaDe(area);
        int oleadasDelArea = datos.Areas.UltimaOleadaDe(area) - primera + 1;
        int w = oleada - primera + 1;
        int rampa = p.CapEquivalenteMax - p.CapEquivalenteMin;

        // Sube lineal de min a max dentro del área, con redondeo a mitad para
        // arriba y sin punto flotante (la simulación es entera; JUGABILIDAD.md §4).
        int capEq = oleadasDelArea <= 1
            ? p.CapEquivalenteMax
            : p.CapEquivalenteMin + ((((w - 1) * rampa * 2) + (oleadasDelArea - 1)) / (2 * (oleadasDelArea - 1)));

        long tope = sumaPesoCosto * capEq / sumaPeso;

        return tope > int.MaxValue ? int.MaxValue : (int)tope;
    }

    /// <summary>
    /// Los enemigos que pueden salir en un área, con su peso.
    /// </summary>
    /// <remarks>
    /// <para>Se toman del área actual <b>y de la anterior</b>. Que los bichos de
    /// la zona previa sigan apareciendo un rato hace dos cosas: suaviza el salto
    /// entre áreas —que si no se siente como un muro— y le da al jugador la
    /// satisfacción de ver cómo lo que antes lo mataba ahora cae de un golpe.</para>
    ///
    /// <para>Los del área anterior pesan menos, así que el área nueva domina la
    /// composición sin que la vieja desaparezca de golpe.</para>
    /// </remarks>
    private static (int TipoId, int Peso)[] PermitidosDe(int area, DatosBalance datos)
    {
        var actual = datos.DeArea(area);
        var anterior = area > 1 ? datos.DeArea(area - 1) : [];

        var lista = new List<(int, int)>(actual.Count + anterior.Count);

        foreach (var e in actual)
            lista.Add((e.Id, datos.Curvas.Oleada.PesoAreaActual));

        foreach (var e in anterior)
            lista.Add((e.Id, datos.Curvas.Oleada.PesoAreaAnterior));

        // ⚠ Sin bestiario no hay oleada posible. Antes acá había un lobo
        // provisional escrito en el código; ahora que los datos son obligatorios,
        // un pool vacío es un error de datos y hay que decirlo, no taparlo con un
        // bicho inventado que hace que la corrida parezca funcionar.
        if (lista.Count == 0)
        {
            throw new DatosInvalidosException(
                $"el área {area} no tiene ningún enemigo en enemigos.json. " +
                "Corré el importador:  --importar-npcs");
        }

        return [.. lista];
    }
}
