using Godot;

namespace TowerOfArgentum.Ui;

/// <summary>
/// La disposición de la interfaz, copiada del panel del diseñador.
/// </summary>
/// <remarks>
/// <para>Las medidas salen de
/// <c>Resources\ui\tower_of_argentum_ui_mockup_spec.json</c>. Ese archivo
/// describe un lienzo de <b>1672×941</b> en el que la interfaz jugable ocupa el
/// rectángulo <c>game_ui</c> = (532, 51, 1108, 868); la mitad izquierda de la
/// imagen es la carta de colores y no se dibuja.</para>
///
/// <para><b>Las coordenadas del spec se guardan crudas y se convierten acá.</b>
/// Es a propósito: así cada constante de este archivo se puede buscar tal cual
/// en el JSON del diseñador, y si mañana manda una versión nueva se cambian los
/// números de entrada sin tocar ni una pantalla.</para>
///
/// <para>⚠ <b>La conversión NO es uniforme, y no puede serlo.</b> El
/// <c>game_ui</c> del diseño mide 1108×868, que es 1,28:1; el juego se dibuja
/// contra 1920×1080, que es 1,78:1 y no se negocia
/// (<c>INTERFAZ.md</c> §1: <c>aspect = "keep"</c>, misma vista para todos porque
/// el ranking es por velocidad). Escalar parejo dejaría 1504 px de alto sobre
/// una pantalla de 1080, o barras negras enormes a los costados.</para>
///
/// <para>La salida es escalar <b>cada eje por separado</b>: ×1,733 a lo ancho y
/// ×1,244 a lo alto. Lo que se conserva —que es lo que se pidió— es la
/// <b>distribución</b>: cada panel queda en la misma proporción de la pantalla
/// que en el diseño, con los mismos vecinos y las mismas separaciones
/// relativas. Lo que cambia es que los huecos son más anchos que altos.</para>
///
/// <para>Por eso existe <see cref="Square"/>: lo que tiene que seguir siendo
/// cuadrado —iconos de perk, retratos, la moneda— se mete centrado en su hueco
/// estirado en vez de deformarse. Un icono ovalado se nota; un panel 12 % más
/// ancho, no.</para>
/// </remarks>
public static class UiLayout
{
    // --- El marco del diseño. Todo lo demás es relativo a esto. ---

    private const float SpecX = 532f;
    private const float SpecY = 51f;
    private const float SpecW = 1108f;
    private const float SpecH = 868f;

    /// <summary>Cuánto se estira el diseño a lo ancho: 1920 / 1108.</summary>
    public const float ScaleX = Canvas.CanvasWidth / SpecW;

    /// <summary>Cuánto se estira el diseño a lo alto: 1080 / 868.</summary>
    public const float ScaleY = Canvas.CanvasHeight / SpecH;

    /// <summary>
    /// Un rectángulo del spec, en píxeles del lienzo del juego.
    /// </summary>
    /// <remarks>
    /// Los cuatro números que recibe son exactamente los del bloque
    /// <c>rect</c> del JSON, sin restar nada: la resta del origen la hace esto.
    /// </remarks>
    public static Rect2 R(float x, float y, float w, float h)
        => new(
            (x - SpecX) * ScaleX,
            (y - SpecY) * ScaleY,
            w * ScaleX,
            h * ScaleY);

    /// <summary>El cuadrado más grande que entra centrado en un rectángulo.</summary>
    /// <remarks>
    /// La conversión estira más a lo ancho que a lo alto, así que un hueco que
    /// en el diseño era cuadrado acá no lo es. Lo que dibuja arte —iconos,
    /// retratos, monedas— pasa por acá para no salir ovalado.
    /// </remarks>
    public static Rect2 Square(Rect2 r)
    {
        float lado = Mathf.Min(r.Size.X, r.Size.Y);

        return new Rect2(
            r.Position.X + ((r.Size.X - lado) / 2f),
            r.Position.Y + ((r.Size.Y - lado) / 2f),
            lado, lado);
    }

    /// <summary>El mismo rectángulo, encogido <paramref name="p"/> px por lado.</summary>
    public static Rect2 Inset(this Rect2 r, float p)
        => new(r.Position.X + p, r.Position.Y + p, r.Size.X - (p * 2f), r.Size.Y - (p * 2f));

