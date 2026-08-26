using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum;

/// <summary>
/// El suelo del área, horneado una vez en un solo lienzo.
/// </summary>
/// <remarks>
/// <para><b>Se pega en CPU y se dibuja una vez por frame.</b> El suelo no cambia
/// nunca durante un área, así que se arma una textura de 1920×1080 al entrar y
/// después es <b>un</b> <c>DrawTexture</c> por frame.</para>
///
/// <para>⚠ <b>Los bloques se pegan enteros, NO se pican en tiles de 128.</b>
/// Probado y descartado: cada <c>piso.*</c> es una foto de 512×512 con manchas
/// orgánicas —pasto con calvas de tierra— y no cuatro por cuatro tiles
/// independientes. Sorteando cada 128 sale un damero de verde y marrón que se ve
/// artificial de lejos. Pegando el bloque entero las manchas quedan como las
/// dibujaron.</para>
///
/// <para>La variedad sale de otro lado: se sortea <b>qué bloque</b> va en cada
/// casillero de 512 y <b>en qué orientación</b>. Espejar una textura que ya es
/// contínua la deja contínua, así que cuatro bloques de pasto rinden dieciséis
/// variantes y el patrón de repetición desaparece.</para>
///
/// <para>⚠ <b>Esto no entra al hash de datos y no toca la simulación.</b> Es
/// decoración: el mismo layout se ve distinto en cada área y la corrida da
/// exactamente el mismo resultado. Por eso el sorteo usa un chorro de RNG propio,
/// derivado de la seed pero separado del de combate y del de mapa.</para>
/// </remarks>
public static class PisoHorneado
{
    /// <summary>
    /// Constante que separa el chorro de RNG del piso de todos los demás.
    /// </summary>
    /// <remarks>
    /// Misma idea que <c>Layouts.RngDeMapa</c>: si el piso sacara números del
    /// mismo chorro que el combate, agregar una textura cambiaría el resultado de
    /// la corrida. Con la seed mezclada acá, no.
    /// </remarks>
    private const ulong SemillaDePiso = 0xB1050DEC0A115EEDUL;

    /// <summary>
    /// Qué paleta le toca a cada área. Índice 0 = área 1.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Faltan paletas.</b> Hay pisos para seis de las ocho áreas: el
    /// pantano de la 3 y el camino de tierra de la 4 no están en
    /// <c>graficos.json</c>, así que las dos van con pasto, y la <b>nieve de la 7
    /// está rota en la fuente</b> —la hoja 10150 de Austral es blanco con manchas
    /// negras, no es piso— así que Nix va con piedra hasta que aparezca otra.
    /// <c>piso.minas</c> y <c>piso.dungeonNewbie</c> están traídos y sin usar.</para>
    ///
    /// <para>Esta tabla es <b>solo del cliente</b> y a propósito: es arte, no
    /// balance. Ponerla en <c>Datos\*.json</c> la metería en el hash de
    /// <c>/run/start</c> y cambiar una textura invalidaría las corridas viejas.</para>
    /// </remarks>
    private static readonly string[][] _paletas =
    [
        ["piso.pastoA", "piso.pastoB", "piso.pastoC", "piso.pastoD"],  // 1 Ullathorpe
        ["piso.pastoA", "piso.pastoB", "piso.pastoC", "piso.pastoD"],  // 2 Norte de Ullathorpe
        ["piso.pastoA", "piso.pastoC"],                                // 3 Suburbios — falta pantano
        ["piso.pastoB", "piso.pastoD"],                                // 4 Bosque — falta pastoCamino
        ["piso.dungeon"],                                              // 5 Catacumbas
        ["piso.desierto"],                                             // 6 Desierto Rinkel
        ["piso.nieveA", "piso.nieveB", "piso.nieveC", "piso.nieveD"],  // 7 Nix
        ["piso.lava"],                                                 // 8 Dungeon Dragon
    ];

