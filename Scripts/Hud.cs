using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// El HUD de la corrida. <b>Flota sobre el mundo, sin marco.</b>
/// </summary>
/// <remarks>
/// <para>Austral AO tiene la interfaz <i>alrededor</i> del mundo: la ventana de
/// juego es un recorte chico en medio de un marco de madera. Acá el mundo ocupa
/// los 1920x1080 completos y esto se dibuja encima (INTERFAZ.md §3).</para>
///
/// <para>De ahí sale una regla que hay que respetar al diseñar layouts: <b>las
/// esquinas son zona de HUD</b>, así que ningún mapa pone ruta ni zona de
/// colocación ahí, o el jugador va a estar clickeando abajo de un panel.</para>
///
/// <para>Y otra: los paneles llevan fondo propio. Van a quedar sobre pasto,
/// sobre agua y sobre un dragón; texto blanco a secas no se lee sobre cualquier
/// cosa.</para>
/// </remarks>
public partial class Hud : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;

    public Hud(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        // Sin theme propio todavía: la fuente de respaldo del motor alcanza para
        // que los números se lean. La pasada de tipografía es Fase 7.
        _fuente = ThemeDB.FallbackFont;

        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var p = _juego.Run;
        if (p is null)
            return;

        // --- Arriba a la izquierda: la Torre. Lo más grande de la pantalla,
        //     porque es la condición de derrota.
        Panel(new Rect2(24, 24, 420, 92));

        int porcentaje = p.Tower.MaxHp == 0
            ? 0
            : p.Tower.Hp * 100 / p.Tower.MaxHp;

        Text($"{Loc.Get("ui.hud.torre")}   {p.Tower.Hp} / {p.Tower.MaxHp}", 44, 62, 30, Blanco);

        // Barra de vida: verde, ámbar, rojo. El color dice la urgencia antes de
        // que el jugador llegue a leer el número.
        var colorBarra =
            porcentaje > 50 ? new Color(0.25f, 0.85f, 0.35f) :
            porcentaje > 20 ? new Color(0.95f, 0.72f, 0.15f) :
                              new Color(0.9f, 0.25f, 0.25f);

        DrawRect(new Rect2(44, 78, 380, 16), new Color(0, 0, 0, 0.55f));
        DrawRect(new Rect2(44, 78, 380 * porcentaje / 100f, 16), colorBarra);

        // --- Arriba a la derecha: oleada y cronómetro.
        Panel(new Rect2(Escala.LienzoAncho - 404, 24, 380, 92));

        // El "/ 100" va escrito a propósito: la corrida TERMINA en la 100, y eso
        // hay que comunicarlo desde la oleada 1, no descubrirlo al llegar.
        Text($"{Loc.Get("ui.hud.oleada")}  {p.Wave} / {CargadorDatos.Actual.Curves.RunCurve.WavesPerRun}",
            Escala.LienzoAncho - 384, 62, 28, Blanco);

        // El ranking desempata por ticks: si el cronómetro no se muestra, nadie
        // sabe que está corriendo uno.
        int segundos = p.Tick / Ints.TicksPerSecond;
        Text($"{segundos / 60:00}:{segundos % 60:00}   ·   {p.Tick} {Loc.Get("ui.hud.ticks")}",
            Escala.LienzoAncho - 384, 96, 22, Gris);

        // --- Abajo a la izquierda: el oro DE LA CORRIDA.
        Panel(new Rect2(24, Escala.LienzoAlto - 108, 300, 84));

        // ⚠ Este oro NO es la billetera. La billetera es progresión permanente y
        // vive en la cuenta; esto muere con la corrida. Son dos sistemas
        // distintos y no se pueden confundir en pantalla.
        Text($"{Loc.Get("ui.hud.oro")}   {p.Gold}", 44, Escala.LienzoAlto - 56, 30, new Color(0.98f, 0.85f, 0.35f));

        // --- Abajo a la derecha: estado.
        if (p.State != RunState.InProgress)
        {
            string cartel = p.State == RunState.Won
                ? Loc.Get("ui.hud.ganaste")
                : Loc.Get("ui.hud.cayo_la_torre");
            var color = p.State == RunState.Won
                ? new Color(0.4f, 0.95f, 0.5f)
                : new Color(0.95f, 0.35f, 0.35f);

            Panel(new Rect2(Escala.LienzoAncho / 2 - 260, Escala.LienzoAlto / 2 - 70, 520, 140));
            Text(cartel, Escala.LienzoAncho / 2 - 220, Escala.LienzoAlto / 2 + 10, 52, color);
        }

        BarraDeClases(p);

        if (_arrastrando)
            DibujarFantasma(p);

        Text(Loc.Get("ui.hud.ayuda"),
            24, Escala.LienzoAlto - 150, 17, Gris);
    }

    /// <summary>Geometría de la barra lateral. La usan el dibujo Y el input.</summary>
    private const int FichaAncho = 216;
    private const int FichaAlto = 78;
    private const int FichaHueco = 8;
    private const int FichaY0 = 140;

    private static Rect2 RectDeFicha(int i)
        => new(
            Escala.LienzoAncho - FichaAncho - 16,
            FichaY0 + (i * (FichaAlto + FichaHueco)),
            FichaAncho,
            FichaAlto);

    /// <summary>La ficha bajo esta posición, o −1.</summary>
    private int FichaEn(Vector2 posicion)
    {
        int cuantas = _juego.Run?.Roster.Classes.Count ?? 0;

        for (int i = 0; i < cuantas; i++)
        {
            if (RectDeFicha(i).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    /// <summary>Si arrancó un arrastre desde una ficha y todavía no soltó.</summary>
    private bool _arrastrando;

    /// <summary>
    /// Click y arrastre sobre la barra de clases.
    /// </summary>
    /// <remarks>
    /// <para>Las dos formas de poner que espera un jugador de tower defense:
    /// <b>click en la ficha y click en la celda</b>, o <b>arrastrar de la ficha a
    /// la celda</b>. Apretar sobre la ficha elige la clase en los dos casos; si
    /// el botón se suelta sobre el mapa, además pone.</para>
    ///
    /// <para>El evento se marca atendido para que no siga viaje a
    /// <see cref="Juego"/>: sin eso, el click en una ficha que tapa una celda
    /// colocable pondría un defensor abajo de la barra.</para>
    /// </remarks>
    public override void _UnhandledInput(InputEvent evento)
    {
        var p = _juego.Run;

        // Con un modal abierto la barra no atiende: la elección y la mochila
        // están encima y el click es para ellas.
        if (p is null || p.State != RunState.InProgress || p.WaitingForChoice)
            return;

        if (_juego.Backpack is { Visible: true })
            return;

        if (evento is not InputEventMouseButton { ButtonIndex: MouseButton.Left } boton)
            return;

        if (boton.Pressed)
        {
            int i = FichaEn(boton.Position);

            if (i < 0)
                return;

            _juego.SeleccionarClase(p.Roster.Classes[i].DefenderClass);
            _arrastrando = true;
            GetViewport().SetInputAsHandled();
        }
        else if (_arrastrando)
        {
            _arrastrando = false;

            // Soltó fuera de la barra: eso es un arrastre al mapa. Si soltó sobre
            // otra ficha fue un click normal, y no hay que poner nada.
            if (FichaEn(boton.Position) < 0)
                _juego.PonerEn(boton.Position);

            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// La barra lateral de clases: retrato, nivel, costo y unidades puestas.
    /// </summary>
    /// <remarks>
    /// <para>Muestra el nivel y los slots como <c>2/6</c> porque son dos cosas
    /// distintas que el jugador necesita ver juntas: <b>el nivel abre capacidad y
    /// los drops la llenan</b>, así que un <c>1/3</c> significa "tengo lugar y no
    /// tengo con qué" y un <c>3/3</c> lo contrario.</para>
    ///
    /// <para>Va vertical a la derecha, no centrada abajo: es donde la mano
    /// descansa entre oleadas, deja el centro del campo despejado, y una columna
    /// aguanta las 9 clases del roster completo sin achicarse.</para>
    /// </remarks>
    private void BarraDeClases(Run p)
    {
        var clases = p.Roster.Classes;

        // Vacía también se dibuja: la barra está SIEMPRE, para que el lugar
        // exista antes que el contenido. Si apareciera recién con la primera
        // clase, el jugador no sabría dónde va a vivir su roster.
        if (clases.Count == 0)
        {
            var hueco = RectDeFicha(0);

            DrawRect(hueco, new Color(0.06f, 0.06f, 0.09f, 0.55f));
            DrawRect(hueco, new Color(1, 1, 1, 0.10f), filled: false, width: 1.5f);

            Text(Loc.Get("ui.hud.aca_van_tus_clases"), hueco.Position.X + 16, hueco.Position.Y + 34, 16, Gris);
            Text(Loc.Get("ui.hud.elegi_la_primera"), hueco.Position.X + 16, hueco.Position.Y + 56, 16, Gris);
            return;
        }

        for (int i = 0; i < clases.Count; i++)
        {
            var ficha = clases[i];
            bool elegida = ficha.DefenderClass == _juego.ClaseElegida;

            var r = RectDeFicha(i);
            int costo = CargadorDatos.Actual.StatsOf(ficha.DefenderClass).Cost;
            bool alcanza = p.Gold >= costo;

            DrawRect(r, new Color(0.06f, 0.06f, 0.09f, 0.82f));
            DrawRect(r,
                elegida ? new Color(0.4f, 0.9f, 0.5f, 0.95f) : new Color(1, 1, 1, 0.12f),
                filled: false, width: elegida ? 3f : 1.5f);

            DibujarRetrato(ficha, new Rect2(r.Position.X + 6, r.Position.Y + 6, 54, FichaAlto - 12));

            float xTexto = r.Position.X + 68;

            // La clase que no se puede pagar se ve apagada. Es más rápido de leer
            // que comparar el precio con el oro de la otra punta de la pantalla.
            Text(Loc.Class(ficha.DefenderClass), xTexto, r.Position.Y + 26, 19,
                alcanza ? Blanco : new Color(0.5f, 0.45f, 0.45f));

            Text($"{Loc.Get("ui.hud.nv")} {ficha.Level} · {ficha.Perks.Count}/{ficha.OpenSlots} {Loc.Get("ui.hud.perks")}",
                xTexto, r.Position.Y + 46, 14, Gris);

            // Cuántas unidades de esta clase hay puestas en el tablero.
            int puestas = 0;
            foreach (var d in p.Defenders)
                if (d.DefenderClass == ficha.DefenderClass)
                    puestas++;

            Text($"{puestas} {Loc.Get("ui.hud.en_el_mapa")}", xTexto, r.Position.Y + 66, 14,
                puestas > 0 ? new Color(0.55f, 0.8f, 0.6f) : Gris);

            Text($"{costo}", r.Position.X + FichaAncho - 52, r.Position.Y + 26, 18,
                alcanza ? new Color(0.98f, 0.85f, 0.35f) : new Color(0.6f, 0.4f, 0.4f));
        }

        // La mochila: cuando está llena, cada drop obliga a destruir algo. Que se
        // vea el 15/15 antes de que pase es lo que convierte eso en una decisión
        // y no en una sorpresa.
        bool llena = p.Backpack.Ocupados >= CargadorDatos.Actual.Curves.Defenders.BackpackSlots;

        Text($"{Loc.Get("ui.hud.mochila")} {p.Backpack.Ocupados}/{CargadorDatos.Actual.Curves.Defenders.BackpackSlots}",
            Escala.LienzoAncho - 220, Escala.LienzoAlto - 40, 19,
            llena ? new Color(0.95f, 0.6f, 0.3f) : Gris);
    }

    /// <summary>
    /// El muñeco de la clase, quieto y mirando al sur, adentro de su ficha.
    /// </summary>
    /// <remarks>
    /// Cuerpo y cabeza con el mismo anclaje que usa el mundo —los dos apoyados
    /// abajo, la cabeza corrida por su offset— pero a la escala de la ficha. El
    /// arma no va: a este tamaño es ruido. Si falta el arte, la ficha queda solo
    /// con texto, igual que el mundo cae a la primitiva.
    /// </remarks>
    private void DibujarRetrato(RosterClass ficha, Rect2 marco)
    {
        var cuerpo = TexturaDeCuerpo(ficha, out var apariencia);

        if (cuerpo is null || apariencia is null)
            return;

        // La escala la fija el alto del marco; el muñeco se apoya en su base.
        float s = marco.Size.Y / cuerpo.GetSize().Y;

        DibujarMuneco(cuerpo, apariencia, s,
            new Vector2(marco.Position.X + (marco.Size.X / 2f), marco.End.Y),
            new Color(1, 1, 1, 1));
    }

    /// <summary>
    /// El fantasma que acompaña el arrastre: el muñeco pegado al cursor.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que hace que arrastrar se sienta como <b>llevar</b> la unidad
    /// y no como mover un puntero. Se imanta al centro de la celda de abajo —el
    /// mismo redondeo que va a usar <see cref="Juego.PonerEn"/> al soltar, así lo
    /// que se ve es lo que va a pasar— y se tiñe de rojo cuando en esa celda no
    /// se puede: ruta, ocupada, bloqueada o sin oro.</para>
    ///
    /// <para>Mientras el cursor sigue sobre la barra todavía no hay celda que
    /// mostrar, y el muñeco flota en el cursor sin imantarse.</para>
    /// </remarks>
    private void DibujarFantasma(Run p)
    {
        var ficha = p.Roster.Find(_juego.ClaseElegida);

        if (ficha is null)
            return;

        var cuerpo = TexturaDeCuerpo(ficha, out var apariencia);

        if (cuerpo is null || apariencia is null)
            return;

        var mouse = GetLocalMousePosition();

        // Mismo 0,5 con el que el mundo dibuja las hojas x4: el fantasma mide lo
        // que va a medir el defensor puesto.
        const float s = 0.5f;

        var tinte = new Color(1f, 1f, 1f, 0.72f);
        var pie = mouse + new Vector2(0, Escala.LadoCelda / 2f);

        if (FichaEn(mouse) < 0)
        {
            var celda = new Cell(
                (int)(mouse.X / Escala.LadoCelda),
                (int)(mouse.Y / Escala.LadoCelda));

            if (p.PuedePoner(_juego.ClaseElegida, celda))
            {
                var (cx, cy) = Escala.CentroDe(celda.X, celda.Y);
                pie = new Vector2(cx, cy + (Escala.LadoCelda / 2f));
            }
            else
            {
                tinte = new Color(1f, 0.42f, 0.38f, 0.62f);
            }
        }

        DibujarMuneco(cuerpo, apariencia, s, pie, tinte);
    }

    /// <summary>El cuerpo al sur de una clase, con su apariencia. Compartido.</summary>
    private static Texture2D? TexturaDeCuerpo(RosterClass ficha, out Apariencia? apariencia)
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
    private void DibujarMuneco(
        Texture2D cuerpo, Apariencia apariencia, float s, Vector2 pie, Color tinte)
    {
        var tamanoCuerpo = cuerpo.GetSize() * s;

        DrawTextureRect(cuerpo,
            new Rect2(pie.X - (tamanoCuerpo.X / 2f), pie.Y - tamanoCuerpo.Y,
                tamanoCuerpo.X, tamanoCuerpo.Y),
            tile: false, tinte);

        var cabeza = CargadorGraficos.Textura($"{apariencia.Head}.sur");

        if (cabeza is null)
            return;

        var tamanoCabeza = cabeza.GetSize() * s;

        DrawTextureRect(cabeza,
            new Rect2(
                pie.X - (tamanoCabeza.X / 2f) + (apariencia.HeadOffset.X * s),
                pie.Y - tamanoCabeza.Y + (apariencia.HeadOffset.Y * s),
                tamanoCabeza.X, tamanoCabeza.Y),
            tile: false, tinte);
    }

    private static readonly Color Blanco = new(0.94f, 0.94f, 0.92f);
    private static readonly Color Gris = new(0.68f, 0.68f, 0.66f);

    /// <summary>
    /// El fondo de un panel. Semitransparente y no negro puro: el HUD va sobre
    /// el arte, y un rectángulo opaco recorta un pedazo del mapa.
    /// </summary>
    private void Panel(Rect2 r)
    {
        DrawRect(r, new Color(0.06f, 0.06f, 0.09f, 0.72f));
        DrawRect(r, new Color(1f, 1f, 1f, 0.10f), filled: false, width: 2f);
    }

    private void Text(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