    /// <summary>Un pedazo horizontal, con el alto y el desplazamiento dados.</summary>
    public static Rect2 Row(this Rect2 r, float y, float alto)
        => new(r.Position.X, r.Position.Y + y, r.Size.X, alto);

    // =====================================================================
    // 1. La barra de estado de arriba.
    // =====================================================================

    /// <summary>Barra superior: oleada, vida de la Torre, oro y los tres botones.</summary>
    public static readonly Rect2 TopBar = R(538, 54, 1098, 72);

    public static readonly Rect2 WaveStatus = R(550, 62, 185, 55);
    public static readonly Rect2 TowerHp = R(753, 62, 308, 55);
    public static readonly Rect2 GoldStatus = R(1080, 62, 224, 55);
    public static readonly Rect2 SettingsButton = R(1323, 66, 52, 45);
    public static readonly Rect2 MenuButton = R(1412, 66, 117, 45);
    public static readonly Rect2 PauseButton = R(1557, 66, 49, 45);

    // =====================================================================
    // 2. La columna de la izquierda: el roster.
    // =====================================================================

    /// <summary>Lista vertical de clases contratadas.</summary>
    public static readonly Rect2 HeroesPanel = R(540, 143, 181, 567);

    public static readonly Rect2 HeroesTitle = R(547, 145, 166, 32);

    /// <summary>La primera tarjeta de clase. Las demás bajan <see cref="HeroCardStep"/>.</summary>
    public static readonly Rect2 HeroCard0 = R(547, 181, 165, 95);

    /// <summary>De una tarjeta a la siguiente: 95 de alto + 10 de hueco, escalados.</summary>
    public static readonly float HeroCardStep = 105f * ScaleY;

    /// <summary>
    /// La tarjeta número <paramref name="i"/> del roster.
    /// </summary>
    /// <remarks>
    /// El diseño dibuja cinco y el roster llega a nueve clases. La que se pasa
    /// del panel no se dibuja: la lista scrollea, no se encoge, porque tarjetas
    /// de alto variable romperían la distribución que se está copiando.
    /// </remarks>
    public static Rect2 HeroCard(int i)
        => new(HeroCard0.Position.X, HeroCard0.Position.Y + (i * HeroCardStep),
               HeroCard0.Size.X, HeroCard0.Size.Y);

    /// <summary>Cuántas tarjetas entran enteras en el panel.</summary>
    public static readonly int HeroCardsVisible =
        (int)((HeroesPanel.End.Y - HeroCard0.Position.Y) / HeroCardStep);

    // =====================================================================
    // 3. La columna central: pestañas, filtros y la grilla.
    // =====================================================================

    public static readonly Rect2 InventoryPanel = R(729, 143, 381, 567);

    public static readonly Rect2 TabPerks = R(735, 145, 112, 39);
    public static readonly Rect2 TabWeapons = R(850, 145, 117, 39);
    public static readonly Rect2 TabBlueprints = R(970, 145, 132, 39);

    public static readonly Rect2 FilterDropdown = R(744, 196, 127, 32);
    public static readonly Rect2 SortDropdown = R(945, 196, 153, 32);

    public static readonly Rect2 PerkGrid = R(743, 252, 358, 362);
    public static readonly Rect2 InventoryCapacity = R(743, 650, 355, 42);

    /// <summary>Columnas de la grilla de inventario. Del bloque <c>grid</c> del spec.</summary>
    public const int GridColumns = 5;

    /// <summary>Filas visibles sin scrollear.</summary>
    public const int GridRows = 4;

    private const float SpecCellW = 62f;
    private const float SpecCellH = 82f;
    private const float SpecGapX = 10f;
    private const float SpecGapY = 12f;

