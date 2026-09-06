using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace TowerOfArgentum.Tools;

/// <summary>
/// Corta la hoja de piezas de interfaz en archivos sueltos.
/// </summary>
/// <remarks>
/// <para>La hoja se genera de una sola vez —piezas separadas sobre un fondo gris
/// plano— y esto la parte: detecta cada pieza por diferencia contra el color del
/// fondo, la exporta con el fondo en transparente y escribe un índice.</para>
///
/// <para><b>Por qué una herramienta y no recortar a mano.</b> Son 23 piezas, y
/// cada una necesita además su margen de <i>9-slice</i> medido. A mano eso es
/// media hora y varios errores de un píxel, y hay que rehacerlo entero cada vez
/// que se regenera la hoja. Acá se vuelve a correr el comando.</para>
///
/// <para>⚠ <b>El margen de 9-slice que calcula es una SUGERENCIA</b>, no una
/// medición exacta. Sale de buscar desde cada borde hacia adentro hasta que las
/// filas —o las columnas— dejan de cambiar, que es donde empieza el centro
/// plano. Acierta en paneles y botones; en las piezas con esquinas ornamentadas
/// conviene mirarlo y corregirlo a mano en el JSON. Es un número que se escribe
/// una vez y no se vuelve a tocar.</para>
///
/// <code>
/// --recortar-ui                       usa Resources\ui\pieces\sheet.png
/// --recortar-ui otra.png              otra hoja, en la misma carpeta
/// --recortar-ui sheet.png 24          con tolerancia de fondo a mano
/// </code>
/// </remarks>
public static class UiSlicer
{
    private static readonly string CarpetaResources =
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "Resources"));

    /// <summary>Dónde vive la hoja y dónde van a parar las piezas.</summary>
    private const string SubCarpeta = "ui/pieces";

    /// <summary>
    /// Cuánto se puede alejar un píxel del color del fondo y seguir siendo fondo.
    /// </summary>
    /// <remarks>
    /// ⚠ Es el único número delicado del archivo. Muy bajo deja un halo gris
    /// alrededor de cada pieza —el borde viene con antialiasing contra el fondo—;
    /// muy alto se come los píxeles oscuros del borde metálico. 20 sobre 255 anda
    /// bien con las hojas generadas hasta ahora, y se puede pisar por argumento.
    /// </remarks>
    private const int ToleranciaPorDefecto = 20;

    /// <summary>Menos píxeles que esto y es basura, no una pieza.</summary>
    private const int AreaMinima = 400;

    /// <summary>
    /// Los nombres, en el orden en que se leen: por filas de arriba abajo y de
    /// izquierda a derecha adentro de cada fila.
    /// </summary>
    /// <remarks>
    /// <para>Se aplican <b>solo si la cuenta coincide exactamente</b>. Si la hoja
    /// trae otra cantidad de piezas, salen numeradas y con un aviso: ponerle
    /// «button_hover» a lo que en realidad es una pestaña sería peor que no
    /// ponerle nombre.</para>
    ///
    /// <para>Van en inglés, como todo lo que se nombra en el proyecto.</para>
    /// </remarks>
    private static readonly string[] NombresEsperados =
    [
        // Fila 1 — paneles
        "panel", "panel_elevated",

        // Fila 2 — botones, los cuatro estados
        "button", "button_hover", "button_pressed", "button_disabled",

        // Fila 3 — pestañas
        "tab_active", "tab_inactive",

        // Fila 4 — el hueco vacío y los seis marcos de rareza
        "slot", "slot_common", "slot_uncommon", "slot_rare",
        "slot_epic", "slot_legendary", "slot_unique",

        // Fila 5 — la barra, en tres partes
        "bar_rail", "bar_fill", "bar_cap",

        // Fila 6 — las piezas chicas, que no se estiran
        "corner", "rivet", "divider", "chevron", "coin",
    ];

    /// <summary>Una pieza encontrada en la hoja.</summary>
    private sealed record Pieza(string Nombre, int X, int Y, int W, int H, int Margen);

    public static void Execute(string archivo, int tolerancia)
    {
        if (tolerancia <= 0)
            tolerancia = ToleranciaPorDefecto;

        string carpeta = Path.Combine(CarpetaResources, SubCarpeta);
        string nombreHoja = string.IsNullOrWhiteSpace(archivo) ? "sheet.png" : archivo;
        string rutaHoja = Path.Combine(carpeta, nombreHoja);

        GD.Print("── Recorte de la hoja de interfaz ─────────────────");
        GD.Print($"  hoja       : {nombreHoja}");
        GD.Print($"  tolerancia : {tolerancia}");

        if (!File.Exists(rutaHoja))
        {
            GD.PrintErr($"  ✗ no está {rutaHoja}");
            return;
        }

        var hoja = new Image();

        if (hoja.LoadPngFromBuffer(File.ReadAllBytes(rutaHoja)) != Error.Ok)
        {
            GD.PrintErr("  ✗ no se pudo leer el PNG");
            return;
        }

        int ancho = hoja.GetWidth();
        int alto = hoja.GetHeight();

        // El color del fondo sale de la esquina superior izquierda. Es más
        // confiable que el color más frecuente: una hoja con muchos paneles casi
        // negros puede tener más píxeles de panel que de fondo.
        var fondo = hoja.GetPixel(0, 0);

        GD.Print($"  medidas    : {ancho} x {alto}");
        GD.Print($"  fondo      : #{fondo.ToHtml(false)}");

        var piezas = Detectar(hoja, fondo, tolerancia, ancho, alto);

        if (piezas.Count == 0)
        {
            GD.PrintErr("  ✗ no se detectó ninguna pieza. ¿La tolerancia es muy alta?");
            return;
        }

        var ordenadas = Ordenar(piezas);
        var nombradas = Nombrar(ordenadas, hoja, fondo, tolerancia);

        Exportar(hoja, fondo, tolerancia, nombradas, carpeta);
        EscribirIndice(nombradas, carpeta, nombreHoja, ancho, alto);
    }

    // =====================================================================
    // Detección: componentes conectados que no son el fondo
    // =====================================================================

    /// <summary>
    /// Encuentra las piezas por relleno por inundación.
    /// </summary>
    /// <remarks>
    /// Vecindad de <b>ocho</b> y no de cuatro: los bordes en diagonal de las
    /// esquinas achaflanadas se cortan en pedacitos con vecindad de cuatro, y una
    /// pieza sale como cinco.
    /// </remarks>
    private static List<Rect2I> Detectar(
        Image hoja, Color fondo, int tolerancia, int ancho, int alto)
    {
        var visto = new bool[ancho * alto];
        var salida = new List<Rect2I>();
        var pila = new Stack<int>();

        for (int y0 = 0; y0 < alto; y0++)
        {
            for (int x0 = 0; x0 < ancho; x0++)
            {
                int raiz = (y0 * ancho) + x0;

                if (visto[raiz] || EsFondo(hoja.GetPixel(x0, y0), fondo, tolerancia))
                    continue;

                int minX = x0, maxX = x0, minY = y0, maxY = y0, area = 0;

                pila.Push(raiz);
                visto[raiz] = true;

                while (pila.Count > 0)
                {
                    int p = pila.Pop();
                    int px = p % ancho;
                    int py = p / ancho;

                    area++;

                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx;
                            int ny = py + dy;

                            if (nx < 0 || ny < 0 || nx >= ancho || ny >= alto)
                                continue;

                            int n = (ny * ancho) + nx;

                            if (visto[n] || EsFondo(hoja.GetPixel(nx, ny), fondo, tolerancia))
                                continue;

                            visto[n] = true;
                            pila.Push(n);
                        }
                    }
                }

                if (area >= AreaMinima)
                    salida.Add(new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
        }

        return salida;
    }

    /// <summary>Si un color es el fondo, con tolerancia. Compara en 0-255.</summary>
    private static bool EsFondo(Color c, Color fondo, int tolerancia)
    {
        int dr = Mathf.Abs((int)(c.R * 255f) - (int)(fondo.R * 255f));
        int dg = Mathf.Abs((int)(c.G * 255f) - (int)(fondo.G * 255f));
        int db = Mathf.Abs((int)(c.B * 255f) - (int)(fondo.B * 255f));

        return dr <= tolerancia && dg <= tolerancia && db <= tolerancia;
    }

    // =====================================================================
    // Orden: por filas, y adentro de cada fila de izquierda a derecha
    // =====================================================================

    /// <summary>
    /// Ordena como se lee: filas de arriba abajo, y adentro de cada fila de
    /// izquierda a derecha.
    /// </summary>
    /// <remarks>
    /// ⚠ Ordenar por <c>Y</c> a secas no alcanza: dos piezas de la misma fila
    /// casi nunca empiezan en el mismo <c>Y</c> exacto —una es más alta que la
    /// otra— y con un orden por Y puro la fila sale barajada. Se agrupan las que
    /// se solapan verticalmente y recién ahí se ordena por X.
    /// </remarks>
    private static List<Rect2I> Ordenar(List<Rect2I> piezas)
    {
        var pendientes = new List<Rect2I>(piezas);
        pendientes.Sort((a, b) => a.Position.Y.CompareTo(b.Position.Y));

        var salida = new List<Rect2I>();

        while (pendientes.Count > 0)
        {
            var primera = pendientes[0];
            int centro = primera.Position.Y + (primera.Size.Y / 2);

            var fila = new List<Rect2I>();

            for (int i = pendientes.Count - 1; i >= 0; i--)
            {
                var p = pendientes[i];

                // Pertenece a la fila si su franja vertical contiene el centro de
                // la primera, o al revés. Cubre el caso de una pieza baja al lado
                // de una alta.
                bool comparteFranja =
                    (centro >= p.Position.Y && centro <= p.Position.Y + p.Size.Y) ||
                    (p.Position.Y + (p.Size.Y / 2) >= primera.Position.Y &&
                     p.Position.Y + (p.Size.Y / 2) <= primera.Position.Y + primera.Size.Y);

                if (!comparteFranja)
                    continue;

                fila.Add(p);
                pendientes.RemoveAt(i);
            }

            fila.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
            salida.AddRange(fila);
        }

        return salida;
    }

    // =====================================================================
    // Nombres y margen de 9-slice
    // =====================================================================

    private static List<Pieza> Nombrar(
        List<Rect2I> ordenadas, Image hoja, Color fondo, int tolerancia)
    {
        bool coincide = ordenadas.Count == NombresEsperados.Length;

        if (!coincide)
        {
            GD.PrintErr(
                $"  ⚠ salieron {ordenadas.Count} piezas y se esperaban " +
                $"{NombresEsperados.Length}: van numeradas, hay que renombrarlas " +
                "en el JSON a mano.");
        }

        var salida = new List<Pieza>();

        for (int i = 0; i < ordenadas.Count; i++)
        {
            var r = ordenadas[i];

            string nombre = coincide
                ? NombresEsperados[i]
                : $"pieza_{(i + 1):00}";

            salida.Add(new Pieza(
                nombre, r.Position.X, r.Position.Y, r.Size.X, r.Size.Y,
                Margen(hoja, r, fondo, tolerancia)));
        }

        return salida;
    }

    /// <summary>
    /// Sugiere el margen de 9-slice: dónde termina el borde y empieza el centro.
    /// </summary>
    /// <remarks>
    /// <para>Camina desde el borde hacia adentro comparando cada columna con la
    /// siguiente. Mientras el dibujo cambia, todavía es el marco; cuando tres
    /// columnas seguidas salen casi iguales, ahí empezó el centro plano.</para>
    ///
    /// <para>Se toma el mayor de los cuatro lados y se topea en el 45 % del lado
    /// más corto: un margen que se pasa de la mitad hace que Godot no tenga
    /// centro que estirar.</para>
    /// </remarks>
    private static int Margen(Image hoja, Rect2I r, Color fondo, int tolerancia)
    {
        int tope = Mathf.Max(2, (int)(Mathf.Min(r.Size.X, r.Size.Y) * 0.45f));

        int izq = MargenHorizontal(hoja, r, tope, desdeLaIzquierda: true);
        int der = MargenHorizontal(hoja, r, tope, desdeLaIzquierda: false);
        int arr = MargenVertical(hoja, r, tope, desdeArriba: true);
        int aba = MargenVertical(hoja, r, tope, desdeArriba: false);

        _ = fondo;
        _ = tolerancia;

        return Mathf.Clamp(Mathf.Max(Mathf.Max(izq, der), Mathf.Max(arr, aba)), 2, tope);
    }

    private static int MargenHorizontal(Image hoja, Rect2I r, int tope, bool desdeLaIzquierda)
    {
        int iguales = 0;

        for (int paso = 0; paso < tope; paso++)
        {
            int x = desdeLaIzquierda
                ? r.Position.X + paso
                : r.Position.X + r.Size.X - 1 - paso;

            int siguiente = desdeLaIzquierda ? x + 1 : x - 1;

            if (ColumnasIguales(hoja, x, siguiente, r.Position.Y, r.Size.Y))
            {
                if (++iguales >= 3)
                    return Mathf.Max(2, paso - 2);
            }
            else
            {
                iguales = 0;
            }
        }

        return tope;
    }

    private static int MargenVertical(Image hoja, Rect2I r, int tope, bool desdeArriba)
    {
        int iguales = 0;

        for (int paso = 0; paso < tope; paso++)
        {
            int y = desdeArriba
                ? r.Position.Y + paso
                : r.Position.Y + r.Size.Y - 1 - paso;

            int siguiente = desdeArriba ? y + 1 : y - 1;

            if (FilasIguales(hoja, y, siguiente, r.Position.X, r.Size.X))
            {
                if (++iguales >= 3)
                    return Mathf.Max(2, paso - 2);
            }
            else
            {
                iguales = 0;
            }
        }

        return tope;
    }

    /// <summary>Cuánto puede diferir un canal y seguir contando como «igual».</summary>
    private const int EpsilonIgual = 6;

    private static bool ColumnasIguales(Image hoja, int x1, int x2, int y0, int alto)
    {
        for (int y = y0; y < y0 + alto; y++)
        {
            if (!Parecidos(hoja.GetPixel(x1, y), hoja.GetPixel(x2, y)))
                return false;
        }

        return true;
    }

    private static bool FilasIguales(Image hoja, int y1, int y2, int x0, int ancho)
    {
        for (int x = x0; x < x0 + ancho; x++)
        {
            if (!Parecidos(hoja.GetPixel(x, y1), hoja.GetPixel(x, y2)))
                return false;
        }

        return true;
    }

    private static bool Parecidos(Color a, Color b)
        => Mathf.Abs((int)(a.R * 255f) - (int)(b.R * 255f)) <= EpsilonIgual
        && Mathf.Abs((int)(a.G * 255f) - (int)(b.G * 255f)) <= EpsilonIgual
        && Mathf.Abs((int)(a.B * 255f) - (int)(b.B * 255f)) <= EpsilonIgual;

    // =====================================================================
    // Exportar
    // =====================================================================

    /// <summary>
    /// Escribe cada pieza como PNG con el fondo en transparente.
    /// </summary>
    /// <remarks>
    /// ⚠ El recorte deja un halo de un píxel donde el borde de la pieza venía con
    /// antialiasing contra el gris: esos píxeles son mitad pieza y mitad fondo, y
    /// ningún umbral los parte bien. Se van subiendo la tolerancia, al precio de
    /// comerse el borde. Si molesta en pantalla, se arregla en el editor de
    /// imágenes una vez y listo.
    /// </remarks>
    private static void Exportar(
        Image hoja, Color fondo, int tolerancia, List<Pieza> piezas, string carpeta)
    {
        GD.Print($"  → {piezas.Count} piezas");

        foreach (var p in piezas)
        {
            var pieza = Image.CreateEmpty(p.W, p.H, false, Image.Format.Rgba8);

            for (int y = 0; y < p.H; y++)
            {
                for (int x = 0; x < p.W; x++)
                {
                    var c = hoja.GetPixel(p.X + x, p.Y + y);

                    pieza.SetPixel(x, y, EsFondo(c, fondo, tolerancia)
                        ? new Color(c.R, c.G, c.B, 0f)
                        : c);
                }
            }

            string destino = Path.Combine(carpeta, $"{p.Nombre}.png");
            var falla = pieza.SavePng(destino);

            if (falla != Error.Ok)
            {
                GD.PrintErr($"    ✗ {p.Nombre}: {falla}");
                continue;
            }

            GD.Print($"    {p.Nombre,-18} {p.W,4} x {p.H,-4}  margen {p.Margen}");
        }
    }

    /// <summary>
    /// El índice, con el mismo formato de siempre: claves de contenido en
    /// español, como <c>graficos.json</c>.
    /// </summary>
    private static void EscribirIndice(
        List<Pieza> piezas, string carpeta, string hoja, int ancho, int alto)
    {
        var sb = new StringBuilder();

        sb.AppendLine("{");
        sb.AppendLine("  // Las piezas de interfaz recortadas de la hoja.");
        sb.AppendLine("  //");
        sb.AppendLine("  // ⚠ LO ESCRIBE `--recortar-ui`. Editarlo a mano se pierde en el próximo");
        sb.AppendLine("  //   recorte, salvo el campo `margen`, que es lo único que conviene");
        sb.AppendLine("  //   corregir a ojo (ver abajo).");
        sb.AppendLine("  //");
        sb.AppendLine("  // `x`, `y`, `w`, `h` son la posición de la pieza DENTRO de la hoja. El");
        sb.AppendLine("  // juego no los usa —cada pieza ya está exportada suelta— pero son la");
        sb.AppendLine("  // trazabilidad: dicen de dónde salió cada archivo.");
        sb.AppendLine("  //");
        sb.AppendLine("  // `margen` es el 9-slice: cuántos píxeles de cada borde NO se estiran.");
        sb.AppendLine("  // Es una SUGERENCIA calculada mirando dónde deja de cambiar el dibujo.");
        sb.AppendLine("  // Acierta en paneles y botones; en las piezas con esquinas ornamentadas");
        sb.AppendLine("  // conviene mirarlo en pantalla y corregirlo. Se escribe una vez.");
        sb.AppendLine();
        sb.AppendLine($"  \"hoja\": \"{hoja}\",");
        sb.AppendLine($"  \"hojaAncho\": {ancho},");
        sb.AppendLine($"  \"hojaAlto\": {alto},");
        sb.AppendLine();
        sb.AppendLine("  \"piezas\": {");

        for (int i = 0; i < piezas.Count; i++)
        {
            var p = piezas[i];
            string coma = i == piezas.Count - 1 ? "" : ",";

            sb.AppendLine(
                $"    \"{p.Nombre}\": {{ \"x\": {p.X}, \"y\": {p.Y}, " +
                $"\"w\": {p.W}, \"h\": {p.H}, \"margen\": {p.Margen} }}{coma}");
        }

        sb.AppendLine("  }");
        sb.AppendLine("}");

        string destino = Path.Combine(carpeta, "pieces.json");
        File.WriteAllText(destino, sb.ToString(), new UTF8Encoding(false));

        GD.Print($"  → pieces.json escrito ({piezas.Count} entradas)");
    }
}
