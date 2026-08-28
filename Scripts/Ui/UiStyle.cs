using Godot;

namespace TowerOfArgentum.Ui;

/// <summary>
/// El kit de dibujo de la interfaz: panel, marco, slot, botón, barra, pestaña.
/// </summary>
/// <remarks>
/// <para>Toda la interfaz del juego se dibuja a mano dentro de <c>_Draw</c>, sin
/// nodos <c>Control</c> ni <c>Theme</c> de Godot. La consistencia entre
/// pantallas, entonces, no la puede dar el motor: la tiene que dar un solo lugar
/// con las primitivas, y este es ese lugar.</para>
///
/// <para>Cada primitiva reproduce una pieza del panel del diseñador
/// (<c>Resources\ui\preview.png</c>). El vocabulario visual son cuatro cosas y
/// se repiten en toda la pantalla:</para>
///
/// <list type="number">
/// <item><b>Vidrio oscuro</b> — el relleno de todo panel, casi negro y con un
/// punto de transparencia, porque el HUD va sobre el mapa
/// (<c>INTERFAZ.md</c> §3).</item>
/// <item><b>Metal pulido</b> — el borde, más claro arriba que abajo, con
/// <b>escuadras en las esquinas</b> y remaches. Es lo que hace que la interfaz
/// se lea como forjada y no como un rectángulo de CSS.</item>
/// <item><b>Borde brillante</b> — el color de rareza, siempre en el borde y
/// nunca en el relleno: el fondo de una tarjeta se mantiene oscuro para que el
/// icono se vea.</item>
/// <item><b>Resplandor</b> — el halo alrededor de lo raro y de lo
/// seleccionado.</item>
/// </list>
///
/// <para>⚠ Los métodos son extensiones de <see cref="CanvasItem"/> porque las
/// pantallas son todas <c>Control</c> y dibujan con <c>this</c>. Nada de acá
/// guarda estado: se llama desde <c>_Draw</c> y termina.</para>
/// </remarks>
public static class UiStyle
{
    /// <summary>
    /// La tipografía de toda la interfaz.
    /// </summary>
    /// <remarks>
    /// ⚠ Hoy es la de respaldo del motor. El diseño usa una condensada en
    /// mayúsculas y traerla es un cambio de una línea acá —no de trece
    /// pantallas—, que es justamente el motivo de que la fuente viva en un solo
    /// campo.
    /// </remarks>
    public static Font Font { get; set; } = null!;

    /// <summary>Se llama una vez, al arrancar. Sin esto no se dibuja nada.</summary>
    public static void Init() => Font ??= ThemeDB.FallbackFont;

    // =====================================================================
    // Texto
    // =====================================================================

    /// <summary>
    /// Texto con la línea de base en <paramref name="y"/>.
    /// </summary>
    /// <remarks>
    /// Godot ancla el texto en la <b>base</b>, no arriba. Casi siempre conviene
    /// <see cref="Label"/>, que centra dentro de un rectángulo; esto queda para
    /// cuando la base es lo que importa.
    /// </remarks>
    public static void Text(
        this CanvasItem ci, string s, float x, float y, int tamano, Color color)
        => ci.DrawString(Font, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);

    /// <summary>Cuánto mide un texto. Para alinear a la derecha y para medir columnas.</summary>
    public static float Width(string s, int tamano)
        => Font.GetStringSize(s, HorizontalAlignment.Left, -1, tamano).X;

