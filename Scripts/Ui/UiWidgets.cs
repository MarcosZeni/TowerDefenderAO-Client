using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Ui;

/// <summary>
/// Las piezas de interfaz que sí saben de qué va el juego: el retrato de una
/// clase, el hueco de un perk, la tarjeta del roster.
/// </summary>
/// <remarks>
/// <para>La división con <see cref="UiStyle"/> es a propósito y vale la pena
/// respetarla: <c>UiStyle</c> dibuja rectángulos y no conoce ni un tipo de la
/// simulación, así que sirve igual para el login que para la tienda. Esto de
/// acá conoce <see cref="RosterClass"/> y <see cref="PerkDefinition"/>.</para>
///
/// <para>Todo lo que aparece más de una vez en el diseño vive acá: el hueco de
/// perk sale en la grilla de la mochila, en los perks equipados de abajo, en
/// las ofertas de la tienda y en la pantalla de elección. Que las cuatro se
/// dibujen con la misma llamada es lo que hace que se vean iguales.</para>
/// </remarks>
public static class UiWidgets
{
    /// <summary>
    /// El hueco de un perk: marco de rareza, icono y la etiqueta de nivel.
    /// </summary>
    /// <remarks>
    /// El hueco del diseño es más alto que ancho —62×82— porque abajo del icono
    /// va <c>NIV. n</c>. El icono se dibuja en el cuadrado de arriba, así que no
    /// se deforma aunque la conversión al lienzo estire más a lo ancho.
    /// </remarks>
    /// <param name="nivel">Cuántas copias apiladas. 0 esconde la etiqueta.</param>
    public static void PerkSlot(
        this CanvasItem ci, Rect2 r, PerkDefinition perk, int nivel = 0, bool elegido = false)
    {
        var color = Palette.Rarity(perk.Tier);

        ci.Slot(r, color, elegido);

        // La franja de la etiqueta se descuenta del alto antes de encajar el
        // icono: si no, el icono la pisa en los huecos chicos.
        float franja = nivel > 0 ? Mathf.Min(18f, r.Size.Y * 0.24f) : 0f;
        var hueco = UiLayout.Square(new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y - franja))).Inset(5f);

        if (!CargadorIconos.Dibujar(ci, perk.Key, hueco.Position.X, hueco.Position.Y, hueco.Size.X))
        {
            // Sin icono queda la inicial del perk, en el color del tier. Es feo
            // a propósito: se tiene que notar cuál falta.
            ci.Label(hueco, Loc.Perk(perk)[..1].ToUpperInvariant(),
                (int)(hueco.Size.X * 0.6f), color, HorizontalAlignment.Center);
        }

        if (nivel > 0)
        {
            ci.Label(new Rect2(r.Position.X, r.End.Y - franja, r.Size.X, franja),
                $"{Loc.Get("ui.hud.niv")} {nivel}", (int)Mathf.Min(13f, franja * 0.8f),
                Palette.TextSecondary, HorizontalAlignment.Center);
        }
    }

    /// <summary>
    /// Un hueco libre: marco apagado y un signo de más en el medio.
    /// </summary>
    /// <remarks>
    /// El diseño lo dibuja igual que un slot lleno pero en gris. Es lo que
    /// convierte «me falta equipo» en algo que se ve, en vez de un vacío que
    /// parece el final de la lista.
    /// </remarks>
    public static void EmptySlot(this CanvasItem ci, Rect2 r, bool bloqueado = false)
    {
        ci.Slot(r, Palette.Common, vacio: true);

        if (bloqueado)
        {
            // El candado: un cuerpo y un arco arriba, a la escala del hueco.
            float lado = Mathf.Min(r.Size.X, r.Size.Y) * 0.26f;
            var centro = r.Position + (r.Size / 2f);

            ci.DrawRect(new Rect2(centro.X - (lado / 2f), centro.Y - (lado * 0.15f), lado, lado * 0.8f),
                Palette.TextDisabled);
            ci.DrawArc(new Vector2(centro.X, centro.Y - (lado * 0.15f)), lado * 0.32f,
                Mathf.Pi, Mathf.Tau, 12, Palette.TextDisabled, 3f);
            return;
        }

        float brazo = Mathf.Min(r.Size.X, r.Size.Y) * 0.18f;
        var c = r.Position + (r.Size / 2f);

        ci.DrawLine(c - new Vector2(brazo, 0), c + new Vector2(brazo, 0), Palette.TextDisabled, 3f);
        ci.DrawLine(c - new Vector2(0, brazo), c + new Vector2(0, brazo), Palette.TextDisabled, 3f);
    }

    /// <summary>
    /// La tarjeta de una clase del roster: retrato, nombre, oficio y nivel.
    /// </summary>
    /// <remarks>
    /// <para>Muestra el nivel y los slots juntos porque son dos cosas distintas
    /// que hacen falta a la vez: <b>el nivel abre capacidad y los drops la
    /// llenan</b>, así que un <c>1/3</c> quiere decir «tengo lugar y no tengo con
    /// qué» y un <c>3/3</c> lo contrario.</para>
    ///
    /// <para>La clase que no se puede pagar se apaga entera. Es más rápido de
    /// leer que ir a comparar el precio con el oro de la barra de arriba.</para>
    /// </remarks>
    public static void ClassCard(
        this CanvasItem ci, Rect2 r, RosterClass ficha, int costo, bool alcanza,
        bool elegida, int puestas)
    {
        var acento = elegida ? Palette.Info : Palette.BorderNormal;

        ci.DrawRect(r, Palette.PanelElevated.Alpha(elegida ? 0.96f : 0.88f));
        ci.DrawRect(r.Row(0, 3f), acento.Alpha(elegida ? 1f : 0.35f));

        if (elegida)
            ci.Glow(r, Palette.Info, 1.4f);

        ci.DrawRect(r, acento, filled: false, width: elegida ? 2.5f : 1.5f);
        ci.Corners(r, elegida ? Palette.Info : Palette.BorderLight.Alpha(0.7f));

        // El retrato ocupa el alto entero de la tarjeta menos el margen; el
        // texto arranca donde termina.
        var retrato = new Rect2(r.Position.X + 5f, r.Position.Y + 5f,
            r.Size.Y - 10f, r.Size.Y - 10f);

        ci.Portrait(retrato, ficha);

        float x = retrato.End.X + 8f;
        float ancho = r.End.X - x - 8f;
        float alto = r.Size.Y / 3.4f;

        ci.Label(new Rect2(x, r.Position.Y + (r.Size.Y * 0.10f), ancho, alto),
            Loc.Class(ficha.DefenderClass).ToUpperInvariant(), 18,
            alcanza ? Palette.TextPrimary : Palette.TextDisabled);

        ci.Label(new Rect2(x, r.Position.Y + (r.Size.Y * 0.34f), ancho, alto),
            Loc.Role(ficha.DefenderClass), 14, Palette.TextMuted);

        // Abajo a la derecha: el nivel, con los perks puestos sobre los abiertos.
        var badge = new Rect2(x, r.End.Y - (r.Size.Y * 0.34f) - 4f, ancho, r.Size.Y * 0.30f);

        ci.DrawRect(badge, Palette.BackgroundMain.Alpha(0.75f));
        ci.DrawRect(badge, Palette.BorderSoft, filled: false, width: 1f);

        ci.Label(badge, $"{Loc.Get("ui.hud.niv")} {ficha.Level}", 14,
            Palette.TextSecondary, HorizontalAlignment.Left, 6f);

        ci.Label(badge, $"{ficha.Perks.Count}/{ficha.OpenSlots}", 14,
            ficha.HasFreeSlot ? Palette.Success : Palette.TextMuted,
            HorizontalAlignment.Right, 6f);

        // El costo va sobre el retrato, abajo, con la moneda: es el número que
        // se mira mientras se arrastra, y ahí es donde está el ojo.
        var precio = new Rect2(retrato.Position.X, retrato.End.Y - 20f, retrato.Size.X, 20f);

        ci.DrawRect(precio, Palette.BackgroundMain.Alpha(0.78f));
        ci.Price(precio.Inset(2f), costo, alcanza, HorizontalAlignment.Center);

        // Cuántas hay puestas, arriba a la derecha del retrato. Solo si hay.
        if (puestas <= 0)
            return;

        var contador = new Rect2(retrato.End.X - 20f, retrato.Position.Y, 20f, 18f);

        ci.DrawRect(contador, Palette.Success.Alpha(0.85f));
        ci.Label(contador, puestas.ToString(), 13, Palette.BackgroundMain, HorizontalAlignment.Center);
    }

    /// <summary>
    /// La tarjeta vacía del roster: el lugar existe antes que el contenido.
    /// </summary>
    /// <remarks>
    /// Se dibuja aunque no haya ninguna clase. Si la columna apareciera recién
    /// con la primera contratación, el jugador no sabría dónde va a vivir su
    /// roster.
    /// </remarks>
    public static void LockedClassCard(this CanvasItem ci, Rect2 r, string texto)
    {
        ci.DrawRect(r, Palette.BackgroundAlt.Alpha(0.7f));
        ci.DrawRect(r, Palette.BorderSoft, filled: false, width: 1.5f);

        ci.Label(r.Row(r.Size.Y * 0.28f, r.Size.Y * 0.3f), texto.ToUpperInvariant(), 15,
            Palette.TextDisabled, HorizontalAlignment.Center);

        ci.Label(r.Row(r.Size.Y * 0.55f, r.Size.Y * 0.3f), "?????", 17,
            Palette.TextDisabled.Alpha(0.6f), HorizontalAlignment.Center);
    }

    // =====================================================================
    // El muñeco
    // =====================================================================

    /// <summary>
    /// El muñeco de una clase, quieto y mirando al sur, encajado en un marco.
    /// </summary>
    /// <remarks>
    /// Cuerpo y cabeza con el mismo anclaje que usa el mundo —los dos apoyados
    /// abajo, la cabeza corrida por su offset— pero a la escala del marco. El
    /// arma no va: a este tamaño es ruido. Si falta el arte, queda el hueco con
    /// su fondo, igual que el mundo cae a la primitiva.
    /// </remarks>
    public static void Portrait(this CanvasItem ci, Rect2 marco, RosterClass ficha)
    {
        ci.DrawRect(marco, Palette.BackgroundMain.Alpha(0.85f));
        ci.DrawRect(marco, Palette.BorderSoft, filled: false, width: 1f);

        var cuerpo = BodyOf(ficha, out var apariencia);

        if (cuerpo is null || apariencia is null)
            return;

        // La escala la fija el alto del marco; el muñeco se apoya en su base.
        float s = marco.Size.Y / cuerpo.GetSize().Y;

        ci.Doll(cuerpo, apariencia, s,
            new Vector2(marco.Position.X + (marco.Size.X / 2f), marco.End.Y),
            Colors.White);
    }

    /// <summary>El cuerpo al sur de una clase, con su apariencia. Compartido.</summary>
    public static Texture2D? BodyOf(RosterClass ficha, out Apariencia? apariencia)
    {
        apariencia = CargadorApariencia.De(ficha.DefenderClass, ficha.VisualTier);

        if (apariencia is null || apariencia.Body.Length == 0)
            return null;

        return CargadorGraficos.Textura($"{apariencia.Body}.sur");
    }

    /// <summary>
    /// Cuerpo + cabeza apoyados en <paramref name="pie"/>, a escala
    /// <paramref name="s"/>. Mismo anclaje que usa el mundo.
    /// </summary>
    public static void Doll(
        this CanvasItem ci, Texture2D cuerpo, Apariencia apariencia, float s, Vector2 pie, Color tinte)
    {
        var tamanoCuerpo = cuerpo.GetSize() * s;

        ci.DrawTextureRect(cuerpo,
            new Rect2(pie.X - (tamanoCuerpo.X / 2f), pie.Y - tamanoCuerpo.Y,
                tamanoCuerpo.X, tamanoCuerpo.Y),
            tile: false, tinte);

        var cabeza = CargadorGraficos.Textura($"{apariencia.Head}.sur");

        if (cabeza is null)
            return;

        var tamanoCabeza = cabeza.GetSize() * s;

        ci.DrawTextureRect(cabeza,
            new Rect2(
                pie.X - (tamanoCabeza.X / 2f) + (apariencia.HeadOffset.X * s),
                pie.Y - tamanoCabeza.Y + (apariencia.HeadOffset.Y * s),
                tamanoCabeza.X, tamanoCabeza.Y),
            tile: false, tinte);
    }

    // =====================================================================
    // La leyenda de rarezas del pie
    // =====================================================================

    /// <summary>
    /// La leyenda del pie: un rombo por rareza y su nombre.
    /// </summary>
    /// <remarks>
    /// Está siempre a la vista porque el color del marco es la única forma de
    /// leer la rareza de un slot, y un jugador nuevo no tiene por qué saber que
    /// el dorado es Oro. Ocupa 26 px de alto en el diseño: no molesta.
    /// </remarks>
    public static void RarityLegend(this CanvasItem ci, Rect2 r)
    {
        (Color Color, string Texto)[] items =
        [
            (Palette.Common, Loc.Get("ui.hud.libre")),
            (Palette.Uncommon, Loc.Tier(PerkTier.Bronze)),
            (Palette.Rare, Loc.Tier(PerkTier.Silver)),
            (Palette.Epic, Loc.Get("ui.hud.arma_equipada")),
            (Palette.Legendary, Loc.Tier(PerkTier.Gold)),
            (Palette.Unique, Loc.Tier(PerkTier.Unique)),
        ];

        const int tamano = 14;
        float paso = r.Size.X / items.Length;

        for (int i = 0; i < items.Length; i++)
        {
            var celda = new Rect2(r.Position.X + (i * paso), r.Position.Y, paso, r.Size.Y);
            var centro = new Vector2(celda.Position.X + 9f, celda.Position.Y + (celda.Size.Y / 2f));

            Diamond(ci, centro, 6f, items[i].Color);

            ci.Label(new Rect2(celda.Position.X + 22f, celda.Position.Y, paso - 22f, celda.Size.Y),
                items[i].Texto, tamano, Palette.TextMuted);
        }
    }

    /// <summary>El rombo de la leyenda: contorno, no relleno, igual que el diseño.</summary>
    private static void Diamond(CanvasItem ci, Vector2 c, float radio, Color color)
        => ci.DrawPolyline(
        [
            c + new Vector2(0, -radio),
            c + new Vector2(radio, 0),
            c + new Vector2(0, radio),
            c + new Vector2(-radio, 0),
            c + new Vector2(0, -radio),
        ], color, 1.6f);
}
