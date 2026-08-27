using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// La mochila y el panel de clase, en un solo overlay.
/// </summary>
/// <remarks>
/// <para>Van juntos porque la acción que importa —equipar— necesita las dos
/// mitades a la vista: qué tengo guardado y dónde lo puedo poner. Separarlas
/// obligaría a abrir y cerrar dos pantallas para una sola decisión.</para>
///
/// <para>Las dos acciones que <b>destruyen</b> avisan antes: sacar un perk
/// equipado y fusionar. La primera no vuelve a la mochila, la segunda consume los
/// dos Oro.</para>
/// </remarks>
public partial class PantallaMochila : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;

    private bool _abierta;
    private int _cosaElegida = -1;
    private DefenderClass? _claseElegida;

    /// <summary>Lo que hay que confirmar antes de hacerlo, porque destruye.</summary>
    private (DefenderClass DefenderClass, int Slot)? _confirmarSacar;

    public PantallaMochila(Juego juego)
    {
        _juego = juego;
    }

    public bool Abierta => _abierta;

    public void Alternar()
    {
        _abierta = !_abierta;
        _cosaElegida = -1;
        _confirmarSacar = null;
    }

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        Visible = _abierta;
        MouseFilter = _abierta ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        if (_abierta)
            QueueRedraw();
    }

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la misma cicatriz que
    /// <see cref="PantallaEleccion"/>: con <c>_GuiInput</c> la pantalla quedaba
    /// sorda a clicks y a teclas. La posición sale de
    /// <see cref="CanvasItem.GetGlobalMousePosition"/>, no del evento, para que
    /// funcione con la ventana a cualquier tamaño.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        if (!_abierta)
            return;

        if (evento is InputEventKey tecla && tecla.Pressed && tecla.Keycode == Key.Escape)
        {
            Alternar();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (evento is not InputEventMouseButton boton || !boton.Pressed ||
            boton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        // Abierta, el click es de esta pantalla caiga donde caiga: sin esto un
        // click al costado pondría un defensor atrás del panel.
        GetViewport().SetInputAsHandled();

        var posicion = GetGlobalMousePosition();

        var partida = _juego.Run;

        // Confirmar la destrucción, si hay una pendiente.
        if (_confirmarSacar is { } pendiente)
        {
            if (RectConfirmar().HasPoint(posicion))
            {
                _juego.RemovePerk(pendiente.DefenderClass, pendiente.Slot);
                _confirmarSacar = null;
            }
            else if (RectCancelar().HasPoint(posicion))
            {
                _confirmarSacar = null;
            }

            return;
        }

        // Un lugar de la mochila.
        for (int i = 0; i < partida.Backpack.Items.Count; i++)
        {
            if (RectLugar(i).HasPoint(posicion))
            {
                _cosaElegida = _cosaElegida == i ? -1 : i;
                return;
            }
        }

        // Una clase: si hay algo elegido, se equipa ahí.
        var clases = partida.Roster.Classes;

        for (int i = 0; i < clases.Count; i++)
        {
            if (!RectClase(i).HasPoint(posicion))
                continue;

            if (_cosaElegida >= 0)
            {
                _juego.EquiparDeMochila(_cosaElegida, clases[i].DefenderClass);
                _cosaElegida = -1;
            }
            else
            {
                _claseElegida = clases[i].DefenderClass;
            }

            return;
        }

        // Un slot de perk de la clase mirada: sacarlo pide confirmación.
        if (_claseElegida is { } clase)
        {
            var ficha = partida.Roster.Find(clase);

            if (ficha is not null)
            {
                for (int s = 0; s < ficha.Perks.Count; s++)
                {
                    if (RectSlot(s).HasPoint(posicion))
                    {
                        _confirmarSacar = (clase, s);
                        return;
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Geometría
    // -------------------------------------------------------------------------

    private const int LugarLado = 92;
    private const int LugaresPorFila = 5;

    private static Rect2 RectLugar(int i)
    {
        int fila = i / LugaresPorFila;
        int col = i % LugaresPorFila;

        return new Rect2(
            180 + (col * (LugarLado + 12)),
            300 + (fila * (LugarLado + 12)),
            LugarLado, LugarLado);
    }

    private static Rect2 RectClase(int i)
        => new(760, 300 + (i * 64), 380, 54);

    private static Rect2 RectSlot(int i)
        => new(1200 + (i * 92), 300, 84, 84);

    private static Rect2 RectConfirmar()
        => new(Escala.LienzoAncho / 2 - 220, Escala.LienzoAlto / 2 + 30, 200, 56);

    private static Rect2 RectCancelar()
        => new(Escala.LienzoAncho / 2 + 20, Escala.LienzoAlto / 2 + 30, 200, 56);

    // -------------------------------------------------------------------------
    //  Dibujo
    // -------------------------------------------------------------------------

    public override void _Draw()
    {
        var partida = _juego.Run;
        if (partida is null)
            return;

        DrawRect(new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto), new Color(0, 0, 0, 0.82f));

        DibujarMochila(partida);
        DibujarClases(partida);
        DibujarPerksDeClase(partida);

        if (_confirmarSacar is not null)
            DibujarConfirmacion();

        Text(Loc.Get("ui.mochila.esc_cerrar"), 180, Escala.LienzoAlto - 60, 18, Gris);
    }

    private void DibujarMochila(Run partida)
    {
        var mochila = partida.Backpack;
        bool llena = mochila.Ocupados >= CargadorDatos.Actual.Curves.Defenders.BackpackSlots;

        Text($"{Loc.Get("ui.mochila.titulo")}  {mochila.Ocupados} / {CargadorDatos.Actual.Curves.Defenders.BackpackSlots}",
            180, 260, 30, llena ? new Color(0.95f, 0.6f, 0.3f) : Blanco);

        if (llena)
        {
            // ⚠ Cuando está llena, el próximo drop obliga a destruir algo. Decirlo
            // ANTES es lo que convierte eso en una decisión y no en una sorpresa.
            Text(Loc.Get("ui.mochila.llena"),
                470, 260, 18, new Color(0.95f, 0.6f, 0.3f));
        }

        for (int i = 0; i < CargadorDatos.Actual.Curves.Defenders.BackpackSlots; i++)
        {
            var r = RectLugar(i);
            bool ocupado = i < mochila.Items.Count;
            bool elegido = i == _cosaElegida;

            DrawRect(r, ocupado ? new Color(0.13f, 0.13f, 0.18f) : new Color(0.07f, 0.07f, 0.09f));
            DrawRect(r,
                elegido ? new Color(0.4f, 0.9f, 0.5f) : new Color(1, 1, 1, 0.12f),
                filled: false, width: elegido ? 3f : 1.5f);

            if (!ocupado)
                continue;

            var cosa = mochila.Items[i];

            if (cosa.Kind == ItemType.Perk)
            {
                var def = CargadorDatos.Actual.Perks.Get(cosa.Id);

                DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 6), ColorDe(def.Tier));
                TextoAjustado(Loc.Perk(def), r.Position.X + 8, r.Position.Y + 34, 15, Blanco, LugarLado - 16);
                Text(Loc.Tier(def.Tier), r.Position.X + 8, r.Position.Y + 76, 13, ColorDe(def.Tier));
            }
            else
            {
                var arma = mochila.WeaponById(cosa.Id);

                // La franja de arriba lleva el color de la RAREZA, no un gris
                // genérico: es lo primero que mira el jugador al abrir la mochila,
                // y una legendaria tiene que gritar desde el otro lado de la
                // pantalla.
                DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 6),
                    ColorDeRareza(arma?.Rarity ?? WeaponRarity.Common));

                TextoAjustado(arma is null ? "?" : Loc.Weapon(arma), r.Position.X + 8, r.Position.Y + 34, 14, Blanco, LugarLado - 16);

                // Si tiene habilidad se dice, aunque sea corto. Un arma con
                // habilidad y números flojos puede valer más que una sin ella, y
                // el jugador no puede saberlo si solo ve el precio.
                if (arma is not null && arma.Ability != WeaponAbility.None)
                {
                    Text("★", r.Position.X + LugarLado - 22, r.Position.Y + 24, 18,
                        ColorDeRareza(arma.Rarity));
                }

                Text($"{arma?.Value ?? 0} {Loc.Get("ui.mochila.oro")}",
                    r.Position.X + 8, r.Position.Y + 76, 13, Gris);
            }
        }
    }

    private void DibujarClases(Run partida)
    {
        Text(Loc.Get("ui.mochila.equipar_en"), 760, 260, 30, Blanco);

        var clases = partida.Roster.Classes;

        for (int i = 0; i < clases.Count; i++)
        {
            var ficha = clases[i];
            var r = RectClase(i);
            bool mirada = _claseElegida == ficha.DefenderClass;

            // Si hay algo elegido en la mochila, se marca en verde dónde entra.
            bool aceptaLoElegido = AceptaLoElegido(partida, ficha);

            DrawRect(r, new Color(0.11f, 0.11f, 0.15f));
            DrawRect(r,
                aceptaLoElegido ? new Color(0.4f, 0.9f, 0.5f) :
                mirada ? new Color(0.6f, 0.7f, 0.95f) : new Color(1, 1, 1, 0.12f),
                filled: false, width: aceptaLoElegido || mirada ? 3f : 1.5f);

            Text(Loc.Class(ficha.DefenderClass), r.Position.X + 14, r.Position.Y + 34, 22, Blanco);

            Text($"{Loc.Get("ui.mochila.nv")} {ficha.Level} · {ficha.Perks.Count}/{ficha.OpenSlots}",
                r.Position.X + 250, r.Position.Y + 34, 18, Gris);
        }
    }

    /// <summary>
    /// ¿Lo que está elegido en la mochila entra en esta clase?
    /// </summary>
    /// <remarks>
    /// Marca en verde dónde se puede soltar, <b>antes</b> del click. Para un perk
    /// hay que mirar tres cosas —que la clase lo acepte, que haya slot abierto y
    /// que no se pase del tope de apilado—; el equipo siempre entra, porque su
    /// ranura se pisa y lo anterior vuelve a la mochila.
    /// </remarks>
    private bool AceptaLoElegido(Run partida, RosterClass ficha)
    {
        if (_cosaElegida < 0 || _cosaElegida >= partida.Backpack.Items.Count)
            return false;

        var cosa = partida.Backpack.Items[_cosaElegida];

        if (cosa.Kind == ItemType.Weapon)
            return true;

        var def = CargadorDatos.Actual.Perks.Get(cosa.Id);

        if (!def.WorksFor(ficha.DefenderClass) || !ficha.HasFreeSlot)
            return false;

        int yaTiene = 0;
        foreach (int p in ficha.Perks)
            if (p == cosa.Id)
                yaTiene++;

        return yaTiene < def.MaxStacked;
    }

    private void DibujarPerksDeClase(Run partida)
    {
        if (_claseElegida is not { } clase)
        {
            Text(Loc.Get("ui.mochila.elegi_clase"), 1200, 280, 18, Gris);
            return;
        }

        var ficha = partida.Roster.Find(clase);
        if (ficha is null)
            return;

        Text($"{Loc.Class(clase)} · {Loc.Get("ui.mochila.nivel")} {ficha.Level}", 1200, 260, 26, Blanco);

        for (int s = 0; s < CargadorDatos.Actual.Classes.PerkSlotsPerClass; s++)
        {
            var r = RectSlot(s);
            bool abierto = s < ficha.OpenSlots;
            bool lleno = s < ficha.Perks.Count;

            // ⚠ Los slots cerrados se ven CERRADOS, no ocultos: el jugador tiene
            // que ver qué le falta para saber que levelear sirve.
            DrawRect(r, abierto ? new Color(0.13f, 0.13f, 0.18f) : new Color(0.05f, 0.05f, 0.06f));
            DrawRect(r, new Color(1, 1, 1, abierto ? 0.15f : 0.06f), filled: false, width: 1.5f);

            if (!abierto)
            {
                Text($"{Loc.Get("ui.mochila.nv")} {s + 1}",
                    r.Position.X + 20, r.Position.Y + 48, 16, new Color(0.4f, 0.4f, 0.42f));
                continue;
            }

            if (!lleno)
                continue;

            var def = CargadorDatos.Actual.Perks.Get(ficha.Perks[s]);
            DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 5), ColorDe(def.Tier));
            TextoAjustado(Loc.Perk(def), r.Position.X + 6, r.Position.Y + 34, 14, Blanco, 74);
        }

        var stats = partida.Roster.StatsOf(clase, partida.Backpack);

        // ⚠ Los dos vienen en escala interna —centésimas de punto y milésimas de
        // celda—, así que pasan por Loc. Es la pantalla donde se ve si un perk de
        // alcance sirvió: con el redondeo a celdas enteras de antes, equipar
        // Halcón no movía este número ni un poco.
        Text($"{Loc.Get("ui.mochila.dano")} {Loc.Damage(stats.Damage)} · " +
             $"{Loc.Get("ui.mochila.alcance")} {Loc.Range(stats.RangeMilli)} · " +
             $"{Loc.Get("ui.mochila.cada")} {stats.CadenceTicks} {Loc.Get("ui.mochila.ticks")}",
            1200, 420, 19, new Color(0.85f, 0.85f, 0.82f));

        if (stats.Behavior != PerkEffect.None)
        {
            Text($"{Loc.Get("ui.mochila.habilidad")} {Loc.Effect(stats.Behavior)}",
                1200, 448, 19, new Color(0.95f, 0.8f, 0.4f));
        }

        Text(Loc.Get("ui.mochila.sacar_destruye"), 1200, 490, 16, Gris);
    }

    private void DibujarConfirmacion()
    {
        var panel = new Rect2(Escala.LienzoAncho / 2 - 320, Escala.LienzoAlto / 2 - 110, 640, 220);

        DrawRect(panel, new Color(0.10f, 0.07f, 0.07f, 0.98f));
        DrawRect(panel, new Color(0.9f, 0.35f, 0.3f), filled: false, width: 3f);

        Text(Loc.Get("ui.mochila.confirmar_titulo"),
            panel.Position.X + 40, panel.Position.Y + 60, 28, new Color(0.95f, 0.5f, 0.45f));

        Text(Loc.Get("ui.mochila.confirmar_detalle"),
            panel.Position.X + 40, panel.Position.Y + 100, 19, new Color(0.85f, 0.8f, 0.8f));

        var confirmar = RectConfirmar();
        DrawRect(confirmar, new Color(0.5f, 0.16f, 0.16f));
        Text(Loc.Get("ui.mochila.destruir"), confirmar.Position.X + 50, confirmar.Position.Y + 36, 21, Blanco);

        var cancelar = RectCancelar();
        DrawRect(cancelar, new Color(0.18f, 0.18f, 0.22f));
        Text(Loc.Get("ui.mochila.cancelar"), cancelar.Position.X + 50, cancelar.Position.Y + 36, 21, Blanco);
    }

    /// <summary>
    /// El color de la rareza de un arma.
    /// </summary>
    /// <remarks>
    /// Naranja para la legendaria y no dorado, para que no se confunda con el
    /// dorado del perk de Oro: son dos escalas distintas —una de perks, otra de
    /// armas— y mezclarlas visualmente haría que el jugador crea que un arma
    /// legendaria y un perk de Oro son la misma categoría de cosa.
    /// </remarks>
    private static Color ColorDeRareza(WeaponRarity r) => r switch
    {
        WeaponRarity.Rare => new Color(0.35f, 0.65f, 0.95f),
        WeaponRarity.Epic => new Color(0.70f, 0.40f, 0.95f),
        WeaponRarity.Legendary => new Color(0.98f, 0.55f, 0.15f),
        _ => new Color(0.62f, 0.62f, 0.66f),
    };

    private static Color ColorDe(PerkTier tier) => tier switch
    {
        PerkTier.Bronze => new Color(0.72f, 0.45f, 0.20f),
        PerkTier.Silver => new Color(0.75f, 0.78f, 0.82f),
        PerkTier.Gold => new Color(0.95f, 0.78f, 0.25f),
        _ => new Color(0.55f, 0.75f, 0.98f),
    };

    private static readonly Color Blanco = new(0.94f, 0.94f, 0.92f);
    private static readonly Color Gris = new(0.62f, 0.62f, 0.60f);

    private void Text(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);

    private void TextoAjustado(string s, float x, float y, int tamano, Color color, float ancho)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, ancho, tamano, color);
}
