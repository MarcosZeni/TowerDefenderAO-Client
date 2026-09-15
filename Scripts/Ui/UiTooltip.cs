using System.Collections.Generic;
using Godot;

namespace TowerOfArgentum.Ui;

/// <summary>
/// El cartelito que explica qué es un icono, mientras el cursor está encima.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Existe porque los iconos reemplazaron palabras.</b> Cambiar
/// «daño 7» por una espada y un 7 alinea las columnas y saca el largo del texto
/// de la ecuación, pero le pide al jugador que <b>ya sepa</b> qué significa cada
/// dibujo. Un reloj puede ser cadencia, duración o tiempo restante. El cartel es
/// lo que hace que el icono no sea un acertijo la primera vez.</para>
///
/// <para><b>Cómo funciona con dibujo inmediato:</b> la interfaz se redibuja
/// entera cada cuadro, así que no hay nodos a los que colgarles un tooltip. En
/// vez de eso, cada pieza que quiere explicarse <b>declara su rectángulo</b>
/// mientras se dibuja, y el que gana es el último que contiene al cursor — que,
/// por el orden de dibujo, es también el que está más arriba. La pantalla llama
/// <see cref="Begin"/> al empezar su <c>_Draw</c> y <see cref="Draw"/> al
/// terminarlo, para que el cartel quede por encima de todo lo demás.</para>
///
/// <para>⚠ <b>Un cartel tiene DOS mitades y son distintas.</b> El
/// <see cref="Over">dato</see> es lo que el elemento vale ahora —«daño 10,4»,
/// «Ricochet», «2 con más vida»— y se muestra siempre. La
/// <see cref="Help">ayuda</see> es la regla del juego que hay detrás —«sacar un
/// perk equipado lo destruye»— y se puede apagar en Opciones
/// (<see cref="Settings.ShowHelp"/>). Van juntas en el mismo cartel, separadas
/// por una regla, porque son la misma pregunta del jugador: «¿qué es esto?».
/// El veterano apaga la mitad de abajo y conserva la de arriba.</para>
///
/// <para>⚠ <b>Guarda algo entre cuadros, y es a propósito:</b> el reloj de la
/// demora. El texto y el ancla siguen viviendo un solo cuadro —se resetean en
/// <see cref="Begin"/>, así que dos pantallas abiertas no se pisan— y lo único
/// que persiste es <i>desde cuándo</i> el cursor está sobre el mismo rectángulo.
/// Sin esa demora, cruzar la barra de arriba para llegar al botón de pausa
/// prende y apaga cuatro carteles en el camino.</para>
/// </remarks>
public static class UiTooltip
{
    /// <summary>Cuánto hay que quedarse quieto encima para que aparezca.</summary>
    /// <remarks>
    /// 350 ms es el intervalo clásico de la interfaz de escritorio, y está
    /// elegido contra el gesto que molesta: barrer la pantalla para llegar a un
    /// botón dura menos que eso, y detenerse a mirar algo dura más.
    /// </remarks>
    private const long DelayMs = 350;

    /// <summary>
    /// Ventana de gracia: con un cartel recién cerrado, el siguiente no espera.
    /// </summary>
    /// <remarks>
    /// Es lo que hace legible <b>recorrer</b> una fila de iconos. Quien ya está
    /// leyendo carteles no quiere esperar 350 ms por cada uno; quien está yendo
    /// a otra cosa hace rato que no ve ninguno, y para él la demora sigue viva.
    /// </remarks>
    private const long GraceMs = 500;

    private static Vector2 _cursor;
    private static string _texto = "";
    private static string _ayuda = "";
    private static Rect2 _ancla;

    /// <summary>El ancla que se venía señalando, para medir cuánto hace.</summary>
    private static Rect2 _anclaEstable;
    private static long _desdeMs;

