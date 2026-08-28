using Godot;
using TowerOfArgentum.Simulacion;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El modal de "levelear o contratar". <b>No se puede cerrar sin elegir.</b>
/// </summary>
/// <remarks>
/// <para>Overlay sobre el mundo oscurecido, no una pantalla aparte: el jugador
/// tiene que seguir viendo el estado de su defensa mientras decide (INTERFAZ.md
/// §3).</para>
///
/// <para>Las tarjetas son las <b>del diseño</b> —las mismas de la tienda, con la
/// misma proporción vertical y el mismo marco de rareza— pero centradas y más
/// grandes. Es la única pantalla que el panel del diseñador no dibuja, y la
/// regla acá es la de siempre: cuando el diseño no tiene una pieza, se arma con
/// las que sí tiene en vez de inventar un estilo nuevo.</para>
///
/// <para>⚠ <b>Muestra qué desbloquea levelear.</b> Las dos opciones dan +1 slot
/// de perk; si eso no se ve, contratar parece siempre mejor —trae un rol nuevo—
/// y la decisión "ancho o profundo" se cae sola.</para>
/// </remarks>
public partial class PantallaEleccion : Control
{
    private readonly Juego _juego;
    private int _bajoElCursor = -1;

    /// <summary>
    /// La tarjeta, con la proporción de las de la tienda del diseño (132×205).
    /// </summary>
    /// <remarks>
    /// Conservar la proporción es lo que hace que las tres se lean como las
    /// mismas tarjetas de la tienda y no como otra pantalla: acá hay más lugar,
    /// así que son más grandes, pero no son más anchas.
    /// </remarks>
    private const int AnchoTarjeta = 420;

    private const int AltoTarjeta = (int)(AnchoTarjeta * 205f / 132f);
    private const int Separacion = 40;

