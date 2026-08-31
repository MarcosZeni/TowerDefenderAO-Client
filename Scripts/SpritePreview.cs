using System.Collections.Generic;
using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// Un banco de pruebas: se hace aparecer bichos y se los mira caminar la ruta.
/// </summary>
/// <remarks>
/// <para>No es parte del juego: es una escena suelta, como
/// <c>LayoutEditor.tscn</c>, y se abre con <b>F6</b> desde el editor de Godot.
/// No toca ningún archivo — solo lee <c>Data\</c> y <c>Resources\</c>.</para>
///
/// <para>Existe por una pregunta que no se puede contestar leyendo números: el
/// arte de Austral trae al Dragón Rojo en cuadros de 1492 px de ancho, que a la
/// escala del mundo son <b>11,7 celdas</b>, y la única forma de saber si eso está
/// bien o mal es verlo caminando sobre la ruta al lado de una hormiga. Llegar
/// ahí jugando son cien oleadas.</para>
///
/// <para>⚠ <b>El movimiento es el del juego, no una animación de muestra.</b>
/// Paso fijo de 30 Hz y <c>distancia += velocidad</c> con la velocidad del
/// bestiario, que es exactamente la fase 4 del tick (<c>Run.FaseMover</c>). Un
/// Golem cruza lento porque en el juego cruza lento: si acá fueran todos a la
/// misma velocidad, la escena mentiría sobre lo único que se viene a mirar —cómo
/// se ve el bicho moviéndose—.</para>
///
/// <para>⚠ <b>Dibuja con la misma cuenta de escala que <see cref="Game"/>.</b> No
/// tiene una propia: si las dos se separan, esta escena deja de servir para
/// decidir nada. La cuenta es <c>0,75 × 4 / escala del gráfico</c> —tres cuartos
/// porque el tile x4 de AO mide 128 px y la celda 96 (INTERFAZ.md §2)— y el
/// multiplicador de prueba se aplica <b>encima</b>, para poder tantear un tamaño
/// nuevo sin tocar los datos.</para>
/// </remarks>
public partial class SpritePreview : Node2D
{
    /// <summary>Los cuatro sufijos de dirección, en el orden de <see cref="Route.Direction"/>.</summary>
    private static readonly string[] Direcciones = ["norte", "este", "sur", "oeste"];

    /// <summary>Milisegundos por tick. Es el paso fijo de la simulación, 30 Hz.</summary>
    private const int MsPorTick = 33;

    /// <summary>Ancho de la lista de bichos, en píxeles.</summary>
    private const float AnchoLista = 300f;

    /// <summary>Alto de una fila de la lista.</summary>
    private const float AltoFila = 22f;

    /// <summary>
    /// Un bicho vivo en la escena.
    /// </summary>
    /// <remarks>
    /// Es deliberadamente <b>no</b> un <see cref="Enemy"/> de la simulación: acá
    /// no hay HP, ni estados, ni defensores pegándole. Lo único que se comparte
    /// con el tick es la cuenta del avance, y compartir la clase entera invitaría
    /// a que esta escena empezara a simular cosas que no simula.
    /// </remarks>
    private sealed class Vivo
    {
        public required EnemyData Dato { get; init; }
        public required int SpawnId { get; init; }
        public long Distancia { get; set; }
    }

    private BalanceData _datos = null!;
    private readonly List<EnemyData> _bichos = [];
    private readonly List<Vivo> _vivos = [];

    private int _elegido;
    private int _proximoSpawnId = 1;
    private ulong _seed = 1;
    private Map _mapa = null!;

    /// <summary>
    /// El multiplicador de prueba, en porcentaje.
    /// </summary>
    /// <remarks>
    /// Es lo único de esta escena que no sale de los datos: con <c>+</c> y
    /// <c>-</c> se achica el bicho en vivo y se lee cuántas celdas mide, que es
    /// exactamente el número que hay que pasarle después a
    /// <c>--achicar-hoja</c>.
    /// </remarks>
    private int _prueba = 100;

    /// <summary>
    /// Tanteo del ancla, en píxeles de la hoja. Positivo baja el sprite.
    /// </summary>
    /// <remarks>
    /// Se suma al <c>anclaY</c> que ya tenga la entrada, así que lo que muestra el
    /// panel es el total: ese es el número que va a <c>--anclar-grafico</c>.
    /// </remarks>
    private int _ancla;

