using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace TowerOfArgentum.Tools;

/// <summary>Un objeto de <c>OBJ.DAT</c>, con lo que hace falta para traer su arte.</summary>
public sealed record AoObject(int Id, string Name, int GrhIndex, int Kind, int Anim, int NumRopaje);

/// <summary>
/// Mira la cadena <c>objeto → grh → frames[] → fileNum</c> sin copiar nada.
/// </summary>
/// <remarks>
/// <para>Existe para <b>chequear antes de tocar</b>. Copiar una hoja de 15 MB y
/// anotar una región equivocada se descubre en runtime, cuando el sprite sale
/// recortado por la mitad; verlo acá cuesta un segundo.</para>
///
/// <code>
/// --inspeccionar-grh 566        una entrada del .ind
/// --inspeccionar-obj 129        un objeto de OBJ.DAT, con su grh resuelto
/// --inspeccionar-tiers          los tiers de equipo de Bots.dat, resueltos
/// </code>
/// </remarks>
public static class GraphicsInspector
{
    private const string RutaObjDat =
        @"D:\Github\Tidies24\Austral-Csharp-SERVER\TdsServer\Dat\OBJ.DAT";

    private const string RutaBotsDat =
        @"D:\Github\Tidies24\Austral-Csharp-SERVER\TdsServer\Dat\Bots.dat";

    public static string SheetsFolder =
        @"D:\Github\Tidies24\Tools\asset-legacy\graficos";

    // -------------------------------------------------------------------------
    //  Verificación del lector
    // -------------------------------------------------------------------------

    /// <summary>
    /// Comprueba que el lector de <c>Graficos.ind</c> devuelve lo que dicen los
    /// bytes.
    /// </summary>
    /// <remarks>
    /// El formato del <c>.ind</c> se dedujo leyendo el archivo en hexadecimal, y
    /// una deducción puede estar mal de una forma que no se nota: un campo de más
    /// desplaza todo lo que sigue y el archivo se lee entero sin errores,
    /// devolviendo basura coherente.
    ///
    /// <para>Los valores esperados salen del dump del principio del archivo:
    /// <c>grh 1 → fileNum 1, región (64,0) de 32x32</c>.</para>
    /// </remarks>
    public static void CheckReader()
    {
        GD.Print("── Lector de Graficos.ind ─────────────────────────");

        var indice = GraphicsIndexReader.Read();

        if (indice.Count == 0)
        {
            GD.PrintErr("  ✗ no se leyó nada: ¿está el archivo?");
            return;
        }

        GD.Print($"  {indice.Count} grh leídos");

        int fallas = 0;

        // El grh 1, decodificado a mano del hexadecimal.
        fallas += Esperar(indice, grh: 1, fileNum: 1, x: 64, y: 0, ancho: 32, alto: 32);

        // Coherencia general: ninguna región puede tener lado 0 ni fileNum <= 0.
        int regionesRotas = 0;
        int animaciones = 0;
        int maxFileNum = 0;

        foreach (var e in indice.Values)
        {
            if (e.IsAnimation)
            {
                animaciones++;
                continue;
            }

            if (e.FileNum <= 0 || e.Width <= 0 || e.Height <= 0)
                regionesRotas++;

            maxFileNum = System.Math.Max(maxFileNum, e.FileNum);
        }

        GD.Print($"  {animaciones} animaciones · {indice.Count - animaciones} regiones");
        GD.Print($"  fileNum más alto: {maxFileNum}");

        // Un puñado de regiones raras es normal en un archivo de 16.000 entradas
        // con veinte años de historia; muchas significan que el parser se
        // desalineó y está leyendo campos corridos.
        if (regionesRotas > indice.Count / 20)
        {
            GD.PrintErr($"  ✗ {regionesRotas} regiones con datos imposibles: el parser está desalineado");
            fallas++;
        }
        else
        {
            GD.Print($"  {regionesRotas} regiones con datos raros (tolerable)");
        }

        GD.Print(fallas == 0
            ? "── LECTOR OK ──────────────────────────────────────"
            : $"── {fallas} FALLA(S) ─────────────────────────────────");
    }

    private static int Esperar(
        Dictionary<int, GrhEntry> indice, int grh, int fileNum, int x, int y, int ancho, int alto)
    {
        if (!indice.TryGetValue(grh, out var e))
        {
            GD.PrintErr($"  ✗ falta el grh {grh}");
            return 1;
        }

        bool ok = e.FileNum == fileNum && e.X == x && e.Y == y && e.Width == ancho && e.Height == alto;

        if (ok)
        {
            GD.Print($"  ✓ grh {grh} → hoja {fileNum}, ({x},{y}) {ancho}x{alto}");
            return 0;
        }

        GD.PrintErr($"  ✗ grh {grh}: se esperaba hoja {fileNum} ({x},{y}) {ancho}x{alto}, " +
                    $"salió hoja {e.FileNum} ({e.X},{e.Y}) {e.Width}x{e.Height}");

        return 1;
    }

