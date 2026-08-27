using System.Collections.Generic;
using Godot;
using TowerOfArgentum.Simulacion;

namespace TowerOfArgentum.Herramientas;

/// <summary>
/// Revisa que los datos y los layouts sean coherentes.
/// </summary>
/// <remarks>
/// <para>Los errores de datos no se ven al compilar y muchos tampoco al jugar
/// una partida: aparecen en la oleada 60, o con una seed de cada veinte, o solo
/// cuando el servidor revalida. Este chequeo los adelanta.</para>
///
/// <code>
/// Godot_v4.8-dev3_mono_win64.exe --headless --path . -- --validar
/// </code>
/// </remarks>
public static class ValidadorDatos
{
    public static void Correr()
    {
        var problemas = new List<string>();
        var avisos = new List<string>();

        GD.Print("── Validador de datos ─────────────────────────────");

        var datos = CargadorDatos.Actual;

        // ⚠ Primero de todo: si alguna de las siete tablas no cargó, lo demás va
        // a validar los datos de emergencia y va a decir que está todo bien.
        // El error del cargador es el único que importa mientras exista.
        if (CargadorDatos.Problema.Length > 0)
        {
            GD.PrintErr($"  ✗ {CargadorDatos.Problema}");
            GD.PrintErr("── NO SE PUDO CARGAR · el resto no se validó ──────");
            return;
        }

        ValidarBestiario(datos, problemas, avisos);
        ValidarApariencia(problemas, avisos);
        ValidarTablas(datos, problemas, avisos);
        ValidarPerks(problemas, avisos);
        ValidarPlanos(problemas);
        ValidarLayouts(problemas, avisos);
        ValidarCurvas(datos, problemas, avisos);

        foreach (string a in avisos)
            GD.Print($"  · {a}");

        foreach (string p in problemas)
            GD.PrintErr($"  ✗ {p}");

        GD.Print(problemas.Count == 0
            ? $"── OK · {avisos.Count} aviso(s) ───────────────────────"
            : $"── {problemas.Count} PROBLEMA(S) ──────────────────────");
    }

