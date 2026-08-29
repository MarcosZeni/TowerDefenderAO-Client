using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using TowerOfArgentum.Simulacion;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// Editor de layouts: se pinta la ruta a mano y sale un JSON.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Esto es una HERRAMIENTA, no parte del juego.</b> La escena vive en
/// Godot; el layout que produce es un JSON neutro. Tiene que ser así porque el
/// <b>servidor revalida las corridas y no tiene Godot</b>: <c>Simulacion\</c>
/// tiene prohibido <c>using Godot</c> y el revalidador es una librería .NET
/// pelada. Un <c>.tscn</c> no lo puede leer.</para>
///
/// <para><b>Se pinta lógica, no arte.</b> Los cinco valores son entrada, ruta,
/// colocable, bloqueado y Torre. El bioma se aplica encima al dibujar, así que
/// <b>el mismo layout sirve para las ocho áreas</b>: pintar con pasto lo ataría a
/// Ullathorpe y habría que repintarlo para nieve, arena y lava.</para>
///
/// <para>El piso que se ve es solo para no pintar sobre un fondo gris. Cambiarlo
/// no cambia el dato.</para>
/// </remarks>
public partial class EditorLayouts : Node2D
{
    /// <summary>Lo que puede ser una celda. Es el dato que se guarda.</summary>
    private enum Pincel
    {
        Bloqueado = 0,
        Colocable = 1,
        Route = 2,
        Entrada = 3,
        Tower = 4,
    }

    /// <summary>
    /// El campo, en celdas. <b>20×11 con celda de 96 px.</b>
    /// </summary>
    /// <remarks>
    /// <para>Son <b>220 celdas</b>, que es la vista de Argentum Online —17×13 =
    /// 221— con celdas cuadradas. La medida exacta de AO no entra en 1920×1080
    /// con celdas cuadradas (1080/13 = 83,08), y 20 columnas dan 96 px justos.</para>
    ///
    /// <para>96 divide limpio el tile x4 de AO, que mide 128: se dibuja al 75 %.
    /// Y contra las celdas de 64 que había antes, los sprites salen <b>50 % más
    /// grandes</b>, que es lo que hace falta con seis de las nueve clases
    /// compartiendo muñeco y con blancos de toque en móvil.</para>
    ///
    /// <para>⚠ <c>Escala.cs</c> e <c>INTERFAZ.md</c> todavía dicen 30×16 con celda
    /// de 64. Hay que bajarlos acá, y con eso se caen los tres layouts escritos a
    /// mano en <c>Layouts.cs</c>, que tienen coordenadas de 30×16.</para>
    /// </remarks>
    private const int Width = 20;

    private const int Height = 11;

    private const int Cell = 96;

    /// <summary>La grilla pintada. Arranca entera en bloqueado.</summary>
    private Pincel[,] _celdas = new Pincel[1, 1];

    private Pincel _pincel = Pincel.Route;

    private Cell? _bajoMouse;

    private bool _pintando;

    private bool _borrando;

    /// <summary>Qué piso se ve de fondo. Solo estética.</summary>
    private int _piso;

    private static readonly string[] Pisos =
    [
        "piso.pastoA", "piso.pastoB", "piso.pastoC", "piso.pastoD",
        "piso.desierto", "piso.nieve", "piso.dungeon", "piso.dungeonNewbie",
        "piso.minas", "piso.lava",
    ];

    private string _estado = "";

    private bool _valido;

    private int _largoRuta;

    /// <summary>Si se ve el panel. Se esconde con H para pintar las filas de abajo.</summary>
    private bool _panel = true;

    public override void _Ready()
    {
        UiStyle.Init();

        Reiniciar();
        Validar();
    }

    /// <summary>
    /// Grilla nueva, <b>todo bloqueado</b>.
    /// </summary>
    /// <remarks>
    /// Bloqueado y no colocable, porque las celdas donde se puede poner
    /// <b>aparecen solas</b> al pintar la ruta: el layout se dibuja trazando el
    /// camino y el borde colocable se llena atrás. Arrancar con las 220 en
    /// colocable obligaría a tachar a mano todo lo que no toca la ruta.
    /// </remarks>
    private void Reiniciar()
    {
        _celdas = new Pincel[Width, Height];
        QueueRedraw();
    }

