using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace TowerOfArgentum.Tools;

/// <summary>
/// Lee <c>NPCs.dat</c> de Austral y escribe <c>enemigos.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Se corre a mano, offline, una vez.</b> Los <c>.dat</c> de Austral
/// nunca se leen en runtime: el juego consume los JSON de <c>Client\Data\</c>,
/// que son entrada de la simulación y llevan versión y hash.</para>
///
/// <para>⚠ <b><c>Encoding.Latin1</c> vive SOLO en el borde de importación</b>, y
/// este archivo es uno de los tres que lo tocan —los otros dos son
/// <see cref="GraphicsInspector"/> (<c>Bots.dat</c>) y
/// <see cref="DatIndexReader"/> (<c>Armas.dat</c>)—. Los tres son herramientas
/// offline. Lo que sale de acá se escribe en UTF-8, y la base, la API, los
/// <c>.cs</c>, las escenas y los documentos son UTF-8 sin excepción.</para>
///
/// <para>⚠ <b><c>OBJ.DAT</c> es la excepción: es UTF-16 LE</b>, único entre los
/// <c>.dat</c> de Austral. Leerlo como Latin-1 devuelve un byte nulo entre cada
/// letra.</para>
///
/// <code>
/// Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --importar-npcs
/// </code>
/// </remarks>
public static class NpcImporter
{
    private const string RutaOrigen =
        @"D:\Github\Tidies24\Austral-Csharp-SERVER\TdsServer\Dat\NPCs.dat";

    private const string RutaDestino = "Data/enemigos.json";

    /// <summary>
    /// Techo del bestiario útil, en HP crudos de Austral.
    /// </summary>
    /// <remarks>
    /// El Golem tiene 23.000 y es la criatura más dura que tiene sentido como
    /// enemigo de oleada. Arriba de eso están los "Dark &lt;nombre&gt;" de 45.000
    /// y la Ely maligna de 500.000, que son <b>personajes de GM y de evento</b>,
    /// no fauna. Meterlos arruinaría el reescalado: la curva se estiraría para
    /// cubrir un rango que nunca se usa, y todo lo de abajo quedaría aplastado.
    /// </remarks>
    private const int HpCrudoMaximo = 23_000;

    /// <summary>Piso: por debajo no hay stats de combate reales.</summary>
    private const int HpCrudoMinimo = 5;

    /// <summary>Lo que se guarda de cada criatura.</summary>
    private sealed record EnemigoImportado
    {
        public required int Id { get; init; }

        /// <summary>El número de NPC en <c>NPCs.dat</c>. Es la trazabilidad al original.</summary>
        public required int OriginalNpc { get; init; }

        public required string Name { get; init; }

        /// <summary>
        /// A qué área pertenece, de 1 a 8. Sale del HP reescalado.
        /// </summary>
        /// <remarks>
        /// Calcularlo acá y no a mano evita el trabajo más aburrido del proyecto
        /// —repartir 100 criaturas en ocho listas— y, más importante, hace que
        /// agregar un bicho nuevo no obligue a decidir nada: cae en el área que le
        /// corresponde por lo que pega.
        /// </remarks>
        public required int Area { get; init; }

        /// <summary>HP ya reescalado. Es el que usa la simulación.</summary>
        public required int ScaledHp { get; init; }

        /// <summary>HP crudo de Austral, para poder rehacer el reescalado.</summary>
        public required int RawHp { get; init; }

        /// <summary>
        /// ⚠ <c>Body</c> NO es un <c>fileNum</c>. Es el índice del cuerpo en
        /// Austral, y para llegar al gráfico hay que resolver
        /// <c>body → grh → frames[] → fileNum</c> con la skill
        /// <c>traer-grafico</c>. Se guarda como pista, no como respuesta.
        /// </summary>
        public required int Body { get; init; }

        public required int MinDamage { get; init; }
        public required int MaxDamage { get; init; }
        public required int Defense { get; init; }
        public required int AttackPower { get; init; }
        public required int Gold { get; init; }
        public required int Exp { get; init; }

        /// <summary>Milésimas de celda por tick. Derivada, no viene del .dat.</summary>
        public required int Speed { get; init; }

        /// <summary>Ticks entre golpe y golpe a la Torre.</summary>
        public required int CadenceTicks { get; init; }

