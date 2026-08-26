namespace TowerOfArgentum.Simulacion;

/// <summary>
/// Números de balance, provisionales.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Nada de esto es definitivo y nada de esto es su casa final.</b>
/// Los números de balance viven en <c>Client\Datos\*.json</c>, con versión y
/// hash, porque son entrada de la simulación: cambiar uno vuelve irreplicables
/// las corridas viejas, y por eso cada rebalanceo es una temporada del ranking
/// (DATOS.md §7).</para>
///
/// <para>Existen acá, como constantes, para que la simulación se pueda escribir
/// y correr antes de que existan los JSON y su cargador. Cuando entre la Fase 6
/// esto se vacía y los valores salen de los datos. La firma de los métodos que
/// los usan no cambia.</para>
/// </remarks>
public static class Balance
{
    // -------------------------------------------------------------------------
    //  La Torre
    // -------------------------------------------------------------------------

    /// <summary>
    /// HP de la Torre. Provisional.
    ///
    /// <para>El diseño dice que su vida es proporcional a los NPCs de la oleada,
    /// a determinar en balance. Hasta que exista esa curva, un número redondo
    /// que alcanza para que un error se note sin que la primera prueba termine
    /// en cinco segundos.</para>
    /// </summary>
    public const int TorreHpInicial = 1000;

    /// <summary>
    /// Lo que pega la Torre a quien la esté mordiendo, y cada cuánto.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>La Torre se defiende sola</b> (pedido 2026-08-26). Un enemigo
    /// que llega y se queda pegado a ella, fuera del alcance de todos los
    /// defensores, frenaba la oleada <b>para siempre</b>: no moría, la oleada no
    /// se limpiaba, el bono no llegaba, y la corrida se convertía en ver caer
    /// 1.000 HP de a 5. Medido con el corredor: seeds clavadas 27.000 ticks en la
    /// oleada 1.</para>
    ///
    /// <para>20 parejo y una vez por segundo: mata una rata en un segundo y no le
    /// hace nada a un jefe de 8.400. Es una <b>red de seguridad</b>, no un
    /// defensor — si pegara fuerte, no poner nada cerca de la Torre sería una
    /// estrategia, y la Torre es lo que se defiende, no con lo que se
    /// defiende.</para>
    /// </remarks>
    public const int TorreDano = 20;

    public const int TorreCadenciaTicks = Enteros.TicksPorSegundo;

    // -------------------------------------------------------------------------
    //  La corrida
    // -------------------------------------------------------------------------

    /// <summary>
    /// Oleadas de una corrida. <b>Esto no es provisional: es la regla.</b>
    ///
    /// <para>La corrida termina en la 100 matando al jefe final del área 8. No
    /// hay modo sin fin y no hay área 9.</para>
    /// </summary>
    public const int OleadasPorCorrida = 100;

    /// <summary>
    /// Áreas por las que pasa una corrida.
    /// </summary>
    /// <remarks>
    /// ⚠ Se llama CantidadAreas y no Areas porque `Areas` es la clase que reparte
    /// las 100 oleadas entre las ocho. Una constante con ese nombre acá la tapaba
    /// y el compilador se quejaba de que un int no tiene DeOleada().
    /// </remarks>
    public const int CantidadAreas = 8;

    /// <summary>
    /// Cada cuántas oleadas aparece la pantalla de elección (levelear o
    /// contratar). Provisional: es la curva de poder del jugador y hay que
    /// jugarla para saber si son 3.
    /// </summary>
    public const int OleadasEntreElecciones = 3;

    /// <summary>
    /// Oro por cada enemigo que sigue en pie al adelantar la oleada.
    /// </summary>
    /// <remarks>
    /// ⚠ Reemplaza a <c>TicksEntreOleadas</c>, que ya no existe: el hueco entre
    /// oleadas dejó de correr el reloj y pasó a ser la fase de preparación, en
    /// pausa. El bonus por adelantar pagaba por saltear esa espera, y saltear algo
    /// gratis no vale nada; ahora paga por <b>el riesgo</b>, que es cuántos bichos
    /// te quedan encima cuando llamás a la siguiente.
    /// </remarks>
    public const int OroPorEnemigoAdelantado = 12;