    /// <summary>
    /// ⚠ Se atiende <b>ESC</b> acá para que no cierre el programa.
    /// </summary>
    /// <remarks>
    /// El motor cierra con ESC cuando la acción <c>ui_cancel</c> llega sin que
    /// nadie la haya consumido. En una herramienta de dibujo eso es perder el
    /// trabajo de media hora con una tecla que uno aprieta sin pensar. Va en
    /// <c>_Input</c> y no en <c>_UnhandledInput</c> porque hay que agarrarla
    /// <b>antes</b> de que el motor la vea.
    /// </remarks>
    public override void _Input(InputEvent evento)
    {
        if (evento is InputEventKey { Pressed: true, Keycode: Key.Escape })
            GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent evento)
    {
        if (evento is InputEventMouseButton boton)
        {
            // Las fichas de pincel primero: si el click cayó en una, elige el
            // pincel y no pinta la celda que la ficha tapa.
            if (boton is { Pressed: true, ButtonIndex: MouseButton.Left }
                && PincelEn(boton.Position))
            {
                return;
            }

            if (boton.ButtonIndex == MouseButton.Left)
            {
                _pintando = boton.Pressed;

                if (boton.Pressed)
                    Pintar();
            }
            else if (boton.ButtonIndex == MouseButton.Right)
            {
                _borrando = boton.Pressed;

                if (boton.Pressed)
                    Pintar();
            }

            if (!boton.Pressed)
                Validar();

            return;
        }

        if (evento is InputEventMouseMotion movimiento)
        {
            _bajoMouse = ACelda(movimiento.Position);

            if (_pintando || _borrando)
                Pintar();

            QueueRedraw();
            return;
        }

        if (evento is not InputEventKey tecla || !tecla.Pressed || tecla.Echo)
            return;

        switch (tecla.Keycode)
        {
            case Key.Key1: _pincel = Pincel.Bloqueado; break;
            case Key.Key2: _pincel = Pincel.Colocable; break;
            case Key.Key3: _pincel = Pincel.Route; break;
            case Key.Key4: _pincel = Pincel.Entrada; break;
            case Key.Key5: _pincel = Pincel.Tower; break;

            case Key.P:
                _piso = (_piso + 1) % Pisos.Length;
                break;

            case Key.C:
                Reiniciar();
                Validar();
                break;

            // El panel tapa las filas de abajo mientras se dibuja. Se esconde
            // para pintar ahí: en el juego esas filas se ven, porque el mundo
            // tiene su propio agujero adentro del marco.
            case Key.H:
                _panel = !_panel;
                break;

            case Key.S:
                Store();
                break;
        }

        QueueRedraw();
    }

