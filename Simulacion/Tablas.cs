using System.Text.Json;

namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Un archivo de datos que no se pudo leer o que dice algo imposible.
/// </summary>
/// <remarks>
/// Excepción y no un valor por defecto silencioso: un número de balance que se
/// cae a un default es una corrida que mide otra cosa que las demás, y el
/// jugador no se entera hasta que el ranking no le cierra. <b>Mejor no arrancar
/// que arrancar midiendo mal.</b>
/// </remarks>
public sealed class DatosInvalidosException(string mensaje) : System.Exception(mensaje);

// =============================================================================
//  Las formas de las curvas
// =============================================================================

/// <summary>Una recta <c>base + porOleada × n</c>.</summary>
public readonly record struct CurvaLineal(int Base, int PorOleada)
{
    public int En(int oleada) => Base + (oleada * PorOleada);
}

/// <summary>
/// Una chance que sube con el presupuesto y está acotada:
/// <c>limitar(presupuesto / divisor, min, max)</c>.
/// </summary>
public readonly record struct RangoChance(int Divisor, int Min, int Max)
{
    public int En(int presupuesto)
        => Enteros.Limitar(Divisor == 0 ? Min : presupuesto / Divisor, Min, Max);
}

/// <summary>
/// La magnitud de una habilidad: <c>limitar(base + presupuesto / divisor, min, max)</c>.
/// </summary>
/// <remarks>
/// <c>divisor = 0</c> quiere decir valor fijo, no escala. Es el caso de Doble,
/// que dispara dos veces y no hay medio disparo.
/// </remarks>
public readonly record struct FormulaMagnitud(int Base, int Divisor, int Min, int Max)
{
    public int En(int presupuesto)
        => Enteros.Limitar(Divisor == 0 ? Base : Base + (presupuesto / Divisor), Min, Max);
}

// =============================================================================
//  curvas.json
// =============================================================================

public sealed record DatoTorre(int HpInicial, int Dano, int CadenciaTicks);

public sealed record DatoCorrida(
    int OleadasPorCorrida,
    int CantidadAreas,
    int[] UltimaOleadaPorArea,
    int OleadasEntreElecciones,
    int SegundosDePreparacion);

public sealed record DatoDefensores(
    int LugaresMochila,
    int SlotsEquipoPorClase,
    int PorcentajeVentaDefensor,
    int PorcentajeVentaArma);

public sealed record DatoEconomia(
    int FichasDeOroInicial,
    int OroPorEnemigoAdelantado,
    int TopeOroPorAdelantar);

public sealed record DatoPresupuesto(
    int Base,
    int Cuadratico,
    int CapEquivalenteMin,
    int CapEquivalenteMax)
{
    /// <summary>La cuadrática, antes del tope por pool.</summary>
    public int SinTope(int oleada) => Base + (Cuadratico * oleada * oleada);
}

public sealed record DatoCurvasPorOleada(
    CurvaLineal BonusOroLimpiar,
    CurvaLineal PresupuestoDropJefe,
    CurvaLineal PresupuestoItemTienda,
    int IntervaloBase,
    int IntervaloDivisor,
    int IntervaloMin,
    int IntervaloMax,
    int ChancePerkDivisor,
    int ChancePerkMin,
    int ChancePerkMax)
{
    public int IntervaloSpawn(int oleada) => Enteros.Limitar(
        IntervaloBase - (IntervaloDivisor == 0 ? 0 : oleada / IntervaloDivisor),
        IntervaloMin, IntervaloMax);

    public int ChancePerkNpc(int oleada) => Enteros.Limitar(
        ChancePerkDivisor == 0 ? 0 : oleada / ChancePerkDivisor,
        ChancePerkMin, ChancePerkMax);
}

public sealed record DatoTienda(
    int PerksEnStock,
    int ArmasEnStock,
    int ChanceBronce,
    CurvaLineal PrecioPerkBronce,
    CurvaLineal PrecioPerkPlata)
{
    public int PrecioDePerk(TierPerk tier, int oleada)
        => tier == TierPerk.Bronce ? PrecioPerkBronce.En(oleada) : PrecioPerkPlata.En(oleada);
}

public sealed record DatoDrops(int ChanceComun, int ChanceBronce);

public sealed record DatoOleada(
    int MaxNpcs,
    int PesoAreaActual,
    int PesoAreaAnterior,
    int DivisorHp,
    int DivisorDanoMax,
    int DivisorVelocidad,
    int IntervaloSpawnPorDefecto);

public sealed record DatoPerksNpc(
    int CostoVida,
    int CostoVelocidad,
    int EscalonVida,
    int EscalonVelocidad,
    int MaxVida,
    int MaxVelocidad,
    int ChanceVidaEnLaTirada,
    int ChanceVelocidadEnSobrante);

public sealed record DatoJefe(int DivisorVidaExtra, int MaxVidaExtra, string NombreJefeFinal);

/// <summary>Todo lo de <c>curvas.json</c>.</summary>
public sealed record DatoCurvas(
    DatoTorre Torre,
    DatoCorrida Corrida,
    DatoDefensores Defensores,
    DatoEconomia Economia,
    DatoPresupuesto Presupuesto,
    DatoCurvasPorOleada PorOleada,
    DatoTienda Tienda,
    DatoDrops Drops,
    DatoOleada Oleada,
    DatoPerksNpc PerksNpc,
    DatoJefe Jefe);

// =============================================================================
//  combate.json
// =============================================================================