        /// <summary>
        /// Cuánto se dibuja de grande, en porcentaje. 100 es el tamaño natural.
        /// </summary>
        /// <remarks>
        /// <para><b>Es render, no simulación.</b> El enemigo no tiene tamaño en la
        /// simulación: tiene una <c>distanciaRecorrida</c> escalar y nada más. Este
        /// número no entra en ninguna cuenta del tick.</para>
        ///
        /// <para>Existe porque <b>catorce cuerpos de Austral los comparten dos o
        /// tres bichos</b>: "Rata" y "Rata Gigante" son el mismo <c>Body=71</c>, y
        /// "Gallo", "Gallo salvaje" y "Gallo salvaje de dungeon" el mismo
        /// <c>Body=5</c>. En un MMO no molesta —están en mapas distintos y el
        /// nombre se lee al pasar el mouse—; acá caminan treinta a la vez y el
        /// jugador tiene que decidir dónde poner un defensor mirando la oleada. Dos
        /// sprites idénticos con el triple de vida es información escondida.</para>
        ///
        /// <para>La regla es la que ya dicen los nombres: <b>dentro de un mismo
        /// cuerpo, más grande es más duro</b>. No hace falta arte nuevo y se lee de
        /// una.</para>
        /// </remarks>
        public required int VisualScale { get; init; }

        /// <summary>
        /// La clave de este bicho en <c>Resources\graficos.json</c>, sin la
        /// dirección: <c>bicho.hormiga</c> se dibuja con
        /// <c>bicho.hormiga.sur</c> y sus tres hermanas.
        /// </summary>
        /// <remarks>
        /// <para>⚠ <b>La escribe el importador y NADIE la vuelve a calcular.</b>
        /// La primera versión derivaba la clave del nombre en dos lugares —el
        /// traedor de arte y el que dibuja—, y en cuanto el importador cambió un
        /// nombre, el índice quedó con <c>bicho.rataGigante</c> apuntando a un
        /// enemigo que ahora se llama "Rata". Con la clave en el dato, el que
        /// dibuja no adivina y el validador puede comparar.</para>
        /// </remarks>
        public required string Graphic { get; init; }
    }

