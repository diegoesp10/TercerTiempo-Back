# TercerTiempo.Analisis

Analizador del vídeo de un partido: detecta jugadores, árbitros y balón, los sigue, separa los equipos, calibra el
campo y de ahí saca la **posesión** (total, por mitad, por tercio, por minuto), mapas de calor, distancias y
velocidades por jugador, y **eventos candidatos** (balón fuera: saque de banda, córner, saque de puerta; tiros y tiros
a puerta; goles con el saque de centro que los confirma; paradas; parones y descanso). Lo deja en una carpeta como
`analisis.json`, `resumen.md`, `acta-propuesta.json` (con la forma que pide el acta interna de la API), mapas,
recortes de cada jugador seguido, `posiciones.csv.gz` y clips de los eventos. Opcionalmente, un modelo de lenguaje
con visión (la API de Claude o un modelo local vía Ollama) confirma los eventos con tres fotogramas, lee dorsales en
los recortes y redacta el resumen.

Es un programa aparte de la API y del servicio de vídeo (Python, GPU); la evaluación previa y el plan están en
[`docs/lector-videos.md`](../../docs/lector-videos.md). Desde el 06-11-2026 la app lo lanza sola: ver «Integración con
la app».

## Qué sale y con qué fiabilidad

| Dato | Cómo | Fiabilidad con cámara fija lateral a 1080p |
|---|---|---|
| Posesión por equipo, mitad, tercio y minuto | Jugador más cercano al balón con histéresis; el balón en vuelo es de quien lo lanzó | Alta |
| Mapas de calor, distancia, velocidad máxima | Posiciones proyectadas al campo (homografía por los puntos de las líneas) | Alta si la calibración es buena |
| Saques de banda, córners, saques de puerta | Balón fuera + quién lo tocó + hacia dónde ataca cada equipo | Media-alta |
| Tiros / a puerta | Balón rápido hacia la portería desde menos de 35 m; corte con la línea de gol | Media (candidatos: revisar el clip) |
| Goles | Balón que cruza entre los postes; se confirma si sigue un saque de centro | Media-alta con confirmación |
| Paradas | Tiro a puerta seguido del portero con el balón | Media |
| Pases, pérdidas, recuperaciones por tracklet | Cadenas de toques del mismo equipo / cambios de equipo sin balón fuera | Media |
| Quién es quién | Recortes + dorsal leído por el modelo de lenguaje | Baja: el cuerpo técnico asigna cada tracklet a una ficha |
| De qué equipo es cada uno | Color de la camiseta (resumen Lab del torso, sin el césped, con la claridad medida respecto al césped del recorte; dos grupos por KMeans) | Alta si las dos equipaciones se distinguen, también de noche |

Faltas, fueras de juego y tarjetas no se deducen del vídeo: vienen del acta oficial.

## Instalación (Windows, con GPU)

Hace falta `uv` (gestor de Python) y una GPU NVIDIA con driver reciente. El propio `uv` instala la Python 3.12 que
usa el proyecto (sin tocar la Python del sistema) y torch con CUDA 12.8 (la RTX 5070 Ti es Blackwell: torch ≥ 2.7).

```powershell
python -m pip install --user uv
cd backend\src\TercerTiempo.Analisis
uv sync --extra dev
uv run tt-analisis descargar-modelos --con-videos
uv run tt-analisis info
```

`descargar-modelos` baja los tres detectores YOLOv8 que publica Roboflow con su ejemplo de fútbol (jugadores,
balón y puntos del campo; ≈ 410 MB) y dos clips de muestra. Están entrenados con vídeo de televisión: el siguiente paso
es afinarlos con partidos asturianos (ver «Siguientes pasos»).

ffmpeg hace falta solo para los clips de los eventos (en Windows se busca también el de winget).

## Uso

```powershell
uv run tt-analisis analizar partido.mp4 --salida salidas\partido --equipos "Vallobín,Pumarín"
uv run tt-analisis analizar partido.mp4 --segmento 20:00-25:00 --video-anotado      # un tramo, con el vídeo anotado
uv run tt-analisis analizar partido.mp4 --descanso 52:30 --llm claude                # confirma eventos y redacta con la API de Claude
uv run tt-analisis analizar partido.mp4 --llm ollama --modelo-llm gemma4:12b         # lo mismo con un modelo local
uv run tt-analisis analizar partido.mp4 --fps 3 --sin-balon-fino --sin-clips         # más rápido, menos fino
```

