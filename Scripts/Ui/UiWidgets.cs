using Godot;
using TowerOfArgentum.Simulation;

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

        // El orden de reserva es: arte propio → RPG Awesome → la inicial.
        //
        // ⚠ El del medio es nuevo y cambia mucho: hasta ahora casi ningún perk
        // tenía arte, así que la grilla de la mochila era veinte LETRAS de
        // colores. Con el icono del efecto, un perk se reconoce por lo que hace
        // —una llama es quemadura, un copo es lento— sin leer el nombre.
        //
        // El arte propio sigue primero porque es por PERK y esto es por EFECTO:
        // dos perks del mismo efecto comparten dibujo, que alcanza para
        // reconocerlos pero no para distinguirlos.
        if (!IconLoader.Draw(ci, perk.Key, hueco.Position.X, hueco.Position.Y, hueco.Size.X))
        {
            if (UiStyle.Icons is not null)
            {
                ci.Icon(hueco, IconFor(perk.Effect), color);
            }
            else
            {
                // Sin ninguna de las dos queda la inicial, en el color del tier.
                // Es feo a propósito: se tiene que notar cuál falta.
                ci.Label(hueco, Loc.Perk(perk)[..1].ToUpperInvariant(),
                    (int)(hueco.Size.X * 0.6f), color, HorizontalAlignment.Center);
            }
        }

        UiTooltip.Over(r, $"{Loc.Perk(perk)} · {Loc.Effect(perk.Effect)} {perk.Magnitude}%");

        if (nivel > 0)
        {
            ci.Label(new Rect2(r.Position.X, r.End.Y - franja, r.Size.X, franja),
                $"{Loc.Get("ui.hud.niv")} {nivel}", (int)Mathf.Min(13f, franja * 0.8f),
                Palette.TextSecondary, HorizontalAlignment.Center);
        }
    }

    /// <summary>
    /// Qué icono de RPG Awesome le toca a cada efecto de perk.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Es por <b>efecto</b> y no por perk, y esa es la decisión que hace
    /// la tabla escribible: hay 47 perks y 35 efectos, pero los efectos son
    /// conceptos —quemar, ralentizar, rebotar— y un concepto tiene un dibujo
    /// obvio. Perk por perk habría que elegir 47 veces y la mitad terminarían
    /// repetidos igual.</para>
    ///
    /// <para>El criterio de cada elección es <b>qué hace</b>, no cómo se llama:
    /// Quebranto y Debilidad son los dos escudos rotos porque los dos bajan la
    /// defensa, y el jugador los agrupa así aunque los nombres no se parezcan.</para>
    ///
    /// <para>El comentario de cada línea es la clase CSS de RPG Awesome, que es
    /// como se busca un reemplazo en la hoja de referencia.</para>
    /// </remarks>
    public static UiStyle.Icono IconFor(PerkEffect efecto) => efecto switch
    {
        // --- Bronce y plata: los tres números ---------------------------------
        PerkEffect.Damage => UiStyle.Icono.Damage,
        PerkEffect.Cadence => UiStyle.Icono.Cadence,
        PerkEffect.Range => UiStyle.Icono.Range,

        // --- Oro: los comportamientos -----------------------------------------
        PerkEffect.Ricochet => (UiStyle.Icono)0xEAC0,          // ra-supersonic-arrow
        PerkEffect.Splash => (UiStyle.Icono)0xE9B1,            // ra-explosion
        PerkEffect.Slow => (UiStyle.Icono)0xEAA7,              // ra-snowflake
        PerkEffect.CriticalHit => (UiStyle.Icono)0xEA52,       // ra-on-target
        PerkEffect.Poison => (UiStyle.Icono)0xEA71,            // ra-poison-cloud
        PerkEffect.Chain => (UiStyle.Icono)0xE960,             // ra-chain
        PerkEffect.Burn => (UiStyle.Icono)0xE9C3,              // ra-fire
        PerkEffect.Weaken => (UiStyle.Icono)0xE94A,            // ra-broken-shield
        PerkEffect.Sunder => (UiStyle.Icono)0xE976,            // ra-cracked-shield
        PerkEffect.Focus => (UiStyle.Icono)0xEAC4,             // ra-target-laser
        PerkEffect.HuntMark => (UiStyle.Icono)0xE9B5,          // ra-eyeball
        PerkEffect.Bleed => (UiStyle.Icono)0xE9A9,             // ra-droplet
        PerkEffect.Root => (UiStyle.Icono)0xEAE2,              // ra-vine-whip
        PerkEffect.Seismic => (UiStyle.Icono)0xE93C,           // ra-boot-stomp
        PerkEffect.Inspire => (UiStyle.Icono)0xEA04,           // ra-horn-call
        PerkEffect.Loot => (UiStyle.Icono)0xE9E4,              // ra-gold-bar
        PerkEffect.Opportunist => (UiStyle.Icono)0xEA65,       // ra-plain-dagger
        PerkEffect.ThresholdGuard => (UiStyle.Icono)0xEAD2,    // ra-tower
        PerkEffect.ArcaneEcho => (UiStyle.Icono)0xE965,        // ra-circle-of-circles
        PerkEffect.Overpenetration => (UiStyle.Icono)0xE911,   // ra-arrow-cluster
        PerkEffect.FirstStrike => (UiStyle.Icono)0xE97D,       // ra-crossed-swords
        PerkEffect.Charge => (UiStyle.Icono)0xE9D0,            // ra-focused-lightning

        // Los cinco que antes solo salían de un arma. ⚠ Reusan iconos que ya
        // están en esta tabla o en UiStyle.Icono: no hay catálogo de la fuente
        // RPG Awesome en el repo, y un codepoint elegido a ciegas es un glifo
        // vacío en pantalla. Repetido con sentido se lee; en blanco no.
        PerkEffect.Marksman => UiStyle.Icono.Range,             // el mismo del alcance
        PerkEffect.Butchery => (UiStyle.Icono)0xE979,           // ra-crossed-axes
        PerkEffect.Bloodthirst => (UiStyle.Icono)0xE9A9,        // ra-droplet
        PerkEffect.TwinStrike => (UiStyle.Icono)0xE97D,         // ra-crossed-swords
        PerkEffect.Siphon => UiStyle.Icono.Health,              // el mismo de la vida

        // --- Únicos: los nueve --------------------------------------------------
        PerkEffect.Perforation => (UiStyle.Icono)0xE919,       // ra-barbed-arrow
        PerkEffect.FrostWave => (UiStyle.Icono)0xE9D6,         // ra-frost-emblem
        PerkEffect.WrathOfGod => (UiStyle.Icono)0xEABE,        // ra-sunbeams
        PerkEffect.Apocalypse => (UiStyle.Icono)0xE98C,        // ra-death-skull
        PerkEffect.TerribleHunger => (UiStyle.Icono)0xEAE7,    // ra-wolf-head
        PerkEffect.DivineStorm => (UiStyle.Icono)0xEA31,       // ra-lightning
        PerkEffect.Blindness => (UiStyle.Icono)0xE950,         // ra-burning-eye
        PerkEffect.Strength => (UiStyle.Icono)0xE979,          // ra-crossed-axes
        PerkEffect.Invisibility => (UiStyle.Icono)0xE927,      // ra-batwings

        _ => UiStyle.Icono.Damage,
    };

    /// <summary>
    /// El efecto de un perk con su icono: <b>icono, hueco, texto</b>.
    /// </summary>
    /// <remarks>
    /// El mismo formato que <see cref="UiStyle.StatIcon"/>, para que una tarjeta
    /// de perk y una de clase se lean con el mismo movimiento del ojo.
    /// </remarks>
    public static void EffectRow(this CanvasItem ci, Rect2 r, PerkDefinition perk, Color color)
    {
        float lado = Mathf.Min(r.Size.Y, 18f);
        var caja = new Rect2(r.Position.X, r.Position.Y + ((r.Size.Y - lado) / 2f), lado, lado);

        ci.Icon(caja, IconFor(perk.Effect), color);

        int tamano = (int)Mathf.Min(15f, r.Size.Y * 0.78f);

        ci.Label(new Rect2(caja.End.X + 8f, r.Position.Y, r.End.X - caja.End.X - 8f, r.Size.Y),
            $"{Loc.Effect(perk.Effect)} {perk.Magnitude}%", tamano, Palette.TextSecondary);
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

        // ⚠ La clase ELEGIDA es la que se va a poner con el próximo click en el
        // mapa, así que tiene que verse de lejos y sin buscarla: es la pregunta
        // «¿qué estoy por poner?», y se contesta mirando de reojo mientras el ojo
        // está en el tablero.
        //
        // Antes la diferencia era un borde de 2,5 px contra 1,5 y una franja
        // arriba: en una columna de cinco tarjetas iguales, invisible.
        if (elegida)
            ci.DrawRect(r, Palette.Info.Alpha(0.16f));

        ci.DrawRect(r.Row(0, elegida ? 5f : 3f), acento.Alpha(elegida ? 1f : 0.35f));

        if (elegida)
            ci.Glow(r, Palette.Info, 2.4f, 7);

        ci.DrawRect(r, acento, filled: false, width: elegida ? 3f : 1.5f);
        ci.Corners(r, elegida ? Palette.Info : Palette.BorderLight.Alpha(0.7f),
            elegida ? 3f : 2f);

        // La lengüeta de la izquierda: una barra maciza pegada al borde. Es la
        // señal que sobrevive a tener la tarjeta medio tapada por el fantasma que
        // acompaña al cursor mientras se arrastra.
        if (elegida)
            ci.DrawRect(new Rect2(r.Position.X, r.Position.Y, 5f, r.Size.Y), Palette.Info);

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
    /// Body y cabeza con el mismo anclaje que usa el mundo —los dos apoyados
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

        // ⚠ LA ESCALA SALE DEL MUÑECO ENTERO, NO DEL CUERPO.
        //
        // Antes era `marco.alto / alto del cuerpo`, y el muñeco se salía del
        // recuadro por arriba: la cabeza NO se dibuja adentro del cuerpo, se
        // dibuja corrida hacia arriba por `HeadOffset.Y` —que ronda los -152 px
        // del arte x4—. O sea que el alto real del muñeco es el del cuerpo MÁS
        // ese corrimiento, y escalando solo por el cuerpo la cabeza siempre
        // quedaba afuera. Es lo que se veía en las tarjetas de la tienda.
        //
        // El ancho entra en la cuenta por lo mismo: un marco apaisado no puede
        // decidir la escala por el alto solo.
        var lienzo = Extensiones(cuerpo, apariencia);

        var caja = marco.Inset(4f);
        float s = Mathf.Min(caja.Size.Y / lienzo.Y, caja.Size.X / lienzo.X);

        ci.Doll(cuerpo, apariencia, s,
            new Vector2(caja.Position.X + (caja.Size.X / 2f), caja.End.Y),
            Colors.White);
    }

    /// <summary>
    /// Cuánto mide el muñeco entero —cuerpo más cabeza corrida— en píxeles del arte.
    /// </summary>
    /// <remarks>
    /// El alto se mide <b>desde los pies</b>, que es el punto en el que
    /// <see cref="Doll"/> ancla las dos piezas. Como <c>HeadOffset.Y</c> es
    /// negativo, restarlo suma: <c>altoCabeza − offset</c> es «hasta dónde llega
    /// la cabeza por encima del piso».
    /// </remarks>
    private static Vector2 Extensiones(Texture2D cuerpo, Appearance apariencia)
    {
        var c = cuerpo.GetSize();
        var cabeza = GraphicsLoader.Texture($"{apariencia.Head}.sur");

        if (cabeza is null)
            return c;

        var h = cabeza.GetSize();

        return new Vector2(
            Mathf.Max(c.X, h.X + (Mathf.Abs(apariencia.HeadOffset.X) * 2f)),
            Mathf.Max(c.Y, h.Y - apariencia.HeadOffset.Y));
    }

    /// <summary>El cuerpo al sur de una clase, con su apariencia. Compartido.</summary>
    public static Texture2D? BodyOf(RosterClass ficha, out Appearance? apariencia)
    {
        apariencia = AppearanceLoader.De(ficha.DefenderClass, ficha.VisualTier);

        if (apariencia is null || apariencia.Body.Length == 0)
            return null;

        return GraphicsLoader.Texture($"{apariencia.Body}.sur");
    }

    /// <summary>
    /// Body + cabeza apoyados en <paramref name="pie"/>, a escala
    /// <paramref name="s"/>. Mismo anclaje que usa el mundo.
    /// </summary>
    public static void Doll(
        this CanvasItem ci, Texture2D cuerpo, Appearance apariencia, float s, Vector2 pie, Color tinte)
    {
        var tamanoCuerpo = cuerpo.GetSize() * s;

        ci.DrawTextureRect(cuerpo,
            new Rect2(pie.X - (tamanoCuerpo.X / 2f), pie.Y - tamanoCuerpo.Y,
                tamanoCuerpo.X, tamanoCuerpo.Y),
            tile: false, tinte);

        var cabeza = GraphicsLoader.Texture($"{apariencia.Head}.sur");

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
            (Palette.Rarity(PerkTier.Bronze), Loc.Tier(PerkTier.Bronze)),
            (Palette.Rarity(PerkTier.Silver), Loc.Tier(PerkTier.Silver)),
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
