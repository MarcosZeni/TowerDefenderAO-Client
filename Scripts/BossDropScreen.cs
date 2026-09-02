using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// Lo que soltó el jefe: los Oro que ofrece y cuáles se lleva el jugador.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Antes esto no tenía pantalla propia.</b> El Oro del jefe entraba
/// como una tarjeta más adentro del mercado, en la columna de perks, al lado de
/// las cosas que se compran con oro. O sea que el premio de matar al jefe —el
/// único momento de la corrida en que aparece un comportamiento gratis, y una de
/// las pocas veces que se define la build— se veía igual que una oferta de
/// bronce de 300.</para>
///
/// <para>Y el mercado es <b>cerrable</b>. Esto no: es la única pantalla del juego
/// que frena la corrida y no se puede saltear con Esc. Compartir ventana con algo
/// cerrable obligaba a que la del mercado cambiara de reglas según lo que
/// tuviera adentro, que es exactamente el tipo de estado que después nadie
/// entiende.</para>
///
/// <para>⚠ <b>No siempre se elige uno.</b> Desde el área <c>bossGoldLateArea</c>
/// el jefe deja llevarse <b>dos de tres</b>, y por eso la pantalla lleva la cuenta
/// en grande: una pantalla que dice «elegí» y después no se cierra al elegir se
/// lee como un bug, no como una segunda elección.</para>
///
/// <para><b>Información total</b>, igual que el mercado: cada tarjeta dice qué
/// hace el perk, qué clases pueden equiparlo y —lo que no se puede deducir de
/// ninguna otra pantalla— <b>si completa una receta</b> que el jugador ya conoce
/// y tiene a medias. Ese último renglón es el que convierte el sorteo del jefe en
/// una decisión de build.</para>
/// </remarks>
public partial class BossDropScreen : Control
{
    private readonly Game _juego;

    /// <summary>La tarjeta que está a un click de tomarse. <c>-1</c> = ninguna.</summary>
    /// <remarks>
    /// ⚠ Doble click, igual que el mercado y que vender un defensor. Acá pesa más
    /// que en ningún lado: <b>lo que no se elige se pierde</b>, el jefe no vuelve,
    /// y son ocho oportunidades en toda la corrida. Un click de más en la tarjeta
    /// equivocada no se deshace ni jugando mejor.
    /// </remarks>
    private int _armado = -1;

    private int _bajoElCursor = -1;

    public BossDropScreen(Game juego)
    {
        _juego = juego;
    }

    /// <summary>
    /// Si hay una oferta de jefe sin resolver.
    /// </summary>
    /// <remarks>
    /// ⚠ Cede ante la mochila y nada más. Que el jugador pueda abrir la mochila
    /// con esto abierto es necesario, no un lujo: con la mochila llena, tomar un
    /// Oro lo tira a la basura, así que hay que poder ir a hacer lugar antes de
    /// decidir. La oferta no se pierde — sigue pendiente y la ventana vuelve.
    /// </remarks>
    public bool IsOpen
        => _juego.Run is { BossPicksLeft: > 0 } p
           && p.OpenChoices.Length > 0
           && _juego.Backpack is not { IsOpen: true };

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        bool abierta = IsOpen;

        Visible = abierta;

        if (!abierta)
        {
            _armado = -1;
            _bajoElCursor = -1;
            return;
        }

