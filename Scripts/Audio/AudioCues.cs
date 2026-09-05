using System.Collections.Generic;
using TowerOfArgentum.Simulation;

namespace TowerOfArgentum.Audio;

/// <summary>
/// Mira la simulación y decide qué suena. El puente entre el tick y el audio.
/// </summary>
/// <remarks>
/// <para>⚠ <b>La simulación no llama a nadie.</b> No hay eventos, no hay
/// callbacks y no va a haberlos: <c>Simulation\</c> no puede depender del motor
/// —el servidor la corre sin Godot— y una lista de «cosas que pasaron este tick»
/// sería estado nuevo que hay que serializar, limpiar al cargar una partida y
/// mantener idéntico en las dos mitades. Un campo de más ahí adentro es un
/// determinismo roto en potencia.</para>
///
/// <para><b>Por eso esto compara fotos.</b> Guarda lo que había antes del tick,
/// mira lo que hay después y deduce qué pasó. Es exactamente lo que ya hace
/// <c>Game.DibujarDisparos</c> con <see cref="Defender.LastHitTick"/> para
/// dibujar los proyectiles sin mantener una lista de proyectiles, y funciona por
/// el mismo motivo: nada que limpiar cuando un bicho muere, nada que se
/// desincronice al pausar, al acelerar o al cargar una corrida guardada.</para>
///
/// <para><b>Los impactos van en cola.</b> El daño se cobró en el tick del
/// disparo, pero el proyectil tarda <c>Game.TicksDeVuelo</c> en llegar a la
/// pantalla. Sonar el impacto en el momento del disparo se oye como un solo golpe
/// seco y tira abajo la lectura de «esto salió de allá y llegó acá», así que se
/// encola y se dispara cuando el sprite llega.</para>
/// </remarks>
public sealed class AudioCues
{
    /// <summary>Los enemigos que estaban vivos la última vez que miramos.</summary>
    private HashSet<int> _vivos = [];

    /// <summary>La foto de este tick. Se intercambia con la anterior, no se copia.</summary>
    private HashSet<int> _ahora = [];

    /// <summary>Vida total de los enemigos en la foto anterior. Ver <c>EnemyHurt</c>.</summary>
    private long _hpEnemigos = -1;

    /// <summary>Último <see cref="Defender.LastHitTick"/> visto, por defensor.</summary>
    private readonly Dictionary<int, int> _ultimoDisparo = [];

    /// <summary>Impactos esperando a que el proyectil llegue: en qué tick y cuál.</summary>
    private readonly List<(int Tick, SoundId Id)> _impactos = [];

    private int _hpTorre = -1;
    private int _oleada = -1;
    private bool _preparando;
    private RunState _estado = RunState.InProgress;
    private bool _primera = true;

    /// <summary>
    /// Olvida todo. Al empezar o al cargar una corrida.
    /// </summary>
    /// <remarks>
    /// ⚠ Hace falta de verdad: cargar una partida guardada <b>re-simula desde el
    /// tick 0 a máxima velocidad</b>. Sin este reset, esa reconstrucción emitiría
    /// de golpe todos los sonidos de las 40 oleadas ya jugadas.
    /// </remarks>
    public void Reset()
    {
        _vivos.Clear();
        _ahora.Clear();
        _ultimoDisparo.Clear();
        _impactos.Clear();

        _hpEnemigos = -1;
        _hpTorre = -1;
        _oleada = -1;
        _preparando = false;
        _estado = RunState.InProgress;
        _primera = true;
    }

    /// <summary>
    /// Corre después de cada tick. Emite lo que haya que emitir.
    /// </summary>
    /// <param name="run">La corrida, ya avanzada.</param>
    /// <param name="silencioso">
    /// Verdadero mientras se reconstruye una partida guardada: actualiza las
    /// fotos pero no suena nada.
    /// </param>
    public void AfterTick(Run run, bool silencioso = false)
    {
        // La primera pasada solo toma la foto. Si emitiera, el arranque sonaría
        // como si acabaran de aparecer todos los enemigos de la oleada 1 a la vez.
        bool emitir = !silencioso && !_primera;

        Impactos(run, emitir);
        Disparos(run, emitir);
        Enemigos(run, emitir);
        Torre(run, emitir);
        Ritmo(run, emitir);

        if (emitir)
            Music.Play(PistaDe(run));

        _primera = false;
    }