/// <summary>
/// Los números del tick, los que antes eran literales dentro de
/// <see cref="Partida"/>.
/// </summary>
public sealed record DatoCombate(
    int DivisorPromedioDanoEnemigo,
    int DanoMinimoEnemigo,
    int TopeSlowPorciento,
    int DuracionSlowTicks,
    int VentanaSaltoRicochetCeldas,
    int RadioSplashCeldas,
    int RebotesRicochet,
    int RebotesPerforacion,
    int RebotesApocalipsis,
    int MaxSecundariosSplashOro,
    int MaxSecundariosOndaDeFrio,
    int MaxSecundariosDivino,
    int SlowTerribleHambre,
    int DivisorPenetrante,
    int GolpesPorCritico,
    int MaxSecundariosSplashArma,
    int RebotesReboteArma,
    int MaxSecundariosCadena,
    int TopeBonusSed,
    int TopePorcionAlcance,
    int EscalaPorcionAlcance,
    int UmbralEjecutarTropa,
    int UmbralEjecutarJefe,
    bool JefeFinalInmuneAEjecutar,
    int CooldownVampirismoTicks,
    int DuracionMarcaCodiciaTicks);

// =============================================================================
//  clases.json
// =============================================================================

/// <summary>Los stats base de una clase, a nivel 1 y sin nada equipado.</summary>
public sealed record DatoClase(
    Clase Clase,
    int Costo,
    int Dano,
    int AlcanceCeldas,
    int CadenciaTicks,
    bool Soporte);

/// <summary>El roster completo: las nueve clases y la curva de nivel.</summary>
public sealed class TablaClases
{
    private readonly DatoClase[] _todas;
    private readonly DatoClase?[] _porClase = new DatoClase?[9];

    public TablaClases(DatoClase[] todas, int extraDanoPorNivel, int slotsPerkPorClase)
    {
        _todas = todas;
        ExtraDanoPorNivel = extraDanoPorNivel;
        SlotsPerkPorClase = slotsPerkPorClase;

        foreach (var c in todas)
        {
            int i = (int)c.Clase;

            if (i >= 0 && i < _porClase.Length)
                _porClase[i] = c;
        }
    }

    public IReadOnlyList<DatoClase> Todas => _todas;

    /// <summary>Puntos porcentuales de daño por cada nivel arriba del 1.</summary>
    public int ExtraDanoPorNivel { get; }

    /// <summary>Slots de perk por clase, que es también el nivel máximo.</summary>
    public int SlotsPerkPorClase { get; }

    /// <summary>
    /// Los stats de una clase.
    /// </summary>
    /// <remarks>
    /// Tira si la clase no está en el archivo, en vez de devolver un default: una
    /// clase sin stats es una clase que pega lo que se le ocurra al fallback, y
    /// eso no se nota hasta que el ranking no cierra.
    /// </remarks>
    public DatoClase De(Clase clase)
        => _porClase[(int)clase]
           ?? throw new DatosInvalidosException($"clases.json no define la clase «{clase}»");

    /// <summary>Las que pegan lo suficiente para sostener solas una defensa.</summary>
    /// <remarks>
    /// ⚠ Al menos una de las tres opciones del arranque sale de acá. No es una
    /// concesión: con el reparto libre, un arranque de clérigo + bardo + druida
    /// es una corrida perdida en la oleada 1 sin que el jugador haya hecho nada
    /// mal. Perder por una decisión propia es el juego; perder por el sorteo de
    /// la pantalla inicial, antes de tocar nada, es un bug de diseño.
    /// </remarks>
    public Clase[] Ofensivas()
    {
        var lista = new List<Clase>();

        foreach (var c in _todas)
        {
            if (!c.Soporte)
                lista.Add(c.Clase);
        }

        return [.. lista];
    }
}

// =============================================================================
//  El reparto de las 100 oleadas entre las 8 áreas
// =============================================================================

/// <summary>
/// El reparto de las oleadas entre las áreas.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Las oleadas se numeran 1 a 100 corridas, no por área.</b> El área
/// es una propiedad de la oleada, no un contador que se reinicia. Numerarlas por
/// área haría imposible leer el ranking: "oleada 7" no diría nada sin saber de
/// qué área.</para>
///
/// <para>Antes era una clase estática con el array escrito en código. Ahora sale
/// de <c>curvas.json</c>, así que es una instancia y cuelga de
/// <see cref="DatosBalance"/> como todo lo demás que es entrada de la
/// simulación.</para>
/// </remarks>
public sealed class TablaAreas(int[] ultimaOleada)
{
    private readonly int[] _ultimaOleada = ultimaOleada;

    public int Cantidad => _ultimaOleada.Length;

    public int DeOleada(int oleada)
    {
        for (int i = 0; i < _ultimaOleada.Length; i++)
        {
            if (oleada <= _ultimaOleada[i])
                return i + 1;
        }

        return _ultimaOleada.Length;
    }

    /// <summary>
    /// ¿Esta oleada cierra un área? Entonces trae jefe.
    /// </summary>
    /// <remarks>
    /// <b>El jefe es la única fuente de perks de Oro</b>, así que hay exactamente
    /// ocho oportunidades por corrida de conseguir uno. Eso es lo que hace que
    /// matar al jefe valga más que sobrevivirlo, y lo que le pone techo a cuántos
    /// Oro puede juntar un jugador — que a su vez es lo que hace que la fusión
    /// sea una decisión y no un trámite.
    /// </remarks>
    public bool TieneJefe(int oleada) => System.Array.IndexOf(_ultimaOleada, oleada) >= 0;

    public int UltimaOleadaDe(int area) => _ultimaOleada[area - 1];

    public int PrimeraOleadaDe(int area) => area <= 1 ? 1 : _ultimaOleada[area - 2] + 1;
}

// =============================================================================
//  perks.json  y  planos.json
// =============================================================================

