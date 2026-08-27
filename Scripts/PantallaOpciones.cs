using Godot;

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
    private Font _fuente = null!;

    /// <summary>Se cerró. Lo escucha quien lo abrió, para volver a mostrarse.</summary>
    public event System.Action? Cerrado;

    private bool _abierto;
    private int _bajoElCursor = -1;

    /// <summary>Qué atajo está esperando una tecla. −1 = ninguno.</summary>
    private int _capturando = -1;

    private const int Ancho = 900;
    private const int X0 = (Escala.LienzoAncho - Ancho) / 2;
    private const int Y0 = 120;
    private const int AltoFila = 46;

    public bool Abierto => _abierto;

    public override void _Ready()
    {
        _fuente = ThemeDB.FallbackFont;
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
        Configuracion.Guardar();
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
        => new(X0, Y0 + (i * AltoFila), Ancho, AltoFila - 6);

    /// <summary>Las cajitas que se pueden apretar dentro de una fila de valor.</summary>
    private static Rect2 RectValor(int i, int columna, int deCuantas)
    {
        float ancho = 150;
        float x = X0 + Ancho - 24 - ((deCuantas - columna) * (ancho + 10));

        return new Rect2(x, Y0 + (i * AltoFila) + 4, ancho, AltoFila - 14);
    }

    private static int IndiceDe(Fila fila) => (int)fila;

    private static int FilaRestaurar => TotalFilas - 2;
    private static int FilaVolver => TotalFilas - 1;

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la cicatriz de
    /// <see cref="PantallaEleccion"/>: el hook de GUI dejaba las pantallas sordas.
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
                Configuracion.Guardar();
                break;

            case Fila.VolumenSonido:
                Configuracion.VolumenSonido = VolumenEn(i, donde, Configuracion.VolumenSonido);
                Configuracion.Guardar();
                break;

            case Fila.AnimarArmas:
                Configuracion.AnimarArmas = !Configuracion.AnimarArmas;
                Configuracion.Guardar();
                break;

            case Fila.AnimarEfectos:
                Configuracion.AnimarEfectos = !Configuracion.AnimarEfectos;
                Configuracion.Guardar();
                break;
        }

        QueueRedraw();
    }

    private static Rect2 RectBarra(int i)
        => new(X0 + Ancho - 344, Y0 + (i * AltoFila) + 12, 320, AltoFila - 30);

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

        DrawRect(new Rect2(0, 0, Escala.LienzoAncho, Escala.LienzoAlto), new Color(0, 0, 0, 0.86f));

        Texto("OPCIONES", X0, Y0 - 34, 40, new Color(0.95f, 0.95f, 0.92f));

        Titulo(IndiceDe(Fila.TituloVentana), "VENTANA");
        ModoVentana(IndiceDe(Fila.ModoVentana));
        Resoluciones(IndiceDe(Fila.Resolucion));

        Titulo(IndiceDe(Fila.TituloSonido), "SONIDO");
        Barra(IndiceDe(Fila.VolumenMusica), "Música", Configuracion.VolumenMusica);
        Barra(IndiceDe(Fila.VolumenSonido), "Efectos", Configuracion.VolumenSonido);

        Titulo(IndiceDe(Fila.TituloAnimaciones), "ANIMACIONES");
        Interruptor(IndiceDe(Fila.AnimarArmas), "Armas de los defensores", Configuracion.AnimarArmas);
        Interruptor(IndiceDe(Fila.AnimarEfectos), "Portal y cristal de la Torre", Configuracion.AnimarEfectos);

        Titulo(IndiceDe(Fila.TituloTeclas), "TECLAS");

        for (int a = 0; a < CantidadAtajos; a++)
            Atajo(IndiceDe(Fila.PrimerAtajo) + a, (Atajo)a);

        Boton(FilaRestaurar, "Restaurar las teclas de fábrica");
        Boton(FilaVolver, "Volver   ·   ESC");
    }

    private void Titulo(int i, string texto)
        => Texto(texto, X0, Y0 + (i * AltoFila) + 30, 20, new Color(0.55f, 0.75f, 0.95f));

    private void Fondo(int i, bool interactiva)
    {
        var r = RectFila(i);
        bool resaltada = interactiva && i == _bajoElCursor;

        DrawRect(r, new Color(0.09f, 0.09f, 0.13f, resaltada ? 0.95f : 0.72f));

        if (resaltada)
            DrawRect(r, new Color(0.45f, 0.85f, 0.55f, 0.6f), filled: false, width: 2f);
    }

    private void ModoVentana(int i)
    {
        Fondo(i, interactiva: true);
        Texto("Modo", X0 + 20, Y0 + (i * AltoFila) + 28, 19, Blanco);

        bool completa = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.ExclusiveFullscreen
            or DisplayServer.WindowMode.Fullscreen;

        Opcion(RectValor(i, 0, 2), "Pantalla completa", completa);
        Opcion(RectValor(i, 1, 2), "En ventana", !completa);
    }

    private void Resoluciones(int i)
    {
        Fondo(i, interactiva: true);
        Texto("Resolución", X0 + 20, Y0 + (i * AltoFila) + 28, 19, Blanco);

        var actual = DisplayServer.WindowGetSize();

        for (int r = 0; r < Resolucion.Opciones.Length; r++)
        {
            var o = Resolucion.Opciones[r];
            Opcion(RectResolucion(i, r), $"{o.X}", o == actual);
        }

        // ⚠ La resolución cambia la NITIDEZ, no el campo visible: el lienzo es
        // siempre 1920x1080 y `keep` no le da más ruta a nadie. Decirlo acá evita
        // que el jugador piense que en 4K ve más mapa (INTERFAZ.md §1).
        Texto("no cambia el campo visible, solo la nitidez",
            X0 + 300 + (Resolucion.Opciones.Length * 96) + 8,
            Y0 + (i * AltoFila) + 27, 14, new Color(0.5f, 0.5f, 0.48f));
    }

    private void Barra(int i, string nombre, int valor)
    {
        Fondo(i, interactiva: true);
        Texto(nombre, X0 + 20, Y0 + (i * AltoFila) + 28, 19, Blanco);

        var barra = RectBarra(i);

        DrawRect(barra, new Color(0, 0, 0, 0.55f));
        DrawRect(new Rect2(barra.Position, new Vector2(barra.Size.X * valor / 100f, barra.Size.Y)),
            valor > 0 ? new Color(0.35f, 0.75f, 0.95f) : new Color(0.4f, 0.4f, 0.4f));

        Texto(valor == 0 ? "mudo" : $"{valor}",
            barra.End.X - 52, Y0 + (i * AltoFila) + 28, 17, new Color(0.8f, 0.8f, 0.78f));
    }

    private void Interruptor(int i, string nombre, bool activo)
    {
        Fondo(i, interactiva: true);
        Texto(nombre, X0 + 20, Y0 + (i * AltoFila) + 28, 19, Blanco);

        Opcion(RectValor(i, 0, 1), activo ? "Sí" : "No", activo);
    }

    private void Atajo(int i, Atajo atajo)
    {
        Fondo(i, interactiva: true);
        Texto(Configuracion.NombreDe(atajo), X0 + 20, Y0 + (i * AltoFila) + 28, 19, Blanco);

        bool capturando = _capturando == (int)atajo;

        Opcion(RectValor(i, 0, 1),
            capturando ? "apretá una tecla" : Configuracion.NombreDeTecla(Configuracion.TeclaDe(atajo)),
            capturando);
    }

    private void Boton(int i, string texto)
    {
        Fondo(i, interactiva: true);
        Texto(texto, X0 + 20, Y0 + (i * AltoFila) + 28, 20, new Color(0.85f, 0.9f, 0.88f));
    }

    private void Opcion(Rect2 r, string texto, bool elegida)
    {
        DrawRect(r, elegida ? new Color(0.16f, 0.42f, 0.24f) : new Color(0.14f, 0.14f, 0.18f));
        DrawRect(r, elegida ? new Color(0.45f, 0.85f, 0.55f) : new Color(1, 1, 1, 0.14f),
            filled: false, width: elegida ? 2f : 1f);

        float ancho = _fuente.GetStringSize(texto, HorizontalAlignment.Left, -1, 17).X;

        Texto(texto, r.Position.X + ((r.Size.X - ancho) / 2f), r.Position.Y + 22, 17,
            elegida ? new Color(0.9f, 0.98f, 0.92f) : new Color(0.72f, 0.72f, 0.7f));
    }

    private static readonly Color Blanco = new(0.9f, 0.9f, 0.88f);

    private void Texto(string s, float x, float y, int tamano, Color color)
        => DrawString(_fuente, new Vector2(x, y), s, HorizontalAlignment.Left, -1, tamano, color);
}
