using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// El modal de "levelear o contratar". <b>No se puede cerrar sin elegir.</b>
/// </summary>
/// <remarks>
/// <para>Overlay sobre el mundo oscurecido, no una pantalla aparte: el jugador
/// tiene que seguir viendo el estado de su defensa mientras decide (INTERFAZ.md
/// §3).</para>
///
/// <para>⚠ <b>Muestra qué desbloquea levelear.</b> Las dos opciones dan +1 slot
/// de perk; si eso no se ve, contratar parece siempre mejor —trae un rol nuevo—
/// y la decisión "ancho o profundo" se cae sola.</para>
/// </remarks>
public partial class PantallaEleccion : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;
    private int _bajoElCursor = -1;

    private const int AnchoTarjeta = 460;
    private const int AltoTarjeta = 340;
    private const int Separacion = 40;

    public PantallaEleccion(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
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
        DrawRect(new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto), new Color(0, 0, 0, 0.72f));

        Text(Loc.Get("ui.eleccion.elegi_una"),
            Escala.LienzoAncho / 2 - 130, 210, 44, new Color(0.95f, 0.95f, 0.92f));
        Text(Loc.Get("ui.eleccion.no_se_puede_cerrar"),
            Escala.LienzoAncho / 2 - 150, 250, 20, new Color(0.6f, 0.6f, 0.58f));

        for (int i = 0; i < opciones.Length; i++)
            DibujarTarjeta(i, opciones[i], opciones.Length, partida);
    }

    private void DibujarTarjeta(int i, Choice opcion, int cantidad, Run partida)
    {
        var r = RectDe(i, cantidad);
        bool resaltada = i == _bajoElCursor;

        bool contrata = opcion.Kind == ChoiceType.Hire;
        bool esPerk = opcion.Kind == ChoiceType.Perk;

        // El Oro del jefe usa su propio acento —el dorado del tier— para que se
        // distinga de un vistazo de las dos elecciones de roster: es otra decisión
        // y llega en otro momento de la corrida.
        var acento =
            esPerk ? new Color(0.93f, 0.78f, 0.31f) :
            contrata ? new Color(0.35f, 0.65f, 0.95f) :
                       new Color(0.95f, 0.72f, 0.25f);

        DrawRect(r, new Color(0.09f, 0.09f, 0.13f, 0.97f));
        DrawRect(r, resaltada ? acento : new Color(1, 1, 1, 0.15f), filled: false, width: resaltada ? 4f : 2f);

        // Franja de color arriba: contratar y levelear se distinguen de un
        // vistazo, antes de leer una palabra.
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 8), acento);

        float x = r.Position.X + 32;
        float y = r.Position.Y + 70;

        if (esPerk)
        {
            DibujarTarjetaDePerk(opcion, x, y, acento);
            Text($"[{i + 1}]", r.Position.X + r.Size.X - 70, r.Position.Y + r.Size.Y - 28, 24,
                new Color(0.5f, 0.5f, 0.5f));

            return;
        }

        Text(Loc.Get(contrata ? "ui.eleccion.contratar" : "ui.eleccion.subir_de_nivel"), x, y, 22, acento);

        y += 56;
        Text(Loc.Class(opcion.DefenderClass), x, y, 40, new Color(0.95f, 0.95f, 0.92f));

        y += 50;
        var stats = CargadorDatos.Actual.StatsOf(opcion.DefenderClass);
        Text($"{stats.Damage} {Loc.Get("ui.eleccion.de_dano")} · " +
             $"{Loc.Get("ui.eleccion.alcance")} {stats.RangeCells} · " +
             $"{stats.Cost} {Loc.Get("ui.eleccion.de_oro")}",
            x, y, 19, new Color(0.7f, 0.7f, 0.68f));

        y += 54;

        if (contrata)
        {
            Text(Loc.Get("ui.eleccion.rol_nuevo"), x, y, 21, new Color(0.82f, 0.82f, 0.8f));
            y += 30;
            Text(Loc.Get("ui.eleccion.arranca_con_slot"), x, y, 21, new Color(0.82f, 0.82f, 0.8f));
        }
        else
        {
            var ficha = partida.Roster.Find(opcion.DefenderClass);
            int slotsAntes = ficha?.OpenSlots ?? 1;

            Text($"{Loc.Get("ui.eleccion.level")} {opcion.ResultingLevel - 1} → {opcion.ResultingLevel}",
                x, y, 21, new Color(0.82f, 0.82f, 0.8f));

            y += 30;

            // ⚠ Esta línea es la que hace que la elección sea una elección. Sin
            // ella, contratar parece siempre mejor porque trae algo visiblemente
            // nuevo, y levelear parece "solo números".
            int slotsDespues = System.Math.Min(opcion.ResultingLevel, CargadorDatos.Actual.Classes.PerkSlotsPerClass);

            Text(slotsDespues > slotsAntes
                    ? $"{Loc.Get("ui.eleccion.abre_slot")} {slotsDespues} " +
                      $"{Loc.Get("ui.eleccion.de")} {CargadorDatos.Actual.Classes.PerkSlotsPerClass}."
                    : Loc.Get("ui.eleccion.slots_completos"),
                x, y, 21, new Color(0.95f, 0.85f, 0.5f));
        }

        Text($"[{i + 1}]", r.Position.X + r.Size.X - 70, r.Position.Y + r.Size.Y - 28, 24,
            new Color(0.5f, 0.5f, 0.5f));
    }

    /// <summary>
    /// La tarjeta de un perk de Oro ofrecido por un jefe.
    /// </summary>
    /// <remarks>
    /// ⚠ Dice <b>a qué clase le sirve</b> y no solo el nombre. Un Oro es un
    /// comportamiento que solo pueden usar dos o tres de las nueve clases, y
    /// elegir entre tres nombres sin saber quién los puede equipar es elegir a
    /// ciegas — que es exactamente lo que la oferta de tres vino a arreglar.
    /// </remarks>
    private void DibujarTarjetaDePerk(Choice opcion, float x, float y, Color acento)
    {
        var def = CargadorDatos.Actual.Perks.Get(opcion.PerkId);

        Text(Loc.Tier(def.Tier), x, y, 22, acento);

        y += 56;
        Text(Loc.Perk(def), x, y, 40, new Color(0.95f, 0.95f, 0.92f));

        y += 50;
        Text($"{Loc.Effect(def.Effect)} · {def.Magnitude}%", x, y, 19, new Color(0.7f, 0.7f, 0.68f));

        y += 54;

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

        Text(Loc.Get("ui.eleccion.sirve_a"), x, y, 19, new Color(0.6f, 0.6f, 0.58f));
        y += 28;
        Text(sirve.ToString(), x, y, 21, new Color(0.82f, 0.82f, 0.8f));

        y += 36;
        Text(Loc.Get("ui.eleccion.va_a_la_mochila"), x, y, 18, new Color(0.6f, 0.6f, 0.58f));
    }

    /// <summary>
    /// ⚠ Cazador, nunca "arquero". Es el error de nombre más fácil de cometer en
    /// este proyecto porque "arquero" es lo que uno esperaría de un tower defense,
    /// pero en Argentum Online esa clase no existe.
    /// </summary>

    private void Text(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