    /// <summary>
    /// Cuándo se dibujó el último cartel. Arranca justo <b>afuera</b> de la
    /// ventana de gracia.
    /// </summary>
    /// <remarks>
    /// ⚠ Y no en <c>long.MinValue</c>, que es lo que parecía obvio: la resta
    /// <c>ahora - _ultimoVisibleMs</c> desborda ahí y da negativo, así que el
    /// primer cartel de la sesión salía sin esperar la demora. Con un valor
    /// apenas anterior a la ventana alcanza — el reloj arranca en cero al abrir
    /// el juego y solo sube.
    /// </remarks>
    private static long _ultimoVisibleMs = -GraceMs - 1;

    /// <summary>
    /// El último cuadro en el que <b>alguna</b> pantalla tuvo algo que decir.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Existe porque en un cuadro dibujan VARIAS pantallas.</b> El HUD
    /// vive en su capa y la preparación, el panel del defensor y el mercado en la
    /// de arriba: todas llaman <see cref="Begin"/> y <see cref="Draw"/>, una
    /// detrás de la otra, en el mismo cuadro. Con el cursor sobre la barra del
    /// HUD, la preparación llama <c>Begin</c> —que limpia— y su <c>Draw</c> no
    /// encuentra nada.</para>
    ///
    /// <para>Sin este contador, ese <c>Draw</c> vacío se leía como «el cursor
    /// salió de todo» y reiniciaba el reloj de la demora <b>cada cuadro</b>: la
    /// demora quedaba anulada durante toda la fase de preparación, que es
    /// justamente cuando más carteles se leen. Con él, olvidar solo pasa cuando
    /// nadie declaró nada en el cuadro entero.</para>
    /// </remarks>
    private static ulong _ultimoCuadroConTexto = ulong.MaxValue;

    // -------------------------------------------------------------------------
    //  El dedo
    // -------------------------------------------------------------------------

    /// <summary>Cuánto hay que mantener apretado para que salga el cartel.</summary>
    /// <remarks>
    /// Un poco más que la demora del mouse: acá el gesto que molesta no es barrer
    /// la pantalla sino el toque normal, y un toque decidido dura menos de 200 ms.
    /// Con 450 nadie lo dispara sin querer y nadie se cansa esperándolo.
    /// </remarks>
    private const long HoldMs = 450;

    /// <summary>Cuánto se puede mover el dedo sin que deje de ser «mantener».</summary>
    /// <remarks>
    /// En píxeles del lienzo. Tres veces el umbral de arrastre del tablero
    /// (<c>Game.UmbralArrastre</c>): el dedo tiembla más que el mouse, y un cartel
    /// que se cancela por el pulso obliga a volver a apoyar.
    /// </remarks>
    private const float TemblorMax = 24f;

    /// <summary>Desde cuándo está apoyado el dedo. −1 = no está apoyado.</summary>
    private static long _dedoDesdeMs = -1;

    private static Vector2 _dedoOrigen;

    /// <summary>Si el cartel quedó abierto después de soltar.</summary>
    private static bool _fijado;

    /// <summary>Si hay que tragarse el próximo soltar: el del toque que cerró el cartel.</summary>
    private static bool _tragarSoltar;

    /// <summary>
    /// Si el último dedo que se soltó estaba <b>leyendo</b> un cartel, no tocando.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Existe porque ese soltar NO se puede tragar.</b> El HUD arma el
    /// arrastre de la tarjeta al apretar y lo desarma al soltar; si el soltar no
    /// le llega, queda arrastrando un fantasma que pone un defensor en el próximo
    /// toque. Así que el soltar viaja, y las pantallas que actúan al soltar
    /// —elegir una ficha puesta, salir del fin de corrida— preguntan esto antes.</para>
    ///
    /// <para>Las que actúan al <b>apretar</b> no lo necesitan: todo lo que ahí
    /// cuesta algo —comprar, vender, elegir el Oro, sacar un perk— ya pide un
    /// segundo toque para confirmar.</para>
    /// </remarks>
    public static bool ReleaseWasHelp { get; private set; }