    /// <summary>
    /// Lo que los archivos de datos no pueden verificar solos.
    /// </summary>
    /// <remarks>
    /// <para>El lector de <c>Tablas.cs</c> ya rechaza lo que está mal <b>dentro
    /// de un archivo</b> —un id repetido, un plano que fusiona un Bronce, un
    /// alcance 1—. Acá van los cruces <b>entre</b> archivos y las cosas que no
    /// son errores pero conviene mirar.</para>
    ///
    /// <para>La diferencia importa: lo que rompe la simulación tira al cargar y
    /// el juego ni abre; lo que solo es raro sale por acá como aviso y se juega
    /// igual.</para>
    /// </remarks>
    private static void ValidarTablas(BalanceData datos, List<string> problemas, List<string> avisos)
    {
        // Toda clase del enum tiene que estar en clases.json: si falta una, el
        // juego funciona hasta que la elección la ofrece y ahí tira.
        foreach (var clase in ChoiceBuilder.All)
        {
            try
            {
                datos.Classes.De(clase);
            }
            catch (InvalidBalanceDataException)
            {
                problemas.Add($"clases.json no define «{clase}», y la elección la puede ofrecer");
            }
        }

        avisos.Add(
            $"clases: {datos.Classes.All.Count} · {datos.Classes.Offensive().Length} ofensivas · " +
            $"nivel máximo {datos.Classes.PerkSlotsPerClass} (+{datos.Classes.ExtraDamagePerLevel}% daño por nivel)");

        // ⚠ Era la anomalía P0 de la propuesta V2: con daño entero y chico, el
        // truncamiento se comía niveles enteros —4 de 5 en Cazador, 3 de 5 en
        // Clérigo, Guerrero, Ladrón y Bardo— y el jugador gastaba la elección en
        // algo que no movía ningún número.
        //
        // Lo arregló la escala interna ×100, y este chequeo QUEDA como guarda de
        // regresión: si alguien vuelve a calcular sobre puntos enteros, o baja
        // extraDamagePerLevel por debajo de lo que la escala puede representar,
        // el aviso reaparece. Se mide sobre la escala interna, que es donde la
        // simulación hace la cuenta de verdad.
        foreach (var c in datos.Classes.All)
        {
            int mudos = 0;
            int baseInterno = Ints.ToHundredths(c.Damage);

            for (int nivel = 2; nivel <= datos.Classes.PerkSlotsPerClass; nivel++)
            {
                int anterior = Ints.WithPercents(baseInterno, (nivel - 2) * datos.Classes.ExtraDamagePerLevel);
                int actual = Ints.WithPercents(baseInterno, (nivel - 1) * datos.Classes.ExtraDamagePerLevel);

                if (actual == anterior)
                    mudos++;
            }

            if (mudos > 0)
            {
                avisos.Add(
                    $"{c.DefenderClass}: daño base {c.Damage}, {mudos} de {datos.Classes.PerkSlotsPerClass - 1} niveles " +
                    "NO suben nada por truncamiento entero");
            }
        }

        // Lo mismo con los perks numéricos: un perk que no mueve el número en
        // ninguna clase es un slot y un drop gastados en nada.
        //
        // ⚠ También se mide en escala interna. Antes, medido en puntos y celdas
        // enteras, «punteria» (+15% de alcance) no cambiaba nada en NINGUNA de
        // las nueve clases y los dos perks de alcance de plata no servían en las
        // cuatro cuerpo a cuerpo. Con centésimas y milésimas, un porcentaje
        // chico ya tiene dónde caer.
        foreach (var p in datos.Perks.All)
        {
            if (p.Effect is not (PerkEffect.Damage or PerkEffect.Range))
                continue;

            int sirveEn = 0;

            foreach (var c in datos.Classes.All)
            {
                if (!p.WorksFor(c.DefenderClass))
                    continue;

                int baseValor = p.Effect == PerkEffect.Damage
                    ? Ints.ToHundredths(c.Damage)
                    : Ints.ToThousandths(c.RangeCells);

                if (Ints.WithPercents(baseValor, p.Magnitude) > baseValor)
                    sirveEn++;
            }

            if (sirveEn == 0)
                problemas.Add($"el perk «{p.Key}» (+{p.Magnitude}%) no cambia el número en NINGUNA clase: trunca a cero");
        }

        // El jefe final tiene que existir, o la corrida no se puede ganar.
        int ultima = datos.Curves.RunCurve.AreaCount;
        var jefeFinal = datos.BossOf(ultima);

        if (jefeFinal is null)
            problemas.Add($"el área {ultima} no tiene jefe: la corrida no se puede terminar");
        else
            avisos.Add($"jefe final: «{jefeFinal.Name}» ({jefeFinal.Hp} HP) en la oleada {datos.Curves.RunCurve.WavesPerRun}");
    }