    // -------------------------------------------------------------------------
    //  Defensores
    // -------------------------------------------------------------------------

    /// <summary>Slots de perk por clase, que se abren al levelear.</summary>
    public const int SlotsPerkPorClase = 6;

    /// <summary>Lugares de la mochila, compartidos entre perks y equipo.</summary>
    public const int LugaresMochila = 15;

    /// <summary>
    /// Slots de equipo por clase: <b>uno, el arma</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Armadura, casco y escudo <b>no dan nada</b>: son el frame que
    /// cambia cuando la clase sube de nivel (ClaseEnRoster.TierVisual). Todo el
    /// poder del equipo vive en el arma.</para>
    ///
    /// <para>Antes eran dos. Concentrarlo en uno hace la decisión de equipar
    /// nítida —hay un solo lugar, y lo que entra saca lo que estaba— y deja que
    /// las armas sean mucho más locas sin desbalancear: con cuatro piezas con
    /// habilidades habría que pensar cada combinación de cuatro.</para>
    /// </remarks>
    public const int SlotsEquipoPorClase = 1;

    // -------------------------------------------------------------------------
    //  La curva del drop
    // -------------------------------------------------------------------------

    /// <summary>
    /// Chance de que un bicho común suelte algo, en porcentaje entero.
    /// </summary>
    /// <remarks>
    /// <para>4% con ~40 bichos por oleada da 1,6 drops por oleada, o sea unos 20
    /// por área. Con 15 lugares de mochila, eso aprieta recién entrando a la
    /// segunda área — que es exactamente el ritmo que el diseño quiere: un tope
    /// que existe pero no molesta al principio.</para>
    ///
    /// <para>No sube con la oleada. Lo que sube es la <b>calidad</b>, no la
    /// cantidad: más drops tarde solo llenarían la mochila más rápido, y la
    /// decisión de qué tirar se volvería constante en vez de puntual.</para>
    /// </remarks>
    public const int ChanceDropComun = 4;

    /// <summary>
    /// El presupuesto del equipo que suelta un jefe, por oleada.
    /// </summary>
    /// <remarks>
    /// <para><b>Esta curva se balancea contra la del presupuesto de oleada.</b>
    /// La de la oleada es cuadrática (<c>50 + 9n²</c>) y esta es lineal, y eso es
    /// deliberado: si las dos crecieran igual, el jugador iría siempre al mismo
    /// ritmo que la dificultad y la corrida no se sentiría más dura nunca.</para>
    ///
    /// <para>Que la dificultad crezca más rápido que el equipo es lo que hace que
    /// la corrida termine — que es el punto de que sean 100 oleadas y no infinitas.
    /// La diferencia la tiene que cubrir el jugador jugando mejor: mejores
    /// posiciones, mejores fusiones, adelantar cuando conviene.</para>
    /// </remarks>
    public static int PresupuestoDropJefe(int oleada) => 40 + (oleada * 6);

    /// <summary>El presupuesto de un ítem de tienda, más chico que el de un jefe.</summary>
    /// <remarks>
    /// Más chico a propósito: lo que se compra tiene que ser peor que lo que se
    /// gana matando al jefe, o matar al jefe deja de valer la pena.
    /// </remarks>
    public static int PresupuestoItemTienda(int oleada) => 30 + (oleada * 5);

    /// <summary>
    /// Cuánto devuelve vender un defensor, en porcentaje de lo que costó.
    /// </summary>
    public const int PorcentajeVenta = 50;