    /// <summary>
    /// Comprueba los lectores de <c>Armas.dat</c>, <c>Personajes.ind</c> y
    /// <c>Cabezas.ind</c>.
    /// </summary>
    /// <remarks>
    /// El valor esperado del arma 27 sale de leer <c>Armas.dat</c> a mano:
    /// <c>[ARMA27] Dir1=14569 Dir2=14571 Dir3=14568 Dir4=14570</c>. Si el parser
    /// devuelve otra cosa, está corriendo campos.
    ///
    /// <para>Los <c>.ind</c> binarios no se pueden comparar contra un valor
    /// escrito a mano tan fácil, así que se chequea la <b>forma</b>: que la
    /// cantidad sea razonable y que los grh existan en <c>Graficos.ind</c>. Un
    /// parser desalineado da grh enormes que no existen, y eso se ve enseguida.
    /// </para>
    /// </remarks>
    public static void CheckIndexes()
    {
        GD.Print("── Lectores de índices ────────────────────────────");

        var graficos = GraphicsIndexReader.Read();
        int fallas = 0;

        // --- Armas.dat, contra el valor leído a mano ---
        var armas = DatIndexReader.ReadWeapons();
        GD.Print($"  {armas.Count} armas en Weapons.dat");

        if (armas.TryGetValue(27, out var arma27))
        {
            int[] esperado = [14569, 14571, 14568, 14570];
            bool ok = true;

            for (int i = 0; i < 4; i++)
                ok &= arma27.Direcciones[i] == esperado[i];

            if (ok)
            {
                GD.Print($"  ✓ arma 27 → {string.Join(", ", arma27.Direcciones)}");
            }
            else
            {
                GD.PrintErr($"  ✗ arma 27: se esperaba {string.Join(", ", esperado)}, " +
                            $"salió {string.Join(", ", arma27.Direcciones)}");
                fallas++;
            }
        }
        else
        {
            GD.PrintErr("  ✗ falta el arma 27");
            fallas++;
        }

        fallas += VerificarGrhExisten("armas", armas.Values, graficos);

        // --- Personajes.ind ---
        var cuerpos = DatIndexReader.ReadBodies();
        GD.Print($"  {cuerpos.Count} cuerpos en Personajes.ind");

        if (cuerpos.Count is < 10 or > 5000)
        {
            GD.PrintErr($"  ✗ {cuerpos.Count} cuerpos es un número imposible: el parser se desalineó");
            fallas++;
        }

        int conOffset = 0;

        foreach (var c in cuerpos.Values)
        {
            // El offset de cabeza es un desplazamiento en píxeles 1x: valores de
            // dos dígitos. Miles significa que se está leyendo otro campo.
            if (System.Math.Abs(c.OffsetCabezaX) > 200 || System.Math.Abs(c.OffsetCabezaY) > 200)
                conOffset++;
        }

        if (conOffset > cuerpos.Count / 10)
        {
            GD.PrintErr($"  ✗ {conOffset} cuerpos con offset de cabeza absurdo: campos corridos");
            fallas++;
        }

        var muestra = new List<Body>(cuerpos.Values);

        if (muestra.Count > 0)
        {
            var c = muestra[0];
            GD.Print($"    cuerpo {c.Id}: dirs {string.Join(",", c.Direcciones)} · " +
                     $"cabeza en ({c.OffsetCabezaX},{c.OffsetCabezaY})");
        }

        var comoDirecciones = new List<FourDirections>(cuerpos.Count);

        foreach (var c in cuerpos.Values)
            comoDirecciones.Add(new FourDirections(c.Id, c.Direcciones));

        fallas += VerificarGrhExisten("cuerpos", comoDirecciones, graficos);

        // --- Cabezas.ind ---
        var cabezas = DatIndexReader.ReadHeads();
        GD.Print($"  {cabezas.Count} cabezas en Cabezas.ind");

        fallas += VerificarGrhExisten("cabezas", cabezas.Values, graficos);

        GD.Print(fallas == 0
            ? "── LECTORES OK ────────────────────────────────────"
            : $"── {fallas} FALLA(S) ─────────────────────────────────");
    }

    /// <summary>
    /// Cuántos grh referenciados no existen en <c>Graficos.ind</c>.
    /// </summary>
    /// <remarks>
    /// Es la mejor señal de que un parser binario se desalineó: los índices que
    /// salen mal apuntan a grh que no existen, y eso se cuenta.
    /// </remarks>
    private static int VerificarGrhExisten(
        string que, IEnumerable<FourDirections> entradas, Dictionary<int, GrhEntry> graficos)
    {
        int total = 0;
        int rotos = 0;

        foreach (var e in entradas)
        {
            foreach (int grh in e.Direcciones)
            {
                if (grh == 0)
                    continue;

                total++;

                if (!graficos.ContainsKey(grh))
                    rotos++;
            }
        }

        if (total == 0)
        {
            GD.PrintErr($"  ✗ {que}: ninguna referencia a grh");
            return 1;
        }

        int porciento = rotos * 100 / total;

        if (porciento > 5)
        {
            GD.PrintErr($"  ✗ {que}: {rotos} de {total} grh no existen ({porciento}%)");
            return 1;
        }

        GD.Print($"  ✓ {que}: {total - rotos} de {total} grh existen");
        return 0;
    }

    /// <summary>
    /// Lista qué cuerpos y cabezas existen, agrupados por centena.
    /// </summary>
    /// <remarks>
    /// En Argentum los ids de cabeza están repartidos por raza en bloques de cien
    /// —humano en las unidades, elfo en las 100, elfo oscuro en las 200, y así—
    /// pero <c>Head.dat</c> solo trae contadores, no los rangos. La única forma de
    /// saber qué hay es mirar el índice.
    /// </remarks>
    public static void ListRaces()
    {
        var cuerpos = DatIndexReader.ReadBodies();
        var cabezas = DatIndexReader.ReadHeads();

        GD.Print("── Cuerpos y cabezas por bloque ───────────────────");

        GD.Print($"\n  CABEZAS ({cabezas.Count} en Cabezas.ind)");

        var porCentena = new SortedDictionary<int, List<int>>();

        foreach (int id in cabezas.Keys)
        {
            int centena = id / 100;

            if (!porCentena.TryGetValue(centena, out var lista))
                porCentena[centena] = lista = [];

            lista.Add(id);
        }

        foreach (var (centena, lista) in porCentena)
        {
            lista.Sort();

            string ids = lista.Count <= 12
                ? string.Join(",", lista)
                : string.Join(",", lista.GetRange(0, 12)) + $", … ({lista.Count} en total)";

            GD.Print($"    {centena * 100,4}-{(centena * 100) + 99,4}: {ids}");
        }

        GD.Print($"\n  CUERPOS ({cuerpos.Count} en Personajes.ind)");
        GD.Print($"    ids de 1 a {MaximoDe(cuerpos.Keys)}");

        // Los cuerpos que interesan para razas, según los comentarios de Body.dat
        // del servidor: Body1 HUMANO CIVIL, Body2 ELFO CIVIL, Body3 ELFO OSCURO
        // CIVIL, Body52 vestimentas de enano, Body53 enano desnudo.
        foreach (int id in new[] { 1, 2, 3, 21, 32, 52, 53 })
        {
            if (cuerpos.TryGetValue(id, out var c))
            {
                GD.Print($"    cuerpo {id,3}: dirs {string.Join(",", c.Direcciones)} · " +
                         $"cabeza en ({c.OffsetCabezaX},{c.OffsetCabezaY})");
            }
            else
            {
                GD.Print($"    cuerpo {id,3}: NO EXISTE");
            }
        }
    }