    private static void ValidarBestiario(BalanceData datos, List<string> problemas, List<string> avisos)
    {
        if (datos.Enemies.Count == 0)
        {
            problemas.Add("el bestiario está vacío: corré --importar-npcs");
            return;
        }

        var ids = new HashSet<int>();

        // Dos bichos indistinguibles: mismo cuerpo Y los mismos números del tick.
        //
        // Austral tiene "Rata" y "Rata Gigante" con Body=71 y stats idénticos, y
        // tres gallos con Body=5. En pantalla son el mismo sprite y en el tick son
        // el mismo enemigo: lo único que hacen es triplicarle las chances de salir
        // a ese bicho frente a los demás del área. El importador los descarta; esto
        // es la red por si vuelven.
        var firmas = new Dictionary<(int, int, int, int, int, int), string>();

        // El sprite compartido con stats DISTINTOS sí se acepta, pero tiene que
        // verse distinto: si no, el jugador ve dos lobos iguales y uno tiene el
        // triple de vida.
        var porCuerpo = new Dictionary<int, List<EnemyData>>();

        foreach (var e in datos.Enemies)
        {
            var firma = (e.Body, e.Hp, e.MinDamage, e.MaxDamage, e.Speed, e.CadenceTicks);

            if (firmas.TryGetValue(firma, out string? gemelo))
            {
                problemas.Add($"{e.Name} y {gemelo} son el mismo bicho: " +
                              $"cuerpo {e.Body} y los mismos números");
            }
            else
            {
                firmas[firma] = e.Name;
            }

            if (!porCuerpo.TryGetValue(e.Body, out var delCuerpo))
            {
                delCuerpo = [];
                porCuerpo[e.Body] = delCuerpo;
            }

            delCuerpo.Add(e);

            if (e.VisualScale is < 100 or > 200)
            {
                problemas.Add($"{e.Name} tiene escalaVisual {e.VisualScale}, " +
                              $"fuera de 100..200");
            }

            if (!ids.Add(e.Id))
                problemas.Add($"id de enemigo repetido: {e.Id} ({e.Name})");

            if (e.Hp <= 0)
                problemas.Add($"{e.Name} tiene {e.Hp} HP");

            if (e.Speed <= 0)
                problemas.Add($"{e.Name} tiene velocidad {e.Speed}: no se movería nunca");

            if (e.CadenceTicks <= 0)
                problemas.Add($"{e.Name} tiene cadencia {e.CadenceTicks}: pegaría infinitas veces por tick");

            if (e.MaxDamage < e.MinDamage)
                problemas.Add($"{e.Name} tiene danoMax {e.MaxDamage} < danoMin {e.MinDamage}");

            if (e.Area is < 1 or > 8)
                problemas.Add($"{e.Name} está en el área {e.Area}, fuera de 1..8");

            // Un enemigo más rápido que una celda por tick se teletransporta:
            // puede saltear el alcance de un defensor sin que llegue a dispararle.
            if (e.Speed >= Ints.ThousandthsPerCell)
                problemas.Add($"{e.Name} va a {e.Speed} milésimas/tick: cruza una celda entera por tick");
        }

        // ⚠ Que falte el arte es un AVISO, no un problema.
        //
        // El arte entra por tandas —hoy está el área 1 y nada más— porque el
        // bestiario entero son 97 MB. Un bicho sin sprite se dibuja con la
        // primitiva de siempre y el juego sigue andando; lo que no puede pasar es
        // que la clave esté MAL escrita, porque eso no se ve hasta que el bicho
        // sale en pantalla y aparece un círculo donde debería haber un lobo.
        string[] direcciones = ["norte", "este", "oeste", "sur"];
        int sinArte = 0;
        int conArte = 0;

        foreach (var e in datos.Enemies)
        {
            if (e.Graphic.Length == 0)
            {
                problemas.Add($"{e.Name} no tiene clave de gráfico: corré --importar-npcs");
                continue;
            }

            int tiene = 0;

            foreach (string d in direcciones)
            {
                if (CargadorGraficos.Datos($"{e.Graphic}.{d}") is not null)
                    tiene++;
            }

            if (tiene == 0)
            {
                sinArte++;
                continue;
            }

            conArte++;

            // Media pieza es peor que ninguna: el bicho se dibuja caminando hacia
            // el este y desaparece al doblar al norte, y eso parece un bug del
            // juego y no arte que falta.
            if (tiene < 4)
            {
                problemas.Add($"{e.Name} tiene {tiene} de 4 direcciones " +
                              $"(«{e.Graphic}»): se borraría al doblar");
            }
        }

        if (sinArte > 0)
        {
            avisos.Add($"{conArte} de {datos.Enemies.Count} enemigos tienen sprite; " +
                       $"a {sinArte} les falta");
        }

        // Los que comparten sprite tienen que tener tamaños distintos entre sí.
        foreach (var (cuerpo, delCuerpo) in porCuerpo)
        {
            if (delCuerpo.Count < 2)
                continue;

            var escalas = new HashSet<int>();

            foreach (var e in delCuerpo)
            {
                if (!escalas.Add(e.VisualScale))
                {
                    problemas.Add($"{e.Name} comparte el cuerpo {cuerpo} y el tamaño " +
                                  $"{e.VisualScale}% con otro: se verían iguales");
                }
            }
        }

        // Un área con pocos enemigos no tiene de dónde variar, y ahí la seed deja
        // de hacer trabajo: todas las corridas de esa área se parecen.
        for (int a = 1; a <= 8; a++)
        {
            int cuantos = datos.InArea(a).Count;

            if (cuantos == 0)
                problemas.Add($"el área {a} no tiene ningún enemigo");
            else if (cuantos < 3)
                avisos.Add($"el área {a} tiene solo {cuantos} enemigo(s): poca variedad");

            if (datos.BossOf(a) is null && cuantos > 0)
                problemas.Add($"el área {a} no tiene jefe");
        }
    }