    /// <summary>
    /// Stats base por clase: costo en oro, daño por golpe, alcance en celdas y
    /// cadencia en ticks.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>La regla del daño es: más alcance, menos golpe.</b> (Pedido del
    /// 2026-08-26.) Un melee ve pasar a cada bicho una sola vez por delante; uno
    /// de rango cubre media ruta y le pega a todo lo que camina por ella. Si
    /// encima pegara igual, el de rango jugaría solo — que es exactamente lo que
    /// pasaba con el mago de 40.</para>
    ///
    /// <para>⚠ <b>El daño base tiene techo en 10</b> (pedido 2026-08-26). Los
    /// bichos del escalón 0 tienen 20-45 HP, y con más de 10 el primer golpe
    /// mataba: la oleada 1 se veía como un tablero borrando cosas sola. Con el
    /// techo hacen falta <b>2 o 3 golpes</b> por bicho, que es donde se ve si
    /// estás pegando o no.</para>
    ///
    /// <para>Con el techo puesto, quien diferencia es la <b>cadencia</b>. En DPS
    /// (daño × 30 / cadencia): guerrero 16 · paladín 12,5 · asesino 10 ·
    /// bardo 8,6 · clérigo 8,75 · ladrón 8,3 · druida 7,8 · mago 5 · cazador 4,3.
    /// El ladrón queda abajo de su escalón a propósito: lo barato tapa huecos,
    /// no gana corridas. Los números se calibran contra la curva de HP con
    /// <c>--balance</c>, no a ojo.</para>
    ///
    /// <para>El alcance se guarda <b>al cuadrado</b> porque así se compara: la
    /// simulación no calcula raíces (JUGABILIDAD.md §4.2). Es la misma razón por
    /// la que <see cref="Enteros.DistanciaCuadrado"/> no las devuelve.</para>
    /// </remarks>
    public static (int Costo, int Dano, int AlcanceCeldas, int CadenciaTicks) StatsDe(Clase clase)
        => clase switch
        {
            // Alcance 4: pega suave. Resuelve grupos por cobertura, no por golpe.
            Clase.Mago => (100, 5, 4, 30),

            // Alcance 3 y pega BIEN: el rol de soporte (curar) todavía no existe,
            // y mientras tanto un clérigo de 8 era un defensor que no defendía.
            Clase.Clerigo => (80, 7, 3, 24),

            // Cuerpo a cuerpo, pero alcance 2 y no 1.
            //
            // ⚠ Con alcance 1 y distancia euclídea, una celda EN DIAGONAL queda a
            // √2 ≈ 1,41 y no entra: el alcance 1 cubre solo las cuatro celdas
            // ortogonales. Un guerrero puesto en diagonal respecto del camino no
            // llegaba a tocar nada, y el jugador no tenía forma de saber por qué.
            //
            // Medido con el corredor: corridas que morían en la oleada 1 con
            // cinco guerreros puestos y ningún enemigo muerto.
            Clase.Guerrero => (60, 8, 2, 15),

            // El golpe más grande del roster, lento: identidad de burst. Sigue la
            // regla — alcance 2 — solo que concentrado en menos golpes.
            Clase.Asesino => (90, 10, 2, 30),

            // Barato y flojo: sirve para tapar un hueco.
            Clase.Ladron => (40, 5, 2, 18),

            // Alcance 3, medio: cuando exista el soporte va a bajar de nuevo.
            Clase.Bardo => (70, 6, 3, 21),

            Clase.Druida => (85, 7, 3, 27),

            Clase.Paladin => (95, 10, 2, 24),

            // ⚠ Cazador, NO "arquero". El de más alcance del roster, y por la
            // regla, el golpe más suave.
            Clase.Cazador => (75, 3, 6, 21),

            _ => (100, 4, 2, 30),
        };

    // -------------------------------------------------------------------------
    //  Enemigos
    // -------------------------------------------------------------------------

    /// <summary>
    /// El bestiario provisional.
    /// </summary>
    /// <remarks>
    /// Los stats reales salen de <c>NPCs.dat</c> de Austral (251 NPCs, Latin-1)
    /// pasados por el importador y <b>reescalados</b>: los de AO están pensados
    /// para un MMO donde un personaje pelea de a uno, no para una oleada de
    /// treinta. Ver OLEADAS.md §4. Los nombres y el escalón de cada uno sí salen
    /// de ahí; los números de acá son de relleno.
    /// </remarks>
    public const int Rata = 0;
    public const int Gallo = 1;
    public const int Murcielago = 2;
    public const int Serpiente = 3;
    public const int Lobo = 4;
    public const int Cuervo = 5;
    public const int Oso = 6;
    public const int Golem = 7;
    public const int DragonRojo = 8;

