# Licencias del audio

**Todo lo que hay en esta carpeta es CC0 1.0 (dominio público).** Ningún archivo
pide atribución, ninguno cobra regalías, todos se pueden modificar y todos se
pueden vender adentro de un juego comercial.

Verificado el **2026-09-05**, entrando a la página oficial de cada recurso y
leyendo el campo de licencia. La evidencia —copia de la página, texto legal de
CC0 y checksum del original— está en `Resources\Audio\Licenses\`.

Vive acá y no en `Doc\` por la misma razón que `IBMPlex-LICENSE.txt` vive al lado
de la tipografía en `Data\font\`: **la licencia tiene que estar donde está el
archivo.** Un documento en otra carpeta se desincroniza; este no.

---

## Por qué CC0 y no «royalty free»

CC0 es una **renuncia de derechos, no un permiso**. El autor no puede echarse
atrás: no hay nada que revocar. Un «gratis para uso comercial» escrito a mano en
la página de un pack sí lo puede cambiar el autor mañana, y ahí el juego ya está
publicado.

Por eso quedaron afuera varios packs buenos: los CC-BY (obligan a una línea de
créditos permanente) y los que dicen «free for commercial use» sin nombrar una
licencia formal.

## Content ID

**No aplica a un juego en Steam o Play**: Content ID solo corre en YouTube. El
riesgo real es el tráiler, el devlog y los streamers, y existe incluso con CC0
—hay quien registra dominio público en Content ID y dispara reclamos falsos—. La
defensa es la carpeta de evidencia: la disputa se gana mostrando el PDF.

Ninguno de estos recursos tiene reclamos conocidos.

---

## Qué salió de dónde

| Carpeta | Recurso | Autor | Página oficial |
|---|---|---|---|
| `sfx/ui/` | Interface Sounds · UI Audio | Kenney | [interface-sounds](https://kenney.nl/assets/interface-sounds) · [ui-audio](https://kenney.nl/assets/ui-audio) |
| `sfx/impact/` | Impact Sounds | Kenney | [impact-sounds](https://kenney.nl/assets/impact-sounds) |
| `sfx/tower/` | RPG Audio · Music Jingles · RPG Sound Pack | Kenney · artisticdude | [rpg-audio](https://kenney.nl/assets/rpg-audio) · [rpg-sound-pack](https://opengameart.org/content/rpg-sound-pack) |
| `sfx/game/` | Music Jingles · Interface Sounds | Kenney | [music-jingles](https://kenney.nl/assets/music-jingles) |
| `sfx/attack/` | RPG Sound Pack · 80 CC0 RPG SFX · Interface Sounds · Impact Sounds | artisticdude · rubberduck · Kenney | [rpg-sound-pack](https://opengameart.org/content/rpg-sound-pack) · [80-cc0-rpg-sfx](https://opengameart.org/content/80-cc0-rpg-sfx) |
| `sfx/enemy/` | 80 CC0 RPG SFX · RPG Sound Pack · Impact Sounds | rubberduck · artisticdude · Kenney | [80-cc0-rpg-sfx](https://opengameart.org/content/80-cc0-rpg-sfx) |
| `music/menu.mp3` | Medieval: Exploration | RandomMind | [medieval-exploration](https://opengameart.org/content/medieval-exploration) |
| `music/calm.wav` | Medieval: The Old Tower Inn (versión loop) | RandomMind | [medieval-the-old-tower-inn](https://opengameart.org/content/medieval-the-old-tower-inn) |
| `music/normal.mp3` | Medieval: Market Day (versión loop) | RandomMind | [medieval-market-day](https://opengameart.org/content/medieval-market-day) |
| `music/intense.mp3` | Medieval: Battle | RandomMind | [medieval-battle](https://opengameart.org/content/medieval-battle) |
| `music/boss.wav` | Epic Boss Battle (seamlessly looping) | SubspaceAudio, o sea Juhani Junkala | [boss-battle-music](https://opengameart.org/content/boss-battle-music) |
| `music/victory.mp3` | Medieval: Victory Theme | RandomMind | [medieval-victory-theme](https://opengameart.org/content/medieval-victory-theme) |
| `music/defeat.mp3` | Medieval: Defeat Theme | RandomMind | [medieval-defeat-theme](https://opengameart.org/content/medieval-defeat-theme) |

⚠ **`boss.wav` está subido por «SubspaceAudio» y el archivo se llama «Juhani
Junkala».** No es un pack mal etiquetado: son la misma persona, SubspaceAudio es
el alias de Juhani Junkala. Se aclara acá porque la discrepancia entre el autor
de la página y el nombre del archivo es exactamente la pinta que tiene una subida
robada, y alguien la va a mirar con desconfianza otra vez dentro de un año.

---

## Créditos, aunque no hagan falta

Ninguna de estas licencias obliga, pero es la moneda con la que se paga este
ecosistema y no cuesta nada. Si se arma pantalla de créditos:

```
Audio (CC0):
  Kenney — kenney.nl
  artisticdude, rubberduck — opengameart.org
  RandomMind — opengameart.org
  Juhani Junkala (SubspaceAudio) — opengameart.org
```

---

## Antes de agregar un sonido nuevo

1. Verificar la licencia **en la página oficial**, no en un agregador.
2. Correr `Resources\Audio\tools\bajar.sh`, que ya guarda la evidencia sola:
   copia de la página, URL y checksum del original.
3. Anotar la fila en `Resources\Audio\AUDIO_MANIFEST.csv`.
4. Agregar el archivo a `audio.json` y, si es un evento nuevo, a `SoundId`.

⚠ **Y no hace falta más que eso.** CC0 es una renuncia de derechos, no un
permiso: no hay nada que el autor pueda revocar después, así que no hay que
archivar PDFs ni capturas. Lo que sí hay que mirar antes de bajar es **de quién**
sale: el único riesgo que queda es que alguien haya subido material ajeno
etiquetado CC0, y contra eso no protege ningún archivo guardado — protege elegir
autores con años de historial y miles de descargas, que es el criterio con el que
se eligieron estos.