    private static int MaximoDe(IEnumerable<int> ids)
    {
        int m = 0;

        foreach (int i in ids)
            m = System.Math.Max(m, i);

        return m;
    }

    // -------------------------------------------------------------------------
    //  Inspección
    // -------------------------------------------------------------------------

    public static void InspectGrh(int grh)
    {
        var indice = GraphicsIndexReader.Read();

        GD.Print($"── grh {grh} ──────────────────────────────────────");

        if (!indice.TryGetValue(grh, out var e))
        {
            GD.PrintErr($"  no existe el grh {grh}");
            return;
        }

        if (e.IsAnimation)
        {
            GD.Print($"  animación de {e.NumFrames} cuadros · velocidad {e.Speed}");

            foreach (var cuadro in GraphicsIndexReader.Frames(indice, grh))
                Describir(cuadro);
        }
        else
        {
            Describir(e);
        }
    }

    private static void Describir(GrhEntry e)
    {
        const int k = GraphicsIndexReader.Canvas;

        // ⚠ Las dos escalas se muestran juntas a propósito: el .ind está en 1x y
        // las hojas son x4. Confundirlas recorta la esquina del sprite.
        GD.Print($"    grh {e.Grh,6} → hoja {e.FileNum,5} · " +
                 $"1x ({e.X},{e.Y}) {e.Width}x{e.Height} · " +
                 $"x4 ({e.X * k},{e.Y * k}) {e.Width * k}x{e.Height * k}   {EstadoHoja(e.FileNum)}");
    }

    /// <summary>Si la hoja existe en la fuente, y cuánto pesa.</summary>
    private static string EstadoHoja(int fileNum)
    {
        foreach (string extension in new[] { ".PNG", ".png" })
        {
            string ruta = Path.Combine(SheetsFolder, fileNum + extension);

            if (!File.Exists(ruta))
                continue;

            long kb = new FileInfo(ruta).Length / 1024;

            // La minúscula se marca: de las 1312 hojas, 41 están así y promedian
            // 4,7 MB — el peor ratio del set.
            string aviso = extension == ".png" ? "  ⚠ minúscula" : "";

            return $"[{kb} KB{aviso}]";
        }

        return "[FALTA la hoja]";
    }

