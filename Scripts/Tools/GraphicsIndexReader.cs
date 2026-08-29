using System.Collections.Generic;
using System.IO;

namespace TowerOfArgentum.Tools;

/// <summary>Una entrada de <c>Graficos.ind</c>: o una región, o una animación.</summary>
/// <remarks>
/// Un grh de <c>numFrames == 1</c> apunta a una <b>región</b> de una hoja. Uno de
/// <c>numFrames > 1</c> no apunta a ninguna hoja: apunta a <b>otros grh</b>, los
/// cuadros de la animación. Por eso resolver un grh hasta el archivo puede pedir
/// dos saltos, y por eso la skill del proyecto se llama "resolver la cadena
/// <c>grh → frames[] → fileNum</c>".
/// </remarks>
public sealed record GrhEntry(
    int Grh,
    int NumFrames,
    int FileNum,
    int X,
    int Y,
    int Width,
    int Height,
    int[] Frames,
    float Speed)
{
    public bool IsAnimation => NumFrames > 1;
}

/// <summary>
/// Lee <c>Graficos.ind</c> de Austral.
/// </summary>
/// <remarks>
/// <para>Formato, verificado byte por byte contra el archivo real:</para>
///
/// <code>
///   version   Int32
///   cantidad  Int32
///   por cada grh:
///     grh        Int32
///     numFrames  Int16
///     si numFrames > 1:
///       frames[]   Int32 x numFrames
///       velocidad  Single
///     si numFrames == 1:
///       fileNum    Int32
///       x          Int16
///       y          Int16
///       ancho      Int16
///       alto       Int16
/// </code>
///
/// <para>⚠ <b>Las coordenadas están en 1x.</b> Lo dice el README de
/// <c>asset-legacy</c>: "Graficos.ind continúa usando esos números y coordenadas
/// 1x". Las hojas de <c>graficos\</c> son x4, así que <b>hay que multiplicar por
/// 4</b>. Copiar las coordenadas tal cual recorta la esquina superior izquierda
/// del sprite y nadie entiende por qué.</para>
///
/// <para>Este archivo se lee <b>una vez, offline</b>, para armar el índice
/// propio. No entra al juego: <c>Resources\graficos.json</c> es el índice del TD y
/// no depende de nada de Austral.</para>
/// </remarks>
public static class GraphicsIndexReader
{
    /// <summary>La escala de las hojas HD contra las coordenadas del .ind.</summary>
    public const int Canvas = 4;

    private const string RutaPorDefecto =
        @"D:\Github\Tidies24\Austral-Csharp-CLIENT-k\Resources\Data\Graficos.ind";

    public static Dictionary<int, GrhEntry> Read(string? ruta = null)
    {
        ruta ??= RutaPorDefecto;

        var salida = new Dictionary<int, GrhEntry>();

        if (!File.Exists(ruta))
            return salida;

        using var f = File.OpenRead(ruta);
        using var r = new BinaryReader(f);

        _ = r.ReadInt32();               // versión
        int cantidad = r.ReadInt32();

        // El contador dice cuántos grh hay, pero los índices no son densos: el
        // bucle corta por fin de archivo y no por el contador, porque un .ind de
        // otra versión podría mentir y dejar el lector leyendo basura.
        for (int i = 0; i < cantidad && f.Position < f.Length - 6; i++)
        {
            int grh = r.ReadInt32();
            short numFrames = r.ReadInt16();

            if (numFrames <= 0)
                continue;

            if (numFrames > 1)
            {
                var frames = new int[numFrames];

                for (int j = 0; j < numFrames; j++)
                    frames[j] = r.ReadInt32();

                float velocidad = r.ReadSingle();

                salida[grh] = new GrhEntry(grh, numFrames, 0, 0, 0, 0, 0, frames, velocidad);
            }
            else
            {
                int fileNum = r.ReadInt32();
                short x = r.ReadInt16();
                short y = r.ReadInt16();
                short ancho = r.ReadInt16();
                short alto = r.ReadInt16();

                salida[grh] = new GrhEntry(grh, 1, fileNum, x, y, ancho, alto, [], 0f);
            }
        }

        return salida;
    }

    /// <summary>
    /// Resuelve un grh hasta la región concreta, siguiendo la animación si hace
    /// falta.
    /// </summary>
    /// <remarks>
    /// Devuelve el <b>primer cuadro</b> de una animación. Para el ícono de un
    /// ítem alcanza; para una criatura que camina hay que traer todos los cuadros,
    /// y ahí importa que estén en la misma hoja.
    /// </remarks>
    public static GrhEntry? Resolve(Dictionary<int, GrhEntry> indice, int grh, int saltos = 0)
    {
        // Tope de saltos: un .ind con una animación que se apunta a sí misma haría
        // un bucle infinito, y esto corre sobre un archivo de otro proyecto.
        if (saltos > 4 || !indice.TryGetValue(grh, out var entrada))
            return null;

        if (!entrada.IsAnimation)
            return entrada;

        return entrada.Frames.Length == 0
            ? null
            : Resolve(indice, entrada.Frames[0], saltos + 1);
    }

    /// <summary>Todos los cuadros de un grh, ya resueltos a regiones.</summary>
    public static List<GrhEntry> Frames(Dictionary<int, GrhEntry> indice, int grh)
    {
        var salida = new List<GrhEntry>();

        if (!indice.TryGetValue(grh, out var entrada))
            return salida;

        if (!entrada.IsAnimation)
        {
            salida.Add(entrada);
            return salida;
        }

        foreach (int f in entrada.Frames)
        {
            var cuadro = Resolve(indice, f);

            if (cuadro is not null)
                salida.Add(cuadro);
        }

        return salida;
    }
}