Opciones: `--camara movil|fija` (ver abajo), `--fps` (fotogramas analizados por segundo, 5 por defecto),
`--segmento inicio-fin`, `--descanso minuto` (si no, se detecta por el parón más largo del medio), `--imgsz` (1280),
`--confianza` (0.3), `--tracker` (un yaml de Ultralytics; por defecto el del paquete según la cámara),
`--campo 100x62` (medidas reales del campo en metros; por defecto las del modelo del campo de Roboflow, 120 × 70), `--sin-afinados`
(usar los pesos públicos aunque haya pesos afinados), `--muestras-equipos` (600), `--max-fotogramas N` (para probar),
`--device cpu`, `--progreso` (el avance como líneas `##progreso N` en la salida estándar, sin barras: para quien lo
lanza como proceso) y `--exigir-gpu` (sin GPU CUDA termina con el código de salida 3 en vez de analizar en CPU).

### Afinar los detectores con partidos propios

Los tres detectores públicos están entrenados con televisión. Para que aprendan nuestros campos (focos, césped, el
encuadre de la cámara) se afinan con imágenes de partidos propios, **etiquetadas automáticamente**:

```powershell
uv run tt-analisis medir-campo partido.mp4                       # intenta medir el largo y el ancho del campo
uv run tt-analisis etiquetar partido.mp4 --salida datasets\partido [--campo 100x62]
uv run tt-analisis entrenar balon --dataset datasets\partido      # también campo, jugadores o todos
uv run tt-analisis etiquetar partido.mp4 --salida datasets\partido-r2 --afinados   # segunda ronda: el balón afinado etiqueta
uv run tt-analisis entrenar balon --dataset datasets\partido-r2
uv run tt-analisis evaluar todos --dataset datasets\partido       # públicos contra afinados, sobre la validación
```

`etiquetar` toma un fotograma cada 2 s (`--cada`) y escribe un dataset YOLO con tres conjuntos (`balon/`,
`personas/`, `campo/`, cada uno con su `data.yaml`), partido en entrenamiento y validación **por bloques de tiempo**
(un bloque de 2 min de cada cinco), más `revision/` con vistas previas y `muestras.jsonl` con el porqué de cada
decisión. Cómo se confirma cada cosa:

- **Balón**: el detector busca por teselas y a fotograma entero en el fotograma y en cuatro vecinos (±0,2 y ±0,4 s).
  Lo que separa un balón de una marca de la línea, un reflejo o un foco es que **se mueve respecto al césped**: se
  estima el movimiento de la cámara hacia cada vecino (flujo óptico sobre el fondo) y se mira si el candidato sigue en
  el mismo sitio del campo (quieto: marca, salvo que el detector lo vea con mucha confianza, que es un balón parado) o
  se ha desplazado con una velocidad coherente entre vecinos (balón en juego). Lo que cae sobre una persona (medias,
  botas) solo vale si el detector de jugadores, entrenado con otros datos, también ve ahí el balón. Los fotogramas con
  dos balones igual de buenos se descartan, y las marcas rechazadas entran como negativos difíciles.
- **Personas**: las del detector de jugadores con confianza ≥ 0,45; el fotograma entra solo si el balón está resuelto
  (para no enseñar que «ahí no hay balón» cuando sí lo hay).
- **Campo**: con seis o más puntos coherentes se ajusta la homografía; si se conocen las medidas reales del campo
  (`--campo`) se reproyectan los 32 vértices y quedan etiquetados también los que el detector no vio; si no, solo los
  puntos vistos (el resto queda sin etiqueta, que el entrenamiento ignora).

`--juez claude` manda además un recorte ampliado de cada candidato a balón a la API de Claude (hace falta
`ANTHROPIC_API_KEY`) y se queda con lo que confirma; `--juez ollama` hace lo mismo con un modelo local, pero
`gemma4:12b` tarda 12-19 s por recorte y da por balón casi todo, así que no se usa por defecto. Las pseudoetiquetas
se pueden corregir a mano con cualquier herramienta que lea YOLO (CVAT, Label Studio) antes de entrenar.

