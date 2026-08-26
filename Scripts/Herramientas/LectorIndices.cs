using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TowerOfArgentum.Herramientas;

/// <summary>Un cuerpo: cuatro grh, uno por dirección, y dónde va la cabeza.</summary>
public sealed record Cuerpo(int Id, int[] Direcciones, int OffsetCabezaX, int OffsetCabezaY);

/// <summary>Una cabeza o un casco: cuatro grh, uno por dirección.</summary>
public sealed record CuatroDirecciones(int Id, int[] Direcciones);

/// <summary>
/// Lee los índices binarios de Austral: <c>Personajes.ind</c> y
/// <c>Cabezas.ind</c>.
/// </summary>
/// <remarks>
/// <para>Formato, portado de <c>IndexLoader.cs</c> del cliente de Austral, que a
/// su vez es un port de <c>TileEngine.bas</c> de VB6:</para>
///
/// <code>
///   cabecera   263 bytes   descripción(255) + CRC(4) + palabra mágica(4)
///   cantidad   Int16       ⚠ dos bytes, no cuatro: en VB6 "Integer" es Int16
///
///   Personajes.ind, por entrada (20 bytes):
///     dir[1..4]      Int32 x4    grh de cada dirección
///     offsetCabezaX  Int16
///     offsetCabezaY  Int16
///
///   Cabezas.ind, por entrada (16 bytes):
///     dir[1..4]      Int32 x4
/// </code>
///
/// <para>⚠ <b>El <c>Int16</c> del contador es la trampa del formato.</b> Leerlo
/// como <c>Int32</c> —que es lo que uno escribiría— corre todo dos bytes y el
/// archivo entero sale con basura coherente: entradas con grh enormes que no
/// existen, y ningún error.</para>
///
/// <para>Las direcciones van 1=norte, 2=este, 3=oeste, 4=sur.</para>
/// </remarks>
public static class LectorIndices
{
    private const int TamanoCabecera = 255 + 4 + 4;

    private const string CarpetaDatos =
        @"D:\Github\Tidies24\Austral-Csharp-CLIENT-k\Resources\Data";

    /// <summary>Lee <c>Personajes.ind</c>: los cuerpos.</summary>
    public static Dictionary<int, Cuerpo> LeerCuerpos(string? ruta = null)
    {
        ruta ??= Path.Combine(CarpetaDatos, "Personajes.ind");

        var salida = new Dictionary<int, Cuerpo>();

        if (!File.Exists(ruta))
            return salida;

        using var r = new BinaryReader(File.OpenRead(ruta));

        r.ReadBytes(TamanoCabecera);
        short cantidad = r.ReadInt16();

        for (int i = 1; i <= cantidad; i++)
        {
            var direcciones = new int[4];

            for (int d = 0; d < 4; d++)
                direcciones[d] = r.ReadInt32();

            short offsetX = r.ReadInt16();
            short offsetY = r.ReadInt16();

            // Un cuerpo con la primera dirección en cero es un hueco del archivo,
            // no un cuerpo. Austral los saltea igual.
            if (direcciones[0] != 0)
                salida[i] = new Cuerpo(i, direcciones, offsetX, offsetY);
        }

        return salida;
    }

    /// <summary>Lee <c>Cabezas.ind</c> o <c>Cascos.ind</c>: mismo formato.</summary>
    public static Dictionary<int, CuatroDirecciones> LeerCabezas(string? ruta = null)
    {
        ruta ??= Path.Combine(CarpetaDatos, "Cabezas.ind");

        var salida = new Dictionary<int, CuatroDirecciones>();

        if (!File.Exists(ruta))
            return salida;

        using var r = new BinaryReader(File.OpenRead(ruta));

        r.ReadBytes(TamanoCabecera);
        short cantidad = r.ReadInt16();

        for (int i = 1; i <= cantidad; i++)
        {
            var direcciones = new int[4];

            for (int d = 0; d < 4; d++)
                direcciones[d] = r.ReadInt32();

            if (direcciones[0] != 0)
                salida[i] = new CuatroDirecciones(i, direcciones);
        }

        return salida;
    }

    /// <summary>
    /// Lee <c>Armas.dat</c>: las animaciones de arma por dirección.
    /// </summary>
    /// <remarks>
    /// <para>La clave es el <c>Anim</c> del objeto en <c>OBJ.DAT</c>, no su id:
    /// el Arco compuesto es el objeto 479 y su animación es la
    /// <c>[ARMA27]</c>.</para>
    ///
    /// <para>⚠ Es un <c>.dat</c> de texto, <b>Latin-1</b>, con las secciones a
    /// veces en mayúscula (<c>[ARMA27]</c>) y a veces no (<c>[Arma1]</c>). El
    /// parser compara ignorando mayúsculas por eso.</para>
    /// </remarks>
    public static Dictionary<int, CuatroDirecciones> LeerArmas(string? ruta = null)
    {
        ruta ??= Path.Combine(CarpetaDatos, "Armas.dat");
        return LeerDatDeDirecciones(ruta, "ARMA");
    }

    /// <summary>Lee <c>Escudos.dat</c>. Mismo formato que <c>Armas.dat</c>.</summary>
    public static Dictionary<int, CuatroDirecciones> LeerEscudos(string? ruta = null)
    {
        ruta ??= Path.Combine(CarpetaDatos, "Escudos.dat");
        return LeerDatDeDirecciones(ruta, "ESCUDO");
    }

    private static Dictionary<int, CuatroDirecciones> LeerDatDeDirecciones(string ruta, string prefijo)
    {
        var salida = new Dictionary<int, CuatroDirecciones>();

        if (!File.Exists(ruta))
            return salida;

        string[] lineas = File.ReadAllLines(ruta, Encoding.Latin1);

        int id = -1;
        var direcciones = new int[4];

        void Cerrar()
        {
            if (id > 0 && direcciones[0] != 0)
                salida[id] = new CuatroDirecciones(id, (int[])direcciones.Clone());

            System.Array.Clear(direcciones);
        }

        foreach (string cruda in lineas)
        {
            string linea = cruda.Trim();

            if (linea.Length == 0 || linea[0] == '\'' || linea[0] == ';')
                continue;

            if (linea[0] == '[')
            {
                Cerrar();

                int cierre = linea.IndexOf(']');
                string etiqueta = cierre > 0 ? linea[1..cierre] : linea[1..];

                id = etiqueta.StartsWith(prefijo, System.StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(etiqueta.AsSpan(prefijo.Length), out int n) ? n : -1;

                continue;
            }

            int igual = linea.IndexOf('=');

            if (igual <= 0)
                continue;

            string clave = linea[..igual].Trim();

            // ⚠ El valor puede traer un comentario pegado: «Dir1=4719 'norte».
            // Sin cortarlo, el int.TryParse falla y la dirección queda en cero, y
            // un cuerpo con Dir1 en cero se descarta entero.
            string valor = linea[(igual + 1)..].Trim();
            int comentario = valor.IndexOf('\'');

            if (comentario >= 0)
                valor = valor[..comentario].Trim();

            if (!clave.StartsWith("Dir", System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(clave.AsSpan(3), out int d) && d is >= 1 and <= 4
                && int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int grh))
            {
                direcciones[d - 1] = grh;
            }
        }

        Cerrar();
        return salida;
    }
}