    /// <summary>
    /// Con qué se pinta el camino de cada área. Índice 0 = área 1.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Esto es un suplente.</b> Austral tiene tiles de camino de
    /// verdad —con borde, esquina y empalme— y todavía no están traídos. Hasta
    /// que lleguen, el camino es <b>otra textura de piso</b> estampada sobre la
    /// ruta con los bordes difuminados: no tiene borde dibujado, pero se lee como
    /// un sendero gastado y no como una línea de color.</para>
    ///
    /// <para>Cuando aparezcan los tiles, lo único que cambia es de dónde sale la
    /// textura: el estampado, la máscara y el horneado quedan igual.</para>
    /// </remarks>
    private static readonly string[] _caminos =
    [
        "piso.minas",           // 1 Ullathorpe — tierra sobre pasto
        "piso.minas",           // 2 Norte de Ullathorpe
        "piso.minas",           // 3 Suburbios
        "piso.minas",           // 4 Bosque
        "piso.dungeonNewbie",   // 5 Catacumbas — mármol sobre piedra oscura
        "piso.dungeon",         // 6 Desierto — piedra sobre arena
        "piso.minas",           // 7 Nix — roca clara: la piedra oscura sobre nieve se lee negra
        "piso.dungeon",         // 8 Dungeon Dragon — piedra sobre lava
    ];

    /// <summary>
    /// Con qué tono se recolorea el camino de cada área, en milésimas. 0 = tal cual.
    /// </summary>
    /// <remarks>
    /// <para>La roca de minas es <b>naranja</b>, y sobre pasto se lee como lava,
    /// no como tierra. Recolorearla por luminancia le deja el dibujo —las vetas,
    /// las piedritas, el relieve— y le cambia el tono a marrón de tierra.</para>
    ///
    /// <para>Es un tinte, no un filtro de color plano: lo que se conserva es el
    /// detalle, que es lo único que hace que un camino no parezca una faja
    /// pintada encima.</para>
    /// </remarks>
    private static readonly (int Rojo, int Verde, int Azul)[] _tintes =
    [
        (1150, 900, 650),  // 1 Ullathorpe — tierra
        (1150, 900, 650),  // 2 Norte de Ullathorpe
        (1000, 880, 700),  // 3 Suburbios — tierra más apagada, de pantano
        (1150, 900, 650),  // 4 Bosque
        (620, 660, 740),   // 5 Catacumbas — el mármol, bajado a piedra gris azulada
        (0, 0, 0),         // 6 Desierto
        (1400, 1500, 1700), // 7 Nix — piedra fría, para que se despegue de la nieve
        (0, 0, 0),         // 8 Dungeon Dragon
    ];

    private static Texture2D? _horneado;
    private static int _areaHorneada;
    private static ulong _seedHorneada;
    private static Mapa? _mapaHorneado;

    /// <summary>
    /// El suelo de un área con su camino, horneado. Una vez por área y corrida.
    /// </summary>
    /// <returns>
    /// <c>null</c> si falta el arte. Quien dibuja sigue adelante sin piso, que es
    /// lo que pasaba antes de que existiera esto.
    /// </returns>
    public static Texture2D? Para(int area, ulong seed, Mapa? mapa = null)
    {
        if (_horneado is not null
            && _areaHorneada == area
            && _seedHorneada == seed
            && ReferenceEquals(_mapaHorneado, mapa))
        {
            return _horneado;
        }

        var lienzo = Pintar(area, seed, mapa);

        _horneado = lienzo is null ? null : ImageTexture.CreateFromImage(lienzo);
        _areaHorneada = area;
        _seedHorneada = seed;
        _mapaHorneado = mapa;

        return _horneado;
    }

    /// <summary>Tira lo horneado. Al empezar otra corrida.</summary>
    public static void Olvidar()
    {
        _horneado = null;
        _areaHorneada = 0;
        _seedHorneada = 0;
        _mapaHorneado = null;
    }

    /// <summary>
    /// El suelo de un área, como imagen. Para la herramienta que lo saca a PNG.
    /// </summary>
    public static Image? Imagen(int area, ulong seed, Mapa? mapa = null)
        => Pintar(area, seed, mapa);

    private static Image? Pintar(int area, ulong seed, Mapa? mapa)
    {
        var suelo = Tejer(Bloques(area), seed, area);

        if (suelo is null)
        {
            GD.PushWarning($"Área {area}: no hay paleta de piso. El mundo se dibuja sin suelo.");
            return null;
        }

        if (mapa is null)
            return suelo;

        var camino = Tejer(BloquesDeCamino(area), seed, area + 100);

        if (camino is null)
            return suelo;

        var tinte = area >= 1 && area <= _tintes.Length ? _tintes[area - 1] : default;

        if (tinte.Rojo > 0)
            camino = Recolorear(camino, tinte);

        return Estampar(suelo, camino, Mascara(mapa), Ruido(seed));
    }