`entrenar` sigue entrenando desde los pesos públicos con el tronco congelado (`--congelar 10`), tasa de aprendizaje
baja y las mismas aumentaciones que el entrenamiento original; deja los pesos en `modelos/afinados/<que>.pt`, que
`analizar` usa automáticamente si existen. `evaluar` compara públicos y afinados sobre la partición de validación
con el mAP de Ultralytics y con lo que importa al analizador: balones encontrados a fotograma entero con el umbral del
análisis, precisión y exhaustividad por clase de persona, fotogramas calibrables y error de la homografía.

### La cámara

- **`--camara movil`** (por defecto): alguien graba con paneo y zoom (como el vídeo del Vallobín: un operador detrás
  de la cámara). El campo se calibra en cada fotograma; cuando se ven pocos puntos se comprueba si la homografía
  anterior sigue valiendo y, si la cámara se ha movido, el fotograma queda sin calibrar antes que proyectar mal. El
  seguimiento es BoT-SORT con compensación del movimiento de cámara y reidentificación por apariencia.
- **`--camara fija`**: trípode alto o cámara tipo Veo. Se acumulan los puntos del campo de fotogramas repartidos por
  todo el tramo, se toma la mediana de cada uno y se ajusta una sola homografía robusta para todo el partido; el
  seguimiento es ByteTrack.

En los dos casos, quien queda lejos de las líneas del campo (gradas, banquillo) no cuenta, y un «balón» en la grada,
de tamaño imposible o que salta de un lado a otro entre fotogramas se descarta. Sin campo calibrado, la posesión se
mide en la imagen tomando la altura de cada jugador como 1,80 m; los tercios, las distancias y los eventos de
geometría (fuera, tiros, goles) solo salen con el campo calibrado.

Con `--llm claude` la clave se toma de `ANTHROPIC_API_KEY` (o del perfil de `ant auth login`); se mandan solo
fotogramas sueltos y recortes, nunca el vídeo. El modelo por defecto es `claude-opus-5-5` y la petición lleva el
respaldo del servidor ante un rechazo de seguridad (`fallbacks: "default"`). Con `--llm ollama` se habla con
`http://localhost:11434` (`OLLAMA_HOST` para otro) y el vídeo no sale del equipo.

### Salidas

| Archivo | Qué es |
|---|---|
| `resumen.md` | El informe para el cuerpo técnico: posesión, datos de equipo propuestos, eventos, jugadores seguidos, calidad del análisis y, si hay modelo de lenguaje, su resumen |
| `analisis.json` | Todo: vídeo y parámetros, equipos y colores, dirección de ataque por mitad, descanso, calibración, recuentos, posesión, eventos (con confianza, clip y veredicto del modelo), tracklets (acciones, distancia, mapa de calor, recorte, dorsal) y tiempos |
| `acta-propuesta.json` | Lo que pide la API del acta interna: `teamStats` desde los dos lados (posesión, tiros, córners, saques, goles) y una línea por tracklet con la propuesta por jugador; la app decide cuál es «nuestro» equipo y qué tracklet es cada ficha |
| `mapas/` | Mapas de calor PNG por equipo y por tracklet |
| `recortes/` | El mejor recorte de cada tracklet, para asignarlo a una ficha |
| `clips/` | Clip de 10 s de cada tiro, gol, córner y parada |
| `posiciones.csv.gz` | Posición de cada persona y del balón en cada fotograma analizado (campo en cm e imagen en px) |
| `anotado.mp4` | Con `--video-anotado`: cajas por equipo, número de tracklet, balón y radar del campo |

## Cómo funciona