    /// <summary>
    /// Que cada clase se pueda dibujar en sus tres tiers.
    /// </summary>
    /// <remarks>
    /// <para>Acá <b>sí</b> es un problema y no un aviso: el arte de las 9 clases
    /// está completo desde la primera tanda, así que una clave que no resuelve no
    /// es "arte que todavía no vino", es una clave rota. Y una clase sin sprite se
    /// dibuja con el cuadrado verde de siempre, que en pantalla parece una
    /// decisión de estilo y no un archivo faltante.</para>
    ///
    /// <para>Se chequean las cuatro direcciones: media pieza se borra al doblar.</para>
    /// </remarks>
    private static void ValidarApariencia(List<string> problemas, List<string> avisos)
    {
        string[] direcciones = ["norte", "este", "oeste", "sur"];
        int piezas = 0;

        foreach (DefenderClass clase in System.Enum.GetValues<DefenderClass>())
        {
            for (int tier = 1; tier <= 3; tier++)
            {
                var a = CargadorApariencia.De(clase, tier);

                if (a is null)
                {
                    problemas.Add($"{clase} tier {tier} no está en apariencia.json: " +
                                  $"corré --importar-apariencia");
                    continue;
                }

                foreach (string clave in new[] { a.Body, a.Head, a.Weapon })
                {
                    if (clave.Length == 0)
                    {
                        problemas.Add($"{clase} tier {tier} tiene una pieza vacía");
                        continue;
                    }

                    foreach (string d in direcciones)
                    {
                        if (CargadorGraficos.Datos($"{clave}.{d}") is null)
                            problemas.Add($"{clase} tier {tier}: falta «{clave}.{d}»");
                        else
                            piezas++;
                    }
                }

                // Un cuerpo sin offset de cabeza deja la cabeza a los pies. El
                // único que puede ser cero de verdad es el X, que casi siempre lo
                // es: los cuerpos están centrados.
                if (a.HeadOffset.Y == 0)
                {
                    problemas.Add($"{clase} tier {tier}: el cuerpo «{a.Body}» no trae " +
                                  $"offset de cabeza, quedaría a los pies");
                }
            }
        }

        avisos.Add($"apariencia: {piezas} piezas resueltas en 9 clases × 3 tiers");
    }

    private static void ValidarPerks(List<string> problemas, List<string> avisos)
    {
        var ids = new HashSet<int>();
        int oros = 0, unicos = 0;

        foreach (var p in CargadorDatos.Actual.Perks.All)
        {
            if (!ids.Add(p.Id))
                problemas.Add($"id de perk repetido: {p.Id} ({p.Key})");

            if (p.MaxStacked <= 0)
                problemas.Add($"el perk {p.Key} tiene maxApilado {p.MaxStacked}: no se podría equipar");

            // ⚠ La regla de apilado sale del tier: apilable si es un número,
            // único si es un comportamiento. Un Oro con maxApilado > 1 diría
            // "Ricochet dos veces", que no quiere decir nada.
            if (p.Tier is PerkTier.Gold or PerkTier.Unique && p.MaxStacked != 1)
                problemas.Add($"{p.Key} es {p.Tier} y apila {p.MaxStacked}: los comportamientos no se apilan");

            if (p.Tier == PerkTier.Unique)
            {
                unicos++;

                // Un Único es LA habilidad de una clase. Compartido entre varias
                // deja de ser único y pisa el lugar de los Oro.
                if (p.Classes.Length != 1)
                    problemas.Add($"{p.Key} es Único y sirve a {p.Classes.Length} clases: tiene que ser una");
            }

            if (p.Tier == PerkTier.Gold)
            {
                oros++;

                if (p.Classes.Length == 0)
                    avisos.Add($"{p.Key} es Gold y sirve a todas las clases: se esperaban 2-3");
            }

            if (p.Tier is PerkTier.Bronze or PerkTier.Silver && p.Magnitude <= 0)
                problemas.Add($"{p.Key} es {p.Tier} y no suma nada: los números tienen que sumar");
        }

        if (oros < 2)
            problemas.Add($"hay {oros} perk(s) de Oro: la fusión necesita al menos dos para combinar");

        avisos.Add($"catálogo: {CargadorDatos.Actual.Perks.All.Count} perks ({oros} Gold, {unicos} Único)");
    }