    /// <summary>
    /// Cubre el lienzo repitiendo bloques: sorteados, espejados y trabados.
    /// </summary>
    private static Image? Tejer(
        System.Collections.Generic.List<Image> bloques, ulong seed, int veta)
    {
        if (bloques.Count == 0)
            return null;

        var lienzo = Image.CreateEmpty(
            Escala.LienzoAncho, Escala.LienzoAlto, false, Image.Format.Rgba8);

        // La veta entra a la semilla para que dos áreas con la misma paleta —el
        // pasto de la 1 y el de la 2— no salgan pintadas idénticas, y para que el
        // camino no repita el mismo sorteo que el suelo de abajo.
        var rng = new Aleatorio(seed ^ SemillaDePiso ^ ((ulong)veta * 0x9E3779B97F4A7C15UL));

        int paso = bloques[0].GetWidth();
        int pasoAlto = bloques[0].GetHeight();

        for (int y = 0, fila = 0; y < Escala.LienzoAlto; y += pasoAlto, fila++)
        {
            // Filas trabadas, como ladrillos. Sin esto las juntas de los bloques
            // forman una cruz cada 512 px que el ojo encuentra enseguida; corridas
            // media textura, las juntas verticales no se alinean nunca.
            int desfase = (fila % 2) * (paso / 2);

            for (int x = -desfase; x < Escala.LienzoAncho; x += paso)
            {
                var bloque = bloques[rng.Indice(bloques.Count)];

                // La columna trabada arranca antes del borde: se recorta por
                // izquierda. Y el lienzo no es múltiplo del bloque —1920 / 512 da
                // 3,75—, así que la última columna y la última fila también salen
                // a medias.
                int desdeX = x < 0 ? -x : 0;
                int hastaX = x < 0 ? 0 : x;

                int ancho = System.Math.Min(bloque.GetWidth() - desdeX, Escala.LienzoAncho - hastaX);
                int alto = System.Math.Min(bloque.GetHeight(), Escala.LienzoAlto - y);

                if (ancho <= 0 || alto <= 0)
                    continue;

                lienzo.BlitRect(bloque, new Rect2I(desdeX, 0, ancho, alto), new Vector2I(hastaX, y));
            }
        }

        return lienzo;
    }

    /// <summary>
    /// Dónde va el camino, como imagen en gris del tamaño del lienzo.
    /// </summary>
    /// <remarks>
    /// <para>Se dibuja <b>chica y se agranda</b>: una casilla por media celda,
    /// 40×22, y de ahí a 1920×1080 con interpolación bilineal. Ese estirón es lo
    /// que difumina el borde: sale una rampa de medio celda en vez del escalón de
    /// 96 px que dejaría pintar celda por celda, y el camino deja de verse como
    /// una fila de cuadrados.</para>
    ///
    /// <para>La ruta se recorre por distancia y no por vértices porque los tramos
    /// en diagonal —si algún día hay uno— pasan por celdas que ningún vértice
    /// nombra.</para>
    /// </remarks>
    private static Image Mascara(Mapa mapa)
    {
        const int PorCelda = 2;

        var chica = Image.CreateEmpty(
            mapa.Ancho * PorCelda, mapa.Alto * PorCelda, false, Image.Format.L8);

        var blanco = new Color(1, 1, 1);

        // Medio paso de celda: más fino no agrega nada —la casilla de la máscara
        // mide media celda— y más grueso se saltea esquinas.
        for (long d = 0; d <= mapa.Ruta.Largo; d += Enteros.MilesimasPorCelda / 2)
        {
            var celda = mapa.Ruta.CeldaEn(d);

            for (int j = 0; j < PorCelda; j++)
            {
                for (int i = 0; i < PorCelda; i++)
                {
                    int x = (celda.X * PorCelda) + i;
                    int y = (celda.Y * PorCelda) + j;

                    if (x >= 0 && y >= 0 && x < chica.GetWidth() && y < chica.GetHeight())
                        chica.SetPixel(x, y, blanco);
                }
            }
        }

        chica.Resize(Escala.LienzoAncho, Escala.LienzoAlto, Image.Interpolation.Bilinear);
        return chica;
    }

