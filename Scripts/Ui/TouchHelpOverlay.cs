using Godot;

namespace TowerOfArgentum.Ui;

/// <summary>
/// La capa de los carteles táctiles: mira el dedo antes que nadie y dibuja el
/// popup encima de todo.
/// </summary>
/// <remarks>
/// <para>Toda la lógica vive en <see cref="UiTooltip"/>; esto es solo el enchufe
/// al árbol, porque una clase estática no recibe eventos ni dibuja.</para>
///
/// <para>⚠ <b>Dibuja ACÁ y no en la pantalla que declaró el cartel.</b> El globo
/// de escritorio lo pinta cada pantalla al final de su <c>_Draw</c>, y alcanza
/// porque es chico y cae pegado a lo señalado. El popup es grande y va a una
/// franja fija, así que la pantalla de la capa de abajo lo dibujaba debajo de
/// la de arriba: el cartel de una tarjeta del roster —HUD, capa 10— salía tapado
/// por la barra de la preparación —capa 20—.</para>
///
/// <para>⚠ <b>Va colgado de la RAÍZ y agregado al final</b> (ver <c>Main</c>),
/// dentro de su propio <c>CanvasLayer</c>. El final del árbol es lo que lo hace
/// escuchar primero: Godot reparte <c>_Input</c> en orden inverso al del árbol.
/// Colgado de <c>Main</c> sería ancestro de todas las pantallas y se enteraría
/// último — después de que el mercado ya compró con el toque que venía a cerrar
/// el cartel.</para>
/// </remarks>
public partial class TouchHelpOverlay : Node2D
{
    /// <summary>La capa: arriba de los modales (20) y del menú (5).</summary>
    public const int Layer = 100;

    public override void _Input(InputEvent evento)
    {
        if (UiTooltip.HandleTouch(evento, GetGlobalMousePosition()))
            GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (Device.IsTouch)
            QueueRedraw();
    }

    public override void _Draw() => UiTooltip.DrawTouchPopup(this);
}
