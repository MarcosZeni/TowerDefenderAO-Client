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
    /// <summary>Cuánto cuesta un punto porcentual de cada número.</summary>
    private const int CostoDano = 2;

    private const int CostoCadencia = 3;

    private const int CostoAlcance = 4;

    /// <summary>
    /// Genera un arma con ese presupuesto.
    /// </summary>
    /// <param name="presupuesto">Sube con la oleada: un arma tardía es mejor.</param>
    /// <param name="clases">Las del roster. El arma va a servir para alguna.</param>
    public static Arma Generar(int presupuesto, IReadOnlyList<Clase> clases, Aleatorio rng, int idNuevo)
    {
        var rareza = TirarRareza(presupuesto, rng);

        // ⚠ La rareza se decide ANTES de gastar, y de ella sale cuánto se va en
        // habilidad. Al revés —gastar en números y ver qué sobra— las legendarias
        // saldrían siempre con números flojos, porque la habilidad se comería el
        // resto.
        int paraHabilidad = rareza switch
        {
            RarezaArma.Legendaria => presupuesto * 60 / 100,
            RarezaArma.Epica => presupuesto * 40 / 100,
            RarezaArma.Rara => presupuesto * 25 / 100,
            _ => 0,
        };

        var habilidad = HabilidadArma.Ninguna;
        int magnitud = 0;

        if (paraHabilidad > 0)
        {
            habilidad = TirarHabilidad(rareza, rng);
            magnitud = MagnitudDe(habilidad, paraHabilidad);
        }

        int restante = presupuesto - paraHabilidad;
        int dano = 0, cadencia = 0, alcance = 0;

        while (restante >= CostoDano * 5)
        {
            int cual = rng.Rango(0, 3);

            if (cual == 0)
            {
                dano += 5;
                restante -= CostoDano * 5;
            }
            else if (cual == 1 && restante >= CostoCadencia * 5)
            {
                cadencia += 5;
                restante -= CostoCadencia * 5;
            }
            else if (restante >= CostoAlcance * 5)
            {
                alcance += 5;
                restante -= CostoAlcance * 5;
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
            Nombre = Nombrar(rareza, habilidad, dano, cadencia, alcance),
            Rareza = rareza,
            GraficoId = 0,
            Dano = dano,
            Cadencia = cadencia,
            Alcance = alcance,
            Habilidad = habilidad,
            Magnitud = magnitud,
            Clases = paraQuien,
            Valor = presupuesto / 2 * (1 + (int)rareza),
        };
    }

    /// <summary>
    /// Tira la rareza.
    /// </summary>
    /// <remarks>
    /// <para>La chance de legendaria sube con el presupuesto, o sea con la oleada.
    /// Es a propósito: en la oleada 10 una legendaria arruinaría el resto de la
    /// corrida, y en la 90 es el premio por haber llegado.</para>
    ///
    /// <para>Aun así el techo es 4%: una legendaria tiene que seguir siendo un
    /// hallazgo. Si saliera seguido, dejaría de romper el juego y pasaría a ser
    /// el juego.</para>
    /// </remarks>
    private static RarezaArma TirarRareza(int presupuesto, Aleatorio rng)
    {
        int chanceLegendaria = Enteros.Limitar(presupuesto / 200, 0, 4);
        int chanceEpica = Enteros.Limitar(presupuesto / 60, 2, 12);
        int chanceRara = Enteros.Limitar(presupuesto / 20, 10, 35);

        if (rng.Ocurre(chanceLegendaria)) return RarezaArma.Legendaria;
        if (rng.Ocurre(chanceEpica)) return RarezaArma.Epica;
        if (rng.Ocurre(chanceRara)) return RarezaArma.Rara;

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
    private static HabilidadArma TirarHabilidad(RarezaArma rareza, Aleatorio rng)
    {
        HabilidadArma[] comunes =
        [
            HabilidadArma.Splash, HabilidadArma.Rebote,
            HabilidadArma.Escarcha, HabilidadArma.Veneno,
        ];

        HabilidadArma[] raras =
        [
            HabilidadArma.Critico, HabilidadArma.Francotirador,
            HabilidadArma.Carnicero, HabilidadArma.Sed, HabilidadArma.Doble,
        ];

        HabilidadArma[] rompen =
        [
            HabilidadArma.Ejecutar, HabilidadArma.Cadena,
            HabilidadArma.Codicia, HabilidadArma.Vampirismo,
        ];

        return rareza switch
        {
            RarezaArma.Legendaria => rompen[rng.Indice(rompen.Length)],
            RarezaArma.Epica => raras[rng.Indice(raras.Length)],
            _ => comunes[rng.Indice(comunes.Length)],
        };
    }

    /// <summary>
    /// Cuánto pega la habilidad, con el presupuesto que le tocó.
    /// </summary>
    /// <remarks>
    /// Cada una tiene su propia escala porque significan cosas distintas: un 40%
    /// de splash es un porcentaje del daño, un 40% de ejecutar sería un umbral de
    /// vida que mata todo. Los topes están para que ninguna se vuelva absoluta.
    /// </remarks>
    private static int MagnitudDe(HabilidadArma habilidad, int presupuesto) => habilidad switch
    {
        HabilidadArma.Splash => Enteros.Limitar(20 + (presupuesto / 4), 20, 90),
        HabilidadArma.Rebote => Enteros.Limitar(25 + (presupuesto / 4), 25, 100),
        HabilidadArma.Escarcha => Enteros.Limitar(10 + (presupuesto / 8), 10, 45),
        HabilidadArma.Veneno => Enteros.Limitar(15 + (presupuesto / 5), 15, 80),

        HabilidadArma.Critico => Enteros.Limitar(120 + (presupuesto / 2), 120, 400),
        HabilidadArma.Francotirador => Enteros.Limitar(15 + (presupuesto / 6), 15, 60),
        HabilidadArma.Carnicero => Enteros.Limitar(20 + (presupuesto / 5), 20, 80),
        HabilidadArma.Sed => Enteros.Limitar(2 + (presupuesto / 40), 2, 15),
        HabilidadArma.Doble => 100,

        // ⚠ El umbral de ejecutar tiene techo bajo a propósito. Al 25% ya mata
        // casi todo lo que sobrevivió a un golpe; al 50% haría irrelevante el
        // daño del arma y la única estadística sería la cadencia.
        HabilidadArma.Ejecutar => Enteros.Limitar(8 + (presupuesto / 40), 8, 25),

        HabilidadArma.Cadena => Enteros.Limitar(30 + (presupuesto / 8), 30, 70),
        HabilidadArma.Codicia => Enteros.Limitar(50 + (presupuesto / 6), 50, 200),
        HabilidadArma.Vampirismo => Enteros.Limitar(1 + (presupuesto / 50), 1, 8),

        _ => 0,
    };

    /// <summary>
    /// El nombre, armado de lo que el arma realmente tiene.
    /// </summary>
    /// <remarks>
    /// Sale de los números y no al revés, así que un arma generada nunca promete
    /// algo que no hace. Los nombres de las habilidades salen de los 41 hechizos
    /// reales de <c>Hechizos.dat</c>.
    /// </remarks>
    private static string Nombrar(
        RarezaArma rareza, HabilidadArma habilidad, int dano, int cadencia, int alcance)
    {
        string baseNombre =
            dano >= cadencia && dano >= alcance ? "Arma afilada" :
            cadencia >= alcance ? "Arma ágil" : "Arma certera";

        if (habilidad == HabilidadArma.Ninguna)
            return rareza == RarezaArma.Comun ? baseNombre : $"{baseNombre} {SufijoRareza(rareza)}";

        return $"{baseNombre} {NombreDeHabilidad(habilidad)}";
    }

    private static string SufijoRareza(RarezaArma r) => r switch
    {
        RarezaArma.Rara => "templada",
        RarezaArma.Epica => "rúnica",
        RarezaArma.Legendaria => "legendaria",
        _ => "",
    };

    public static string NombreDeHabilidad(HabilidadArma h) => h switch
    {
        HabilidadArma.Splash => "de Tormenta de Fuego",
        HabilidadArma.Rebote => "de Descarga Eléctrica",
        HabilidadArma.Escarcha => "de Inmovilizar",
        HabilidadArma.Veneno => "de Envenenar",

        HabilidadArma.Critico => "de Fuerza",
        HabilidadArma.Francotirador => "de Flecha Mágica",
        HabilidadArma.Carnicero => "de Terrible Hambre",
        HabilidadArma.Sed => "de Ira de Dios",
        HabilidadArma.Doble => "de Celeridad",

        HabilidadArma.Ejecutar => "de Apocalipsis",
        HabilidadArma.Cadena => "de Tormenta Divina",
        HabilidadArma.Codicia => "de Habilidad Ilimitada",
        HabilidadArma.Vampirismo => "de Curación Celestial",

        _ => "",
    };

    /// <summary>Qué hace la habilidad, en una línea, para la UI.</summary>
    public static string ExplicarHabilidad(HabilidadArma h, int magnitud) => h switch
    {
        HabilidadArma.Splash => $"daña a los que están cerca, {magnitud}% del golpe",
        HabilidadArma.Rebote => $"rebota a otro enemigo, {magnitud}% del golpe",
        HabilidadArma.Escarcha => $"deja al objetivo {magnitud}% más lento",
        HabilidadArma.Veneno => $"envenena por {magnitud}% del golpe",

        HabilidadArma.Critico => $"cada 7 golpes, uno pega {magnitud}% más",
        HabilidadArma.Francotirador => $"hasta {magnitud}% más daño según la distancia",
        HabilidadArma.Carnicero => $"hasta {magnitud}% más daño de cerca",
        HabilidadArma.Sed => $"+{magnitud}% de daño por muerte, se reinicia cada oleada",
        HabilidadArma.Doble => "dispara dos veces",

        HabilidadArma.Ejecutar => $"MATA de un golpe por debajo del {magnitud}% de vida",
        HabilidadArma.Cadena => $"le pega a TODOS los que tenga en alcance, {magnitud}% del golpe",
        HabilidadArma.Codicia => $"+{magnitud}% de oro por cada enemigo que mata",
        HabilidadArma.Vampirismo => $"cura {magnitud} de vida a la Torre por golpe",

        _ => "",
    };
}
