using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace TowerOfArgentum.Tools;

/// <summary>
/// Trae un gráfico de Austral al proyecto, <b>renumerado</b>.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Los números arrancan en 1 y son consecutivos.</b> El gráfico 10071
/// de Austral entra acá como <c>3.PNG</c> o el que le toque. Así la numeración
/// queda pareja y no hereda los huecos de veinte años de un MMO.</para>
///
/// <para>La trazabilidad no se pierde: cada entrada del índice guarda un campo
/// <c>origen</c> con el <c>fileNum</c>, el <c>grh</c> y el objeto de los que
/// salió. Es prosa que no lee nadie en runtime, y es lo único que permite volver
/// a la fuente dentro de seis meses.</para>
///
/// <para><b>Se trae de a uno y se verifica.</b> Copiar una hoja y anotar una
/// región equivocada se descubre en runtime, cuando el sprite sale cortado. Cada
/// copia chequea tres cosas: que el archivo llegó completo, que sus dimensiones
/// son las que dice el <c>.ind</c>, y que la región cabe adentro.</para>
///
/// <code>
/// --traer-item 479              un objeto de OBJ.DAT, por id
/// --traer-tiers                 los 30 objetos de los tiers de Bots.dat
/// </code>
/// </remarks>
public static class GraphicFetcher
{
    private static readonly string CarpetaDestino =
        Path.GetFullPath(Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "Resources"));

    private static string RutaIndice => Path.Combine(CarpetaDestino, "graficos.json");

    // -------------------------------------------------------------------------
    //  Traer uno
    // -------------------------------------------------------------------------

    public static void FetchItem(int idObjeto)
    {
        var objetos = GraphicsInspector.ReadObjects();
        var indice = GraphicsIndexReader.Read();

        GD.Print($"── Traer objeto {idObjeto} ────────────────────────");

        if (!objetos.TryGetValue(idObjeto, out var o))
        {
            GD.PrintErr($"  ✗ no existe el objeto {idObjeto} en OBJ.DAT");
            return;
        }

        var region = GraphicsIndexReader.Resolve(indice, o.GrhIndex);

        if (region is null)
        {
            GD.PrintErr($"  ✗ el grh {o.GrhIndex} de «{o.Name}» no resuelve a ninguna hoja");
            return;
        }

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        string nombre = NameOf(o);

        if (graficos.ContainsKey(nombre))
        {
            GD.Print($"  · «{nombre}» ya está en el índice, no se toca");
            return;
        }

        int numeroNuevo = ProximoNumero(graficos);

        if (Copiar(region.FileNum, numeroNuevo, region, nombre, o, graficos))
        {
            GuardarIndice(json);
            GD.Print($"  ✓ {o.Name} → {numeroNuevo}.PNG   («{nombre}»)");
        }
    }

    // -------------------------------------------------------------------------
    //  Tilesets de piso
    // -------------------------------------------------------------------------

    /// <summary>
    /// Trae una hoja entera de tiles de piso como <b>una sola entrada</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Un tileset NO entra tile por tile.</b> Las hojas de piso de
    /// Austral son grillas perfectas con grh consecutivos —verificado con
    /// <c>--grilla-tileset</c>: la 6001 son 16 columnas × 4 filas, grh 6064 a
    /// 6127—. Anotar los 64 tiles por separado metería 400 entradas para seis
    /// paletas y triplicaría el índice, para describir algo que se dice con tres
    /// números: lado, columnas y cantidad.</para>
    ///
    /// <para>La entrada guarda igual <c>x/y/w/h</c> del primer tile, así que quien
    /// solo quiera una imagen fija no necesita entender la grilla.</para>
    ///
    /// <para>El piso de AO mide <b>32×32 en 1x</b>, o sea 128×128 en las hojas x4.
    /// Los bloques de 128×128 en 1x —4×4 tiles— existen pero son 132 contra 4452:
    /// son piezas decorativas, no el piso.</para>
    /// </remarks>
    /// <summary>
    /// Trae un <b>piso</b>: un bloque 4×4 de tiles, que es una textura sin
    /// costura.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Un piso NO es una grilla de tiles sueltos.</b> Austral guarda
    /// cada textura de suelo como un bloque de 4×4 tiles de 32 px — 128×128 en 1x,
    /// <b>512×512 en las hojas x4</b>—, que se repite sin costura. Los 16 pedazos
    /// no son 16 opciones: son una sola imagen cortada.</para>
    ///
    /// <para>La primera versión de esto los anotó como "64 tiles independientes" y
    /// estaba mal: un decorador sorteando entre los 64 mezcla pedazos de cuatro
    /// texturas distintas y sale un mosaico roto. Además arrastraba dos problemas
    /// que con este formato <b>desaparecen</b>: las casillas vacías y los índices
    /// de tile.</para>
    ///
    /// <para>Una hoja puede traer <b>varias</b> texturas al lado: la 6000 mide
    /// 2048×512 en x4, o sea <b>cuatro</b> pastos distintos. Por eso el parámetro
    /// <paramref name="bloque"/>.</para>
    /// </remarks>
    /// <param name="bloque">Cuál de las texturas de la hoja, desde 0.</param>
    public static void FetchFloor(int hojaOrigen, string nombre, int bloque)
    {
        if (hojaOrigen <= 0 || string.IsNullOrWhiteSpace(nombre))
        {
            GD.PrintErr("  ✗ uso: --traer-piso <hoja> <nombre> [bloque]");
            return;
        }

        var indice = GraphicsIndexReader.Read();

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = 0, maxY = 0;
        int cuantos = 0;

        foreach (var (_, e) in indice)
        {
            if (e.IsAnimation || e.FileNum != hojaOrigen || e.Width != 32 || e.Height != 32)
                continue;

            minX = System.Math.Min(minX, e.X);
            minY = System.Math.Min(minY, e.Y);
            maxX = System.Math.Max(maxX, e.X + e.Width);
            maxY = System.Math.Max(maxY, e.Y + e.Height);
            cuantos++;
        }

        GD.Print($"── Traer piso {hojaOrigen}[{bloque}] como «{nombre}» ─────────");

        if (cuantos == 0)
        {
            GD.PrintErr($"  ✗ la hoja {hojaOrigen} no tiene tiles de 32×32 en el .ind");
            return;
        }

        const int Side = 32;
        const int PorBloque = 4;
        int anchoBloque = Side * PorBloque;

        int columnas = (maxX - minX) / anchoBloque;
        int filas = (maxY - minY) / anchoBloque;

        if (columnas < 1 || filas < 1)
        {
            GD.PrintErr($"  ✗ la grilla mide {maxX - minX}×{maxY - minY} en 1x: " +
                        $"no llega a un bloque de {anchoBloque}×{anchoBloque}");
            return;
        }

        int bloques = columnas * filas;

        if (bloque < 0 || bloque >= bloques)
        {
            GD.PrintErr($"  ✗ la hoja {hojaOrigen} tiene {bloques} textura(s), " +
                        $"pediste la {bloque}");
            return;
        }

        string clave = $"piso.{nombre}";
        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        if (graficos.ContainsKey(clave))
        {
            GD.Print($"  · «{clave}» ya está en el índice, no se toca");
            return;
        }

        int numero = NumeroDeHoja(graficos, hojaOrigen);

        if (numero == 0)
        {
            numero = ProximoNumero(graficos);

            if (!CopiarHojaEntera(hojaOrigen, numero, out string error))
            {
                GD.PrintErr($"  ✗ {error}");
                return;
            }
        }

        int k = GraphicsIndexReader.Canvas;

        int bx = minX + ((bloque % columnas) * anchoBloque);
        int by = minY + ((bloque / columnas) * anchoBloque);

        graficos[clave] = new JsonObject
        {
            ["fileNum"] = numero,
            ["x"] = bx * k,
            ["y"] = by * k,
            ["w"] = anchoBloque * k,
            ["h"] = anchoBloque * k,
            ["frames"] = 1,

            // ⚠ `hoja=N` va SIEMPRE al final: NumeroDeHoja lo busca con EndsWith.
            ["origen"] = $"AO piso «{nombre}» bloque {bloque} de {bloques} " +
                         $"4×4 tiles hoja={hojaOrigen}",
        };

        GuardarIndice(json);

        GD.Print($"  ✓ {numero}.PNG · textura {bloque + 1} de {bloques} · " +
                 $"{anchoBloque * k}×{anchoBloque * k} px en ({bx * k},{by * k})");

        GD.Print($"  el índice tiene {graficos.Count} entradas");
    }

    public static void FetchTileset(int hojaOrigen, string nombre)
    {
        if (hojaOrigen <= 0 || string.IsNullOrWhiteSpace(nombre))
        {
            GD.PrintErr("  ✗ uso: --traer-tileset <hoja> <nombre>");
            return;
        }

        var indice = GraphicsIndexReader.Read();

        GD.Print($"── Traer tileset {hojaOrigen} como «{nombre}» ─────────────");

        // Las regiones de tile de esta hoja, en orden de grh.
        var grhs = new List<int>();

        foreach (var (grh, e) in indice)
        {
            if (!e.IsAnimation && e.FileNum == hojaOrigen && e.Width == 32 && e.Height == 32)
                grhs.Add(grh);
        }

        if (grhs.Count == 0)
        {
            GD.PrintErr($"  ✗ la hoja {hojaOrigen} no tiene tiles de 32×32 en el .ind");
            return;
        }

        grhs.Sort();

        var xs = new SortedSet<int>();
        var ys = new SortedSet<int>();
        var posiciones = new HashSet<(int, int)>();

        foreach (int grh in grhs)
        {
            var e = indice[grh];
            xs.Add(e.X);
            ys.Add(e.Y);
            posiciones.Add((e.X, e.Y));
        }

        // ⚠ Se comparan POSICIONES distintas, no cuántos grh hay.
        //
        // Austral reusa la misma región desde varios grh: la hoja 1532 tiene 80
        // entradas sobre 64 posiciones. Contando grh, una grilla perfecta se
        // rechazaba por tener duplicados, que es justo lo que no importa — lo que
        // la entrada compacta necesita es que las posiciones formen la grilla.
        if (xs.Count * ys.Count != posiciones.Count)
        {
            GD.PrintErr($"  ✗ las {posiciones.Count} posiciones no forman grilla " +
                        $"({xs.Count} × {ys.Count}); esta hoja necesita entradas sueltas");
            return;
        }

        string clave = $"tileset.{nombre}";
        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        if (graficos.ContainsKey(clave))
        {
            GD.Print($"  · «{clave}» ya está en el índice, no se toca");
            return;
        }

        int numero = NumeroDeHoja(graficos, hojaOrigen);

        if (numero == 0)
        {
            numero = ProximoNumero(graficos);

            if (!CopiarHojaEntera(hojaOrigen, numero, out string error))
            {
                GD.PrintErr($"  ✗ {error}");
                return;
            }
        }

        int k = GraphicsIndexReader.Canvas;
        var primero = indice[grhs[0]];

        var grilla = new JsonObject
        {
            ["columnas"] = xs.Count,
            ["filas"] = ys.Count,
            ["cantidad"] = posiciones.Count,
            ["lado"] = primero.Width * k,
        };

        // ⚠ Hay hojas con la mitad sin dibujar.
        //
        // La de arena tiene 96 casillas de grilla y solo 62 con contenido. El
        // verificador de arte no lo ve, porque un tileset es UNA entrada del
        // índice y ese chequeo mira el primer tile. Un decorador que sortee entre
        // los 96 pinta agujeros en el piso.
        //
        // Se listan los que sirven, y solo cuando falta alguno: una hoja entera no
        // necesita cargar 64 números.
        var usables = TilesConContenido(
            numero, primero.X * k, primero.Y * k, xs.Count, ys.Count, primero.Width * k);

        if (usables.Count > 0 && usables.Count < posiciones.Count)
        {
            var lista = new JsonArray();

            foreach (int i in usables)
                lista.Add(i);

            grilla["usables"] = lista;
        }

        graficos[clave] = new JsonObject
        {
            ["fileNum"] = numero,
            ["x"] = primero.X * k,
            ["y"] = primero.Y * k,
            ["w"] = primero.Width * k,
            ["h"] = primero.Height * k,
            ["frames"] = 1,
            ["grilla"] = grilla,

            // ⚠ `hoja=N` va SIEMPRE al final: NumeroDeHoja lo busca con EndsWith
            // para reusar un archivo ya copiado.
            ["origen"] = $"AO tileset «{nombre}» grh {grhs[0]}-{grhs[^1]} " +
                         $"{xs.Count}×{ys.Count} hoja={hojaOrigen}",
        };

        GuardarIndice(json);

        GD.Print($"  ✓ {numero}.PNG ← hoja {hojaOrigen} · " +
                 $"{posiciones.Count} tiles · {xs.Count} columnas × {ys.Count} filas" +
                 (usables.Count < posiciones.Count
                     ? $" · {posiciones.Count - usables.Count} vacíos descartados"
                     : ""));

        GD.Print($"  el índice tiene {graficos.Count} entradas");
    }

    /// <summary>
    /// Qué casillas de la grilla tienen algo dibujado.
    /// </summary>
    /// <remarks>
    /// Se mira un píxel cada 8. Un tile de 128×128 con contenido tiene cientos de
    /// píxeles opacos; uno vacío no tiene ninguno, y ocho de salto no se lo pierde.
    /// </remarks>
    private static List<int> TilesConContenido(
        int hoja, int x0, int y0, int columnas, int filas, int lado)
    {
        var salida = new List<int>();
        string ruta = Path.Combine(CarpetaDestino, hoja + ".PNG");

        if (!File.Exists(ruta))
            return salida;

        var imagen = Image.LoadFromFile(ruta);

        if (imagen is null)
            return salida;

        imagen.Convert(Image.Format.Rgba8);

        for (int i = 0; i < columnas * filas; i++)
        {
            int cx = x0 + ((i % columnas) * lado);
            int cy = y0 + ((i / columnas) * lado);
            bool algo = false;

            for (int y = cy; y < cy + lado && !algo; y += 8)
            {
                for (int x = cx; x < cx + lado; x += 8)
                {
                    if (x >= imagen.GetWidth() || y >= imagen.GetHeight())
                        continue;

                    if (imagen.GetPixel(x, y).A > 0.1f)
                    {
                        algo = true;
                        break;
                    }
                }
            }

            if (algo)
                salida.Add(i);
        }

        return salida;
    }

    /// <summary>
    /// Copia una hoja entera, sin chequear regiones.
    /// </summary>
    /// <remarks>
    /// <see cref="CopiarHoja"/> verifica que cada cuadro caiga adentro, y para un
    /// tileset esa verificación la hizo antes el chequeo de grilla. Acá lo único
    /// que importa es que el archivo llegue completo.
    /// </remarks>
    private static bool CopiarHojaEntera(int origen, int destino, out string error)
    {
        error = "";

        foreach (string extension in new[] { ".PNG", ".png" })
        {
            string desde = Path.Combine(GraphicsInspector.SheetsFolder, origen + extension);

            if (!File.Exists(desde))
                continue;

            Directory.CreateDirectory(CarpetaDestino);
            string hacia = Path.Combine(CarpetaDestino, destino + ".PNG");

            File.Copy(desde, hacia, overwrite: true);

            var a = new FileInfo(desde);
            var b = new FileInfo(hacia);

            if (a.Length != b.Length)
            {
                error = $"la copia quedó en {b.Length} bytes y el original tiene {a.Length}";
                return false;
            }

            return true;
        }

        error = $"no está la hoja {origen} en la carpeta de origen";
        return false;
    }

    // -------------------------------------------------------------------------
    //  Traer los tiers enteros
    // -------------------------------------------------------------------------

    public static void FetchTiers()
    {
        var tiers = GraphicsInspector.ReadTiers();
        var objetos = GraphicsInspector.ReadObjects();
        var indice = GraphicsIndexReader.Read();

        GD.Print("── Traer los tiers de equipo ──────────────────────");

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        // Se junta el universo primero y se recorre ORDENADO. Sin ordenar, la
        // numeración dependería del orden del diccionario y correr esto dos veces
        // en dos máquinas daría números distintos para los mismos ítems.
        var ids = new SortedSet<int>();

        foreach (var t in tiers)
        {
            foreach (int id in t.Weapon)
                if (id > 0) ids.Add(id);

            foreach (int id in t.Armadura)
                if (id > 0) ids.Add(id);
        }

        int traidos = 0;
        int reusados = 0;
        int saltados = 0;
        int fallados = 0;

        foreach (int id in ids)
        {
            if (!objetos.TryGetValue(id, out var o))
            {
                GD.PrintErr($"  ✗ obj {id}: no está en OBJ.DAT");
                fallados++;
                continue;
            }

            string nombre = NameOf(o);

            if (graficos.ContainsKey(nombre))
            {
                saltados++;
                continue;
            }

            var region = GraphicsIndexReader.Resolve(indice, o.GrhIndex);

            if (region is null)
            {
                GD.PrintErr($"  ✗ obj {id} «{o.Name}»: el grh {o.GrhIndex} no resuelve");
                fallados++;
                continue;
            }

            // ⚠ Si esa hoja ya se trajo, se REUSA el número en vez de copiarla de
            // nuevo.
            //
            // Varios ítems comparten gráfico: la Daga +3 y la Daga +4 son dos
            // objetos distintos de AO que apuntan a la misma hoja 711. Sin esto se
            // copiaban dos archivos idénticos con números distintos —lo encontró
            // el chequeo de duplicados por hash— y con quinientos gráficos eso
            // sería un montón de bytes repetidos y un atlas más grande de lo
            // necesario.
            int yaTraida = NumeroDeHoja(graficos, region.FileNum);

            if (yaTraida > 0)
            {
                Anotar(graficos, nombre, yaTraida, region, o, region.FileNum);

                GD.Print($"  ↺ {yaTraida,3}.PNG  ← hoja {region.FileNum,5}  " +
                         $"obj {id,3}  {o.Name}   (comparte gráfico)");

                reusados++;
                continue;
            }

            int numeroNuevo = ProximoNumero(graficos);

            if (Copiar(region.FileNum, numeroNuevo, region, nombre, o, graficos))
            {
                GD.Print($"  ✓ {numeroNuevo,3}.PNG  ← hoja {region.FileNum,5}  " +
                         $"obj {id,3}  {o.Name}");
                traidos++;
            }
            else
            {
                fallados++;
            }
        }

        GuardarIndice(json);

        GD.Print($"\n  traídos {traidos} · reusados {reusados} · ya estaban {saltados} · fallaron {fallados}");
        GD.Print($"  el índice tiene ahora {graficos.Count} entradas");
    }

    /// <summary>
    /// Trae el arte de las 9 clases: íconos, armas animadas, cuerpos y cabezas.
    /// </summary>
    /// <remarks>
    /// <para>Las tres cadenas, que son distintas y hay que no confundir:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Ícono</b>: <c>obj.GrhIndex</c> → hoja. Es lo que se ve en la
    /// mochila y en la tienda.</item>
    /// <item><b>Arma en la mano</b>: <c>obj.Anim</c> → <c>[ARMA&lt;n&gt;]</c> de
    /// <c>Armas.dat</c> → 4 direcciones animadas. <b>No sale del
    /// GrhIndex</b>.</item>
    /// <item><b>Body</b>: <c>obj.NumRopaje</c> de la ARMADURA →
    /// <c>Personajes.ind</c> → 4 direcciones. El cuerpo que se ve lo da la
    /// armadura equipada, no el <c>Body</c> del personaje: es todo el punto de que
    /// haya tiers.</item>
    /// </list>
    /// </remarks>
    public static void FetchClassArt(int soloEsteObjeto = 0)
    {
        var tiers = GraphicsInspector.ReadTiers();
        var objetos = GraphicsInspector.ReadObjects();
        var indice = GraphicsIndexReader.Read();
        var armas = DatIndexReader.ReadWeapons();
        var cuerpos = DatIndexReader.ReadBodies();
        var cabezas = DatIndexReader.ReadHeads();

        GD.Print(soloEsteObjeto > 0
            ? $"── Arte del objeto {soloEsteObjeto} ───────────────────────"
            : "── Arte de las 9 clases ──────────────────────────");

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        var armasVistas = new SortedSet<int>();
        var ropajesVistos = new SortedSet<int>();

        int entradas = 0;

        // --- Armas: ícono + las 4 direcciones animadas ---
        foreach (var t in tiers)
        {
            foreach (int id in t.Weapon)
            {
                if (id == 0 || (soloEsteObjeto > 0 && id != soloEsteObjeto))
                    continue;

                if (!objetos.TryGetValue(id, out var o) || o.Anim <= 0)
                    continue;

                if (!armasVistas.Add(o.Anim))
                    continue;

                if (!armas.TryGetValue(o.Anim, out var dirs))
                {
                    GD.PrintErr($"  ✗ obj {id} «{o.Name}»: no está la [ARMA{o.Anim}] en Weapons.dat");
                    continue;
                }

                int n = TraerCuatroDirecciones(
                    graficos, $"arma.{ShortName(o)}", dirs.Direcciones, indice,
                    $"AO obj={id} «{o.Name}» anim={o.Anim}");

                if (n > 0)
                    GD.Print($"  ✓ arma.{ShortName(o),-24} {n} direcciones   ({o.Name})");

                entradas += n;
            }
        }

        // --- Armaduras: el cuerpo con ese ropaje, 4 direcciones ---
        foreach (var t in tiers)
        {
            foreach (int id in t.Armadura)
            {
                if (id == 0 || (soloEsteObjeto > 0 && id != soloEsteObjeto))
                    continue;

                if (!objetos.TryGetValue(id, out var o) || o.NumRopaje <= 0)
                    continue;

                if (!ropajesVistos.Add(o.NumRopaje))
                    continue;

                if (!cuerpos.TryGetValue(o.NumRopaje, out var cuerpo))
                {
                    GD.PrintErr($"  ✗ obj {id} «{o.Name}»: no está el ropaje {o.NumRopaje}");
                    continue;
                }

                int n = TraerCuatroDirecciones(
                    graficos, $"cuerpo.{ShortName(o)}", cuerpo.Direcciones, indice,
                    $"AO obj={id} «{o.Name}» ropaje={o.NumRopaje} " +
                    $"cabeza=({cuerpo.OffsetCabezaX},{cuerpo.OffsetCabezaY})");

                if (n > 0)
                {
                    GD.Print($"  ✓ cuerpo.{ShortName(o),-22} {n} direcciones   " +
                             $"(ropaje {o.NumRopaje}, cabeza en " +
                             $"{cuerpo.OffsetCabezaX * GraphicsIndexReader.Canvas}," +
                             $"{cuerpo.OffsetCabezaY * GraphicsIndexReader.Canvas})");
                }

                entradas += n;
            }
        }

        // --- Cabezas: las dos que usan los bots ---
        if (soloEsteObjeto == 0)
        {
            foreach (int id in new[] { 1, 201 })
            {
                if (!cabezas.TryGetValue(id, out var c))
                    continue;

                int n = TraerCuatroDirecciones(
                    graficos, $"cabeza.{id}", c.Direcciones, indice, $"AO cabeza={id}");

                if (n > 0)
                    GD.Print($"  ✓ cabeza.{id,-25} {n} direcciones");

                entradas += n;
            }
        }

        GuardarIndice(json);

        int hojas = Directory.Exists(CarpetaDestino)
            ? Directory.GetFiles(CarpetaDestino, "*.PNG").Length
            : 0;

        GD.Print($"\n  {entradas} entradas nuevas · el índice tiene {graficos.Count} · {hojas} hojas en disco");
    }

    /// <summary>El nombre del ítem sin el prefijo <c>item.</c>.</summary>
    internal static string ShortName(AoObject o) => NameOf(o)["item.".Length..];

    // -------------------------------------------------------------------------
    //  Razas
    // -------------------------------------------------------------------------

    /// <summary>Las cinco razas de Argentum, con su cuerpo y su cabeza.</summary>
    /// <remarks>
    /// <para>Los números salen de mirar los índices, no de suponer:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Humano y elfo NO comparten cuerpo.</b> El humano es el
    /// <c>Body1</c> (grh 4582-4584) y el elfo el <c>Body2</c> (4586-4588). Se
    /// parecen, pero son dibujos distintos.</item>
    /// <item><b>Enano y gnomo SÍ comparten cuerpo</b>, el 52. Las dos son razas
    /// bajas y el arte de Austral las trata igual de la cabeza para abajo; lo que
    /// las distingue es la cabeza.</item>
    /// <item>Las cabezas están repartidas en bloques de cien por raza:
    /// humano en las unidades, elfo en las 100, elfo oscuro en las 200, enano en
    /// las 300, gnomo en las 400.</item>
    /// </list>
    ///
    /// <para>⚠ El <b>offset de cabeza</b> es lo que separa altas de bajas, y es
    /// un dato, no una opinión: <c>-38</c> píxeles para humano, elfo y elfo
    /// oscuro; <c>-28</c> para enano y gnomo. Un enano con el offset de humano
    /// tiene la cabeza flotando.</para>
    /// </remarks>
    private static readonly (string Name, int Body, int Cabeza, bool Baja)[] _razas =
    [
        ("humano",      1,   1, false),
        ("elfo",        2, 102, false),
        ("elfoOscuro",  3, 201, false),
        ("enano",      52, 301, true),
        ("gnomo",      52, 401, true),
    ];

    /// <summary>
    /// Trae un cuerpo y una cabeza por raza.
    /// </summary>
    /// <remarks>
    /// Una variante de cada una, que es lo que hace falta. <b>Hoy no hay nada en
    /// el juego atado a la raza</b> —los defensores son clases, no razas— pero el
    /// arte cuesta 5 cuerpos y 5 cabezas, y traerlo ahora evita volver a esta
    /// cadena dentro de seis meses.
    /// </remarks>
    public static void FetchRaces()
    {
        var indice = GraphicsIndexReader.Read();
        var cuerpos = DatIndexReader.ReadBodies();
        var cabezas = DatIndexReader.ReadHeads();

        GD.Print("── Cuerpos y cabezas de las razas ─────────────────");

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        int entradas = 0;

        foreach (var (nombre, idCuerpo, idCabeza, baja) in _razas)
        {
            string altura = baja ? "baja" : "alta";

            if (cuerpos.TryGetValue(idCuerpo, out var cuerpo))
            {
                // El offset de cabeza va en el `origen` y se guarda en píxeles x4,
                // que es el espacio en el que trabaja todo el índice. Es lo que
                // permite pegar la cabeza al cuerpo sin adivinar.
                int offsetX = cuerpo.OffsetCabezaX * GraphicsIndexReader.Canvas;
                int offsetY = cuerpo.OffsetCabezaY * GraphicsIndexReader.Canvas;

                int n = TraerCuatroDirecciones(
                    graficos, $"raza.{nombre}.cuerpo", cuerpo.Direcciones, indice,
                    $"AO body={idCuerpo} raza={nombre} ({altura}) cabezaOffset=({offsetX},{offsetY})");

                if (n > 0)
                {
                    GD.Print($"  ✓ raza.{nombre}.cuerpo{new string(' ', 12 - nombre.Length)}" +
                             $"{n} dirs · body {idCuerpo} · cabeza en ({offsetX},{offsetY}) · {altura}");
                }

                entradas += n;
            }
            else
            {
                GD.PrintErr($"  ✗ raza {nombre}: no existe el cuerpo {idCuerpo}");
            }

            if (cabezas.TryGetValue(idCabeza, out var cabeza))
            {
                int n = TraerCuatroDirecciones(
                    graficos, $"raza.{nombre}.cabeza", cabeza.Direcciones, indice,
                    $"AO head={idCabeza} raza={nombre}");

                if (n > 0)
                    GD.Print($"  ✓ raza.{nombre}.cabeza{new string(' ', 12 - nombre.Length)}{n} dirs · head {idCabeza}");

                entradas += n;
            }
            else
            {
                GD.PrintErr($"  ✗ raza {nombre}: no existe la cabeza {idCabeza}");
            }
        }

        GuardarIndice(json);

        int hojas = Directory.Exists(CarpetaDestino)
            ? Directory.GetFiles(CarpetaDestino, "*.PNG").Length
            : 0;

        GD.Print($"\n  {entradas} entradas nuevas · el índice tiene {graficos.Count} · {hojas} hojas");
    }

    // -------------------------------------------------------------------------
    //  Armaduras de raza baja
    // -------------------------------------------------------------------------

    /// <summary>
    /// La versión para enano y gnomo de cada armadura de los tiers.
    /// </summary>
    /// <remarks>
    /// <para>⚠ En Austral una armadura es <b>o de raza alta o de raza baja</b>, no
    /// tiene dos ropajes: el flag <c>RazaEnana=1</c> marca cuál es cuál, y son dos
    /// objetos distintos con nombres emparejados —<c>(H/E/EO-H/M)</c> contra
    /// <c>(E/G-H/M)</c>—.</para>
    ///
    /// <para>Seis de las ocho tienen equivalente exacto. Las dos que no, van con
    /// lo más cercano y queda dicho en el <c>origen</c>:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Túnica Azul</b> no existe en raza baja. Va la Túnica gris, que es
    /// la túnica genérica de enano y gnomo.</item>
    /// <item><b>Armadura Klox</b> tampoco. Va la Bruñida, que es la armadura
    /// pesada de raza baja del mismo escalón.</item>
    /// </list>
    /// </remarks>
    private static readonly (int Alta, int Baja, string Nota)[] _paresDeArmadura =
    [
        (30, 613, ""),                      // Armadura de cuero
        (359, 616, ""),                     // Cota de mallas
        (195, 243, ""),                     // Armadura de placas completa
        (237, 524, ""),                     // Túnica Roja
        (519, 615, ""),                     // Túnica Legendary
        (360, 612, ""),                     // Armadura de cazador
        (238, 620, "equivalente: no hay Túnica Azul de raza baja, va la gris"),
        (688, 500, "equivalente: no hay Klox de raza baja, va la Bruñida"),
    ];

    public static void FetchLowArmors()
    {
        var objetos = GraphicsInspector.ReadObjects();
        var indice = GraphicsIndexReader.Read();
        var cuerpos = DatIndexReader.ReadBodies();

        GD.Print("── Armaduras de raza baja (enano y gnomo) ─────────");

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        int entradas = 0;

        foreach (var (idAlta, idBaja, nota) in _paresDeArmadura)
        {
            if (!objetos.TryGetValue(idBaja, out var baja))
            {
                GD.PrintErr($"  ✗ obj {idBaja}: no está en OBJ.DAT");
                continue;
            }

            if (baja.NumRopaje <= 0 || !cuerpos.TryGetValue(baja.NumRopaje, out var cuerpo))
            {
                GD.PrintErr($"  ✗ obj {idBaja} «{baja.Name}»: sin ropaje {baja.NumRopaje}");
                continue;
            }

            string nombreAlta = objetos.TryGetValue(idAlta, out var alta)
                ? ShortName(alta)
                : $"obj{idAlta}";

            // El nombre se arma con el de la armadura ALTA más el sufijo, para que
            // las dos versiones de la misma pieza queden juntas al ordenar y sea
            // obvio cuál es el par de cuál.
            int n = TraerCuatroDirecciones(
                graficos, $"cuerpoBajo.{nombreAlta}", cuerpo.Direcciones, indice,
                $"AO obj={idBaja} «{baja.Name}» ropaje={baja.NumRopaje} " +
                $"parDe={idAlta}" + (nota.Length > 0 ? $" [{nota}]" : ""));

            if (n > 0)
            {
                GD.Print($"  ✓ cuerpoBajo.{nombreAlta,-24} {n} dirs · obj {idBaja} " +
                         $"«{baja.Name}»" + (nota.Length > 0 ? "  ⚠ equivalente" : ""));
            }

            entradas += n;
        }

        GuardarIndice(json);

        int hojas = Directory.Exists(CarpetaDestino)
            ? Directory.GetFiles(CarpetaDestino, "*.PNG").Length
            : 0;

        GD.Print($"\n  {entradas} entradas nuevas · el índice tiene {graficos.Count} · {hojas} hojas");
    }

    // -------------------------------------------------------------------------
    //  El bestiario
    // -------------------------------------------------------------------------

    /// <summary>
    /// Trae el cuerpo de cada enemigo de <c>enemigos.json</c>, por área.
    /// </summary>
    /// <remarks>
    /// <para><b>Se trae de a un área.</b> El bestiario entero son 81 hojas y
    /// 97 MB —el área 8 sola pesa 58 MB, porque los dragones ocupan hojas de
    /// varios MB—, contra los 13 MB que tiene hoy el repo de arte. Traerlo todo
    /// de golpe multiplicaría por ocho el peso para tener 64 bichos que no se van
    /// a ver hasta dentro de mucho.</para>
    ///
    /// <para>El orden lo fija <c>PASOS.md</c>: Ullathorpe entera y jugable antes
    /// de escribir una línea del área 2. El área 1 son 5 hojas y 836 KB.</para>
    ///
    /// <para>La cadena es la misma que la de las razas —<c>body → grh →
    /// frames[] → fileNum</c> por <c>Personajes.ind</c>—, porque un enemigo se
    /// dibuja igual que un personaje: es un cuerpo de AO y nada más. La
    /// diferencia está en la cabeza, y es que <b>no la tiene</b>: los NPCs de
    /// fauna traen <c>Head=0</c> y todo el bicho vive en el cuerpo.</para>
    ///
    /// <code>
    /// --traer-bestiario 1           el área 1
    /// --traer-bestiario 0           las ocho, ~97 MB
    /// </code>
    /// </remarks>
    public static void FetchBestiary(int area)
    {
        var indice = GraphicsIndexReader.Read();
        var cuerpos = DatIndexReader.ReadBodies();
        var enemigos = GraphicsInspector.ReadEnemies();

        string cual = area == 0 ? "todas las áreas" : $"el área {area}";
        GD.Print($"── Bestiario: {cual} ──────────────────────────");

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        int entradas = 0;
        int bichos = 0;
        int yaEstaban = 0;

        // ⚠ Dos enemigos pueden compartir `body`.
        //
        // En Austral las variantes de un mismo bicho —el lobo de un mapa y el de
        // otro— comparten cuerpo. Acá el importador ya deduplicó por nombre, pero
        // quedan pares distintos con el mismo cuerpo. La clave del índice sale del
        // NOMBRE, así que sin este control se anotarían dos claves apuntando a la
        // misma región: no rompe nada, pero infla el índice y hace creer que hay
        // dos sprites donde hay uno.
        var porBody = new Dictionary<int, string>();

        foreach (var e in enemigos)
        {
            if (area != 0 && e.Area != area)
                continue;

            bichos++;

            if (!cuerpos.TryGetValue(e.Body, out var cuerpo))
            {
                GD.PrintErr($"  ✗ {e.Name}: no existe el cuerpo {e.Body}");
                continue;
            }

            // ⚠ La clave sale del DATO, no se recalcula acá.
            //
            // Derivarla del nombre en este archivo y también en el que dibuja fue
            // lo que dejó el índice con `bicho.rataGigante` después de que el
            // importador renombrara ese bicho a "Rata": dos derivaciones de la
            // misma cosa que se separaron sin que nadie se enterara.
            string clave = e.Graphic;

            if (clave.Length == 0)
            {
                GD.PrintErr($"  ✗ {e.Name}: sin campo «grafico», corré --importar-npcs");
                continue;
            }

            if (porBody.TryGetValue(e.Body, out string? gemelo))
            {
                GD.Print($"  = {clave}{Relleno(clave)}comparte el body {e.Body} con {gemelo}");
                continue;
            }

            porBody[e.Body] = clave;

            if (graficos.ContainsKey($"{clave}.sur"))
            {
                yaEstaban++;
                continue;
            }

            int n = TraerCuatroDirecciones(
                graficos, clave, cuerpo.Direcciones, indice,
                $"AO npc={e.Id} «{e.Name}» área={e.Area} body={e.Body}");

            if (n > 0)
                GD.Print($"  ✓ {clave}{Relleno(clave)}{n} dirs · body {e.Body}");

            entradas += n;
        }

        GuardarIndice(json);

        int hojas = Directory.Exists(CarpetaDestino)
            ? Directory.GetFiles(CarpetaDestino, "*.PNG").Length
            : 0;

        GD.Print($"\n  {bichos} enemigos · {entradas} entradas nuevas" +
                 (yaEstaban > 0 ? $" · {yaEstaban} ya estaban" : ""));

        GD.Print($"  el índice tiene {graficos.Count} entradas · {hojas} hojas");
    }

    /// <summary>
    /// Borra del índice todas las entradas que empiecen con un prefijo, y las
    /// hojas que queden sin dueño.
    /// </summary>
    /// <remarks>
    /// <para>Hace falta porque los JSON de datos <b>cambian</b>. El bestiario se
    /// trajo con los nombres de una versión de <c>enemigos.json</c>, el importador
    /// se corrigió, y de golpe el índice tenía <c>bicho.rataGigante</c> apuntando
    /// a un enemigo que ahora se llama <c>Rata</c>. Sin esto, la única salida es
    /// editar el índice a mano, que es exactamente donde se cuela un error que se
    /// descubre en runtime.</para>
    ///
    /// <para>⚠ <b>Solo borra hojas del final.</b> Los <c>fileNum</c> son
    /// consecutivos y arrancan en 1; sacar la hoja 30 dejaría un hueco que el
    /// próximo <c>ProximoNumero</c> volvería a usar para otra cosa, o —peor—
    /// obligaría a renumerar y a reescribir todas las entradas. Si lo que se
    /// quiere olvidar dejó huérfana una hoja del medio, se avisa y se deja.</para>
    /// </remarks>
    public static void Forget(string prefijo)
    {
        if (string.IsNullOrWhiteSpace(prefijo))
        {
            GD.PrintErr("  ✗ hace falta un prefijo, por ejemplo «bicho.»");
            return;
        }

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        GD.Print($"── Olvidar «{prefijo}» ────────────────────────────");

        var aBorrar = new List<string>();

        foreach (var (clave, _) in graficos)
        {
            if (clave.StartsWith(prefijo, System.StringComparison.Ordinal))
                aBorrar.Add(clave);
        }

        if (aBorrar.Count == 0)
        {
            GD.Print("  · no hay ninguna entrada con ese prefijo");
            return;
        }

        foreach (string clave in aBorrar)
            graficos.Remove(clave);

        // Qué hojas siguen teniendo dueño después del borrado.
        var conDueno = new SortedSet<int>();

        foreach (var (_, valor) in graficos)
        {
            if (valor?["fileNum"] is not null)
                conDueno.Add((int)valor["fileNum"]!);
        }

        // ⚠ El barrido arranca en la hoja más alta que hay EN DISCO, no en la más
        // alta que queda en el índice.
        //
        // Después de borrar las entradas, el índice ya no menciona las hojas que
        // quedaron huérfanas: preguntarle a él por dónde empezar da un número más
        // chico que las que hay que borrar, y no se borra ninguna. Es el bug que
        // tuvo la primera versión de esto.
        var enDisco = new List<int>();

        if (Directory.Exists(CarpetaDestino))
        {
            foreach (string ruta in Directory.GetFiles(CarpetaDestino, "*.PNG"))
            {
                if (int.TryParse(Path.GetFileNameWithoutExtension(ruta), out int n))
                    enDisco.Add(n);
            }
        }

        enDisco.Sort();

        // ⚠ Nunca tocar números que NO son de nuestra numeración.
        //
        // Los `fileNum` propios son consecutivos desde 1, así que el más alto que
        // puede existir es la cantidad de entradas del índice. Todo lo que esté
        // por encima es un archivo que puso alguien a mano —hojas de Austral con
        // su número original, que es como se muestran los candidatos antes de
        // importarlos— y borrarlo es destruir trabajo ajeno.
        //
        // Pasó: este barrido se llevó puestas seis hojas que el usuario había
        // dejado para elegir.
        int techoPropio = graficos.Count + 1;

        int borradas = 0;
        var huerfanasDelMedio = new List<int>();
        var ajenas = new List<int>();
        bool sigueSiendoElFinal = true;

        for (int k = enDisco.Count - 1; k >= 0; k--)
        {
            int n = enDisco[k];

            if (n > techoPropio)
            {
                ajenas.Add(n);
                continue;
            }

            if (conDueno.Contains(n))
            {
                sigueSiendoElFinal = false;
                continue;
            }

            if (!sigueSiendoElFinal)
            {
                huerfanasDelMedio.Add(n);
                continue;
            }

            string ruta = Path.Combine(CarpetaDestino, n + ".PNG");

            if (File.Exists(ruta))
            {
                File.Delete(ruta);
                borradas++;
            }
        }

        GuardarIndice(json);

        GD.Print($"  ✓ {aBorrar.Count} entradas borradas · {borradas} hojas borradas");


        if (huerfanasDelMedio.Count > 0)
        {
            GD.PrintErr($"  ⚠ quedan sin dueño hojas del medio, y NO se borran " +
                        $"porque renumerar rompería el resto: " +
                        $"{string.Join(", ", huerfanasDelMedio)}");
        }

        if (ajenas.Count > 0)
        {
            GD.Print($"  · {ajenas.Count} archivo(s) con numeración de Austral, " +
                     $"intactos: {string.Join(", ", ajenas)}");
        }
    }

    /// <summary>Alinea la columna del listado sin depender de un ancho fijo.</summary>
    private static string Relleno(string clave)
    {
        int faltan = 30 - clave.Length;
        return faltan > 1 ? new string(' ', faltan) : "  ";
    }

    // -------------------------------------------------------------------------
    //  Animaciones: armas, cuerpos y cabezas
    // -------------------------------------------------------------------------

    /// <summary>
    /// Trae una animación de 4 direcciones: un arma, un cuerpo o una cabeza.
    /// </summary>
    /// <remarks>
    /// <para>Las cuatro direcciones de una misma pieza suelen vivir en la
    /// <b>misma hoja</b>, en bandas horizontales: el arco compuesto ocupa una sola
    /// hoja de 268 KB con sus cuatro direcciones de 5-6 cuadros. Por eso traer las
    /// cuatro cuesta lo mismo en disco que traer una, y solo agrega tres entradas
    /// al índice.</para>
    ///
    /// <para>Direcciones: 1 = norte, 2 = este, 3 = sur, 4 = oeste. Es el
    /// <c>eHeading</c> de AO, no un orden nuestro.</para>
    /// </remarks>
    private static int TraerCuatroDirecciones(
        JsonObject graficos, string prefijo, int[] grhPorDireccion,
        Dictionary<int, GrhEntry> indice, string origenBase)
    {
        // ⚠ EL ORDEN ES EL eHEADING DE AO: NORTH=1, EAST=2, SOUTH=3, WEST=4
        // (Constants.cs del server de Austral). Acá decía
        // ["norte", "este", "oeste", "sur"] y las dos últimas estaban dadas
        // vuelta, así que todo lo que el índice llamaba «.sur» era la banda
        // del oeste: los muñecos de las previsualizaciones salían mirando a
        // la izquierda.
        string[] nombresDireccion = ["norte", "este", "sur", "oeste"];
        int traidos = 0;

        for (int d = 0; d < 4; d++)
        {
            int grh = grhPorDireccion[d];

            if (grh == 0)
                continue;

            string nombre = $"{prefijo}.{nombresDireccion[d]}";

            if (graficos.ContainsKey(nombre))
                continue;

            var cuadros = GraphicsIndexReader.Frames(indice, grh);

            if (cuadros.Count == 0)
            {
                GD.PrintErr($"  ✗ {nombre}: el grh {grh} no resuelve a ninguna región");
                continue;
            }

            // ⚠ Todos los cuadros tienen que estar en la MISMA hoja.
            //
            // Si una animación se reparte en dos archivos, este traedor copiaría
            // solo el primero y los cuadros restantes apuntarían a coordenadas de
            // una hoja que no está. El síntoma sería una animación que muestra
            // basura a partir del segundo cuadro.
            int hojaOrigen = cuadros[0].FileNum;
            bool repartida = false;

            foreach (var c in cuadros)
            {
                if (c.FileNum != hojaOrigen)
                    repartida = true;
            }

            if (repartida)
            {
                GD.PrintErr($"  ✗ {nombre}: la animación se reparte en varias hojas, se saltea");
                continue;
            }

            int numero = NumeroDeHoja(graficos, hojaOrigen);

            if (numero == 0)
            {
                numero = ProximoNumero(graficos);

                if (!CopiarHoja(hojaOrigen, numero, cuadros, out string error))
                {
                    GD.PrintErr($"  ✗ {nombre}: {error}");
                    continue;
                }
            }

            // ⚠ Se recortan los cuadros vacíos del final.
            //
            // En el arte de Austral, el arma mirando al NORTE queda detrás del
            // cuerpo en los últimos cuadros y la hoja no tiene nada ahí: el grh
            // existe, la región está bien formada, y adentro no hay un solo píxel.
            // Dibujarlos haría que el arma parpadee.
            //
            // Lo encontró el chequeo de píxeles: 16 de 491 regiones, casi todas
            // «arma.*.norte». La geometría sola no lo ve.
            var conContenido = RecortarVacios(cuadros, numero, out int descartados);

            if (conContenido.Count == 0)
            {
                GD.PrintErr($"  ✗ {nombre}: los {cuadros.Count} cuadros están vacíos, se saltea");
                continue;
            }

            var entradaOriginal = indice[grh];

            // ⚠ `hoja=N` va SIEMPRE al final del origen, y la nota antes.
            //
            // NumeroDeHoja busca la hoja con EndsWith sobre este texto: es lo que
            // permite reusar un archivo ya copiado cuando dos entradas comparten
            // gráfico. Con la nota pegada al final, el EndsWith fallaba y la misma
            // hoja se copiaba varias veces — seis duplicados, que encontró el
            // chequeo de md5.
            string nota = descartados > 0
                ? $" ({descartados} cuadros vacíos recortados)"
                : "";

            AnotarCuadros(
                graficos, nombre, numero, conContenido,
                (int)entradaOriginal.Speed,
                $"{origenBase} dir={nombresDireccion[d]} grh={grh}{nota} hoja={hojaOrigen}");

            traidos++;
        }

        return traidos;
    }

    /// <summary>
    /// Saca los cuadros sin un solo píxel opaco, estén donde estén.
    /// </summary>
    /// <remarks>
    /// <para>La primera versión solo recortaba la <b>cola</b>, suponiendo que un
    /// hueco en el medio podía ser una pausa intencional. El chequeo de píxeles
    /// mostró que no: de 476 regiones quedó <b>una sola</b> con hueco interno
    /// —<c>arma.espadaDePlata.norte</c>, cuadro 4 vacío y cuadro 5 con
    /// contenido—. Un caso aislado en un arma que tiene que verse siempre es un
    /// agujero del arte, no una decisión.</para>
    ///
    /// <para>Y el costo de equivocarse es asimétrico: sacar una pausa real acorta
    /// una animación en un frame y no lo nota nadie; dejar un hueco hace que el
    /// arma <b>desaparezca y reaparezca</b>, que se ve enseguida.</para>
    ///
    /// <para>Se lee la hoja YA COPIADA y no la original, para que el chequeo mida
    /// exactamente lo que va a ver el juego.</para>
    /// </remarks>
    private static List<GrhEntry> RecortarVacios(
        IReadOnlyList<GrhEntry> cuadros, int numeroHoja, out int descartados)
    {
        descartados = 0;

        string ruta = Path.Combine(CarpetaDestino, numeroHoja + ".PNG");
        var imagen = File.Exists(ruta) ? Image.LoadFromFile(ruta) : null;

        if (imagen is null)
            return [.. cuadros];

        const int k = GraphicsIndexReader.Canvas;
        var salida = new List<GrhEntry>(cuadros.Count);

        foreach (var c in cuadros)
        {
            if (TieneContenido(imagen, c.X * k, c.Y * k, c.Width * k, c.Height * k))
                salida.Add(c);
            else
                descartados++;
        }

        return salida;
    }

    /// <summary>¿La región tiene al menos unos pocos píxeles opacos?</summary>
    private static bool TieneContenido(Image imagen, int x, int y, int ancho, int alto)
    {
        if (x < 0 || y < 0 || x + ancho > imagen.GetWidth() || y + alto > imagen.GetHeight())
            return false;

        int opacos = 0;

        // De a 2 píxeles: alcanza para distinguir "vacío" de "hay un sprite", y
        // esto corre sobre cientos de cuadros.
        for (int j = 0; j < alto; j += 2)
        {
            for (int i = 0; i < ancho; i += 2)
            {
                if (imagen.GetPixel(x + i, y + j).A >= 0.5f && ++opacos >= 20)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Copia una hoja verificando que todos los cuadros caben adentro.
    /// </summary>
    private static bool CopiarHoja(
        int fileNumOrigen, int numeroNuevo, IReadOnlyList<GrhEntry> cuadros, out string error)
    {
        error = "";

        string? origen = BuscarHoja(fileNumOrigen);

        if (origen is null)
        {
            error = $"falta la hoja {fileNumOrigen}.PNG en la fuente";
            return false;
        }

        var (anchoHoja, altoHoja) = DimensionesPng(origen);

        if (anchoHoja == 0 || altoHoja == 0)
        {
            error = $"hoja {fileNumOrigen}: no se pudo leer el header PNG";
            return false;
        }

        const int k = GraphicsIndexReader.Canvas;

        // Cada cuadro tiene que caber. Con una animación es más importante que con
        // una imagen suelta: el último cuadro es el que más lejos está del origen,
        // y es justo el que un error de escala saca de la hoja.
        foreach (var c in cuadros)
        {
            if ((c.X * k) + (c.Width * k) > anchoHoja || (c.Y * k) + (c.Height * k) > altoHoja)
            {
                error = $"hoja {fileNumOrigen}: el cuadro grh {c.Grh} x4 " +
                        $"({c.X * k},{c.Y * k}) {c.Width * k}x{c.Height * k} " +
                        $"no cabe en {anchoHoja}x{altoHoja}";

                return false;
            }
        }

        string destino = Path.Combine(CarpetaDestino, numeroNuevo + ".PNG");

        Directory.CreateDirectory(CarpetaDestino);
        File.Copy(origen, destino, overwrite: true);

        if (new FileInfo(origen).Length != new FileInfo(destino).Length)
        {
            error = "la copia quedó incompleta";
            File.Delete(destino);
            return false;
        }

        return true;
    }

    // -------------------------------------------------------------------------
    //  La copia, con sus tres chequeos
    // -------------------------------------------------------------------------

    private static bool Copiar(
        int fileNumOrigen, int numeroNuevo, GrhEntry region,
        string nombre, AoObject objeto, JsonObject graficos)
    {
        string? origen = BuscarHoja(fileNumOrigen);

        if (origen is null)
        {
            GD.PrintErr($"  ✗ falta la hoja {fileNumOrigen}.PNG en la fuente");
            return false;
        }

        // --- Chequeo 1: las dimensiones de la hoja, del header PNG ---
        var (anchoHoja, altoHoja) = DimensionesPng(origen);

        if (anchoHoja == 0 || altoHoja == 0)
        {
            GD.PrintErr($"  ✗ hoja {fileNumOrigen}: no se pudo leer el header PNG");
            return false;
        }

        const int k = GraphicsIndexReader.Canvas;

        int x = region.X * k;
        int y = region.Y * k;
        int ancho = region.Width * k;
        int alto = region.Height * k;

        // --- Chequeo 2: la región cabe adentro de la hoja ---
        //
        // ⚠ Es el chequeo que atrapa el error de escala. El .ind está en 1x y las
        // hojas son x4; si alguien copiara las coordenadas sin multiplicar, la
        // región sería un cuarto del sprite y esto NO lo notaría —cabe de sobra—.
        // Al revés sí: multiplicar de más se sale de la hoja y salta acá.
        if (x + ancho > anchoHoja || y + alto > altoHoja)
        {
            GD.PrintErr(
                $"  ✗ hoja {fileNumOrigen}: la región x4 ({x},{y}) {ancho}x{alto} " +
                $"no cabe en {anchoHoja}x{altoHoja}");

            return false;
        }

        string destino = Path.Combine(CarpetaDestino, numeroNuevo + ".PNG");

        Directory.CreateDirectory(CarpetaDestino);
        File.Copy(origen, destino, overwrite: true);

        // --- Chequeo 3: llegó completo ---
        //
        // Comparar los bytes y no solo que exista: una copia cortada por falta de
        // espacio deja un archivo válido y más chico, y el PNG truncado falla
        // recién al cargarse.
        long bytesOrigen = new FileInfo(origen).Length;
        long bytesDestino = new FileInfo(destino).Length;

        if (bytesOrigen != bytesDestino)
        {
            GD.PrintErr($"  ✗ la copia quedó incompleta: {bytesOrigen} → {bytesDestino} bytes");
            File.Delete(destino);
            return false;
        }

        var (anchoCopia, altoCopia) = DimensionesPng(destino);

        if (anchoCopia != anchoHoja || altoCopia != altoHoja)
        {
            GD.PrintErr($"  ✗ la copia tiene otras dimensiones: {anchoCopia}x{altoCopia}");
            File.Delete(destino);
            return false;
        }

        // ⚠ La entrada la escribe Anotar, que es el ÚNICO lugar donde se arma.
        // Con dos formas de escribirla —una acá y otra en el camino del reuso—
        // se desincronizan en cuanto alguien agregue un campo.
        Anotar(graficos, nombre, numeroNuevo, region, objeto, fileNumOrigen);

        return true;
    }

    // -------------------------------------------------------------------------
    //  Plomería
    // -------------------------------------------------------------------------

    /// <summary>
    /// Busca la hoja probando las dos capitalizaciones.
    /// </summary>
    /// <remarks>
    /// ⚠ De las 1312 hojas de la fuente, <b>41 terminan en <c>.png</c>
    /// minúscula</b>. En Windows da igual; en Linux son archivos distintos, y un
    /// glob que no lo contemple las deja afuera sin decir nada. Al copiar se
    /// normaliza siempre a <c>.PNG</c>.
    /// </remarks>
    private static string? BuscarHoja(int fileNum)
    {
        foreach (string extension in new[] { ".PNG", ".png" })
        {
            string ruta = Path.Combine(GraphicsInspector.SheetsFolder, fileNum + extension);

            if (File.Exists(ruta))
                return ruta;
        }

        return null;
    }

    /// <summary>
    /// Ancho y alto de un PNG, del header.
    /// </summary>
    /// <remarks>
    /// El IHDR de un PNG está siempre en el mismo lugar: firma de 8 bytes, largo
    /// y tipo de chunk (8 más), y ahí ancho y alto como enteros de 32 bits
    /// <b>big-endian</b> — al revés que todo lo demás en x86, así que hay que dar
    /// vuelta los bytes.
    /// </remarks>
    private static (int Width, int Height) DimensionesPng(string ruta)
    {
        try
        {
            using var f = File.OpenRead(ruta);
            var cabecera = new byte[24];

            if (f.Read(cabecera, 0, 24) != 24)
                return (0, 0);

            // Firma PNG: 89 50 4E 47.
            if (cabecera[0] != 0x89 || cabecera[1] != 0x50 ||
                cabecera[2] != 0x4E || cabecera[3] != 0x47)
            {
                return (0, 0);
            }

            int ancho = (cabecera[16] << 24) | (cabecera[17] << 16) | (cabecera[18] << 8) | cabecera[19];
            int alto = (cabecera[20] << 24) | (cabecera[21] << 16) | (cabecera[22] << 8) | cabecera[23];

            return (ancho, alto);
        }
        catch (IOException)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Con qué número quedó una hoja de origen, si ya se trajo.
    /// </summary>
    /// <remarks>
    /// Busca por el <c>origen</c>, que es el único lugar donde queda escrito de
    /// qué hoja de Austral salió cada archivo. Es la segunda razón por la que ese
    /// campo existe, además de la trazabilidad: sin él no habría forma de saber
    /// que dos ítems comparten gráfico.
    /// </remarks>
    private static int NumeroDeHoja(JsonObject graficos, int fileNumOrigen)
    {
        // ⚠ El `hoja=N` va siempre ÚLTIMO en el origen, y por eso alcanza con
        // EndsWith. Comparar con Contains sería un bug sutil: «hoja=1» matchearía
        // dentro de «hoja=12137» y dos gráficos distintos compartirían archivo.
        string marca = $"hoja={fileNumOrigen}";

        foreach (var (_, valor) in graficos)
        {
            string? origen = valor?["origen"]?.GetValue<string>();

            if (origen is not null && origen.EndsWith(marca, System.StringComparison.Ordinal))
                return valor!["fileNum"]!.GetValue<int>();
        }

        return 0;
    }

    /// <summary>Anota una entrada estática en el índice, sin copiar nada.</summary>
    private static void Anotar(
        JsonObject graficos, string nombre, int numero,
        GrhEntry region, AoObject objeto, int fileNumOrigen)
    {
        AnotarCuadros(
            graficos, nombre, numero, [region], 0,
            $"AO obj={objeto.Id} «{objeto.Name}» grh={region.Grh} hoja={fileNumOrigen}");
    }

    /// <summary>
    /// Anota una entrada de uno o varios cuadros.
    /// </summary>
    /// <remarks>
    /// <para>Un gráfico estático lleva <c>x/y/w/h</c> sueltos; uno animado lleva
    /// además <c>regiones</c>, un array con un <c>[x,y,w,h]</c> por cuadro. Los
    /// campos sueltos son los del <b>primer</b> cuadro y quedan siempre, así que
    /// quien solo quiera una imagen fija no tiene que mirar el array.</para>
    ///
    /// <para>⚠ Los cuadros de una animación pueden tener <b>anchos distintos</b>
    /// —el arco compuesto va de 92 a 104 px según el cuadro— así que no alcanza
    /// con guardar el primero y correrlo de a pasos iguales. Cada región va
    /// entera.</para>
    /// </remarks>
    private static void AnotarCuadros(
        JsonObject graficos, string nombre, int numero,
        IReadOnlyList<GrhEntry> cuadros, int velocidadMs, string origen)
    {
        if (cuadros.Count == 0)
            return;

        const int k = GraphicsIndexReader.Canvas;
        var primero = cuadros[0];

        var entrada = new JsonObject
        {
            ["fileNum"] = numero,
            ["x"] = primero.X * k,
            ["y"] = primero.Y * k,
            ["w"] = primero.Width * k,
            ["h"] = primero.Height * k,
            ["frames"] = cuadros.Count,
        };

        if (cuadros.Count > 1)
        {
            var regiones = new JsonArray();

            foreach (var c in cuadros)
            {
                regiones.Add(new JsonArray(c.X * k, c.Y * k, c.Width * k, c.Height * k));
            }

            entrada["regiones"] = regiones;

            // La velocidad viene del .ind y es la duración de la animación
            // ENTERA, en milisegundos. Se guarda por cuadro, que es lo que hace
            // falta al dibujar.
            entrada["msPorFrame"] = velocidadMs > 0 ? velocidadMs / cuadros.Count : 120;
        }

        entrada["origen"] = origen;

        graficos[nombre] = entrada;
    }

    /// <summary>El próximo número libre. Consecutivo desde 1.</summary>
    private static int ProximoNumero(JsonObject graficos)
    {
        int maximo = 0;

        foreach (var (_, valor) in graficos)
        {
            int n = valor?["fileNum"]?.GetValue<int>() ?? 0;
            maximo = System.Math.Max(maximo, n);
        }

        return maximo + 1;
    }

    /// <summary>
    /// El nombre semántico de un objeto: <c>item.arcoCompuesto</c>.
    /// </summary>
    /// <remarks>
    /// Sale del nombre de AO, no del id, porque <b>dos clases comparten ítems</b>
    /// —la Daga+2 la usan el asesino y el ladrón— y con un nombre por clase la
    /// misma hoja entraría dos veces.
    /// </remarks>
    private static string NameOf(AoObject o) => ClaveDe("item.", o.Name);

    /// <summary>
    /// Arma una clave del índice a partir de un nombre de Austral.
    /// </summary>
    /// <remarks>
    /// Los nombres de AO traen de todo —paréntesis, barras, acentos,
    /// <c>"Túnica Azul (H/E/EO-H/M)"</c>—. La clave es un identificador, así que
    /// sale en camelCase sin acentos y con cada separador cortando palabra.
    /// </remarks>
    private static string ClaveDe(string prefijo, string nombre)
    {
        var limpio = new StringBuilder(prefijo);
        bool mayuscula = false;

        foreach (char c in SinAcentos(nombre))
        {
            if (char.IsLetterOrDigit(c))
            {
                limpio.Append(mayuscula ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                mayuscula = false;
            }
            else
            {
                // Todo lo que no sea letra o número corta palabra: los nombres de
                // AO traen paréntesis y barras —"Túnica Azul (H/E/EO-H/M)"— y eso
                // no puede entrar en una clave.
                mayuscula = true;
            }
        }

        return limpio.ToString();
    }

    /// <summary>
    /// Saca los acentos.
    /// </summary>
    /// <remarks>
    /// Solo para la CLAVE del índice, que es un identificador. Los textos que ve
    /// el jugador van en español con acentos (regla de oro 4); una clave con
    /// tilde es una que alguien va a escribir mal.
    /// </remarks>
    private static string SinAcentos(string s)
    {
        var salida = new StringBuilder(s.Length);

        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                salida.Append(c);
            }
        }

        return salida.ToString();
    }

    /// <summary>
    /// Borra las hojas que quedaron sin dueño y renumera el resto de 1 a N.
    /// </summary>
    /// <remarks>
    /// <para>Es la segunda mitad de <see cref="Forget"/>. Aquel borra las entradas
    /// del índice y solo se anima a borrar hojas <b>del final</b>: sacar una del
    /// medio deja un agujero, y taparlo obliga a renumerar todo lo que viene
    /// después. Esto hace exactamente eso, y por eso va en un comando aparte que
    /// se corre cuando se terminó de sacar cosas, no una vez por cada olvido.</para>
    ///
    /// <para>El invariante que restablece lo declara el propio índice en
    /// <c>_numeracion</c>: <i>«Los fileNum arrancan en 1 y son consecutivos»</i>.
    /// La trazabilidad al gráfico de Austral no se pierde al renumerar porque vive
    /// en <c>origen</c>, no en el <c>fileNum</c>.</para>
    ///
    /// <para>⚠ <b>Las hojas ajenas no se tocan.</b> Es la misma guarda que
    /// <see cref="Forget"/>: arriba de <c>graficos.Count + 1</c> están las hojas
    /// que el usuario dejó a mano con la numeración original de Austral para
    /// elegirlas, y ni se borran ni se renumeran.</para>
    ///
    /// <para>El renombre va en orden ascendente y el destino siempre es menor o
    /// igual que el origen, así que no se pisa nada. Si aun así el destino existe
    /// —una hoja ajena parada justo ahí— se corta sin escribir el índice: es
    /// preferible dejar los agujeros a mezclar dos numeraciones.</para>
    /// </remarks>
    public static void Compact()
    {
        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        GD.Print("── Acomodar las hojas ─────────────────────────────");

        var usados = new SortedSet<int>();

        foreach (var (_, valor) in graficos)
        {
            if (valor?["fileNum"] is not null)
                usados.Add((int)valor["fileNum"]!);
        }

        int techoPropio = graficos.Count + 1;

        var enDisco = new List<int>();

        if (Directory.Exists(CarpetaDestino))
        {
            foreach (string ruta in Directory.GetFiles(CarpetaDestino, "*.PNG"))
            {
                if (int.TryParse(Path.GetFileNameWithoutExtension(ruta), out int n))
                    enDisco.Add(n);
            }
        }

        enDisco.Sort();

        // Primero las huérfanas: si no se borran antes de renumerar, el hueco que
        // dejan no se llena y la cuenta final no da consecutiva.
        int borradas = 0;

        foreach (int n in enDisco)
        {
            if (n > techoPropio || usados.Contains(n))
                continue;

            string ruta = Path.Combine(CarpetaDestino, n + ".PNG");

            if (!File.Exists(ruta))
                continue;

            File.Delete(ruta);
            borradas++;
            GD.Print($"  ✗ hoja {n} borrada (sin dueño)");
        }

        // Ahora el corrimiento. `usados` viene ordenado, así que el destino de la
        // i-ésima hoja es i+1 y nunca es mayor que su número actual.
        var mapa = new Dictionary<int, int>();
        int siguiente = 1;

        foreach (int viejo in usados)
        {
            if (viejo > techoPropio)
            {
                GD.PrintErr($"  ⚠ la hoja {viejo} está fuera de la numeración propia " +
                            $"y se deja como está");
                continue;
            }

            mapa[viejo] = siguiente++;
        }

        int movidas = 0;

        foreach (var (viejo, nuevo) in mapa)
        {
            if (viejo == nuevo)
                continue;

            string origen = Path.Combine(CarpetaDestino, viejo + ".PNG");
            string destino = Path.Combine(CarpetaDestino, nuevo + ".PNG");

            if (!File.Exists(origen))
            {
                GD.PrintErr($"  ⚠ el índice apunta a la hoja {viejo} y no está en disco");
                continue;
            }

            if (File.Exists(destino))
            {
                GD.PrintErr($"  ✗ la hoja {nuevo} ya existe y no es de esta renumeración. " +
                            $"Se corta sin escribir el índice.");
                return;
            }

            File.Move(origen, destino);
            movidas++;
        }

        // El índice se toca al final y de una: si algo falló arriba, el JSON quedó
        // como estaba y se puede volver a correr.
        foreach (var (_, valor) in graficos)
        {
            if (valor?["fileNum"] is null)
                continue;

            if (mapa.TryGetValue((int)valor["fileNum"]!, out int nuevo))
                valor["fileNum"] = nuevo;
        }

        GuardarIndice(json);

        GD.Print($"  ✓ {borradas} hojas borradas · {movidas} renumeradas · " +
                 $"{mapa.Count} hojas del 1 al {mapa.Count}");
        GD.Print("───────────────────────────────────────────────────");
    }

    /// <summary>
    /// Recorta y empaqueta una hoja, <b>bajando los píxeles sin cambiar cuánto se
    /// ve</b>.
    /// </summary>
    /// <remarks>
    /// El <c>divisor</c> 2 deja el arte en x2 —la mitad de lado, un cuarto de
    /// píxeles— y compensa con el campo <c>escala</c>, así que el sprite se sigue
    /// viendo igual. Es una optimización. Para achicar el bicho <b>de verdad</b>
    /// está <see cref="ShrinkSheet"/>.
    /// </remarks>
    public static void RescaleSheet(int fileNum, int divisor)
        => Rehacer([fileNum], divisor <= 0 ? 100 : 100 / divisor, compensarEscala: true);

    /// <summary>
    /// Achica de verdad un bicho: menos píxeles <b>y</b> más chico en pantalla.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es lo contrario de <see cref="RescaleSheet"/>, aunque las dos
    /// resamplean.</b> Aquella baja los píxeles y <b>compensa</b> con el campo
    /// <c>escala</c>, así que el sprite se sigue viendo igual. Esta baja los
    /// píxeles y <b>no compensa</b>, así que el bicho se ve más chico. Confundirlas
    /// es fácil y el síntoma es mudo —el arte cambia de tamaño en disco y en
    /// pantalla no pasa nada, o al revés—, por eso son dos comandos y no un
    /// flag.</para>
    ///
    /// <para>El porcentaje es sobre el tamaño actual: 75 deja al bicho en tres
    /// cuartos. El número sale de mirarlo caminar en
    /// <c>Scenes\SpritePreview.tscn</c>, que aplica el mismo porcentaje en vivo
    /// con <c>+</c> y <c>-</c>.</para>
    ///
    /// <para>⚠ <b>Resamplear encima de lo resampleado pierde definición.</b> Cada
    /// pasada interpola sobre la anterior, así que conviene tantear en la escena
    /// —que no toca el disco— y aplicar una sola vez el número final, en vez de ir
    /// bajando de a diez.</para>
    /// </remarks>
    public static void ShrinkSheet(int fileNum, int porcentaje)
    {
        if (porcentaje is < 5 or > 100)
        {
            GD.PrintErr("  ✗ el porcentaje va entre 5 y 100 (75 = tres cuartos)");
            return;
        }

        Rehacer([fileNum], porcentaje, compensarEscala: false);
    }

    /// <summary>
    /// Junta varias hojas en una sola, recortando y empaquetando.
    /// </summary>
    /// <remarks>
    /// <para>Austral guarda una hoja por dirección cuando el bicho es grande: el
    /// Oso Polar son cuatro archivos de 1810×518, uno por lado, y en el juego
    /// nunca se dibuja uno sin los otros tres. Cuatro texturas donde va una son
    /// cuatro cargas de disco, cuatro entradas en el diccionario de hojas y —lo
    /// que importa para el atlas— cuatro rectángulos que empaquetar en vez de
    /// uno.</para>
    ///
    /// <para>Es el mismo trabajo que <see cref="RescaleSheet"/> con más de una
    /// hoja de entrada, así que comparten cuerpo: lo único que agrega es que el
    /// rectángulo se identifica por <b>hoja + posición</b> y no solo por posición
    /// —dos hojas distintas tienen un (0,0) cada una—.</para>
    /// </remarks>
    public static void MergeSheets(int[] fileNums, int divisor)
    {
        if (fileNums.Length < 2)
        {
            GD.PrintErr("  ✗ hacen falta al menos dos hojas para fusionar");
            return;
        }

        Rehacer([.. fileNums], divisor <= 0 ? 100 : 100 / divisor, compensarEscala: true);
    }

    /// <summary>
    /// Rehace un conjunto de hojas: recorta lo que se usa, lo escala y lo empaqueta
    /// pegado en una hoja nueva.
    /// </summary>
    /// <remarks>
    /// <para>Las hojas de Austral vienen del atlas del MMO y traen la grilla
    /// entera aunque nosotros usemos cuatro regiones. La del Dragón Rojo medía
    /// 8192×8192: son <b>256 MB de VRAM en RGBA8</b> para dibujar un cuadro, y
    /// muchos Android con GLES3 topean la textura en 4096 y directamente no la
    /// pueden crear. Esto arma una hoja nueva del tamaño de lo que se usa.</para>
    ///
    /// <para>No hay arte x2 guardado en <c>asset-legacy</c>: hay x4
    /// (<c>N.PNG</c>) y 1x (<c>N.PNG.orig</c>), y el x2 se fabrica bajando el x4.
    /// Subir el 1x se ve peor, que es lo que dice INTERFAZ.md §2.</para>
    ///
    /// <para><c>compensarEscala</c> es lo único que separa las dos herramientas
    /// que llaman acá: con él, la entrada anota que ahora está en x2 y el renderer
    /// dibuja al doble, así que el sprite queda igual; sin él, la entrada sigue
    /// diciendo x4 y el bicho se achica en pantalla.</para>
    ///
    /// <para>⚠ <b>Se empaqueta con dos píxeles de aire entre cuadro y cuadro.</b>
    /// El filtro es lineal y un <c>AtlasTexture</c> pegado al vecino le chupa una
    /// fila de píxeles al interpolar: el cuadro aparece con una costura del color
    /// del de al lado.</para>
    /// </remarks>
    private static void Rehacer(
        List<int> hojas, int porcentaje, bool compensarEscala, List<string>? soloEstas = null)
    {
        if (porcentaje is < 1 or > 100)
        {
            GD.PrintErr($"  ✗ porcentaje fuera de rango: {porcentaje}");
            return;
        }

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        string cuales = string.Join(" + ", hojas);
        GD.Print($"── Rehacer la hoja {cuales} al {porcentaje} % ──────────────");

        // Las entradas que viven en estas hojas. Se juntan TODAS: las cuatro
        // direcciones de un bicho suelen compartir hoja, y rehacer una sola
        // dejaría la vieja con dueño y no se podría borrar.
        var entradas = new List<(string Clave, JsonObject Valor)>();

        foreach (var (clave, valor) in graficos)
        {
            if (valor?["fileNum"] is null || !hojas.Contains((int)valor["fileNum"]!))
                continue;

            // El filtro es para partir: se rehace un subconjunto de las entradas de
            // la hoja y el resto se queda donde está.
            if (soloEstas is not null && !soloEstas.Contains(clave))
                continue;

            entradas.Add((clave, valor.AsObject()));
        }

        if (entradas.Count == 0)
        {
            GD.PrintErr($"  ✗ ninguna entrada usa {cuales}");
            return;
        }

        // ⚠ Todas las entradas tienen que estar en la MISMA escala.
        //
        // La hoja nueva es una sola y lleva una sola escala, así que fusionar una
        // entrada en x4 con otra en x2 dejaría a la segunda dibujándose al doble
        // o a la mitad, sin un aviso. Pasa en cuanto alguien fusione después de
        // haber reescalado una parte, que es exactamente el orden en que se
        // trabajó el Dragón Rojo.
        int escalaVieja = EscalaDe(entradas[0].Valor);

        foreach (var (clave, valor) in entradas)
        {
            if (EscalaDe(valor) == escalaVieja)
                continue;

            GD.PrintErr($"  ✗ {clave} está en x{EscalaDe(valor)} y el resto en " +
                        $"x{escalaVieja}. No se fusionan escalas distintas.");

            return;
        }

        var origenes = new Dictionary<int, Image>();
        long pixelesAntes = 0;
        long bytesAntes = 0;

        foreach (int n in hojas)
        {
            string ruta = Path.Combine(CarpetaDestino, n + ".PNG");

            if (!File.Exists(ruta))
            {
                GD.PrintErr($"  ✗ no está {ruta}");
                return;
            }

            var img = Image.LoadFromFile(ruta);

            if (img is null)
            {
                GD.PrintErr($"  ✗ no se pudo abrir {ruta}");
                return;
            }

            img.Convert(Image.Format.Rgba8);
            origenes[n] = img;

            pixelesAntes += (long)img.GetWidth() * img.GetHeight();
            bytesAntes += new FileInfo(ruta).Length;
        }

        // ⚠ El recorte al dibujo es SOLO para los bichos.
        //
        // Un defensor se arma apilando cuerpo + cabeza + arma, y la cabeza se
        // corre con el offset de `Personajes.ind`, que está medido contra la caja
        // original. Recortar esas cajas mueve las tres piezas cada una por su
        // cuenta y el muñeco se desarma. Los pisos ya quedan afuera antes de
        // llegar acá.
        bool soloBichos = true;

        foreach (var (clave, _) in entradas)
            soloBichos &= clave.StartsWith("bicho.", System.StringComparison.Ordinal);

        var recorte = soloBichos
            ? RecorteAlDibujo(entradas, origenes)
            : [];

        if (soloBichos && recorte.Count > 0)
            GD.Print($"  · recortado al dibujo en {recorte.Count} entrada(s)");

        // Un mismo rectángulo puede estar en varias entradas —las direcciones que
        // comparten cuadro—, así que se recorta una vez sola y todas apuntan ahí.
        //
        // ⚠ La clave lleva la hoja adelante. Sin eso, al fusionar, el (0,0) de la
        // hoja 116 se confunde con el (0,0) de la 115 y las cuatro direcciones del
        // Oso Polar terminan mostrando la misma.
        var rects = new List<(int Hoja, Rect2I R)>();
        var donde = new Dictionary<(int, Rect2I), int>();
        var recortadas = new Dictionary<string, List<Rect2I>>();

        foreach (var (clave, valor) in entradas)
        {
            int hoja = (int)valor["fileNum"]!;
            var lista = new List<Rect2I>();

            foreach (var r in RegionesDe(valor))
            {
                var efectiva = recorte.TryGetValue(clave, out var margen)
                    ? Aplicar(r, margen)
                    : r;

                lista.Add(efectiva);

                if (donde.ContainsKey((hoja, efectiva)))
                    continue;

                donde[(hoja, efectiva)] = rects.Count;
                rects.Add((hoja, efectiva));
            }

            recortadas[clave] = lista;
        }

        // Empaquetado por filas, los más altos primero: es el que menos aire deja
        // sin meterse en un packer de verdad, y acá los cuadros de un mismo bicho
        // miden todos casi lo mismo.
        //
        // ⚠ Los pisos van SIN aire, y eso depende de cómo se los dibuja.
        //
        // Los dos píxeles de separación existen porque un `AtlasTexture` con filtro
        // lineal le chupa una fila de píxeles al vecino y aparece una costura. Un
        // `piso.*` no pasa nunca por ahí: `BakedFloor` lo lee como `Image` con
        // `SheetImage` y lo copia con `BlitRect`, que es una copia exacta en CPU.
        // Sin filtrado no hay de dónde sangrar, y sin el aire cuatro bloques de
        // 512 entran justos en 1024×1024 en vez de necesitar 1036.
        //
        // Si algún día un piso se dibuja con `GraphicsLoader.Texture`, esto vuelve
        // a hacer falta.
        bool soloPisos = true;

        foreach (var (clave, _) in entradas)
            soloPisos &= clave.StartsWith("piso.", System.StringComparison.Ordinal);

        int Aire = soloPisos ? 0 : 2;

        // ⚠ El ancho sale del ÁREA, no de una constante.
        //
        // Con un ancho fijo la hoja crece solo para abajo, y eso es peor que no
        // empaquetar: el Dragón Rojo fusionado —48 cuadros— salió 1738×9818, y una
        // textura de 9818 de alto no la crea ninguna GPU de celular. Apuntando al
        // lado de un cuadrado la misma carga da algo del orden de 4100×4100, que
        // entra donde el tirón vertical no entraba.
        long areaTotal = 0;
        var tamanoDe = new Vector2I[rects.Count];

        for (int i = 0; i < rects.Count; i++)
        {
            int w = System.Math.Max(1, rects[i].R.Size.X * porcentaje / 100);
            int h = System.Math.Max(1, rects[i].R.Size.Y * porcentaje / 100);

            tamanoDe[i] = new Vector2I(w, h);
            areaTotal += (long)(w + Aire) * (h + Aire);
        }

        int anchoObjetivo = (int)System.Math.Sqrt(areaTotal * 1.05);

        // El piso es el cuadro más ancho: si el objetivo quedara por debajo, ese
        // cuadro no entraría en ninguna fila y el empaquetado no terminaría nunca.
        foreach (var t in tamanoDe)
            anchoObjetivo = System.Math.Max(anchoObjetivo, t.X + (Aire * 2));

        var orden = new List<int>(rects.Count);
        for (int i = 0; i < rects.Count; i++)
            orden.Add(i);

        orden.Sort((a, b) => tamanoDe[b].Y.CompareTo(tamanoDe[a].Y));

        var destinoDe = new Vector2I[rects.Count];

        int cursorX = Aire, cursorY = Aire, altoFila = 0, anchoUsado = 0;

        foreach (int i in orden)
        {
            if (cursorX + tamanoDe[i].X + Aire > anchoObjetivo && cursorX > Aire)
            {
                cursorX = Aire;
                cursorY += altoFila + Aire;
                altoFila = 0;
            }

            destinoDe[i] = new Vector2I(cursorX, cursorY);

            cursorX += tamanoDe[i].X + Aire;
            altoFila = System.Math.Max(altoFila, tamanoDe[i].Y);
            anchoUsado = System.Math.Max(anchoUsado, cursorX);
        }

        int anchoHoja = anchoUsado + Aire;
        int altoHoja = cursorY + altoFila + Aire;

        var salida = Image.CreateEmpty(anchoHoja, altoHoja, false, Image.Format.Rgba8);

        for (int i = 0; i < rects.Count; i++)
        {
            var trozo = origenes[rects[i].Hoja].GetRegion(rects[i].R);

            if (tamanoDe[i] != rects[i].R.Size)
                trozo.Resize(tamanoDe[i].X, tamanoDe[i].Y, Image.Interpolation.Lanczos);

            salida.BlitRect(trozo, new Rect2I(Vector2I.Zero, trozo.GetSize()), destinoDe[i]);
        }

        // La hoja nueva va al final de la numeración. Compactar es trabajo de
        // --acomodar-graficos, que ya sabe hacerlo sin romper el resto.
        int numeroNuevo = 1;

        foreach (var (_, valor) in graficos)
        {
            if (valor?["fileNum"] is not null)
                numeroNuevo = System.Math.Max(numeroNuevo, (int)valor["fileNum"]! + 1);
        }

        string destinoRuta = Path.Combine(CarpetaDestino, numeroNuevo + ".PNG");
        var error = salida.SavePng(destinoRuta);

        if (error != Error.Ok)
        {
            GD.PrintErr($"  ✗ no se pudo escribir {destinoRuta}: {error}");
            return;
        }

        int escalaNueva = compensarEscala
            ? System.Math.Max(1, escalaVieja * porcentaje / 100)
            : escalaVieja;

        foreach (var (clave, valor) in entradas)
        {
            int hoja = (int)valor["fileNum"]!;
            var lista = recortadas[clave];
            var nuevas = new JsonArray();

            foreach (var r in lista)
            {
                int i = donde[(hoja, r)];

                nuevas.Add(new JsonArray(
                    destinoDe[i].X, destinoDe[i].Y, tamanoDe[i].X, tamanoDe[i].Y));
            }

            int i0 = donde[(hoja, lista[0])];

            valor["fileNum"] = numeroNuevo;
            valor["x"] = destinoDe[i0].X;
            valor["y"] = destinoDe[i0].Y;
            valor["w"] = tamanoDe[i0].X;
            valor["h"] = tamanoDe[i0].Y;
            valor["escala"] = escalaNueva;
            valor["regiones"] = nuevas;

            GD.Print($"  ✓ {clave}  {lista.Count} cuadros");
        }

        GuardarIndice(json);

        long despues = new FileInfo(destinoRuta).Length;

        GD.Print($"  {hojas.Count} hoja(s) → hoja {numeroNuevo} {anchoHoja}x{altoHoja} " +
                 $"· escala x{escalaNueva}");

        GD.Print($"  disco {bytesAntes / 1024} KB → {despues / 1024} KB · " +
                 $"VRAM {pixelesAntes * 4 / 1048576} MB → " +
                 $"{(long)anchoHoja * altoHoja * 4 / 1048576} MB");

        GD.Print("  · la hoja vieja queda sin dueño: corré --acomodar-graficos");
        GD.Print("───────────────────────────────────────────────────");
    }

    /// <summary>
    /// Cuánto sobra de transparente alrededor del dibujo de cada entrada.
    /// </summary>
    /// <remarks>
    /// <para>Arregla un bug que se ve caminando: el sprite se ancla al <b>borde
    /// inferior de la caja</b>, y en el arte de Austral la caja no está ajustada
    /// al dibujo. La Tortuga Gigante de perfil tiene 43 píxeles transparentes
    /// abajo contra 12 de frente, así que al ir de izquierda a derecha apoyaba
    /// treinta píxeles más arriba que al ir de arriba abajo, flotando sobre el
    /// camino.</para>
    ///
    /// <para>⚠ <b>El margen es de la ENTRADA, no del cuadro.</b> Recortar cada
    /// cuadro por su cuenta le saca el movimiento a la animación: el ciclo de
    /// caminar de AO sube y baja al bicho unos píxeles, y eso <b>es</b> el
    /// movimiento. Se toma el rectángulo que cubre a todos los cuadros de esa
    /// dirección, así que el bicho queda apoyado y el balanceo queda.</para>
    ///
    /// <para>El umbral de alfa no es cero: el arte HD es un upscale suavizado y
    /// deja un halo de alfa 1-3 alrededor del dibujo que, contado, devuelve la caja
    /// entera y no recorta nada.</para>
    /// </remarks>
    /// <returns>Por clave, cuánto sacar de cada lado: izquierda, arriba, derecha, abajo.</returns>
    private static Dictionary<string, (int L, int T, int R, int B)> RecorteAlDibujo(
        List<(string Clave, JsonObject Valor)> entradas, Dictionary<int, Image> origenes)
    {
        var salida = new Dictionary<string, (int, int, int, int)>();

        foreach (var (clave, valor) in entradas)
        {
            if (valor["fileNum"] is null || !origenes.TryGetValue((int)valor["fileNum"]!, out var hoja))
                continue;

            var regiones = RegionesDe(valor);

            int menorL = int.MaxValue, menorT = int.MaxValue;
            int menorR = int.MaxValue, menorB = int.MaxValue;

            foreach (var r in regiones)
            {
                int minX = int.MaxValue, maxX = int.MinValue;
                int minY = int.MaxValue, maxY = int.MinValue;

                for (int y = 0; y < r.Size.Y; y++)
                {
                    for (int x = 0; x < r.Size.X; x++)
                    {
                        if (hoja.GetPixel(r.Position.X + x, r.Position.Y + y).A <= 0.05f)
                            continue;

                        if (x < minX)
                            minX = x;

                        if (x > maxX)
                            maxX = x;

                        if (y < minY)
                            minY = y;

                        if (y > maxY)
                            maxY = y;
                    }
                }

                // Un cuadro vacío no aporta margen y no puede recortar a los otros.
                if (minX > maxX)
                    continue;

                menorL = System.Math.Min(menorL, minX);
                menorT = System.Math.Min(menorT, minY);
                menorR = System.Math.Min(menorR, r.Size.X - 1 - maxX);
                menorB = System.Math.Min(menorB, r.Size.Y - 1 - maxY);
            }

            if (menorL == int.MaxValue)
                continue;

            if (menorL + menorT + menorR + menorB == 0)
                continue;

            salida[clave] = (menorL, menorT, menorR, menorB);
        }

        return salida;
    }

    /// <summary>Le saca el margen a un rectángulo, sin dejarlo vacío.</summary>
    private static Rect2I Aplicar(Rect2I r, (int L, int T, int R, int B) m)
    {
        int w = System.Math.Max(1, r.Size.X - m.L - m.R);
        int h = System.Math.Max(1, r.Size.Y - m.T - m.B);

        return new Rect2I(r.Position.X + m.L, r.Position.Y + m.T, w, h);
    }

    /// <summary>
    /// Parte una hoja en varias, repartiendo sus entradas.
    /// </summary>
    /// <remarks>
    /// <para>Es el inverso de <see cref="MergeSheets"/>, y existe porque fusionar
    /// no siempre entra. El Dragón Rojo son 48 cuadros y a x2 juntos dan
    /// 4050×4436: pasan el techo de 4096 que tiene la textura en buena parte de
    /// los Android, así que la hoja fusionada no se puede crear. Partido en dos
    /// cada mitad queda cómoda, y <b>no hace falta tocar la escala</b> —que es lo
    /// que no se puede decidir hasta ver el bicho en pantalla—.</para>
    ///
    /// <para>Reparte por <b>entradas enteras</b>, nunca por cuadros: los cuadros de
    /// una dirección tienen que quedar juntos porque comparten el
    /// <c>AtlasTexture</c> de su hoja.</para>
    /// </remarks>
    public static void SplitSheet(int fileNum, int partes)
    {
        if (partes < 2)
        {
            GD.PrintErr("  ✗ hacen falta al menos dos partes");
            return;
        }

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        var claves = new List<string>();

        foreach (var (clave, valor) in graficos)
        {
            if (valor?["fileNum"] is not null && (int)valor["fileNum"]! == fileNum)
                claves.Add(clave);
        }

        if (claves.Count < partes)
        {
            GD.PrintErr($"  ✗ la hoja {fileNum} tiene {claves.Count} entrada(s): " +
                        $"no se puede partir en {partes}");

            return;
        }

        GD.Print($"── Partir la hoja {fileNum} en {partes} ──────────────");

        // Se reparte en tramos contiguos y no salteado: las direcciones de un bicho
        // vienen seguidas en el índice, así que cada parte se lleva un par que se
        // dibuja junto.
        int porParte = (claves.Count + partes - 1) / partes;

        for (int i = 0; i < claves.Count; i += porParte)
        {
            var tramo = claves.GetRange(i, System.Math.Min(porParte, claves.Count - i));
            Rehacer([fileNum], 100, compensarEscala: false, tramo);
        }

        GD.Print("───────────────────────────────────────────────────");
    }

    /// <summary>
    /// Le pasa <see cref="RescaleSheet"/> a todas las hojas del índice.
    /// </summary>
    /// <remarks>
    /// <para>Con <c>divisor 1</c> no achica nada: solo recorta y empaqueta. Es la
    /// mitad barata de la mejora, porque las hojas de Austral son en su mayoría
    /// aire —el atlas del MMO trae la grilla entera y nosotros usamos cuatro
    /// regiones—. Medido sobre este índice, de 547 millones de píxeles en disco se
    /// usan 150.</para>
    ///
    /// <para>Los pisos entran también. Se saltearon hasta el 2026-08-31 por miedo
    /// a las costuras del filtro lineal, hasta que se verificó que un <c>piso.*</c>
    /// no pasa nunca por un <c>AtlasTexture</c>: <c>BakedFloor</c> lo lee como
    /// <c>Image</c> y lo copia con <c>BlitRect</c>, que es una copia exacta en CPU.
    /// Van con cero aire, y por eso cuatro bloques de 512 entran justos en
    /// 1024×1024 en vez de necesitar 1036.</para>
    ///
    /// <para>La lista de hojas se saca <b>antes</b> de empezar. Cada rehecho crea
    /// una hoja nueva al final de la numeración, y sin la foto previa el recorrido
    /// se encontraría con las que él mismo va fabricando.</para>
    /// </remarks>
    public static void RescaleAll(int divisor)
    {
        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        var hojas = new SortedSet<int>();

        foreach (var (_, valor) in graficos)
        {
            if (valor?["fileNum"] is not null)
                hojas.Add((int)valor["fileNum"]!);
        }

        GD.Print($"── Rehacer las {hojas.Count} hojas ÷{divisor} ─────────────");

        int hechas = 0;

        foreach (int n in hojas)
        {
            RescaleSheet(n, divisor);
            hechas++;
        }

        GD.Print($"── {hechas} hojas rehechas · corré --acomodar-graficos ────");
    }

    /// <summary>
    /// Escribe el <c>anclaY</c> de las entradas que empiezan con un prefijo.
    /// </summary>
    /// <remarks>
    /// <para>El sprite se ancla al borde inferior de su región. Con la caja
    /// recortada al dibujo eso lo deja apoyado, pero apoyado en el borde de abajo
    /// de la celda no siempre <b>se ve</b> apoyado: una criatura de perfil es
    /// ancha y baja, y su masa queda por encima de la línea del camino. La Tortuga
    /// Gigante yendo de izquierda a derecha fue el caso que lo destapó.</para>
    ///
    /// <para>Es corrección de render y por <b>entrada</b> —o sea, por dirección—,
    /// porque el problema es por dirección: de frente el mismo bicho se ve bien.
    /// Positivo baja. No toca ningún píxel: solo escribe el número en el
    /// índice.</para>
    ///
    /// <para>El valor se tantea en <c>Scenes\SpritePreview.tscn</c> con <c>[</c> y
    /// <c>]</c>, que muestran el total en el panel.</para>
    /// </remarks>
    public static void AnchorGraphic(string prefijo, int px)
    {
        if (string.IsNullOrWhiteSpace(prefijo))
        {
            GD.PrintErr("  ✗ hace falta una clave, por ejemplo «bicho.tortugaGigante.este»");
            return;
        }

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        GD.Print($"── Ancla de «{prefijo}» = {px} px ────────────────");

        int tocadas = 0;

        foreach (var (clave, valor) in graficos)
        {
            if (!clave.StartsWith(prefijo, System.StringComparison.Ordinal) || valor is null)
                continue;

            // El cero se borra en vez de escribirse: una entrada sin el campo y una
            // con `anclaY: 0` valen lo mismo, y dejarlo escrito ensucia el índice
            // con ruido que después hay que leer.
            if (px == 0)
                valor.AsObject().Remove("anclaY");
            else
                valor["anclaY"] = px;

            tocadas++;
            GD.Print($"  ✓ {clave}");
        }

        if (tocadas == 0)
        {
            GD.PrintErr("  ✗ no hay ninguna entrada con ese prefijo");
            return;
        }

        GuardarIndice(json);

        GD.Print($"  {tocadas} entrada(s)");
        GD.Print("───────────────────────────────────────────────────");
    }

    /// <summary>Los rectángulos de una entrada, con el suelto como respaldo.</summary>
    private static List<Rect2I> RegionesDe(JsonObject valor)
    {
        var salida = new List<Rect2I>();

        if (valor["regiones"] is JsonArray arr && arr.Count > 0)
        {
            foreach (var nodo in arr)
            {
                if (nodo is JsonArray r && r.Count >= 4)
                    salida.Add(new Rect2I((int)r[0]!, (int)r[1]!, (int)r[2]!, (int)r[3]!));
            }
        }

        if (salida.Count == 0)
        {
            salida.Add(new Rect2I(
                (int)valor["x"]!, (int)valor["y"]!, (int)valor["w"]!, (int)valor["h"]!));
        }

        return salida;
    }

    /// <summary>La escala de una entrada. Sin el campo es x4, que es el default histórico.</summary>
    private static int EscalaDe(JsonObject valor)
        => valor["escala"] is not null ? (int)valor["escala"]! : 4;

    private static JsonNode CargarIndice()
    {
        if (File.Exists(RutaIndice))
        {
            string texto = File.ReadAllText(RutaIndice, Encoding.UTF8);
            var nodo = JsonNode.Parse(texto);

            if (nodo?["graficos"] is not null)
                return nodo;
        }

        return new JsonObject
        {
            ["version"] = 1,
            ["escala"] = 4,
            ["graficos"] = new JsonObject(),
        };
    }

    private static void GuardarIndice(JsonNode json)
    {
        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // UTF-8 SIN BOM: el BOM rompe parsers que leen el primer byte esperando
        // una llave.
        File.WriteAllText(
            RutaIndice,
            json.ToJsonString(opciones),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
