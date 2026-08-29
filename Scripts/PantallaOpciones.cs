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
/// <see cref="Configuracion"/> y <see cref="Resolucion"/>.</para>
/// </remarks>
public partial class PantallaOpciones : Control
{
    /// <summary>Se cerró. Lo escucha quien lo abrió, para volver a mostrarse.</summary>
    public event System.Action? Cerrado;

    private bool _abierto;
    private int _bajoElCursor = -1;

    /// <summary>Qué atajo está esperando una tecla. −1 = ninguno.</summary>
    private int _capturando = -1;

    private const int Width = 900;
    private const int X0 = (Escala.LienzoAncho - Width) / 2;
    private const int Y0 = 120;
    private const int AltoFila = 46;

    public bool Abierto => _abierto;

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public void Abrir()
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
        Configuracion.Store();
        Cerrado?.Invoke();
    }

    // -------------------------------------------------------------------------
    //  Geometría: una sola tabla de filas, usada por el dibujo Y por el input.
    // -------------------------------------------------------------------------

    /// <summary>Qué hay en cada renglón, en orden.</summary>
    private enum Fila
    {
        TituloVentana,
        ModoVentana,
        Resolucion,
        TituloSonido,
        VolumenMusica,
        VolumenSonido,
        TituloAnimaciones,
        AnimarArmas,
        AnimarEfectos,
        TituloTeclas,
        // Los 8 atajos entran acá, uno por fila.
        PrimerAtajo,
    }

    private const int CantidadAtajos = 8;
    private static int TotalFilas => (int)Fila.PrimerAtajo + CantidadAtajos + 2;

    /// <summary>La fila de índice <paramref name="i"/>, o los botones del final.</summary>
    private static Rect2 RectFila(int i)
        => new(X0, Y0 + (i * AltoFila), Width, AltoFila - 6);

    /// <summary>Las cajitas que se pueden apretar dentro de una fila de valor.</summary>
    private static Rect2 RectValor(int i, int columna, int deCuantas)
    {
        float ancho = 150;
        float x = X0 + Width - 24 - ((deCuantas - columna) * (ancho + 10));

        return new Rect2(x, Y0 + (i * AltoFila) + 4, ancho, AltoFila - 14);
    }

    private static int IndiceDe(Fila fila) => (int)fila;

    private static int FilaRestaurar => TotalFilas - 2;
    private static int FilaVolver => TotalFilas - 1;

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la cicatriz de
    /// <see cref="PantallaMercado"/>: el hook de GUI dejaba las pantallas sordas.
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
                    Configuracion.Asignar((Atajo)_capturando, captura.Keycode);

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
            if (RectFila(i).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    private void Apretar(Vector2 donde)
    {
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
            Configuracion.RestaurarTeclas();
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
                if (RectValor(i, 0, 2).HasPoint(donde))
                    Resolucion.Completa();
                else if (RectValor(i, 1, 2).HasPoint(donde))
                    Resolucion.EnVentana(new Vector2I(1600, 900));

                break;

            case Fila.Resolucion:
                for (int r = 0; r < Resolucion.Opciones.Length; r++)
                {
                    if (RectResolucion(i, r).HasPoint(donde))
                        Resolucion.EnVentana(Resolucion.Opciones[r]);
                }

                break;

            case Fila.VolumenMusica:
                Configuracion.VolumenMusica = VolumenEn(i, donde, Configuracion.VolumenMusica);
                Configuracion.Store();
                break;

            case Fila.VolumenSonido:
                Configuracion.VolumenSonido = VolumenEn(i, donde, Configuracion.VolumenSonido);
                Configuracion.Store();
                break;

            case Fila.AnimarArmas:
                Configuracion.AnimarArmas = !Configuracion.AnimarArmas;
                Configuracion.Store();
                break;

            case Fila.AnimarEfectos:
                Configuracion.AnimarEfectos = !Configuracion.AnimarEfectos;
                Configuracion.Store();
                break;
        }

        QueueRedraw();
    }

    private static Rect2 RectBarra(int i)
        => new(X0 + Width - 344, Y0 + (i * AltoFila) + 12, 320, AltoFila - 30);

    /// <summary>Dónde cayó el click sobre la barra, en 0-100. Fuera, no cambia.</summary>
    private static int VolumenEn(int i, Vector2 donde, int actual)
    {
        var barra = RectBarra(i);

        if (!barra.HasPoint(donde))
            return actual;

        return Mathf.Clamp((int)((donde.X - barra.Position.X) * 100f / barra.Size.X), 0, 100);
    }

    private static Rect2 RectResolucion(int i, int r)
        => new(X0 + 300 + (r * 96), Y0 + (i * AltoFila) + 4, 90, AltoFila - 14);

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
        var marco = new Rect2(X0 - 24f, Y0 - 78f, Width + 48f, (TotalFilas * AltoFila) + 100f);

        this.Panel(marco, opacidad: 1f);

        this.Label(new Rect2(X0, Y0 - 68f, Width, 48f), Loc.Get("ui.opciones.titulo"), 38,
            Palette.TextPrimary);

        this.Rule(marco, 76f);

        Titulo(IndiceDe(Fila.TituloVentana), Loc.Get("ui.opciones.ventana"));
        ModoVentana(IndiceDe(Fila.ModoVentana));
        Resoluciones(IndiceDe(Fila.Resolucion));

        Titulo(IndiceDe(Fila.TituloSonido), Loc.Get("ui.opciones.sonido"));
        Barra(IndiceDe(Fila.VolumenMusica), Loc.Get("ui.opciones.musica"), Configuracion.VolumenMusica);
        Barra(IndiceDe(Fila.VolumenSonido), Loc.Get("ui.opciones.efectos"), Configuracion.VolumenSonido);

        Titulo(IndiceDe(Fila.TituloAnimaciones), Loc.Get("ui.opciones.animaciones"));
        Interruptor(IndiceDe(Fila.AnimarArmas), Loc.Get("ui.opciones.armas"), Configuracion.AnimarArmas);
        Interruptor(IndiceDe(Fila.AnimarEfectos), Loc.Get("ui.opciones.portal"), Configuracion.AnimarEfectos);

        Titulo(IndiceDe(Fila.TituloTeclas), Loc.Get("ui.opciones.teclas"));

        for (int a = 0; a < CantidadAtajos; a++)
            Atajo(IndiceDe(Fila.PrimerAtajo) + a, (Atajo)a);

        Boton(FilaRestaurar, Loc.Get("ui.opciones.restaurar"));
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

        bool completa = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.ExclusiveFullscreen
            or DisplayServer.WindowMode.Fullscreen;

        Choice(RectValor(i, 0, 2), Loc.Get("ui.opciones.completa"), completa);
        Choice(RectValor(i, 1, 2), Loc.Get("ui.opciones.enventana"), !completa);
    }

    private void Resoluciones(int i)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), Loc.Get("ui.opciones.resolucion"), 18, Palette.TextPrimary, HorizontalAlignment.Left, 20f);

        var actual = DisplayServer.WindowGetSize();

        for (int r = 0; r < Resolucion.Opciones.Length; r++)
        {
            var o = Resolucion.Opciones[r];
            Choice(RectResolucion(i, r), $"{o.X}", o == actual);
        }

        // ⚠ La resolución cambia la NITIDEZ, no el campo visible: el lienzo es
        // siempre 1920x1080 y `keep` no le da más ruta a nadie. Decirlo acá evita
        // que el jugador piense que en 4K ve más mapa (INTERFAZ.md §1).
        float x = X0 + 300 + (Resolucion.Opciones.Length * 96) + 8;

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

    private void Atajo(int i, Atajo atajo)
    {
        Fondo(i, interactiva: true);
        this.Label(RectFila(i), Configuracion.NombreDe(atajo), 18, Palette.TextPrimary, HorizontalAlignment.Left, 20f);

        bool capturando = _capturando == (int)atajo;

        Choice(RectValor(i, 0, 1),
            capturando
                ? Loc.Get("ui.opciones.captura")
                : Configuracion.NombreDeTecla(Configuracion.TeclaDe(atajo)),
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