    public PantallaEleccion(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Stop y no Ignore: mientras el modal está abierto se come los clicks,
        // así el jugador no pone un defensor sin querer al apuntar a una tarjeta.
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        bool abierta = _juego.Run?.WaitingForChoice ?? false;

        Visible = abierta;
        MouseFilter = abierta ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        if (abierta)
            QueueRedraw();
    }

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, y es una cicatriz.
    /// </summary>
    /// <remarks>
    /// <para>Con <c>_GuiInput</c> el modal quedaba <b>sordo</b>: el click en la
    /// tarjeta no hacía nada, sin error y sin pista — el jugador no podía ni
    /// elegir su primera clase. Y las teclas 1-2-3 tampoco: <c>_GuiInput</c> solo
    /// recibe teclado con foco, y este Control nunca lo toma.</para>
    ///
    /// <para><c>_Input</c> llega siempre, antes que la GUI y que el resto. La
    /// posición se toma de <see cref="CanvasItem.GetGlobalMousePosition"/> y no
    /// del evento: la del evento viene en píxeles de la ventana, y con
    /// <c>stretch = keep</c> eso no coincide con el lienzo si la ventana no es
    /// exactamente 1920×1080.</para>
    ///
    /// <para>Mientras el modal está abierto, <b>todo click se marca atendido</b>,
    /// caiga donde caiga: es un modal, y un click que se escapa al mapa pondría
    /// un defensor sin querer.</para>
    /// </remarks>
    public override void _Input(InputEvent evento)
    {
        var partida = _juego.Run;

        if (partida is null || !partida.WaitingForChoice)
            return;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = TarjetaEn(GetGlobalMousePosition(), partida.OpenChoices.Length);
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
            {
                int i = TarjetaEn(GetGlobalMousePosition(), partida.OpenChoices.Length);

                if (i >= 0)
                    _juego.Choose(i);

                GetViewport().SetInputAsHandled();
                break;
            }

            // Las teclas 1-2-3 hacen lo mismo que el click. Con una corrida de
            // horas, poder elegir sin soltar el mouse importa. Se marcan
            // atendidas para que el 1-2-3 no cambie además la velocidad.
            case InputEventKey { Pressed: true, Echo: false } tecla:
            {
                int i = tecla.Keycode switch
                {
                    Key.Key1 => 0,
                    Key.Key2 => 1,
                    Key.Key3 => 2,
                    _ => -1,
                };

                if (i >= 0 && i < partida.OpenChoices.Length)
                {
                    _juego.Choose(i);
                    GetViewport().SetInputAsHandled();
                }

                break;
            }
        }
    }

    private static int TarjetaEn(Vector2 posicion, int cantidad)
    {
        for (int i = 0; i < cantidad; i++)
        {
            if (RectDe(i, cantidad).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    private static Rect2 RectDe(int i, int cantidad)
    {
        int anchoTotal = (cantidad * AnchoTarjeta) + ((cantidad - 1) * Separacion);
        int x = ((Escala.LienzoAncho - anchoTotal) / 2) + (i * (AnchoTarjeta + Separacion));
        int y = (Escala.LienzoAlto - AltoTarjeta) / 2;

        return new Rect2(x, y, AnchoTarjeta, AltoTarjeta);
    }

    public override void _Draw()
    {
        var partida = _juego.Run;

        if (partida is null || !partida.WaitingForChoice)
            return;

        var opciones = partida.OpenChoices;

        // El mundo se oscurece pero se sigue viendo: el jugador decide mirando su
        // defensa, no de memoria.
        this.Scrim();

        var cabecera = new Rect2(0, RectDe(0, opciones.Length).Position.Y - 110f,
            Escala.LienzoAncho, 46f);

        this.Label(cabecera, Loc.Get("ui.eleccion.elegi_una"), 42, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(cabecera.Row(48f, 26f), Loc.Get("ui.eleccion.no_se_puede_cerrar"), 18,
            Palette.TextMuted, HorizontalAlignment.Center);

        for (int i = 0; i < opciones.Length; i++)
            DibujarTarjeta(i, opciones[i], opciones.Length, partida);
    }

    private void DibujarTarjeta(int i, Choice opcion, int cantidad, Run partida)
    {
        var r = RectDe(i, cantidad);

        bool contrata = opcion.Kind == ChoiceType.Hire;
        bool esPerk = opcion.Kind == ChoiceType.Perk;

        // El Oro del jefe usa el dorado de su tier; contratar, el azul que en
        // todo el juego marca «esta es la clase elegida»; levelear, el ámbar de
        // lo que sube. Se distinguen antes de leer una palabra.
        var acento =
            esPerk ? Palette.Rarity(CargadorDatos.Actual.Perks.Get(opcion.PerkId).Tier)
            : contrata ? Palette.Info
            : Palette.Warning;

        this.Slot(r, acento, elegido: i == _bajoElCursor);

        var titulo = r.Row(18f, 26f);
        var icono = UiLayout.Square(r.Row(52f, r.Size.Y * 0.30f));
        var nombre = r.Row(52f + (r.Size.Y * 0.30f) + 10f, 44f);
        var cuerpo = new Rect2(
            r.Position.X + 28f, nombre.End.Y + 12f,
            r.Size.X - 56f, r.End.Y - nombre.End.Y - 62f);

        if (esPerk)
        {
            DibujarPerk(opcion, titulo, icono, nombre, cuerpo, acento);
        }
        else
        {
            DibujarClase(opcion, partida, contrata, titulo, icono, nombre, cuerpo, acento);
        }

        // El número de la tecla. Con una corrida de horas, elegir con la mano
        // izquierda sin soltar el mouse es la diferencia entre cien elecciones
        // cómodas y cien viajes del cursor.
        var tecla = new Rect2(r.End.X - 46f, r.End.Y - 40f, 30f, 28f);

        DrawRect(tecla, Palette.BackgroundMain.Alpha(0.9f));
        DrawRect(tecla, Palette.BorderSoft, filled: false, width: 1f);

        this.Label(tecla, $"{i + 1}", 17, Palette.TextMuted, HorizontalAlignment.Center);
    }

    /// <summary>Contratar una clase nueva, o subirle el nivel a una que ya está.</summary>
    private void DibujarClase(
        Choice opcion, Run partida, bool contrata,
        Rect2 titulo, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento)
    {
        // ⚠ El nombre sale de `Loc.Class`, nunca escrito a mano: el que se
        // escribe a mano termina siendo "arquero", que es lo que uno esperaría de
        // un tower defense y la clase que en Argentum Online no existe. El que
        // tira flechas es el Cazador.
        var clase = opcion.DefenderClass;
        var stats = CargadorDatos.Actual.StatsOf(clase);

        this.Label(titulo,
            Loc.Get(contrata ? "ui.eleccion.contratar" : "ui.eleccion.subir_de_nivel").ToUpperInvariant(),
            18, acento, HorizontalAlignment.Center);

        // El muñeco y no un icono: es lo que se va a ver en el mapa.
        var ficha = partida.Roster.Find(clase)
            ?? new RosterClass { DefenderClass = clase, MaxLevel = 1 };

        this.Portrait(icono, ficha);

        this.Label(nombre, Loc.Class(clase).ToUpperInvariant(), 36, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(cuerpo.Row(0f, 22f), Loc.Role(clase), 16, Palette.TextMuted,
            HorizontalAlignment.Center);

        float alto = 24f;

        this.StatRow(cuerpo.Row(30f, alto), Loc.Get("ui.eleccion.de_dano"), stats.Damage.ToString());
        this.StatRow(cuerpo.Row(30f + alto, alto), Loc.Get("ui.eleccion.alcance"), stats.RangeCells.ToString());
        this.StatRow(cuerpo.Row(30f + (alto * 2f), alto), Loc.Get("ui.eleccion.de_oro"), stats.Cost.ToString(), Palette.Gold);

        this.Rule(cuerpo, 30f + (alto * 3f) + 8f);

        float y = 30f + (alto * 3f) + 20f;

        if (contrata)
        {
            this.Label(cuerpo.Row(y, 24f), Loc.Get("ui.eleccion.rol_nuevo"), 18,
                Palette.TextSecondary, HorizontalAlignment.Center);

            this.Label(cuerpo.Row(y + 26f, 24f), Loc.Get("ui.eleccion.arranca_con_slot"), 18,
                Palette.TextSecondary, HorizontalAlignment.Center);

            return;
        }

        int slotsAntes = partida.Roster.Find(clase)?.OpenSlots ?? 1;
        int tope = CargadorDatos.Actual.Classes.PerkSlotsPerClass;
        int slotsDespues = System.Math.Min(opcion.ResultingLevel, tope);

        this.Label(cuerpo.Row(y, 24f),
            $"{Loc.Get("ui.eleccion.level")} {opcion.ResultingLevel - 1} → {opcion.ResultingLevel}",
            19, Palette.TextSecondary, HorizontalAlignment.Center);

        // ⚠ Esta línea es la que hace que la elección sea una elección. Sin ella,
        // contratar parece siempre mejor porque trae algo visiblemente nuevo, y
        // levelear parece "solo números".
        this.Label(cuerpo.Row(y + 28f, 24f),
            slotsDespues > slotsAntes
                ? $"{Loc.Get("ui.eleccion.abre_slot")} {slotsDespues} {Loc.Get("ui.eleccion.de")} {tope}."
                : Loc.Get("ui.eleccion.slots_completos"),
            18, slotsDespues > slotsAntes ? Palette.Legendary : Palette.TextMuted,
            HorizontalAlignment.Center);
    }

    /// <summary>
    /// La tarjeta de un perk de Oro ofrecido por un jefe.
    /// </summary>
    /// <remarks>
    /// ⚠ Dice <b>a qué clase le sirve</b> y no solo el nombre. Un Oro es un
    /// comportamiento que solo pueden usar dos o tres de las nueve clases, y
    /// elegir entre dos nombres sin saber quién los puede equipar es elegir a
    /// ciegas — que es exactamente lo que la oferta del jefe vino a arreglar.
    /// </remarks>
    private void DibujarPerk(
        Choice opcion, Rect2 titulo, Rect2 icono, Rect2 nombre, Rect2 cuerpo, Color acento)
    {
        var def = CargadorDatos.Actual.Perks.Get(opcion.PerkId);

        this.Label(titulo, Loc.Tier(def.Tier).ToUpperInvariant(), 18, acento,
            HorizontalAlignment.Center);

        this.Slot(icono, acento);

        if (!CargadorIconos.Dibujar(this, def.Key,
                icono.Position.X + 10f, icono.Position.Y + 10f, icono.Size.X - 20f))
        {
            this.Label(icono, Loc.Perk(def)[..1].ToUpperInvariant(),
                (int)(icono.Size.X * 0.5f), acento, HorizontalAlignment.Center);
        }

        this.Label(nombre, Loc.Perk(def).ToUpperInvariant(), 30, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(cuerpo.Row(0f, 24f), $"{Loc.Effect(def.Effect)} · {def.Magnitude}%", 18,
            Palette.TextSecondary, HorizontalAlignment.Center);

        this.Rule(cuerpo, 32f);

        this.Label(cuerpo.Row(38f, 22f), Loc.Get("ui.eleccion.sirve_a"), 15, Palette.TextMuted,
            HorizontalAlignment.Center);

        // Las clases que lo pueden equipar, no todas las del roster.
        var sirve = new System.Text.StringBuilder();

        foreach (var c in ChoiceBuilder.All)
        {
            if (!def.WorksFor(c))
                continue;

            if (sirve.Length > 0)
                sirve.Append(" · ");

            sirve.Append(Loc.Class(c));
        }

        this.Paragraph(cuerpo.Row(62f, cuerpo.Size.Y - 88f), sirve.ToString(), 18,
            Palette.TextSecondary);

        this.Label(cuerpo.Row(cuerpo.Size.Y - 24f, 22f), Loc.Get("ui.eleccion.va_a_la_mochila"), 15,
            Palette.TextMuted, HorizontalAlignment.Center);
    }
}