    private long _tick;
    private bool _pausa;
    private bool _regla = true;

    /// <summary>
    /// Lo que sobró del frame anterior, en segundos.
    /// </summary>
    /// <remarks>
    /// El tick es fijo a 30 Hz y el frame no: sin acumulador, un monitor de 144 Hz
    /// haría caminar a los bichos casi cinco veces más rápido y la escena volvería
    /// a mentir sobre la velocidad.
    /// </remarks>
    private double _resto;

    public override void _Ready()
    {
        // Esta escena se abre sola, sin pasar por Main, así que lo que aquel deja
        // listo hay que dejarlo listo acá: sin la fuente el panel no dibuja nada, y
        // sin los ajustes el idioma queda en el default.
        UiStyle.Init();
        Settings.Load();
        Loc.Load(Settings.Language);

        _datos = DataLoader.Current;

        foreach (var e in _datos.Enemies)
            _bichos.Add(e);

        // Arranca en el más grande, que es el que motivó la escena. Buscarlo por
        // el ancho del sprite y no por el nombre: si mañana el Dragón deja de ser
        // el más gordo, esto sigue abriendo en el que importa.
        int mejor = 0;
        float ancho = -1f;

        for (int i = 0; i < _bichos.Count; i++)
        {
            var g = GraphicsLoader.Data($"{_bichos[i].Graphic}.este");

            if (g is null)
                continue;

            float w = g.Width * EscalaDe(g);

            if (w <= ancho)
                continue;

            ancho = w;
            mejor = i;
        }

        _elegido = mejor;
        _mapa = Layouts.Choose(_seed, _datos);

        GD.Print($"SpritePreview · {_bichos.Count} bichos · layout de la seed {_seed}");
    }

    public override void _Process(double delta)
    {
        if (!_pausa)
        {
            _resto += delta;

            double paso = 1.0 / Ints.TicksPerSecond;

            // Tope de pasos por frame: si la ventana estuvo minimizada, `delta`
            // llega enorme y sin el tope la escena se cuelga poniéndose al día.
            for (int i = 0; i < 10 && _resto >= paso; i++)
            {
                _resto -= paso;
                Tick();
            }

            if (_resto >= paso)
                _resto = 0;
        }

        QueueRedraw();
    }

    /// <summary>Un paso de 1/30 de segundo: mover y sacar a los que llegaron.</summary>
    private void Tick()
    {
        _tick++;

        long largo = _mapa.Route.Length;

        for (int i = _vivos.Count - 1; i >= 0; i--)
        {
            var v = _vivos[i];

            // La misma línea que la fase 4 del tick. Sin perks ni estados: acá no
            // hay quien los aplique.
            v.Distancia += v.Dato.Speed;

            // Llegó al cristal. En el juego acá empezaría a pegarle a la Torre;
            // en la escena se borra, que es lo que se vino a mirar.
            if (v.Distancia >= largo)
                _vivos.RemoveAt(i);
        }
    }

    public override void _UnhandledInput(InputEvent evento)
    {
        if (evento is InputEventMouseButton { Pressed: true } click)
        {
            Clic(click);
            return;
        }

        if (evento is not InputEventKey { Pressed: true } tecla)
            return;

        switch (tecla.Keycode)
        {
            case Key.Down or Key.S:
                _elegido = (_elegido + 1) % _bichos.Count;
                break;

            case Key.Up or Key.W:
                _elegido = (_elegido - 1 + _bichos.Count) % _bichos.Count;
                break;

            case Key.Enter or Key.KpEnter:
                Spawn(_elegido);
                break;

            case Key.Right or Key.D:
                _seed++;
                _mapa = Layouts.Choose(_seed, _datos);
                _vivos.Clear();
                break;

            case Key.Left or Key.A:
                _seed = _seed == 0 ? 0 : _seed - 1;
                _mapa = Layouts.Choose(_seed, _datos);
                _vivos.Clear();
                break;

            case Key.Equal or Key.Plus or Key.KpAdd:
                _prueba = Mathf.Min(400, _prueba + 5);
                break;

            case Key.Minus or Key.KpSubtract:
                _prueba = Mathf.Max(5, _prueba - 5);
                break;

            case Key.Bracketleft:
                _ancla -= 2;
                break;

            case Key.Bracketright:
                _ancla += 2;
                break;

            case Key.Key0 or Key.Kp0:
                _prueba = 100;
                _ancla = 0;
                break;

            case Key.C:
                _vivos.Clear();
                break;

            case Key.Space:
                _pausa = !_pausa;
                break;

            case Key.G:
                _regla = !_regla;
                break;

            case Key.Escape:
                GetTree().Quit();
                break;
        }
    }

