using Godot;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Saca a PNG el piso horneado de las ocho áreas, para <b>mirarlo</b>.
/// </summary>
/// <remarks>
/// <para>Misma idea que <c>--verificar-tilesets</c>: dibuja por el mismo camino
/// que va a usar el juego —<see cref="PisoHorneado"/>, el índice, las hojas— así
/// que si una paleta quedara mal picada, la costura se ve acá antes que en el
/// juego.</para>
///
/// <para>Sale una sola hoja de contacto con las ocho áreas a un cuarto de
/// tamaño, en dos columnas: arriba a la izquierda el área 1, abajo a la derecha
/// la 8. Ocho archivos de 1920×1080 serían ocho ventanas para abrir.</para>
/// </remarks>
public static class MuestrearPiso
{
    public static void Correr(string destino, ulong seed, int soloArea = 0)
    {
        if (string.IsNullOrWhiteSpace(destino))
            destino = "piso.png";

        // Una sola área, a tamaño real. La hoja de contacto va a un cuarto y a esa
        // escala el borde del camino no se puede juzgar.
        if (soloArea >= 1 && soloArea <= 8)
        {
            var sola = PisoHorneado.Imagen(
                soloArea, seed, Simulacion.Layouts.Choose(seed, CargadorDatos.Actual));

            if (sola is null)
            {
                GD.PushError($"área {soloArea}: sin piso");
                return;
            }

            var falla = sola.SavePng(destino);

            GD.Print(falla == Error.Ok
                ? $"{destino} · área {soloArea} a tamaño real · seed {seed}"
                : $"No se pudo escribir {destino}: {falla}");

            return;
        }

        const int Columnas = 2;
        const int Filas = 4;

        int ancho = Escala.LienzoAncho / 4;
        int alto = Escala.LienzoAlto / 4;

        var hoja = Image.CreateEmpty(
            ancho * Columnas, alto * Filas, false, Image.Format.Rgba8);

        int pintadas = 0;

        // El mismo layout en las ocho: lo que se compara acá es la paleta y el
        // camino, no la forma de la ruta.
        var mapa = Simulacion.Layouts.Choose(seed, CargadorDatos.Actual);

        for (int area = 1; area <= 8; area++)
        {
            var piso = PisoHorneado.Imagen(area, seed, mapa);

            if (piso is null)
            {
                GD.Print($"área {area}: sin piso");
                continue;
            }

            piso.Resize(ancho, alto, Image.Interpolation.Bilinear);

            int columna = (area - 1) % Columnas;
            int fila = (area - 1) / Columnas;

            hoja.BlitRect(
                piso,
                new Rect2I(0, 0, ancho, alto),
                new Vector2I(columna * ancho, fila * alto));

            pintadas++;
        }

        if (pintadas == 0)
        {
            GD.PushError("No se pudo hornear ningún piso. ¿Está graficos.json?");
            return;
        }

        var error = hoja.SavePng(destino);

        if (error != Error.Ok)
        {
            GD.PushError($"No se pudo escribir {destino}: {error}");
            return;
        }

        GD.Print($"{destino} · {pintadas} de 8 áreas · seed {seed}");
        GD.Print("orden: 1 2 / 3 4 / 5 6 / 7 8");
    }
}
