namespace TowerOfArgentum;

/// <summary>
/// La escala del mundo en pantalla. Los números salen de INTERFAZ.md §2 y son
/// una cuenta, no una preferencia.
/// </summary>
/// <remarks>
/// <para>El tile de Argentum Online mide 32x32 en 1x, y el proyecto fijó
/// <b>una sola escala, x4</b>, así que en disco mide 128x128. La celda se dibuja
/// a tres cuartos de eso, 96 px.</para>
///
/// <para>Por qué no a tamaño completo: con celdas de 128 px entran 15 x 8,4
/// celdas en 1920x1080, y en ese campo no hay lugar para una ruta que serpentee
/// ni para ver una oleada entera. A tres cuartos entran 20 x 11 = 220 celdas,
/// que es la vista de Argentum Online (17 x 13 = 221) con celdas cuadradas.</para>
///
/// <para>⚠ Estas constantes no viven en el proyecto de simulación, y es a
/// propósito: <b>la simulación no sabe cuántos píxeles mide una celda.</b>
/// Trabaja en celdas y en milésimas de celda. Si un día se cambia el zoom, la
/// corrida tiene que dar exactamente el mismo resultado.</para>
/// </remarks>
public static class Canvas
{
    /// <summary>Lado de una celda, en píxeles lógicos.</summary>
    public const int CellSide = 96;

    /// <summary>Ancho del campo, en celdas. 20 x 96 = 1920.</summary>
    public const int CellsWide = 20;

    /// <summary>
    /// Alto del campo, en celdas. 11 x 96 = 1056, o sea 24 px de sobra contra
    /// los 1080 del lienzo. Ese sobrante es margen para el HUD, que flota encima
    /// del mundo y no tiene marco propio (INTERFAZ.md §3).
    /// </summary>
    public const int CellsHigh = 11;

    /// <summary>Ancho del lienzo lógico. Fijo: no cambia con la resolución.</summary>
    public const int CanvasWidth = 1920;

    /// <summary>Alto del lienzo lógico.</summary>
    public const int CanvasHeight = 1080;

    /// <summary>El centro de una celda, en píxeles.</summary>
    public static (float X, float Y) CentroDe(int celdaX, int celdaY)
        => ((celdaX + 0.5f) * CellSide, (celdaY + 0.5f) * CellSide);
}