    public static string NombreDe(int tipoId) => tipoId switch
    {
        Rata => "rata gigante",
        Gallo => "gallo salvaje",
        Murcielago => "murciélago",
        Serpiente => "serpiente",
        Lobo => "lobo",
        Cuervo => "cuervo",
        Oso => "oso pardo",
        Golem => "golem",
        DragonRojo => "dragón rojo",
        _ => "?",
    };

    /// <summary>
    /// Velocidad en milésimas de celda por tick. A 30 Hz, 40 son 1,2 celdas por
    /// segundo: una ruta de 40 celdas se recorre en ~33 segundos.
    /// </summary>
    public static (int Hp, int Velocidad, int Dano, int CadenciaTicks, int Oro) EnemigoBase(int tipoId)
        => tipoId switch
        {
            Rata => (Hp: 20, Velocidad: 45, Dano: 5, CadenciaTicks: 24, Oro: 3),
            Gallo => (Hp: 28, Velocidad: 38, Dano: 7, CadenciaTicks: 30, Oro: 4),

            // Rápido y frágil: el que se escapa si el jugador puso todo al final.
            Murcielago => (Hp: 24, Velocidad: 70, Dano: 6, CadenciaTicks: 20, Oro: 5),

            Serpiente => (Hp: 45, Velocidad: 35, Dano: 14, CadenciaTicks: 36, Oro: 7),
            Lobo => (Hp: 60, Velocidad: 48, Dano: 12, CadenciaTicks: 30, Oro: 8),
            Cuervo => (Hp: 40, Velocidad: 62, Dano: 9, CadenciaTicks: 24, Oro: 7),

            // Jefe de área temprana: lento, mucha vida.
            Oso => (Hp: 420, Velocidad: 26, Dano: 45, CadenciaTicks: 45, Oro: 90),

            Golem => (Hp: 1400, Velocidad: 18, Dano: 80, CadenciaTicks: 60, Oro: 300),

            // ⚠ El jefe final, oleada 100. Matarlo es cómo se gana.
            DragonRojo => (Hp: 9000, Velocidad: 30, Dano: 260, CadenciaTicks: 45, Oro: 2500),

            _ => (Hp: 30, Velocidad: 35, Dano: 8, CadenciaTicks: 30, Oro: 4),
        };

    /// <summary>
    /// El bestiario provisional, envuelto como <see cref="DatosBalance"/>.
    /// </summary>
    /// <remarks>
    /// <para>Es la red de seguridad: si el importador no corrió, o si alguien
    /// arma una <see cref="Partida"/> sin pasarle datos, la simulación funciona
    /// igual con estos nueve bichos en vez de tirar una excepción.</para>
    ///
    /// <para>⚠ El hash dice <c>"provisorio"</c>, que nunca va a coincidir con el
    /// del servidor. Eso es a propósito: una corrida jugada con este bestiario se
    /// rechaza al abrirse, en vez de terminar en el ranking midiendo otra cosa que
    /// las demás.</para>
    /// </remarks>
    public static DatosBalance DatosProvisorios { get; } = ConstruirProvisorios();

    private static DatosBalance ConstruirProvisorios()
    {
        var lista = new List<DatoEnemigo>();

        for (int id = Rata; id <= DragonRojo; id++)
        {
            var b = EnemigoBase(id);

            lista.Add(new DatoEnemigo
            {
                Id = id,
                Nombre = NombreDe(id),

                // Ocho tipos repartidos a ojo en las ocho áreas. Es provisorio y
                // no importa que sea prolijo: lo que importa es que exista.
                Area = Enteros.Limitar(id + 1, 1, 8),

                Hp = b.Hp,
                DanoMin = b.Dano,
                DanoMax = b.Dano,
                Velocidad = b.Velocidad,
                CadenciaTicks = b.CadenciaTicks,
                Oro = b.Oro,
            });
        }

        var datos = new DatosBalance
        {
            Version = 0,
            Hash = "provisorio",
            Enemigos = lista,

            // Sin layouts: este bestiario provisorio es para arrancar sin datos,
            // y el mapa lo pone quien construye la Partida.
            Layouts = [],
        };

        datos.Indexar();
        return datos;
    }