        QueueRedraw();
    }

    // =====================================================================
    // Geometría
    // =====================================================================

    private static Rect2 Ventana()
        => new((Canvas.CanvasWidth - 1320f) / 2f, (Canvas.CanvasHeight - 700f) / 2f, 1320f, 700f);

    /// <summary>
    /// La tarjeta de una opción.
    /// </summary>
    /// <remarks>
    /// Verticales y grandes, al revés que las apaisadas del mercado. Son dos o
    /// tres en toda la pantalla y no compiten con nada: acá el lugar sobra, y lo
    /// que se gana con él es que el efecto y la receta entren enteros sin
    /// recortarse.
    /// </remarks>
    private static Rect2 TarjetaRect(int i, int cuantas)
    {
        var v = Ventana();

        const float Hueco = 20f;
        const float Techo = 400f;

        float disponible = v.Size.X - 60f;
        float ancho = Mathf.Min(Techo, (disponible - (Hueco * (cuantas - 1))) / cuantas);

        float total = (ancho * cuantas) + (Hueco * (cuantas - 1));
        float x0 = v.Position.X + ((v.Size.X - total) / 2f);

        return new Rect2(x0 + (i * (ancho + Hueco)), v.Position.Y + 130f, ancho, 476f);
    }

    private int TarjetaEn(Vector2 posicion)
    {
        int cuantas = _juego.Run?.OpenChoices.Length ?? 0;

        for (int i = 0; i < cuantas; i++)
        {
            if (TarjetaRect(i, cuantas).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    // =====================================================================
    // Entrada
    // =====================================================================

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_UnhandledInput</c>, y todo click se come.
    /// </summary>
    /// <remarks>
    /// Es modal de verdad: la corrida está frenada y el tablero no se toca. Un
    /// click que se escape pondría un defensor detrás de la ventana, y el jugador
    /// vería el oro bajar sin ver la ficha.
    /// </remarks>
    public override void _Input(InputEvent evento)
    {
        if (!IsOpen)
            return;

        switch (evento)
        {
            case InputEventMouseMotion:
            {
                int antes = _bajoElCursor;

                _bajoElCursor = TarjetaEn(GetGlobalMousePosition());

                // Salir de la tarjeta armada la desarma.
                if (_bajoElCursor != antes)
                    _armado = -1;

                QueueRedraw();
                break;
            }

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                Apretar(TarjetaEn(GetGlobalMousePosition()));
                GetViewport().SetInputAsHandled();
                break;
            }

            // ⚠ Sin Esc y sin atajos numéricos. Esta pantalla NO se saltea: el Oro
            // del jefe es gratis, así que un botón de pasar sería una trampa, y un
            // «1-2-3» que resuelve de un toque la decisión más cara de la corrida
            // es el peor atajo posible.
            case InputEventKey { Pressed: true, Echo: false }:
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void Apretar(int indice)
    {
        if (indice < 0)
            return;

        if (_armado != indice)
        {
            _armado = indice;
            QueueRedraw();
            return;
        }

        _armado = -1;
        _bajoElCursor = -1;
        _juego.Choose(indice);
    }

    // =====================================================================
    // Dibujo
    // =====================================================================

    public override void _Draw()
    {
        var p = _juego.Run;

        if (p is null || !IsOpen)
            return;

        var datos = DataLoader.Current;

        UiTooltip.Begin(GetGlobalMousePosition());

        // El mundo se oscurece más que en el mercado: acá no hay nada que mirar
        // atrás. La defensa está congelada y la decisión no depende del tablero.
        this.Scrim();

        var v = Ventana();

        this.Panel(v, elevado: true, opacidad: 1f);
        this.Glow(v, Palette.Legendary, 1.4f, 6);
        DrawRect(v.Row(0f, 3f), Palette.Legendary);

        Cabecera(p, v);

        for (int i = 0; i < p.OpenChoices.Length; i++)
            DibujarTarjeta(p, datos, TarjetaRect(i, p.OpenChoices.Length), i);

        Pie(p, v);

        UiTooltip.Draw(this);
    }

    private void Cabecera(Run p, Rect2 v)
    {
        this.SectionTitle(v.Row(16f, 36f), Loc.Get("ui.jefe.titulo"), Palette.Legendary);

        // ⚠ Cuántos van y de cuántos, en el subtítulo y no escondido abajo. Es lo
        // primero que cambia entre un jefe temprano y uno tardío, y sin decirlo la
        // pantalla que no se cierra al primer click se lee como colgada.
        this.Label(v.Row(56f, 26f),
            $"{Loc.Get("ui.jefe.elegi")} {p.BossPicksLeft} {Loc.Get("ui.jefe.de")} " +
            $"{p.OpenChoices.Length}",
            20, Palette.TextPrimary, HorizontalAlignment.Center);

        this.Label(v.Row(84f, 22f), Loc.Get("ui.jefe.se_pierde"), 14, Palette.TextMuted,
            HorizontalAlignment.Center);
    }

    private void Pie(Run p, Rect2 v)
    {
        // La mochila, porque tomar un Oro con la mochila llena lo tira. Es la
        // única acción útil que se puede hacer desde acá, así que se dice acá.
        int libres = p.Backpack.Cap - p.Backpack.Used;

        this.Label(v.Row(v.Size.Y - 44f, 24f),
            libres > 0
                ? $"{Loc.Get("ui.jefe.mochila")} {libres}"
                : Loc.Get("ui.jefe.mochila_llena"),
            15, libres > 0 ? Palette.TextMuted : Palette.Danger,
            HorizontalAlignment.Center);
    }

    /// <summary>
    /// Una opción: el perk entero, con quién lo puede usar y qué receta cierra.
    /// </summary>
    /// <remarks>
    /// El marco va <b>siempre encendido</b>, no solo bajo el cursor: las tres
    /// frenan la corrida y ninguna se puede ignorar.
    /// </remarks>
    private void DibujarTarjeta(Run p, BalanceData datos, Rect2 r, int indice)
    {
        var opcion = p.OpenChoices[indice];
        var def = datos.Perks.Get(opcion.PerkId);
        var acento = Palette.Rarity(def.Tier);

        this.Slot(r, acento, elegido: true);

        if (_bajoElCursor == indice)
            DrawRect(r, acento.Alpha(0.10f));

        // --- El icono, grande y arriba.
        var icono = new Rect2(r.Position.X + ((r.Size.X - 132f) / 2f), r.Position.Y + 18f, 132f, 132f);

        this.Slot(icono, acento);

        if (!IconLoader.Draw(this, def.Key,
                icono.Position.X + 14f, icono.Position.Y + 14f, icono.Size.X - 28f))
        {
            this.Label(icono, Loc.Perk(def)[..1].ToUpperInvariant(), 60, acento,
                HorizontalAlignment.Center);
        }

        // --- El tier y el nombre.
        this.Label(new Rect2(r.Position.X + 12f, icono.End.Y + 8f, r.Size.X - 24f, 20f),
            Loc.Tier(def.Tier).ToUpperInvariant(), 14, acento, HorizontalAlignment.Center);

        this.Label(new Rect2(r.Position.X + 12f, icono.End.Y + 28f, r.Size.X - 24f, 32f),
            Loc.Perk(def).ToUpperInvariant(), 24, Palette.TextPrimary, HorizontalAlignment.Center);

        // --- Qué hace.
        var cuerpo = new Rect2(r.Position.X + 14f, icono.End.Y + 66f, r.Size.X - 28f, 150f);

        this.EffectRow(cuerpo.Row(0f, 22f), def, acento);

        // --- QUÉ HACE. Es la tarjeta donde más falta hacía: el jugador elige uno
        //     de dos Oro, ocho veces en toda la corrida, y hasta acá lo único que
        //     leía era «sobrepenetra 40%» — un nombre y un número sin unidad.
        this.Paragraph(cuerpo.Row(26f, 48f), Loc.PerkText(def), 14, Palette.TextSecondary);

        // --- A quién le sirve. Un Oro que ninguna clase del roster puede equipar
        //     es una de las ocho oportunidades tirada a la basura, así que se dice
        //     con el color, no con un rótulo.
        this.Label(cuerpo.Row(78f, 18f), Loc.Get("ui.jefe.le_sirve").ToUpperInvariant(), 12,
            Palette.TextMuted);

        DibujarClases(p, def, cuerpo.Row(98f, 50f));

        this.Rule(r, r.Size.Y - 76f);

        DibujarReceta(p, datos, def, new Rect2(r.Position.X + 14f, r.End.Y - 66f, r.Size.X - 28f, 40f));

        DibujarArmado(r, indice);
    }

    /// <summary>Las clases que lo pueden equipar; las del roster, encendidas.</summary>
    private void DibujarClases(Run p, PerkDefinition def, Rect2 r)
    {
        var texto = new System.Text.StringBuilder();
        bool alguna = false;

        foreach (var c in ChoiceBuilder.All)
        {
            if (!def.WorksFor(c))
                continue;

            if (texto.Length > 0)
                texto.Append(" · ");

            texto.Append(Loc.Class(c));

            if (p.Roster.Has(c))
                alguna = true;
        }

        if (texto.Length == 0)
        {
            this.Paragraph(r, Loc.Get("ui.tienda.cualquier_clase"), 13, Palette.Success);
            return;
        }

        // ⚠ En rojo cuando NINGUNA clase del roster lo puede usar. Es la única
        // forma de que el jugador vea, sin abrir la mochila, que ese Oro no le
        // sirve para nada hoy — y sigue pudiendo elegirlo, porque puede reclutar
        // la clase más adelante.
        this.Paragraph(r, texto.ToString(), 13, alguna ? Palette.TextMuted : Palette.Danger);
    }

    /// <summary>
    /// Si este Oro cierra una receta que el jugador ya conoce y tiene a medias.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es lo único de la tarjeta que no se puede deducir de ninguna
    /// otra pantalla</b>, y es lo que convierte el sorteo del jefe en una decisión
    /// de build: entre dos Oro parecidos, el que completa un Único vale el doble,
    /// porque libera además una ranura de comportamiento.</para>
    ///
    /// <para>Solo mira los planos que el jugador <b>conoce</b>. Anunciar una
    /// receta que no puede fusionar sería prometer algo que la mochila va a
    /// rebotar.</para>
    /// </remarks>
    private void DibujarReceta(Run p, BalanceData datos, PerkDefinition def, Rect2 r)
    {
        for (int i = 0; i < datos.Blueprints.All.Count; i++)
        {
            var plano = datos.Blueprints.All[i];

            if (plano.GoldA != def.Id && plano.GoldB != def.Id)
                continue;

            if (!p.KnowsBlueprint(i))
                continue;

            int pareja = plano.GoldA == def.Id ? plano.GoldB : plano.GoldA;

            if (!TienePerk(p, pareja))
                continue;

            var unico = datos.Perks.Get(plano.ResultingUnique);

            this.Label(r.Row(0f, 18f), Loc.Get("ui.jefe.completa").ToUpperInvariant(), 12,
                Palette.Legendary);

            this.Label(r.Row(18f, 22f), Loc.Perk(unico).ToUpperInvariant(), 17,
                Palette.Legendary);

            return;
        }
    }

    private static bool TienePerk(Run p, int idPerk)
    {
        foreach (var cosa in p.Backpack.Items)
        {
            if (cosa.Kind == ItemType.Perk && cosa.Id == idPerk)
                return true;
        }

        return false;
    }

    private void DibujarArmado(Rect2 r, int indice)
    {
        if (_armado != indice)
            return;

        DrawRect(r, Palette.Warning, filled: false, width: 2.5f);

        var banda = new Rect2(r.Position.X + 2f, r.End.Y - 32f, r.Size.X - 4f, 30f);

        DrawRect(banda, Palette.Warning.Alpha(0.92f));

        this.Label(banda, Loc.Get("ui.mercado.confirmar").ToUpperInvariant(), 14,
            Palette.BackgroundMain, HorizontalAlignment.Center);
    }
}
