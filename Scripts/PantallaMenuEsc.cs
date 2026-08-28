using Godot;
using TowerOfArgentum.Ui;
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

    /// <summary>Guardó y quiere volver al menú principal. Lo escucha <c>Principal</c>.</summary>
    public event System.Action? SalirAlMenu;

    private bool _abierto;
    private int _bajoElCursor = -1;

    /// <summary>El panel de opciones, que este menú abre y esconde.</summary>
    private readonly PantallaOpciones _opciones;

    private const int Width = 520;
    private const int AltoBoton = 74;

    public PantallaMenuEsc(Juego juego, PantallaOpciones opciones)
    {
        _juego = juego;
        _opciones = opciones;

        // Al cerrarse opciones, el menú vuelve a la vista: el jugador entró por
        // acá y tiene que salir por acá.
        opciones.Cerrado += () =>
        {
            if (_abierto)
                Visible = true;
        };
    }

    public override void _Ready()
    {
        UiStyle.Init();
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
    /// Los botones, de arriba para abajo. El orden es por frecuencia de uso:
    /// lo que más se aprieta, más cerca de donde ya está el mouse.
    /// </summary>
    /// <remarks>
    /// Lo que se guarda acá son las <b>claves</b>, no los textos: el idioma se
    /// puede cambiar desde opciones y esta tabla es estática: si guardara el
    /// texto ya traducido quedaría congelado en el idioma del arranque.
    /// </remarks>
    private static readonly (string ClaveTitulo, string ClaveDetalle)[] _botones =
    [
        ("ui.pausa.seguir", "ui.pausa.seguir.detalle"),
        ("ui.pausa.opciones", "ui.pausa.opciones.detalle"),
        ("ui.pausa.salir", "ui.pausa.salir.detalle"),
        ("ui.pausa.cerrar", "ui.pausa.cerrar.detalle"),
    ];

    private static Rect2 RectBoton(int i)
        => new(
            (Escala.LienzoAncho - Width) / 2f,
            360 + (i * (AltoBoton + 18)),
            Width,
            AltoBoton);

    /// <summary>
    /// <c>_Input</c> y no <c>_GuiInput</c> — misma cicatriz que
    /// <see cref="PantallaEleccion"/>: el hook de GUI dejaba las pantallas sordas.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        var p = _juego.Run;

        if (p is null || p.State != RunState.InProgress)
            return;

        if (evento is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            // Con la mochila abierta ESC es de ella: la cierra. El menú atiende
            // el ESC siguiente. La elección no cede nunca — es modal de verdad.
            if (!_abierto && (_juego.Backpack is { Abierta: true } || p.WaitingForChoice))
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

    /// <summary>Si está a la vista. Lo mira el HUD para no dibujar encima.</summary>
    public bool Abierto => _abierto;

    /// <summary>
    /// Lo abre desde afuera: el botón MENÚ de la barra de arriba.
    /// </summary>
    /// <remarks>
    /// El botón entra por el mismo <see cref="Alternar"/> que el ESC, así que
    /// congela el reloj igual. Abrirlo poniendo <c>Visible</c> a mano dejaría la
    /// corrida corriendo atrás del velo.
    /// </remarks>
    public void Abrir() => Alternar(true);

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
                // El menú se esconde pero NO se cierra: el reloj sigue congelado
                // mientras se toquetean las opciones, y al volver de opciones
                // reaparece. Sin esto, abrir opciones despausaría la corrida.
                Visible = false;
                _opciones.Abrir();
                break;

            case 2:
                _juego.GuardarCorrida();
                Alternar(false);
                SalirAlMenu?.Invoke();
                break;

            case 3:
                _juego.GuardarCorrida();
                GetTree().Quit();
                break;
        }
    }

    public override void _Draw()
    {
        if (!_abierto)
            return;

        this.Scrim();

        // El marco alrededor de los cuatro botones, con el mismo aire que el
        // resto: título arriba, regla, y la lista abajo.
        var primero = RectBoton(0);
        var ultimo = RectBoton(_botones.Length - 1);

        var marco = new Rect2(
            primero.Position.X - 40f, 240f,
            primero.Size.X + 80f, ultimo.End.Y - 240f + 40f);

        this.Panel(marco, elevado: true, opacidad: 1f);

        this.Label(marco.Row(24f, 56f), Loc.Get("ui.pausa.titulo"), 48, Palette.TextPrimary,
            HorizontalAlignment.Center);

        this.Label(marco.Row(78f, 26f), Loc.Get("ui.pausa.aviso"), 16, Palette.TextMuted,
            HorizontalAlignment.Center);

        this.Rule(marco, 112f);

        for (int i = 0; i < _botones.Length; i++)
        {
            var r = RectBoton(i);
            bool resaltado = i == _bajoElCursor;

            // El acento del último —salir del juego— es rojo: es la única acción
            // de esta lista que no se puede deshacer con otro click.
            var acento = i == _botones.Length - 1 ? Palette.Danger : Palette.BorderNormal;

            this.Button(r, "", acento, relleno: false, encima: resaltado);

            this.Label(new Rect2(r.Position.X + 28f, r.Position.Y + 8f, r.Size.X - 56f, 28f),
                Loc.Get(_botones[i].ClaveTitulo), 22, Palette.TextPrimary);

            this.Label(new Rect2(r.Position.X + 28f, r.Position.Y + 38f, r.Size.X - 56f, 24f),
                Loc.Get(_botones[i].ClaveDetalle), 14, Palette.TextMuted);
        }
    }
}