1. **Equipos**: una pasada rápida por el tramo recoge recortes de jugadores y separa dos grupos **por el color de la
   camiseta** (`colores.py`). De cada torso (el centro del recorte, sin el césped que asoma) se resume el color en Lab:
   claridad media y su dispersión, tono medio (a, b), saturación y qué parte de los píxeles es oscura o clara. La
   claridad se mide **respecto al césped del propio recorte**, que dice cuánta luz hay donde está el jugador: así el
   mismo jugador bajo un foco y en la sombra cae en el mismo grupo. Los rasgos se normalizan, KMeans da un primer
   reparto y con él se calcula la dirección que mejor separa los dos grupos (discriminante de Fisher, afinado un par de
   veces): de día manda el tono (azul frente a amarillo), de noche, con los focos apagando los colores, la claridad
   (marino frente a rayas rojiblancas). Cada recorte recibe una puntuación (±1 en el centro de cada grupo, cerca de 0
   si no está claro) y el equipo de cada tracklet se vota fotograma a fotograma con esa puntuación como peso, así que
   los recortes borrosos o a medio tapar no deciden. Y como el seguidor a veces junta a dos jugadores en un mismo
   número (se cruzan y sale el otro), cuando la camiseta de un tracklet cambia de equipo y se queda así dos segundos
   (`seguimiento.py`: diez muestras seguidas y claras del otro equipo; una tapada dura menos) el tracklet **se parte**
   y lo que viene después es otro (numerados desde 100000; `recuento.tracklets_partidos` dice cuántos). El recorte que
   se enseña de cada tracklet es siempre de su equipo, no del jugador contrario con el que se confundió un momento.
   Hasta el 09-11-2026 se usaba
   el clasificador de apariencia de `sports` (SigLIP → UMAP → KMeans), que con el vídeo nocturno del Vallobín repartía
   6 y 21 jugadores.
   Los porteros van con el equipo cuyo grueso tienen más cerca; el reparto sale en `recuento.reparto_equipos` y el
   analizador avisa si un grupo se queda con menos de la quinta parte de los recortes.
2. **Pasada principal** (5 fotogramas/s): detección y seguimiento de personas con Ultralytics en modo `track`
   (BoT-SORT o ByteTrack según la cámara), detección del balón (fotograma entero y, si se pierde, por teselas; con
   filtros de tamaño y de salto), calibración del campo (`campo.py`: puntos clave de las líneas → homografía con
   RANSAC y error de reproyección; por fotograma o única), filtro de personas fuera del campo, proyección de
   posiciones y la máquina de posesión (`posesion.py`).
3. **Post-proceso** sobre las posiciones guardadas (`estadisticas.py`, `eventos.py`): parones y descanso, dirección de
   ataque por mitad (por dónde están los porteros), eventos, posesión, secuencias y acciones por jugador.
4. **Modelo de lenguaje** (opcional, `llm/`): confirma eventos, lee dorsales, redacta.
5. **Informes** (`informes.py`).

La lógica de posesión, eventos, estadísticas y geometría es pura y se prueba sin GPU: `uv run pytest`.

## Integración con la app

El servicio de vídeo (`TercerTiempo.Videos`) lanza este programa cuando un vídeo termina de comprimirse (o cuando el
cuerpo técnico pide analizarlo desde el acta interna), de uno en uno, desde esta carpeta:

```
uv run tt-analisis analizar <vídeo> --salida <Videos:Directory>/analysis/<vídeo> --equipos "<nuestro>,<rival>"
       [--campo LxA] --sin-clips --progreso --camara movil --exigir-gpu [--segmento 20:00-25:00]
```

- Lee el avance de las líneas `##progreso N` (0-8 separar equipos, 8-95 la pasada, 95-100 el post-proceso) y, al
  terminar, carga `acta-propuesta.json` (la propuesta), `analisis.json` (versión, pesos, fotogramas, % con balón y %
  calibrados, tramo y medidas del campo) y el recorte de cada jugador de la propuesta. Todo eso queda en la base
  (`MatchVideoAnalysis`); la carpeta de salida se conserva hasta que se elimina el vídeo.
- `--campo` lleva las medidas que el club del equipo **local** apunta en la ficha de su equipo; sin ellas se calibra
  con 120 × 70 y la app lo avisa. Los nombres de `--equipos` son solo para los informes: el analizador no sabe cuál de
  las dos equipaciones es la del club, lo dice el cuerpo técnico en la app.