    /// <summary>
    /// La celda <paramref name="i"/> de la grilla, en orden de lectura.
    /// </summary>
    /// <remarks>
    /// El hueco es más alto que ancho (62×82 en el diseño) porque abajo del
    /// icono va la etiqueta <c>NIV. n</c>. El icono sale de
    /// <see cref="Square"/> sobre la parte de arriba.
    /// </remarks>
    public static Rect2 GridCell(int i)
    {
        int col = i % GridColumns;
        int fila = i / GridColumns;

        return new Rect2(
            PerkGrid.Position.X + (col * (SpecCellW + SpecGapX) * ScaleX),
            PerkGrid.Position.Y + (fila * (SpecCellH + SpecGapY) * ScaleY),
            SpecCellW * ScaleX,
            SpecCellH * ScaleY);
    }

    /// <summary>Cuántos huecos hay a la vista de una vez.</summary>
    public const int GridVisible = GridColumns * GridRows;

    // =====================================================================
    // 4. La columna del detalle.
    // =====================================================================

    public static readonly Rect2 DetailPanel = R(1121, 143, 204, 567);

    public static readonly Rect2 DetailTitle = R(1140, 164, 166, 52);
    public static readonly Rect2 DetailIcon = R(1180, 228, 85, 85);
    public static readonly Rect2 DetailDescription = R(1138, 332, 171, 72);
    public static readonly Rect2 DetailStats = R(1138, 414, 171, 112);

    /// <summary>
    /// Las clases que pueden equipar el perk, abajo de los stats.
    /// </summary>
    /// <remarks>
    /// ⚠ Vive en el hueco que dejan las tres filas de <see cref="DetailStats"/>
    /// —usan 66 de 112— y no en <see cref="DetailDescription"/>, que pasó a ser
    /// <b>qué hace el perk</b>. Las dos cosas son necesarias y ninguna reemplaza
    /// a la otra: una dice qué compra el jugador, la otra a quién se lo puede
    /// poner. Solo lo usa la ficha de un perk; la de un arma no lo dibuja.
    /// </remarks>
    public static readonly Rect2 DetailClasses = R(1138, 484, 171, 44);
    public static readonly Rect2 DetailPrice = R(1138, 535, 171, 38);
    public static readonly Rect2 DetailBuy = R(1140, 588, 162, 40);
    public static readonly Rect2 DetailEquip = R(1140, 638, 162, 38);

    // =====================================================================
    // 5. La columna de la derecha: la tienda rotativa.
    // =====================================================================

    public static readonly Rect2 ShopPanel = R(1335, 143, 300, 567);

    public static readonly Rect2 ShopTitle = R(1352, 150, 266, 41);
    public static readonly Rect2 ShopTimer = R(1361, 190, 247, 32);

    /// <summary>Las cuatro ofertas. Son cuatro fijas, no una lista (<c>CLAUDE.md</c>).</summary>
    public static readonly Rect2[] ShopCards =
    [
        R(1350, 231, 132, 205),
        R(1493, 231, 132, 205),
        R(1350, 451, 132, 198),
        R(1493, 451, 132, 198),
    ];

    public static readonly Rect2 ShopRefresh = R(1350, 665, 168, 35);
    public static readonly Rect2 ShopRefreshCost = R(1525, 665, 100, 35);

    // =====================================================================
    // 6. La barra de abajo: la build de un vistazo.
    // =====================================================================

    public static readonly Rect2 BuildPanel = R(539, 724, 1097, 150);

    public static readonly Rect2 EquippedPerks = R(551, 736, 405, 123);
    public static readonly Rect2 EquippedWeapon = R(969, 736, 186, 123);
    public static readonly Rect2 ActiveBlueprints = R(1167, 736, 220, 123);
    public static readonly Rect2 Consumables = R(1401, 736, 223, 123);

    // =====================================================================
    // 7. El pie: consejo y leyenda.
    // =====================================================================

    /// <summary>
    /// El pie de pantalla.
    /// </summary>
    /// <remarks>
    /// ⚠ En el diseño termina en y=920 y el <c>game_ui</c> termina en 919, así
    /// que convertido se pasa un píxel del lienzo. Se recorta el alto en vez de
    /// mover la barra: el borde de abajo tiene que quedar adentro o Godot lo
    /// dibuja fuera del área visible en las pantallas que no son 16:9.
    /// </remarks>
    public static readonly Rect2 FooterBar = new(
        R(538, 883, 1098, 37).Position,
        new Vector2(
            R(538, 883, 1098, 37).Size.X,
            Canvas.CanvasHeight - R(538, 883, 1098, 37).Position.Y - 4f));

