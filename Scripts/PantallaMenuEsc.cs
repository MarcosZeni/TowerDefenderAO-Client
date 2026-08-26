using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// El menú de ESC durante la corrida: seguir, guardar y salir.
/// </summary>
/// <remarks>
/// <para>Existía un agujero real: <b>no había ninguna forma de cerrar el juego</b>
/// desde adentro de una corrida — el usuario terminó en Alt+F4. Este menú es la
/// salida ordenada: guarda y recién después se va.</para>
///
/// <para>Mientras está abierto <b>el reloj se congela</b>
/// (<see cref="Juego.MenuAbierto"/>), igual que la pausa de espacio: se mira pero
/// no se actúa. No regala tiempo de pensar —el mundo queda tapado por el velo— y
/// deja atender la puerta sin perder la oleada.</para>
///
/// <para>Acá van a vivir las <b>opciones</b> —modo de ventana, sonido,
/// animaciones, teclas—. Los renglones ya están listados como apagados para que
/// el lugar exista antes que el contenido, igual que la barra de clases vacía.</para>
/// </remarks>
public partial class PantallaMenuEsc : Control
{
    private readonly Juego _juego;
    private Font _fuente = null!;

    /// <summary>Guardó y quiere volver al menú principal. Lo escucha <c>Principal</c>.</summary>
    public event System.Action? SalirAlMenu;

    private bool _abierto;
    private int _bajoElCursor = -1;

    private const int Ancho = 520;
    private const int AltoBoton = 74;

    public PantallaMenuEsc(Juego juego)
    {
        _juego = juego;
    }

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_abierto)
            QueueRedraw();
    }

    /// <summary>
    /// Los tres botones, de arriba para abajo. El orden es por frecuencia de uso:
    /// lo que más se aprieta, más cerca de donde ya está el mouse.
    /// </summary>
    private static readonly (string Titulo, string Detalle)[] _botones =
    [
        ("SEGUIR JUGANDO", "o ESC de nuevo"),
        ("GUARDAR Y SALIR AL MENÚ", "la corrida queda para continuar"),
        ("GUARDAR Y CERRAR EL JUEGO", "cierra la ventana"),
    ];

    private static Rect2 RectBoton(int i)
        => new(
            (Escala.LienzoAncho - Ancho) / 2f,
            360 + (i * (AltoBoton + 18)),
            Ancho,
            AltoBoton);

    /// <summary>
    /// <c>_Input</c> y no <c>_GuiInput</c> — misma cicatriz que
    /// <see cref="PantallaEleccion"/>: el hook de GUI dejaba las pantallas sordas.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        var p = _juego.Partida;

        if (p is null || p.Estado != EstadoPartida.EnCurso)
            return;

        if (evento is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            // Con la mochila abierta ESC es de ella: la cierra. El menú atiende
            // el ESC siguiente. La elección no cede nunca — es modal de verdad.
            if (!_abierto && (_juego.Mochila is { Abierta: true } || p.EsperandoEleccion))
                return;

            Alternar(!_abierto);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_abierto)
            return;

        switch (evento)
        {
            case InputEventMouseMotion:
                _bajoElCursor = BotonEn(GetGlobalMousePosition());
                QueueRedraw();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Apretar(BotonEn(GetGlobalMousePosition()));
                GetViewport().SetInputAsHandled();
                break;

            // Abierto, el menú se come el teclado entero: que un espacio no
            // despause el juego atrás del velo.
            case InputEventKey { Pressed: true }:
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void Alternar(bool abierto)
    {
        _abierto = abierto;
        Visible = abierto;
        _juego.MenuAbierto = abierto;
        _bajoElCursor = -1;
        QueueRedraw();
    }

    private static int BotonEn(Vector2 posicion)
    {
        for (int i = 0; i < _botones.Length; i++)
        {
            if (RectBoton(i).HasPoint(posicion))
                return i;
        }

        return -1;
    }

    private void Apretar(int i)
    {
        switch (i)
        {
            case 0:
                Alternar(false);
                break;

            case 1:
                _juego.GuardarCorrida();
                Alternar(false);
                SalirAlMenu?.Invoke();
                break;

            case 2:
                _juego.GuardarCorrida();
                GetTree().Quit();
                break;
        }
    }

    public override void _Draw()
    {
        if (!_abierto)
            return;

        DrawRect(new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto), new Color(0, 0, 0, 0.72f));

        Texto("PAUSA", Escala.LienzoAncho / 2f - 86, 280, 52, new Color(0.95f, 0.95f, 0.92f));

        Texto("el reloj está detenido · la corrida se guarda sola al final de cada oleada",
            Escala.LienzoAncho / 2f - 320, 318, 18, new Color(0.6f, 0.6f, 0.58f));

        for (int i = 0; i < _botones.Length; i++)
        {
            var r = RectBoton(i);
            bool resaltado = i == _bajoElCursor;

            DrawRect(r, new Color(0.09f, 0.09f, 0.13f, 0.97f));
            DrawRect(r,
                resaltado ? new Color(0.45f, 0.85f, 0.55f) : new Color(1, 1, 1, 0.15f),
                filled: false, width: resaltado ? 3f : 1.5f);

            Texto(_botones[i].Titulo, r.Position.X + 28, r.Position.Y + 34, 23,
                new Color(0.92f, 0.92f, 0.9f));
            Texto(_botones[i].Detalle, r.Position.X + 28, r.Position.Y + 58, 15,
                new Color(0.58f, 0.58f, 0.56f));
        }

        // Lo que va a vivir acá y todavía no existe. Listado apagado a propósito:
        // el lugar primero, el contenido después.
        Texto("pronto: ventana · sonido · animaciones · teclas",
            Escala.LienzoAncho / 2f - 220, RectBoton(_botones.Length - 1).End.Y + 56, 17,
            new Color(0.45f, 0.45f, 0.43f));
    }

    private void Texto(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
