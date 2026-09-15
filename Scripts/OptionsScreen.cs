using Godot;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// El panel de opciones: ventana, sonido, animaciones y teclas.
/// </summary>
/// <remarks>
/// <para>Dibujado a mano y no con nodos de GUI, como el resto de las pantallas
/// del juego: es un panel que vive adentro de otro overlay, y mezclar
/// <c>Control</c> anidados con dibujo directo traía el mismo problema de picking
/// que dejó sordas a la elección y a la mochila.</para>
///
/// <para>Se abre desde el menú de ESC durante la corrida y desde el menú
/// principal. Por eso <b>no sabe nada de la partida</b>: solo toca
/// <see cref="Settings"/> y <see cref="Resolution"/>.</para>
/// </remarks>
public partial class OptionsScreen : Control
{
    /// <summary>Se cerró. Lo escucha quien lo abrió, para volver a mostrarse.</summary>
    public event System.Action? Closed;

    private bool _abierto;
    private int _bajoElCursor = -1;

    /// <summary>Qué atajo está esperando una tecla. −1 = ninguno.</summary>
    private int _capturando = -1;

    private const int Width = 900;
    private const int X0 = (Canvas.CanvasWidth - Width) / 2;
    private const int Y0 = 120;
    /// <summary>
    /// El alto de cada renglón.
    /// </summary>
    /// <remarks>
    /// ⚠ Era 46, y con 22 renglones «Volver» caía en y=1086: debajo del lienzo de
    /// 1080, imposible de clickear. En la PC se salía con ESC y no se notaba; en
    /// el celular no hay ESC y Opciones no se cerraba. Con 42 la lista entera
    /// entraba; con el tamaño de los carteles son 23 y el marco se salía por
    /// abajo, así que bajó a 40. En el dedo son 70: ahí la lista es la mitad
    /// —no hay ventana ni teclas— y un renglón de 42 px es chico para tocar.
    /// </remarks>
    private static int AltoFila => Device.IsTouch ? 70 : 40;

    public bool IsOpen => _abierto;

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public void Open()
    {
        _abierto = true;
        _capturando = -1;
        Visible = true;
        QueueRedraw();
    }

    private void Cerrar()
    {
        _abierto = false;
        _capturando = -1;
        Visible = false;
        Settings.Store();
        Closed?.Invoke();
    }

    // -------------------------------------------------------------------------
    //  Geometría: una sola tabla de filas, usada por el dibujo Y por el input.
    // -------------------------------------------------------------------------

    /// <summary>Qué hay en cada renglón, en orden.</summary>
    private enum Fila
    {
        TituloVentana,
        ModoVentana,
        Resolution,
        TituloSonido,
        MusicVolume,
        SoundVolume,
        TituloAnimaciones,
        AnimateWeapons,
        AnimateEffects,

        // ⚠ Sección propia y no un renglón más de animaciones: lo que apaga no
        // es un adorno, es la mitad explicativa de todos los carteles del juego.
        TituloAyudas,
        ShowHelp,
        HelpScale,
        TituloTeclas,
        // Los 8 atajos entran acá, uno por fila.
        PrimerAtajo,
    }

    private const int CantidadAtajos = 8;
    private static int TotalFilas => (int)Fila.PrimerAtajo + CantidadAtajos + 2;

    /// <summary>
    /// Si la fila existe en este aparato.
    /// </summary>
    /// <remarks>
    /// En el dedo no existen la sección de ventana —el sistema no deja cambiar
    /// modo ni tamaño— ni la de teclas con su «restaurar»: remapear espera una
    /// tecla, y sin teclado el renglón quedaba capturando para siempre.
    /// </remarks>
    private static bool Existe(int i)
    {
        if (!Device.IsTouch)
            return true;

        if (i <= (int)Fila.Resolution)
            return false;

        return i < (int)Fila.TituloTeclas || i == FilaVolver;
    }

    /// <summary>En qué renglón de la pantalla cae la fila: las que no existen no ocupan lugar.</summary>
    private static int Renglon(int i)
    {
        int renglon = 0;

        for (int k = 0; k < i; k++)
        {
            if (Existe(k))
                renglon++;
        }

        return renglon;
    }

    private static int FilasVisibles => Renglon(TotalFilas);

    /// <summary>La fila de índice <paramref name="i"/>, o los botones del final.</summary>
    private static Rect2 RectFila(int i)
        => new(X0, Y0 + (Renglon(i) * AltoFila), Width, AltoFila - 6);

