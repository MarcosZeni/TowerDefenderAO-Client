using Godot;

namespace TowerOfArgentum.Ui;

/// <summary>
/// El <see cref="Theme"/> de Godot, armado con la misma paleta que el dibujo a mano.
/// </summary>
/// <remarks>
/// <para>Casi toda la interfaz se dibuja en <c>_Draw</c> y no usa esto. La
/// excepción es el <b>login</b>, que tiene campos de texto: escribir un
/// <c>LineEdit</c> desde cero —cursor, selección, portapapeles, acentos con
/// teclado muerto— es mucho trabajo para reinventar algo que el motor ya trae
/// bien.</para>
///
/// <para>El problema es que un <c>LineEdit</c> con el tema de fábrica de Godot no
/// se parece en nada al diseño, y dos estilos en la misma pantalla se notan al
/// instante. Esto lo resuelve pintando los nodos del motor con
/// <see cref="Palette"/>: los mismos fondos, los mismos bordes y los mismos
/// cuatro escalones de texto.</para>
///
/// <para>⚠ <b>Sin esquinas redondeadas.</b> El diseño es de metal recortado, no
/// de tarjetas: un radio de esquina de 4 px alcanza para que un campo de texto
/// se vea de otro juego que el panel que lo rodea.</para>
/// </remarks>
public static class UiTheme
{
    private static Theme? _tema;

    /// <summary>El tema, armado una sola vez.</summary>
    public static Theme Current => _tema ??= Build();

    private static Theme Build()
    {
        var tema = new Theme();

        // --- Etiquetas.
        tema.SetColor("font_color", "Label", Palette.TextSecondary);
        tema.SetFontSize("font_size", "Label", 17);

        // --- Campos de texto. El foco se marca con el borde claro y nada más:
        //     un relleno distinto al enfocarse hace saltar el campo entero.
        tema.SetStylebox("normal", "LineEdit", Caja(Palette.BackgroundAlt, Palette.BorderSoft));
        tema.SetStylebox("focus", "LineEdit", Caja(Palette.BackgroundAlt, Palette.Info, 2));
        tema.SetStylebox("read_only", "LineEdit", Caja(Palette.BackgroundMain, Palette.BorderSoft));

        tema.SetColor("font_color", "LineEdit", Palette.TextPrimary);
        tema.SetColor("font_placeholder_color", "LineEdit", Palette.TextDisabled);
        tema.SetColor("font_selected_color", "LineEdit", Palette.BackgroundMain);
        tema.SetColor("selection_color", "LineEdit", Palette.Info.Alpha(0.55f));
        tema.SetColor("caret_color", "LineEdit", Palette.Info);
        tema.SetFontSize("font_size", "LineEdit", 20);

        // --- Botones. Los cuatro estados con el mismo borde y distinto relleno,
        //     igual que UiStyle.Button.
        tema.SetStylebox("normal", "Button", Caja(Palette.Panel, Palette.BorderNormal));
        tema.SetStylebox("hover", "Button", Caja(Palette.PanelElevated, Palette.BorderLight));
        tema.SetStylebox("pressed", "Button", Caja(Palette.BackgroundMain, Palette.Info, 2));
        tema.SetStylebox("focus", "Button", Caja(Palette.PanelElevated, Palette.Info, 2));
        tema.SetStylebox("disabled", "Button", Caja(Palette.BackgroundAlt, Palette.BorderSoft));

        tema.SetColor("font_color", "Button", Palette.TextPrimary);
        tema.SetColor("font_hover_color", "Button", Palette.TextPrimary);
        tema.SetColor("font_pressed_color", "Button", Palette.Info);
        tema.SetColor("font_disabled_color", "Button", Palette.TextDisabled);
        tema.SetFontSize("font_size", "Button", 19);

        return tema;
    }

    /// <summary>Una caja de estilo con el relleno y el borde de la paleta.</summary>
    private static StyleBoxFlat Caja(Color relleno, Color borde, int grosor = 1)
    {
        var caja = new StyleBoxFlat
        {
            BgColor = relleno,
            BorderColor = borde,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };

        caja.SetBorderWidthAll(grosor);

        return caja;
    }
}