    /// <summary>Un clic en la lista elige y hace aparecer.</summary>
    private void Clic(InputEventMouseButton click)
    {
        if (click.ButtonIndex != MouseButton.Left)
            return;

        var p = GetGlobalMousePosition();

        float x0 = Canvas.CanvasWidth - AnchoLista;

        if (p.X < x0)
            return;

        int fila = (int)((p.Y - 8f) / AltoFila);

        if (fila < 0 || fila >= _bichos.Count)
            return;

        _elegido = fila;
        Spawn(fila);
    }

    /// <summary>Pone un bicho en la entrada de la ruta.</summary>
    private void Spawn(int indice)
        => _vivos.Add(new Vivo
        {
            Dato = _bichos[indice],
            SpawnId = _proximoSpawnId++,
            Distancia = 0,
        });

    public override void _Draw()
    {
        const int celda = Canvas.CellSide;

        var ruta = _mapa.Route;

        DrawRect(new Rect2(0, 0, Canvas.CanvasWidth, Canvas.CanvasHeight), Palette.BackgroundMain);

        // El piso horneado del área del bicho elegido, si el arte está. Con él se
        // ve el sprite contra el pasto y no contra un fondo plano, que es lo que
        // hace que un bicho se lea grande o chico.
        var piso = BakedFloor.For(_bichos[_elegido].Area, _seed, _mapa);

        if (piso is not null)
            DrawTexture(piso, Vector2.Zero);
        else
            DibujarRuta(ruta, celda);

        if (_regla)
            DibujarGrilla(celda);

        DibujarCristal(ruta, celda);
        DibujarPatron(celda);

        foreach (var v in _vivos)
            DibujarVivo(v, ruta, celda);

        DibujarLista();
        DibujarPanel();
    }

    /// <summary>La ruta como una banda marrón, igual que <see cref="Game"/> sin piso.</summary>
    private void DibujarRuta(Route ruta, int celda)
    {
        for (int i = 1; i < ruta.VertexCount; i++)
        {
            var a = ruta.Vertex(i - 1);
            var b = ruta.Vertex(i);

            DrawLine(
                new Vector2((a.X + 0.5f) * celda, (a.Y + 0.5f) * celda),
                new Vector2((b.X + 0.5f) * celda, (b.Y + 0.5f) * celda),
                new Color(0.35f, 0.30f, 0.22f),
                celda * 0.8f);
        }
    }

    /// <summary>
    /// La grilla de celdas, con una celda marcada como patrón.
    /// </summary>
    /// <remarks>
    /// Sin la grilla, "el bicho mide cuatro celdas" es una cuenta que hay que
    /// creer. Con la grilla encima se cuenta mirando, que es de lo que se trata
    /// la escena.
    /// </remarks>
    private void DibujarGrilla(int celda)
    {
        var linea = Palette.BorderSoft.Alpha(0.35f);

        for (int x = 0; x <= Canvas.CellsWide; x++)
        {
            DrawLine(new Vector2(x * celda, 0), new Vector2(x * celda, Canvas.CellsHigh * celda),
                linea, 1f);
        }

        for (int y = 0; y <= Canvas.CellsHigh; y++)
        {
            DrawLine(new Vector2(0, y * celda), new Vector2(Canvas.CellsWide * celda, y * celda),
                linea, 1f);
        }
    }

    /// <summary>Dónde está la Torre: es la celda en la que el bicho desaparece.</summary>
    private void DibujarCristal(Route ruta, int celda)
    {
        var fin = ruta.End;

        var centro = new Vector2((fin.X + 0.5f) * celda, (fin.Y + 0.5f) * celda);

        DrawCircle(centro, celda * 0.32f, Palette.Info.Alpha(0.35f));
        DrawArc(centro, celda * 0.32f, 0, Mathf.Tau, 32, Palette.Info, 2f);

        DrawString(UiStyle.Font, centro + new Vector2(-24f, -celda * 0.42f), "cristal",
            HorizontalAlignment.Left, -1, 14, Palette.Info);
    }

