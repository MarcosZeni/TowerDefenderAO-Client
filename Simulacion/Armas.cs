namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Lo que una habilidad de arma le hace al disparo.
/// </summary>
/// <remarks>
/// <para>⚠ Estas <b>no son</b> los <see cref="EfectoPerk"/>. Un perk es una
/// decisión de construcción: se elige, se equipa y ocupa un slot. Una habilidad
/// de arma <b>viene con el arma</b>: la tirás o no la tirás.</para>
///
/// <para>Y por eso pueden ser más raras. Un perk que rompe el juego arruina la
/// progresión, porque el jugador lo va a buscar siempre; un arma que rompe el
/// juego es un hallazgo, y la corrida siguiente no lo tiene.</para>
/// </remarks>
public enum HabilidadArma
{
    Ninguna = 0,

    // --- Comunes: tocan un número ---

    /// <summary>El golpe pega también alrededor del objetivo.</summary>
    Splash = 1,

    /// <summary>El disparo rebota a otro enemigo.</summary>
    Rebote = 2,

    /// <summary>El objetivo camina más lento.</summary>
    Escarcha = 3,

    /// <summary>Daño por tick durante un rato.</summary>
    Veneno = 4,

    // --- Raras: cambian cómo funciona el defensor ---

    /// <summary>Cada N golpes, uno pega muchísimo más.</summary>
    Critico = 10,

    /// <summary>Cuanto más lejos el objetivo, más daño.</summary>
    Francotirador = 11,

    /// <summary>Cuanto más cerca, más daño. Lo opuesto.</summary>
    Carnicero = 12,

    /// <summary>Cada enemigo que mata sube el daño, hasta un techo. Se resetea al terminar la oleada.</summary>
    Sed = 13,

    /// <summary>Dispara dos veces por cada cooldown.</summary>
    Doble = 14,

    // --- Que rompen el juego: son el premio de una corrida ---

    /// <summary>
    /// El daño ignora todo y mata de un golpe a lo que esté por debajo de un
    /// porcentaje de vida.
    /// </summary>
    Ejecutar = 20,

    /// <summary>Le pega a TODOS los enemigos en alcance, no a uno.</summary>
    Cadena = 21,

    /// <summary>El oro que suelta lo que mata se multiplica.</summary>
    Codicia = 22,

    /// <summary>Cada golpe le devuelve vida a la Torre.</summary>
    Vampirismo = 23,
}

/// <summary>Cuán raro es un arma. Decide cuántos afijos lleva y de qué clase.</summary>
public enum RarezaArma
{
    Comun = 0,
    Rara = 1,
    Epica = 2,

    /// <summary>Una por corrida, si hay suerte. Trae una habilidad que rompe.</summary>
    Legendaria = 3,
}

/// <summary>
/// Un arma. <b>El único ítem con stats del juego.</b>
/// </summary>
/// <remarks>
/// <para>⚠ Armadura, casco y escudo <b>no dan nada</b>: son el frame que cambia
/// cuando la clase sube de nivel. Todo el poder del equipo vive acá.</para>
///
/// <para>Concentrar los stats en una sola pieza hace dos cosas. La decisión de
/// equipar se vuelve nítida —hay un solo lugar, y lo que entra saca lo que
/// estaba— y las armas pueden ser mucho más locas sin desbalancear: si hubiera
/// cuatro piezas con habilidades, habría que pensar cada combinación de cuatro.</para>
/// </remarks>
public sealed class Arma
{
    public required int Id { get; init; }

    public required string Nombre { get; init; }

    public required RarezaArma Rareza { get; init; }

    /// <summary>Qué gráfico usa. Índice en el catálogo de armas del arte.</summary>
    public required int GraficoId { get; init; }

    // --- Números, en puntos porcentuales sobre la base de la clase ---

    public int Dano { get; init; }

    public int Cadencia { get; init; }

    public int Alcance { get; init; }

    // --- La habilidad, si tiene ---

    public HabilidadArma Habilidad { get; init; }

    /// <summary>Cuánto pega la habilidad, en porcentaje del daño del golpe.</summary>
    public int Magnitud { get; init; }

    /// <summary>
    /// Qué clases la pueden usar. Vacío = cualquiera.
    /// </summary>
    /// <remarks>
    /// Un arco en manos de un guerrero no tiene sentido, y un arma que solo sirve
    /// para una clase que no está en el roster es un lugar de mochila tirado. El
    /// generador filtra por las clases que el jugador tiene.
    /// </remarks>
    public Clase[] Clases { get; init; } = [];

    /// <summary>Lo que paga la tienda. Las armas se venden.</summary>
    public required int Valor { get; init; }

    public bool SirveA(Clase clase) => Clases.Length == 0 || System.Array.IndexOf(Clases, clase) >= 0;
}