    /// <summary>
    /// Texto centrado verticalmente dentro de un rectángulo.
    /// </summary>
    /// <param name="pad">Margen horizontal, para que no toque el borde.</param>
    public static void Label(
        this CanvasItem ci, Rect2 r, string s, int tamano, Color color,
        HorizontalAlignment alineacion = HorizontalAlignment.Left, float pad = 0f)
    {
        // La base va en el centro corregida por ascendente y descendente: sin
        // eso el texto queda visiblemente alto dentro de su caja.
        float baseY = r.Position.Y
            + ((r.Size.Y + Font.GetAscent(tamano) - Font.GetDescent(tamano)) / 2f);

        float x = alineacion switch
        {
            HorizontalAlignment.Center => r.Position.X + ((r.Size.X - Width(s, tamano)) / 2f),
            HorizontalAlignment.Right => r.End.X - Width(s, tamano) - pad,
            _ => r.Position.X + pad,
        };

        ci.DrawString(Font, new Vector2(x, baseY), s, HorizontalAlignment.Left, -1, tamano, color);
    }

    /// <summary>
    /// Texto que se corta en varias líneas dentro de un ancho.
    /// </summary>
    /// <remarks>
    /// El corte es por palabra y a ojo, midiendo con la fuente. Godot tiene
    /// <c>DrawMultilineString</c>, pero devuelve el alto solo después de
    /// dibujar y acá hace falta saber cuántas líneas entraron para decidir si
    /// hay que achicar. Devuelve el alto usado.
    /// </remarks>
    public static float Paragraph(
        this CanvasItem ci, Rect2 r, string s, int tamano, Color color, float interlineado = 1.25f)
    {
        float alto = tamano * interlineado;
        float y = r.Position.Y + Font.GetAscent(tamano);
        string linea = "";

        foreach (string palabra in s.Split(' '))
        {
            string prueba = linea.Length == 0 ? palabra : $"{linea} {palabra}";

            if (Width(prueba, tamano) <= r.Size.X)
            {
                linea = prueba;
                continue;
            }

            ci.Text(linea, r.Position.X, y, tamano, color);
            y += alto;
            linea = palabra;

            // Se corta al llegar al borde de abajo en vez de desbordar sobre el
            // panel de al lado. Una descripción larga se lee recortada; una que
            // pisa la grilla rompe la pantalla.
            if (y > r.End.Y)
                return y - r.Position.Y;
        }

        if (linea.Length > 0)
        {
            ci.Text(linea, r.Position.X, y, tamano, color);
            y += alto;
        }

        return y - r.Position.Y;
    }

    // =====================================================================
    // Marcos
    // =====================================================================

    /// <summary>El largo de la escuadra de esquina, contra el lado más corto.</summary>
    private static float LargoEscuadra(Rect2 r) => Mathf.Min(16f, Mathf.Min(r.Size.X, r.Size.Y) * 0.28f);

    /// <summary>
    /// Las cuatro escuadras metálicas de las esquinas.
    /// </summary>
    /// <remarks>
    /// Es el detalle que más hace por el aire de "forjado" del diseño: el borde
    /// entero de un color plano se lee como una caja de interfaz web, y dos
    /// trazos más claros en cada esquina se leen como un herraje.
    /// </remarks>
    public static void Corners(this CanvasItem ci, Rect2 r, Color color, float grosor = 2f)
    {
        float l = LargoEscuadra(r);
        Vector2 a = r.Position;
        Vector2 b = r.End;

        // Arriba a la izquierda.
        ci.DrawLine(a, a + new Vector2(l, 0), color, grosor);
        ci.DrawLine(a, a + new Vector2(0, l), color, grosor);

        // Arriba a la derecha.
        ci.DrawLine(new Vector2(b.X, a.Y), new Vector2(b.X - l, a.Y), color, grosor);
        ci.DrawLine(new Vector2(b.X, a.Y), new Vector2(b.X, a.Y + l), color, grosor);

        // Abajo a la izquierda.
        ci.DrawLine(new Vector2(a.X, b.Y), new Vector2(a.X + l, b.Y), color, grosor);
        ci.DrawLine(new Vector2(a.X, b.Y), new Vector2(a.X, b.Y - l), color, grosor);

        // Abajo a la derecha.
        ci.DrawLine(b, b - new Vector2(l, 0), color, grosor);
        ci.DrawLine(b, b - new Vector2(0, l), color, grosor);
    }