    /// <summary>
    /// El bicho más chico del bestiario, quieto y en una celda fija.
    /// </summary>
    /// <remarks>
    /// <para>Es el patrón vivo. La grilla dice cuántas celdas mide el sprite, pero
    /// "cuatro celdas" no dice si eso <b>se ve</b> grande: eso lo dice tenerlo al
    /// lado de la Hormiga, que ocupa una y es lo que el jugador va a ver en la
    /// oleada 1.</para>
    ///
    /// <para>Va sin el multiplicador de prueba: es la referencia, y una referencia
    /// que se achica con lo que se está midiendo no mide nada.</para>
    /// </remarks>
    private void DibujarPatron(int celda)
    {
        if (_datos.Enemies.Count == 0)
            return;

        // El bestiario viene ordenado por HP, así que el 0 es el más flojo — hoy
        // la Hormiga. Se toma por posición y no por nombre para que siga andando
        // cuando el importador cambie el bestiario.
        var patron = _datos.Enemies[0];

        var donde = new Cell(1, Canvas.CellsHigh - 2);

        var centro = new Vector2((donde.X + 0.5f) * celda, (donde.Y + 0.5f) * celda);

        DrawRect(new Rect2(donde.X * celda, donde.Y * celda, celda, celda),
            Palette.Info.Alpha(0.25f));

        int prueba = _prueba;
        _prueba = 100;

        DibujarSprite(patron, "sur", centro, celda, 0);

        _prueba = prueba;

        DrawString(UiStyle.Font, new Vector2(donde.X * celda, (donde.Y * celda) - 6f),
            $"{patron.Name} · 1 celda", HorizontalAlignment.Left, -1, 14, Palette.TextSecondary);
    }

    /// <summary>Un bicho vivo, con su animación y la celda que pisa marcada.</summary>
    private void DibujarVivo(Vivo v, Route ruta, int celda)
    {
        var (fx, fy) = ruta.PosicionFina(v.Distancia);

        var centro = new Vector2(
            (float)fx / Ints.ThousandthsPerCell * celda + (celda / 2f),
            (float)fy / Ints.ThousandthsPerCell * celda + (celda / 2f));

        int direccion = ruta.Direction(v.Distancia);

        DibujarSprite(v.Dato, Direcciones[direccion], centro, celda, v.SpawnId);

        // La huella: qué celda pisa. Es la que la simulación considera su
        // posición, y con un sprite de once celdas de ancho deja de ser obvia.
        var pisa = ruta.CellAt(v.Distancia);

        DrawRect(new Rect2(pisa.X * celda, pisa.Y * celda, celda, celda),
            Palette.Danger.Alpha(0.9f), filled: false, width: 2f);
    }

    /// <summary>
    /// Dibuja un sprite anclado abajo, con la escala del juego por el multiplicador.
    /// </summary>
    /// <returns><c>false</c> si a ese bicho todavía le falta el arte.</returns>
    private bool DibujarSprite(EnemyData bicho, string direccion, Vector2 centro, int celda, int desfase)
    {
        if (bicho.Graphic.Length == 0)
            return false;

        string clave = $"{bicho.Graphic}.{direccion}";
        var g = GraphicsLoader.Data(clave);

        if (g is null)
        {
            DrawCircle(centro, celda * 0.3f, Palette.Danger);
            return false;
        }

        // El cuadro sale del tick y no del reloj de pantalla, igual que en el
        // juego. El desfase por bicho evita que tres copias muevan la pata a la vez.
        long ms = _tick * MsPorTick;
        var textura = GraphicsLoader.Texture(clave, (int)(ms / g.MsPerFrame) + desfase);

        if (textura is null)
            return false;

        float escala = EscalaDe(g) * bicho.VisualScale / 100f * _prueba / 100f;
        var tamano = textura.GetSize() * escala;

        // El ancla de la entrada más el tanteo de la escena, que no toca el disco.
        var esquina = new Vector2(
            centro.X - (tamano.X / 2f),
            centro.Y + (celda / 2f) - tamano.Y + ((g.AnchorY + _ancla) * escala));

        DrawTextureRect(textura, new Rect2(esquina, tamano), tile: false);

        return true;
    }