    private void Pintar()
    {
        if (_bajoMouse is not { } c)
            return;

        var valor = _borrando ? Pincel.Bloqueado : _pincel;

        // Entrada y Torre son únicas: pintar una segunda borra la primera. Si no,
        // el trazador encuentra dos arranques y no sabe cuál es.
        if (valor is Pincel.Entrada or Pincel.Tower)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_celdas[x, y] == valor)
                        _celdas[x, y] = Pincel.Route;
                }
            }
        }

        _celdas[c.X, c.Y] = valor;

        // Al trazar el camino, el borde colocable aparece solo.
        //
        // Es la regla número uno del layout —tiene que haber dónde poner pegado a
        // la ruta o el cuerpo a cuerpo no sirve— y hacerla a mano es pintar dos
        // veces el mismo recorrido. Solo se pisa lo bloqueado: una celda que ya es
        // ruta no se toca, y por eso dos tramos paralelos no se comen entre sí.
        if (valor is Pincel.Route or Pincel.Entrada or Pincel.Tower)
            AbrirCostados(c);

        QueueRedraw();
    }

    /// <summary>Pone en colocable lo que rodea a una celda de ruta, si estaba bloqueado.</summary>
    /// <remarks>
    /// Los ocho vecinos y no los cuatro ortogonales: en una curva, la celda
    /// diagonal es parte del costado del camino igual, y dejarla bloqueada hace
    /// que las esquinas se vean mordidas.
    /// </remarks>
    private void AbrirCostados(Cell c)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = c.X + dx;
                int y = c.Y + dy;

                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    continue;

                if (_celdas[x, y] == Pincel.Bloqueado)
                    _celdas[x, y] = Pincel.Colocable;
            }
        }
    }

    private Cell? ACelda(Vector2 posicion)
    {
        int x = (int)(posicion.X / Cell);
        int y = (int)(posicion.Y / Cell);

        return x >= 0 && y >= 0 && x < Width && y < Height ? new Cell(x, y) : null;
    }

    // -------------------------------------------------------------------------
    //  Validación
    // -------------------------------------------------------------------------

    /// <summary>
    /// Chequea las reglas duras del layout.
    /// </summary>
    /// <remarks>
    /// <para>Pintar a mano saca el generador, <b>no el chequeo</b>. Las cinco
    /// reglas están medidas y una mano libre las rompe sin querer:</para>
    ///
    /// <list type="number">
    /// <item>Un solo camino, sin ramas: el enemigo avanza por una distancia
    /// escalar y una bifurcación no tiene cómo representarse.</item>
    /// <item>Celdas colocables pegadas a la ruta, o el cuerpo a cuerpo se pone,
    /// cuesta oro y no pega nunca.</item>
    /// <item>La Torre lejos del borde: contra una esquina su anillo queda cortado
    /// y un enemigo que llegue se vuelve imposible de alcanzar.</item>
    /// <item>Esquinas de pantalla libres, que es donde flota el HUD.</item>
    /// <item>Largo parecido al de los demás: si uno mide 74 celdas y otro 122, la
    /// seed decide el puesto en el ranking antes de que el jugador juegue.</item>
    /// </list>
    /// </remarks>
    private void Validar()
    {
        var problemas = new List<string>();

        Cell? entrada = null;
        Cell? torre = null;
        int celdasRuta = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                switch (_celdas[x, y])
                {
                    case Pincel.Entrada: entrada = new Cell(x, y); celdasRuta++; break;
                    case Pincel.Tower: torre = new Cell(x, y); celdasRuta++; break;
                    case Pincel.Route: celdasRuta++; break;
                }
            }
        }

        if (entrada is null)
            problemas.Add("falta la entrada (tecla 4)");

        if (torre is null)
            problemas.Add("falta la Torre (tecla 5)");

        _largoRuta = 0;

        if (entrada is { } e && torre is { } t)
        {
            var camino = Trace(e, t, out string error);

            if (camino is null)
                problemas.Add(error);
            else
                _largoRuta = camino.Count;

            if (camino is not null && camino.Count != celdasRuta)
            {
                problemas.Add($"hay {celdasRuta - camino.Count} celda(s) de ruta " +
                              "sueltas, fuera del camino");
            }

            // La Torre necesita su anillo entero adentro del campo.
            if (t.X < 2 || t.Y < 2 || t.X >= Width - 2 || t.Y >= Height - 2)
                problemas.Add("la Torre está a menos de 2 celdas del borde");
        }

        int pegadas = ContarPegadas();

        if (pegadas < 8)
            problemas.Add($"solo {pegadas} colocables pegadas a la ruta: el cuerpo a cuerpo no sirve");

        foreach (var esquina in Esquinas())
        {
            if (_celdas[esquina.X, esquina.Y] == Pincel.Colocable)
            {
                problemas.Add("hay colocables en una esquina, donde flota el HUD");
                break;
            }
        }

        _valido = problemas.Count == 0;

        _estado = _valido
            ? $"OK · ruta de {_largoRuta} celdas · {pegadas} colocables pegadas"
            : string.Join("   ·   ", problemas);
    }

    /// <summary>
    /// Sigue el camino de la entrada a la Torre, y se queja si se bifurca.
    /// </summary>
    /// <remarks>
    /// Va celda por celda mirando los vecinos de ruta que todavía no visitó. Si en
    /// algún paso hay <b>dos</b>, el camino se abrió en dos y no hay forma de
    /// decir cuánto avanzó un enemigo. Si hay <b>cero</b> antes de llegar a la
    /// Torre, está cortado.
    /// </remarks>
    private List<Cell>? Trace(Cell entrada, Cell torre, out string error)
    {
        error = "";

        var camino = new List<Cell> { entrada };
        var visitadas = new HashSet<Cell> { entrada };
        var actual = entrada;

        while (actual != torre)
        {
            var siguientes = new List<Cell>();

            foreach (var v in Neighbours(actual))
            {
                if (visitadas.Contains(v))
                    continue;

                if (_celdas[v.X, v.Y] is Pincel.Route or Pincel.Tower)
                    siguientes.Add(v);
            }

            if (siguientes.Count == 0)
            {
                error = $"el camino se corta en ({actual.X},{actual.Y}) sin llegar a la Torre";
                return null;
            }

            if (siguientes.Count > 1)
            {
                error = $"el camino se bifurca en ({actual.X},{actual.Y}): tiene que ser uno solo";
                return null;
            }

            actual = siguientes[0];
            visitadas.Add(actual);
            camino.Add(actual);
        }

        return camino;
    }

    /// <summary>Los cuatro vecinos ortogonales. Sin diagonales: no se camina en diagonal.</summary>
    private IEnumerable<Cell> Neighbours(Cell c)
    {
        if (c.X > 0) yield return new Cell(c.X - 1, c.Y);
        if (c.Y > 0) yield return new Cell(c.X, c.Y - 1);
        if (c.X < Width - 1) yield return new Cell(c.X + 1, c.Y);
        if (c.Y < Height - 1) yield return new Cell(c.X, c.Y + 1);
    }

    private int ContarPegadas()
    {
        int cuantas = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_celdas[x, y] != Pincel.Colocable)
                    continue;

                foreach (var v in Neighbours(new Cell(x, y)))
                {
                    if (_celdas[v.X, v.Y] is Pincel.Route or Pincel.Entrada or Pincel.Tower)
                    {
                        cuantas++;
                        break;
                    }
                }
            }
        }

        return cuantas;
    }

    /// <summary>Las cuatro esquinas de 3x2 celdas donde flota el HUD.</summary>
    private IEnumerable<Cell> Esquinas()
    {
        for (int dy = 0; dy < 2; dy++)
        {
            for (int dx = 0; dx < 3; dx++)
            {
                yield return new Cell(dx, dy);
                yield return new Cell(Width - 1 - dx, dy);
                yield return new Cell(dx, Height - 1 - dy);
                yield return new Cell(Width - 1 - dx, Height - 1 - dy);
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Guardado
    // -------------------------------------------------------------------------

    /// <summary>
    /// Escribe el layout como ASCII adentro de un JSON.
    /// </summary>
    /// <remarks>
    /// <para>ASCII y no una lista de coordenadas: el mapa <b>se ve</b> abriendo el
    /// archivo, se lee en un diff y se corrige a mano si hace falta. Una lista de
    /// pares no la entiende nadie a los seis meses.</para>
    ///
    /// <para>Se guarda igual aunque no valide, con la bandera <c>valido</c> en
    /// falso: perder media hora de dibujo porque falta cerrar un tramo sería la
    /// peor forma de perderla.</para>
    /// </remarks>
    private void Store()
    {
        var filas = new JsonArray();

        for (int y = 0; y < Height; y++)
        {
            var fila = new StringBuilder(Width);

            for (int x = 0; x < Width; x++)
            {
                fila.Append(_celdas[x, y] switch
                {
                    Pincel.Bloqueado => 'X',
                    Pincel.Colocable => '.',
                    Pincel.Route => '#',
                    Pincel.Entrada => 'E',
                    _ => 'T',
                });
            }

            filas.Add(fila.ToString());
        }

        var salida = new JsonObject
        {
            ["version"] = 1,

            ["legend"] = "E entrada · # ruta · . colocable · X bloqueado · T la Torre",

            ["note"] = "Lo escribe el editor de layouts (Escenas/EditorLayouts.tscn). " +
                        "Se puede corregir a mano, pero conviene volver a pasarlo por " +
                        "el editor para que revalide.",

            ["width"] = Width,
            ["height"] = Height,
            ["routeLength"] = _largoRuta,
            ["valid"] = _valido,
            ["rows"] = filas,
        };

        string carpeta = ProjectSettings.GlobalizePath("res://Datos/layouts");
        System.IO.Directory.CreateDirectory(carpeta);

        // Numerado y sin pisar: dos dibujos seguidos no se tapan entre sí.
        int n = 1;
        string ruta;

        do
        {
            ruta = System.IO.Path.Combine(carpeta, $"layout{n:00}.json");
            n++;
        }
        while (System.IO.File.Exists(ruta));

        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        System.IO.File.WriteAllText(
            ruta,
            salida.ToJsonString(opciones),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _estado = $"guardado en {System.IO.Path.GetFileName(ruta)}" +
                  (_valido ? "" : "   ⚠ NO valida todavía");

        GD.Print($"Layout guardado: {ruta}");
    }

    // -------------------------------------------------------------------------
    //  Dibujo
    // -------------------------------------------------------------------------

    /// <summary>
    /// El color de cada pincel. <b>Los mismos que usa el juego para lo mismo.</b>
    /// </summary>
    /// <remarks>
    /// <para>Sale de <see cref="Ui.Palette"/> y no de valores sueltos, y no es
    /// cosmético: pintar una celda colocable con un verde y que en la partida se
    /// vea otro obliga a traducir de memoria entre las dos pantallas. La
    /// <b>entrada</b> es celeste porque el portal del juego se dibuja con ese
    /// mismo celeste, y la <b>Torre</b> es violeta porque es el único color de la
    /// paleta que no aparece en el tablero: no se confunde con nada.</para>
    ///
    /// <para>La ruta es el ámbar oscurecido. La paleta del diseño es fría y no
    /// tiene un marrón; en vez de inventar uno se usa el ámbar bajado, que es
    /// tierra y sigue siendo de la familia.</para>
    /// </remarks>
    private static Color ColorDe(Pincel pincel) => pincel switch
    {
        Pincel.Bloqueado => Palette.BackgroundMain.Alpha(0.62f),
        Pincel.Colocable => Palette.Success.Alpha(0.24f),
        Pincel.Route => Palette.Warning.Darkened(0.55f).Alpha(0.80f),
        Pincel.Entrada => Palette.Info.Alpha(0.85f),
        _ => Palette.Epic.Alpha(0.85f),
    };

    public override void _Draw()
    {
        DibujarPiso();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var caja = new Rect2(x * Cell, y * Cell, Cell, Cell);

                DrawRect(caja, ColorDe(_celdas[x, y]));
                DrawRect(caja, Palette.BackgroundMain.Alpha(0.18f), filled: false, width: 1f);
            }
        }

        if (_bajoMouse is { } c)
        {
            var caja = new Rect2(c.X * Cell, c.Y * Cell, Cell, Cell);

            // El cursor lleva el color del pincel que está cargado, no blanco:
            // así se ve qué se va a pintar antes de apretar.
            this.Glow(caja, ColorDe(_pincel), 1.4f);
            DrawRect(caja, Palette.TextPrimary, filled: false, width: 3f);
        }

        if (_panel)
            DibujarPanel();
    }

    /// <summary>
    /// El piso de fondo, para no pintar sobre gris.
    /// </summary>
    /// <remarks>
    /// La textura es un bloque de 4x4 tiles de AO: 512x512 px en x4, o sea 4x4
    /// celdas. Se repite por bloques en vez de estirarse, que es como se va a
    /// dibujar en el juego.
    /// </remarks>
    private void DibujarPiso()
    {
        var textura = CargadorGraficos.Textura(Pisos[_piso]);

        if (textura is null)
        {
            DrawRect(
                new Rect2(0, 0, Width * Cell, Height * Cell),
                Palette.BackgroundAlt);

            return;
        }

        int lado = Cell * 4;

        for (int y = 0; y < Height * Cell; y += lado)
        {
            for (int x = 0; x < Width * Cell; x += lado)
                DrawTextureRect(textura, new Rect2(x, y, lado, lado), tile: false);
        }
    }

    /// <summary>
    /// <summary>Los cinco pinceles, en el orden en que se numeran y se dibujan.</summary>
    private static readonly (Pincel Pincel, string Nombre)[] _pinceles =
    [
        (Pincel.Bloqueado, "bloqueado"),
        (Pincel.Colocable, "colocable"),
        (Pincel.Route, "ruta"),
        (Pincel.Entrada, "entrada"),
        (Pincel.Tower, "Torre"),
    ];

    /// <summary>
    /// La ficha del pincel <paramref name="i"/>. La usan el dibujo Y el click.
    /// </summary>
    /// <remarks>
    /// Las cinco ocupan los dos bloques del medio de la barra de abajo del
    /// diseño —el del arma y el de los planos—: en uno solo quedan de 64 px y el
    /// nombre no entra.
    /// </remarks>
    private static Rect2 FichaDePincel(int i)
    {
        var zona = new Rect2(
            UiLayout.EquippedWeapon.Position,
            new Vector2(
                UiLayout.ActiveBlueprints.End.X - UiLayout.EquippedWeapon.Position.X,
                UiLayout.EquippedWeapon.Size.Y));

        float paso = zona.Size.X / _pinceles.Length;

        return new Rect2(zona.Position.X + (i * paso), zona.Position.Y + 30f, paso - 10f, 56f);
    }

    /// <summary>
    /// Un click sobre una ficha carga ese pincel. <c>true</c> si lo atendió.
    /// </summary>
    /// <remarks>
    /// ⚠ Hay que consumirlo: el panel tapa las dos filas de abajo de la grilla y
    /// sin esto, elegir un pincel además <b>pinta</b> la celda que quedó debajo
    /// de la ficha.
    /// </remarks>
    private bool PincelEn(Vector2 posicion)
    {
        if (!_panel)
            return false;

        for (int i = 0; i < _pinceles.Length; i++)
        {
            if (!FichaDePincel(i).HasPoint(posicion))
                continue;

            _pincel = _pinceles[i].Pincel;
            QueueRedraw();

            return true;
        }

        return false;
    }

    /// <summary>
    /// El panel de abajo: pinceles, teclas y —lo que importa— si el layout valida.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Va ENCIMA del campo, anclado al borde de abajo del lienzo.</b>
    /// La primera versión lo puso en la franja sobrante debajo de la grilla, y
    /// con 16 filas de 64 px eso son 56 px: la línea de estado caía en y=1130,
    /// fuera de los 1080 del lienzo. Se guardaron diez layouts inválidos sin que
    /// nada avisara, porque el aviso estaba abajo de la pantalla.</para>
    ///
    /// <para>⚠ <b>Que el panel tape filas ya NO tiene consecuencia de diseño.</b>
    /// Con el marco (2026-08-28) el juego dibuja el mundo adentro de su propio
    /// agujero y ningún panel se le encima, así que una celda de ruta pintada en
    /// la fila 10 se juega perfecto. Acá el panel la tapa solo mientras se
    /// dibuja, y para eso está la <b>H</b>.</para>
    /// </remarks>
    private void DibujarPanel()
    {
        // ⚠ El panel usa el MISMO rectángulo que la barra de abajo del juego
        // (`build_panel` del diseño). Así, lo que acá tapa el panel es
        // exactamente lo que en la partida va a quedar abajo del HUD: si una
        // celda de ruta no se ve mientras se dibuja, tampoco se va a poder
        // clickear jugando.
        // Llega hasta el borde de abajo del lienzo —o sea, la barra de build más
        // el pie— y no solo la barra: si terminara en 1024, los últimos 32 px de
        // la fila 10 asomarían por debajo del panel y se verían como una franja
        // de grilla suelta.
        var panel = new Rect2(
            UiLayout.BuildPanel.Position,
            new Vector2(UiLayout.BuildPanel.Size.X,
                Escala.LienzoAlto - UiLayout.BuildPanel.Position.Y - 4f));

        // El fondo tapa el ancho entero: el panel del diseño deja 12 px de margen
        // a cada lado, y ahí abajo hay grilla.
        DrawRect(
            new Rect2(0, panel.Position.Y, Escala.LienzoAncho,
                Escala.LienzoAlto - panel.Position.Y),
            Palette.BackgroundMain);

        this.Panel(panel, opacidad: 1f);

        // El estado va PRIMERO y más grande: es lo único que hay que mirar para
        // saber si lo que se está dibujando sirve.
        var color = _valido ? Palette.Success : Palette.Danger;

        DrawRect(panel.Row(0f, 4f), color);

        var estado = UiLayout.EquippedPerks;

        this.Label(estado.Row(0f, 22f), "layout".ToUpperInvariant(), 14, Palette.TextMuted);

        this.Label(estado.Row(24f, 34f), (_valido ? "✓  " : "×  ") + _estado, 24, color);

        this.Label(estado.Row(64f, 24f),
            $"campo {Width}×{Height}, celda {Cell} px — la vista de AO, 220 celdas",
            14, Palette.TextMuted);

        this.Label(estado.Row(88f, 24f), "click izquierdo pinta · derecho borra", 14,
            Palette.TextMuted);

        // Las cinco fichas, con el color real de cada pincel adentro: el número
        // de la tecla arriba y el nombre abajo.
        this.Label(new Rect2(FichaDePincel(0).Position.X, panel.Position.Y + 6f, 300f, 22f),
            "pincel".ToUpperInvariant(), 14, Palette.TextMuted);

        for (int i = 0; i < _pinceles.Length; i++)
        {
            var r = FichaDePincel(i);
            bool activo = _pincel == _pinceles[i].Pincel;
            var suyo = ColorDe(_pinceles[i].Pincel);

            this.Slot(r, activo ? Palette.TextPrimary : Palette.BorderNormal, activo);

            // La muestra de color adentro de la ficha, opaca: el tinte del
            // tablero es semitransparente y sobre el panel oscuro no se vería.
            DrawRect(new Rect2(r.Position.X + 8f, r.Position.Y + 8f, 26f, r.Size.Y - 16f),
                suyo.Alpha(1f));

            this.Label(new Rect2(r.Position.X + 42f, r.Position.Y + 6f, r.Size.X - 48f, 20f),
                $"{i + 1}", 14, Palette.TextMuted);

            this.Label(new Rect2(r.Position.X + 42f, r.Position.Y + 26f, r.Size.X - 48f, 24f),
                _pinceles[i].Nombre, 17,
                activo ? Palette.TextPrimary : Palette.TextSecondary);
        }

        // Las teclas que no son pinceles, en el último bloque.
        var teclas = UiLayout.Consumables;

        this.Label(teclas.Row(0f, 22f), "teclas".ToUpperInvariant(), 14, Palette.TextMuted);

        this.StatRow(teclas.Row(24f, 21f), "P", $"piso ({Pisos[_piso]})");
        this.StatRow(teclas.Row(45f, 21f), "H", "esconde el panel");

        this.StatRow(teclas.Row(66f, 21f), "C", "limpia");
        this.StatRow(teclas.Row(87f, 21f), "S", "guarda", Palette.Success);
    }
}