    /// <summary>Los cuatro remaches. Puntos claros hacia adentro de las esquinas.</summary>
    public static void Rivets(this CanvasItem ci, Rect2 r, Color color, float radio = 1.7f)
    {
        float d = 5f;

        ci.DrawCircle(r.Position + new Vector2(d, d), radio, color);
        ci.DrawCircle(new Vector2(r.End.X - d, r.Position.Y + d), radio, color);
        ci.DrawCircle(new Vector2(r.Position.X + d, r.End.Y - d), radio, color);
        ci.DrawCircle(r.End - new Vector2(d, d), radio, color);
    }

    /// <summary>
    /// El resplandor de lo raro y de lo elegido: aros hacia afuera que se apagan.
    /// </summary>
    public static void Glow(this CanvasItem ci, Rect2 r, Color color, float fuerza = 1f, int pasos = 4)
    {
        for (int i = 1; i <= pasos; i++)
        {
            float d = i * 2f;
            float a = fuerza * 0.22f * (1f - ((i - 1f) / pasos));

            ci.DrawRect(
                new Rect2(r.Position.X - d, r.Position.Y - d, r.Size.X + (d * 2f), r.Size.Y + (d * 2f)),
                color.Alpha(a), filled: false, width: 2f);
        }
    }

    // =====================================================================
    // Panel
    // =====================================================================

    /// <summary>
    /// Un panel: vidrio oscuro, borde de metal y escuadras.
    /// </summary>
    /// <param name="elevado">
    /// Lo que está por encima del resto —una tarjeta, una pestaña activa— usa el
    /// relleno claro. Es la única jerarquía de profundidad que hay.
    /// </param>
    /// <param name="opacidad">
    /// El HUD va sobre pasto, agua y dragones: un panel opaco recorta un pedazo
    /// del mapa. Las pantallas de menú, que no tienen mundo abajo, pasan 1.
    /// </param>
    public static void Panel(
        this CanvasItem ci, Rect2 r, bool elevado = false, float opacidad = 0.92f)
    {
        var relleno = elevado ? Palette.PanelElevated : Palette.Panel;

        ci.DrawRect(r, relleno.Alpha(opacidad));

        // Una franja apenas más clara arriba: la luz cae de arriba en todo el
        // diseño, y sin esto los paneles grandes se ven planos.
        ci.DrawRect(r.Row(0, Mathf.Min(3f, r.Size.Y)), Palette.BorderSoft.Alpha(0.30f * opacidad));

        ci.DrawRect(r, Palette.BorderNormal.Alpha(opacidad), filled: false, width: 2f);
        ci.Corners(r, Palette.BorderLight.Alpha(opacidad));
    }

    /// <summary>
    /// El título de una sección: mayúsculas centradas entre dos reglas.
    /// </summary>
    public static void SectionTitle(this CanvasItem ci, Rect2 r, string texto, Color? color = null)
    {
        var c = color ?? Palette.TextMuted;
        string s = texto.ToUpperInvariant();

        int tamano = (int)Mathf.Min(20f, r.Size.Y * 0.62f);
        float ancho = Width(s, tamano);
        float medio = r.Position.Y + (r.Size.Y / 2f);
        float hueco = 12f;

        ci.Label(r, s, tamano, c, HorizontalAlignment.Center);

        // Las reglas salen desde el texto hacia los dos bordes. Si el título no
        // deja lugar, no se dibujan: mejor sin regla que con un guion suelto.
        float izq = r.Position.X + ((r.Size.X - ancho) / 2f) - hueco;
        float der = r.End.X - ((r.Size.X - ancho) / 2f) + hueco;

        if (izq - r.Position.X > 8f)
        {
            ci.DrawLine(new Vector2(r.Position.X, medio), new Vector2(izq, medio),
                Palette.BorderSoft, 1.5f);
            ci.DrawLine(new Vector2(der, medio), new Vector2(r.End.X, medio),
                Palette.BorderSoft, 1.5f);
        }
    }