    // -------------------------------------------------------------------------
    //  La curva del presupuesto
    // -------------------------------------------------------------------------

    /// <summary>
    /// La receta de una oleada.
    /// </summary>
    /// <remarks>
    /// <para><b>La curva es cuadrática: <c>50 + 9n²</c>.</b> Lineal es aburrido
    /// —la oleada 80 se siente igual que la 40— y exponencial se descontrola: a
    /// la oleada 100 el número deja de tener sentido y no hay defensa que
    /// alcance.</para>
    ///
    /// <para>La cuadrática viene de los puntos de ejemplo de OLEADAS.md; la
    /// constante bajó de 50 a 35 cuando el oro inicial bajó a dos fichas. Hoy:
    /// oleada 1 = 44, oleada 10 = 935, oleada 100 = 90.035.</para>
    ///
    /// <para>⚠ Esto va a cambiar cuando se pueda medir con el corredor headless,
    /// y <b>cambiarlo es una temporada nueva del ranking</b>: las corridas viejas
    /// dejan de ser reproducibles.</para>
    /// </remarks>
    public static RecetaOleada RecetaDe(int oleada, DatosBalance datos)
    {
        int area = Areas.DeOleada(oleada);
        // La constante era 50, calibrada para 400 de oro inicial. Con dos fichas
        // hay que aflojar el arranque, pero no tanto como para que la oleada 1
        // traiga UN bicho: con 20 traía uno solo y no se entendía qué pasaba.
        // 35 da 44 de presupuesto, o sea dos. La cola cuadrática no se toca.
        int presupuesto = 35 + (9 * oleada * oleada);

        // El jefe cierra el área. Son ocho por corrida, y como el jefe es la
        // única fuente de perks de Oro, eso le pone techo a cuántos puede juntar
        // un jugador — que es lo que hace que fusionar sea una decisión.
        int jefe = -1;

        if (Areas.TieneJefe(oleada))
            jefe = datos.JefeDe(area)?.Id ?? -1;

        return new RecetaOleada
        {
            Numero = oleada,
            Presupuesto = presupuesto,
            Permitidos = PermitidosDe(area, datos),
            JefeTipoId = jefe,

            // Sube con la corrida: más adelante, más bichos con perk.
            ChancePerkNpc = Enteros.Limitar(oleada / 2, 0, 30),

            // Los spawns se aprietan con las oleadas, así que la presión no
            // crece solo por cantidad sino por concentración.
            IntervaloSpawnTicks = Enteros.Limitar(45 - (oleada / 3), 8, 45),

            // La mitad de lo que era: con 20 + oleada×12 el tablero se llenaba en
            // pocas oleadas y el oro quedaba suelto sin nada que comprar.
            BonusOro = 10 + (oleada * 6),
        };
    }

    /// <summary>
    /// Los enemigos que pueden salir en un área, con su peso.
    /// </summary>
    /// <remarks>
    /// <para>Se toman del área actual <b>y de la anterior</b>. Que los bichos de
    /// la zona previa sigan apareciendo un rato hace dos cosas: suaviza el salto
    /// entre áreas —que si no se siente como un muro— y le da al jugador la
    /// satisfacción de ver cómo lo que antes lo mataba ahora cae de un golpe.</para>
    ///
    /// <para>Los del área anterior pesan menos, así que el área nueva domina la
    /// composición sin que la vieja desaparezca de golpe.</para>
    /// </remarks>
    private static (int TipoId, int Peso)[] PermitidosDe(int area, DatosBalance datos)
    {
        var actual = datos.DeArea(area);
        var anterior = area > 1 ? datos.DeArea(area - 1) : [];

        var lista = new List<(int, int)>(actual.Count + anterior.Count);

        foreach (var e in actual)
            lista.Add((e.Id, 40));

        foreach (var e in anterior)
            lista.Add((e.Id, 15));

        // Sin datos —el bestiario no se importó— se cae al lobo provisional, para
        // que la simulación corra igual en vez de tirar una excepción.
        if (lista.Count == 0)
            lista.Add((Lobo, 100));

        return [.. lista];
    }
}
