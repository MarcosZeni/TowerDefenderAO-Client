using System.Collections.Generic;
using Godot;
using TowerOfArgentum.Simulation;
using TowerOfArgentum.Ui;

namespace TowerOfArgentum;

/// <summary>
/// La mochila y el panel de clase, en un solo overlay.
/// </summary>
/// <remarks>
/// <para>Van juntos porque la acción que importa —equipar— necesita las dos
/// mitades a la vista: qué tengo guardado y dónde lo puedo poner. Separarlas
/// obligaría a abrir y cerrar dos pantallas para una sola decisión.</para>
///
/// <para>Es la pantalla que el diseño describe entera, y usa sus cinco zonas tal
/// cual (<see cref="UiLayout"/>):</para>
///
/// <list type="table">
/// <item><term>Columna izquierda</term><description>las clases: dónde se
/// equipa.</description></item>
/// <item><term>Columna central</term><description>las pestañas, los filtros y la
/// grilla de 5×4 con lo guardado.</description></item>
/// <item><term>Columna de detalle</term><description>lo elegido: icono grande,
/// descripción, stats y el botón de equipar.</description></item>
/// <item><term>Columna derecha</term><description>la clase mirada: sus seis
/// slots, sus stats, el nivel y qué comportamiento manda.</description></item>
/// <item><term>Barra de abajo</term><description>la build armada, igual que en
/// el HUD.</description></item>
/// </list>
///
/// <para>Las tres acciones que <b>destruyen</b> avisan antes: sacar un perk
/// equipado —que además pide doble click—, fusionar y mejorar. La primera no
/// vuelve a la mochila; las otras dos consumen lo que entra.</para>
///
/// <para>⚠ <b>Un click en un perk equipado MUESTRA lo que da; el doble click lo
/// saca.</b> Antes un solo click abría la confirmación de destruir, y eso dejaba
/// la pregunta más común de la pantalla sin ningún gesto mientras ponía la acción
/// irreversible a un click del cursor.</para>
/// </remarks>
public partial class BackpackScreen : Control
{
    private readonly Game _juego;

    private bool _abierta;
    private int _cosaElegida = -1;
    private DefenderClass? _claseElegida;

    /// <summary>
    /// Los lugares de mochila marcados para mejorar.
    /// </summary>
    /// <remarks>
    /// <para>Guarda <b>índices de mochila</b>, no posiciones de pantalla: la
    /// grilla se reordena por rareza y una marca atada a la posición cambiaría de
    /// perk al cambiar el orden.</para>
    ///
    /// <para>Se limpia después de <b>toda</b> acción que toque la mochila. Sacar
    /// algo corre los índices de abajo, así que una marca vieja apuntaría a otro
    /// perk; la simulación revalida el tier de cada uno y rebotaría, pero
    /// rebotaría <b>en silencio</b>, que es peor.</para>
    /// </remarks>
    private readonly HashSet<int> _marcados = [];

    /// <summary>
    /// Qué slot de la clase mirada se está inspeccionando. -1 = ninguno.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Un click MUESTRA, el doble click SACA.</b> Antes un solo click abría
    /// la confirmación de destruir, y eso dejaba sin ningún gesto la pregunta más
    /// común de la pantalla —«¿qué me está dando este perk?»— mientras ponía la
    /// acción irreversible a un click de distancia.
    /// </remarks>
    private int _slotMirado = -1;

    /// <summary>El lugar de la mochila que se está por TIRAR, o -1.</summary>
    /// <remarks>
    /// ⚠ Es otra confirmación y no la misma que <see cref="_confirmarSacar"/>:
    /// aquella saca un perk EQUIPADO y esta destruye uno GUARDADO. Las dos
    /// destruyen, así que las dos preguntan, pero el texto tiene que decir cuál
    /// es — «sacar» y «tirar» se confunden justo cuando no hay vuelta atrás.
    /// </remarks>
    private int _confirmarTirar = -1;

    /// <summary>Lo que hay que confirmar antes de sacarlo, porque destruye.</summary>
    private (DefenderClass DefenderClass, int Slot)? _confirmarSacar;

    public BackpackScreen(Game juego)
    {
        _juego = juego;
    }

    public bool IsOpen => _abierta;

    public void Toggle()
    {
        _abierta = !_abierta;
        Forget();
        _confirmarSacar = null;
    }

    /// <summary>
    /// Borra toda la selección. Se llama después de cada acción sobre la mochila.
    /// </summary>
    /// <remarks>
    /// Equipar, mejorar o sacar corren los índices, y una marca vieja apuntaría a
    /// un perk que el jugador no eligió.
    /// </remarks>
    private void Forget()
    {
        _cosaElegida = -1;
        _slotMirado = -1;
        _marcados.Clear();
        _confirmarTirar = -1;
    }

    public override void _Ready()
    {
        UiStyle.Init();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        Visible = _abierta;
        MouseFilter = _abierta ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        if (_abierta)
            QueueRedraw();
    }

    // =====================================================================
    // Pestañas, filtro y orden
    // =====================================================================

    /// <summary>Qué mira la grilla del medio.</summary>
    private enum Pestana
    {
        Perks,
        Armas,
        Blueprints,
    }

    private Pestana _pestana = Pestana.Perks;

    /// <summary>Si la grilla muestra solo lo que le entra a la clase mirada.</summary>
    private bool _soloLoQueEntra;

    /// <summary>Si la grilla ordena por rareza en vez de por orden de llegada.</summary>
    private bool _ordenPorRareza = true;

    /// <summary>
    /// Los índices de la mochila que se ven ahora, ya filtrados y ordenados.
    /// </summary>
    /// <remarks>
    /// <para>Devuelve <b>índices de la mochila</b>, no los objetos: equipar pide
    /// el índice real y el orden de pantalla no es el de adentro. Si esto
    /// devolviera los objetos, cada click habría que resolverlo buscando de
    /// vuelta, y ordenar rompería el equipar.</para>
    ///
    /// <para>La pestaña de planos no sale de acá: los planos no están en la
    /// mochila, son de la cuenta.</para>
    /// </remarks>
    private List<int> Visibles(Run p)
    {
        var salida = new List<int>();
        var items = p.Backpack.Items;

        for (int i = 0; i < items.Count; i++)
        {
            var cosa = items[i];

            bool esDeLaPestana = _pestana switch
            {
                Pestana.Perks => cosa.Kind == ItemType.Perk,
                Pestana.Armas => cosa.Kind == ItemType.Weapon,
                _ => false,
            };

            if (!esDeLaPestana)
                continue;

            if (_soloLoQueEntra && _claseElegida is { } clase)
            {
                var ficha = p.Roster.Find(clase);

                if (ficha is null || !Acepta(p, ficha, i))
                    continue;
            }

            salida.Add(i);
        }

        if (!_ordenPorRareza)
            return salida;

        // Lo más raro primero. Es el orden que pide el diseño y el útil: lo que
        // decide una corrida es el Oro, no el tercer perk de bronce.
        salida.Sort((a, b) => Rango(p, b).CompareTo(Rango(p, a)));

        return salida;
    }

    /// <summary>Cuán raro es lo que está en ese lugar. Perks y armas en una escala.</summary>
    private static int Rango(Run p, int indice)
    {
        var cosa = p.Backpack.Items[indice];

        if (cosa.Kind == ItemType.Perk)
            return (int)DataLoader.Current.Perks.Get(cosa.Id).Tier;

        return (int)(p.Backpack.WeaponById(cosa.Id)?.Rarity ?? WeaponRarity.Common);
    }

    // =====================================================================
    // Entrada
    // =====================================================================

    /// <summary>
    /// ⚠ <c>_Input</c> y no <c>_GuiInput</c>, por la misma cicatriz que
    /// <see cref="MarketScreen"/>: con <c>_GuiInput</c> la pantalla quedaba
    /// sorda a clicks y a teclas. La posición sale de
    /// <see cref="CanvasItem.GetGlobalMousePosition"/>, no del evento, para que
    /// funcione con la ventana a cualquier tamaño.
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        if (!_abierta)
            return;