    // =====================================================================
    // Slot
    // =====================================================================

    /// <summary>
    /// El hueco de un objeto: marco de rareza, remaches y resplandor.
    /// </summary>
    /// <remarks>
    /// <para>Es la pieza que más veces aparece en pantalla —la grilla, los perks
    /// equipados, las ofertas de la tienda, los planos— así que es la que fija
    /// el aire de toda la interfaz.</para>
    ///
    /// <para><b>El relleno se mantiene oscuro siempre</b>, con apenas un tinte
    /// del color de rareza. La rareza se dice con el borde y con el resplandor,
    /// que es lo que pide el diseño: un fondo saturado se come el icono.</para>
    /// </remarks>
    /// <param name="rareza">El color del tier. <see cref="Palette.Rarity"/>.</param>
    /// <param name="elegido">Marco más grueso y resplandor al doble.</param>
    /// <param name="vacio">Sin tinte y con el borde apagado: el hueco libre.</param>
    public static void Slot(
        this CanvasItem ci, Rect2 r, Color rareza, bool elegido = false, bool vacio = false)
    {
        if (vacio)
        {
            ci.DrawRect(r, Palette.BackgroundAlt.Alpha(0.85f));
            ci.DrawRect(r, Palette.BorderSoft, filled: false, width: 1.5f);
            return;
        }

        // Un octavo de tinte alcanza para que se note de qué color es el marco
        // sin pelear con el icono.
        ci.DrawRect(r, Palette.BackgroundMain.Alpha(0.95f));
        ci.DrawRect(r, rareza.Alpha(0.13f));

        // La mitad de arriba, un poco más clara: da volumen sin usar gradiente.
        ci.DrawRect(r.Row(0, r.Size.Y * 0.45f), rareza.Alpha(0.06f));

        ci.Glow(r, rareza, elegido ? 2f : 1f);
        ci.DrawRect(r, rareza, filled: false, width: elegido ? 3f : 2f);
        ci.Rivets(r, Palette.BorderLight.Alpha(0.8f));
    }

    // =====================================================================
    // Controles
    // =====================================================================

    /// <summary>
    /// Un botón. Relleno tenue del acento, borde del acento, texto en mayúsculas.
    /// </summary>
    /// <param name="relleno">
    /// La acción principal va rellena y la secundaria solo con borde. En el
    /// diseño son COMPRAR y EQUIPAR: se distinguen sin leerlas.
    /// </param>
    public static void Button(
        this CanvasItem ci, Rect2 r, string texto, Color acento,
        bool habilitado = true, bool relleno = true, bool encima = false)
    {
        var c = habilitado ? acento : Palette.TextDisabled;

        ci.DrawRect(r, Palette.BackgroundMain.Alpha(0.9f));

        if (relleno)
            ci.DrawRect(r, c.Alpha(encima ? 0.45f : 0.28f));
        else if (encima)
            ci.DrawRect(r, c.Alpha(0.14f));

        if (habilitado && encima)
            ci.Glow(r, c, 1.2f, 3);

        ci.DrawRect(r, c, filled: false, width: 2f);
        ci.Corners(r, c.Alpha(0.85f), 2f);

        ci.Label(r, texto.ToUpperInvariant(),
            (int)Mathf.Min(19f, r.Size.Y * 0.46f),
            habilitado ? Palette.TextPrimary : Palette.TextDisabled,
            HorizontalAlignment.Center);
    }

    /// <summary>
    /// Una pestaña. La activa se ilumina arriba y se funde con el panel de abajo.
    /// </summary>
    public static void Tab(this CanvasItem ci, Rect2 r, string texto, bool activa)
    {
        ci.DrawRect(r, activa ? Palette.PanelElevated : Palette.BackgroundAlt.Alpha(0.85f));
        ci.DrawRect(r, activa ? Palette.BorderNormal : Palette.BorderSoft, filled: false, width: 1.5f);

        // La barra de arriba es lo que dice cuál está elegida. Va arriba y no
        // abajo porque abajo está el contenido: una línea inferior se leería
        // como el borde del panel.
        if (activa)
            ci.DrawRect(r.Row(0, 3f), Palette.Info);

        ci.Label(r, texto.ToUpperInvariant(),
            (int)Mathf.Min(17f, r.Size.Y * 0.44f),
            activa ? Palette.TextPrimary : Palette.TextMuted,
            HorizontalAlignment.Center);
    }

