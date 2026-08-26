using System.IO;
using System.IO.Compression;

namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Serializa el log de acciones.
/// </summary>
/// <remarks>
/// <para>Este formato hace dos trabajos con el mismo código:</para>
///
/// <list type="number">
/// <item><b>Es lo que viaja a <c>/run/finish</c></b>, para que el servidor pueda
/// revalidar la corrida re-simulándola.</item>
/// <item><b>Es el archivo de guardado.</b> Sale gratis del determinismo: guardar
/// es escribir esto a disco, y cargar es re-simular desde el tick 0 a máxima
/// velocidad. No hay que serializar el estado del juego, no hay formato de save
/// que versionar aparte, y no puede quedar desincronizado del código — si el save
/// carga, es porque el código lo reprodujo exactamente.</item>
/// </list>
///
/// <para><b>Delta del tick y varint.</b> Las acciones están ordenadas por tick y
/// vienen en ráfagas: guardar el tick absoluto en 4 bytes desperdicia tres. Con
/// el delta contra la anterior, casi todas las acciones entran en 1 byte de tick.
/// Con 100 oleadas y decenas de miles de acciones, eso es la diferencia entre un
/// log que entra en el <c>MEDIUMBLOB</c> y uno que hay que truncar.</para>
/// </remarks>
public static class LogBinario
{
    /// <summary>
    /// Versión del formato, primer byte del blob.
    /// </summary>
    /// <remarks>
    /// Va <b>afuera</b> de la compresión, para poder leerlo sin descomprimir. El
    /// día que cambie el formato, un log viejo tiene que seguir siendo legible:
    /// son corridas que ya están en la base y son el historial del ranking.
    /// </remarks>
    public const byte Version = 1;

    public static byte[] Escribir(LogAcciones log)
    {
        using var salida = new MemoryStream();
        salida.WriteByte(Version);

        // Deflate y no Gzip: el mismo algoritmo sin los 18 bytes de encabezado y
        // checksum, que acá no aportan —el transporte es HTTPS y la integridad ya
        // está cubierta.
        using (var comprimido = new DeflateStream(salida, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var w = new BinaryWriter(comprimido))
        {
            EscribirVarint(w, (uint)log.Cantidad);

            int tickAnterior = 0;

            foreach (var a in log.Todas)
            {
                // El delta nunca es negativo porque el log está ordenado por tick:
                // se escribe mientras se juega y nunca se reordena. Si alguna vez
                // diera negativo, sería que alguien ordenó la lista, y eso además
                // rompería el determinismo.
                EscribirVarint(w, (uint)(a.Tick - tickAnterior));
                tickAnterior = a.Tick;

                w.Write((byte)a.Tipo);

                // Los tres campos van en zigzag: pueden ser negativos (un índice
                // de mochila inválido, un -1 de "ninguno") y un varint crudo
                // gastaría 5 bytes en cada número negativo.
                EscribirVarint(w, ZigZag(a.A));
                EscribirVarint(w, ZigZag(a.B));
                EscribirVarint(w, ZigZag(a.C));
            }
        }

        return salida.ToArray();
    }

    public static LogAcciones Leer(byte[] datos)
    {
        var log = new LogAcciones();

        if (datos.Length < 2)
            return log;

        byte version = datos[0];

        if (version != Version)
        {
            // Un formato que no se conoce se devuelve vacío en vez de tirar
            // excepción: del lado del servidor, un log ilegible tiene que dejar la
            // corrida con validado = 0, no voltear el request.
            return log;
        }

        using var entrada = new MemoryStream(datos, 1, datos.Length - 1);
        using var descomprimido = new DeflateStream(entrada, CompressionMode.Decompress);
        using var r = new BinaryReader(descomprimido);

        uint cantidad = LeerVarint(r);
        int tick = 0;

        for (uint i = 0; i < cantidad; i++)
        {
            tick += (int)LeerVarint(r);

            var tipo = (TipoAccion)r.ReadByte();
            int a = DesZigZag(LeerVarint(r));
            int b = DesZigZag(LeerVarint(r));
            int c = DesZigZag(LeerVarint(r));

            log.Agregar(new Accion(tick, tipo, a, b, c));
        }

        return log;
    }

    /// <summary>
    /// Entero de longitud variable: 7 bits de datos por byte, el octavo dice si
    /// sigue. Un número chico ocupa 1 byte; uno grande, hasta 5.
    /// </summary>
    private static void EscribirVarint(BinaryWriter w, uint valor)
    {
        while (valor >= 0x80)
        {
            w.Write((byte)(valor | 0x80));
            valor >>= 7;
        }

        w.Write((byte)valor);
    }

    private static uint LeerVarint(BinaryReader r)
    {
        uint resultado = 0;
        int corrimiento = 0;

        while (true)
        {
            byte b = r.ReadByte();
            resultado |= (uint)(b & 0x7F) << corrimiento;

            if ((b & 0x80) == 0)
                return resultado;

            corrimiento += 7;

            // Cinco bytes de 7 bits son 35, más que los 32 de un uint. Sin este
            // corte, un blob corrupto haría un bucle que corre para siempre — y
            // este código lo corre el SERVIDOR sobre datos que manda el cliente.
            if (corrimiento > 28)
                return resultado;
        }
    }

    /// <summary>
    /// Mapea negativos a positivos intercalados: 0, -1, 1, -2, 2... así un -1
    /// ocupa 1 byte en vez de 5.
    /// </summary>
    private static uint ZigZag(int v) => (uint)((v << 1) ^ (v >> 31));

    private static int DesZigZag(uint v) => (int)(v >> 1) ^ -(int)(v & 1);
}