    /// <summary>
    /// Mira el dedo antes que ninguna pantalla. Devuelve <c>true</c> si el evento
    /// era para cerrar el cartel y no tiene que llegarle a nadie más.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>En el dedo no hay hover.</b> Godot convierte el toque en un
    /// mouse que queda quieto donde se tocó, así que el cartel de escritorio
    /// salía igual —a los 350 ms de cualquier toque, debajo del dedo, en letra de
    /// escritorio y sin forma de cerrarlo salvo tocando otra cosa, que además
    /// hacía lo suyo—. Con esto el cartel sale solo si se <b>pide</b>: mantener
    /// apretado.</para>
    ///
    /// <para>Mira los eventos de <b>mouse emulado</b> y no los de toque, que es lo
    /// que ya escuchan todas las pantallas. Así funciona igual con
    /// <c>--celular</c> en la PC.</para>
    /// </remarks>
    public static bool HandleTouch(InputEvent evento, Vector2 posicion)
    {
        if (!Device.IsTouch)
            return false;

        switch (evento)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                ReleaseWasHelp = false;

                // Con el cartel abierto, el toque es para cerrarlo y nada más. Si
                // pasara, el que toca afuera para cerrar compraría lo de abajo.
                if (_fijado)
                {
                    _fijado = false;
                    _dedoDesdeMs = -1;
                    _tragarSoltar = true;
                    return true;
                }

                _dedoDesdeMs = FineClock.Ms;
                _dedoOrigen = posicion;
                return false;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _dedoDesdeMs = -1;

                if (_tragarSoltar)
                {
                    _tragarSoltar = false;
                    return true;
                }

                return false;

            // Se movió de verdad: es un arrastre. Si el cartel ya había salido se
            // cierra — es el gesto de leer la tarjeta y después llevarla al mapa.
            case InputEventMouseMotion when _dedoDesdeMs >= 0
                                            && posicion.DistanceTo(_dedoOrigen) > TemblorMax:
                _dedoDesdeMs = -1;
                _fijado = false;
                ReleaseWasHelp = false;
                return false;
        }

