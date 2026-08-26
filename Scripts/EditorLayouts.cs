using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using TowerOfArgentum.Simulacion;

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
        Ruta = 2,
        Entrada = 3,
        Torre = 4,
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
    private const int Ancho = 20;

    private const int Alto = 11;

    private const int Celda = 96;

    /// <summary>La grilla pintada. Arranca entera en bloqueado.</summary>
    private Pincel[,] _celdas = new Pincel[1, 1];

    private Pincel _pincel = Pincel.Ruta;

    private Celda? _bajoMouse;

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
        _celdas = new Pincel[Ancho, Alto];
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
            case Key.Key3: _pincel = Pincel.Ruta; break;
            case Key.Key4: _pincel = Pincel.Entrada; break;
            case Key.Key5: _pincel = Pincel.Torre; break;

            case Key.P:
                _piso = (_piso + 1) % Pisos.Length;
                break;

            case Key.C:
                Reiniciar();
                Validar();
                break;

            // El panel tapa las filas de abajo. Se esconde para pintar ahí, que
            // es justo donde en el juego va a flotar el HUD.
            case Key.H:
                _panel = !_panel;
                break;

            case Key.S:
                Guardar();
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
        if (valor is Pincel.Entrada or Pincel.Torre)
        {
            for (int y = 0; y < Alto; y++)
            {
                for (int x = 0; x < Ancho; x++)
                {
                    if (_celdas[x, y] == valor)
                        _celdas[x, y] = Pincel.Ruta;
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
        if (valor is Pincel.Ruta or Pincel.Entrada or Pincel.Torre)
            AbrirCostados(c);

        QueueRedraw();
    }

    /// <summary>Pone en colocable lo que rodea a una celda de ruta, si estaba bloqueado.</summary>
    /// <remarks>
    /// Los ocho vecinos y no los cuatro ortogonales: en una curva, la celda
    /// diagonal es parte del costado del camino igual, y dejarla bloqueada hace
    /// que las esquinas se vean mordidas.
    /// </remarks>
    private void AbrirCostados(Celda c)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = c.X + dx;
                int y = c.Y + dy;

                if (x < 0 || y < 0 || x >= Ancho || y >= Alto)
                    continue;

                if (_celdas[x, y] == Pincel.Bloqueado)
                    _celdas[x, y] = Pincel.Colocable;
            }
        }
    }

    private Celda? ACelda(Vector2 posicion)
    {
        int x = (int)(posicion.X / Celda);
        int y = (int)(posicion.Y / Celda);

        return x >= 0 && y >= 0 && x < Ancho && y < Alto ? new Celda(x, y) : null;
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

        Celda? entrada = null;
        Celda? torre = null;
        int celdasRuta = 0;

        for (int y = 0; y < Alto; y++)
        {
            for (int x = 0; x < Ancho; x++)
            {
                switch (_celdas[x, y])
                {
                    case Pincel.Entrada: entrada = new Celda(x, y); celdasRuta++; break;
                    case Pincel.Torre: torre = new Celda(x, y); celdasRuta++; break;
                    case Pincel.Ruta: celdasRuta++; break;
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
            var camino = Trazar(e, t, out string error);

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
            if (t.X < 2 || t.Y < 2 || t.X >= Ancho - 2 || t.Y >= Alto - 2)
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
    private List<Celda>? Trazar(Celda entrada, Celda torre, out string error)
    {
        error = "";

        var camino = new List<Celda> { entrada };
        var visitadas = new HashSet<Celda> { entrada };
        var actual = entrada;

        while (actual != torre)
        {
            var siguientes = new List<Celda>();

            foreach (var v in Vecinos(actual))
            {
                if (visitadas.Contains(v))
                    continue;

                if (_celdas[v.X, v.Y] is Pincel.Ruta or Pincel.Torre)
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
    private IEnumerable<Celda> Vecinos(Celda c)
    {
        if (c.X > 0) yield return new Celda(c.X - 1, c.Y);
        if (c.Y > 0) yield return new Celda(c.X, c.Y - 1);
        if (c.X < Ancho - 1) yield return new Celda(c.X + 1, c.Y);
        if (c.Y < Alto - 1) yield return new Celda(c.X, c.Y + 1);
    }

    private int ContarPegadas()
    {
        int cuantas = 0;

        for (int y = 0; y < Alto; y++)
        {
            for (int x = 0; x < Ancho; x++)
            {
                if (_celdas[x, y] != Pincel.Colocable)
                    continue;

                foreach (var v in Vecinos(new Celda(x, y)))
                {
                    if (_celdas[v.X, v.Y] is Pincel.Ruta or Pincel.Entrada or Pincel.Torre)
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
    private IEnumerable<Celda> Esquinas()
    {
        for (int dy = 0; dy < 2; dy++)
        {
            for (int dx = 0; dx < 3; dx++)
            {
                yield return new Celda(dx, dy);
                yield return new Celda(Ancho - 1 - dx, dy);
                yield return new Celda(dx, Alto - 1 - dy);
                yield return new Celda(Ancho - 1 - dx, Alto - 1 - dy);
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
    private void Guardar()
    {
        var filas = new JsonArray();

        for (int y = 0; y < Alto; y++)
        {
            var fila = new StringBuilder(Ancho);

            for (int x = 0; x < Ancho; x++)
            {
                fila.Append(_celdas[x, y] switch
                {
                    Pincel.Bloqueado => 'X',
                    Pincel.Colocable => '.',
                    Pincel.Ruta => '#',
                    Pincel.Entrada => 'E',
                    _ => 'T',
                });
            }

            filas.Add(fila.ToString());
        }

        var salida = new JsonObject
        {
            ["version"] = 1,

            ["leyenda"] = "E entrada · # ruta · . colocable · X bloqueado · T la Torre",

            ["aviso"] = "Lo escribe el editor de layouts (Escenas/EditorLayouts.tscn). " +
                        "Se puede corregir a mano, pero conviene volver a pasarlo por " +
                        "el editor para que revalide.",

            ["ancho"] = Ancho,
            ["alto"] = Alto,
            ["largoRuta"] = _largoRuta,
            ["valido"] = _valido,
            ["filas"] = filas,
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

    public override void _Draw()
    {
        DibujarPiso();

        for (int y = 0; y < Alto; y++)
        {
            for (int x = 0; x < Ancho; x++)
            {
                var caja = new Rect2(x * Celda, y * Celda, Celda, Celda);

                var tinte = _celdas[x, y] switch
                {
                    Pincel.Bloqueado => new Color(0f, 0f, 0f, 0.55f),
                    Pincel.Colocable => new Color(0.25f, 0.85f, 0.45f, 0.22f),
                    Pincel.Ruta => new Color(0.55f, 0.40f, 0.20f, 0.75f),
                    Pincel.Entrada => new Color(0.95f, 0.85f, 0.20f, 0.85f),
                    _ => new Color(0.30f, 0.55f, 0.95f, 0.85f),
                };

                DrawRect(caja, tinte);
                DrawRect(caja, new Color(0f, 0f, 0f, 0.15f), filled: false, width: 1f);
            }
        }

        if (_bajoMouse is { } c)
        {
            DrawRect(
                new Rect2(c.X * Celda, c.Y * Celda, Celda, Celda),
                Colors.White, filled: false, width: 3f);
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
                new Rect2(0, 0, Ancho * Celda, Alto * Celda),
                new Color(0.16f, 0.18f, 0.16f));

            return;
        }

        int lado = Celda * 4;

        for (int y = 0; y < Alto * Celda; y += lado)
        {
            for (int x = 0; x < Ancho * Celda; x += lado)
                DrawTextureRect(textura, new Rect2(x, y, lado, lado), tile: false);
        }
    }

    /// <summary>
    /// El panel de abajo: pinceles, teclas y —lo que importa— si el layout
    /// valida.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Va ENCIMA del campo, anclado al borde de abajo del lienzo.</b> La
    /// primera versión lo puso en la franja sobrante debajo de la grilla, y con
    /// 16 filas de 64 px eso son 56 px: la línea de estado caía en y=1130, fuera
    /// de los 1080 del lienzo. Se guardaron diez layouts inválidos sin que nada
    /// avisara, porque el aviso estaba abajo de la pantalla. Con 20×11 el
    /// sobrante es de 24 px y habría sido peor.
    /// </remarks>
    private void DibujarPanel()
    {
        var fuente = ThemeDB.FallbackFont;

        const int AltoPanel = 132;
        int alto = Escala.LienzoAlto - AltoPanel;

        DrawRect(new Rect2(0, alto, Escala.LienzoAncho, AltoPanel),
            new Color(0.06f, 0.07f, 0.09f, 0.88f));

        string pincel = _pincel switch
        {
            Pincel.Bloqueado => "1 bloqueado",
            Pincel.Colocable => "2 colocable",
            Pincel.Ruta => "3 ruta",
            Pincel.Entrada => "4 entrada",
            _ => "5 Torre",
        };

        // El estado va PRIMERO y más grande: es lo único que hay que mirar para
        // saber si lo que se está dibujando sirve.
        var color = _valido
            ? new Color(0.45f, 0.90f, 0.55f)
            : new Color(0.98f, 0.50f, 0.40f);

        DrawRect(new Rect2(0, alto, Escala.LienzoAncho, 4), color);

        DrawString(fuente, new Vector2(16, alto + 34),
            (_valido ? "✓  " : "✗  ") + _estado,
            HorizontalAlignment.Left, -1, 24, color);

        DrawString(fuente, new Vector2(16, alto + 66),
            $"pincel: {pincel}      1 bloqueado   2 colocable   3 ruta   4 entrada   5 Torre" +
            "      click izq pinta · der borra",
            HorizontalAlignment.Left, -1, 18, new Color(0.85f, 0.88f, 0.92f));

        DrawString(fuente, new Vector2(16, alto + 92),
            $"P cambia piso ({Pisos[_piso]})   H esconde este panel   C limpia   S guarda",
            HorizontalAlignment.Left, -1, 18, new Color(0.60f, 0.65f, 0.72f));

        DrawString(fuente, new Vector2(16, alto + 118),
            $"campo {Ancho}x{Alto}, celda {Celda} px — la vista de AO, 220 celdas",
            HorizontalAlignment.Left, -1, 18, new Color(0.60f, 0.65f, 0.72f));
    }
}