- Con `--exigir-gpu`, sin CUDA sale con el código 3 y la app deja el análisis como fallido con el motivo.
- Configuración en `Analysis:*` del servicio de vídeo (`UvPath`, `Directory` = esta carpeta, `Segment` para probar con
  un tramo, `Camera`): ver el [README del backend](../../README.md#análisis-del-vídeo-la-propuesta-de-acta-interna).
  En Docker no corre (la imagen del servicio de vídeo no lleva Python ni GPU): `Analysis:Enabled` es false allí.

Probado el 06-11-2026 en FakeDatos con el vídeo del Vallobín: el tramo 20:00-25:00 pedido desde el acta interna (5 min
de cálculo, 27 jugadores seguidos, balón en el 19,3 % de los fotogramas y campo calibrado en el 66,9 %) y un clip de
100 s subido por trozos, que se comprimió y se analizó solo con el campo de 100 × 62 apuntado por el club (2 min).

## Docker (GPU)

```powershell
docker build -t tercer-tiempo-analisis .
docker run --rm --gpus all -v ${PWD}\modelos:/app/modelos -v ${PWD}\salidas:/app/salidas -v C:\videos:/videos tercer-tiempo-analisis analizar /videos/partido.mp4 --salida /app/salidas/partido
```

En Windows, Docker Desktop usa WSL2 y la máquina virtual tiene 6 GB en `.wslconfig`: para analizar en Docker hay que
subirla (8-12 GB) o correr el analizador fuera de Docker, como en las pruebas.

## Licencias

`sports`, `supervision` y WASB son MIT/Apache; **Ultralytics (YOLO) es AGPL-3.0**: para un servicio cerrado hace falta
su licencia comercial o cambiar los detectores a RF-DETR / RT-DETR (Apache 2.0). Hay que decidirlo antes de publicar.

## Estado de las pruebas (05-11-2026)

Cinco minutos reales del vídeo del Vallobín (1080p a 50 fps, de noche, **cámara con operador**: paneo y zoom), en la
RTX 5070 Ti, 5 fotogramas/s: **≈ 5 min de cálculo por cada 5 min de vídeo**, 9 GB de VRAM.

| Qué | Resultado | Lectura |
|---|---|---|
| Jugadores y árbitro | Se detectan y se siguen; la gente de la grada y del banquillo queda fuera | Bien. Los tracklets se cortan a menudo (23 de más de 30 s en 5 min): falta reidentificación entre cortes |
| Campo calibrado | 46 % de los fotogramas (el resto se descarta a propósito cuando los puntos no cuadran) | El detector de líneas es de televisión; de noche y con zoom ve pocos puntos |
| Balón | A la vista en el 28 % de los fotogramas | **El cuello de botella**: el detector público falla con los focos. Hay que afinarlo con partidos propios |
| Posesión | 59/41 en una pasada y 33/67 en otra sobre el mismo tramo | Con tan poco balón, todavía no es fiable: la mejora viene del balón, no de la posesión |
| Eventos | 6-8 en 5 min (saques de puerta, de banda, un córner), ningún falso gol | Plausibles; con el balón bien seguido saldrán tiros y goles |
| Velocidades | Puntas de 20-28 km/h, distancias de 100-180 m por tracklet en 1-1,5 min | Plausibles |

Con una cámara fija y alta (`--camara fija`) todo lo que depende de la calibración mejora de golpe: una sola
homografía para todo el partido.

## Afinado con el vídeo del Vallobín (06-11-2026)

Lo que se vio al etiquetar automáticamente el partido entero (96 min, una muestra cada 2 s):

- **El campo no mide 105 × 68** y el detector público de puntos del campo, de noche y con este encuadre, solo acierta
  unos 5 puntos por fotograma (los del círculo central y la línea de medio campo); el resto los sitúa a 20-40 px de
  donde van. Con eso la medida automática del campo no converge (`medir-campo` lo dice: error medio > 12 px), así que
  el conjunto `campo/` lleva solo los puntos vistos y coherentes. Con las medidas reales del campo (`--campo`) saldrían
  etiquetas completas por reproyección.
- **El balón**: el detector público a baja confianza marca sobre todo marcas de las líneas, medias y botas; la regla
  de movimiento las deja fuera (en las pruebas sobre tramos, 5-7 de cada 6-10 balones aprobados eran balones de
  verdad, y lo rechazado eran marcas, focos y medias). A cambio solo se confirma el 5-10 % de las muestras: por eso
  hay dos rondas, y en la segunda etiqueta el balón ya afinado.

Etiquetado del partido entero (2.891 muestras, 28 min): 684 balones confirmados y 288 negativos difíciles, 1.838
fotogramas de personas y 1.260 de campo; de validación quedan 179, 336 y 248. Entrenamientos en la RTX 5070 Ti con el
tronco congelado: balón 10 épocas en 8 min (lote 4 a 1280 px, 5 GB de GPU), campo 40 épocas en 10 min, jugadores 12
épocas en 15 min. Resultados sobre la validación (fotogramas que ningún entrenamiento ha visto):

| Detector | Públicos | Afinados | Decisión |
|---|---|---|---|
| **Balón** (134 balones; a fotograma entero, umbral 0,25) | 97 encontrados (72 %), 45 falsos; mAP50 0,755 | **103 encontrados (77 %), 19 falsos; mAP50 0,825** | **Se usa el afinado** (`modelos/afinados/balon.pt`) |
| Jugadores (5.033 jugadores, 75 porteros, 675 árbitros) | jugador 0,99 / 0,94 (exhaustividad / precisión) | jugador 0,96 / 0,90; mAP50 0,81 → 0,86 | Se queda el público: las etiquetas salen de él (evaluación circular) y de noche ya acierta casi todo |
| Campo (248 fotogramas) | a 640 px: 78 % calibrables, 5,4 puntos correctos y 8,8 falsos por fotograma, error de homografía 13 px | a 640 px: 44 % calibrables, 1,2 correctos y 2,0 falsos, error 32 px | Se queda el público: con solo los puntos vistos aprende a callarse los demás |

Dos cosas más que salieron de la evaluación:

- Una segunda ronda de autoentrenamiento (el balón afinado vuelve a etiquetar, 603 balones) **empeora** (90 encontrados):
  el maestro afinado propone menos candidatos pequeños y dudosos, y el conjunto pierde justo los difíciles. Se queda la
  primera ronda.
- **El detector público del campo se entrenó a 640 px y a 1280 se inventa puntos**: a 1280 calibra el 46 % de los
  fotogramas con un error mediano de 351 px (basura); a 640, el 78 % con 13 px. El analizador lo usaba a 1280: desde
  este día la calibración va a 640 (`--imgsz-campo`), que es la mejora grande y gratis de esta tanda.

Los pesos afinados descartados (campo y jugadores) quedan en `entrenamientos/descartados/` por si se quieren comparar.

El mismo tramo de 5 min (20:00-25:00) analizado de nuevo, 5 fotogramas/s, cámara móvil:

| Qué | 05-11 (públicos, campo a 1280) | Públicos, campo a 640 | Balón afinado, campo a 640 |
|---|---|---|---|
| Fotogramas calibrados | 46 % | **67 %** (9 puntos en el mejor, 128 invalidaciones frente a 207) | 67 % |
| Fotogramas con balón | 28 % | 34 % | 19 % |
| Balones descartados por tamaño o salto | 71 | 71 | **14** |
| Toques / eventos | 51 / 6 | 37 / 12 (2 «goles» y 2 tiros que no se han verificado) | 27 / 4 |
| Posesión | 33 / 67 | 75 / 25 | 20 / 80 |
| Tiempo | 4,7 min | 4,6 min | 4,4 min |

Lectura honesta: la calibración mejora de golpe con el detector del campo a su escala. El balón afinado quita falsos
balones (en la validación, de 45 a 19) y por eso el analizador «ve» el balón en menos fotogramas (19 %): parte de lo
que antes contaba como balón eran marcas y medias. Pero sigue sin verlo en cuatro de cada cinco fotogramas, y con eso
la posesión todavía baila de una pasada a otra: **el balón sigue siendo el cuello de botella** y el siguiente salto
pide etiquetas de más partidos (o corregidas a mano) y, sobre todo, una cámara fija y más cerca del juego.

## Siguientes pasos

- Medidas reales del campo del Vallobín (o de cada campo) para etiquetar los 32 puntos por reproyección y afinar de
  verdad el detector del campo; a medio plazo, calibrar por las líneas (segmentación) y no por puntos.
- Revisar y corregir a mano una parte de las pseudoetiquetas (CVAT o Label Studio) y volver a entrenar; con la clave
  de la API, `--juez claude` sobre los candidatos a balón.
- En la sección «Del vídeo» de la app: los eventos uno a uno con su clip (hoy se lanza con `--sin-clips`), el minimapa
  y la posesión por tramos; y separar mejor los dos equipos de noche (en las pruebas quedaron 6 y 21 jugadores seguidos).
- Unir tracklets de la misma persona (re-identificación) y leer dorsales con un modelo específico.
- Condiciones de grabación en el manual y aviso en la subida cuando el encuadre no sirve.