    // -------------------------------------------------------------------------
    //  OBJ.DAT
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lee <c>OBJ.DAT</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Este archivo es UTF-16 little-endian, NO Latin-1.</b> Es el único
    /// <c>.dat</c> de Austral que rompe la regla: <c>NPCs.dat</c>,
    /// <c>Hechizos.dat</c> y <c>Bots.dat</c> son ISO-8859. Leerlo con
    /// <c>Encoding.Latin1</c> devuelve un archivo lleno de bytes nulos entre cada
    /// letra y ninguna sección se reconoce.
    /// </remarks>
    public static Dictionary<int, AoObject> ReadObjects()
    {
        var salida = new Dictionary<int, AoObject>();

        if (!File.Exists(RutaObjDat))
            return salida;

        string[] lineas = File.ReadAllLines(RutaObjDat, Encoding.Unicode);

        int id = -1;
        var campos = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        void Cerrar()
        {
            if (id < 0)
                return;

            if (campos.Count > 0)
            {
                salida[id] = new AoObject(
                    id,
                    Text(campos, "Name"),
                    Entero(campos, "GrhIndex"),

                    // ⚠ En OBJ.DAT la clave aparece como "Objtype" Y como
                    // "ObjType", con la T cambiada. El diccionario es
                    // OrdinalIgnoreCase justamente por esto.
                    Entero(campos, "ObjType"),

                    Entero(campos, "Anim"),
                    Entero(campos, "NumRopaje"));
            }

            campos.Clear();
        }

        foreach (string cruda in lineas)
        {
            string linea = cruda.Trim();

            if (linea.Length == 0 || linea[0] == ';' || linea[0] == '\'')
                continue;

            if (linea[0] == '[')
            {
                Cerrar();

                int cierre = linea.IndexOf(']');
                string etiqueta = cierre > 0 ? linea[1..cierre] : linea[1..];

                id = etiqueta.StartsWith("OBJ", System.StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(etiqueta.AsSpan(3), out int n) ? n : -1;

                continue;
            }

            int igual = linea.IndexOf('=');

            if (igual > 0)
                campos[linea[..igual].Trim()] = linea[(igual + 1)..].Trim();
        }

        Cerrar();
        return salida;
    }

    public static void InspectObject(int idObjeto)
    {
        var objetos = ReadObjects();
        var indice = GraphicsIndexReader.Read();

        GD.Print($"── objeto {idObjeto} ──────────────────────────────");

        if (!objetos.TryGetValue(idObjeto, out var o))
        {
            GD.PrintErr($"  no existe el objeto {idObjeto} en OBJ.DAT");
            return;
        }

        GD.Print($"  {o.Name}");
        GD.Print($"  tipo {o.Kind} · grhIndex {o.GrhIndex} · anim {o.Anim} · ropaje {o.NumRopaje}");
        GD.Print("  ícono:");

        foreach (var cuadro in GraphicsIndexReader.Frames(indice, o.GrhIndex))
            Describir(cuadro);
    }

    // -------------------------------------------------------------------------
    //  Bots.dat — los tiers de equipo
    // -------------------------------------------------------------------------

    /// <summary>Lo que equipa una clase en los tres tiers.</summary>
    public sealed record TiersDeClase(string DefenderClass, int[] Weapon, int[] Armadura, int[] Casco, int[] Escudo);

    /// <summary>
    /// Lee los tiers de <c>Bots.dat</c>.
    /// </summary>
    /// <remarks>
    /// <para>Los bots de Austral están equipados en tres escalones —nivel 36, 42 y
    /// 47— y eso es una <b>curva de equipo ya balanceada por gente que jugó el
    /// juego</b>. Sirve como esqueleto de los tiers del TD sin tener que
    /// inventarlos.</para>
    ///
    /// <para>⚠ Solo las 9 clases de combate. Pescador, herrero, minero, carpintero
    /// y pirata están en el archivo y quedan afuera: las trabajadoras no pelean.</para>
    /// </remarks>
    public static List<TiersDeClase> ReadTiers()
    {
        var salida = new List<TiersDeClase>();

        if (!File.Exists(RutaBotsDat))
            return salida;

        // Latin-1 acá: Bots.dat SÍ es ISO-8859, a diferencia de OBJ.DAT.
        string[] lineas = File.ReadAllLines(RutaBotsDat, Encoding.Latin1);

        string[] deCombate =
        [
            "Mage", "Cleric", "Warrior", "Assassin", "Thief",
            "Bard", "Druid", "Paladin", "Hunter",
        ];

        string clase = "";
        var campos = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        void Cerrar()
        {
            if (clase.Length == 0 || System.Array.IndexOf(deCombate, clase) < 0)
            {
                campos.Clear();
                return;
            }

            salida.Add(new TiersDeClase(
                clase,
                Tres(campos, "Weapon"),
                Tres(campos, "Armour"),
                Tres(campos, "Helmet"),
                Tres(campos, "Shield")));

            campos.Clear();
        }

        foreach (string cruda in lineas)
        {
            string linea = cruda.Trim();

            if (linea.Length == 0 || linea[0] == ';')
                continue;

            if (linea[0] == '[')
            {
                Cerrar();

                int cierre = linea.IndexOf(']');
                clase = cierre > 0 ? linea[1..cierre] : "";

                continue;
            }

            int igual = linea.IndexOf('=');

            if (igual > 0)
                campos[linea[..igual].Trim()] = linea[(igual + 1)..].Trim();
        }

        Cerrar();
        return salida;
    }

    private static int Entero(Dictionary<string, string> campos, string clave)
        => campos.TryGetValue(clave, out string? v)
           && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : 0;

    private static string Text(Dictionary<string, string> campos, string clave)
        => campos.TryGetValue(clave, out string? v) ? v : "";

    /// <summary>Parsea <c>625-624-623</c> en tres enteros.</summary>
    private static int[] Tres(Dictionary<string, string> campos, string clave)
    {
        if (!campos.TryGetValue(clave, out string? v))
            return [0, 0, 0];

        string[] partes = v.Split('-');
        var salida = new int[3];

        for (int i = 0; i < 3; i++)
        {
            salida[i] = i < partes.Length
                && int.TryParse(partes[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n : 0;
        }

        return salida;
    }

    public static void InspectTiers()
    {
        var tiers = ReadTiers();
        var objetos = ReadObjects();
        var indice = GraphicsIndexReader.Read();

        GD.Print("── Tiers de equipo · Bots.dat ─────────────────────");

        var hojas = new SortedSet<int>();

        foreach (var t in tiers)
        {
            GD.Print($"\n  {t.DefenderClass}");

            Fila("arma", t.Weapon, objetos, indice, hojas);
            Fila("armadura", t.Armadura, objetos, indice, hojas);
        }

        GD.Print($"\n  hojas distintas que harían falta: {hojas.Count}");
        GD.Print($"  {string.Join(", ", hojas)}");

        long total = 0;
        int faltan = 0;

        foreach (int h in hojas)
        {
            long bytes = PesoDeHoja(h);

            if (bytes == 0)
                faltan++;

            total += bytes;
        }

        GD.Print($"  peso total: {total / 1024 / 1024} MB" + (faltan > 0 ? $" · {faltan} sin encontrar" : ""));
    }

    private static void Fila(
        string etiqueta, int[] ids,
        Dictionary<int, AoObject> objetos,
        Dictionary<int, GrhEntry> indice,
        SortedSet<int> hojas)
    {
        var texto = new StringBuilder($"    {etiqueta,-10}");

        foreach (int id in ids)
        {
            if (id == 0)
            {
                texto.Append("  —");
                continue;
            }

            if (!objetos.TryGetValue(id, out var o))
            {
                texto.Append(CultureInfo.InvariantCulture, $"  obj{id}(?)");
                continue;
            }

            var region = GraphicsIndexReader.Resolve(indice, o.GrhIndex);

            if (region is not null)
                hojas.Add(region.FileNum);

            texto.Append(CultureInfo.InvariantCulture,
                $"  [{id}] {o.Name} (grh {o.GrhIndex} → hoja {region?.FileNum.ToString(CultureInfo.InvariantCulture) ?? "?"})");
        }

        GD.Print(texto.ToString());
    }

    /// <summary>
    /// Cuánto arte hace falta para las 9 clases, contado por hojas.
    /// </summary>
    /// <remarks>
    /// Contar <b>hojas distintas</b> y no gráficos: las cuatro direcciones de un
    /// arma viven en la misma hoja, así que traerlas cuesta lo mismo que traer
    /// una. El número que importa para el presupuesto de arte es este.
    /// </remarks>
    public static void Budget()
    {
        var tiers = ReadTiers();
        var objetos = ReadObjects();
        var graficos = GraphicsIndexReader.Read();
        var armas = DatIndexReader.ReadWeapons();
        var cuerpos = DatIndexReader.ReadBodies();
        var cabezas = DatIndexReader.ReadHeads();

        GD.Print("── Budget de arte para las 9 clases ──────────");

        var hojasIconos = new SortedSet<int>();
        var hojasAnimArma = new SortedSet<int>();
        var hojasCuerpo = new SortedSet<int>();
        var ropajes = new SortedSet<int>();
        var animaciones = new SortedSet<int>();

        foreach (var t in tiers)
        {
            foreach (int id in t.Weapon)
            {
                if (id == 0 || !objetos.TryGetValue(id, out var o))
                    continue;

                Sumar(hojasIconos, graficos, o.GrhIndex);

                // ⚠ La animación NO sale del GrhIndex: sale de `Anim`, que es la
                // clave de la sección [ARMA<n>] de Armas.dat. El GrhIndex es el
                // ícono del inventario y no sirve para dibujar al defensor.
                if (o.Anim > 0)
                {
                    animaciones.Add(o.Anim);

                    if (armas.TryGetValue(o.Anim, out var dirs))
                    {
                        foreach (int grh in dirs.Direcciones)
                            Sumar(hojasAnimArma, graficos, grh);
                    }
                }
            }

            foreach (int id in t.Armadura)
            {
                if (id == 0 || !objetos.TryGetValue(id, out var o))
                    continue;

                Sumar(hojasIconos, graficos, o.GrhIndex);

                // El cuerpo que se ve es el NumRopaje de la armadura, no el Body
                // del bot: un defensor con armadura de cuero se ve distinto que
                // uno con placas, y ese es todo el punto de que haya tiers.
                if (o.NumRopaje > 0)
                {
                    ropajes.Add(o.NumRopaje);

                    if (cuerpos.TryGetValue(o.NumRopaje, out var cuerpo))
                    {
                        foreach (int grh in cuerpo.Direcciones)
                            Sumar(hojasCuerpo, graficos, grh);
                    }
                }
            }
        }

        // Las cabezas de los bots: Head=1 y Head=201.
        var hojasCabeza = new SortedSet<int>();

        foreach (int id in new[] { 1, 201 })
        {
            if (cabezas.TryGetValue(id, out var c))
            {
                foreach (int grh in c.Direcciones)
                    Sumar(hojasCabeza, graficos, grh);
            }
        }

        Bloque("íconos de ítem", hojasIconos);
        Bloque("animaciones de arma", hojasAnimArma);
        Bloque("cuerpos con ropaje", hojasCuerpo);
        Bloque("cabezas", hojasCabeza);

        var todas = new SortedSet<int>(hojasIconos);
        todas.UnionWith(hojasAnimArma);
        todas.UnionWith(hojasCuerpo);
        todas.UnionWith(hojasCabeza);

        GD.Print($"\n  {animaciones.Count} animaciones de arma distintas ({string.Join(",", animaciones)})");
        GD.Print($"  {ropajes.Count} ropajes distintos ({string.Join(",", ropajes)})");

        long total = 0;

        foreach (int h in todas)
            total += PesoDeHoja(h);

        GD.Print($"\n  TOTAL: {todas.Count} hojas · {total / 1024} KB");
    }

    private static void Sumar(SortedSet<int> hojas, Dictionary<int, GrhEntry> graficos, int grh)
    {
        if (grh == 0)
            return;

        foreach (var cuadro in GraphicsIndexReader.Frames(graficos, grh))
            hojas.Add(cuadro.FileNum);
    }

    private static void Bloque(string etiqueta, SortedSet<int> hojas)
    {
        long peso = 0;
        int faltan = 0;

        foreach (int h in hojas)
        {
            long b = PesoDeHoja(h);

            if (b == 0)
                faltan++;

            peso += b;
        }

        GD.Print($"  {etiqueta,-22} {hojas.Count,3} hojas · {peso / 1024,6} KB" +
                 (faltan > 0 ? $"  ⚠ {faltan} sin encontrar" : ""));
    }

    private static long PesoDeHoja(int fileNum)
    {
        foreach (string extension in new[] { ".PNG", ".png" })
        {
            string ruta = Path.Combine(SheetsFolder, fileNum + extension);

            if (File.Exists(ruta))
                return new FileInfo(ruta).Length;
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    //  Presupuesto del bestiario
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cuánto arte hace falta para los enemigos de <c>enemigos.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>Se mide <b>antes</b> de copiar porque el bestiario es la tanda más
    /// cara del proyecto: 75 bichos contra los 30 objetos de los tiers. Una hoja
    /// de Austral promedia 750 KB y llega a 15 MB, así que la diferencia entre
    /// «60 hojas» y «200 hojas» decide si esto entra en el repo o si hay que
    /// hornear el atlas antes de tiempo.</para>
    ///
    /// <para>Account <b>hojas distintas</b>: muchos bichos comparten cuerpo —las
    /// variantes de color de un mismo lobo son el mismo <c>body</c>— y las cuatro
    /// direcciones viven casi siempre en la misma hoja.</para>
    ///
    /// <para>El corte por área importa: Ullathorpe se trae entera y las otras
    /// siete esperan, así que lo que decide el trabajo de hoy es la primera
    /// fila, no el total.</para>
    /// </remarks>
    public static void BestiaryBudget()
    {
        var graficos = GraphicsIndexReader.Read();
        var cuerpos = DatIndexReader.ReadBodies();
        var enemigos = ReadEnemies();

        GD.Print("── Budget de arte del bestiario ──────────────");

        var hojas = new SortedSet<int>();
        var bodies = new SortedSet<int>();
        var sinCuerpo = new List<string>();
        var sinRegion = new List<string>();
        var porArea = new SortedDictionary<int, SortedSet<int>>();
        var bichosPorArea = new SortedDictionary<int, int>();

        foreach (var e in enemigos)
        {
            bichosPorArea.TryGetValue(e.Area, out int cuenta);
            bichosPorArea[e.Area] = cuenta + 1;

            if (!cuerpos.TryGetValue(e.Body, out var cuerpo))
            {
                sinCuerpo.Add($"{e.Name} (body {e.Body})");
                continue;
            }

            bodies.Add(e.Body);

            if (!porArea.TryGetValue(e.Area, out var deArea))
            {
                deArea = new SortedSet<int>();
                porArea[e.Area] = deArea;
            }

            bool alguna = false;

            foreach (int grh in cuerpo.Direcciones)
            {
                if (grh == 0)
                    continue;

                foreach (var c in GraphicsIndexReader.Frames(graficos, grh))
                {
                    hojas.Add(c.FileNum);
                    deArea.Add(c.FileNum);
                    alguna = true;
                }
            }

            if (!alguna)
                sinRegion.Add($"{e.Name} (body {e.Body})");
        }

        foreach (var (area, deArea) in porArea)
        {
            long peso = 0;

            foreach (int h in deArea)
                peso += PesoDeHoja(h);

            GD.Print($"  área {area}   {bichosPorArea[area],2} bichos · " +
                     $"{deArea.Count,3} hojas · {peso / 1024,7} KB");
        }

        long total = 0;
        int faltan = 0;

        foreach (int h in hojas)
        {
            long b = PesoDeHoja(h);

            if (b == 0)
                faltan++;

            total += b;
        }

        GD.Print($"\n  {enemigos.Count} enemigos · {bodies.Count} cuerpos distintos");
        GD.Print($"  TOTAL sin repetir entre áreas: {hojas.Count} hojas · {total / 1024} KB");

        if (faltan > 0)
            GD.PrintErr($"  ⚠ {faltan} hojas no están en la carpeta de origen");

        foreach (string s in sinCuerpo)
            GD.PrintErr($"  ✗ sin cuerpo en Personajes.ind: {s}");

        foreach (string s in sinRegion)
            GD.PrintErr($"  ✗ el cuerpo no resuelve a ninguna región: {s}");
    }

    /// <summary>Lee lo justo de <c>enemigos.json</c>: nombre, área y body.</summary>
    public static List<EnemyArt> ReadEnemies()
    {
        string ruta = Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "Datos", "enemigos.json");

        var salida = new List<EnemyArt>();

        if (!File.Exists(ruta))
        {
            GD.PrintErr($"  ✗ no está {ruta}");
            return salida;
        }

        var json = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(ruta, Encoding.UTF8));

        var lista = json?["enemigos"]?.AsArray();

        if (lista is null)
            return salida;

        foreach (var nodo in lista)
        {
            if (nodo is null)
                continue;

            salida.Add(new EnemyArt(
                (int)nodo["id"]!,
                nodo["nombre"]!.GetValue<string>(),
                (int)nodo["area"]!,
                (int)nodo["body"]!,
                nodo["grafico"]?.GetValue<string>() ?? ""));
        }

        return salida;
    }

    // -------------------------------------------------------------------------
    //  Tilesets
    // -------------------------------------------------------------------------

    /// <summary>
    /// Busca las hojas que son <b>tilesets de piso</b>.
    /// </summary>
    /// <remarks>
    /// <para>Un tile de piso de AO mide <b>32×32 en 1x</b> —128×128 en las hojas
    /// x4— y es de un solo cuadro. Una hoja llena de regiones de ese tamaño es un
    /// tileset; una hoja con regiones de mil tamaños distintos es de sprites.</para>
    ///
    /// <para>Se mide en vez de creerle al README, que nombra la 215 y la 20 sin
    /// verificar.</para>
    /// </remarks>
    /// <summary>
    /// Le saca el color promedio a cada hoja de tiles, para saber cuál es pasto,
    /// cuál nieve y cuál arena.
    /// </summary>
    /// <remarks>
    /// <para>Ochenta y tres hojas candidatas es demasiado para abrirlas de a una.
    /// El color promedio las ordena solo: el pasto da verde, la nieve da gris muy
    /// claro y sin saturación, la arena da naranja apagado, la piedra da gris
    /// medio.</para>
    ///
    /// <para>Se muestrea <b>un píxel cada 16</b> y salteando los transparentes.
    /// Abrir una hoja de 15 MB y leerla entera por cada una de las 83 tarda; con
    /// una de cada 256 el promedio no cambia y termina rápido.</para>
    /// </remarks>
    /// <summary>
    /// Arma una hoja de contacto: unos pocos tiles de cada hoja candidata, en
    /// filas, para poder <b>mirarlos</b>.
    /// </summary>
    /// <remarks>
    /// <para>El color promedio ordena las 83 hojas, pero no confirma nada: verde
    /// puede ser pasto de pradera o musgo de cueva. Lo único que confirma es
    /// abrir el archivo, y abrir 83 hojas de 2 MB no es plan.</para>
    ///
    /// <para>Esto recorta 8 tiles reales de cada hoja —usando las regiones del
    /// <c>.ind</c>, no coordenadas adivinadas—, los baja a 64 px y los pone en una
    /// fila. Diez hojas entran en una imagen de 512×640 que se mira de un
    /// vistazo.</para>
    /// </remarks>
    /// <summary>
    /// ¿Los tiles de una hoja están en grilla regular?
    /// </summary>
    /// <remarks>
    /// Decide el formato del índice. Si están en grilla, una hoja entra como
    /// <b>una</b> entrada con columnas y cantidad, y no como 64 entradas sueltas
    /// —que con seis paletas serían 400 entradas y triplicarían el índice—.
    /// </remarks>
    public static void TilesetGrid(string listaHojas)
    {
        var graficos = GraphicsIndexReader.Read();
        GD.Print("── ¿Los tiles forman grilla? ──────────────────────");

        foreach (string parte in listaHojas.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(parte.Trim(), out int hoja))
                continue;

            var xs = new SortedSet<int>();
            var ys = new SortedSet<int>();
            int cuantos = 0;
            int grhMin = int.MaxValue;
            int grhMax = 0;

            foreach (var (grh, e) in graficos)
            {
                if (e.IsAnimation || e.FileNum != hoja || e.Width != 32 || e.Height != 32)
                    continue;

                xs.Add(e.X);
                ys.Add(e.Y);
                cuantos++;

                if (grh < grhMin) grhMin = grh;
                if (grh > grhMax) grhMax = grh;
            }

            if (cuantos == 0)
            {
                GD.PrintErr($"  hoja {hoja}: sin tiles");
                continue;
            }

            bool grilla = xs.Count * ys.Count == cuantos;
            bool grhSeguidos = grhMax - grhMin + 1 == cuantos;

            GD.Print($"  hoja {hoja,5} · {cuantos,3} tiles · " +
                     $"{xs.Count} columnas × {ys.Count} filas · " +
                     $"grh {grhMin}-{grhMax}" +
                     (grilla ? "  ✓ grilla" : "  ✗ irregular") +
                     (grhSeguidos ? " · grh seguidos" : " · grh con huecos"));
        }
    }

    /// <summary>
    /// Dibuja la grilla <b>entera</b> de cada hoja, como una sola imagen.
    /// </summary>
    /// <remarks>
    /// <para>Los pisos principales de Austral son <b>4×4</b>: una textura que se
    /// repite sin costura, cortada en 16 tiles de 32 px. Mirar 8 de esos 16
    /// sueltos y salteados no dice nada — se ven pedazos inconexos y parece que la
    /// hoja es un revoltijo.</para>
    ///
    /// <para>Acá se pega la grilla completa y se baja a un cuadrado. Así se ve la
    /// textura como la va a ver el jugador.</para>
    /// </remarks>
    public static void SampleBlocks(string listaHojas, string destino)
    {
        var graficos = GraphicsIndexReader.Read();

        var hojas = new List<int>();

        foreach (string parte in listaHojas.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(parte.Trim(), out int n))
                hojas.Add(n);
        }

        if (hojas.Count == 0)
        {
            GD.PrintErr("  ✗ hace falta una lista de hojas");
            return;
        }

        GD.Print($"── Bloques enteros de {hojas.Count} hojas ─────────────");

        const int Side = 160;

        var contacto = Image.CreateEmpty(hojas.Count * Side, Side, false, Image.Format.Rgba8);
        contacto.Fill(new Color(1f, 0f, 1f));

        for (int col = 0; col < hojas.Count; col++)
        {
            int hoja = hojas[col];

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = 0, maxY = 0;
            int cuantos = 0;

            foreach (var (_, e) in graficos)
            {
                if (e.IsAnimation || e.FileNum != hoja || e.Width != 32 || e.Height != 32)
                    continue;

                minX = System.Math.Min(minX, e.X);
                minY = System.Math.Min(minY, e.Y);
                maxX = System.Math.Max(maxX, e.X + e.Width);
                maxY = System.Math.Max(maxY, e.Y + e.Height);
                cuantos++;
            }

            if (cuantos == 0)
            {
                GD.PrintErr($"  ✗ hoja {hoja}: sin tiles");
                continue;
            }

            string? ruta = RutaDeHoja(hoja);

            if (ruta is null)
                continue;

            var imagen = Image.LoadFromFile(ruta);

            if (imagen is null)
                continue;

            imagen.Convert(Image.Format.Rgba8);

            int k = GraphicsIndexReader.Canvas;

            var region = new Rect2I(
                minX * k, minY * k, (maxX - minX) * k, (maxY - minY) * k);

            if (region.End.X > imagen.GetWidth() || region.End.Y > imagen.GetHeight())
            {
                GD.PrintErr($"  ✗ hoja {hoja}: la grilla {region} se sale de " +
                            $"{imagen.GetWidth()}×{imagen.GetHeight()}");
                continue;
            }

            var recorte = imagen.GetRegion(region);
            recorte.Resize(Side, Side, Image.Interpolation.Bilinear);

            contacto.BlitRect(
                recorte, new Rect2I(0, 0, Side, Side), new Vector2I(col * Side, 0));

            GD.Print($"  columna {col}: hoja {hoja,5} · {cuantos} tiles · " +
                     $"{region.Size.X}×{region.Size.Y} px en x4");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        contacto.SavePng(destino);

        GD.Print($"\n  escrito en {destino}");
    }

    public static void SampleTilesets(string listaHojas, string destino)
    {
        var graficos = GraphicsIndexReader.Read();

        var hojas = new List<int>();

        foreach (string parte in listaHojas.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(parte.Trim(), out int n))
                hojas.Add(n);
        }

        if (hojas.Count == 0)
        {
            GD.PrintErr("  ✗ hace falta una lista de hojas, por ejemplo 6001,10140,8007");
            return;
        }

        GD.Print($"── Hoja de contacto de {hojas.Count} tilesets ─────────────");

        // Las regiones de tile de cada hoja, en orden de grh para que dos
        // corridas den la misma muestra.
        var porHoja = new Dictionary<int, List<GrhEntry>>();
        var grhs = new List<int>(graficos.Keys);
        grhs.Sort();

        foreach (int grh in grhs)
        {
            var e = graficos[grh];

            if (e.IsAnimation || e.Width != 32 || e.Height != 32)
                continue;

            if (!hojas.Contains(e.FileNum))
                continue;

            if (!porHoja.TryGetValue(e.FileNum, out var lista))
            {
                lista = [];
                porHoja[e.FileNum] = lista;
            }

            lista.Add(e);
        }

        const int PorFila = 8;
        const int Side = 64;

        var contacto = Image.CreateEmpty(
            PorFila * Side, hojas.Count * Side, false, Image.Format.Rgba8);

        contacto.Fill(new Color(1f, 0f, 1f));   // magenta: lo que quede sin pintar se ve

        for (int fila = 0; fila < hojas.Count; fila++)
        {
            int hoja = hojas[fila];

            if (!porHoja.TryGetValue(hoja, out var tiles) || tiles.Count == 0)
            {
                GD.PrintErr($"  ✗ hoja {hoja}: no tiene tiles de 32×32 en el .ind");
                continue;
            }

            string? ruta = RutaDeHoja(hoja);

            if (ruta is null)
            {
                GD.PrintErr($"  ✗ hoja {hoja}: no está el archivo");
                continue;
            }

            var imagen = Image.LoadFromFile(ruta);

            if (imagen is null)
            {
                GD.PrintErr($"  ✗ hoja {hoja}: no se pudo abrir");
                continue;
            }

            imagen.Convert(Image.Format.Rgba8);

            // Se reparten a lo largo de la hoja en vez de tomar los 8 primeros:
            // los primeros suelen ser todos la misma variante.
            int paso = System.Math.Max(1, tiles.Count / PorFila);

            for (int col = 0; col < PorFila; col++)
            {
                int i = col * paso;

                if (i >= tiles.Count)
                    break;

                var t = tiles[i];
                int k = GraphicsIndexReader.Canvas;

                var region = new Rect2I(t.X * k, t.Y * k, t.Width * k, t.Height * k);

                if (region.Position.X < 0 || region.Position.Y < 0
                    || region.End.X > imagen.GetWidth() || region.End.Y > imagen.GetHeight())
                {
                    continue;
                }

                var recorte = imagen.GetRegion(region);
                recorte.Resize(Side, Side, Image.Interpolation.Bilinear);

                contacto.BlitRect(
                    recorte,
                    new Rect2I(0, 0, Side, Side),
                    new Vector2I(col * Side, fila * Side));
            }

            GD.Print($"  fila {fila}: hoja {hoja,5} · {tiles.Count} tiles");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        contacto.SavePng(destino);

        GD.Print($"\n  escrito en {destino}");
    }

    public static void ClassifyTilesets(int minimoTiles)
    {
        var graficos = GraphicsIndexReader.Read();

        GD.Print($"── Color de las hojas con {minimoTiles}+ tiles ────────────");

        var deTile = new SortedDictionary<int, int>();

        foreach (var (_, e) in graficos)
        {
            if (!e.IsAnimation && e.Width == 32 && e.Height == 32)
            {
                deTile.TryGetValue(e.FileNum, out int d);
                deTile[e.FileNum] = d + 1;
            }
        }

        GD.Print("     hoja  tiles      R    G    B   sat    peso   parece");

        var hojas = new List<int>(deTile.Keys);
        hojas.Sort((a, b) => deTile[b].CompareTo(deTile[a]));

        foreach (int hoja in hojas)
        {
            if (deTile[hoja] < minimoTiles)
                continue;

            string? ruta = RutaDeHoja(hoja);

            if (ruta is null)
                continue;

            var imagen = Image.LoadFromFile(ruta);

            if (imagen is null)
                continue;

            long sr = 0, sg = 0, sb = 0, cuantos = 0;

            for (int y = 0; y < imagen.GetHeight(); y += 16)
            {
                for (int x = 0; x < imagen.GetWidth(); x += 16)
                {
                    var c = imagen.GetPixel(x, y);

                    if (c.A < 0.5f)
                        continue;

                    sr += (int)(c.R * 255);
                    sg += (int)(c.G * 255);
                    sb += (int)(c.B * 255);
                    cuantos++;
                }
            }

            if (cuantos == 0)
                continue;

            int r = (int)(sr / cuantos);
            int g = (int)(sg / cuantos);
            int b = (int)(sb / cuantos);

            int max = System.Math.Max(r, System.Math.Max(g, b));
            int min = System.Math.Min(r, System.Math.Min(g, b));
            int sat = max == 0 ? 0 : (max - min) * 100 / max;

            GD.Print($"    {hoja,5}  {deTile[hoja],5}    {r,3}  {g,3}  {b,3}  {sat,3}%  " +
                     $"{PesoDeHoja(hoja) / 1024,6}KB   {Adivinar(r, g, b, sat)}");
        }
    }

    /// <summary>Un nombre tentativo a partir del color. Es una pista, no un dato.</summary>
    private static string Adivinar(int r, int g, int b, int sat)
    {
        if (sat < 12 && r > 180) return "nieve / piedra clara";
        if (sat < 15) return r > 110 ? "piedra" : "piedra oscura / dungeon";
        if (g > r && g > b) return "pasto";
        if (r > g && g > b && r > 140) return "arena / tierra";
        if (b > r && b > g) return "agua / hielo";
        return "mezcla";
    }

    private static string? RutaDeHoja(int fileNum)
    {
        foreach (string extension in new[] { ".PNG", ".png" })
        {
            string ruta = Path.Combine(SheetsFolder, fileNum + extension);

            if (File.Exists(ruta))
                return ruta;
        }

        return null;
    }

    public static void Tilesets()
    {
        var graficos = GraphicsIndexReader.Read();

        GD.Print("── Hojas que parecen tileset de piso ──────────────");

        // Por hoja: cuántas regiones tiene y cuántas son de 32×32 (1x).
        var total = new SortedDictionary<int, int>();
        var deTile = new SortedDictionary<int, int>();
        var tamanos = new Dictionary<(int, int), int>();

        foreach (var (_, e) in graficos)
        {
            if (e.IsAnimation)
                continue;

            total.TryGetValue(e.FileNum, out int t);
            total[e.FileNum] = t + 1;

            // El lector ya multiplicó por 4, así que un tile de 32 llega como 128.
            // ⚠ GrhEntry guarda las coordenadas CRUDAS del .ind, o sea 1x.
            // Quien dibuja multiplica por Canvas. Buscar 128 acá no encontraba
            // ningún tile: encontraba los bloques 4×4.
            const int lado = 32;

            var clave = (e.Width, e.Height);
            tamanos.TryGetValue(clave, out int c);
            tamanos[clave] = c + 1;

            if (e.Width == lado && e.Height == lado)
            {
                deTile.TryGetValue(e.FileNum, out int d);
                deTile[e.FileNum] = d + 1;
            }
        }

        // Los tamaños más comunes de todo el índice: dice qué es "normal".
        var ordenTamanos = new List<KeyValuePair<(int, int), int>>(tamanos);
        ordenTamanos.Sort((a, b) => b.Value.CompareTo(a.Value));

        GD.Print("  tamaños de región más comunes (en píxeles 1x, como los guarda el .ind):");

        for (int i = 0; i < 6 && i < ordenTamanos.Count; i++)
        {
            var (medida, cuantas) = (ordenTamanos[i].Key, ordenTamanos[i].Value);
            GD.Print($"    {medida.Item1,4} × {medida.Item2,-4} {cuantas,5} regiones" +
                     (medida.Item1 == 32 && medida.Item2 == 32 ? "   ← tile de piso (1x)"
                      : medida.Item1 == 128 && medida.Item2 == 128 ? "   ← bloque 4×4 de tiles"
                      : ""));
        }

        // Las hojas donde MANDAN los tiles.
        var candidatas = new List<(int Hoja, int Tiles, int Total, long Weight)>();

        foreach (var (hoja, cuantosTiles) in deTile)
        {
            int cuantasEnTotal = total[hoja];

            // Umbral: la mitad de la hoja tiene que ser tiles, y tiene que haber
            // unos cuantos. Una hoja con 3 tiles sueltos no es un tileset.
            if (cuantosTiles >= 16 && cuantosTiles * 2 >= cuantasEnTotal)
                candidatas.Add((hoja, cuantosTiles, cuantasEnTotal, PesoDeHoja(hoja)));
        }

        candidatas.Sort((a, b) => b.Tiles.CompareTo(a.Tiles));

        GD.Print($"\n  {candidatas.Count} hojas candidatas:");
        GD.Print("     hoja   tiles  regiones      peso");

        long suma = 0;

        foreach (var c in candidatas)
        {
            GD.Print($"    {c.Hoja,5}   {c.Tiles,5}   {c.Total,7}   {c.Weight / 1024,6} KB");
            suma += c.Weight;
        }

        GD.Print($"\n  suman {suma / 1024} KB");
    }
}

/// <summary>Lo que el pipeline de arte necesita de un enemigo.</summary>
public sealed record EnemyArt(int Id, string Name, int Area, int Body, string Graphic);