    /// <summary>
    /// Le cambia el tono a una textura sin perderle el dibujo.
    /// </summary>
    /// <remarks>
    /// Se saca la luminancia de cada píxel —30 % rojo, 59 % verde, 11 % azul, la
    /// cuenta de toda la vida— y se la vuelve a pintar con el tono pedido. El
    /// relieve, las vetas y las piedritas quedan; el color cambia entero.
    /// </remarks>
    private static Image Recolorear(Image textura, (int Rojo, int Verde, int Azul) tono)
    {
        byte[] px = textura.GetData();

        for (int p = 0; p + 3 < px.Length; p += 4)
        {
            int luz = ((px[p] * 30) + (px[p + 1] * 59) + (px[p + 2] * 11)) / 100;

            px[p] = (byte)System.Math.Min(255, luz * tono.Rojo / 1000);
            px[p + 1] = (byte)System.Math.Min(255, luz * tono.Verde / 1000);
            px[p + 2] = (byte)System.Math.Min(255, luz * tono.Azul / 1000);
        }

        return Image.CreateFromData(
            textura.GetWidth(), textura.GetHeight(), false, Image.Format.Rgba8, px);
    }

    /// <summary>
    /// Ruido para deformar el borde del camino. Del tamaño del lienzo, en gris.
    /// </summary>
    /// <remarks>
    /// <para>Sin esto el camino sale con forma de <b>cápsula</b>: dos rectas y dos
    /// puntas redondas, que a simple vista se lee como un cable y no como un
    /// sendero. Corriendo la máscara con ruido antes de mezclar, el borde queda
    /// irregular en toda su largo.</para>
    ///
    /// <para>Tres octavas y frecuencia baja: una sola octava ondula parejo —queda
    /// como un río— y lo que se busca es que haya mordidas grandes y mordiscos
    /// chicos a la vez.</para>
    ///
    /// <para>La semilla sale de la corrida, así que <b>dos corridas con la misma
    /// seed dibujan el mismo borde</b>. Es decoración igual: no entra al hash.</para>
    /// </remarks>
    private static Image Ruido(ulong seed)
    {
        var ruido = new FastNoiseLite
        {
            Seed = (int)(seed & 0x7FFFFFFF),
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.012f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 3,
        };

        return ruido.GetImage(Escala.LienzoAncho, Escala.LienzoAlto);
    }

    /// <summary>
    /// Mezcla el camino sobre el suelo por altura, no por transparencia.
    /// </summary>
    /// <remarks>
    /// <para><b>Mezclar lineal —<c>suelo·(1−a) + camino·a</c>— da un degradé</b>, y
    /// un camino de verdad no tiene degradé: tiene matas de pasto metidas adentro
    /// de la tierra y piedritas sueltas afuera. Acá se usa la <b>luminancia de
    /// cada textura como si fuera altura</b> y en la franja del borde gana la más
    /// alta, así que cada mata que sobresale asoma sola por encima del camino. Es
    /// el <i>height blend</i> de <i>Advanced Terrain Texture Splatting</i>.</para>
    ///
    /// <para>Y es <b>genérico</b>: no sabe qué texturas está mezclando. Sirve para
    /// pasto contra tierra, nieve contra piedra y para las que se traigan
    /// después, sin tocar un número.</para>
    ///
    /// <para>Se trabaja sobre los bytes crudos y no con <c>SetPixel</c>: son dos
    /// millones de píxeles, y llamar al motor una vez por cada uno tarda segundos
    /// en algo que tiene que ser instantáneo al entrar a un área.</para>
    /// </remarks>
    private static Image Estampar(Image suelo, Image camino, Image mascara, Image ruido)
    {
        byte[] abajo = suelo.GetData();
        byte[] arriba = camino.GetData();
        byte[] alfa = mascara.GetData();
        byte[] grano = ruido.GetData();

        // Cuánto pesa la altura contra la máscara, en milésimas. Es lo que decide
        // cuánto puede meterse una mata de pasto adentro del camino.
        const int Relieve = 700;

        // Ancho de la franja donde las dos texturas se entrelazan. Más chico, el
        // borde queda filoso; más grande, vuelve el degradé.
        const int Franja = 300;

        // Cuánto corre el ruido el borde, en unidades de máscara. La rampa mide
        // 255 sobre media celda, así que 80 son unos 15 px de mordida.
        const int Mordida = 80;

        for (int i = 0, p = 0; i < alfa.Length && p + 3 < abajo.Length; i++, p += 4)
        {
            int a = alfa[i];

            if (a > 0 && a < 255)
            {
                a += ((grano.Length > i ? grano[i] : 128) - 128) * Mordida / 128;
                a = a < 0 ? 0 : a > 255 ? 255 : a;
            }

            if (a == 0)
                continue;

            if (a == 255)
            {
                abajo[p] = arriba[p];
                abajo[p + 1] = arriba[p + 1];
                abajo[p + 2] = arriba[p + 2];
                continue;
            }

            int luzAbajo = ((abajo[p] * 30) + (abajo[p + 1] * 59) + (abajo[p + 2] * 11)) / 100;
            int luzArriba = ((arriba[p] * 30) + (arriba[p + 1] * 59) + (arriba[p + 2] * 11)) / 100;

            // Peso de la máscara y altura de la textura, sumados: la máscara dice
            // dónde debería ir cada una y la altura decide quién gana en el borde.
            int altoAbajo = ((255 - a) * 1000 / 255) + (luzAbajo * Relieve / 255);
            int altoArriba = (a * 1000 / 255) + (luzArriba * Relieve / 255);

            int tope = System.Math.Max(altoAbajo, altoArriba) - Franja;

            int pesoAbajo = System.Math.Max(altoAbajo - tope, 0);
            int pesoArriba = System.Math.Max(altoArriba - tope, 0);
            int total = pesoAbajo + pesoArriba;

            abajo[p] = (byte)(((abajo[p] * pesoAbajo) + (arriba[p] * pesoArriba)) / total);
            abajo[p + 1] = (byte)(((abajo[p + 1] * pesoAbajo) + (arriba[p + 1] * pesoArriba)) / total);
            abajo[p + 2] = (byte)(((abajo[p + 2] * pesoAbajo) + (arriba[p + 2] * pesoArriba)) / total);
        }

        return Image.CreateFromData(
            Escala.LienzoAncho, Escala.LienzoAlto, false, Image.Format.Rgba8, abajo);
    }