    /// <summary>Una lista desplegable: etiqueta, valor y el triangulito.</summary>
    public static void Dropdown(
        this CanvasItem ci, Rect2 r, string etiqueta, string valor, bool encima = false)
    {
        ci.DrawRect(r, Palette.BackgroundAlt.Alpha(0.9f));
        ci.DrawRect(r, encima ? Palette.BorderLight : Palette.BorderSoft, filled: false, width: 1.5f);

        int tamano = (int)Mathf.Min(16f, r.Size.Y * 0.5f);
        float x = r.Position.X + 10f;

        if (etiqueta.Length > 0)
        {
            ci.Label(r, etiqueta.ToUpperInvariant(), tamano, Palette.TextMuted, HorizontalAlignment.Left, 10f);
            x += Width(etiqueta.ToUpperInvariant(), tamano) + 8f;
        }

        ci.Label(new Rect2(x, r.Position.Y, r.End.X - x - 26f, r.Size.Y),
            valor.ToUpperInvariant(), tamano, Palette.TextPrimary);

        Triangle(ci, new Vector2(r.End.X - 15f, r.Position.Y + (r.Size.Y / 2f) - 2f), 5f,
            encima ? Palette.TextPrimary : Palette.TextMuted);
    }

    /// <summary>El triangulito hacia abajo de los desplegables.</summary>
    private static void Triangle(CanvasItem ci, Vector2 centro, float lado, Color color)
        => ci.DrawColoredPolygon(
        [
            centro + new Vector2(-lado, -lado * 0.6f),
            centro + new Vector2(lado, -lado * 0.6f),
            centro + new Vector2(0, lado * 0.7f),
        ], color);

    /// <summary>
    /// Una barra de progreso: riel hundido, relleno y borde.
    /// </summary>
    /// <param name="texto">Si va, se dibuja centrado adentro con sombra.</param>
    public static void Bar(
        this CanvasItem ci, Rect2 r, float fraccion, Color color, string? texto = null)
    {
        fraccion = Mathf.Clamp(fraccion, 0f, 1f);

        ci.DrawRect(r, Palette.BackgroundMain.Alpha(0.9f));

        if (fraccion > 0f)
        {
            var lleno = new Rect2(r.Position, new Vector2(r.Size.X * fraccion, r.Size.Y));

            ci.DrawRect(lleno, color);

            // La arista clara de arriba. Es lo que separa una barra de un
            // rectángulo de color.
            ci.DrawRect(lleno.Row(0, Mathf.Min(2f, r.Size.Y)), color.Lightened(0.35f));
        }

        ci.DrawRect(r, Palette.BorderSoft, filled: false, width: 1.5f);

        if (texto is null)
            return;

        int tamano = (int)Mathf.Min(16f, r.Size.Y * 0.72f);

        // Sombra un píxel abajo: el texto va sobre el relleno de la barra, que
        // cambia de color con la vida, y tiene que leerse sobre los tres.
        ci.Label(new Rect2(r.Position + Vector2.One, r.Size), texto, tamano,
            Palette.BackgroundMain.Alpha(0.85f), HorizontalAlignment.Center);
        ci.Label(r, texto, tamano, Palette.TextPrimary, HorizontalAlignment.Center);
    }

