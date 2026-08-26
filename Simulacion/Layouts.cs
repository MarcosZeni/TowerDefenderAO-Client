namespace TowerOfArgentum.Simulacion;

/// <summary>
/// De dónde sale el mapa de una corrida.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Ya no hay layouts escritos en código.</b> Antes vivían acá tres
/// formas —serpiente, espiral y peine— con coordenadas a mano para un campo de
/// 30×16. Ahora se dibujan con <c>Escenas/EditorLayouts.tscn</c>, se guardan como
/// ASCII en <c>Datos/layouts/</c> y entran por <see cref="DatosBalance.Layouts"/>,
/// que es lo que también recibe el servidor al revalidar.</para>
///
/// <para>El cambio no fue solo de forma: el campo pasó a <b>20×11</b> —la vista
/// de Argentum con celdas cuadradas— y las rutas de 74 celdas a 25-33.</para>
///
/// <para>Lo que queda acá es la <b>elección</b>, que necesita su propio stream de
/// azar.</para>
/// </remarks>
public static class Layouts
{
    /// <summary>
    /// Deriva el RNG del mapa a partir de la seed de la corrida.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Stream aparte del de combate, y esto no es paranoia.</b> La seed
    /// alimenta una secuencia de tiradas y el servidor rehace esa misma secuencia.
    /// Si elegir el mapa consumiera del mismo stream, agregar mañana una tirada
    /// más —otro layout, una variante de ambientación— correría todas las tiradas
    /// de combate que vienen después, y las corridas viejas dejarían de
    /// reproducirse sin que nadie tocara el combate.</para>
    ///
    /// <para>La constante no significa nada: solo tiene que ser fija y distinta de
    /// las otras.</para>
    /// </remarks>
    public static Aleatorio RngDeMapa(ulong seed) => new(seed ^ 0x1A5E5EEDUL);

    /// <summary>El mapa que le toca a una corrida.</summary>
    public static Mapa Elegir(ulong seed, DatosBalance datos)
        => datos.LayoutElegido(RngDeMapa(seed));
}