    public static readonly Rect2 FooterTip = R(555, 888, 558, 26);
    public static readonly Rect2 FooterLegend = R(1115, 888, 500, 26);

    // =====================================================================
    // 8. EL MARCO DE LA PARTIDA
    // =====================================================================
    //
    // Todo lo de arriba es la disposición del diseño convertida, y la usan los
    // MODALES A PANTALLA COMPLETA —mochila, menú principal—, que tapan el mundo
    // a propósito.
    //
    // Lo de acá abajo es otra cosa: son las CUATRO PAREDES del marco, y dejan un
    // agujero real en el medio para el mundo.

    /// <summary>
    /// El grosor del bisel: el borde de metal entre la pared y el mundo.
    /// </summary>
    private const float Bisel = 8f;

    /// <summary>La barra de arriba. Misma del diseño: oleada, vida, oro, botones.</summary>
    public static readonly Rect2 RunTopBar = TopBar;

    /// <summary>La columna del roster. Misma del diseño, a la izquierda.</summary>
    public static readonly Rect2 RunRoster = HeroesPanel;

    /// <summary>
    /// La columna de la derecha: la tienda, o el panel del defensor elegido.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es más angosta que la del diseño</b> —313 px contra 520— y esa
    /// es la concesión que paga el marco. Con los 520 originales el agujero del
    /// medio queda en 1039×723 y el mundo tiene que dibujarse a escala 0,54: los
    /// bichos pasan de 96×138 px a 69×99 y seis de las nueve clases, que ya
    /// comparten muñeco, dejan de distinguirse.</para>
    ///
    /// <para>313 es el ancho de la columna izquierda. Que las dos midan lo mismo
    /// no es casualidad ni ahorro: un marco con los dos lados distintos se lee
    /// como un error de alineación.</para>
    ///
    /// <para>⚠ <b>La tienda y el panel del defensor comparten esta columna y no
    /// se dibujan juntos.</b> Manda el defensor cuando hay uno elegido, porque el
    /// jugador lo acaba de clickear; volver a clickearlo lo deselecciona y la
    /// tienda vuelve. Apilarlos partiría la columna en dos mitades donde no entra
    /// ni una tarjeta de oferta.</para>
    /// </remarks>
    public static readonly Rect2 RunSidePanel = new(
        RunTopBar.End.X - RunRoster.Size.X,
        RunTopBar.End.Y + 10f,
        RunRoster.Size.X,
        788f);

    /// <summary>
    /// La barra de abajo: la build en combate, la preparación entre oleadas.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Más baja que la del diseño</b> —124 px contra 187—, por el mismo
    /// motivo que la columna: cada píxel que se lleva la pared se lo saca al
    /// mundo. Los cuatro bloques de adentro conservan las proporciones exactas
    /// del diseño, así que la distribución no cambia: cambia la altura.
    /// </remarks>
    public static readonly Rect2 RunBottomBar = new(
        BuildPanel.Position.X, 900f, BuildPanel.Size.X, 124f);

    /// <summary>El pie: el consejo y la leyenda de rarezas.</summary>
    public static readonly Rect2 RunFooter = new(
        BuildPanel.Position.X, 1030f, BuildPanel.Size.X, 44f);

    public static readonly Rect2 RunFooterTip = new(
        RunFooter.Position.X + 20f, RunFooter.Position.Y, 700f, RunFooter.Size.Y);

    public static readonly Rect2 RunFooterLegend = new(
        RunFooter.End.X - 820f, RunFooter.Position.Y, 800f, RunFooter.Size.Y);