    /// <summary>
    /// La moneda de oro. Se dibuja, no es un sprite.
    /// </summary>
    /// <remarks>
    /// Aparece en cada precio de la tienda, en el HUD y en cada tarjeta: son
    /// veinte por pantalla. Un círculo con un aro adentro cuesta dos llamadas y
    /// no gasta una entrada de <c>graficos.json</c>.
    /// </remarks>
    public static void Coin(this CanvasItem ci, Vector2 centro, float radio)
    {
        ci.DrawCircle(centro, radio, Palette.Gold);
        ci.DrawCircle(centro, radio * 0.72f, Palette.Gold.Darkened(0.28f));
        ci.DrawArc(centro, radio * 0.42f, 0f, Mathf.Tau, 16, Palette.Gold.Lightened(0.3f), 2f);
    }

    /// <summary>
    /// Un precio: la moneda y el número, alineados a la izquierda de un rect.
    /// </summary>
    /// <param name="alcanza">Si no alcanza el oro, el número se apaga en rojo.</param>
    public static void Price(
        this CanvasItem ci, Rect2 r, int oro, bool alcanza = true, HorizontalAlignment alineacion = HorizontalAlignment.Left)
    {
        int tamano = (int)Mathf.Min(20f, r.Size.Y * 0.66f);
        float radio = tamano * 0.42f;
        string s = Thousands(oro);

        float ancho = (radio * 2f) + 8f + Width(s, tamano);

        float x = alineacion switch
        {
            HorizontalAlignment.Center => r.Position.X + ((r.Size.X - ancho) / 2f),
            HorizontalAlignment.Right => r.End.X - ancho,
            _ => r.Position.X,
        };

        ci.Coin(new Vector2(x + radio, r.Position.Y + (r.Size.Y / 2f)), radio);

        ci.Label(new Rect2(x + (radio * 2f) + 8f, r.Position.Y, ancho, r.Size.Y), s, tamano,
            alcanza ? Palette.Gold : Palette.Danger);
    }

    /// <summary>
    /// Un entero con separador de miles, el del idioma.
    /// </summary>
    /// <remarks>
    /// El diseño escribe «12.450» y no «12450». Con cinco dígitos la diferencia
    /// entre 12.450 y 124.500 se lee de un vistazo; sin separador hay que contar
    /// las cifras. El separador sale de <c>lang\</c> porque en inglés es la coma.
    /// </remarks>
    public static string Thousands(int n)
        => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            .Replace(",", Loc.Get("ui.format.miles"), System.StringComparison.Ordinal);

    /// <summary>Una línea de separación fina, del ancho del rectángulo.</summary>
    public static void Rule(this CanvasItem ci, Rect2 r, float y)
        => ci.DrawLine(
            new Vector2(r.Position.X, r.Position.Y + y),
            new Vector2(r.End.X, r.Position.Y + y),
            Palette.BorderSoft, 1.5f);

    /// <summary>
    /// Una fila de stat: etiqueta a la izquierda, valor a la derecha.
    /// </summary>
    /// <remarks>
    /// El patrón del panel de detalle del diseño. Los valores van alineados a la
    /// derecha para que se puedan comparar de un vistazo entre dos objetos.
    /// </remarks>
    public static void StatRow(
        this CanvasItem ci, Rect2 r, string etiqueta, string valor, Color? colorValor = null)
    {
        int tamano = (int)Mathf.Min(16f, r.Size.Y * 0.72f);

        ci.Label(r, etiqueta, tamano, Palette.TextMuted);
        ci.Label(r, valor, tamano, colorValor ?? Palette.TextPrimary, HorizontalAlignment.Right);
    }

    /// <summary>
    /// El velo que oscurece el mundo detrás de un overlay modal.
    /// </summary>
    /// <remarks>
    /// <c>INTERFAZ.md</c> §3: la mochila, la elección y los perks son overlays
    /// sobre el mundo, no pantallas aparte. El velo es lo que dice que el juego
    /// está detenido.
    /// </remarks>
    public static void Scrim(this CanvasItem ci)
        => ci.DrawRect(
            new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto),
            Palette.BackgroundMain.Alpha(0.82f));
}