    /// <summary>
    /// La misma cuenta que <see cref="Game"/>: tres cuartos, corregido por la
    /// escala en la que está guardado el gráfico.
    /// </summary>
    private static float EscalaDe(Graphic g)
        => 0.75f * 4f / (g.Scale > 0 ? g.Scale : 4);

    /// <summary>La lista de los 45 bichos, a la derecha. Un clic hace aparecer.</summary>
    private void DibujarLista()
    {
        float x0 = Canvas.CanvasWidth - AnchoLista;

        DrawRect(new Rect2(x0, 0, AnchoLista, Canvas.CanvasHeight),
            Palette.BackgroundMain.Alpha(0.88f));

        DrawLine(new Vector2(x0, 0), new Vector2(x0, Canvas.CanvasHeight), Palette.BorderNormal, 1f);

        for (int i = 0; i < _bichos.Count; i++)
        {
            float y = 8f + (i * AltoFila);

            if (i == _elegido)
                DrawRect(new Rect2(x0 + 2f, y, AnchoLista - 4f, AltoFila), Palette.Info.Alpha(0.25f));

            var color = GraphicsLoader.Data($"{_bichos[i].Graphic}.sur") is null
                ? Palette.TextDisabled
                : Palette.TextPrimary;

            DrawString(UiStyle.Font, new Vector2(x0 + 10f, y + 16f),
                $"{_bichos[i].Area}  {_bichos[i].Name}",
                HorizontalAlignment.Left, AnchoLista - 20f, 15, color);
        }
    }

    /// <summary>Los números del bicho elegido y las teclas, abajo a la izquierda.</summary>
    private void DibujarPanel()
    {
        var bicho = _bichos[_elegido];

        var g = GraphicsLoader.Data($"{bicho.Graphic}.este")
                ?? GraphicsLoader.Data($"{bicho.Graphic}.sur");

        var lineas = new List<string>
        {
            $"{bicho.Name}   (área {bicho.Area})",
            "",
        };

        if (g is null)
        {
            lineas.Add("sin arte");
        }
        else
        {
            float escala = EscalaDe(g) * bicho.VisualScale / 100f * _prueba / 100f;

            lineas.Add($"en disco    {g.Width}x{g.Height} px  (x{g.Scale}, {g.Frames} cuadros)");
            lineas.Add($"en pantalla {g.Width * escala:0}x{g.Height * escala:0} px");
            lineas.Add($"            {g.Width * escala / Canvas.CellSide:0.0} x " +
                       $"{g.Height * escala / Canvas.CellSide:0.0} celdas");
            lineas.Add($"hoja        {g.FileNum}.PNG");
            lineas.Add($"ancla Y     {g.AnchorY + _ancla} px   ([ / ])");
        }

        // Cuánto tarda en cruzar, con la velocidad del bestiario. Es el otro
        // número que no se puede leer de un JSON.
        float segundos = bicho.Speed > 0
            ? (float)_mapa.Route.Length / bicho.Speed / Ints.TicksPerSecond
            : 0f;

        lineas.Add($"velocidad   {bicho.Speed} milésimas/tick · cruza en {segundos:0.0} s");
        lineas.Add("");
        lineas.Add($"prueba      {_prueba} %   (+ / - / 0)");
        lineas.Add($"layout      seed {_seed}   (← / →)");
        lineas.Add($"vivos       {_vivos.Count}{(_pausa ? "   [PAUSA]" : "")}");
        lineas.Add("");
        lineas.Add("clic o enter: aparecer · ↑↓ elegir · c limpiar");
        lineas.Add("espacio pausa · g grilla · esc salir");

        float ancho = 0f;

        foreach (string s in lineas)
            ancho = Mathf.Max(ancho, UiStyle.Width(s, 15));

        var caja = new Rect2(
            16f, Canvas.CanvasHeight - (lineas.Count * 20f) - 36f, ancho + 24f,
            (lineas.Count * 20f) + 20f);

        DrawRect(caja, Palette.BackgroundMain.Alpha(0.85f));
        DrawRect(caja, Palette.BorderNormal, filled: false, width: 1f);

        float y = caja.Position.Y + 24f;

        foreach (string s in lineas)
        {
            DrawString(UiStyle.Font, new Vector2(caja.Position.X + 12f, y), s,
                HorizontalAlignment.Left, -1, 15, Palette.TextPrimary);

            y += 20f;
        }
    }
}