    /// <summary>Las cajitas que se pueden apretar dentro de una fila de valor.</summary>
    private static Rect2 RectValor(int i, int columna, int deCuantas)
    {
        float ancho = 150;
        float x = X0 + Width - 24 - ((deCuantas - columna) * (ancho + 10));

        return new Rect2(x, Y0 + (Renglon(i) * AltoFila) + 4, ancho, AltoFila - 14);
    }

    private static int IndiceDe(Fila fila) => (int)fila;

    private static int FilaRestaurar => TotalFilas - 2;
    private static int FilaVolver => TotalFilas - 1;

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la cicatriz de
    /// <see cref="MarketScreen"/>: el hook de GUI dejaba las pantallas sordas.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        if (!_abierto)
            return;

        // Capturando una tecla: la PRÓXIMA que se apriete es la asignación.
        if (_capturando >= 0)
        {
            if (evento is InputEventKey { Pressed: true, Echo: false } captura)
            {
                if (captura.Keycode == Key.Escape)
                    _capturando = -1;
                else
                    Settings.Assign((Shortcut)_capturando, captura.Keycode);

                _capturando = -1;
                QueueRedraw();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (evento is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Cerrar();
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = FilaEn(GetGlobalMousePosition());
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Apretar(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
                break;

            // Abierto, se come el teclado: que un espacio no despause atrás.
            case InputEventKey { Pressed: true }:
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private static int FilaEn(Vector2 posicion)
    {
        for (int i = 0; i < TotalFilas; i++)
        {
            if (Existe(i) && RectFila(i).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    /// <summary>El marco alrededor de la lista. Lo usan el dibujo y la cruz.</summary>
    private static Rect2 Marco()
        => new(X0 - 24f, Y0 - 78f, Width + 48f, (FilasVisibles * AltoFila) + 100f);

    private void Apretar(Vector2 donde)
    {
        // La cruz, igual que «Volver»: está arriba, donde se mira primero.
        if (UiStyle.CloseRectFor(Marco()).HasPoint(donde))
        {
            Cerrar();
            return;
        }

        int i = FilaEn(donde);

        if (i < 0)
            return;

        if (i == FilaVolver)
        {
            Cerrar();
            return;
        }

        if (i == FilaRestaurar)
        {
            Settings.ResetKeys();
            QueueRedraw();
            return;
        }

        // Un atajo: se entra en modo captura y la próxima tecla queda asignada.
        int primerAtajo = IndiceDe(Fila.PrimerAtajo);

        if (i >= primerAtajo && i < primerAtajo + CantidadAtajos)
        {
            _capturando = i - primerAtajo;
            QueueRedraw();
            return;
        }

        var fila = (Fila)i;

        switch (fila)
        {
            case Fila.ModoVentana:
                if (RectValor(i, 0, 3).HasPoint(donde))
                    Resolution.Complete();
                else if (RectValor(i, 1, 3).HasPoint(donde))
                    Resolution.Borderless(Resolution.WindowSize);
                else if (RectValor(i, 2, 3).HasPoint(donde))
                    Resolution.InWindow(Resolution.WindowSize);

                break;

            case Fila.Resolution:
                for (int r = 0; r < Resolution.Options.Length; r++)
                {
                    if (RectResolucion(i, r).HasPoint(donde))
                        Resolution.Resize(Resolution.Options[r]);
                }

                break;

            case Fila.MusicVolume:
                Settings.MusicVolume = VolumenEn(i, donde, Settings.MusicVolume);
                Settings.Store();
                break;

            case Fila.SoundVolume:
                Settings.SoundVolume = VolumenEn(i, donde, Settings.SoundVolume);
                Settings.Store();
                break;

            case Fila.AnimateWeapons:
                Settings.AnimateWeapons = !Settings.AnimateWeapons;
                Settings.Store();
                break;

            case Fila.AnimateEffects:
                Settings.AnimateEffects = !Settings.AnimateEffects;
                Settings.Store();
                break;

            case Fila.ShowHelp:
                Settings.ShowHelp = !Settings.ShowHelp;
                Settings.Store();
                break;

            case Fila.HelpScale:
                for (int e = 0; e < Settings.HelpScaleOptions.Length; e++)
                {
                    if (RectValor(i, e, Settings.HelpScaleOptions.Length).HasPoint(donde))
                        Settings.HelpScale = Settings.HelpScaleOptions[e];
                }

                Settings.Store();
                break;
        }

        QueueRedraw();
    }

    private static Rect2 RectBarra(int i)
        => new(X0 + Width - 344, Y0 + (Renglon(i) * AltoFila) + 12, 320, AltoFila - 30);

    /// <summary>Dónde cayó el click sobre la barra, en 0-100. Fuera, no cambia.</summary>
    private static int VolumenEn(int i, Vector2 donde, int actual)
    {
        var barra = RectBarra(i);

        if (!barra.HasPoint(donde))
            return actual;

        return Mathf.Clamp((int)((donde.X - barra.Position.X) * 100f / barra.Size.X), 0, 100);
    }

    private static Rect2 RectResolucion(int i, int r)
        => new(X0 + 300 + (r * 96), Y0 + (Renglon(i) * AltoFila) + 4, 90, AltoFila - 14);

    // -------------------------------------------------------------------------
    //  Dibujo
    // -------------------------------------------------------------------------

    public override void _Draw()
    {
        if (!_abierto)
            return;

        this.Scrim();

        // El marco alrededor de la lista entera: sin él las filas flotan sobre el
        // velo y la pantalla es la única del juego sin panel.
        var marco = Marco();

        this.Panel(marco, opacidad: 1f);
        this.CloseButton(UiStyle.CloseRectFor(marco));

        this.Label(new Rect2(X0, Y0 - 68f, Width, 48f), Loc.Get("ui.opciones.titulo"), 38,
            Palette.TextPrimary);

        this.Rule(marco, 76f);

        if (Existe(IndiceDe(Fila.ModoVentana)))
        {
            Titulo(IndiceDe(Fila.TituloVentana), Loc.Get("ui.opciones.ventana"));
            ModoVentana(IndiceDe(Fila.ModoVentana));
            Resoluciones(IndiceDe(Fila.Resolution));
        }

        Titulo(IndiceDe(Fila.TituloSonido), Loc.Get("ui.opciones.sonido"));
        Barra(IndiceDe(Fila.MusicVolume), Loc.Get("ui.opciones.musica"), Settings.MusicVolume);
        Barra(IndiceDe(Fila.SoundVolume), Loc.Get("ui.opciones.efectos"), Settings.SoundVolume);

        Titulo(IndiceDe(Fila.TituloAnimaciones), Loc.Get("ui.opciones.animaciones"));
        Interruptor(IndiceDe(Fila.AnimateWeapons), Loc.Get("ui.opciones.armas"), Settings.AnimateWeapons);
        Interruptor(IndiceDe(Fila.AnimateEffects), Loc.Get("ui.opciones.portal"), Settings.AnimateEffects);

        Titulo(IndiceDe(Fila.TituloAyudas), Loc.Get("ui.opciones.ayudas"));
        Interruptor(IndiceDe(Fila.ShowHelp), Loc.Get("ui.opciones.explicaciones"), Settings.ShowHelp);

        // ⚠ Al lado del interruptor y no en un cartel: es la única fila de la
        // pantalla cuyo efecto no se ve al apretarla —lo que apaga vive en las
        // otras pantallas—, así que decir qué hace tiene que ser gratis.
        this.Label(
            new Rect2(X0 + 300f, RectFila(IndiceDe(Fila.ShowHelp)).Position.Y,
                Width - 480f, AltoFila - 6f),
            Loc.Get("ui.opciones.explicaciones_detalle"), 13, Palette.TextDisabled);

        TamanoCarteles(IndiceDe(Fila.HelpScale));

        if (Existe(IndiceDe(Fila.TituloTeclas)))
        {
            Titulo(IndiceDe(Fila.TituloTeclas), Loc.Get("ui.opciones.teclas"));

            for (int a = 0; a < CantidadAtajos; a++)
                Shortcut(IndiceDe(Fila.PrimerAtajo) + a, (Shortcut)a);

            Boton(FilaRestaurar, Loc.Get("ui.opciones.restaurar"));
        }

        Boton(FilaVolver, Loc.Get("ui.opciones.volver"));
    }

    private void Titulo(int i, string texto)
        => this.Label(RectFila(i), texto.ToUpperInvariant(), 17, Palette.Info);

    private void Fondo(int i, bool interactiva)
    {
        var r = RectFila(i);
        bool resaltada = interactiva && i == _bajoElCursor;

        DrawRect(r, resaltada ? Palette.PanelElevated : Palette.BackgroundAlt.Alpha(0.7f));

        if (resaltada)
            DrawRect(r, Palette.BorderLight, filled: false, width: 2f);
    }

    private void ModoVentana(int i)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), Loc.Get("ui.opciones.modo"), 18, Palette.TextPrimary, HorizontalAlignment.Left, 20f);

        // ⚠ Del estado y no de DisplayServer: el cambio real se aplica un frame
        // después, y leerlo del sistema acá marcaba la cajita vieja.
        var modo = Resolution.Style;

        Choice(RectValor(i, 0, 3), Loc.Get("ui.opciones.completa"), modo == Resolution.WindowStyle.Fullscreen);
        Choice(RectValor(i, 1, 3), Loc.Get("ui.opciones.sinbordes"), modo == Resolution.WindowStyle.Borderless);
        Choice(RectValor(i, 2, 3), Loc.Get("ui.opciones.enventana"), modo == Resolution.WindowStyle.Windowed);
    }

    private void Resoluciones(int i)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), Loc.Get("ui.opciones.resolucion"), 18, Palette.TextPrimary, HorizontalAlignment.Left, 20f);

        // En pantalla completa ninguna queda marcada: la resolución es la del
        // monitor. Apretar una pasa a ventana con ese tamaño.
        var actual = Resolution.IsFullscreen ? Vector2I.Zero : Resolution.WindowSize;

        for (int r = 0; r < Resolution.Options.Length; r++)
        {
            var o = Resolution.Options[r];
            Choice(RectResolucion(i, r), $"{o.X}", o == actual);
        }

        // ⚠ La resolución cambia la NITIDEZ, no el campo visible: el lienzo es
        // siempre 1920x1080 y `keep` no le da más ruta a nadie. Decirlo acá evita
        // que el jugador piense que en 4K ve más mapa (INTERFAZ.md §1).
        float x = X0 + 300 + (Resolution.Options.Length * 96) + 8;

        this.Label(new Rect2(x, RectFila(i).Position.Y, X0 + Width - x - 20f, AltoFila - 6f),
            Loc.Get("ui.opciones.nitidez"), 13, Palette.TextDisabled);
    }

    private void Barra(int i, string nombre, int valor)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), nombre, 18, Palette.TextPrimary, HorizontalAlignment.Left, 20f);

        var barra = RectBarra(i);

        this.Bar(barra, valor / 100f, valor > 0 ? Palette.Info : Palette.TextDisabled,
            valor == 0 ? Loc.Get("ui.opciones.mudo") : $"{valor}");
    }

    private void Interruptor(int i, string nombre, bool activo)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), nombre, 18, Palette.TextPrimary, HorizontalAlignment.Left, 20f);

        Choice(RectValor(i, 0, 1), Loc.Get(activo ? "ui.opciones.si" : "ui.opciones.no"), activo);
    }

    /// <summary>Los cuatro tamaños de cartel, uno al lado del otro.</summary>
    /// <remarks>
    /// El nombre de la fila va corto a propósito: cuatro cajitas de 150 ocupan
    /// casi todo el renglón y un rótulo largo se metería debajo de la primera.
    /// </remarks>
    private void TamanoCarteles(int i)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), Loc.Get("ui.opciones.tamano_carteles"), 18, Palette.TextPrimary,
            HorizontalAlignment.Left, 20f);

        int cuantas = Settings.HelpScaleOptions.Length;

        for (int e = 0; e < cuantas; e++)
        {
            int valor = Settings.HelpScaleOptions[e];
            Choice(RectValor(i, e, cuantas), $"{valor} %", valor == Settings.HelpScale);
        }
    }

    private void Shortcut(int i, Shortcut atajo)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), Settings.NameOf(atajo), 18, Palette.TextPrimary, HorizontalAlignment.Left, 20f);

        bool capturando = _capturando == (int)atajo;

        Choice(RectValor(i, 0, 1),
            capturando
                ? Loc.Get("ui.opciones.captura")
                : Settings.KeyName(Settings.KeyFor(atajo)),
            capturando);
    }

    private void Boton(int i, string texto)
        => this.Button(RectFila(i).Inset(2f), texto, Palette.BorderNormal,
            relleno: false, encima: i == _bajoElCursor);

    /// <summary>Una de las cajitas de valor de una fila. La elegida se rellena.</summary>
    private void Choice(Rect2 r, string texto, bool elegida)
        => this.Button(r, texto, elegida ? Palette.Success : Palette.BorderNormal,
            relleno: elegida);
}