    private static void ValidarPlanos(List<string> problemas)
    {
        bool hayInicial = false;

        foreach (var plano in CargadorDatos.Actual.Blueprints.All)
        {
            if (plano.Initial)
                hayInicial = true;

            foreach (int id in new[] { plano.GoldA, plano.GoldB })
            {
                if (id < 0 || id >= CargadorDatos.Actual.Perks.All.Count)
                {
                    problemas.Add($"el plano {plano.Key} referencia el perk {id}, que no existe");
                    continue;
                }

                if (CargadorDatos.Actual.Perks.Get(id).Tier != PerkTier.Gold)
                    problemas.Add($"el plano {plano.Key} usa el perk {id}, que no es de Oro");
            }

            if (plano.ResultingUnique < 0 || plano.ResultingUnique >= CargadorDatos.Actual.Perks.All.Count)
            {
                problemas.Add($"el plano {plano.Key} produce el perk {plano.ResultingUnique}, que no existe");
            }
            else if (CargadorDatos.Actual.Perks.Get(plano.ResultingUnique).Tier != PerkTier.Unique)
            {
                problemas.Add($"el plano {plano.Key} produce un perk que no es Único");
            }

            if (plano.GoldA == plano.GoldB)
                problemas.Add($"el plano {plano.Key} pide dos veces el mismo Gold");
        }

        // Sin al menos una receta conocida, un jugador nuevo nunca ve un Único y
        // la fusión le resulta invisible hasta que gasta oro en algo que no
        // entiende para qué sirve.
        if (!hayInicial)
            problemas.Add("ningún plano es inicial: un jugador nuevo nunca vería un Único");
    }

    /// <summary>
    /// Revisa <b>todos</b> los layouts, no solo el de prueba.
    /// </summary>
    /// <remarks>
    /// Las tres reglas que chequea salieron de bugs medidos, no de teoría: un
    /// layout sin celdas pegadas a la ruta vuelve inútil al cuerpo a cuerpo, y uno
    /// con la Torre contra el borde deja enemigos imposibles de alcanzar. Los dos
    /// pasan desapercibidos hasta que alguien juega ese mapa.
    /// </remarks>
    private static void ValidarLayouts(List<string> problemas, List<string> avisos)
    {
        var cargados = CargadorDatos.Actual.Layouts;

        if (cargados.Count == 0)
        {
            problemas.Add("no hay layouts: corré --generar-layouts");
            return;
        }

        for (int i = 0; i < cargados.Count; i++)
        {
            var mapa = cargados[i];

            if (mapa.CeldasDeDefensaCortadas)
                problemas.Add($"layout {i}: la Torre está pegada al borde, su anillo de defensa queda cortado");

            int colocables = 0;
            int pegadasALaRuta = 0;

            for (int y = 0; y < mapa.Height; y++)
            {
                for (int x = 0; x < mapa.Width; x++)
                {
                    var c = new Cell(x, y);

                    if (!mapa.EsColocable(c))
                        continue;

                    colocables++;

                    if (AUnaCeldaDeLaRuta(mapa, c))
                        pegadasALaRuta++;
                }
            }

            if (colocables == 0)
                problemas.Add($"layout {i}: no tiene ninguna celda colocable");

            // ⚠ Sin celdas a UNA celda del camino, las clases cuerpo a cuerpo no
            // existen en ese mapa: se ponen, cuestan oro y no pegan nunca.
            if (pegadasALaRuta == 0)
                problemas.Add($"layout {i}: sin celdas pegadas a la ruta, el cuerpo a cuerpo sería inútil");

            // ⚠ El piso es 20, no 30.
            //
            // El 30 venía del campo de 30×16, donde las rutas medían 74 celdas. En
            // 20×11 no entra una ruta así, y midiendo resultó que el largo no es lo
            // que decide: una de 65 celdas —la más larga que entra— rinde PEOR que
            // una plegada de 31. Lo que cuenta es cuántas pasadas del camino ve un
            // mismo defensor. Ver OLEADAS.md §10.
            //
            // Queda un piso bajo para atajar el layout degenerado —una ruta de
            // cinco celdas donde nadie llega a disparar— y el largo parejo entre
            // layouts lo chequea --validar-layouts, que es donde importa.
            long celdas = mapa.Route.Length / Ints.ThousandthsPerCell;

            if (celdas < 20)
                problemas.Add($"layout {i}: la ruta mide {celdas} celdas, muy corta");

            avisos.Add($"layout {i}: ruta de {celdas} celdas · " +
                       $"{colocables} colocables ({pegadasALaRuta} pegadas)");
        }
    }

