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
    private static void ValidarTablas(DatosBalance datos, List<string> problemas, List<string> avisos)
    {
        // Toda clase del enum tiene que estar en clases.json: si falta una, el
        // juego funciona hasta que la elección la ofrece y ahí tira.
        foreach (var clase in ArmadorEleccion.Todas)
        {
            try
            {
                datos.Clases.De(clase);
            }
            catch (DatosInvalidosException)
            {
                problemas.Add($"clases.json no define «{clase}», y la elección la puede ofrecer");
            }
        }

        avisos.Add(
            $"clases: {datos.Clases.Todas.Count} · {datos.Clases.Ofensivas().Length} ofensivas · " +
            $"nivel máximo {datos.Clases.SlotsPerkPorClase} (+{datos.Clases.ExtraDanoPorNivel}% daño por nivel)");

        // ⚠ El truncamiento entero se come mejoras enteras cuando el daño base es
        // chico. Es la anomalía P0 de la propuesta V2 y no es un error de datos,
        // pero es invisible: el jugador gasta oro en algo que no hace nada.
        foreach (var c in datos.Clases.Todas)
        {
            int mudos = 0;

            for (int nivel = 2; nivel <= datos.Clases.SlotsPerkPorClase; nivel++)
            {
                int anterior = Enteros.ConPorcentajes(c.Dano, (nivel - 2) * datos.Clases.ExtraDanoPorNivel);
                int actual = Enteros.ConPorcentajes(c.Dano, (nivel - 1) * datos.Clases.ExtraDanoPorNivel);

                if (actual == anterior)
                    mudos++;
            }

            if (mudos > 0)
            {
                avisos.Add(
                    $"{c.Clase}: daño base {c.Dano}, {mudos} de {datos.Clases.SlotsPerkPorClase - 1} niveles " +
                    "NO suben nada por truncamiento entero");
            }
        }

        // Lo mismo con los perks numéricos: un perk que no mueve el número en
        // ninguna clase es un slot y un drop gastados en nada.
        foreach (var p in datos.Perks.Todos)
        {
            if (p.Efecto is not (EfectoPerk.Dano or EfectoPerk.Alcance))
                continue;

            int sirveEn = 0;

            foreach (var c in datos.Clases.Todas)
            {
                if (!p.SirveA(c.Clase))
                    continue;

                int baseValor = p.Efecto == EfectoPerk.Dano ? c.Dano : c.AlcanceCeldas;

                if (Enteros.ConPorcentajes(baseValor, p.Magnitud) > baseValor)
                    sirveEn++;
            }

            if (sirveEn == 0)
                problemas.Add($"el perk «{p.Nombre}» (+{p.Magnitud}%) no cambia el número en NINGUNA clase: trunca a cero");
        }

        // El jefe final tiene que existir, o la corrida no se puede ganar.
        int ultima = datos.Curvas.Corrida.CantidadAreas;
        var jefeFinal = datos.JefeDe(ultima);

        if (jefeFinal is null)
            problemas.Add($"el área {ultima} no tiene jefe: la corrida no se puede terminar");
        else
            avisos.Add($"jefe final: «{jefeFinal.Nombre}» ({jefeFinal.Hp} HP) en la oleada {datos.Curvas.Corrida.OleadasPorCorrida}");
    }

    private static void ValidarBestiario(DatosBalance datos, List<string> problemas, List<string> avisos)
    {
        if (datos.Enemigos.Count == 0)
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
        var porCuerpo = new Dictionary<int, List<DatoEnemigo>>();

        foreach (var e in datos.Enemigos)
        {
            var firma = (e.Body, e.Hp, e.DanoMin, e.DanoMax, e.Velocidad, e.CadenciaTicks);

            if (firmas.TryGetValue(firma, out string? gemelo))
            {
                problemas.Add($"{e.Nombre} y {gemelo} son el mismo bicho: " +
                              $"cuerpo {e.Body} y los mismos números");
            }
            else
            {
                firmas[firma] = e.Nombre;
            }

            if (!porCuerpo.TryGetValue(e.Body, out var delCuerpo))
            {
                delCuerpo = [];
                porCuerpo[e.Body] = delCuerpo;
            }

            delCuerpo.Add(e);

            if (e.EscalaVisual is < 100 or > 200)
            {
                problemas.Add($"{e.Nombre} tiene escalaVisual {e.EscalaVisual}, " +
                              $"fuera de 100..200");
            }

            if (!ids.Add(e.Id))
                problemas.Add($"id de enemigo repetido: {e.Id} ({e.Nombre})");

            if (e.Hp <= 0)
                problemas.Add($"{e.Nombre} tiene {e.Hp} HP");

            if (e.Velocidad <= 0)
                problemas.Add($"{e.Nombre} tiene velocidad {e.Velocidad}: no se movería nunca");

            if (e.CadenciaTicks <= 0)
                problemas.Add($"{e.Nombre} tiene cadencia {e.CadenciaTicks}: pegaría infinitas veces por tick");

            if (e.DanoMax < e.DanoMin)
                problemas.Add($"{e.Nombre} tiene danoMax {e.DanoMax} < danoMin {e.DanoMin}");

            if (e.Area is < 1 or > 8)
                problemas.Add($"{e.Nombre} está en el área {e.Area}, fuera de 1..8");

            // Un enemigo más rápido que una celda por tick se teletransporta:
            // puede saltear el alcance de un defensor sin que llegue a dispararle.
            if (e.Velocidad >= Enteros.MilesimasPorCelda)
                problemas.Add($"{e.Nombre} va a {e.Velocidad} milésimas/tick: cruza una celda entera por tick");
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

        foreach (var e in datos.Enemigos)
        {
            if (e.Grafico.Length == 0)
            {
                problemas.Add($"{e.Nombre} no tiene clave de gráfico: corré --importar-npcs");
                continue;
            }

            int tiene = 0;

            foreach (string d in direcciones)
            {
                if (CargadorGraficos.Datos($"{e.Grafico}.{d}") is not null)
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
                problemas.Add($"{e.Nombre} tiene {tiene} de 4 direcciones " +
                              $"(«{e.Grafico}»): se borraría al doblar");
            }
        }

        if (sinArte > 0)
        {
            avisos.Add($"{conArte} de {datos.Enemigos.Count} enemigos tienen sprite; " +
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
                if (!escalas.Add(e.EscalaVisual))
                {
                    problemas.Add($"{e.Nombre} comparte el cuerpo {cuerpo} y el tamaño " +
                                  $"{e.EscalaVisual}% con otro: se verían iguales");
                }
            }
        }

        // Un área con pocos enemigos no tiene de dónde variar, y ahí la seed deja
        // de hacer trabajo: todas las corridas de esa área se parecen.
        for (int a = 1; a <= 8; a++)
        {
            int cuantos = datos.DeArea(a).Count;

            if (cuantos == 0)
                problemas.Add($"el área {a} no tiene ningún enemigo");
            else if (cuantos < 3)
                avisos.Add($"el área {a} tiene solo {cuantos} enemigo(s): poca variedad");

            if (datos.JefeDe(a) is null && cuantos > 0)
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

        foreach (Clase clase in System.Enum.GetValues<Clase>())
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

                foreach (string clave in new[] { a.Cuerpo, a.Cabeza, a.Arma })
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
                if (a.CabezaOffset.Y == 0)
                {
                    problemas.Add($"{clase} tier {tier}: el cuerpo «{a.Cuerpo}» no trae " +
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

        foreach (var p in CargadorDatos.Actual.Perks.Todos)
        {
            if (!ids.Add(p.Id))
                problemas.Add($"id de perk repetido: {p.Id} ({p.Nombre})");

            if (p.MaxApilado <= 0)
                problemas.Add($"el perk {p.Nombre} tiene maxApilado {p.MaxApilado}: no se podría equipar");

            // ⚠ La regla de apilado sale del tier: apilable si es un número,
            // único si es un comportamiento. Un Oro con maxApilado > 1 diría
            // "Ricochet dos veces", que no quiere decir nada.
            if (p.Tier is TierPerk.Oro or TierPerk.Unico && p.MaxApilado != 1)
                problemas.Add($"{p.Nombre} es {p.Tier} y apila {p.MaxApilado}: los comportamientos no se apilan");

            if (p.Tier == TierPerk.Unico)
            {
                unicos++;

                // Un Único es LA habilidad de una clase. Compartido entre varias
                // deja de ser único y pisa el lugar de los Oro.
                if (p.Clases.Length != 1)
                    problemas.Add($"{p.Nombre} es Único y sirve a {p.Clases.Length} clases: tiene que ser una");
            }

            if (p.Tier == TierPerk.Oro)
            {
                oros++;

                if (p.Clases.Length == 0)
                    avisos.Add($"{p.Nombre} es Oro y sirve a todas las clases: se esperaban 2-3");
            }

            if (p.Tier is TierPerk.Bronce or TierPerk.Plata && p.Magnitud <= 0)
                problemas.Add($"{p.Nombre} es {p.Tier} y no suma nada: los números tienen que sumar");
        }

        if (oros < 2)
            problemas.Add($"hay {oros} perk(s) de Oro: la fusión necesita al menos dos para combinar");

        avisos.Add($"catálogo: {CargadorDatos.Actual.Perks.Todos.Count} perks ({oros} Oro, {unicos} Único)");
    }

    private static void ValidarPlanos(List<string> problemas)
    {
        bool hayInicial = false;

        foreach (var plano in CargadorDatos.Actual.Planos.Todos)
        {
            if (plano.Inicial)
                hayInicial = true;

            foreach (int id in new[] { plano.OroA, plano.OroB })
            {
                if (id < 0 || id >= CargadorDatos.Actual.Perks.Todos.Count)
                {
                    problemas.Add($"el plano {plano.Nombre} referencia el perk {id}, que no existe");
                    continue;
                }

                if (CargadorDatos.Actual.Perks.Por(id).Tier != TierPerk.Oro)
                    problemas.Add($"el plano {plano.Nombre} usa el perk {id}, que no es de Oro");
            }

            if (plano.UnicoResultante < 0 || plano.UnicoResultante >= CargadorDatos.Actual.Perks.Todos.Count)
            {
                problemas.Add($"el plano {plano.Nombre} produce el perk {plano.UnicoResultante}, que no existe");
            }
            else if (CargadorDatos.Actual.Perks.Por(plano.UnicoResultante).Tier != TierPerk.Unico)
            {
                problemas.Add($"el plano {plano.Nombre} produce un perk que no es Único");
            }

            if (plano.OroA == plano.OroB)
                problemas.Add($"el plano {plano.Nombre} pide dos veces el mismo Oro");
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

            for (int y = 0; y < mapa.Alto; y++)
            {
                for (int x = 0; x < mapa.Ancho; x++)
                {
                    var c = new Celda(x, y);

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
            long celdas = mapa.Ruta.Largo / Enteros.MilesimasPorCelda;

            if (celdas < 20)
                problemas.Add($"layout {i}: la ruta mide {celdas} celdas, muy corta");

            avisos.Add($"layout {i}: ruta de {celdas} celdas · " +
                       $"{colocables} colocables ({pegadasALaRuta} pegadas)");
        }
    }

    private static bool AUnaCeldaDeLaRuta(Mapa mapa, Celda celda)
    {
        var ruta = mapa.Ruta;

        for (long d = 0; d <= ruta.Largo; d += Enteros.MilesimasPorCelda / 2)
        {
            var c = ruta.CeldaEn(d);

            if (System.Math.Abs(c.X - celda.X) <= 1 && System.Math.Abs(c.Y - celda.Y) <= 1)
                return true;
        }

        return false;
    }

    private static void ValidarCurvas(DatosBalance datos, List<string> problemas, List<string> avisos)
    {
        var rng = new Aleatorio(1);
        int npcsOleada1 = 0;
        int npcsOleada100 = 0;

        for (int oleada = 1; oleada <= CargadorDatos.Actual.Curvas.Corrida.OleadasPorCorrida; oleada++)
        {
            var receta = Balance.RecetaDe(oleada, datos);

            if (receta.Presupuesto <= 0)
                problemas.Add($"la oleada {oleada} tiene presupuesto {receta.Presupuesto}");

            if (receta.Permitidos.Length == 0)
                problemas.Add($"la oleada {oleada} no tiene enemigos permitidos");

            if (receta.IntervaloSpawnTicks <= 0)
                problemas.Add($"la oleada {oleada} tiene intervalo de spawn {receta.IntervaloSpawnTicks}");

            var armada = ArmadorOleadas.Armar(receta, rng, datos);

            if (armada.Npcs.Count == 0)
                problemas.Add($"la oleada {oleada} no compró ningún enemigo: se pasaría sola");

            if (oleada == 1) npcsOleada1 = armada.Npcs.Count;
            if (oleada == CargadorDatos.Actual.Curvas.Corrida.OleadasPorCorrida) npcsOleada100 = armada.Npcs.Count;

            // Una oleada larguísima no es difícil: es larga. El tope de compra
            // existe justamente para eso, así que si esto salta, el tope no está
            // funcionando.
            if (armada.Npcs.Count > 80)
                problemas.Add($"la oleada {oleada} trae {armada.Npcs.Count} enemigos: demasiado larga");
        }

        // Las ocho oleadas de jefe son las únicas que sueltan Oro. Si el reparto
        // de áreas cambiara y quedaran menos, la fusión se volvería inalcanzable.
        int conJefe = 0;
        for (int oleada = 1; oleada <= CargadorDatos.Actual.Curvas.Corrida.OleadasPorCorrida; oleada++)
            if (CargadorDatos.Actual.Areas.TieneJefe(oleada))
                conJefe++;

        if (conJefe != CargadorDatos.Actual.Curvas.Corrida.CantidadAreas)
            problemas.Add($"hay {conJefe} oleadas con jefe y {CargadorDatos.Actual.Curvas.Corrida.CantidadAreas} áreas: tiene que haber una por área");

        if (!CargadorDatos.Actual.Areas.TieneJefe(CargadorDatos.Actual.Curvas.Corrida.OleadasPorCorrida))
            problemas.Add($"la oleada {CargadorDatos.Actual.Curvas.Corrida.OleadasPorCorrida} no tiene jefe: es como se gana la corrida");

        avisos.Add($"curva: oleada 1 trae {npcsOleada1} enemigos, la 100 trae {npcsOleada100}");
    }
}
