using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace TowerOfArgentum.Herramientas;

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
public static class TraerGrafico
{
    private static readonly string CarpetaDestino =
        Path.GetFullPath(Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "Resources"));

    private static string RutaIndice => Path.Combine(CarpetaDestino, "graficos.json");

    // -------------------------------------------------------------------------
    //  Traer uno
    // -------------------------------------------------------------------------

    public static void TraerItem(int idObjeto)
    {
        var objetos = InspectorGraficos.LeerObjetos();
        var indice = LectorGraficosInd.Leer();

        GD.Print($"── Traer objeto {idObjeto} ────────────────────────");

        if (!objetos.TryGetValue(idObjeto, out var o))
        {
            GD.PrintErr($"  ✗ no existe el objeto {idObjeto} en OBJ.DAT");
            return;
        }

        var region = LectorGraficosInd.Resolver(indice, o.GrhIndex);

        if (region is null)
        {
            GD.PrintErr($"  ✗ el grh {o.GrhIndex} de «{o.Name}» no resuelve a ninguna hoja");
            return;
        }

        var json = CargarIndice();
        var graficos = json["graficos"]!.AsObject();

        string nombre = NombreDe(o);

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
    public static void TraerPiso(int hojaOrigen, string nombre, int bloque)
    {
        if (hojaOrigen <= 0 || string.IsNullOrWhiteSpace(nombre))
        {
            GD.PrintErr("  ✗ uso: --traer-piso <hoja> <nombre> [bloque]");
            return;
        }

        var indice = LectorGraficosInd.Leer();

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = 0, maxY = 0;
        int cuantos = 0;

        foreach (var (_, e) in indice)
        {
            if (e.EsAnimacion || e.FileNum != hojaOrigen || e.Width != 32 || e.Height != 32)
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

        const int Lado = 32;
        const int PorBloque = 4;
        int anchoBloque = Lado * PorBloque;

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

        int k = LectorGraficosInd.Escala;

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

    public static void TraerTileset(int hojaOrigen, string nombre)
    {
        if (hojaOrigen <= 0 || string.IsNullOrWhiteSpace(nombre))
        {
            GD.PrintErr("  ✗ uso: --traer-tileset <hoja> <nombre>");
            return;
        }

        var indice = LectorGraficosInd.Leer();

        GD.Print($"── Traer tileset {hojaOrigen} como «{nombre}» ─────────────");

        // Las regiones de tile de esta hoja, en orden de grh.
        var grhs = new List<int>();

        foreach (var (grh, e) in indice)
        {
            if (!e.EsAnimacion && e.FileNum == hojaOrigen && e.Width == 32 && e.Height == 32)
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

        int k = LectorGraficosInd.Escala;
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
            string desde = Path.Combine(InspectorGraficos.CarpetaHojas, origen + extension);

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

    public static void TraerTiers()
    {
        var tiers = InspectorGraficos.LeerTiers();
        var objetos = InspectorGraficos.LeerObjetos();
        var indice = LectorGraficosInd.Leer();

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

            string nombre = NombreDe(o);

            if (graficos.ContainsKey(nombre))
            {
                saltados++;
                continue;
            }

            var region = LectorGraficosInd.Resolver(indice, o.GrhIndex);

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
    /// <item><b>Cuerpo</b>: <c>obj.NumRopaje</c> de la ARMADURA →
    /// <c>Personajes.ind</c> → 4 direcciones. El cuerpo que se ve lo da la
    /// armadura equipada, no el <c>Body</c> del personaje: es todo el punto de que
    /// haya tiers.</item>
    /// </list>
    /// </remarks>
    public static void TraerArteDeClases(int soloEsteObjeto = 0)
    {
        var tiers = InspectorGraficos.LeerTiers();
        var objetos = InspectorGraficos.LeerObjetos();
        var indice = LectorGraficosInd.Leer();
        var armas = LectorIndices.LeerArmas();
        var cuerpos = LectorIndices.LeerCuerpos();
        var cabezas = LectorIndices.LeerCabezas();

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
                    graficos, $"arma.{NombreCorto(o)}", dirs.Direcciones, indice,
                    $"AO obj={id} «{o.Name}» anim={o.Anim}");

                if (n > 0)
                    GD.Print($"  ✓ arma.{NombreCorto(o),-24} {n} direcciones   ({o.Name})");

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
                    graficos, $"cuerpo.{NombreCorto(o)}", cuerpo.Direcciones, indice,
                    $"AO obj={id} «{o.Name}» ropaje={o.NumRopaje} " +
                    $"cabeza=({cuerpo.OffsetCabezaX},{cuerpo.OffsetCabezaY})");

                if (n > 0)
                {
                    GD.Print($"  ✓ cuerpo.{NombreCorto(o),-22} {n} direcciones   " +
                             $"(ropaje {o.NumRopaje}, cabeza en " +
                             $"{cuerpo.OffsetCabezaX * LectorGraficosInd.Escala}," +
                             $"{cuerpo.OffsetCabezaY * LectorGraficosInd.Escala})");
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
    internal static string NombreCorto(ObjetoAo o) => NombreDe(o)["item.".Length..];

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
    private static readonly (string Name, int Cuerpo, int Cabeza, bool Baja)[] _razas =
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
    public static void TraerRazas()
    {
        var indice = LectorGraficosInd.Leer();
        var cuerpos = LectorIndices.LeerCuerpos();
        var cabezas = LectorIndices.LeerCabezas();

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
                int offsetX = cuerpo.OffsetCabezaX * LectorGraficosInd.Escala;
                int offsetY = cuerpo.OffsetCabezaY * LectorGraficosInd.Escala;

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

    public static void TraerArmadurasBajas()
    {
        var objetos = InspectorGraficos.LeerObjetos();
        var indice = LectorGraficosInd.Leer();
        var cuerpos = LectorIndices.LeerCuerpos();

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
                ? NombreCorto(alta)
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
    public static void TraerBestiario(int area)
    {
        var indice = LectorGraficosInd.Leer();
        var cuerpos = LectorIndices.LeerCuerpos();
        var enemigos = InspectorGraficos.LeerEnemigos();

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
    public static void Olvidar(string prefijo)
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
    /// <para>Direcciones: 1 = norte, 2 = este, 3 = oeste, 4 = sur.</para>
    /// </remarks>
    private static int TraerCuatroDirecciones(
        JsonObject graficos, string prefijo, int[] grhPorDireccion,
        Dictionary<int, EntradaGrh> indice, string origenBase)
    {
        string[] nombresDireccion = ["norte", "este", "oeste", "sur"];
        int traidos = 0;

        for (int d = 0; d < 4; d++)
        {
            int grh = grhPorDireccion[d];

            if (grh == 0)
                continue;

            string nombre = $"{prefijo}.{nombresDireccion[d]}";

            if (graficos.ContainsKey(nombre))
                continue;

            var cuadros = LectorGraficosInd.Cuadros(indice, grh);

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
    private static List<EntradaGrh> RecortarVacios(
        IReadOnlyList<EntradaGrh> cuadros, int numeroHoja, out int descartados)
    {
        descartados = 0;

        string ruta = Path.Combine(CarpetaDestino, numeroHoja + ".PNG");
        var imagen = File.Exists(ruta) ? Image.LoadFromFile(ruta) : null;

        if (imagen is null)
            return [.. cuadros];

        const int k = LectorGraficosInd.Escala;
        var salida = new List<EntradaGrh>(cuadros.Count);

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
        int fileNumOrigen, int numeroNuevo, IReadOnlyList<EntradaGrh> cuadros, out string error)
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

        const int k = LectorGraficosInd.Escala;

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
        int fileNumOrigen, int numeroNuevo, EntradaGrh region,
        string nombre, ObjetoAo objeto, JsonObject graficos)
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

        const int k = LectorGraficosInd.Escala;

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
            string ruta = Path.Combine(InspectorGraficos.CarpetaHojas, fileNum + extension);

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
        EntradaGrh region, ObjetoAo objeto, int fileNumOrigen)
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
        IReadOnlyList<EntradaGrh> cuadros, int velocidadMs, string origen)
    {
        if (cuadros.Count == 0)
            return;

        const int k = LectorGraficosInd.Escala;
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
    private static string NombreDe(ObjetoAo o) => ClaveDe("item.", o.Name);

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