/// <summary>
/// El catálogo de perks, ya cargado.
/// </summary>
/// <remarks>
/// <para><b>Los perks no se generan</b>, a diferencia del equipo: se elige uno de
/// esta lista filtrando por tier y por las clases que el jugador tiene. Un perk
/// generado al azar sería un número sin nombre, y el Oro y el Único son
/// comportamientos — no hay forma de generar un comportamiento.</para>
/// </remarks>
public sealed class TablaPerks
{
    private readonly DefinicionPerk[] _todos;
    private readonly Dictionary<int, DefinicionPerk> _porId = [];

    public TablaPerks(DefinicionPerk[] todos)
    {
        _todos = todos;

        foreach (var p in todos)
            _porId[p.Id] = p;
    }

    public IReadOnlyList<DefinicionPerk> Todos => _todos;

    /// <summary>
    /// El perk de ese id.
    /// </summary>
    /// <remarks>
    /// ⚠ Por id y no por posición. Los ids se escriben a mano en
    /// <c>perks.json</c> justamente para que insertar uno en el medio no corra a
    /// todos los de abajo: la mochila y el log guardan ids.
    /// </remarks>
    public DefinicionPerk Por(int id)
        => _porId.TryGetValue(id, out var p)
            ? p
            : throw new DatosInvalidosException($"no existe el perk con id {id}");

    public DefinicionPerk? PorNombre(string nombre)
    {
        foreach (var p in _todos)
        {
            if (string.Equals(p.Nombre, nombre, System.StringComparison.Ordinal))
                return p;
        }

        return null;
    }

    /// <summary>
    /// Elige un perk al azar del tier pedido, entre los que le sirven a alguna de
    /// las clases que el jugador tiene.
    /// </summary>
    /// <remarks>
    /// Filtrar por las clases que tiene, y no por todo el catálogo, es lo que
    /// evita el drop muerto: un Único de druida cuando no hay druida en el roster
    /// es un lugar de mochila gastado en nada.
    /// </remarks>
    public DefinicionPerk? Tirar(TierPerk tier, IReadOnlyList<Clase> clasesDelJugador, Aleatorio rng)
    {
        var candidatos = new List<DefinicionPerk>();

        foreach (var p in _todos)
        {
            // ⚠ Los Únicos NO caen y NO se compran: se fusionan. Que aparezca uno
            // en una tirada de drop es un bug de datos.
            if (p.Tier == TierPerk.Unico || p.Tier != tier)
                continue;

            foreach (var c in clasesDelJugador)
            {
                if (p.SirveA(c))
                {
                    candidatos.Add(p);
                    break;
                }
            }
        }

        if (candidatos.Count == 0)
            return null;

        return candidatos[rng.Indice(candidatos.Count)];
    }
}

/// <summary>Las recetas de fusión, ya resueltas a ids.</summary>
public sealed class TablaPlanos(Plano[] todos)
{
    public IReadOnlyList<Plano> Todos { get; } = todos;

    /// <summary>Busca la receta que hacen esos dos Oro. El orden no importa.</summary>
    public Plano? Buscar(int perkA, int perkB)
    {
        foreach (var p in Todos)
        {
            if ((p.OroA == perkA && p.OroB == perkB) ||
                (p.OroA == perkB && p.OroB == perkA))
            {
                return p;
            }
        }

        return null;
    }
}

// =============================================================================
//  generador-armas.json
// =============================================================================

public sealed record DatoHabilidadArma(
    HabilidadArma Habilidad,
    FormulaMagnitud Magnitud,
    string Nombre,
    string Explica);

/// <summary>Los parámetros del generador de armas.</summary>
/// <remarks>
/// ⚠ <b>No es un catálogo de ítems.</b> DATOS.md §2 prohíbe <c>armas.json</c> —
/// un catálogo de piezas existe en Austral porque su muñeco se compone por
/// capas, y acá un defensor es un sprite. Las armas se <b>generan</b>: esto son
/// los parámetros del generador, no una lista de cosas.
/// </remarks>
public sealed class TablaArmas
{
    private readonly Dictionary<HabilidadArma, DatoHabilidadArma> _porHabilidad = [];

    public TablaArmas(
        int paso, int costoDano, int costoCadencia, int costoAlcance,
        RangoChance chanceLegendaria, RangoChance chanceEpica, RangoChance chanceRara,
        int porcLegendaria, int porcEpica, int porcRara, int porcComun,
        int divisorValor, int multiplicadorPrecioTienda,
        HabilidadArma[] poolComun, HabilidadArma[] poolRaro, HabilidadArma[] poolRompen,
        DatoHabilidadArma[] habilidades,
        string nombreDano, string nombreCadencia, string nombreAlcance,
        string sufijoRara, string sufijoEpica, string sufijoLegendaria)
    {
        Paso = paso;
        CostoDano = costoDano;
        CostoCadencia = costoCadencia;
        CostoAlcance = costoAlcance;
        ChanceLegendaria = chanceLegendaria;
        ChanceEpica = chanceEpica;
        ChanceRara = chanceRara;
        PorcentajeLegendaria = porcLegendaria;
        PorcentajeEpica = porcEpica;
        PorcentajeRara = porcRara;
        PorcentajeComun = porcComun;
        DivisorValor = divisorValor;
        MultiplicadorPrecioTienda = multiplicadorPrecioTienda;
        PoolComun = poolComun;
        PoolRaro = poolRaro;
        PoolRompen = poolRompen;
        Habilidades = habilidades;
        NombreDano = nombreDano;
        NombreCadencia = nombreCadencia;
        NombreAlcance = nombreAlcance;
        SufijoRara = sufijoRara;
        SufijoEpica = sufijoEpica;
        SufijoLegendaria = sufijoLegendaria;

        foreach (var h in habilidades)
            _porHabilidad[h.Habilidad] = h;
    }

