using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace TowerOfArgentum.Tools;

/// <summary>
/// Escribe <c>Data\apariencia.json</c>: con qué gráficos se dibuja cada clase en
/// cada uno de sus tres tiers.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Existe para que nadie derive una clave de gráfico en runtime.</b>
/// El traedor de arte arma las claves a partir del nombre del objeto de AO
/// —<c>cuerpo.armaduraDeCuero</c>—, y el que dibuja necesita esa misma clave. Si
/// las dos puntas la calculan por su cuenta, alcanza con que Austral escriba un
/// nombre distinto para que el juego pida un gráfico que no existe y dibuje un
/// cuadrado verde sin avisar. Ya pasó con el bestiario.</para>
///
/// <para>El <b>tier lo da el nivel de la clase</b>, no un ítem: la armadura, el
/// casco y el escudo no dan stats y son puro cambio de frame (nivel 1-2 → tier 1,
/// 3-4 → tier 2, 5-6 → tier 3). Lo único que da números es el arma, y esa se
/// dibuja aparte porque es un drop con stats propios.</para>
///
/// <code>
/// Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --importar-apariencia
/// </code>
/// </remarks>
public static class AppearanceImporter
{
    private const string RutaDestino = "Data/apariencia.json";

    /// <summary>
    /// La cabeza que se le pone a cada clase.
    /// </summary>
    /// <remarks>
    /// <para>Los bots de Austral usan dos: la <c>1</c> y la <c>201</c>. Acá se usa
    /// la 1 para todas y la 201 queda traída para cuando haya con qué
    /// distinguir.</para>
    ///
    /// <para>⚠ <b>No hay razas todavía.</b> Los cuerpos y las cabezas de las cinco
    /// están importados, pero ninguna clase está atada a una raza: se trajeron
    /// porque el pipeline ya estaba armado y traerlas después cuesta lo mismo que
    /// traerlas ahora.</para>
    /// </remarks>
    private const int CabezaPorDefecto = 1;

    public static void Execute()
    {
        var tiers = GraphicsInspector.ReadTiers();
        var objetos = GraphicsInspector.ReadObjects();
        var cuerpos = DatIndexReader.ReadBodies();

        GD.Print("── Importador de apariencia ───────────────────────");

        if (tiers.Count == 0)
        {
            GD.PrintErr("  ✗ no se pudo leer Bots.dat");
            return;
        }

        var clases = new List<ClaseApariencia>(tiers.Count);
        int sinArma = 0;
        int sinCuerpo = 0;

        foreach (var t in tiers)
        {
            var deTier = new List<TierApariencia>(3);

            for (int i = 0; i < 3; i++)
            {
                string cuerpo = "";
                string arma = "";
                string nombreArma = "";
                string nombreArmadura = "";
                int cabezaX = 0;
                int cabezaY = 0;

                // ⚠ El cuerpo lo da la ARMADURA, no el Body del personaje.
                //
                // Es todo el punto de que haya tiers: el mismo guerrero se ve
                // distinto con cuero que con placas. El `NumRopaje` de la armadura
                // es el índice del cuerpo en Personajes.ind.
                if (i < t.Armadura.Length && t.Armadura[i] > 0
                    && objetos.TryGetValue(t.Armadura[i], out var oArmadura)
                    && oArmadura.NumRopaje > 0)
                {
                    cuerpo = "cuerpo." + GraphicFetcher.ShortName(oArmadura);
                    nombreArmadura = oArmadura.Name;

                    // ⚠ Dónde va la cabeza lo dice el CUERPO, y hay que sacarlo.
                    //
                    // Sin esto la cabeza se dibuja en el mismo punto de apoyo que
                    // el cuerpo, o sea a los pies. `Personajes.ind` guarda el
                    // offset en 1x y las hojas son x4, así que se multiplica acá y
                    // el archivo queda todo en el mismo espacio que graficos.json.
                    //
                    // Separa razas altas de bajas: -38 px para humano, elfo y elfo
                    // oscuro; -28 para enano y gnomo.
                    if (cuerpos.TryGetValue(oArmadura.NumRopaje, out var datosCuerpo))
                    {
                        cabezaX = datosCuerpo.OffsetCabezaX * GraphicsIndexReader.Canvas;
                        cabezaY = datosCuerpo.OffsetCabezaY * GraphicsIndexReader.Canvas;
                    }
                }
                else
                {
                    sinCuerpo++;
                }

                // ⚠ El arma en la mano NO sale del GrhIndex.
                //
                // El GrhIndex es el ícono de la mochila. La animación sale de
                // `Anim`, que es la clave de la sección [ARMA<n>] de Armas.dat.
                if (i < t.Weapon.Length && t.Weapon[i] > 0
                    && objetos.TryGetValue(t.Weapon[i], out var oArma)
                    && oArma.Anim > 0)
                {
                    arma = "arma." + GraphicFetcher.ShortName(oArma);
                    nombreArma = oArma.Name;
                }
                else
                {
                    sinArma++;
                }

                deTier.Add(new TierApariencia
                {
                    Body = cuerpo,
                    Weapon = arma,
                    HeadOffsetX = cabezaX,
                    HeadOffsetY = cabezaY,
                    ArmorName = nombreArmadura,
                    WeaponName = nombreArma,
                });
            }

            clases.Add(new ClaseApariencia
            {
                DefenderClass = t.DefenderClass,
                Head = $"cabeza.{CabezaPorDefecto}",
                Tiers = deTier,
            });

            GD.Print($"  ✓ {t.DefenderClass,-10} " +
                     $"{string.Join(" · ", deTier.ConvertAll(x => Corto(x.Body)))}");
        }

        Write(clases, sinCuerpo, sinArma);
    }

