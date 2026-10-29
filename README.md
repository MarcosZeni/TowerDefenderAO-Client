<div align="center">

# Tower of Argentum — Client

**Tower defense roguelike 2D, online, con el bestiario y las clases de Argentum Online.**
*A deterministic, integer-only tower defense roguelike built in Godot 4 + C#, with a global speed ranking.*

![Godot 4.8](https://img.shields.io/badge/Godot-4.8--dev3-478CBF?logo=godotengine&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-13-239120?logo=csharp&logoColor=white)
![Plataformas](https://img.shields.io/badge/plataformas-Windows%20%7C%20Android-lightgrey)

</div>

---

## Qué es

Una **corrida** son **100 oleadas** seguidas, repartidas en **ocho áreas** de Argentum Online (Ullathorpe primero), sin cortes ni selección de nivel. Se ponen **defensores** —las nueve clases de combate de AO: mago, clérigo, guerrero, asesino, ladrón, bardo, druida, paladín y cazador— junto a una ruta, y se aguantan oleadas de criaturas que caminan hacia **la Torre**. Si la Torre cae, se terminó. Si matás al jefe de la oleada 100, ganaste.

El **ranking global es el objetivo**: ordena por `ganó → oleada → ticks`, así que arriba quedan los que ganaron y, entre ellos, **el más rápido**. Por eso existe *adelantar oleada*: jugar bien es jugar rápido.

Entre corridas sobreviven solo dos cosas: el **oro de la billetera** y los **planos de fusión** aprendidos. Todo lo demás —clases, niveles, perks, equipo— muere con la corrida.

## Lo que lo hace interesante

- **La partida entera corre en el cliente**, pero el servidor puede **re-simularla** y verificar el resultado. Eso exige una simulación **determinista**: paso fijo a 30 Hz, **todo en enteros**, PRNG propio, orden de fases congelado. Dentro de `Simulation/` están prohibidos `float`, `double`, `System.Random` y las funciones trascendentes de `Math`; una **guarda de build** ([Tools/verify-simulation.ps1](Tools/verify-simulation.ps1)) falla la compilación si aparece uno.
- **La simulación es un paquete aparte** ([Simulation/](Simulation/), `TowerOfArgentum.Simulation`, sin dependencia de Godot). Se publica como NuGet y el [GameApi](https://github.com/TowerDefenderAO/GameApi) lo consume para revalidar corridas con el mismo código que las jugó.
- **El log guarda decisiones, no resultados.** Poner, vender, equipar, comprar, elegir perk. Todo lo que decide el RNG sembrado (drops, tienda, oferta de perks) el servidor lo regenera desde la seed. El mismo log es el guardado local y el *checkpoint* en la nube.
- **Cero números de balance en el código.** Los ocho JSON de [Data/](Data/) llevan los números **con comentarios al lado explicando por qué es ese número**, y entran a un **hash de temporada**: cambiar un número vuelve irreplicables las corridas viejas, así que cada rebalanceo es una temporada nueva del ranking.
- **Sistemas que se combinan**: 9 clases, 54 perks en cuatro tiers (bronce y plata son números; oro y único son comportamientos), fusión de dos Oro en un Único por plano, armas generadas con habilidad, sinergias por composición de tablero, tienda rotativa cada tres oleadas, cuatro modos de targeting por defensor, formaciones de spawn y rasgos de enemigo.
- **Localizado** (`es`, `en`): los datos guardan claves estables; los textos viven en `Data/lang/` y no entran al hash, así que traducir no corta la temporada.
- **Lienzo fijo de 1920×1080** con `aspect = keep`: cambiar la resolución cambia la nitidez, nunca el campo visible. Con ranking por velocidad, más ruta visible sería ventaja de hardware.
- **Arte y audio viajan en dos `.pck` aparte** del ejecutable, montados al arrancar: el arte va crudo porque el piso se hornea con `Image.BlitRect`, y el audio aparte para poder parchearlo sin reexportar.

## Estructura

| Carpeta | Qué hay |
|---|---|
| [Simulation/](Simulation/) | La simulación determinista: tick, combate, estados, perks, tienda, oleadas, RNG, log binario. Proyecto propio, sin Godot |
| [Scripts/](Scripts/) | El puente con Godot: bucle, pantallas, HUD, carga de datos, guardado, sesión |
| [Scripts/Net/](Scripts/Net/) | Cliente HTTP/JSON contra el GameApi |
| [Scripts/Ui/](Scripts/Ui/) | Tema, paleta, widgets, tooltips, piezas 9-slice |
| [Scripts/Graphics/](Scripts/Graphics/) y [Scripts/Audio/](Scripts/Audio/) | Piso horneado, cristal de la Torre, portal; banco de sonidos y música |
| [Scripts/Tools/](Scripts/Tools/) | Las herramientas offline (abajo) |
| [Data/](Data/) | Los ocho JSON de balance, los 12 layouts en ASCII, `lang/`, fuentes y audio |
| [Scenes/](Scenes/) | `Main.tscn`, el editor de layouts y el visor de sprites |
| [Tools/](Tools/) | La guarda de determinismo que corre antes de compilar |

El arte **no está acá**: vive en el repo hermano [Resources](https://github.com/TowerDefenderAO/Resources) y se empaqueta con `--empaquetar-arte`. El diseño del juego, el porqué de cada regla y el historial están en [Doc](https://github.com/TowerDefenderAO/Doc).

## Compilar y correr

Requisitos: **.NET 10 SDK** y **Godot 4.8-dev3 mono**.

```bash
dotnet build                      # compila los scripts C# (y corre la guarda de determinismo)
```

Después se abre el proyecto en el editor de Godot y se corre `Main.tscn`. Por defecto el cliente apunta al servidor alojado; para desarrollar contra una API local:

```bash
Godot_v4.8-dev3_mono_win64.exe --path . -- --servidor http://127.0.0.1:5090
```

Sin `arte.pck` y `audio.pck` al lado del ejecutable el juego arranca igual, pero dibuja con rectángulos y queda mudo. Se arman con las herramientas de abajo.

## Herramientas offline

Todas se corren en modo headless y **ninguna toca el juego**:

```bash
Godot_v4.8-dev3_mono_win64.exe --headless --path . -- <comando>
```

| Comando | Qué hace |
|---|---|
| `--validar` | Que toda referencia entre JSON exista, que los números cierren, que las recetas de fusión sean alcanzables y que no falte ningún texto |
| `--determinismo` | Cinco seeds, dos corridas cada una, comparadas tick a tick |
| `--balance N [piso, normal o techo]` | N corridas headless por política de juego. Reporta hasta dónde llega cada una y el oro sin gastar |
| `--exportar-balance <ruta> [N]` | Un `.xlsx` con los datos vivos y, con `N`, las mediciones |
| `--emparejar-layouts N` | Equidad entre layouts: bloquea celdas hasta que ninguno tenga más de N colocables junto a la ruta |
| `--importar-npcs`, `--importar-apariencia` | Leen los `.dat` de Argentum Online una vez, offline, y escriben los JSON |
| `--traer-bestiario N`, `--traer-arte`, `--verificar-arte` | El pipeline de arte: traen hojas al repo de Resources y las verifican píxel por píxel |
| `--empaquetar-arte`, `--empaquetar-audio` | Arman los dos `.pck` que viajan al lado del ejecutable |
| `--recortar-ui` | Parte la hoja de piezas de interfaz en PNG sueltos con su margen de 9-slice |

## Convenciones

- **Identificadores en inglés, prosa en español.** Tipos, métodos, carpetas y namespaces en inglés; comentarios, documentos y commits en español, con acentos.
- **`Tower` es lo que se defiende; `Defender` es lo que ataca.** Son dos tipos distintos a propósito.
- **Dentro de la simulación, las unidades no son las de los datos**: el daño y el HP van en centésimas de punto y el alcance en milésimas de celda. Los JSON se escriben en puntos y celdas enteras.
- **La versión de los datos no se sube sin decidirlo**: subirla corta la temporada y deja las corridas abiertas del otro lado.

## Créditos

- Clases, criaturas, hechizos y gráficos: **[Argentum Online](https://argentumonline.org/)**, traídos a través del pipeline de **Austral AO**.
- Audio: **[Kenney](https://kenney.nl/)**, **RandomMind**, **artisticdude**, **rubberduck** y **Juhani Junkala** vía [OpenGameArt](https://opengameart.org/), todo CC0. La evidencia de cada licencia está en [Data/audio/LICENSES.md](Data/audio/LICENSES.md).
- Fuentes: **IBM Plex Mono** (OFL) y **RPG Awesome**.