    public int Paso { get; }
    public int CostoDano { get; }
    public int CostoCadencia { get; }
    public int CostoAlcance { get; }

    public RangoChance ChanceLegendaria { get; }
    public RangoChance ChanceEpica { get; }
    public RangoChance ChanceRara { get; }

    public int PorcentajeLegendaria { get; }
    public int PorcentajeEpica { get; }
    public int PorcentajeRara { get; }
    public int PorcentajeComun { get; }

    public int DivisorValor { get; }
    public int MultiplicadorPrecioTienda { get; }

    public HabilidadArma[] PoolComun { get; }
    public HabilidadArma[] PoolRaro { get; }
    public HabilidadArma[] PoolRompen { get; }

    public IReadOnlyList<DatoHabilidadArma> Habilidades { get; }

    public string NombreDano { get; }
    public string NombreCadencia { get; }
    public string NombreAlcance { get; }

    public string SufijoRara { get; }
    public string SufijoEpica { get; }
    public string SufijoLegendaria { get; }

    public DatoHabilidadArma? Por(HabilidadArma h) => _porHabilidad.GetValueOrDefault(h);

    public int PorcentajeAHabilidad(RarezaArma r) => r switch
    {
        RarezaArma.Legendaria => PorcentajeLegendaria,
        RarezaArma.Epica => PorcentajeEpica,
        RarezaArma.Rara => PorcentajeRara,
        _ => PorcentajeComun,
    };

    public HabilidadArma[] PoolDe(RarezaArma r) => r switch
    {
        RarezaArma.Legendaria => PoolRompen,
        RarezaArma.Epica => PoolRaro,
        _ => PoolComun,
    };

    public string SufijoDe(RarezaArma r) => r switch
    {
        RarezaArma.Rara => SufijoRara,
        RarezaArma.Epica => SufijoEpica,
        RarezaArma.Legendaria => SufijoLegendaria,
        _ => "",
    };

    public int MagnitudDe(HabilidadArma h, int presupuesto)
        => Por(h)?.Magnitud.En(presupuesto) ?? 0;

    public string NombreDeHabilidad(HabilidadArma h) => Por(h)?.Nombre ?? "";

    /// <summary>Qué hace la habilidad, en una línea, para la UI.</summary>
    public string ExplicarHabilidad(HabilidadArma h, int magnitud)
    {
        string plantilla = Por(h)?.Explica ?? "";

        return plantilla.Length == 0
            ? ""
            : plantilla.Replace("{m}", magnitud.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.StringComparison.Ordinal);
    }
}

// =============================================================================
//  El lector: JSON de texto -> las tablas de arriba
// =============================================================================

/// <summary>
/// Convierte el texto de los JSON de <c>Datos\</c> en las tablas que usa la
/// simulación.
/// </summary>
/// <remarks>
/// <para>⚠ Vive en <c>Simulacion\</c> y <b>no lee disco</b>: recibe strings. Leer
/// archivos es del cliente, que sabe de Godot. Así el servidor puede revalidar
/// con exactamente el mismo parser, que es la única forma de garantizar que las
/// dos mitades entienden lo mismo por un archivo.</para>
///
/// <para><b>Los JSON admiten comentarios</b> (<c>//</c> y <c>/* */</c>) y comas
/// colgando. Son archivos que edita una persona: un número de balance sin la
/// explicación de por qué es ese número es un número que nadie se anima a
/// tocar.</para>
/// </remarks>
public static class LectorTablas
{
    private static readonly JsonDocumentOptions _opciones = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonDocument Parsear(string texto, string archivo)
    {
        try
        {
            return JsonDocument.Parse(texto, _opciones);
        }
        catch (JsonException e)
        {
            throw new DatosInvalidosException($"{archivo}: no es JSON válido — {e.Message}");
        }
    }

    // -------------------------------------------------------------------------
    //  Ayudantes. Todos tiran con el nombre del archivo y el del campo: un
    //  «Object reference not set» no le dice a nadie qué número falta.
    // -------------------------------------------------------------------------

    private static JsonElement Obj(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.Object)
            throw new DatosInvalidosException($"{archivo}: falta el objeto «{prop}»");