    /// <summary>
    /// Uno de los cuatro bloques de la barra de abajo, en el orden del diseño.
    /// </summary>
    /// <remarks>
    /// Las fracciones salen de <c>build_panel</c> y sus cuatro hijos en el JSON:
    /// el bloque 0 arranca en el 1,1 % del ancho y mide el 36,9 %, y así. Se
    /// guardan como fracción y no como píxeles justamente para que achicar la
    /// barra no descoloque los bloques.
    /// </remarks>
    public static Rect2 RunBottomBlock(int i)
    {
        (float X, float W)[] fracciones =
        [
            (0.0109f, 0.3692f),
            (0.3920f, 0.1696f),
            (0.5725f, 0.2006f),
            (0.7857f, 0.2033f),
        ];

        var (fx, fw) = fracciones[i];

        return new Rect2(
            RunBottomBar.Position.X + (fx * RunBottomBar.Size.X),
            RunBottomBar.Position.Y + (0.08f * RunBottomBar.Size.Y),
            fw * RunBottomBar.Size.X,
            0.82f * RunBottomBar.Size.Y);
    }

    // ---------------------------------------------------------------------
    // El agujero del medio
    // ---------------------------------------------------------------------

    /// <summary>
    /// El hueco entre las cuatro paredes, bisel incluido.
    /// </summary>
    /// <remarks>
    /// Se calcula <b>a partir de las paredes</b> y no a mano: mover una pared
    /// mueve el mundo, y no hay forma de que los dos números se desincronicen.
    /// </remarks>
    public static readonly Rect2 WorldFrame = new(
        RunRoster.End.X,
        RunTopBar.End.Y,
        RunSidePanel.Position.X - RunRoster.End.X,
        RunBottomBar.Position.Y - RunTopBar.End.Y);

    /// <summary>Lo que queda del hueco después del bisel: donde entra el mundo.</summary>
    public static readonly Rect2 WorldViewport = WorldFrame.Inset(Bisel);

    /// <summary>
    /// Cuánto se achica el mundo para entrar en el hueco.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>La simulación NO se entera de esto.</b> El mundo se sigue
    /// simulando y guardando en 20×11 celdas de 96 px —1920×1056—; los layouts,
    /// el hash y el balance quedan intactos. Esto es una transformación de
    /// dibujo y nada más.</para>
    ///
    /// <para>Es el menor de los dos cocientes para que la celda siga siendo
    /// cuadrada. Estirar a llenar el hueco daría celdas rectangulares y muñecos
    /// deformados.</para>
    /// </remarks>
    public static readonly float WorldScale = Mathf.Min(
        WorldViewport.Size.X / (Canvas.CellsWide * (float)Canvas.CellSide),
        WorldViewport.Size.Y / (Canvas.CellsHigh * (float)Canvas.CellSide));

    /// <summary>La esquina de la celda (0,0) en pantalla. El mundo va centrado en el hueco.</summary>
    public static readonly Vector2 WorldOrigin = new(
        WorldViewport.Position.X
            + ((WorldViewport.Size.X - (Canvas.CellsWide * Canvas.CellSide * WorldScale)) / 2f),
        WorldViewport.Position.Y
            + ((WorldViewport.Size.Y - (Canvas.CellsHigh * Canvas.CellSide * WorldScale)) / 2f));

    /// <summary>El mundo dibujado, ya escalado y centrado. Es lo que tapa el bisel.</summary>
    public static readonly Rect2 WorldOnScreen = new(
        WorldOrigin,
        new Vector2(
            Canvas.CellsWide * Canvas.CellSide * WorldScale,
            Canvas.CellsHigh * Canvas.CellSide * WorldScale));

    /// <summary>Cuánto mide una celda en pantalla. Solo para dibujar; la celda sigue siendo 96.</summary>
    public static float CellOnScreen => Canvas.CellSide * WorldScale;

    /// <summary>De coordenadas del mundo a píxeles de pantalla.</summary>
    public static Vector2 ToScreen(Vector2 mundo) => WorldOrigin + (mundo * WorldScale);

    /// <summary>
    /// De píxeles de pantalla a coordenadas del mundo.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Todo click que termine en una celda pasa por acá.</b> Es el único
    /// lugar donde se invierte la transformación, y por eso no puede haber un
    /// segundo: un click convertido con la escala vieja pone el defensor en otra
    /// celda que la que el jugador vio.
    /// </remarks>
    public static Vector2 ToWorld(Vector2 pantalla) => (pantalla - WorldOrigin) / WorldScale;

    /// <summary>Si esa posición de pantalla cae adentro del mundo dibujado.</summary>
    public static bool InWorld(Vector2 pantalla) => WorldOnScreen.HasPoint(pantalla);
}