    /// <summary>
    /// El nombre de un bicho convertido en clave del índice de gráficos.
    /// </summary>
    /// <remarks>
    /// camelCase, sin acentos y sin nada que no sea letra o número: los nombres de
    /// AO traen tildes, eñes y espacios, y una clave con tilde es una que alguien
    /// va a escribir mal. El nombre que ve el jugador queda intacto.
    /// </remarks>
    private static string ClaveGrafico(string nombre)
    {
        var limpio = new StringBuilder("bicho.");
        bool mayuscula = false;

        foreach (char c in Desacentuar(nombre))
        {
            if (char.IsLetterOrDigit(c))
            {
                limpio.Append(mayuscula ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                mayuscula = false;
            }
            else
            {
                mayuscula = true;
            }
        }

        return limpio.ToString();
    }

    /// <summary>
    /// Reescala el HP crudo de Austral al rango que usa este juego.
    /// </summary>
    /// <remarks>
    /// <para>Los números de AO están pensados para un MMO donde un personaje
    /// pelea de a uno: el rango va de 5 a 23.000, un factor de 4.600. Acá hay
    /// treinta bichos a la vez contra diez defensores, y el presupuesto de oleada
    /// crece un factor 1.500 entre la primera y la última. Copiar la escala tal
    /// cual haría que las primeras áreas se pasaran en un suspiro y las últimas
    /// fueran un muro.</para>
    ///
    /// <para>La curva es <c>a · hp^b</c>, clavada en dos puntos: la Hormiga (5 →
    /// 30) y el Golem (23.000 → 6.000). Comprime el extremo alto y respeta el
    /// bajo, que es donde el jugador aprende a jugar.</para>
    ///
    /// <para>⚠ Acá SÍ se puede usar <c>Math.Pow</c> y <c>double</c>: esto es el
    /// importador, corre <b>una vez y offline</b>, y lo que escribe son enteros.
    /// La prohibición vive adentro de <c>Simulation\</c>, donde la aritmética
    /// tiene que dar igual en cualquier máquina.</para>
    /// </remarks>
    private static int Reescalar(int hpCrudo)
    {
        const double hpNuevoMin = 30;
        const double hpNuevoMax = 6000;

        double b = System.Math.Log(hpNuevoMax / hpNuevoMin)
                   / System.Math.Log((double)HpCrudoMaximo / HpCrudoMinimo);

        double a = hpNuevoMin / System.Math.Pow(HpCrudoMinimo, b);

        return (int)System.Math.Round(a * System.Math.Pow(hpCrudo, b));
    }

    /// <summary>
    /// Reparte los enemigos en las 8 áreas <b>por percentil</b>, no por cortes de
    /// HP escritos a mano.
    /// </summary>
    /// <remarks>
    /// <para>Primero se probó con cortes fijos y el reparto salió
    /// <c>1:3 2:17 3:3 4:12 …</c> — el área 1 con tres bichos y la 2 con
    /// diecisiete. Un área con tres enemigos no tiene de dónde variar, y ahí la
    /// seed deja de hacer trabajo: todas las corridas de esa área se parecen.</para>
    ///
    /// <para>Cortando por octiles el reparto sale parejo solo, y —lo que
    /// importa más— <b>sigue saliendo parejo cuando cambie el bestiario</b>.
    /// Números a mano habría que recalibrarlos cada vez que Austral agregue una
    /// criatura o que se toque la curva de reescalado.</para>
    /// </remarks>
    private static void RepartirEnAreas(List<EnemigoImportado> ordenadosPorHp)
    {
        int total = ordenadosPorHp.Count;
        if (total == 0)
            return;

        for (int i = 0; i < total; i++)
        {
            // i * 8 / total da 0..7; +1 lo lleva a 1..8. El último elemento daría
            // 8 exacto y se pasaría, así que se limita.
            int area = System.Math.Min(8, (i * 8 / total) + 1);
            ordenadosPorHp[i] = ordenadosPorHp[i] with { Area = area };
        }

        CoronarAlJefeFinal(ordenadosPorHp);
    }

    /// <summary>
    /// Manda al Dragón Rojo al área 8 y lo hace el más duro de todos.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>La única excepción al reparto automático, y es narrativa.</b>
    /// Matar al jefe de la oleada 100 es cómo se gana la corrida, y ese momento
    /// tiene que ser reconocible: por HP crudo el más duro del bestiario es el
    /// Golem, que como final de cien oleadas no significa nada. El Dragón Rojo sí.</para>
    ///
    /// <para>Se corrige acá, en el <b>dato</b>, y no en la consulta que pide el
    /// jefe del área 8. Así el JSON dice la verdad —el Dragón Rojo está en el área
    /// 8 y es el más duro— en vez de que haya una regla escondida que contradiga
    /// lo que se lee en el archivo.</para>
    ///
    /// <para>Si el bestiario no lo trae, no pasa nada: el área 8 se queda con su
    /// más duro, que es un final genérico pero jugable.</para>
    /// </remarks>
    private static void CoronarAlJefeFinal(List<EnemigoImportado> enemigos)
    {
        int i = enemigos.FindIndex(e =>
            string.Equals(e.Name, "Dragón Rojo", System.StringComparison.OrdinalIgnoreCase));

        if (i < 0)
            return;

        int hpMasAlto = 0;
        foreach (var e in enemigos)
            hpMasAlto = System.Math.Max(hpMasAlto, e.ScaledHp);

        var dragon = enemigos[i];
        enemigos.RemoveAt(i);

        // Un 40% por encima del segundo más duro. Es un salto que se nota sin ser
        // un muro: el jugador que llegó a la oleada 100 ya tiene con qué.
        int hpFinal = hpMasAlto * 140 / 100;

        enemigos.Add(dragon with
        {
            Area = 8,
            ScaledHp = hpFinal,

            // Pega fuerte pero lento: un jefe que mata la Torre en tres golpes no
            // se pelea, se pierde.
            MinDamage = dragon.MinDamage * 3,
            MaxDamage = dragon.MaxDamage * 3,
            CadenceTicks = 55,
            Speed = 22,
            Gold = hpFinal / 4,
        });
    }

    /// <summary>
    /// La velocidad, derivada del HP.
    /// </summary>
    /// <remarks>
    /// <c>NPCs.dat</c> no trae velocidad: en Austral todos los NPC caminan igual
    /// y lo que cambia es cada cuánto se mueven, que es lógica del server. Acá se
    /// deriva con una regla que además sirve al diseño: <b>lo chico es rápido y
    /// lo grande es lento</b>. Así una oleada mixta se estira sola —los rápidos
    /// llegan primero— y el jugador tiene que cubrir dos momentos distintos.
    /// </remarks>
    private static int VelocidadDe(int hpEscalado) => hpEscalado switch
    {
        < 60 => 55,
        < 130 => 48,
        < 280 => 42,
        < 550 => 36,
        < 1000 => 32,
        < 1800 => 28,
        < 3200 => 24,
        _ => 20,
    };

    /// <summary>
    /// Ticks entre golpe y golpe a la Torre. Lo grande pega más lento.
    /// </summary>
    /// <remarks>
    /// Junto con la velocidad, hace que un bicho de área tardía se sienta pesado
    /// y no solo "más números": camina lento, pega lento y aguanta mucho.
    /// </remarks>
    private static int CadenciaDe(int hpEscalado) => hpEscalado switch
    {
        < 60 => 22,
        < 130 => 26,
        < 280 => 30,
        < 550 => 34,
        < 1000 => 38,
        < 1800 => 42,
        < 3200 => 46,
        _ => 50,
    };

    public static void Execute()
    {
        if (!File.Exists(RutaOrigen))
        {
            GD.PrintErr($"No está {RutaOrigen}. El importador necesita el repo de Austral al lado.");
            return;
        }

        // ⚠ Latin1 acá y solo acá. Sin esto, "Arañá" y "Demonio" salen con
        // caracteres rotos y esos nombres terminan en el JSON, en la base y en la
        // pantalla del jugador.
        string[] lineas = File.ReadAllLines(RutaOrigen, Encoding.Latin1);

        var enemigos = new List<EnemigoImportado>();
        var campos = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var vistos = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        int npcActual = -1;
        int descartadosSinCombate = 0;
        int descartadosNoFauna = 0;
        int descartadosDuplicados = 0;
        int descartadosSinCuerpo = 0;

        void Cerrar()
        {
            if (npcActual < 0)
                return;

            // Solo entran los que pelean. De los 251 NPCs, ~159 tienen stats de
            // combate: el resto son aldeanos, banqueros y taberneros, que en un
            // MMO tienen sentido y acá no son nada.
            if (!campos.ContainsKey("MaxHP") || !campos.ContainsKey("MaxHIT"))
            {
                descartadosSinCombate++;
                campos.Clear();
                return;
            }

            int hpCrudo = Entero(campos, "MaxHP");
            string nombre = Text(campos, "Name", $"npc{npcActual}");

            if (!EsFauna(nombre, hpCrudo))
            {
                descartadosNoFauna++;
                campos.Clear();
                return;
            }

            // ⚠ `Body=0` es un NPC sin cuerpo, y no hay arte que traer.
            //
            // En Austral el «Acechador Invisible» tiene Body=0 y Head=0 a
            // propósito: es invisible, y en un MMO eso es una mecánica —el
            // jugador lo sufre sin verlo—. Acá no puede serlo. El jugador no
            // elige objetivo, eligen los defensores; un bicho que no se dibuja no
            // le esconde nada al jugador, solo le esconde la barra de vida y de
            // dónde viene el daño a la Torre.
            //
            // Se descarta en el importador y no a mano en el JSON, porque el JSON
            // se pisa en la próxima corrida del importador.
            if (Entero(campos, "Body") == 0)
            {
                descartadosSinCuerpo++;
                campos.Clear();
                return;
            }

            // Dedupe por nombre. `NPCs.dat` trae 17 "Viuda negra" y 5 "Lobo",
            // porque en el MMO cada mapa tiene su propia entrada con el mismo
            // bicho. Acá son el mismo enemigo: dejar los 17 llenaría las listas de
            // permitidos con clones y le daría a la viuda negra 17 veces más
            // chances de salir que a cualquier otro.
            //
            // ⚠ La clave va SIN ACENTOS, y no alcanza con ignorar mayúsculas.
            //
            // Austral escribe el mismo bicho de dos formas —"Pequeño Dragón rojo"
            // y "Pequeño Dragon rojo"— y con un comparador que solo ignora
            // mayúsculas los dos entraban como enemigos distintos, con el mismo
            // cuerpo y stats parecidos. Lo encontró el reporte de cuerpos
            // repetidos del pipeline de arte, no una lectura del JSON: en una
            // lista de 74 nombres la tilde de más no se ve.
            if (!vistos.Add(SinAcentos(nombre)))
            {
                descartadosDuplicados++;
                campos.Clear();
                return;
            }

            int hpEscalado = Reescalar(hpCrudo);

            int danoA = System.Math.Max(1, Reescalar(System.Math.Max(1, Entero(campos, "MinHIT"))) / 12);
            int danoB = System.Math.Max(1, Reescalar(System.Math.Max(1, Entero(campos, "MaxHIT"))) / 12);

            enemigos.Add(new EnemigoImportado
            {
                Id = enemigos.Count,
                OriginalNpc = npcActual,
                Name = nombre,
                Area = 0,   // se asigna al final, por percentil
                ScaledHp = hpEscalado,
                RawHp = hpCrudo,
                Body = Entero(campos, "Body"),

                // El daño también se reescala, con la misma curva: si no, un bicho
                // del área 8 con MaxHIT crudo mataría la Torre de un golpe.
                //
                // ⚠ Se ordenan después de reescalar. `NPCs.dat` tiene al menos un
                // NPC con MinHIT mayor que MaxHIT —el Golem de Hielo, 38 y 36— y
                // ese dato invertido pasaba tal cual al JSON. Lo encontró el
                // validador; en runtime habría sido un promedio de daño más bajo
                // de lo previsto y nadie se habría enterado.
                MinDamage = System.Math.Min(danoA, danoB),
                MaxDamage = System.Math.Max(danoA, danoB),

                Defense = Entero(campos, "DEF"),
                AttackPower = Entero(campos, "PoderAtaque"),

                // El oro se deriva del HP escalado y no del GiveGLD crudo: en AO
                // hay bichos duros que no sueltan nada y ratas que sueltan mucho,
                // porque allá el oro es economía de mundo. Acá el oro es el ritmo
                // al que el jugador puede poner defensores, así que tiene que ser
                // proporcional a lo que costó matarlo.
                Gold = System.Math.Max(1, hpEscalado / 8),

                Exp = Entero(campos, "GiveEXP"),
                Speed = VelocidadDe(hpEscalado),

                // Lo grande pega más lento. Junto con la velocidad, hace que un
                // bicho de área tardía se sienta pesado y no solo "más números".
                CadenceTicks = CadenciaDe(hpEscalado),

                // Siempre 100: dos enemigos no comparten cuerpo, así que no hay
                // variante grande/chica que marcar. El campo queda porque lo lee el
                // renderer y porque es el lugar donde iría un ajuste a mano.
                VisualScale = 100,
                Graphic = ClaveGrafico(nombre),
            });

            campos.Clear();
        }

        foreach (string cruda in lineas)
        {
            // Los .dat vienen con CRLF; ReadAllLines ya corta por \n pero el \r
            // queda pegado al final de cada valor. Sin este Trim, "Terreno=TIERRA"
            // se lee como "TIERRA\r" y cualquier comparación falla en silencio.
            string linea = cruda.Trim();

            // Comentarios de VB6.
            if (linea.Length == 0 || linea[0] == '\'')
                continue;

            if (linea[0] == '[')
            {
                Cerrar();

                // [NPC123] 'comentario  →  123
                int cierre = linea.IndexOf(']');
                string etiqueta = cierre > 0 ? linea[1..cierre] : linea[1..];

                npcActual = etiqueta.StartsWith("NPC", System.StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(etiqueta.AsSpan(3), out int n) ? n : -1;

                continue;
            }

            int igual = linea.IndexOf('=');
            if (igual <= 0)
                continue;

            // Los valores pueden traer '=' adentro (una descripción), así que se
            // corta en el PRIMERO y el resto es el valor entero.
            campos[linea[..igual].Trim()] = linea[(igual + 1)..].Trim();
        }

        Cerrar();

        int descartadosClones = DescartarClones(enemigos);

        enemigos.Sort((a, b) => a.ScaledHp.CompareTo(b.ScaledHp));

        // El área sale del percentil, así que hay que repartir DESPUÉS de ordenar.
        RepartirEnAreas(enemigos);

        // Los ids se reasignan después de ordenar por HP: así el id crece con la
        // dificultad y las listas de permitidos por área se leen solas.
        var finales = new List<EnemigoImportado>(enemigos.Count);
        for (int i = 0; i < enemigos.Count; i++)
        {
            var e = enemigos[i];
            finales.Add(new EnemigoImportado
            {
                Id = i,
                OriginalNpc = e.OriginalNpc,
                Name = e.Name,
                Area = e.Area,
                ScaledHp = e.ScaledHp,
                RawHp = e.RawHp,
                Body = e.Body,
                MinDamage = e.MinDamage,
                MaxDamage = e.MaxDamage,
                Defense = e.Defense,
                AttackPower = e.AttackPower,
                Gold = e.Gold,
                Exp = e.Exp,
                Speed = e.Speed,
                CadenceTicks = e.CadenceTicks,
                VisualScale = 100,
                Graphic = e.Graphic,
            });
        }

        Write(finales, descartadosSinCombate, descartadosNoFauna,
                 descartadosDuplicados + descartadosClones, descartadosSinCuerpo);
    }

    /// <summary>
    /// Saca los que son el mismo enemigo con otro nombre.
    /// </summary>
    /// <remarks>
    /// <para>El dedupe por nombre no alcanza. Austral tiene "Rata" y "Rata
    /// Gigante" con <b>el mismo cuerpo y exactamente los mismos números</b> —15 HP
    /// crudos, 1-2 de daño, misma velocidad—, y lo mismo con "Gallo", "Gallo
    /// salvaje" y "Gallo salvaje de dungeon". En un MMO son entradas separadas
    /// porque cada mapa tiene la suya; para la simulación son <b>un solo
    /// enemigo</b>, y dejarlos triplica las chances de que salga el gallo.</para>
    ///
    /// <para>⚠ <b>La clave es el cuerpo, y nada más.</b> Antes era el cuerpo más
    /// los números del tick, y eso dejaba pasar los pares que comparten sprite
    /// pero difieren en vida: Lobo y Lobo de dungeon, Tigre y Tigre Salvaje,
    /// Jabalí y Jabalí Salvaje, Zombie y Lord Zombie, Esqueleto y Esqueleto
    /// Guerrero, Duende y Duende Molesto, y los tres Body=43 (Orco, Jefe Orco,
    /// Orco brujo). En pantalla eran <b>el mismo bicho</b> —el único aviso era el
    /// 120 % de <c>visualScale</c>—, así que el jugador veía nueve enemigos
    /// repetidos con otro nombre en la barra de vida. En un MMO cada uno tiene su
    /// mapa; acá el sprite ES la identidad del enemigo.</para>
    ///
    /// <para>Se queda el <b>primero de <c>NPCs.dat</c></b>. Es el criterio que ya
    /// usa el dedupe por nombre, y elegir por "el nombre más lindo" sería una regla
    /// que hay que mantener a mano cada vez que Austral agregue un bicho.</para>
    /// </remarks>
    private static int DescartarClones(List<EnemigoImportado> enemigos)
    {
        var vistos = new HashSet<int>();
        var quedan = new List<EnemigoImportado>(enemigos.Count);

        foreach (var e in enemigos)
        {
            if (vistos.Add(e.Body))
                quedan.Add(e);
        }

        int descartados = enemigos.Count - quedan.Count;

        enemigos.Clear();
        enemigos.AddRange(quedan);

        return descartados;
    }

    private static void Write(
        List<EnemigoImportado> enemigos, int sinCombate, int noFauna, int duplicados,
        int sinCuerpo)
    {
        // ⚠ Las claves salen en INGLÉS y son el contrato con DataLoader.
        //
        // `enemies`, `scaledHp`, `originalNpc`: así las lee `ArchivoEnemigos`, y
        // así las dejó el rename del 2026-08-29 (regla de oro 4: las claves de los
        // JSON de datos van en inglés). Este objeto se había quedado en español
        // —`enemigos`, `hpEscalado`— y no lo avisaba nadie: el importador escribía
        // sin error y el juego arrancaba con cero enemigos.
        var salida = new
        {
            version = 1,
            source = "NPCs.dat de Austral AO, leído con Encoding.Latin1 y escrito en UTF-8",

            note = "Generado por el importador. NO editar a mano: se pisa en la próxima " +
                    "corrida de --importar-npcs. Los ajustes de balance van en el importador.",

            rescaled = "scaledHp = a·rawHp^b, clavado en Hormiga (5→30) y Golem " +
                         "(23000→6000). Los crudos de Austral son para un MMO 1v1; acá hay " +
                         "treinta bichos contra diez defensores. hpCrudo queda para poder " +
                         "rehacer la curva.",

            bodyNote = "⚠ `body` NO es un fileNum. Hay que resolver body → grh → " +
                        "frames[] → fileNum con la skill traer-grafico.",

            visualScale = "Porcentaje de tamaño al dibujar, 100 = natural. Es RENDER, " +
                           "no entra en ninguna cuenta del tick. Hoy son todos 100: " +
                           "desde que el dedupe descarta por cuerpo, no hay dos " +
                           "enemigos que compartan sprite, así que no hay nada que " +
                           "diferenciar por tamaño.",

            enemies = enemigos,
        };

        var opciones = new JsonSerializerOptions
        {
            WriteIndented = true,

            // camelCase, como el resto de los JSON del proyecto. Sin esto, System
            // .Text.Json usa los nombres de las propiedades de C# tal cual y el
            // archivo sale en PascalCase, distinto de todos los demás.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // Sin esto, System.Text.Json escapa los acentos como \u00F1 "por las
            // dudas". El archivo seguiría siendo válido, pero ilegible para el
            // humano que lo tiene que balancear a mano.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        string json = JsonSerializer.Serialize(salida, opciones);

        string ruta = ProjectSettings.GlobalizePath("res://" + RutaDestino);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);

        // UTF-8 SIN BOM: el BOM rompe parsers de JSON que leen el primer byte
        // esperando '{'.
        File.WriteAllText(ruta, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        GD.Print("── Importador de NPCs ─────────────────────────────");
        GD.Print($"  leídos       : " +
                 $"{enemigos.Count + sinCombate + noFauna + duplicados + sinCuerpo} NPCs");
        GD.Print($"  sin combate  : {sinCombate} (aldeanos, banqueros, taberneros)");
        GD.Print($"  no son fauna : {noFauna} (GMs, guardias, invocaciones, relleno)");
        GD.Print($"  duplicados   : {duplicados} (mismo nombre, o mismo cuerpo: " +
                 $"un cuerpo, un enemigo)");
        GD.Print($"  sin cuerpo   : {sinCuerpo} (Body=0, no hay arte que traer)");
        GD.Print($"  ENTRAN       : {enemigos.Count}");

        if (enemigos.Count > 0)
        {
            GD.Print($"  más flojo    : {enemigos[0].Name} " +
                     $"({enemigos[0].RawHp} → {enemigos[0].ScaledHp} HP)");
            GD.Print($"  más duro     : {enemigos[^1].Name} " +
                     $"({enemigos[^1].RawHp} → {enemigos[^1].ScaledHp} HP)");
        }

        // Cuántos quedaron por área. Es el número que dice si el reparto sirve: un
        // área con un solo bicho no tiene de dónde variar, y ahí la seed deja de
        // hacer trabajo.
        var porArea = new int[9];
        foreach (var e in enemigos)
            porArea[e.Area]++;

        var linea = new StringBuilder("  por área     :");
        for (int a = 1; a <= 8; a++)
            linea.Append(CultureInfo.InvariantCulture, $" {a}:{porArea[a]}");

        GD.Print(linea.ToString());

        for (int a = 1; a <= 8; a++)
        {
            if (porArea[a] < 3)
                GD.PushWarning($"El área {a} tiene solo {porArea[a]} enemigo(s): poca variedad.");
        }

        GD.Print($"  escrito en   : {ruta}");
        GD.Print("───────────────────────────────────────────────────");
    }

    /// <summary>
    /// ¿Esto es un enemigo de oleada, o es otra cosa?
    /// </summary>
    /// <remarks>
    /// <para><c>NPCs.dat</c> mezcla la fauna con cosas que en un tower defense no
    /// son enemigos: personajes de GM, invocaciones, mascotas domables y entradas
    /// de relleno con el nombre en un punto. Sin este filtro, el jugador se
    /// encuentra peleando contra "Dark mOrGoLoCk" en la oleada 40.</para>
    ///
    /// <para>El filtro es por nombre y por rango de HP, no por una lista blanca:
    /// una lista blanca habría que mantenerla a mano cada vez que Austral agregue
    /// una criatura.</para>
    /// </remarks>
    private static bool EsFauna(string nombre, int hpCrudo)
    {
        if (hpCrudo < HpCrudoMinimo || hpCrudo > HpCrudoMaximo)
            return false;

        string limpio = nombre.Trim();

        // Entradas de relleno: nombre vacío, un punto, o algo de un caracter.
        if (limpio.Length <= 1)
            return false;

        // Los "Dark <nombre>" son los personajes de los GM del servidor, con
        // 45.000 HP. El rango de arriba ya los saca, pero el prefijo se chequea
        // igual por si alguno tuviera menos vida.
        if (limpio.StartsWith("Dark ", System.StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (string prohibido in _noSonFauna)
        {
            if (limpio.Contains(prohibido, System.StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (string feo in _arteQueNoSirve)
        {
            if (limpio.Contains(feo, System.StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Bichos que se descartan por el arte, no por lo que son.
    /// </summary>
    /// <remarks>
    /// <para>Son fauna y pelean, así que ningún filtro de los de arriba los toca.
    /// Salen porque el sprite no sirve: los dos comparten <c>Body=25</c> en
    /// <c>NPCs.dat</c> —un unicornio y un árbol dibujados con el mismo cuerpo— y
    /// en un tower defense visto desde arriba no se leen como enemigos.</para>
    ///
    /// <para>Va como lista aparte de <see cref="_noSonFauna"/> a propósito: aquella
    /// dice "esto no es un enemigo", esta dice "esto es un enemigo pero no lo
    /// queremos ver". Mezclarlas hace que el próximo que lea el filtro no sepa cuál
    /// de las dos reglas está tocando.</para>
    /// </remarks>
    private static readonly string[] _arteQueNoSirve =
    [
        // Comparten Body=25 en NPCs.dat: un unicornio y un árbol dibujados con el
        // mismo cuerpo, y ninguno de los dos se lee como enemigo desde arriba.
        "ucorno", "arbol de jungla", "árbol de jungla",

        // ⚠ Los de abajo los eligió el usuario mirando las hojas una por una
        // (2026-08-31), así que la razón es visual y no hay regla que la derive.
        //
        // Los barcos y los bichos de agua —galeones, calamar, leviatán— son
        // sprites de 1312 px de ancho hechos para el mar de AO: en una ruta de
        // celdas de 96 px ocupan diez celdas y tapan el tablero.
        //
        // Bandido, Práctica, Servidor del Mal, Bruja, Mago Oscuro y Druida son
        // humanoides que en NPCs.dat traen Head, y el importador solo guarda
        // Body: se dibujan DECAPITADOS. Quedan afuera hasta que el bestiario
        // sepa apilar cabeza, como ya lo hace el defensor.
        "calamar", "leviatán", "leviatan",
        "galeón", "galeon",
        "bandido", "practica", "práctica", "servidor del mal",
        // "mago" se lleva a los tres: Mago, Mago malvado y Mago Oscuro. Los tres
        // son el mismo humanoide con túnica y sin cabeza, y de los tres el cuerpo
        // sale de una armadura de defensor, así que además se pisaban con el
        // muñeco del jugador.
        "bruja", "mago", "druida",
        "fuego fatuo",
    ];

    /// <summary>
    /// Palabras que delatan algo que no es un enemigo de oleada.
    /// </summary>
    /// <remarks>
    /// Los guardias e imperiales son NPC de ciudad: pelean, pero del lado del
    /// jugador. Las mascotas y los espíritus son mecánicas del MMO que acá no
    /// existen.
    /// </remarks>
    private static readonly string[] _noSonFauna =
    [
        "guardia", "imperial", "caos", "sacerdote", "newbie",
        "espiritu", "espíritu", "fantasma de", "soul",
        "entrenador", "mascota", "invocad",
    ];

    private static int Entero(Dictionary<string, string> campos, string clave)
        => campos.TryGetValue(clave, out string? v)
           && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : 0;

    private static string Text(Dictionary<string, string> campos, string clave, string porDefecto)
        => campos.TryGetValue(clave, out string? v) && v.Length > 0 ? v : porDefecto;

    /// <summary>
    /// Saca los acentos y baja a minúscula. <b>Solo para comparar</b>.
    /// </summary>
    /// <remarks>
    /// El nombre que se guarda va con sus tildes y sus eñes (regla de oro 4).
    /// Esto es la clave de deduplicación y nada más: existe porque veinte años de
    /// ediciones a mano dejaron el mismo bicho escrito de dos formas.
    /// </remarks>
    private static string SinAcentos(string s) => Desacentuar(s).ToLowerInvariant();

    /// <summary>Saca los acentos y respeta las mayúsculas.</summary>
    private static string Desacentuar(string s)
    {
        var salida = new StringBuilder(s.Length);

        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                salida.Append(c);
            }
        }

        return salida.ToString();
    }
}