        return e;
    }

    private static int Ent(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.Number)
            throw new DatosInvalidosException($"{archivo}: falta el número «{prop}»");

        if (!e.TryGetInt32(out int v))
        {
            throw new DatosInvalidosException(
                $"{archivo}: «{prop}» tiene que ser un entero. " +
                "Dentro de la simulación no existen los decimales (JUGABILIDAD.md §4).");
        }

        return v;
    }

    private static int EntOpcional(JsonElement padre, string prop, int porDefecto)
        => padre.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number
           && e.TryGetInt32(out int v) ? v : porDefecto;

    private static bool Bool(JsonElement padre, string prop, bool porDefecto)
        => padre.TryGetProperty(prop, out var e)
            ? e.ValueKind == JsonValueKind.True
            : porDefecto;

    private static string Texto(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.String)
            throw new DatosInvalidosException($"{archivo}: falta el texto «{prop}»");

        return e.GetString() ?? "";
    }

    private static string TextoOpcional(JsonElement padre, string prop)
        => padre.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? ""
            : "";

    private static JsonElement Lista(JsonElement padre, string prop, string archivo)
    {
        if (!padre.TryGetProperty(prop, out var e) || e.ValueKind != JsonValueKind.Array)
            throw new DatosInvalidosException($"{archivo}: falta la lista «{prop}»");

        return e;
    }

    private static int[] Enteros_(JsonElement padre, string prop, string archivo)
    {
        var arr = Lista(padre, prop, archivo);
        var salida = new int[arr.GetArrayLength()];
        int i = 0;

        foreach (var v in arr.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out salida[i]))
                throw new DatosInvalidosException($"{archivo}: «{prop}[{i}]» no es un entero");

            i++;
        }

        return salida;
    }

    private static T Enum_<T>(string valor, string prop, string archivo) where T : struct, System.Enum
    {
        if (System.Enum.TryParse<T>(valor, ignoreCase: true, out var v))
            return v;

        throw new DatosInvalidosException(
            $"{archivo}: «{prop}» dice «{valor}», que no es un {typeof(T).Name} válido. " +
            $"Valores posibles: {string.Join(", ", System.Enum.GetNames<T>())}.");
    }

    private static Clase[] ClasesDe(JsonElement padre, string archivo)
    {
        if (!padre.TryGetProperty("clases", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var lista = new List<Clase>();

        foreach (var v in arr.EnumerateArray())
            lista.Add(Enum_<Clase>(v.GetString() ?? "", "clases", archivo));

        return [.. lista];
    }

    private static CurvaLineal Curva(JsonElement padre, string prop, string archivo)
    {
        var e = Obj(padre, prop, archivo);
        return new CurvaLineal(Ent(e, "base", archivo), Ent(e, "porOleada", archivo));
    }

    private static RangoChance Rango(JsonElement padre, string prop, string archivo)
    {
        var e = Obj(padre, prop, archivo);
        return new RangoChance(Ent(e, "divisor", archivo), Ent(e, "min", archivo), Ent(e, "max", archivo));
    }

    private static HabilidadArma[] Pool(JsonElement padre, string prop, string archivo)
    {
        var arr = Lista(padre, prop, archivo);
        var lista = new List<HabilidadArma>();

        foreach (var v in arr.EnumerateArray())
            lista.Add(Enum_<HabilidadArma>(v.GetString() ?? "", prop, archivo));

        if (lista.Count == 0)
            throw new DatosInvalidosException($"{archivo}: el pool «{prop}» está vacío");

        return [.. lista];
    }

    // -------------------------------------------------------------------------
    //  clases.json
    // -------------------------------------------------------------------------

    public static TablaClases Clases(string texto)
    {
        const string archivo = "clases.json";

        using var doc = Parsear(texto, archivo);
        var raiz = doc.RootElement;

        var lista = new List<DatoClase>();
        var vistas = new HashSet<Clase>();

        foreach (var c in Lista(raiz, "clases", archivo).EnumerateArray())
        {
            var clase = Enum_<Clase>(Texto(c, "clase", archivo), "clase", archivo);

            if (!vistas.Add(clase))
                throw new DatosInvalidosException($"{archivo}: la clase «{clase}» está dos veces");

            int alcance = Ent(c, "alcance", archivo);

            // ⚠ Con alcance 1 y distancia euclídea, una celda EN DIAGONAL queda a
            // √2 ≈ 1,41 y no entra: cubriría solo las cuatro ortogonales. Un
            // defensor puesto en diagonal respecto del camino no llegaría a tocar
            // nada, y el jugador no tendría forma de saber por qué.
            if (alcance < 2)
            {
                throw new DatosInvalidosException(
                    $"{archivo}: «{clase}» tiene alcance {alcance}. El mínimo es 2: " +
                    "con 1 no entran las diagonales y el defensor no toca nada.");
            }

            int cadencia = Ent(c, "cadencia", archivo);

            if (cadencia < 1)
                throw new DatosInvalidosException($"{archivo}: «{clase}» tiene cadencia {cadencia}, tiene que ser 1 o más");

            lista.Add(new DatoClase(
                clase,
                Ent(c, "costo", archivo),
                Ent(c, "dano", archivo),
                alcance,
                cadencia,
                Bool(c, "soporte", false)));
        }

        var tabla = new TablaClases(
            [.. lista],
            Ent(raiz, "extraDanoPorNivel", archivo),
            Ent(raiz, "slotsPerkPorClase", archivo));

        // ⚠ Sin al menos una ofensiva, la elección inicial puede repartir tres
        // soportes y la corrida está perdida antes de empezar.
        if (tabla.Ofensivas().Length == 0)
            throw new DatosInvalidosException($"{archivo}: todas las clases son «soporte». Al menos una tiene que poder pelear sola.");

        return tabla;
    }

    // -------------------------------------------------------------------------
    //  perks.json
    // -------------------------------------------------------------------------

    public static TablaPerks Perks(string texto)
    {
        const string archivo = "perks.json";

        using var doc = Parsear(texto, archivo);
        var raiz = doc.RootElement;

        var apilado = Obj(raiz, "maxApiladoPorTier", archivo);

        int MaxDe(TierPerk t) => t switch
        {
            TierPerk.Bronce => Ent(apilado, "bronce", archivo),
            TierPerk.Plata => Ent(apilado, "plata", archivo),
            TierPerk.Oro => Ent(apilado, "oro", archivo),
            _ => Ent(apilado, "unico", archivo),
        };

        var lista = new List<DefinicionPerk>();
        var ids = new HashSet<int>();
        var nombres = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var p in Lista(raiz, "perks", archivo).EnumerateArray())
        {
            int id = Ent(p, "id", archivo);
            string nombre = Texto(p, "nombre", archivo);

            if (!ids.Add(id))
            {
                throw new DatosInvalidosException(
                    $"{archivo}: el id {id} está repetido (segundo: «{nombre}»). " +
                    "Los ids se escriben a mano y los guarda la mochila: tienen que ser únicos.");
            }

            if (!nombres.Add(nombre))
                throw new DatosInvalidosException($"{archivo}: el nombre «{nombre}» está repetido. planos.json referencia por nombre.");

            var tier = Enum_<TierPerk>(Texto(p, "tier", archivo), "tier", archivo);
            var efecto = Enum_<EfectoPerk>(Texto(p, "efecto", archivo), "efecto", archivo);

            lista.Add(new DefinicionPerk
            {
                Id = id,
                Nombre = nombre,
                Tier = tier,
                Efecto = efecto,
                Magnitud = Ent(p, "magnitud", archivo),
                Clases = ClasesDe(p, archivo),
                MaxApilado = EntOpcional(p, "maxApilado", MaxDe(tier)),
            });
        }

        if (lista.Count == 0)
            throw new DatosInvalidosException($"{archivo}: no trae perks");

        return new TablaPerks([.. lista]);
    }

    // -------------------------------------------------------------------------
    //  planos.json
    // -------------------------------------------------------------------------

    public static TablaPlanos Planos(string texto, TablaPerks perks)
    {
        const string archivo = "planos.json";

        using var doc = Parsear(texto, archivo);
        var raiz = doc.RootElement;

        var lista = new List<Plano>();

        int IdDe(string nombre, string campo)
        {
            var p = perks.PorNombre(nombre)
                ?? throw new DatosInvalidosException(
                    $"{archivo}: «{campo}» dice «{nombre}», que no existe en perks.json");

            return p.Id;
        }

        foreach (var e in Lista(raiz, "planos", archivo).EnumerateArray())
        {
            string nombre = Texto(e, "nombre", archivo);
            string oroA = Texto(e, "oroA", archivo);
            string oroB = Texto(e, "oroB", archivo);

            if (string.Equals(oroA, oroB, System.StringComparison.Ordinal))
                throw new DatosInvalidosException($"{archivo}: «{nombre}» fusiona «{oroA}» consigo mismo");

            int idA = IdDe(oroA, "oroA");
            int idB = IdDe(oroB, "oroB");
            int idResultante = IdDe(nombre, "nombre");

            // ⚠ Dos Oro dan un Único. Cualquier otra combinación es un bug de
            // datos: un plano que consume un Bronce vaciaría el sentido de que el
            // jefe sea la única fuente de Oro.
            if (perks.Por(idA).Tier != TierPerk.Oro || perks.Por(idB).Tier != TierPerk.Oro)
                throw new DatosInvalidosException($"{archivo}: «{nombre}» tiene que fusionar dos perks de tier «oro»");

            if (perks.Por(idResultante).Tier != TierPerk.Unico)
                throw new DatosInvalidosException($"{archivo}: «{nombre}» tiene que producir un perk de tier «unico»");

            lista.Add(new Plano(nombre, idA, idB, idResultante, Bool(e, "inicial", false)));
        }

        // ⚠ Sin al menos una receta conocida de arranque, un jugador nuevo nunca
        // ve un Único y la fusión —la salida de los Oro repetidos— le resulta
        // invisible hasta que gasta oro en algo que no entiende para qué sirve.
        bool hayInicial = false;

        foreach (var p in lista)
            hayInicial |= p.Inicial;

        if (lista.Count > 0 && !hayInicial)
            throw new DatosInvalidosException($"{archivo}: ningún plano tiene «inicial: true». Tiene que haber al menos uno.");

        return new TablaPlanos([.. lista]);
    }

    // -------------------------------------------------------------------------
    //  generador-armas.json
    // -------------------------------------------------------------------------

    public static TablaArmas Armas(string texto)
    {
        const string archivo = "generador-armas.json";

        using var doc = Parsear(texto, archivo);
        var raiz = doc.RootElement;

        var afijos = Obj(raiz, "afijos", archivo);
        var rareza = Obj(raiz, "rareza", archivo);
        var porc = Obj(raiz, "porcentajeAHabilidad", archivo);
        var valor = Obj(raiz, "valor", archivo);
        var pools = Obj(raiz, "pools", archivo);
        var nombres = Obj(raiz, "nombresBase", archivo);
        var sufijos = Obj(raiz, "sufijoRareza", archivo);

        var habilidades = new List<DatoHabilidadArma>();
        var vistas = new HashSet<HabilidadArma>();

        foreach (var h in Lista(raiz, "habilidades", archivo).EnumerateArray())
        {
            var cual = Enum_<HabilidadArma>(Texto(h, "habilidad", archivo), "habilidad", archivo);

            if (!vistas.Add(cual))
                throw new DatosInvalidosException($"{archivo}: la habilidad «{cual}» está dos veces");

            habilidades.Add(new DatoHabilidadArma(
                cual,
                new FormulaMagnitud(
                    Ent(h, "base", archivo),
                    Ent(h, "divisor", archivo),
                    Ent(h, "min", archivo),
                    Ent(h, "max", archivo)),
                Texto(h, "nombre", archivo),
                TextoOpcional(h, "explica")));
        }

        var tabla = new TablaArmas(
            Ent(afijos, "paso", archivo),
            Ent(afijos, "costoPuntoDano", archivo),
            Ent(afijos, "costoPuntoCadencia", archivo),
            Ent(afijos, "costoPuntoAlcance", archivo),
            Rango(rareza, "legendaria", archivo),
            Rango(rareza, "epica", archivo),
            Rango(rareza, "rara", archivo),
            Ent(porc, "legendaria", archivo),
            Ent(porc, "epica", archivo),
            Ent(porc, "rara", archivo),
            Ent(porc, "comun", archivo),
            Ent(valor, "divisorValor", archivo),
            Ent(valor, "multiplicadorPrecioTienda", archivo),
            Pool(pools, "comun", archivo),
            Pool(pools, "rara", archivo),
            Pool(pools, "rompen", archivo),
            [.. habilidades],
            Texto(nombres, "dano", archivo),
            Texto(nombres, "cadencia", archivo),
            Texto(nombres, "alcance", archivo),
            Texto(sufijos, "rara", archivo),
            Texto(sufijos, "epica", archivo),
            Texto(sufijos, "legendaria", archivo));

        // Toda habilidad que un pool puede tirar necesita su fórmula, o el arma
        // saldría con magnitud 0 y la habilidad no haría nada.
        foreach (var pool in new[] { tabla.PoolComun, tabla.PoolRaro, tabla.PoolRompen })
        {
            foreach (var h in pool)
            {
                if (tabla.Por(h) is null)
                    throw new DatosInvalidosException($"{archivo}: «{h}» está en un pool pero no tiene fórmula en «habilidades»");
            }
        }

        if (tabla.DivisorValor == 0)
            throw new DatosInvalidosException($"{archivo}: «divisorValor» no puede ser 0");

        if (tabla.Paso < 1)
            throw new DatosInvalidosException($"{archivo}: «paso» tiene que ser 1 o más");

        return tabla;
    }

    // -------------------------------------------------------------------------
    //  curvas.json
    // -------------------------------------------------------------------------

    public static DatoCurvas Curvas(string texto)
    {
        const string archivo = "curvas.json";

        using var doc = Parsear(texto, archivo);
        var raiz = doc.RootElement;

        var torre = Obj(raiz, "torre", archivo);
        var corrida = Obj(raiz, "corrida", archivo);
        var defensores = Obj(raiz, "defensores", archivo);
        var economia = Obj(raiz, "economia", archivo);
        var presupuesto = Obj(raiz, "presupuesto", archivo);
        var porOleada = Obj(raiz, "curvasPorOleada", archivo);
        var tienda = Obj(raiz, "tienda", archivo);
        var drops = Obj(raiz, "drops", archivo);
        var oleada = Obj(raiz, "oleada", archivo);
        var perksNpc = Obj(raiz, "perksNpc", archivo);
        var jefe = Obj(raiz, "jefe", archivo);

        int oleadas = Ent(corrida, "oleadasPorCorrida", archivo);
        int[] ultimaPorArea = Enteros_(corrida, "ultimaOleadaPorArea", archivo);
        int areas = Ent(corrida, "cantidadAreas", archivo);

        if (ultimaPorArea.Length != areas)
        {
            throw new DatosInvalidosException(
                $"{archivo}: «cantidadAreas» dice {areas} pero «ultimaOleadaPorArea» trae {ultimaPorArea.Length} números");
        }

        if (areas == 0)
            throw new DatosInvalidosException($"{archivo}: «cantidadAreas» no puede ser 0");

        // ⚠ La corrida son 100 oleadas y TERMINA ahí, matando al jefe final del
        // área 8. Si la última del reparto no coincide con el total, o queda una
        // corrida que nunca termina o queda un área a la que no se llega nunca.
        if (ultimaPorArea[^1] != oleadas)
        {
            throw new DatosInvalidosException(
                $"{archivo}: la última área termina en la oleada {ultimaPorArea[^1]} pero la corrida son {oleadas}. " +
                "Tienen que coincidir: no hay modo sin fin.");
        }

        for (int i = 1; i < ultimaPorArea.Length; i++)
        {
            if (ultimaPorArea[i] <= ultimaPorArea[i - 1])
                throw new DatosInvalidosException($"{archivo}: «ultimaOleadaPorArea» tiene que ir en aumento (área {i + 1})");
        }

        var intervalo = Obj(porOleada, "intervaloSpawnTicks", archivo);
        var chancePerk = Obj(porOleada, "chancePerkNpc", archivo);
        var costoNpc = Obj(oleada, "costoNpc", archivo);

        var curvas = new DatoCurvas(
            new DatoTorre(
                Ent(torre, "hpInicial", archivo),
                Ent(torre, "dano", archivo),
                Ent(torre, "cadenciaTicks", archivo)),

            new DatoCorrida(
                oleadas,
                areas,
                ultimaPorArea,
                Ent(corrida, "oleadasEntreElecciones", archivo),
                Ent(corrida, "segundosDePreparacion", archivo)),

            new DatoDefensores(
                Ent(defensores, "lugaresMochila", archivo),
                Ent(defensores, "slotsEquipoPorClase", archivo),
                Ent(defensores, "porcentajeVentaDefensor", archivo),
                Ent(defensores, "porcentajeVentaArma", archivo)),

            new DatoEconomia(
                Ent(economia, "fichasDeOroInicial", archivo),
                Ent(economia, "oroPorEnemigoAdelantado", archivo),
                Ent(economia, "topeOroPorAdelantar", archivo)),

            new DatoPresupuesto(
                Ent(presupuesto, "base", archivo),
                Ent(presupuesto, "cuadratico", archivo),
                Ent(presupuesto, "capEquivalenteMin", archivo),
                Ent(presupuesto, "capEquivalenteMax", archivo)),

            new DatoCurvasPorOleada(
                Curva(porOleada, "bonusOroLimpiar", archivo),
                Curva(porOleada, "presupuestoDropJefe", archivo),
                Curva(porOleada, "presupuestoItemTienda", archivo),
                Ent(intervalo, "base", archivo),
                Ent(intervalo, "divisor", archivo),
                Ent(intervalo, "min", archivo),
                Ent(intervalo, "max", archivo),
                Ent(chancePerk, "divisor", archivo),
                Ent(chancePerk, "min", archivo),
                Ent(chancePerk, "max", archivo)),

            new DatoTienda(
                Ent(tienda, "perksEnStock", archivo),
                Ent(tienda, "armasEnStock", archivo),
                Ent(tienda, "chanceBronce", archivo),
                Curva(tienda, "precioPerkBronce", archivo),
                Curva(tienda, "precioPerkPlata", archivo)),

            new DatoDrops(
                Ent(drops, "chanceComun", archivo),
                Ent(drops, "chanceBronce", archivo)),

            new DatoOleada(
                Ent(oleada, "maxNpcs", archivo),
                Ent(oleada, "pesoAreaActual", archivo),
                Ent(oleada, "pesoAreaAnterior", archivo),
                Ent(costoNpc, "divisorHp", archivo),
                Ent(costoNpc, "divisorDanoMax", archivo),
                Ent(costoNpc, "divisorVelocidad", archivo),
                Ent(oleada, "intervaloSpawnPorDefecto", archivo)),

            new DatoPerksNpc(
                Ent(perksNpc, "costoVida", archivo),
                Ent(perksNpc, "costoVelocidad", archivo),
                Ent(perksNpc, "escalonVida", archivo),
                Ent(perksNpc, "escalonVelocidad", archivo),
                Ent(perksNpc, "maxVida", archivo),
                Ent(perksNpc, "maxVelocidad", archivo),
                Ent(perksNpc, "chanceVidaEnLaTirada", archivo),
                Ent(perksNpc, "chanceVelocidadEnSobrante", archivo)),

            new DatoJefe(
                Ent(jefe, "divisorVidaExtra", archivo),
                Ent(jefe, "maxVidaExtra", archivo),
                TextoOpcional(jefe, "nombreJefeFinal")));

        // ⚠ Sin tope de NPCs una oleada tardía compra miles de bichos: no es más
        // difícil, es más larga. Medido: corridas clavadas contra el techo de
        // 700.000 ticks en la oleada 55.
        if (curvas.Oleada.MaxNpcs < 1)
            throw new DatosInvalidosException($"{archivo}: «maxNpcs» tiene que ser 1 o más");

        if (curvas.Oleada.DivisorHp == 0 || curvas.Oleada.DivisorDanoMax == 0 || curvas.Oleada.DivisorVelocidad == 0)
            throw new DatosInvalidosException($"{archivo}: ningún divisor de «costoNpc» puede ser 0");

        if (curvas.Jefe.DivisorVidaExtra == 0)
            throw new DatosInvalidosException($"{archivo}: «divisorVidaExtra» no puede ser 0");

        if (curvas.Torre.CadenciaTicks < 1)
            throw new DatosInvalidosException($"{archivo}: la cadencia de la Torre tiene que ser 1 tick o más");

        return curvas;
    }

    // -------------------------------------------------------------------------
    //  combate.json
    // -------------------------------------------------------------------------

    public static DatoCombate Combate(string texto)
    {
        const string archivo = "combate.json";

        using var doc = Parsear(texto, archivo);
        var raiz = doc.RootElement;

        var enemigo = Obj(raiz, "enemigo", archivo);
        var slow = Obj(raiz, "ralentizar", archivo);
        var comp = Obj(raiz, "comportamientos", archivo);
        var arma = Obj(raiz, "habilidadesArma", archivo);

        var combate = new DatoCombate(
            Ent(enemigo, "divisorPromedioDano", archivo),
            Ent(enemigo, "danoMinimo", archivo),

            Ent(slow, "topePorciento", archivo),
            Ent(slow, "duracionTicks", archivo),

            Ent(comp, "ventanaSaltoRicochetCeldas", archivo),
            Ent(comp, "radioSplashCeldas", archivo),
            Ent(comp, "rebotesRicochet", archivo),
            Ent(comp, "rebotesPerforacion", archivo),
            Ent(comp, "rebotesApocalipsis", archivo),
            Ent(comp, "maxSecundariosSplashOro", archivo),
            Ent(comp, "maxSecundariosOndaDeFrio", archivo),
            Ent(comp, "maxSecundariosIraDeDiosYTormentaDivina", archivo),
            Ent(comp, "slowTerribleHambre", archivo),
            Ent(comp, "divisorPenetrante", archivo),
            Ent(comp, "golpesPorCritico", archivo),

            Ent(arma, "maxSecundariosSplash", archivo),
            Ent(arma, "rebotesRebote", archivo),
            Ent(arma, "maxSecundariosCadena", archivo),
            Ent(arma, "topeBonusSed", archivo),
            Ent(arma, "topePorcionAlcance", archivo),
            Ent(arma, "escalaPorcionAlcance", archivo),
            Ent(arma, "umbralEjecutarTropa", archivo),
            Ent(arma, "umbralEjecutarJefe", archivo),
            Bool(arma, "jefeFinalInmuneAEjecutar", true),
            Ent(arma, "cooldownVampirismoTicks", archivo),
            Ent(arma, "duracionMarcaCodiciaTicks", archivo));

        // ⚠ Un slow del 100% no es un enemigo lento: es un enemigo parado, y un
        // enemigo parado fuera del alcance de todos clava la oleada para siempre.
        if (combate.TopeSlowPorciento is < 0 or > 99)
            throw new DatosInvalidosException($"{archivo}: «topePorciento» del slow tiene que estar entre 0 y 99");

        if (combate.DuracionSlowTicks < 1)
            throw new DatosInvalidosException($"{archivo}: el slow tiene que durar 1 tick o más. Si dura 0 no hace nada; si no vence, es permanente.");

        if (combate.GolpesPorCritico < 1)
            throw new DatosInvalidosException($"{archivo}: «golpesPorCritico» tiene que ser 1 o más");

        if (combate.DivisorPenetrante == 0 || combate.DivisorPromedioDanoEnemigo == 0)
            throw new DatosInvalidosException($"{archivo}: ningún divisor puede ser 0");

        if (combate.EscalaPorcionAlcance == 0)
            throw new DatosInvalidosException($"{archivo}: «escalaPorcionAlcance» no puede ser 0");

        return combate;
    }
}