/// <summary>
/// Genera armas: base, afijos y —si hay suerte— una habilidad.
/// </summary>
/// <remarks>
/// <para>Es el cuarto uso del mismo patrón de presupuesto —oleada, drop, tienda y
/// ahora arma— y por el mismo motivo: es la única forma de que el contenido
/// generado tenga un techo conocido.</para>
///
/// <para>La diferencia con el generador viejo es qué se compra con ese
/// presupuesto. Antes eran solo números; ahora una parte puede irse en una
/// <b>habilidad</b>, y ahí está la gracia: dos armas del mismo presupuesto pueden
/// ser una con números altos y ninguna habilidad, u otra floja en números con
/// algo que cambia cómo juega el defensor.</para>
/// </remarks>
public static class GeneradorArmas
{
    /// <summary>
    /// Genera un arma con ese presupuesto.
    /// </summary>
    /// <param name="presupuesto">Sube con la oleada: un arma tardía es mejor.</param>
    /// <param name="clases">Las del roster. El arma va a servir para alguna.</param>
    /// <param name="tabla">
    /// Los parámetros del generador, de <c>generador-armas.json</c>. Entran por
    /// parámetro y no de una constante porque son entrada de la simulación: el
    /// servidor arma la misma arma con la misma seed al revalidar.
    /// </param>
    public static Arma Generar(
        int presupuesto, IReadOnlyList<Clase> clases, Aleatorio rng, int idNuevo, TablaArmas tabla)
    {
        var rareza = TirarRareza(presupuesto, rng, tabla);

        // ⚠ La rareza se decide ANTES de gastar, y de ella sale cuánto se va en
        // habilidad. Al revés —gastar en números y ver qué sobra— las legendarias
        // saldrían siempre con números flojos, porque la habilidad se comería el
        // resto.
        int paraHabilidad = presupuesto * tabla.PorcentajeAHabilidad(rareza) / 100;

        var habilidad = HabilidadArma.Ninguna;
        int magnitud = 0;

        if (paraHabilidad > 0)
        {
            habilidad = TirarHabilidad(rareza, rng, tabla);
            magnitud = tabla.MagnitudDe(habilidad, paraHabilidad);
        }

        int restante = presupuesto - paraHabilidad;
        int dano = 0, cadencia = 0, alcance = 0;

        int paso = tabla.Paso;
        int costoDano = tabla.CostoDano * paso;
        int costoCadencia = tabla.CostoCadencia * paso;
        int costoAlcance = tabla.CostoAlcance * paso;

        while (restante >= costoDano)
        {
            int cual = rng.Rango(0, 3);

            if (cual == 0)
            {
                dano += paso;
                restante -= costoDano;
            }
            else if (cual == 1 && restante >= costoCadencia)
            {
                cadencia += paso;
                restante -= costoCadencia;
            }
            else if (restante >= costoAlcance)
            {
                alcance += paso;
                restante -= costoAlcance;
            }
            else
            {
                // Lo que queda no alcanza para el afijo que salió. Se corta en vez
                // de reintentar: reintentar hasta gastar el último punto haría que
                // todas las armas terminaran con el afijo más barato.
                break;
            }
        }

        // La clase sale del roster, así que el arma siempre le sirve a alguien. Un
        // arma para una clase que el jugador no tiene es un lugar de mochila
        // tirado a la basura.
        Clase[] paraQuien = clases.Count == 0
            ? []
            : [clases[rng.Indice(clases.Count)]];

        return new Arma
        {
            Id = idNuevo,
            Nombre = Nombrar(rareza, habilidad, dano, cadencia, alcance, tabla),
            Rareza = rareza,
            GraficoId = 0,
            Dano = dano,
            Cadencia = cadencia,
            Alcance = alcance,
            Habilidad = habilidad,
            Magnitud = magnitud,
            Clases = paraQuien,
            Valor = presupuesto / tabla.DivisorValor * (1 + (int)rareza),
        };
    }

    /// <summary>
    /// Tira la rareza.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Las tiradas están <b>encadenadas</b>, no son exclusivas: primero
    /// legendaria, si falla épica, si falla rara. La probabilidad efectiva de las
    /// de abajo es menor que su número.</para>
    ///
    /// <para>La chance de legendaria sube con el presupuesto, o sea con la oleada.
    /// Es a propósito: en la oleada 10 una legendaria arruinaría el resto de la
    /// corrida, y en la 90 es el premio por haber llegado. Aun así tiene techo:
    /// una legendaria tiene que seguir siendo un hallazgo. Si saliera seguido,
    /// dejaría de romper el juego y pasaría a ser el juego.</para>
    /// </remarks>
    private static RarezaArma TirarRareza(int presupuesto, Aleatorio rng, TablaArmas tabla)
    {
        if (rng.Ocurre(tabla.ChanceLegendaria.En(presupuesto))) return RarezaArma.Legendaria;
        if (rng.Ocurre(tabla.ChanceEpica.En(presupuesto))) return RarezaArma.Epica;
        if (rng.Ocurre(tabla.ChanceRara.En(presupuesto))) return RarezaArma.Rara;

        return RarezaArma.Comun;
    }

    /// <summary>
    /// Elige la habilidad según la rareza.
    /// </summary>
    /// <remarks>
    /// Las que rompen el juego <b>solo salen en legendarias</b>. Es lo que las
    /// hace valer: si <c>Cadena</c> pudiera aparecer en una rara, el jugador la
    /// tendría en la oleada 15 y el resto de la corrida sería una formalidad.
    /// </remarks>
    public static HabilidadArma TirarHabilidad(RarezaArma rareza, Aleatorio rng, TablaArmas tabla)
    {
        var pool = tabla.PoolDe(rareza);
        return pool[rng.Indice(pool.Length)];
    }

    /// <summary>
    /// El nombre, armado de lo que el arma realmente tiene.
    /// </summary>
    /// <remarks>
    /// Sale de los números y no al revés, así que un arma generada nunca promete
    /// algo que no hace. Los nombres de las habilidades salen de los 41 hechizos
    /// reales de <c>Hechizos.dat</c>.
    /// </remarks>
    private static string Nombrar(
        RarezaArma rareza, HabilidadArma habilidad, int dano, int cadencia, int alcance, TablaArmas tabla)
    {
        string baseNombre =
            dano >= cadencia && dano >= alcance ? tabla.NombreDano :
            cadencia >= alcance ? tabla.NombreCadencia : tabla.NombreAlcance;

        if (habilidad == HabilidadArma.Ninguna)
            return rareza == RarezaArma.Comun ? baseNombre : $"{baseNombre} {tabla.SufijoDe(rareza)}";

        return $"{baseNombre} {tabla.NombreDeHabilidad(habilidad)}";
    }
}
