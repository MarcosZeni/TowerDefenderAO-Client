using System.Collections.Generic;
using System.IO;
using Godot;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Dibuja los tiles <b>desde nuestro índice</b>, para ver si la grilla cierra.
/// </summary>
/// <remarks>
/// <para>Un tileset entra al índice como una entrada compacta —columnas, filas,
/// lado— y quien dibuja calcula dónde cae cada tile. Esa cuenta es exactamente el
/// tipo de cosa que sale mal en silencio: un tile corrido medio cuadro se ve como
/// "el arte de AO es así".</para>
///
/// <para>Esto arma una hoja de contacto usando <see cref="CargadorGraficos.Tile"/>,
/// o sea el mismo camino que va a usar el juego. Si la grilla está bien, se ven
/// tiles enteros; si está mal, se ven costuras.</para>
/// </remarks>
public static class VerificarTilesets
{
    public static void Correr(string destino)
    {
        GD.Print("── Tilesets, dibujados desde nuestro índice ───────");

        var nombres = new List<string>();

        foreach (var (clave, _) in CargadorGraficos.Index)
        {
            if (clave.StartsWith("tileset.", System.StringComparison.Ordinal))
                nombres.Add(clave);
        }

        nombres.Sort(System.StringComparer.Ordinal);

        if (nombres.Count == 0)
        {
            GD.PrintErr("  ✗ no hay ningún tileset en el índice");
            return;
        }

        const int PorFila = 8;
        const int Lado = 64;

        var contacto = Image.CreateEmpty(
            PorFila * Lado, nombres.Count * Lado, false, Image.Format.Rgba8);

        contacto.Fill(new Color(1f, 0f, 1f));

        int problemas = 0;

        for (int fila = 0; fila < nombres.Count; fila++)
        {
            string nombre = nombres[fila];
            var datos = CargadorGraficos.Datos(nombre);

            if (datos?.Grilla is not { } grilla)
            {
                GD.PrintErr($"  ✗ {nombre}: no tiene grilla");
                problemas++;
                continue;
            }

            // Repartidos a lo largo del tileset, no los 8 primeros: así se ve si
            // la última fila de la grilla también cae donde tiene que caer.
            int paso = System.Math.Max(1, grilla.Count / PorFila);

            for (int col = 0; col < PorFila; col++)
            {
                var textura = CargadorGraficos.Tile(nombre, col * paso);

                if (textura is not AtlasTexture atlas || atlas.Atlas is null)
                {
                    problemas++;
                    continue;
                }

                var imagen = atlas.Atlas.GetImage();

                if (imagen is null)
                {
                    problemas++;
                    continue;
                }

                imagen.Convert(Image.Format.Rgba8);

                var r = atlas.Region;

                var region = new Rect2I(
                    (int)r.Position.X, (int)r.Position.Y, (int)r.Size.X, (int)r.Size.Y);

                // ⚠ La región tiene que caber en la hoja. Si la grilla se pasa,
                // GetRegion devuelve basura en vez de fallar.
                if (region.Position.X < 0 || region.Position.Y < 0
                    || region.End.X > imagen.GetWidth() || region.End.Y > imagen.GetHeight())
                {
                    GD.PrintErr($"  ✗ {nombre} tile {col * paso}: la región {region} " +
                                $"se sale de la hoja de {imagen.GetWidth()}×{imagen.GetHeight()}");
                    problemas++;
                    continue;
                }

                var recorte = imagen.GetRegion(region);
                recorte.Resize(Lado, Lado, Image.Interpolation.Bilinear);

                contacto.BlitRect(
                    recorte, new Rect2I(0, 0, Lado, Lado), new Vector2I(col * Lado, fila * Lado));
            }

            int vacios = ContarVacios(nombre, grilla);

            GD.Print($"  fila {fila}: {nombre,-24} {grilla.Columnas}×{grilla.Filas} " +
                     $"= {grilla.Count} tiles de {grilla.Lado} px" +
                     (vacios > 0 ? $"   ⚠ {vacios} vacíos" : ""));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        contacto.SavePng(destino);

        GD.Print(problemas == 0
            ? $"\n── TILESETS OK · escrito en {destino} ─────"
            : $"\n── {problemas} PROBLEMAS · escrito en {destino} ─────");
    }

    /// <summary>
    /// Cuántos tiles de un tileset están vacíos.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>El verificador de arte NO los ve.</b> Un tileset es <b>una</b>
    /// entrada del índice, así que ese chequeo mira el primer tile y da la hoja
    /// por buena. Las hojas de Austral tienen mitades sin dibujar —la de arena
    /// tiene contenido en la primera mitad y nada en la segunda—, y el decorador
    /// que elija un tile al azar pintaría agujeros en el piso.</para>
    ///
    /// <para>Se muestrea un píxel cada 8. Un tile de 128×128 con contenido tiene
    /// cientos de píxeles opacos; uno vacío no tiene ninguno.</para>
    /// </remarks>
    private static int ContarVacios(string nombre, GrillaTiles grilla)
    {
        int vacios = 0;

        for (int i = 0; i < grilla.Count; i++)
        {
            if (CargadorGraficos.Tile(nombre, i) is not AtlasTexture atlas
                || atlas.Atlas is null)
            {
                continue;
            }

            var imagen = atlas.Atlas.GetImage();

            if (imagen is null)
                continue;

            var r = atlas.Region;
            bool algo = false;

            for (int y = (int)r.Position.Y; y < (int)r.End.Y && !algo; y += 8)
            {
                for (int x = (int)r.Position.X; x < (int)r.End.X; x += 8)
                {
                    if (x >= imagen.GetWidth() || y >= imagen.GetHeight())
                        continue;

                    if (imagen.GetPixel(x, y).A > 0.1f)
                    {
                        algo = true;
                        break;
                    }
                }
            }

            if (!algo)
                vacios++;
        }

        return vacios;
    }
}