    private static bool AUnaCeldaDeLaRuta(Map mapa, Cell celda)
    {
        var ruta = mapa.Route;

        for (long d = 0; d <= ruta.Length; d += Ints.ThousandthsPerCell / 2)
        {
            var c = ruta.CellAt(d);

            if (System.Math.Abs(c.X - celda.X) <= 1 && System.Math.Abs(c.Y - celda.Y) <= 1)
                return true;
        }

        return false;
    }

    private static void ValidarCurvas(BalanceData datos, List<string> problemas, List<string> avisos)
    {
        var rng = new Rng(1);
        int npcsOleada1 = 0;
        int npcsOleada100 = 0;

        for (int oleada = 1; oleada <= CargadorDatos.Actual.Curves.RunCurve.WavesPerRun; oleada++)
        {
            var receta = Balance.RecipeOf(oleada, datos);

            if (receta.Budget <= 0)
                problemas.Add($"la oleada {oleada} tiene presupuesto {receta.Budget}");

            if (receta.Allowed.Length == 0)
                problemas.Add($"la oleada {oleada} no tiene enemigos permitidos");

            if (receta.SpawnIntervalTicks <= 0)
                problemas.Add($"la oleada {oleada} tiene intervalo de spawn {receta.SpawnIntervalTicks}");

            var armada = WaveBuilder.Armar(receta, rng, datos);

            if (armada.Npcs.Count == 0)
                problemas.Add($"la oleada {oleada} no compró ningún enemigo: se pasaría sola");

            if (oleada == 1) npcsOleada1 = armada.Npcs.Count;
            if (oleada == CargadorDatos.Actual.Curves.RunCurve.WavesPerRun) npcsOleada100 = armada.Npcs.Count;

            // Una oleada larguísima no es difícil: es larga. El tope de compra
            // existe justamente para eso, así que si esto salta, el tope no está
            // funcionando.
            if (armada.Npcs.Count > 80)
                problemas.Add($"la oleada {oleada} trae {armada.Npcs.Count} enemigos: demasiado larga");
        }

        // Las ocho oleadas de jefe son las únicas que sueltan Oro. Si el reparto
        // de áreas cambiara y quedaran menos, la fusión se volvería inalcanzable.
        int conJefe = 0;
        for (int oleada = 1; oleada <= CargadorDatos.Actual.Curves.RunCurve.WavesPerRun; oleada++)
            if (CargadorDatos.Actual.Areas.HasBoss(oleada))
                conJefe++;

        if (conJefe != CargadorDatos.Actual.Curves.RunCurve.AreaCount)
            problemas.Add($"hay {conJefe} oleadas con jefe y {CargadorDatos.Actual.Curves.RunCurve.AreaCount} áreas: tiene que haber una por área");

        if (!CargadorDatos.Actual.Areas.HasBoss(CargadorDatos.Actual.Curves.RunCurve.WavesPerRun))
            problemas.Add($"la oleada {CargadorDatos.Actual.Curves.RunCurve.WavesPerRun} no tiene jefe: es como se gana la corrida");

        avisos.Add($"curva: oleada 1 trae {npcsOleada1} enemigos, la 100 trae {npcsOleada100}");
    }
}