        if (evento is InputEventKey tecla && tecla.Pressed && tecla.Keycode == Key.Escape)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (evento is not InputEventMouseButton boton || !boton.Pressed ||
            boton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        // Abierta, el click es de esta pantalla caiga donde caiga: sin esto un
        // click al costado pondría un defensor atrás del panel.
        GetViewport().SetInputAsHandled();

        var posicion = GetGlobalMousePosition();
        var partida = _juego.Run;

        // Confirmar la destrucción, si hay una pendiente. Va primero: mientras
        // hay un modal arriba, nada de abajo atiende.
        if (_confirmarSacar is { } pendiente)
        {
            if (RectConfirmar().HasPoint(posicion))
            {
                _juego.RemovePerk(pendiente.DefenderClass, pendiente.Slot);
                _confirmarSacar = null;

                // Sacar corre los slots de arriba: el que se estaba mirando ya
                // no es el mismo perk.
                Forget();
            }
            else if (RectCancelar().HasPoint(posicion))
            {
                _confirmarSacar = null;
            }

            return;
        }

        // Tirar un perk GUARDADO. Mismo modal y misma regla: destruye, así que
        // pregunta. Ver _confirmarTirar.
        if (_confirmarTirar >= 0)
        {
            if (RectConfirmar().HasPoint(posicion))
            {
                _juego.Discard(_confirmarTirar);
                _confirmarTirar = -1;

                // Tirar corre los índices de arriba, igual que sacar.
                Forget();
            }
            else if (RectCancelar().HasPoint(posicion))
            {
                _confirmarTirar = -1;
            }

            return;
        }

        if (Pestanas(posicion) || Filtros(posicion))
            return;

        if (Grid(partida, posicion))
            return;

        if (Clases(partida, posicion))
            return;

        // ⚠ El doble click de Godot llega como un evento aparte con
        // `DoubleClick` prendido, DESPUÉS del click normal. O sea que el primero
        // ya seleccionó y este es el segundo: es exactamente el gesto que hace
        // falta para que mirar sea barato y destruir cueste dos.
        ColumnaDeClase(partida, posicion, boton.DoubleClick);
    }