    /// <summary>Recorta el prefijo para que el listado entre en una línea.</summary>
    private static string Corto(string clave)
    {
        int punto = clave.IndexOf('.');
        return punto < 0 ? clave : clave[(punto + 1)..];
    }

    private static void Write(List<ClaseApariencia> clases, int sinCuerpo, int sinArma)
    {
        var salida = new
        {
            version = 1,

            origen = "Bots.dat de Austral AO (Latin-1), resuelto contra OBJ.DAT (UTF-16 LE)",

            aviso = "Generado por el importador. NO editar a mano: se pisa en la próxima " +
                    "corrida de --importar-apariencia.",

            avisoTier = "El tier sale del NIVEL de la clase, no de un ítem equipado: " +
                        "nivel 1-2 → tier 1, 3-4 → tier 2, 5-6 → tier 3. La armadura, el " +
                        "casco y el escudo no dan stats, solo cambian el frame. Lo único " +
                        "que da números es el arma, y esa es un drop con stats propios.",

            avisoClaves = "Los valores son claves de Resources/graficos.json SIN la " +
                          "dirección: a «cuerpo.armaduraDeCuero» hay que agregarle " +
                          ".norte, .este, .oeste o .sur. Vacío = ese tier no tiene arte.",

            clases,
        };

        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        string ruta = ProjectSettings.GlobalizePath("res://" + RutaDestino);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);

        // UTF-8 SIN BOM: el BOM rompe parsers de JSON que leen el primer byte
        // esperando '{'.
        File.WriteAllText(
            ruta,
            JsonSerializer.Serialize(salida, opciones),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        GD.Print($"\n  {clases.Count} clases · 3 tiers cada una");

        if (sinCuerpo > 0)
            GD.PrintErr($"  ⚠ {sinCuerpo} tiers sin cuerpo");

        if (sinArma > 0)
            GD.PrintErr($"  ⚠ {sinArma} tiers sin arma");

        GD.Print($"  escrito en   : {ruta}");
        GD.Print("───────────────────────────────────────────────────");
    }

    private sealed record ClaseApariencia
    {
        /// <summary>El nombre de la clase, tal como lo escribe <c>Bots.dat</c>.</summary>
        public required string DefenderClass { get; init; }

        public required string Head { get; init; }

        /// <summary>Tres entradas, en orden de tier.</summary>
        public required List<TierApariencia> Tiers { get; init; }
    }

    private sealed record TierApariencia
    {
        public required string Body { get; init; }

        public required string Weapon { get; init; }

        /// <summary>
        /// Dónde va la cabeza respecto del punto de apoyo del cuerpo, en píxeles
        /// x4. Casi siempre negativo en Y: la cabeza va arriba.
        /// </summary>
        public required int HeadOffsetX { get; init; }

        public required int HeadOffsetY { get; init; }

        /// <summary>
        /// El nombre del objeto de AO, solo para poder leer el archivo.
        /// </summary>
        /// <remarks>
        /// No lo usa nadie en runtime. Está porque un archivo con nueve filas de
        /// <c>cuerpo.tunicaAzulHEEoHM</c> es ilegible sin saber qué armadura era.
        /// </remarks>
        public required string ArmorName { get; init; }

        public required string WeaponName { get; init; }
    }
}