    // ------------------------------------------------------------------ disparos

    private void Disparos(Run run, bool emitir)
    {
        foreach (var d in run.Defenders)
        {
            int visto = _ultimoDisparo.TryGetValue(d.PlacementId, out int v) ? v : int.MinValue;

            if (d.LastHitTick <= visto)
                continue;

            _ultimoDisparo[d.PlacementId] = d.LastHitTick;

            if (!emitir)
                continue;

            // Cuerpo a cuerpo suena distinto de un disparo, y la línea es la
            // distancia y no la clase — el mismo criterio que usa el dibujo del
            // tajo contra el del proyectil en `Game.DibujarDisparos`.
            bool pegado = d.LastHitAt is { } blanco &&
                          System.Math.Max(
                              System.Math.Abs(blanco.X - d.Cell.X),
                              System.Math.Abs(blanco.Y - d.Cell.Y)) <= 1;

            var ataque = pegado ? SoundId.AttackMelee : AtaqueDe(d.DefenderClass);

            Sfx.Play(ataque);

            // El impacto, para cuando el proyectil llegue. Cuerpo a cuerpo llega
            // en el acto: no hay vuelo que esperar.
            var impacto = ImpactoDe(ataque);

            if (pegado)
                Sfx.Play(impacto);
            else
                _impactos.Add((run.Tick + Game.TicksDeVuelo, impacto));
        }
    }

    private void Impactos(Run run, bool emitir)
    {
        for (int i = _impactos.Count - 1; i >= 0; i--)
        {
            if (run.Tick < _impactos[i].Tick)
                continue;

            if (emitir)
                Sfx.Play(_impactos[i].Id);

            _impactos.RemoveAt(i);
        }
    }

    // ----------------------------------------------------------------- enemigos

    private void Enemigos(Run run, bool emitir)
    {
        _ahora.Clear();

        bool jefeNuevo = false;
        long hp = 0;

        foreach (var e in run.Enemies)
        {
            _ahora.Add(e.SpawnId);
            hp += e.Hp;

            // ⚠ Se pregunta contra la foto ANTERIOR, no contra la que se está
            // llenando: contra la nueva, el jefe que se acaba de agregar siempre
            // figuraría como ya conocido y el rugido no sonaría nunca.
            if (e.IsBoss && !_vivos.Contains(e.SpawnId))
                jefeNuevo = true;
        }

        // Los que estaban y ya no están murieron: un enemigo que llega a la
        // Torre no desaparece, se planta y la ataca (`Enemy.AttackingTower`).
        int muertos = 0;

        foreach (int id in _vivos)
        {
            if (!_ahora.Contains(id))
                muertos++;
        }

        // Las dos fotos se intercambian en vez de copiarse: son hasta cien
        // enteros por tick, treinta veces por segundo, durante horas.
        (_vivos, _ahora) = (_ahora, _vivos);

        // ⚠ El quejido va por la vida TOTAL, no por enemigo. Un sonido por golpe
        // recibido es un sonido por defensor por tick: con nueve defensores y
        // sesenta bichos eso son cientos de voces por segundo. Así suena una vez
        // por tanda de daño, y el throttle de 180 ms del índice hace el resto.
        //
        // La resta se hace contra la foto anterior y solo cuenta si nadie murió:
        // una muerte ya tiene su propio sonido, y encimarle el quejido convierte
        // cada kill en dos golpes.
        bool bajoVida = _hpEnemigos >= 0 && hp < _hpEnemigos && muertos == 0;

        _hpEnemigos = hp;

        if (!emitir)
            return;

        // ⚠ UN sonido de muerte, no uno por muerto. Un splash del Mago puede
        // matar ocho bichos en el mismo tick; ocho voces idénticas encimadas
        // suenan a error, no a masacre. El throttle del índice hace el resto.
        if (muertos > 0)
            Sfx.Play(SoundId.EnemyDeath);
        else if (bajoVida)
            Sfx.Play(SoundId.EnemyHurt);

        if (jefeNuevo)
            Sfx.Play(SoundId.EnemyBoss);
    }

    // -------------------------------------------------------------------- Torre

    private void Torre(Run run, bool emitir)
    {
        int hp = run.Tower.Hp;

        if (_hpTorre >= 0 && hp < _hpTorre && emitir)
            Sfx.Play(SoundId.EnemyLeak);

        _hpTorre = hp;
    }