        return false;
    }

    /// <summary>
    /// Arranca el cuadro. Se llama al principio del <c>_Draw</c> de la pantalla.
    /// </summary>
    /// <param name="cursor">
    /// La posición del mouse <b>en el mismo espacio que los rectángulos</b>. Para
    /// las pantallas eso es <c>GetGlobalMousePosition()</c>; el mundo, que está
    /// corrido y a escala adentro del marco, no usa esto.
    /// </param>
    public static void Begin(Vector2 cursor)
    {
        _cursor = cursor;
        _texto = "";
        _ayuda = "";
        _ancla = default;
    }

    /// <summary>Declara que este rectángulo se explica con este texto.</summary>
    /// <remarks>
    /// Gana el <b>último</b> que contenga al cursor. Como el orden de las
    /// llamadas es el orden de dibujo, el último es también el que está encima —
    /// que es exactamente el que el jugador cree estar señalando.
    /// </remarks>
    public static void Over(Rect2 r, string texto)
    {
        if (texto.Length == 0 || !r.HasPoint(_cursor))
            return;

        Anclar(r);
        _texto = texto;
    }

    /// <summary>
    /// La mitad de abajo del cartel: la regla del juego, para el que recién
    /// empieza. Se apaga en Opciones.
    /// </summary>
    /// <remarks>
    /// <para>Recibe la <b>clave</b> de idioma y no el texto ya resuelto, y eso es
    /// deliberado: así el que agrega una ayuda escribe una línea en
    /// <c>es.json</c> y otra en <c>en.json</c>, y no hay forma de dejar una
    /// explicación escrita a mano adentro de un <c>.cs</c> —que es texto del
    /// jugador viviendo donde la regla de oro 4 dice que no puede vivir.</para>
    ///
    /// <para>El rectángulo suele ser <b>el mismo</b> que el de un
    /// <see cref="Over"/>: los dos textos terminan en el mismo cartel.</para>
    /// </remarks>
    public static void Help(Rect2 r, string clave)
    {
        if (!Settings.ShowHelp || clave.Length == 0 || !r.HasPoint(_cursor))
            return;

        Anclar(r);
        _ayuda = Loc.Get(clave);
    }

    /// <summary>Un dato y su ayuda de un saque, sobre el mismo rectángulo.</summary>
    public static void Over(Rect2 r, string texto, string claveAyuda)
    {
        Over(r, texto);
        Help(r, claveAyuda);
    }

    /// <summary>
    /// Tira lo declarado y no dibuja nada este cuadro.
    /// </summary>
    /// <remarks>
    /// Para la pantalla que decide, ya empezado su dibujo, que este no es momento
    /// de explicar nada: el HUD mientras se arrastra una ficha, que lleva un
    /// muñeco pegado al cursor y taparía el cartel. Reemplaza al
    /// <see cref="Draw"/> que esa pantalla no va a llamar — sin esto el reloj de
    /// la demora queda contando un ancla que ya nadie está señalando.
    /// </remarks>
    public static void Cancel()
    {
        _texto = "";
        _ayuda = "";
        _ancla = default;
        _anclaEstable = default;
    }

    /// <summary>
    /// Cambia de ancla, tirando lo que se había declarado para la anterior.
    /// </summary>
    /// <remarks>
    /// ⚠ Sin esto, el dato de un elemento se mezcla con la ayuda de otro: las dos
    /// mitades se declaran por separado y nada obliga a que hablen del mismo
    /// rectángulo.
    /// </remarks>
    private static void Anclar(Rect2 r)
    {
        if (_ancla == r)
            return;

        _texto = "";
        _ayuda = "";
        _ancla = r;
    }

    /// <summary>
    /// Dibuja el cartel, si hay y si ya pasó la demora. Se llama al final del
    /// <c>_Draw</c>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Tiene dos formas y las decide el aparato.</b> Con mouse es un
    /// globo pegado a lo señalado (<see cref="CajaGlobo"/>); con el dedo es un
    /// popup grande en un lugar fijo (<see cref="CajaTactil"/>), que sale al
    /// mantener apretado y queda abierto hasta el próximo toque. Lo que se
    /// declara es lo mismo en los dos: ninguna pantalla sabe cuál se dibuja.</para>
    ///
    /// <para>⚠ El popup <b>no se dibuja sobre <paramref name="ci"/></b>: acá solo
    /// se decide si sale y se guarda qué dice. Lo pinta
    /// <see cref="TouchHelpOverlay"/>, en la capa de arriba de todo.</para>
    ///
    /// <para>Los dos crecen con <see cref="Settings.HelpScale"/>.</para>
    /// </remarks>
    public static void Draw(CanvasItem ci)
    {
        ulong cuadro = Engine.GetProcessFrames();

        if (_texto.Length == 0 && _ayuda.Length == 0)
        {
            // ⚠ Solo se olvida si NADIE declaró nada en este cuadro. Otra pantalla
            // de la misma capa puede haber dibujado su cartel hace tres líneas.
            if (_ultimoCuadroConTexto != cuadro)
                _anclaEstable = default;

            // Y el cartel fijado se cierra si lo que explicaba ya no está: una
            // pantalla que se fue. Con DOS cuadros de margen y no uno, porque en
            // un cuadro dibujan varias pantallas y el orden entre capas no está
            // garantizado — una vacía puede pasar antes que la que tiene texto.
            // Sin cerrarlo, el próximo toque se lo tragaría un cartel invisible.
            if (_fijado && cuadro - _ultimoCuadroConTexto > 1)
                _fijado = false;

            return;
        }

        _ultimoCuadroConTexto = cuadro;

        if (_ancla != _anclaEstable)
        {
            _anclaEstable = _ancla;
            _desdeMs = FineClock.Ms;
        }

        long ahora = FineClock.Ms;
        bool tactil = Device.IsTouch;

        if (tactil)
        {
            // En el dedo manda el mantener apretado, no la demora ni la gracia.
            if (!_fijado)
            {
                if (_dedoDesdeMs < 0 || ahora - _dedoDesdeMs < HoldMs)
                    return;

                _fijado = true;
                ReleaseWasHelp = true;
            }
        }
        else if (ahora - _desdeMs < DelayMs && ahora - _ultimoVisibleMs > GraceMs)
        {
            // ⚠ La gracia se mide contra el último cartel que se VIO, no contra el
            // último que se declaró. Marcarla antes de dibujar hacía que barrer la
            // pantalla —cuatro elementos rozados sin llegar a mostrar ninguno—
            // dejara la ventana de gracia abierta, y el quinto aparecía de golpe.
            return;
        }

        _ultimoVisibleMs = ahora;

        // En el dedo no se dibuja acá: se guarda, y lo pinta la capa de arriba de
        // todo (TouchHelpOverlay). Ver por qué ahí.
        if (tactil)
        {
            _popupTexto = _texto;
            _popupAyuda = _ayuda;
            _popupAncla = _ancla;
            _popupCuadro = cuadro;
            return;
        }

        Pintar(ci, _texto, _ayuda, _ancla, tactil: false);
    }

    private static string _popupTexto = "";
    private static string _popupAyuda = "";
    private static Rect2 _popupAncla;
    private static ulong _popupCuadro;

    /// <summary>
    /// Dibuja el popup del dedo. Lo llama solo <see cref="TouchHelpOverlay"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ Acepta lo declarado en el cuadro <b>anterior</b>: la capa de arriba
    /// puede dibujar antes que las pantallas en el mismo cuadro, y exigir el
    /// actual haría parpadear el cartel un cuadro sí y otro no.
    /// </remarks>
    public static void DrawTouchPopup(CanvasItem ci)
    {
        if (!_fijado || Engine.GetProcessFrames() - _popupCuadro > 1)
            return;

        Pintar(ci, _popupTexto, _popupAyuda, _popupAncla, tactil: true);
    }

    /// <summary>Arma la caja y escribe el cartel. Las dos formas pasan por acá.</summary>
    private static void Pintar(CanvasItem ci, string texto, string textoAyuda, Rect2 ancla, bool tactil)
    {
        // ⚠ LA MEDIDA DEPENDE DEL APARATO, y el tamaño de Opciones va encima.
        //
        // El lienzo es 1920x1080 con `keep`: en un teléfono de 1080 de alto un
        // píxel lógico es uno físico, y a 400 ppp los 14 del escritorio miden
        // menos de un milímetro. La base del dedo es ~2,4 veces la de la PC, que
        // es la distancia entre leer un monitor a 60 cm y un teléfono en la mano
        // medida en densidad, no en pulgadas.
        float escala = Settings.HelpScale / 100f;
        float @base = tactil ? 2.4f : 1f;

        int tamanoDato = Mathf.RoundToInt(14f * @base * escala);
        int tamanoAyuda = Mathf.RoundToInt(13f * @base * escala);
        float pad = 10f * @base * escala;
        float renglon = tamanoDato * 1.36f;
        float renglonAyuda = tamanoAyuda * 1.31f;

        // El ancho al que se corta la ayuda. Un cartel de 420 px entra en el
        // lienzo desde cualquier borde y son unas nueve palabras por renglón,
        // que es donde el ojo todavía encuentra el principio de la línea
        // siguiente sin buscarlo. Crece con la letra para seguir siendo nueve
        // palabras, hasta donde entre en el lienzo.
        float anchoAyuda = Mathf.Min(420f * @base * escala, Canvas.CanvasWidth - 64f - (pad * 2f));

        // ⚠ MULTILÍNEA, partiendo por el salto de línea.
        //
        // Antes era una sola línea, y alcanzaba mientras el cartel explicara un
        // icono —«daño», «cadencia»—. No alcanza para lo que trae encima un
        // bicho: ahí el cartel es una lista de dos o tres cosas distintas, y en
        // un renglón único quedaba una tira de 600 px que se sale de la ventana
        // y que además hay que leer buscando los separadores.
        string[] lineas = texto.Length == 0 ? [] : texto.Split('\n');

        // La ayuda es prosa y no una lista, así que se corta por ancho. El corte
        // se hace ACÁ y no con `Paragraph` porque hace falta saber cuántos
        // renglones son para poder dimensionar la caja antes de dibujarla.
        var ayuda = textoAyuda.Length == 0
            ? []
            : Envolver(textoAyuda, tamanoAyuda, anchoAyuda);

        // En el dedo, un renglón más al pie que dice cómo se cierra. Sin él, el
        // primer cartel que el jugador abre por accidente parece trabado.
        string cierre = tactil ? Loc.Get("ui.ayuda.tocar_para_cerrar") : "";
        int tamanoCierre = Mathf.RoundToInt(tamanoAyuda * 0.8f);
        float renglonCierre = cierre.Length > 0 ? tamanoCierre * 1.5f : 0f;

        float anchoTexto = UiStyle.Width(cierre, tamanoCierre);

        foreach (string linea in lineas)
            anchoTexto = Mathf.Max(anchoTexto, UiStyle.Width(linea, tamanoDato));

        foreach (string linea in ayuda)
            anchoTexto = Mathf.Max(anchoTexto, UiStyle.Width(linea, tamanoAyuda));

        bool separa = lineas.Length > 0 && ayuda.Count > 0;
        float hueco = 9f * (tactil ? @base * escala : 1f);

        float ancho = anchoTexto + (pad * 2f);
        float alto = (lineas.Length * renglon) + (ayuda.Count * renglonAyuda)
            + (separa ? hueco : 0f) + renglonCierre + (pad * 0.9f);

        var caja = tactil ? CajaTactil(ancla, ancho, alto) : CajaGlobo(ancla, ancho, alto);

        ci.DrawRect(caja, Palette.BackgroundMain.Alpha(tactil ? 0.985f : 0.97f));
        ci.DrawRect(caja, Palette.BorderLight, filled: false, width: tactil ? 3f : 1.5f);
        ci.Corners(caja, Palette.BorderLight.Alpha(0.9f), tactil ? 3f : 1.5f);

        // Una línea sigue yendo centrada; varias van a la izquierda, porque una
        // lista centrada no tiene margen contra el que alinear la vista. Con
        // ayuda debajo van siempre a la izquierda: la ayuda es un párrafo y un
        // título centrado sobre un párrafo alineado se lee torcido.
        var alineacion = lineas.Length == 1 && ayuda.Count == 0
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;

        float py = caja.Position.Y + (pad * 0.45f);

        for (int i = 0; i < lineas.Length; i++)
        {
            ci.Label(
                new Rect2(caja.Position.X + pad, py, caja.Size.X - (pad * 2f), renglon),
                lineas[i], tamanoDato, Palette.TextPrimary, alineacion);

            py += renglon;
        }

        if (separa)
        {
            ci.DrawRect(
                new Rect2(caja.Position.X + pad, py + (hueco * 0.45f), caja.Size.X - (pad * 2f), 1f),
                Palette.BorderNormal);

            py += hueco;
        }

        // La ayuda va en el gris apagado y un punto más chica: es la mitad que se
        // lee una vez y se deja de leer, y tiene que verse como tal al lado del
        // dato, que es el que se mira siempre.
        foreach (string linea in ayuda)
        {
            ci.Label(
                new Rect2(caja.Position.X + pad, py, caja.Size.X - (pad * 2f), renglonAyuda),
                linea, tamanoAyuda, Palette.TextMuted);

            py += renglonAyuda;
        }

        if (cierre.Length > 0)
        {
            ci.Label(
                new Rect2(caja.Position.X + pad, py, caja.Size.X - (pad * 2f), renglonCierre),
                cierre, tamanoCierre, Palette.TextDisabled, HorizontalAlignment.Right);
        }
    }

    /// <summary>El globo de escritorio: pegado a lo señalado.</summary>
    /// <remarks>
    /// <para>Va <b>arriba</b> de lo señalado y no abajo: abajo lo tapa el cursor,
    /// que es una flecha que apunta hacia arriba y a la izquierda. Si no entra
    /// arriba —el icono está pegado al borde superior— se da vuelta.</para>
    ///
    /// <para>Y se recorta contra el lienzo a los costados. Un cartel que se sale
    /// por la derecha es peor que uno corrido, porque la mitad que falta es
    /// justo el final de la frase.</para>
    /// </remarks>
    private static Rect2 CajaGlobo(Rect2 ancla, float ancho, float alto)
    {
        float x = ancla.Position.X + (ancla.Size.X / 2f) - (ancho / 2f);
        float y = ancla.Position.Y - alto - 6f;

        if (y < 4f)
            y = ancla.End.Y + 6f;

        x = Mathf.Clamp(x, 4f, Canvas.CanvasWidth - ancho - 4f);

        // Y también contra el borde de abajo: un cartel de cuatro renglones
        // colgado de algo que está en la última fila se sale por abajo, que es
        // donde el jugador no puede ni arrastrar la ventana para verlo.
        y = Mathf.Clamp(y, 4f, Canvas.CanvasHeight - alto - 4f);

        return new Rect2(x, y, ancho, alto);
    }

    /// <summary>
    /// El popup del dedo: centrado, en la mitad de la pantalla <b>opuesta</b> a
    /// lo señalado.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Nunca pegado a lo señalado.</b> Al tamaño que tiene que tener
    /// para leerse, un globo colgado del icono tapa media pantalla en un lugar
    /// distinto cada vez, y lo que tapa suele ser justo lo de al lado — la otra
    /// tarjeta que el jugador está comparando. Con dos lugares fijos el ojo
    /// aprende adónde ir, y del lado opuesto el dedo no lo tapa nunca.</para>
    /// </remarks>
    private static Rect2 CajaTactil(Rect2 ancla, float ancho, float alto)
    {
        const float Margen = 24f;

        bool abajo = ancla.GetCenter().Y < Canvas.CanvasHeight / 2f;

        ancho = Mathf.Min(ancho, Canvas.CanvasWidth - (Margen * 2f));
        alto = Mathf.Min(alto, Canvas.CanvasHeight - (Margen * 2f));

        float x = (Canvas.CanvasWidth - ancho) / 2f;
        float y = abajo ? Canvas.CanvasHeight - alto - Margen : Margen;

        return new Rect2(x, y, ancho, alto);
    }

    /// <summary>Corta un texto en renglones que entren en un ancho.</summary>
    /// <remarks>
    /// Respeta los saltos de línea que ya trae el texto: una ayuda puede ser dos
    /// frases separadas a propósito, y unirlas para volver a cortarlas por ancho
    /// perdería esa separación.
    /// </remarks>
    private static List<string> Envolver(string texto, int tamano, float ancho)
    {
        var salida = new List<string>();

        foreach (string parrafo in texto.Split('\n'))
        {
            string linea = "";

            foreach (string palabra in parrafo.Split(' '))
            {
                string prueba = linea.Length == 0 ? palabra : $"{linea} {palabra}";

                if (UiStyle.Width(prueba, tamano) <= ancho)
                {
                    linea = prueba;
                    continue;
                }

                if (linea.Length > 0)
                    salida.Add(linea);

                linea = palabra;
            }

            salida.Add(linea);
        }

        return salida;
    }
}