    /// <summary>Las tres pestañas de la columna del medio.</summary>
    private bool Pestanas(Vector2 posicion)
    {
        if (UiLayout.TabPerks.HasPoint(posicion))
        {
            _pestana = Pestana.Perks;
            _cosaElegida = -1;
            return true;
        }

        if (UiLayout.TabWeapons.HasPoint(posicion))
        {
            _pestana = Pestana.Armas;
            _cosaElegida = -1;
            return true;
        }

        if (UiLayout.TabBlueprints.HasPoint(posicion))
        {
            _pestana = Pestana.Blueprints;
            _cosaElegida = -1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Los dos desplegables. Alternan, no abren una lista.
    /// </summary>
    /// <remarks>
    /// Cada uno tiene dos valores, así que un menú desplegable de verdad sería
    /// un click de más para llegar al mismo lado. Se dibujan como el desplegable
    /// del diseño porque es lo que el jugador reconoce.
    /// </remarks>
    private bool Filtros(Vector2 posicion)
    {
        if (UiLayout.FilterDropdown.HasPoint(posicion))
        {
            _soloLoQueEntra = !_soloLoQueEntra;
            _cosaElegida = -1;
            return true;
        }

        if (UiLayout.SortDropdown.HasPoint(posicion))
        {
            _ordenPorRareza = !_ordenPorRareza;
            _cosaElegida = -1;
            return true;
        }

        return false;
    }

    /// <summary>Un hueco de la grilla, el botón de equipar o el de mejorar.</summary>
    private bool Grid(Run partida, Vector2 posicion)
    {
        if (_pestana == Pestana.Blueprints)
        {
            // El botón FUSIONAR de un renglón de receta. Es el único gesto que el
            // juego tiene para fabricar un Único: antes esta pestaña no disparaba
            // ninguna acción. Ver GrillaDePlanos.
            int plano = PlanoEn(partida, posicion);

            if (plano >= 0 && partida.FuseBlockedKey(plano) is null &&
                partida.UniqueClassOf(plano) is { } paraQuien)
            {
                _juego.Fuse(plano, paraQuien);
                Forget();

                return true;
            }

            return false;
        }

        {
            var visibles = Visibles(partida);

            for (int i = 0; i < visibles.Count && i < UiLayout.GridVisible; i++)
            {
                if (!UiLayout.GridCell(i).HasPoint(posicion))
                    continue;

                Marcar(partida, visibles[i]);
                return true;
            }
        }

        // Vender un arma o tirar un perk. Es el único gesto que saca algo de la
        // mochila: sin él el bolso se tapa y todo drop posterior cae al vacío.
        if (UiLayout.DetailPrice.HasPoint(posicion) &&
            _cosaElegida >= 0 && _cosaElegida < partida.Backpack.Items.Count)
        {
            // Vender paga y no destruye, así que va derecho. Tirar pregunta.
            if (partida.Backpack.Items[_cosaElegida].Kind == ItemType.Weapon)
            {
                _juego.SellGear(_cosaElegida);
                Forget();
            }
            else
            {
                _confirmarTirar = _cosaElegida;
            }

            return true;
        }

        // El botón de equipar del detalle: equipa en la clase mirada. Es el
        // camino de un click; el de arrastrar a una tarjeta sigue existiendo.
        if (UiLayout.DetailBuy.HasPoint(posicion) && _cosaElegida >= 0 && _claseElegida is { } clase)
        {
            var ficha = partida.Roster.Find(clase);

            if (ficha is not null && Acepta(partida, ficha, _cosaElegida))
            {
                _juego.EquipFromBackpack(_cosaElegida, clase);
                Forget();
            }

            return true;
        }

        if (UiLayout.DetailEquip.HasPoint(posicion) && PuedeMejorar(partida))
        {
            _juego.Upgrade(MascaraMarcada(), TierMarcado(partida)!.Value);
            Forget();

            return true;
        }

        return false;
    }

    /// <summary>
    /// Marca o desmarca un lugar de la mochila, y lo pone en el detalle.
    /// </summary>
    /// <remarks>
    /// <para>Con una sola marca se comporta igual que antes —click elige, click
    /// de vuelta suelta— y esa es la condición para que equipar siga siendo un
    /// click. La segunda marca es lo que abre la mejora.</para>
    ///
    /// <para>Marcar algo de otro tier <b>arranca de cero</b>: una selección
    /// mezclada no es mejorable, así que acumularla solo obligaría a
    /// desmarcar de a uno.</para>
    /// </remarks>
    private void Marcar(Run partida, int indice)
    {
        if (_marcados.Remove(indice))
        {
            if (_cosaElegida == indice)
                _cosaElegida = -1;

            return;
        }

        if (_marcados.Count > 0 && TierDe(partida, indice) != TierMarcado(partida))
            _marcados.Clear();

        _marcados.Add(indice);
        _cosaElegida = indice;
        _slotMirado = -1;
    }

    /// <summary>El tier de lo que hay en ese lugar, o <c>null</c> si es un arma.</summary>
    private static PerkTier? TierDe(Run partida, int indice)
    {
        if (indice < 0 || indice >= partida.Backpack.Items.Count)
            return null;

        var cosa = partida.Backpack.Items[indice];

        return cosa.Kind == ItemType.Perk
            ? DataLoader.Current.Perks.Get(cosa.Id).Tier
            : null;
    }

    /// <summary>El tier común de lo marcado, o <c>null</c> si está mezclado.</summary>
    private PerkTier? TierMarcado(Run partida)
    {
        PerkTier? comun = null;

        foreach (int i in _marcados)
        {
            var t = TierDe(partida, i);

            if (t is null || (comun is not null && t != comun))
                return null;

            comun = t;
        }

        return comun;
    }

    /// <summary>La máscara de bits de lo marcado: un bit por lugar de mochila.</summary>
    private int MascaraMarcada()
    {
        int mascara = 0;

        foreach (int i in _marcados)
            mascara |= 1 << i;

        return mascara;
    }

    /// <summary>
    /// Por qué no se puede mejorar lo marcado, o <c>null</c> si se puede.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Ya no repite los chequeos de la simulación.</b> Los repetía —tres de
    /// los ocho— y por los otros cinco el botón quedaba encendido: el jugador lo
    /// apretaba, la selección se le borraba entera y no pasaba nada. Ahora
    /// pregunta, y muestra la respuesta.
    /// </remarks>
    private string? MotivoParaNoMejorar(Run partida)
    {
        if (TierMarcado(partida) is not { } tier)
            return "ui.mochila.mejora.mezclado";

        return partida.UpgradeBlockedKey(MascaraMarcada(), tier);
    }

    private bool PuedeMejorar(Run partida) => MotivoParaNoMejorar(partida) is null;

    /// <summary>Una tarjeta de clase: la mira, o equipa lo elegido ahí.</summary>
    private bool Clases(Run partida, Vector2 posicion)
    {
        var clases = partida.Roster.Classes;
        int visibles = Mathf.Min(clases.Count, UiLayout.HeroCardsVisible);

        for (int i = 0; i < visibles; i++)
        {
            if (!UiLayout.HeroCard(i).HasPoint(posicion))
                continue;

            if (_cosaElegida >= 0)
            {
                _juego.EquipFromBackpack(_cosaElegida, clases[i].DefenderClass);
                Forget();
            }
            else
            {
                _claseElegida = clases[i].DefenderClass;
                _slotMirado = -1;
            }

            return true;
        }

        return false;
    }

    /// <summary>El nivel, el comportamiento y los slots de la clase mirada.</summary>
    private void ColumnaDeClase(Run partida, Vector2 posicion, bool doble)
    {
        if (_claseElegida is not { } clase)
            return;

        // Comprar un nivel. Es el sumidero de oro de la corrida: con el tablero
        // lleno y la mochila llena, es lo único que queda para gastar.
        if (RectSubirNivel().HasPoint(posicion))
        {
            _juego.BuyLevel(clase);
            return;
        }

        // ⚠ Elegir qué comportamiento manda. Solo si hay conflicto: sin arma con
        // habilidad Y perk de Oro/Único puestos no hay nada que decidir.
        //
        // Existe porque antes ganaba el arma siempre y eso hacía que encontrar
        // una excelente se sintiera como un CASTIGO: obligaba a apagar un Único
        // que costó dos jefes y un plano, así que el jugador terminaba no
        // equipándola. Los stats del arma se conservan en los dos casos.
        if (partida.HasBehaviorConflict(clase))
        {
            if (RectComportamiento(elDelPerk: true).HasPoint(posicion))
            {
                _juego.SetBehavior(clase, preferirElPerk: true);
                return;
            }

            if (RectComportamiento(elDelPerk: false).HasPoint(posicion))
            {
                _juego.SetBehavior(clase, preferirElPerk: false);
                return;
            }
        }

        var ficha = partida.Roster.Find(clase);

        if (ficha is null)
            return;

        // ⚠ Un click MUESTRA lo que da el perk, el doble click lo SACA.
        //
        // Antes un solo click abría la confirmación de destruir. Eso dejaba la
        // pregunta más común de la pantalla —«¿qué me está dando este perk?»— sin
        // ningún gesto, mientras ponía la acción irreversible a un click de
        // distancia del cursor. La confirmación sigue estando: el doble click es
        // deliberado, pero destruir un Oro que costó dos jefes merece las dos.
        for (int s = 0; s < ficha.Perks.Count; s++)
        {
            if (!RectSlot(s).HasPoint(posicion))
                continue;

            if (doble)
            {
                _confirmarSacar = (clase, s);
                return;
            }

            _slotMirado = _slotMirado == s ? -1 : s;
            _cosaElegida = -1;
            _marcados.Clear();

            return;
        }
    }

    /// <summary>
    /// Por qué NO se puede equipar lo elegido en esa clase, o <c>null</c> si sí.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Devuelve el motivo y no un booleano.</b> El botón apagado con
    /// un «no entra» genérico es lo mismo que nada: los cuatro motivos se
    /// arreglan de maneras distintas —esperar un nivel, sacar el que está,
    /// equiparlo en otra clase— y el jugador no puede elegir cuál sin saber
    /// cuál es.</para>
    ///
    /// <para>⚠ Y el de la RANURA DE COMPORTAMIENTO no estaba chequeado acá.
    /// <c>Run.EquipPerk</c> lo rebota —una clase no puede tener dos Oro, ni un
    /// Oro y un Único— pero la mochila dejaba el botón EQUIPAR encendido: se
    /// apretaba, no pasaba nada y el juego no decía una palabra. Era el peor
    /// bug de la pantalla, porque el jugador no tenía forma de saber si el
    /// problema era el perk, la clase o un click que no había entrado.</para>
    /// </remarks>
    private static string? MotivoParaNoEquipar(Run partida, RosterClass ficha, int indice)
    {
        if (indice < 0 || indice >= partida.Backpack.Items.Count)
            return Loc.Get("ui.mochila.no_entra");

        var cosa = partida.Backpack.Items[indice];

        // El arma va a su propio slot y siempre entra: lo que estaba puesto
        // vuelve a la mochila.
        if (cosa.Kind == ItemType.Weapon)
            return null;

        var def = DataLoader.Current.Perks.Get(cosa.Id);

        if (!def.WorksFor(ficha.DefenderClass))
            return Loc.Get("ui.mochila.no_es_de_esta_clase");

        // La ranura de comportamiento se chequea ANTES que los slots libres: con
        // seis slots vacíos y un Oro puesto, «no hay lugar» sería mentira.
        if (def.Tier is PerkTier.Gold or PerkTier.Unique &&
            partida.BehaviorPerkOf(ficha.DefenderClass) is { } puesto)
        {
            return $"{Loc.Get("ui.mochila.ya_tiene_comportamiento")} {Loc.Perk(puesto)}";
        }

        if (!ficha.HasFreeSlot)
            return Loc.Get("ui.mochila.sin_slots");

        int yaTiene = 0;

        foreach (int p in ficha.Perks)
        {
            if (p == cosa.Id)
                yaTiene++;
        }

        return yaTiene < def.MaxStacked
            ? null
            : Loc.Get("ui.mochila.tope_de_apilado");
    }

    private static bool Acepta(Run partida, RosterClass ficha, int indice)
        => MotivoParaNoEquipar(partida, ficha, indice) is null;

    // =====================================================================
    // Geometría propia de la columna derecha
    // =====================================================================

    /// <summary>
    /// Los seis slots de perk de la clase, en la columna de la derecha.
    /// </summary>
    /// <remarks>
    /// ⚠ No usa las cuatro tarjetas de <c>shop_card_*</c> del diseño: son cuatro
    /// y los slots son seis. La grilla se deriva del panel —tres columnas, dos
    /// filas— conservando los márgenes del diseño, que es lo que hace que se vea
    /// de la misma familia.
    /// </remarks>
    private static Rect2 RectSlot(int i)
    {
        var panel = UiLayout.ShopPanel;
        float margen = 20f;
        float hueco = 10f;
        float ancho = (panel.Size.X - (margen * 2f) - (hueco * 2f)) / 3f;
        float alto = ancho * 1.15f;

        return new Rect2(
            panel.Position.X + margen + ((i % 3) * (ancho + hueco)),
            UiLayout.ShopCards[0].Position.Y + ((i / 3) * (alto + hueco)),
            ancho, alto);
    }

    /// <summary>Dónde arrancan los stats de la clase, debajo de los seis slots.</summary>
    private static float YStats() => RectSlot(3).End.Y + 16f;

    /// <summary>El botón de comprar el próximo nivel de la clase mirada.</summary>
    private static Rect2 RectSubirNivel()
        => new(UiLayout.ShopPanel.Position.X + 20f, YStats() + 92f,
               UiLayout.ShopPanel.Size.X - 40f, 44f);

    /// <summary>
    /// Los dos botones de «qué comportamiento manda»: el del perk o el del arma.
    /// </summary>
    /// <remarks>
    /// ⚠ Aparecen <b>solo</b> cuando la clase tiene las dos cosas puestas. Sin
    /// conflicto no hay nada que elegir, y un par de botones permanentes que casi
    /// siempre no hacen nada es peor que no tenerlos.
    /// </remarks>
    private static Rect2 RectComportamiento(bool elDelPerk)
    {
        var panel = UiLayout.ShopPanel;
        float ancho = (panel.Size.X - 50f) / 2f;

        return new Rect2(
            panel.Position.X + 20f + (elDelPerk ? 0f : ancho + 10f),
            RectSubirNivel().End.Y + 30f,
            ancho, 40f);
    }

    private static Rect2 RectConfirmar()
        => new((Canvas.CanvasWidth / 2f) - 220f, (Canvas.CanvasHeight / 2f) + 30f, 200f, 56f);

    private static Rect2 RectCancelar()
        => new((Canvas.CanvasWidth / 2f) + 20f, (Canvas.CanvasHeight / 2f) + 30f, 200f, 56f);

    // =====================================================================
    // Dibujo
    // =====================================================================

    public override void _Draw()
    {
        var partida = _juego.Run;

        if (partida is null)
            return;

        UiTooltip.Begin(GetGlobalMousePosition());

        this.Scrim();

        ColumnaDeClases(partida);
        ColumnaDeInventario(partida);
        ColumnaDeDetalle(partida);
        ColumnaDeLaClaseMirada(partida);
        Pie();

        // Antes de la confirmación: con el modal abierto no hay nada que
        // explicar, y el cartel quedaría flotando arriba del cartel.
        UiTooltip.Draw(this);

        if (_confirmarSacar is not null || _confirmarTirar >= 0)
            DibujarConfirmacion();
    }

    /// <summary>La columna izquierda: dónde se equipa lo elegido.</summary>
    private void ColumnaDeClases(Run partida)
    {
        this.Panel(UiLayout.HeroesPanel, opacidad: 1f);
        this.SectionTitle(UiLayout.HeroesTitle, Loc.Get("ui.mochila.equipar_en"));

        var clases = partida.Roster.Classes;
        int visibles = Mathf.Min(clases.Count, UiLayout.HeroCardsVisible);

        for (int i = 0; i < visibles; i++)
        {
            var ficha = clases[i];
            var r = UiLayout.HeroCard(i);

            this.ClassCard(r, ficha,
                DataLoader.Current.StatsOf(ficha.DefenderClass).Cost,
                alcanza: true,
                elegida: _claseElegida == ficha.DefenderClass,
                puestas: 0);

            // Con algo elegido en la mochila, se marca en verde dónde entra.
            // Antes del click, que es cuando sirve.
            if (_cosaElegida >= 0 && Acepta(partida, ficha, _cosaElegida))
            {
                this.Glow(r, Palette.Success, 2f);
                DrawRect(r, Palette.Success, filled: false, width: 3f);
            }
        }

        if (visibles < UiLayout.HeroCardsVisible)
            this.LockedClassCard(UiLayout.HeroCard(visibles), Loc.Get("ui.hud.proximamente"));
    }

    /// <summary>La columna del medio: pestañas, filtros, grilla y capacidad.</summary>
    private void ColumnaDeInventario(Run partida)
    {
        this.Panel(UiLayout.InventoryPanel, opacidad: 1f);

        this.Tab(UiLayout.TabPerks, Loc.Get("ui.mochila.tab_perks"), _pestana == Pestana.Perks);
        this.Tab(UiLayout.TabWeapons, Loc.Get("ui.mochila.tab_armas"), _pestana == Pestana.Armas);
        this.Tab(UiLayout.TabBlueprints, Loc.Get("ui.mochila.tab_planos"), _pestana == Pestana.Blueprints);

        // ⚠ Las tres pestañas son TRES SISTEMAS distintos, no tres filtros de una
        // misma lista: perks y armas ocupan lugar en la mochila y los planos no
        // —son de la cuenta y no se guardan en ningún lado—. Sin decirlo, la
        // tercera pestaña se lee como una categoría más que llena la mochila.
        UiTooltip.Over(UiLayout.TabPerks, Loc.Get("ui.mochila.tab_perks"), "ui.ayuda.tab_perks");
        UiTooltip.Over(UiLayout.TabWeapons, Loc.Get("ui.mochila.tab_armas"), "ui.ayuda.tab_armas");
        UiTooltip.Over(UiLayout.TabBlueprints, Loc.Get("ui.mochila.tab_planos"), "ui.ayuda.tab_planos");

        this.Dropdown(UiLayout.FilterDropdown, "",
            _soloLoQueEntra ? Loc.Get("ui.mochila.le_entran") : Loc.Get("ui.mochila.todos"));

        this.Dropdown(UiLayout.SortDropdown, Loc.Get("ui.mochila.ordenar"),
            _ordenPorRareza ? Loc.Get("ui.mochila.por_rareza") : Loc.Get("ui.mochila.por_llegada"));

        UiTooltip.Over(UiLayout.FilterDropdown,
            Loc.Get(_soloLoQueEntra ? "ui.mochila.le_entran" : "ui.mochila.todos"),
            "ui.ayuda.filtro");

        UiTooltip.Over(UiLayout.SortDropdown,
            Loc.Get(_ordenPorRareza ? "ui.mochila.por_rareza" : "ui.mochila.por_llegada"),
            "ui.ayuda.orden");

        if (_pestana == Pestana.Blueprints)
        {
            GrillaDePlanos(partida);
            return;
        }

        GrillaDeMochila(partida);
    }

    private void GrillaDeMochila(Run partida)
    {
        var visibles = Visibles(partida);
        int cap = DataLoader.Current.Curves.Defenders.BackpackSlots;

        // ⚠ Se dibujan los QUINCE lugares, no los ocupados. El hueco libre es
        // información: dice cuánto margen queda antes de que el próximo drop
        // obligue a tirar algo.
        for (int i = 0; i < UiLayout.GridVisible; i++)
        {
            var celda = UiLayout.GridCell(i);

            if (i >= visibles.Count)
            {
                if (i < cap)
                    this.EmptySlot(celda);

                continue;
            }

            int indice = visibles[i];
            var cosa = partida.Backpack.Items[indice];
            bool marcado = _marcados.Contains(indice);

            if (cosa.Kind == ItemType.Perk)
                this.PerkSlot(celda, DataLoader.Current.Perks.Get(cosa.Id), 0, marcado);
            else
                DibujarArma(celda, partida.Backpack.WeaponById(cosa.Id), marcado);

            // ⚠ El tilde y el aro interno los pone `UiStyle.Slot` cuando el hueco
            // va elegido, y valen para las dos pantallas que marcan algo. Antes
            // el tilde se dibujaba acá a mano y la diferencia entre elegido y no
            // elegido era, en todo lo demás, un borde un poco más grueso: en una
            // grilla de veinte huecos de colores fuertes, invisible.
        }

        if (visibles.Count == 0)
        {
            this.Label(UiLayout.PerkGrid, Loc.Get("ui.mochila.vacia"), 17,
                Palette.TextDisabled, HorizontalAlignment.Center);
        }

        // La capacidad, con la barra: el 15/15 se tiene que ver ANTES de que el
        // próximo drop obligue a destruir algo.
        var caja = UiLayout.InventoryCapacity;
        bool llena = partida.Backpack.Used >= cap;

        this.Label(caja, Loc.Get("ui.mochila.capacidad").ToUpperInvariant(), 14, Palette.TextMuted);

        float x = caja.Position.X + UiStyle.Width(Loc.Get("ui.mochila.capacidad").ToUpperInvariant(), 14) + 14f;

        this.Label(new Rect2(x, caja.Position.Y, 90f, caja.Size.Y),
            $"{partida.Backpack.Used} / {cap}", 17,
            llena ? Palette.Warning : Palette.TextPrimary);

        this.Bar(new Rect2(x + 80f, caja.Position.Y + (caja.Size.Y / 2f) - 6f,
                caja.End.X - x - 80f, 12f),
            cap == 0 ? 0f : partida.Backpack.Used / (float)cap,
            llena ? Palette.Warning : Palette.Info);

        UiTooltip.Over(caja, $"{partida.Backpack.Used} / {cap}",
            llena ? "ui.ayuda.mochila_llena" : "ui.ayuda.mochila_lugar");
    }

    /// <summary>
    /// La pestaña de planos: las recetas de fusión que la cuenta conoce.
    /// </summary>
    /// <remarks>
    /// <para>Cada una se dibuja como <c>Gold + Gold = Único</c>, que es lo que hay
    /// que mirar cuando un jefe ofrece dos Oro para elegir uno.</para>
    ///
    /// <para>⚠ <b>Y ACÁ SE FUSIONA.</b> Hasta el 2026-09-02 esta pestaña era una
    /// lámina: <c>ActionType.Fuse</c> existía en la simulación, el bot headless la
    /// emitía, y <b>ninguna pantalla del juego la disparaba nunca</b>. O sea que un
    /// jugador con los dos Oro en la mano no tenía un solo gesto para fabricar el
    /// Único — la única progresión permanente del proyecto, con su plano ya pagado
    /// con la billetera, era inalcanzable jugando.</para>
    ///
    /// <para>No se pregunta a quién equipárselo: cada Único es de una sola clase,
    /// así que el destino se deriva de la receta.</para>
    /// </remarks>
    private void GrillaDePlanos(Run partida)
    {
        var todos = DataLoader.Current.Blueprints.All;
        var grilla = UiLayout.PerkGrid;

        float alto = 62f;
        int fila = 0;

        for (int i = 0; i < todos.Count; i++)
        {
            if (!partida.KnowsBlueprint(i))
                continue;

            float y = grilla.Position.Y + (fila * (alto + 8f));

            if (y + alto > grilla.End.Y)
                break;

            var r = new Rect2(grilla.Position.X, y, grilla.Size.X, alto);

            this.Panel(r, elevado: true, opacidad: 1f);

            var plano = todos[i];
            float lado = alto - 14f;
            float x = r.Position.X + 8f;

            Ingrediente(x, y + 7f, lado, plano.GoldA);
            Signo(x + lado + 7f, y + (alto / 2f), "+");
            Ingrediente(x + lado + 24f, y + 7f, lado, plano.GoldB);
            Signo(x + (lado * 2f) + 31f, y + (alto / 2f), "=");

            var salida = DataLoader.Current.Perks.Get(plano.ResultingUnique);
            var destino = new Rect2(x + (lado * 2f) + 48f, y + 7f, lado, lado);

            this.PerkSlot(destino, salida);

            // El nombre a la izquierda y el botón pegado al borde derecho: el
            // renglón se lee «esto + esto = esto, y lo hago acá».
            var boton = new Rect2(r.End.X - 132f, y + 12f, 120f, alto - 24f);

            this.Label(new Rect2(destino.End.X + 12f, y, boton.Position.X - destino.End.X - 20f, alto),
                Loc.Perk(salida), 17, Palette.Unique);

            BotonFusionar(partida, i, boton);

            fila++;
        }

        if (fila == 0)
        {
            this.Label(grilla, Loc.Get("ui.hud.sin_planos"), 17,
                Palette.TextDisabled, HorizontalAlignment.Center);
        }

        this.Label(UiLayout.InventoryCapacity, Loc.Get("ui.mochila.planos_conocidos"), 15,
            Palette.TextMuted);

        // ⚠ Los planos son la única progresión permanente del juego, y esta
        // pestaña es donde se ve. La lista es finita a propósito: la ventaja del
        // veterano tiene techo.
        UiTooltip.Help(UiLayout.InventoryCapacity, "ui.ayuda.planos");

        void Ingrediente(float px, float py, float lado, int idPerk)
            => this.PerkSlot(new Rect2(px, py, lado, lado), DataLoader.Current.Perks.Get(idPerk));

        void Signo(float px, float py, string s)
            => this.Label(new Rect2(px, py - 12f, 16f, 24f), s, 20, Palette.TextMuted,
                HorizontalAlignment.Center);
    }

    /// <summary>
    /// El botón de fusionar de una receta, y por qué está apagado si lo está.
    /// </summary>
    /// <remarks>
    /// El motivo sale de <see cref="Run.FuseBlockedKey"/>, el mismo que va a
    /// consultar la simulación para rebotar: el botón apagado y el rebote no se
    /// pueden separar. Va en el cartel y no en el renglón porque el renglón mide
    /// 120 px y el motivo es una frase.
    /// </remarks>
    private void BotonFusionar(Run partida, int indicePlano, Rect2 r)
    {
        string? motivo = partida.FuseBlockedKey(indicePlano);
        bool puede = motivo is null;

        this.Button(r, Loc.Get("ui.mochila.fusionar"),
            puede ? Palette.Unique : Palette.TextDisabled, habilitado: puede);

        UiTooltip.Over(r,
            puede ? Loc.Get("ui.mochila.fusion.listo") : Loc.Get(motivo!),
            "ui.ayuda.fusionar");
    }

    /// <summary>El renglón de plano que cae bajo el cursor, o <c>-1</c>.</summary>
    /// <remarks>
    /// ⚠ Recorre las recetas <b>conocidas</b> y en el mismo orden que las dibuja
    /// <see cref="GrillaDePlanos"/>: si los dos recorridos se separan, el click
    /// fusiona una receta distinta de la que el jugador está mirando.
    /// </remarks>
    private static int PlanoEn(Run partida, Vector2 posicion)
    {
        var grilla = UiLayout.PerkGrid;
        float alto = 62f;
        int fila = 0;

        for (int i = 0; i < DataLoader.Current.Blueprints.All.Count; i++)
        {
            if (!partida.KnowsBlueprint(i))
                continue;

            float y = grilla.Position.Y + (fila * (alto + 8f));

            if (y + alto > grilla.End.Y)
                break;

            if (new Rect2(grilla.End.X - 132f, y + 12f, 120f, alto - 24f).HasPoint(posicion))
                return i;

            fila++;
        }

        return -1;
    }

    /// <summary>Un arma en un hueco de la grilla: marco de rareza y la estrella.</summary>
    private void DibujarArma(Rect2 celda, Weapon? arma, bool elegido)
    {
        // ⚠ El arma con habilidad va en VIOLETA, no en el dorado del Oro: pelea
        // por la misma ranura de comportamiento, y el jugador elige cuál habla.
        // Con el color del Oro esa competencia no se vería.
        var color = arma is null ? Palette.Common
            : arma.Ability != WeaponAbility.None ? Palette.Epic
            : ColorDeRareza(arma.Rarity);

        this.Slot(celda, color, elegido);

        this.Label(celda.Row(4f, celda.Size.Y * 0.5f),
            arma is null ? "?" : Loc.Weapon(arma)[..1].ToUpperInvariant(),
            (int)(celda.Size.X * 0.42f), color, HorizontalAlignment.Center);

        this.Label(celda.Row(celda.Size.Y - 20f, 18f),
            arma is null ? "?" : Loc.Weapon(arma), 12, Palette.TextSecondary,
            HorizontalAlignment.Center);

        // Si tiene habilidad se dice, aunque sea corto. Un arma con habilidad y
        // números flojos puede valer más que una sin ella, y el jugador no puede
        // saberlo si solo ve los stats.
        if (arma is not null && arma.Ability != WeaponAbility.None)
        {
            // El rayo y no una estrella: IBM Plex Mono no trae el ★, y además el
            // rayo dice «esto hace algo», que es lo que la marca significa.
            this.Icon(new Rect2(celda.End.X - 20f, celda.Position.Y + 3f, 16f, 16f),
                UiStyle.Icono.Ability, color);
        }

        // ⚠ La grilla de armas era la única que no tenía cartel: los perks lo
        // traen desde `PerkSlot` y acá la celda muestra una inicial, un nombre
        // cortado y, con suerte, un rayo. Los números viven en la columna de
        // detalle, a un click de distancia — y comparar dos armas es exactamente
        // lo que se hace antes de dar ese click.
        if (arma is null)
            return;

        UiTooltip.Over(celda,
            $"{Loc.Weapon(arma)}\n{Loc.Get("ui.tienda.dano")} {Loc.Damage(arma.Damage)} · " +
            $"{Loc.Get("ui.tienda.alcance")} {Loc.Range(arma.Range)}",
            arma.Ability == WeaponAbility.None ? "ui.ayuda.arma" : "ui.ayuda.arma_con_habilidad");
    }

    /// <summary>
    /// La columna de detalle: lo elegido, en grande.
    /// </summary>
    /// <remarks>
    /// Existe para no tener un tooltip gigante encima de la grilla, que es lo que
    /// el diseño evita a propósito: con un tooltip, comparar dos perks obliga a
    /// recordar el primero mientras se mira el segundo.
    /// </remarks>
    private void ColumnaDeDetalle(Run partida)
    {
        this.Panel(UiLayout.DetailPanel, opacidad: 1f);

        // Un perk EQUIPADO que se está mirando: mismo detalle, sin botón de
        // equipar —ya está puesto— y con el recordatorio de cómo se saca.
        if (_slotMirado >= 0 && _claseElegida is { } mirada &&
            partida.Roster.Find(mirada) is { } fichaMirada &&
            _slotMirado < fichaMirada.Perks.Count)
        {
            DetalleDePerk(partida, DataLoader.Current.Perks.Get(fichaMirada.Perks[_slotMirado]));

            this.Label(UiLayout.DetailBuy, Loc.Get("ui.mochila.equipado").ToUpperInvariant(), 15,
                Palette.Success, HorizontalAlignment.Center);

            this.Paragraph(UiLayout.DetailEquip, Loc.Get("ui.mochila.sacar_doble_click"), 12,
                Palette.Danger);

            // ⚠ Sacar un perk equipado LO DESTRUYE: no vuelve a la mochila. Es la
            // acción irreversible más fácil de hacer sin querer del juego, y el
            // renglón rojo de arriba mide 12 px.
            UiTooltip.Help(UiLayout.DetailEquip, "ui.ayuda.sacar_destruye");

            return;
        }

        if (_cosaElegida < 0 || _cosaElegida >= partida.Backpack.Items.Count)
        {
            this.Label(UiLayout.DetailPanel, Loc.Get("ui.mochila.elegi_algo"), 15,
                Palette.TextDisabled, HorizontalAlignment.Center);

            return;
        }

        var cosa = partida.Backpack.Items[_cosaElegida];

        if (cosa.Kind == ItemType.Perk)
            DetalleDePerk(partida, DataLoader.Current.Perks.Get(cosa.Id));
        else
            DetalleDeArma(partida, partida.Backpack.WeaponById(cosa.Id));

        BotonEquipar(partida);
        BotonSacarDeLaMochila(partida);
    }

    private void DetalleDePerk(Run partida, PerkDefinition def)
    {
        var color = Palette.Rarity(def.Tier);

        this.Label(UiLayout.DetailTitle.Row(0f, 28f), Loc.Perk(def).ToUpperInvariant(), 21,
            color, HorizontalAlignment.Center);

        this.Label(UiLayout.DetailTitle.Row(26f, 22f), Loc.Tier(def.Tier).ToUpperInvariant(), 14,
            Palette.TextMuted, HorizontalAlignment.Center);

        var icono = UiLayout.Square(UiLayout.DetailIcon);

        this.Slot(icono, color);

        if (!IconLoader.Draw(this, def.Key,
                icono.Position.X + 8f, icono.Position.Y + 8f, icono.Size.X - 16f))
        {
            this.Label(icono, Loc.Perk(def)[..1].ToUpperInvariant(),
                (int)(icono.Size.X * 0.5f), color, HorizontalAlignment.Center);
        }

        // ⚠ ACÁ VA QUÉ HACE, y es lo que faltaba.
        //
        // Antes este lugar lo ocupaban las clases que lo pueden equipar, con
        // este argumento: «los perks no tienen texto propio y la magnitud ya
        // está abajo, en los stats». El argumento no se sostiene con un Oro en
        // la mano: «sobrepenetra 40%» no dice qué hace ni sobre qué es ese 40, y
        // el jugador equipaba una de las ocho oportunidades de la corrida sin
        // saber qué compró.
        //
        // Las clases no se perdieron: bajaron abajo de los stats, que es donde
        // hay lugar y donde igual se leen antes de apretar EQUIPAR.
        this.Paragraph(UiLayout.DetailDescription, Loc.PerkText(def), 13, Palette.TextSecondary);

        // El cartel repite la frase entera: el párrafo se corta con «…» cuando no
        // entra, y justo los Únicos —que combinan dos efectos— son los más largos.
        UiTooltip.Over(UiLayout.DetailDescription, $"{Loc.Perk(def)}\n{Loc.PerkText(def)}");

        var stats = UiLayout.DetailStats;
        float alto = 22f;

        this.EffectRow(stats.Row(0f, alto + 4f), def, color);

        // El segundo número lo tienen los Únicos, que son la fusión de dos Oro:
        // Onda de Frío hace splash por el primero y ralentiza por este.
        if (def.MagnitudeB > 0)
            this.StatRow(stats.Row(alto, alto), Loc.Get("ui.mochila.receta"), $"{def.MagnitudeB}%");

        this.StatRow(stats.Row(alto * 2f, alto), Loc.Get("ui.mochila.tab_perks"),
            $"{def.MaxStacked}×");

        // ⚠ A QUÉ CLASES LES SIRVE. Es lo único de la ficha que no se puede
        // deducir mirando el icono, y un Oro que solo pueden usar dos de las
        // nueve equipado en la que no es, es un slot perdido.
        var sirve = new System.Text.StringBuilder();

        foreach (var c in ChoiceBuilder.All)
        {
            if (!def.WorksFor(c))
                continue;

            if (sirve.Length > 0)
                sirve.Append(" · ");

            sirve.Append(Loc.Class(c));
        }

        this.Paragraph(UiLayout.DetailClasses,
            sirve.Length == 0 ? Loc.Get("ui.tienda.cualquier_clase") : sirve.ToString(),
            13, Palette.TextMuted);
    }

    private void DetalleDeArma(Run partida, Weapon? arma)
    {
        if (arma is null)
            return;

        var color = arma.Ability != WeaponAbility.None ? Palette.Epic : ColorDeRareza(arma.Rarity);

        this.Label(UiLayout.DetailTitle.Row(0f, 28f), Loc.Weapon(arma).ToUpperInvariant(), 19,
            color, HorizontalAlignment.Center);

        var icono = UiLayout.Square(UiLayout.DetailIcon);

        this.Slot(icono, color);
        this.Icon(icono.Inset(icono.Size.X * 0.22f), UiStyle.Icono.Weapon, color);

        if (arma.Ability != WeaponAbility.None)
        {
            this.Paragraph(UiLayout.DetailDescription,
                Loc.AbilityText(arma.Ability, arma.Magnitude), 14, Palette.Epic);
        }
        else
        {
            this.Label(UiLayout.DetailDescription, Loc.Get("ui.tienda.sin_habilidad"), 14,
                Palette.TextDisabled);
        }

        var stats = UiLayout.DetailStats;
        float alto = 22f;

        // ⚠ Los tres vienen en escala interna —centésimas de punto y milésimas de
        // celda—, así que pasan por Loc. Crudos dirían «1040 de daño» cuando el
        // defensor pega 10,4.
        this.StatTrio(stats.Row(0f, alto + 4f),
            Loc.Damage(arma.Damage), Loc.Range(arma.Range), arma.Cadence.ToString());

    }

    /// <summary>
    /// El botón de deshacerse de lo elegido: <b>vender</b> si es un arma,
    /// <b>tirar</b> si es un perk.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>No existía ninguno de los dos.</b> <c>ActionType.SellGear</c> y
    /// <c>ActionType.Discard</c> estaban implementadas en la simulación y las usaba
    /// el bot headless, pero <b>ninguna pantalla del juego las disparaba</b>: no
    /// había un solo gesto para sacar algo de la mochila. Con quince lugares, del
    /// orden de cien Bronce por corrida y la tienda llenando el bolso, eso es una
    /// mochila que se tapa a la oleada 20 y no se destapa nunca — y a partir de
    /// ahí cada drop cae al vacío en silencio.</para>
    ///
    /// <para><b>Vender paga y tirar no</b>, y la diferencia no es un descuido: el
    /// equipo se vende por su valor —ya lo dice su ficha— y un perk no tiene
    /// precio de venta en ningún lado. Inventarle uno sería abrir una fuente de oro
    /// que ninguna medición de <c>--balance</c> vio nunca.</para>
    ///
    /// <para>Vender no pregunta: devuelve oro y el arma valía eso. Tirar sí, porque
    /// destruye — la misma regla que sacar un perk equipado.</para>
    ///
    /// <para>Va en la fila del precio y no en una nueva: para un perk esa fila
    /// estaba <b>vacía</b>, y para un arma decía el valor, que es justo el número
    /// que el botón necesita mostrar.</para>
    /// </remarks>
    private void BotonSacarDeLaMochila(Run partida)
    {
        if (_cosaElegida < 0 || _cosaElegida >= partida.Backpack.Items.Count)
            return;

        var cosa = partida.Backpack.Items[_cosaElegida];

        if (cosa.Kind == ItemType.Weapon)
        {
            var arma = partida.Backpack.WeaponById(cosa.Id);

            if (arma is null)
                return;

            this.Button(UiLayout.DetailPrice,
                $"{Loc.Get("ui.mochila.vender")}  {arma.Value}", Palette.Gold);

            UiTooltip.Over(UiLayout.DetailPrice, Loc.Get("ui.mochila.vender_ayuda"),
                "ui.ayuda.vender_arma");

            return;
        }

        this.Button(UiLayout.DetailPrice, Loc.Get("ui.mochila.tirar"), Palette.Danger,
            relleno: false);

        // ⚠ El perk NO se vende: se tira. Es la asimetría que sorprende, porque
        // el arma de al lado sí devuelve oro.
        UiTooltip.Over(UiLayout.DetailPrice, Loc.Get("ui.mochila.tirar_ayuda"),
            "ui.ayuda.tirar_perk");
    }

    /// <summary>El botón de equipar, que necesita una clase mirada y que entre.</summary>
    private void BotonEquipar(Run partida)
    {
        var ficha = _claseElegida is { } clase ? partida.Roster.Find(clase) : null;

        // ⚠ El motivo, no un booleano: es el renglón que se dibuja abajo cuando
        // el botón queda apagado. Ver MotivoParaNoEquipar.
        string? motivo = ficha is null ? null : MotivoParaNoEquipar(partida, ficha, _cosaElegida);

        bool puede = ficha is not null && motivo is null && _marcados.Count <= 1;

        this.Button(UiLayout.DetailBuy, Loc.Get("ui.mochila.equipar"),
            puede ? Palette.Success : Palette.TextDisabled, habilitado: puede);

        // ⚠ Equipar es de la CLASE, no de la ficha, y sacarlo después destruye el
        // perk. Las dos mitades van juntas en el mismo cartel porque juntas son
        // la decisión: equipar acá es gastar el perk, no prestarlo.
        UiTooltip.Help(UiLayout.DetailBuy, "ui.ayuda.equipar");

        // La segunda ranura la disputan dos cosas, y manda la mejora: si lo
        // marcado es de un tier mejorable, ahí va el contador de la receta.
        // Enterarse de que existe la mecánica no puede depender de leer el pie.
        if (BotonMejorar(partida))
            return;

        if (ficha is null)
        {
            this.Label(UiLayout.DetailEquip, Loc.Get("ui.mochila.elegi_clase"), 13,
                Palette.TextDisabled, HorizontalAlignment.Center);

            return;
        }

        if (puede)
            return;

        // Con varios marcados para mejorar, el motivo no es del perk: es que
        // equipar y mejorar son dos gestos y hay uno a medias.
        string texto = motivo ?? Loc.Get("ui.mochila.desmarca_para_equipar");

        // Dos renglones: el motivo de la ranura de comportamiento nombra al perk
        // que la ocupa y no entra en uno solo.
        this.Paragraph(UiLayout.DetailEquip, texto, 13, Palette.Danger);

        UiTooltip.Over(UiLayout.DetailEquip, texto);
    }

    /// <summary>
    /// El botón de mejorar, con el contador de la receta. <c>false</c> si no
    /// corresponde dibujarlo.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Se dibuja desde la primera marca, apagado y diciendo «1/4».</b>
    /// Un botón que aparece recién cuando la receta está completa no enseña la
    /// receta: el jugador tendría que adivinar que juntar cuatro sirve para algo,
    /// y nunca junta cuatro.</para>
    ///
    /// <para>El tope de Oro fabricado por corrida se dice acá y no en el pie: es
    /// donde el jugador se entera de que ya no puede, en el momento en que lo
    /// intenta.</para>
    /// </remarks>
    private bool BotonMejorar(Run partida)
    {
        if (_marcados.Count == 0 || TierMarcado(partida) is not { } tier)
            return false;

        int cuantos = partida.UpgradeCost(tier);

        if (cuantos < 2)
            return false;

        // ⚠ El motivo lo decide la simulación, no esta pantalla. Ver
        // MotivoParaNoMejorar: repetir los chequeos acá es lo que dejaba el
        // botón encendido en cinco casos en los que la mejora iba a rebotar.
        string? motivo = MotivoParaNoMejorar(partida);
        bool listo = motivo is null;

        // Faltan marcas no es un problema: es la receta a medias, y el contador
        // ya lo dice. Se dibuja apagado y sin cartel, como siempre.
        bool aMedias = motivo == "ui.mochila.mejora.faltan";

        var color = listo || aMedias ? Palette.Rarity(tier + 1) : Palette.Danger;

        this.Button(UiLayout.DetailEquip,
            $"{Loc.Get("ui.mochila.mejorar")} {_marcados.Count}/{cuantos}",
            listo ? color : Palette.TextDisabled, habilitado: listo);

        // ⚠ MEJORAR es la salida de las sobras, y es la mecánica que menos se
        // descubre sola: hay que marcar varios en la grilla ANTES de que el
        // botón exista. Sin esto, la mochila se llena de bronce que el jugador
        // tira uno por uno.
        UiTooltip.Help(UiLayout.DetailEquip,
            tier == PerkTier.Silver ? "ui.ayuda.mejorar_a_oro" : "ui.ayuda.mejorar");

        if (listo || aMedias)
            return true;

        // El renglón de abajo dice POR QUÉ. El del tope lleva además el contador,
        // que es la única forma de saber cuánto cupo queda sin abrir un manual.
        string texto = motivo == "ui.mochila.mejora.tope_de_oro"
            ? $"{Loc.Get(motivo)} {partida.CraftedGold}/{partida.MaxCraftedGold}"
            : Loc.Get(motivo!);

        var pie = new Rect2(UiLayout.DetailEquip.Position.X, UiLayout.DetailEquip.End.Y + 2f,
            UiLayout.DetailEquip.Size.X, 34f);

        this.Paragraph(pie, texto, 12, Palette.Danger);
        UiTooltip.Over(UiLayout.DetailEquip, texto);

        return true;
    }

    /// <summary>La columna derecha: los seis slots de la clase mirada y su nivel.</summary>
    private void ColumnaDeLaClaseMirada(Run partida)
    {
        this.Panel(UiLayout.ShopPanel, opacidad: 1f);

        if (_claseElegida is not { } clase)
        {
            this.Label(UiLayout.ShopPanel, Loc.Get("ui.mochila.elegi_clase"), 16,
                Palette.TextDisabled, HorizontalAlignment.Center);

            return;
        }

        var ficha = partida.Roster.Find(clase);

        if (ficha is null)
            return;

        this.SectionTitle(UiLayout.ShopTitle, Loc.Class(clase), Palette.TextPrimary);

        this.Label(UiLayout.ShopTimer,
            $"{Loc.Get("ui.mochila.nivel")} {ficha.Level} · {ficha.Perks.Count}/{ficha.OpenSlots}",
            16, Palette.TextMuted, HorizontalAlignment.Center);

        // ⚠ Los slots cerrados se ven CERRADOS, no ocultos: el jugador tiene que
        // ver qué le falta para saber que levelear sirve.
        for (int s = 0; s < DataLoader.Current.Classes.PerkSlotsPerClass; s++)
        {
            var r = RectSlot(s);

            if (s >= ficha.OpenSlots)
            {
                this.EmptySlot(r, bloqueado: true);

                this.Label(r.Row(r.Size.Y - 20f, 18f), $"{Loc.Get("ui.hud.niv")} {s + 1}", 12,
                    Palette.TextDisabled, HorizontalAlignment.Center);

                // ⚠ El candado con un número al lado no dice qué abre el número.
                // Es el motivo entero por el que subir de nivel se paga, y estaba
                // escrito en 12 px de gris sobre gris.
                UiTooltip.Over(r, $"{Loc.Get("ui.hud.niv")} {s + 1}", "ui.ayuda.slot_cerrado");

                continue;
            }

            if (s >= ficha.Perks.Count)
            {
                this.EmptySlot(r);
                UiTooltip.Help(r, "ui.ayuda.slot_vacio");
                continue;
            }

            this.PerkSlot(r, DataLoader.Current.Perks.Get(ficha.Perks[s]),
                elegido: s == _slotMirado);
        }

        StatsDeClase(partida, clase);
        DibujarSubirNivel(partida, ficha);
        DibujarComportamiento(partida, ficha);
    }

    private void StatsDeClase(Run partida, DefenderClass clase)
    {
        var stats = partida.Roster.StatsOf(clase, partida.Backpack);
        var panel = UiLayout.ShopPanel;

        float x = panel.Position.X + 20f;
        float ancho = panel.Size.X - 40f;
        float y = YStats();
        float alto = 21f;

        // ⚠ Los dos vienen en escala interna. Es la pantalla donde se ve si un
        // perk de alcance sirvió: con el redondeo a celdas enteras de antes,
        // equipar Halcón no movía este número ni un poco.
        this.StatTrio(new Rect2(x, y, ancho, alto + 4f),
            Loc.Damage(stats.Damage), Loc.Range(stats.RangeMilli), stats.CadenceTicks.ToString());

        string habilidad =
            stats.Behavior != PerkEffect.None ? Loc.Effect(stats.Behavior)
            : stats.Ability != WeaponAbility.None ? Loc.Ability(stats.Ability)
            : "";

        if (habilidad.Length > 0)
        {
            var fila = new Rect2(x, y + alto + 8f, ancho, alto);

            this.StatRow(fila, Loc.Get("ui.mochila.habilidad"), habilidad, Palette.Legendary);

            // ⚠ Acá va la regla más contraintuitiva del juego: SOLO EL GOLPE
            // DIRECTO PROCCEA. Este renglón es el único lugar donde el jugador ve
            // qué comportamiento tiene activo, así que es donde tiene sentido
            // decirle que el splash de ese comportamiento no vuelve a disparar
            // nada. Sin eso, el que combina cadena con quemadura espera una
            // multiplicación que el juego nunca va a hacer.
            UiTooltip.Over(fila, habilidad, "ui.ayuda.golpe_directo");
        }
    }

    /// <summary>
    /// El botón de comprar el próximo nivel, con su precio.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Es el sumidero de oro de la corrida.</b> Con el tablero lleno y
    /// la mochila llena no quedaba nada que comprar y el oro dejaba de ser un
    /// recurso: medido con <c>--balance</c>, ~127.000 sin gastar al morir.</para>
    ///
    /// <para>Va acá y no en la preparación porque es donde se ven los slots
    /// cerrados: el motivo para pagar queda dibujado al lado del botón.</para>
    ///
    /// <para>El precio se muestra también cuando no alcanza. Un botón que
    /// desaparece no enseña cuánto falta.</para>
    /// </remarks>
    private void DibujarSubirNivel(Run partida, RosterClass ficha)
    {
        var caja = RectSubirNivel();

        if (ficha.Level >= ficha.MaxLevel)
        {
            this.Button(caja, Loc.Get("ui.mochila.nivel_maximo"), Palette.TextDisabled,
                habilitado: false);

            return;
        }

        int precio = DataLoader.Current.Curves.Economy.LevelUpPrice(ficha.Level + 1);
        bool alcanza = precio <= partida.Gold;

        this.Button(caja,
            $"{Loc.Get("ui.mochila.subir_nivel")} {ficha.Level + 1}",
            alcanza ? Palette.Success : Palette.Danger, habilitado: alcanza);

        this.Price(new Rect2(caja.Position.X, caja.End.Y + 4f, caja.Size.X, 22f),
            precio, alcanza, HorizontalAlignment.Center);

        // ⚠ El precio es cuadrático y eso hay que decirlo: subir de nivel es el
        // sumidero de oro de la corrida, y el que lo lee como «cuesta 1200» va a
        // planear el siguiente en 1200 también.
        UiTooltip.Over(caja, $"{Loc.Get("ui.mochila.nivel")} {ficha.Level + 1} · {precio}",
            "ui.ayuda.subir_nivel");
    }

    /// <summary>
    /// Los dos botones de «qué comportamiento manda»: el del perk o el del arma.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>La ranura de comportamiento sigue siendo UNA.</b> Esto no
    /// permite tener los dos: permite elegir cuál habla. Tenerlos juntos
    /// multiplicaría las combinaciones a balancear por el catálogo entero de
    /// armas.</para>
    ///
    /// <para><b>Los stats del arma se conservan en los dos casos</b>, y ese es
    /// todo el punto. Antes ganaba el arma siempre, así que encontrar una
    /// excelente obligaba a apagar un Único que costó dos jefes y un plano; el
    /// jugador terminaba dejándola sin equipar, que es lo peor que le puede pasar
    /// a un drop. La línea de abajo lo dice explícitamente, porque si no se dice
    /// el jugador va a suponer que elegir el perk le apaga el arma entera.</para>
    /// </remarks>
    private void DibujarComportamiento(Run partida, RosterClass ficha)
    {
        if (!partida.HasBehaviorConflict(ficha.DefenderClass))
            return;

        var izquierda = RectComportamiento(elDelPerk: true);

        this.Label(new Rect2(izquierda.Position.X, izquierda.Position.Y - 22f,
                UiLayout.ShopPanel.Size.X - 40f, 20f),
            Loc.Get("ui.mochila.comportamiento_activo").ToUpperInvariant(), 13, Palette.TextMuted);

        this.Button(izquierda, Loc.Get("ui.mochila.el_perk"),
            ficha.PreferPerkBehavior ? Palette.Legendary : Palette.BorderNormal,
            relleno: ficha.PreferPerkBehavior);

        UiTooltip.Over(izquierda, Loc.Get("ui.mochila.el_perk"),
            "ui.ayuda.conflicto_comportamiento");

        var derecha = RectComportamiento(elDelPerk: false);

        this.Button(derecha, Loc.Get("ui.mochila.el_arma"),
            ficha.PreferPerkBehavior ? Palette.BorderNormal : Palette.Epic,
            relleno: !ficha.PreferPerkBehavior);

        UiTooltip.Over(derecha, Loc.Get("ui.mochila.el_arma"),
            "ui.ayuda.conflicto_comportamiento");

        var pie = new Rect2(izquierda.Position.X, izquierda.End.Y + 4f,
            UiLayout.ShopPanel.Size.X - 40f, 34f);

        this.Paragraph(pie, Loc.Get("ui.mochila.stats_del_arma_quedan"), 12, Palette.TextMuted);
    }

    /// <summary>El pie: cómo se cierra y la leyenda de rarezas.</summary>
    private void Pie()
    {
        this.Panel(UiLayout.FooterBar, opacidad: 1f);

        this.Label(UiLayout.FooterTip,
            $"{Loc.Get("ui.mochila.esc_cerrar")}  ·  {Loc.Get("ui.mochila.sacar_destruye")}" +
            $"  ·  {Loc.Get("ui.mochila.marcar_para_mejorar")}",
            14, Palette.TextMuted);

        this.RarityLegend(UiLayout.FooterLegend);
    }

    private void DibujarConfirmacion()
    {
        this.Scrim();

        var panel = new Rect2((Canvas.CanvasWidth / 2f) - 320f, (Canvas.CanvasHeight / 2f) - 110f,
            640f, 220f);

        this.Panel(panel, elevado: true, opacidad: 1f);
        this.Glow(panel, Palette.Danger, 2f, 6);
        DrawRect(panel, Palette.Danger, filled: false, width: 3f);

        // ⚠ El texto dice CUÁL de las dos destrucciones es. Sacar un perk puesto
        // y tirar uno guardado son irreversibles las dos, y con un cartel común
        // el jugador confirma sin saber cuál de los dos perks va a perder.
        string clave = _confirmarTirar >= 0 ? "tirar" : "sacar";

        this.Label(panel.Row(40f, 40f), Loc.Get($"ui.mochila.confirmar_{clave}_titulo"), 27,
            Palette.Danger, HorizontalAlignment.Center);

        this.Label(panel.Row(84f, 30f), Loc.Get($"ui.mochila.confirmar_{clave}_detalle"), 17,
            Palette.TextSecondary, HorizontalAlignment.Center);

        this.Button(RectConfirmar(), Loc.Get("ui.mochila.destruir"), Palette.Danger);
        this.Button(RectCancelar(), Loc.Get("ui.mochila.cancelar"), Palette.BorderNormal,
            relleno: false);
    }

    /// <summary>
    /// El color de la rareza de un arma.
    /// </summary>
    /// <remarks>
    /// Usa los mismos seis marcos que todo lo demás
    /// (<see cref="Palette.Rarity"/>), pero la legendaria de arma cae en el
    /// <b>naranja</b> del ámbar y no en el dorado del perk de Oro: son dos
    /// escalas distintas —una de perks, otra de armas— y mezclarlas haría creer
    /// que un arma legendaria y un perk de Oro son la misma categoría de cosa.
    /// </remarks>
    private static Color ColorDeRareza(WeaponRarity r) => r switch
    {
        WeaponRarity.Rare => Palette.Rare,
        WeaponRarity.Epic => Palette.Epic,
        WeaponRarity.Legendary => Palette.Warning,
        _ => Palette.Common,
    };
}