    // ------------------------------------------------------------------- ritmo

    private void Ritmo(Run run, bool emitir)
    {
        if (run.Wave != _oleada)
        {
            if (emitir && _oleada >= 0)
                Sfx.Play(SoundId.WaveStart);

            _oleada = run.Wave;
        }

        // El aviso que pedía el hilo de trabajo: sin él, que la fase de
        // preparación se abrió solo se descubre mirando el HUD.
        bool preparando = run.WaitingForWaveStart;

        if (preparando && !_preparando && emitir)
            Sfx.Play(SoundId.WaveEnd);

        _preparando = preparando;

        if (run.State != _estado)
        {
            _estado = run.State;

            if (emitir && _estado is RunState.Won)
                Sfx.Play(SoundId.UiReward);
        }
    }

    // ------------------------------------------------------------------ música

    /// <summary>
    /// Qué pista corresponde ahora mismo.
    /// </summary>
    /// <remarks>
    /// <para>Se calcula entera en cada tick y se le pide a <see cref="Music"/>,
    /// que ignora el pedido si ya está sonando. Así no hay estado de «qué pista
    /// pusimos» duplicado en dos lados, que es de donde salen las músicas que se
    /// quedan pegadas después de un jefe.</para>
    ///
    /// <para>El orden de las guardas es el orden de prioridad: primero cómo
    /// terminó, después si hay jefe, después si el reloj está parado.</para>
    /// </remarks>
    private static MusicTrack PistaDe(Run run)
    {
        if (run.State is RunState.Won)
            return MusicTrack.Victory;

        if (run.State is RunState.Lost)
            return MusicTrack.Defeat;

        foreach (var e in run.Enemies)
        {
            if (e.IsBoss)
                return MusicTrack.Boss;
        }

        // Preparación y elección: el reloj está parado y el jugador está
        // decidiendo. Es el único momento tranquilo de la corrida.
        if (run.Paused)
            return MusicTrack.Calm;

        // «Intensa» es una medida de presión en pantalla, no de número de
        // oleada: doce bichos vivos aprietan igual en la 20 que en la 80, y una
        // oleada floja de la 90 no tiene por qué sonar épica.
        int vivos = run.Enemies.Count;

        return vivos >= 12 ? MusicTrack.Intense : MusicTrack.Normal;
    }

    // ------------------------------------------------------------------- mapas

    /// <summary>
    /// Qué sonido de ataque le toca a cada clase.
    /// </summary>
    /// <remarks>
    /// <para>Nueve clases, nueve sonidos, pero el mapa no es uno a uno: es por
    /// <b>sabor</b>, no por elemento declarado —la simulación no tiene tipos de
    /// daño, y agregarlos para esto sería la cola moviendo al perro—.</para>
    ///
    /// <para><see cref="SoundId.AttackIce"/> no lo usa ninguna clase todavía: está
    /// en el banco esperando a que exista un arma o un perk de comportamiento de
    /// hielo. Es una línea de este <c>switch</c>, no un archivo que haya que ir a
    /// buscar.</para>
    /// </remarks>
    private static SoundId AtaqueDe(DefenderClass clase) => clase switch
    {
        DefenderClass.Hunter => SoundId.AttackBow,
        DefenderClass.Mage => SoundId.AttackFire,
        DefenderClass.Cleric => SoundId.AttackMagic,
        DefenderClass.Bard => SoundId.AttackLightning,
        DefenderClass.Druid => SoundId.AttackPoison,
        DefenderClass.Paladin => SoundId.AttackCannon,
        DefenderClass.Assassin or DefenderClass.Thief => SoundId.AttackSword,
        _ => SoundId.AttackMelee,
    };

    /// <summary>Qué impacto le corresponde a cada ataque.</summary>
    private static SoundId ImpactoDe(SoundId ataque) => ataque switch
    {
        SoundId.AttackBow => SoundId.ImpactArrow,
        SoundId.AttackFire => SoundId.ImpactFire,
        SoundId.AttackIce => SoundId.ImpactIce,
        SoundId.AttackPoison => SoundId.ImpactSoft,
        SoundId.AttackMagic or SoundId.AttackLightning => SoundId.ImpactMagic,
        _ => SoundId.ImpactSword,
    };
}