    /// <summary>
    /// Los bloques sorteables del área: cada textura en sus cuatro orientaciones.
    /// </summary>
    /// <remarks>
    /// <para>Espejar sale gratis y multiplica por cuatro la variedad. Una textura
    /// que tilea sin costura sigue tileando espejada —el borde izquierdo espejado
    /// es el derecho—, así que no hay que dibujar nada nuevo.</para>
    ///
    /// <para>Las hojas se cargan una vez por <c>fileNum</c> aunque la paleta tenga
    /// cuatro bloques: los cuatro pastos viven en la misma hoja.</para>
    /// </remarks>
    private static System.Collections.Generic.List<Image> Bloques(int area)
        => Bloques(area < 1 || area > _paletas.Length ? [] : _paletas[area - 1]);

    private static System.Collections.Generic.List<Image> BloquesDeCamino(int area)
        => Bloques(area < 1 || area > _caminos.Length ? [] : [_caminos[area - 1]]);

    private static System.Collections.Generic.List<Image> Bloques(string[] nombres)
    {
        var salida = new System.Collections.Generic.List<Image>();
        var hojas = new System.Collections.Generic.Dictionary<int, Image>();

        foreach (string nombre in nombres)
        {
            var datos = CargadorGraficos.Datos(nombre);

            if (datos is null)
            {
                GD.PushWarning($"Falta «{nombre}» en graficos.json.");
                continue;
            }

            if (!hojas.TryGetValue(datos.FileNum, out var hoja))
            {
                var cargada = CargadorGraficos.ImagenDeHoja(datos.FileNum);

                if (cargada is null)
                    continue;

                // BlitRect exige el mismo formato en las dos imágenes, y las hojas
                // de Austral no siempre vienen en RGBA8.
                if (cargada.GetFormat() != Image.Format.Rgba8)
                    cargada.Convert(Image.Format.Rgba8);

                hoja = cargada;
                hojas[datos.FileNum] = hoja;
            }

            var region = new Rect2I(datos.X, datos.Y, datos.Ancho, datos.Alto);

            for (int giro = 0; giro < 4; giro++)
            {
                var copia = hoja.GetRegion(region);

                if ((giro & 1) != 0)
                    copia.FlipX();

                if ((giro & 2) != 0)
                    copia.FlipY();

                salida.Add(copia);
            }
        }

        return salida;
    }
}
