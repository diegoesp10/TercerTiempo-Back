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
| Posesión por equipo, mitad, tercio y minuto | Jugador más cercano al balón con histéresis; el balón en vuelo es de quien lo lanzó. Solo se reparte el tiempo con balón visto o interpolado, y sale con su **confianza** (alta, media o baja) | Alta si el balón se ve; con confianza baja la app no la vuelca |
| Mapas de calor, distancia, velocidad máxima | Posiciones proyectadas al campo (homografía por los puntos de las líneas) | Alta si la calibración es buena |
| Saques de banda, córners, saques de puerta | Balón fuera + quién lo tocó + hacia dónde ataca cada equipo. Si vuelve a salir por la misma línea sin haber vuelto al juego (2 s dentro y a más de 10 m de donde salió), es el mismo parón: lo recogen o la calibración tiembla en la línea | Media-alta |
| Tiros / a puerta | Balón rápido hacia la portería desde menos de 35 m; corte con la línea de gol. Un toque es un solo tiro: el balón que acaba entrando es el mismo disparo que ya se vio | Media (candidatos: revisar el clip) |
| Goles | Balón que cruza entre los postes; se confirma si sigue un saque de centro. Con el acta oficial cerrada, **como mucho los del acta**: con el minuto de cada gol se queda el candidato más cercano (a menos de 5 min si lo confirmó el saque de centro, de 2 min si no; sin minutos, solo los confirmados) y los demás se quitan (balones por encima del larguero o recogidos detrás de la portería). Del tiro de un gol quitado, el disparo que se vio queda como tiro fuera y el que solo se dedujo del cruce se quita | Media-alta con confirmación; con el acta, alta |
| Faltas y penaltis | El juego se para con el balón en el campo (el balón quieto lejos de las líneas o el frenazo de todos a la vez) sin un fuera ni un gol que lo explique: **la recibe el que tenía el balón y la comete el rival más cercano**, que tiene que estar a menos de 5 m | Baja en cobertura (el balón quieto se ve poco), alta en lo que sale |
| Fueras de juego | Parada en la que saca el otro equipo desde donde estaba un atacante por delante del penúltimo defensor en el último pase hacia delante de su equipo | Baja en cobertura |
| Minutos de cada jugador | **Por descarte, del acta oficial cerrada**: sin cambio, el partido entero aunque la cámara no lo vea todo el rato; con cambio, desde o hasta ese minuto | La del acta |
| Paradas | Tiro a puerta seguido del portero con el balón | Media |
| Pases, pérdidas, recuperaciones por tracklet | Cadenas de toques del mismo equipo / cambios de equipo sin balón fuera | Media |
| Quién es quién | **Dorsal leído en la espalda** (PARSeq) y sumado en todos los fotogramas de cada jugador; con el acta oficial (`--plantillas`), solo los dorsales de los que jugaron y en sus minutos | Alta en el número leído; el tiempo sin identificar (de frente, lejos) cuenta para el equipo y no para nadie en concreto |
| De qué equipo es cada uno | Color de la camiseta (resumen Lab del torso, sin el césped, con la claridad medida respecto al césped del recorte; dos grupos por KMeans) | Alta si las dos equipaciones se distinguen, también de noche |

Las tarjetas no se deducen del vídeo: vienen del acta oficial, como el resultado y los minutos.

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

`descargar-modelos` baja el lector de dorsales y dos clips de muestra. Los tres detectores (balón, jugadores y puntos
del campo) no se descargan: son **RF-DETR** (Apache 2.0) entrenados con nuestros partidos y compilados a TensorRT para
la GPU de cada equipo (`modelos/rfdetr/`, ver «El balón con RF-DETR y TensorRT», «Los jugadores con RF-DETR» y «El
campo con RF-DETR Keypoint»). Desde el 18-11-2026 no queda nada de Ultralytics (AGPL-3.0).

ffmpeg hace falta solo para los clips de los eventos (en Windows se busca también el de winget).

## Uso

```powershell
uv run tt-analisis analizar partido.mp4 --salida salidas\partido --equipos "Vallobín,Pumarín"
uv run tt-analisis analizar partido.mp4 --segmento 20:00-25:00 --video-anotado      # un tramo, con el vídeo anotado
uv run tt-analisis analizar partido.mp4 --descanso 52:30 --llm claude                # confirma eventos y redacta con la API de Claude
uv run tt-analisis analizar partido.mp4 --llm ollama --modelo-llm gemma4:12b         # lo mismo con un modelo local
uv run tt-analisis analizar partido.mp4 --fps 3 --sin-clips                          # más rápido, menos fino
uv run tt-analisis comparar salidas\a salidas\b salidas\c                              # varias pasadas del mismo tramo, lado a lado
```

Opciones: `--camara movil|fija` (ver abajo), `--fps` (fotogramas analizados por segundo, 5 por defecto),
`--segmento inicio-fin`, `--descanso minuto` (si no, se detecta por el parón más largo del medio), `--confianza`
(0.3: puntuación mínima de una persona; las flojas solo continúan a alguien ya seguido), `--seguidor botsort|bytetrack`
(por defecto el que toca a la cámara),
`--campo 100x62` (medidas reales del campo en metros; por defecto 120 × 70), `--sin-afinados`
(usar el lector de dorsales publicado aunque haya uno afinado), `--muestras-equipos` (600), `--max-fotogramas N` (para probar),
`--device cpu`, `--plantillas acta.json` (los que jugaron según el acta oficial, con dorsal y minutos: ver «Quién es
quién»), `--saque 1:30` (segundo del vídeo en que empieza el partido, para pasar los minutos del acta al vídeo; 0 por
defecto), `--sin-dorsales` (no leer dorsales: un jugador por tracklet, como antes), `--progreso` (el avance como líneas `##progreso N` en la salida estándar, sin barras: para quien lo
lanza como proceso), `--exigir-gpu` (sin GPU CUDA termina con el código de salida 3 en vez de analizar en CPU) y
`--semilla N` (para medir la estabilidad: fija los generadores y muestrea otros fotogramas del mismo tramo,
desplazados dentro del paso). `comparar` pone varias pasadas lado a lado (balón, calibración, toques, posesión y su
confianza, emparejando los equipos por el color de la camiseta) y dice cuántos puntos se mueve la posesión, total y
por minuto, respecto a la primera.

### Afinar los detectores con partidos propios

Los detectores públicos están entrenados con televisión. Para que aprendan nuestros campos (focos, césped, el
encuadre de la cámara) se entrenan con imágenes de partidos propios, **etiquetadas automáticamente**:

```powershell
uv run tt-analisis medir-campo partido.mp4                       # intenta medir el largo y el ancho del campo
uv run tt-analisis etiquetar partido.mp4 --salida datasets\partido [--campo 100x62]
uv run tt-analisis preparar-balon datasets\partido [datasets\otro-partido...]   # teselas del balón (datasets\balon-teselas)
uv run tt-analisis entrenar-balon                                # RF-DETR Medium → modelos\rfdetr\balon.pth
uv run tt-analisis exportar-balon                                # TensorRT para esta GPU → modelos\rfdetr\balon.trt
uv run tt-analisis evaluar-balon datasets\partido                # fotogramas enteros de validación (--motor pytorch para comparar)
uv run tt-analisis preparar-jugadores datasets\partido [datasets\otro-partido...]   # personas (datasets\jugadores)
uv run tt-analisis entrenar-jugadores                            # RF-DETR Medium a 1280 → modelos\rfdetr\jugadores.pth
uv run tt-analisis exportar-jugadores                            # TensorRT para esta GPU → modelos\rfdetr\jugadores.trt
uv run tt-analisis evaluar-jugadores datasets\jugadores          # contra las etiquetas de validación
uv run tt-analisis preparar-campo partido.mp4 [otro.mp4...]       # 32 puntos por destilación (datasets\campo)
uv run tt-analisis entrenar-campo                                # RF-DETR Keypoint a 768 → modelos\rfdetr\campo.pth
uv run tt-analisis exportar-campo                                # TensorRT para esta GPU → modelos\rfdetr\campo.trt
uv run tt-analisis evaluar-campo datasets\partido [datasets\campo]   # calibrables, puntos y error de la homografía
```

`etiquetar` toma un fotograma cada 2 s (`--cada`) y escribe un dataset YOLO con tres conjuntos (`balon/`,
`personas/`, `campo/`, cada uno con su `data.yaml`), partido en entrenamiento y validación **por bloques de tiempo**
(un bloque de 2 min de cada cinco), más `revision/` con vistas previas y `muestras.jsonl` con el porqué de cada
decisión. Cómo se confirma cada cosa:

- **Balón**: el detector RF-DETR busca por teselas en el fotograma y en seis vecinos (±0,1, ±0,2 y ±0,4 s; el radio
  en el que se busca crece con el desfase, para que un balón rápido no se salga de él). Los umbrales de la regla
  (`CONF_BALON_*`, `CONF_QUIETO_*`) se ajustaron con las puntuaciones del YOLO: hay que revisarlos en la próxima tanda.
  Lo que separa un balón de una marca de la línea, un reflejo o un foco es que **se mueve respecto al césped**: se
  estima el movimiento de la cámara hacia cada vecino (flujo óptico sobre el fondo) y se mira si el candidato sigue en
  el mismo sitio del campo (quieto: marca, salvo que el detector lo vea con mucha confianza, que es un balón parado) o
  se ha desplazado con una velocidad coherente entre vecinos (balón en juego). Lo que cae sobre una persona (medias,
  botas) solo valía si el detector de jugadores YOLO, entrenado con otros datos, también veía ahí el balón: el RF-DETR
  de jugadores no tiene clase balón, así que **desde el 17-11-2026 no se confirma ningún balón sobre una persona**
  hasta rehacer esa regla con RF-DETR (en la tercera ronda del balón). Los fotogramas con dos balones igual de buenos
  se descartan, y las marcas rechazadas entran como negativos difíciles.
- **Personas**: las del detector de jugadores con confianza ≥ 0,45 (umbral del YOLO, por revisar con RF-DETR); el
  fotograma entra solo si el balón está resuelto (para no enseñar que «ahí no hay balón» cuando sí lo hay).
- **Campo**: con seis o más puntos coherentes se ajusta la homografía; si se conocen las medidas reales del campo
  (`--campo`) se reproyectan los 32 vértices y quedan etiquetados también los que el detector no vio; si no, solo los
  puntos vistos. Este `campo/` sirve para **medir** el detector (`evaluar-campo`); para entrenarlo no, porque el RF-DETR
  de puntos aprende también que un punto sin etiqueta «no está» y con solo los coherentes se calla: se entrena con
  `preparar-campo` (ver «El campo con RF-DETR Keypoint»).

`--juez claude` manda además un recorte ampliado de cada candidato a balón a la API de Claude (hace falta
`ANTHROPIC_API_KEY`) y se queda con lo que confirma; `--juez ollama` hace lo mismo con un modelo local, pero
`gemma4:12b` tarda 12-19 s por recorte y da por balón casi todo, así que no se usa por defecto.

Las pseudoetiquetas se pueden **corregir a mano** sin salir del formato YOLO:

```powershell
uv run tt-analisis exportar-revision balon --dataset datasets\partido --particion val --max 200   # zip YOLO 1.1
uv run tt-analisis importar-revision balon datasets\partido\revision-balon-val-corregido.zip --dataset datasets\partido
```

El zip lleva las imágenes, sus etiquetas, `obj.names`, `obj.data` y `train.txt`, tal cual lo importa CVAT (crear la
tarea con las imágenes del zip y subir el mismo zip en «Upload annotations» con el formato YOLO 1.1; Label Studio
lo lee con su conversor). Al terminar se exporta en YOLO 1.1 e `importar-revision` vuelve a escribir solo las
etiquetas de las imágenes del zip (una imagen sin etiqueta en el zip queda sin balón), y `preparar-balon` y
`entrenar` las usan.

`preparar-campo` etiqueta un fotograma por segundo (cuatro en los clips cortos) con el detector del campo que haya como
maestro: cada punto que da con confianza ≥ 0,5 queda etiquetado y el resto sin etiqueta; el reparto entre
entrenamiento y validación es el de `etiquetar`. `evaluar-campo` mide lo que importa al analizador sobre la validación
de uno o varios datasets: fotogramas calibrables, puntos correctos y falsos por fotograma y error de la homografía.

### La cámara

- **`--camara movil`** (por defecto): alguien graba con paneo y zoom (como el vídeo del Vallobín: un operador detrás
  de la cámara). El campo se calibra en cada fotograma; cuando se ven pocos puntos se comprueba si la homografía
  anterior sigue valiendo y, si la cámara se ha movido, el fotograma queda sin calibrar antes que proyectar mal. El
  seguimiento es BoT-SORT con compensación del movimiento de cámara (flujo óptico), el de `trackers` (`rastreador.py`).
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
| `analisis.json` | Todo: vídeo y parámetros, equipos y colores, dirección de ataque por mitad, descanso, calibración, recuentos (`pct_con_balon` es el balón visto, lo que guarda la app; `pct_con_balon_total` y `balon_relleno`, con los huecos rellenados), posesión (con `confianza` y `cobertura`), eventos (con confianza, clip y veredicto del modelo), tracklets (acciones, distancia, mapa de calor, recorte, dorsal) y tiempos |
| `acta-propuesta.json` | Lo que pide la API del acta interna: `teamStats` desde los dos lados (posesión con `possessionConfidence` y `possessionBallPct`, tiros, córners, saques, goles), una línea por tracklet con la propuesta por jugador y `calidad`; la app decide cuál es «nuestro» equipo y qué tracklet es cada ficha |
| `mapas/` | Mapas de calor PNG por equipo y por tracklet |
| `recortes/` | El mejor recorte de cada tracklet, para asignarlo a una ficha |
| `clips/` | Clip de 10 s de cada tiro, gol, córner y parada |
| `posiciones.csv.gz` | Posición de cada persona y del balón en cada fotograma analizado (campo en cm e imagen en px) |
| `anotado.mp4` | Con `--video-anotado`: cajas por equipo, número de tracklet, balón y radar del campo |

## Cómo funciona

1. **Equipos**: unos 50 fotogramas repartidos por el tramo dan los recortes de jugadores con los que se separan dos
   grupos **por el color de la camiseta** (`colores.py`). Si las muestras quedan a más de 20 s unas de otras (un
   partido entero), se salta a cada una en vez de descomprimir el tramo seguido: desde el 18-11-2026 son unos 3 s en
   vez de 5-6 min de CPU con la GPU parada. De cada torso (el centro del recorte, sin el césped que asoma) se resume el color en Lab:
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
2. **Pasada principal** (5 fotogramas/s): detección de personas con el RF-DETR de jugadores en el fotograma entero a
   1280 px (`detector_jugadores.py`) y seguimiento con BoT-SORT o ByteTrack según la cámara (`rastreador.py`, paquete
   `trackers`), detección del balón (`deteccion.py` y `detector_balon.py`: RF-DETR en
   todo el fotograma por 24 teselas de 384 px a resolución nativa, en un lote con TensorRT; filtros de tamaño y de
   salto; entre varios candidatos gana el más coherente con la trayectoria
   reciente y con dónde están los jugadores, y un candidato flojo solo vale cerca de un jugador o de donde se
   esperaba el balón), calibración del campo (`detector_campo.py` y `campo.py`: los 32 puntos clave de las líneas con
   el RF-DETR de puntos a 768 px → homografía con RANSAC y error de reproyección; por fotograma o única), filtro de personas fuera del campo y proyección de posiciones.
3. **Trayectoria del balón** (`balon.py`): primero se quitan los **objetos fijos** que el detector toma por el balón
   (un cono, una bolsa o una botella junto a la línea): sitios donde el «balón» se queda quieto al menos cuatro veces
   a lo largo de diez minutos o más, fuera de los puntos de saque; dentro de su radio solo vale con un jugador a menos
   de 2 m. Luego los avistamientos sueltos (flojos, lejos de todos los jugadores y
   sin otro a menos de 1,5 s) y se rellenan por interpolación los huecos de hasta 2 s entre dos avistamientos con
   una velocidad plausible (en el campo si los dos extremos están proyectados, y siempre en la imagen). El balón de
   cada instante queda marcado como «visto» o «interpolado».
4. **Posesión** (`posesion.py`): la máquina de «jugador más cercano al balón con histéresis» (un cambio de poseedor se
   confirma tras 0,2 s seguidos del mismo candidato, medidos en tiempo y no en muestras, para que la cadencia no la
   cambie) corre sobre esa
   trayectoria, y el reparto cuenta **solo el tiempo de juego activo con balón visto o interpolado**; tras más de 2 s
   sin balón ya no se sabe de quién es hasta el siguiente toque. Sale con su confianza: **alta** si hay balón en el
   60 % del tramo y se reparte al menos la mitad, **media** a partir del 35 % y la cuarta parte, **baja** si no (el
   35 % es el mismo umbral con el que la app avisa de calidad baja). Con confianza baja la posesión se enseña pero
   la app no la vuelca al acta.
5. **Jugadas** (`jugadas.py`, que encadena lo demás sobre las posiciones guardadas): toques de balón; eventos
   (`eventos.py`: fueras, saques, córners, tiros, goles, parones); el **descanso** y la dirección de ataque de cada
   equipo en cada parte (`mitades.py`: el parón más largo del medio del tramo o, si no lo hay, **el cambio de campo**,
   el minuto desde el que la x media de cada equipo está al otro lado; la dirección, por la x media del equipo y, si no,
   por sus porteros); las **interrupciones** (`interrupciones.py`): paradas con el balón en el campo que no explica un
   fuera ni un gol, leídas como en el campo (el que tenía el balón recibe la falta; el rival más cercano, a menos de
   5 m, la comete; si no había ninguno, no fue falta), penaltis (balón en el punto de penalti) y fueras de juego (saca
   el otro equipo y en el último pase hacia delante había un atacante por delante del penúltimo defensor); lo que dice
   el **acta oficial** (`oficial.py`: el resultado manda sobre los goles y los minutos salen por descarte, solo con el
   acta cerrada); y la posesión, las secuencias y las acciones por jugador.
6. **Quién es quién** (`dorsales.py`, `identidades.py`): en la pasada, de cada jugador de más de 60 px de alto se
   recorta la zona del dorsal (la espalda, del 16 al 42 % del alto y el 60 % central del ancho), se estira a 32 × 128 y
   PARSeq (lector de texto de escenas, Apache-2.0, `terceros/parseq`) da la probabilidad de cada número del 1 al 99: la
   primera cifra y lo que va detrás de cada una (otra cifra o el fin), en lotes en la GPU. De frente no hay número y esa
   lectura apenas pesa (`1 − P(sin número)`). Cada lectura se suma al tracklet **solo para el equipo cuya equipación se
   vio en ese fotograma**: si el seguidor confunde un momento al jugador con uno del otro equipo, el dorsal del otro no
   cuenta. Luego, sobre los tracklets ya cerrados: (1) **cadenas** de tracklets del mismo equipo que empiezan justo donde
   y cuando acaba otro (≤ 1 s, velocidad posible), nunca dos que dicen dorsales distintos; (2) **el dorsal de cada
   cadena** por la suma de sus lecturas, con el acta solo entre los que jugaron y en sus minutos (± 5 min por el desfase
   de relojes); el lado del acta (local o visitante) de cada equipo del vídeo sale de en qué reparto caen más lecturas;
   (3) **un dorsal, una persona a la vez**: si dos cadenas lo dicen a la vez, la más débil prueba con su segundo número;
   (4) **continuidad** (apagada: medida con el vídeo del Vallobín fallaba más que acertaba): una cadena sin número que
   encaja en el hueco de un solo jugador sería ese jugador. Lo que no tiene número leído queda sin identificar. Como mucho 16
   por equipo (once y los cambios; con el acta, los que jugaron). Desde ahí cada jugador identificado tiene un único
   identificador (`900000 + equipo·1000 + dorsal`) y la posesión, los pases y las acciones salen por jugador.
7. **Modelo de lenguaje** (opcional, `llm/`): confirma eventos, lee dorsales, redacta.
8. **Informes** (`informes.py`). `acta-propuesta.json` es la versión 2 cuando hay identificación: una línea por
   jugador identificado con `dorsal`, `nombreActa`, `confianzaDorsal` (parte de las lecturas que dicen ese número) y
   `tramos`, el `lado` de cada equipo y `calidad.sinIdentificarS`. Cada equipo trae `foulsCommitted`/`foulsReceived` y
   `offsidesFor`/`offsidesAgainst`; cada jugador, `fouls`, `foulsReceived` y `offsides`, y con el acta cerrada
   `minutos` (por descarte) y sus goles del acta. `actaOficial` resume el acta que se usó (resultado, si está cerrada y
   los minutos y goles de cada uno de los dos lados).

La lógica de posesión, eventos, estadísticas y geometría es pura y se prueba sin GPU: `uv run pytest`.

## Integración con la app

El servicio de vídeo (`TercerTiempo.Videos`) lanza este programa cuando un vídeo termina de comprimirse (o cuando el
cuerpo técnico pide analizarlo desde el acta interna), de uno en uno, desde esta carpeta:

```
uv run tt-analisis analizar <vídeo> --salida <Videos:Directory>/analysis/<vídeo> --equipos "<nuestro>,<rival>"
       [--plantillas <salida>/plantillas.json] [--campo LxA] --sin-clips --progreso --camara movil --exigir-gpu [--segmento 20:00-25:00]
```

- Lee el avance de las líneas `##progreso N` (0-8 separar equipos, 8-95 la pasada, 95-100 el post-proceso) y, al
  terminar, carga `acta-propuesta.json` (la propuesta, con la confianza de la posesión: con «baja» la enseña pero no
  la vuelca), `analisis.json` (versión, pesos, fotogramas, % con balón visto y % calibrados, tramo y medidas del
  campo) y el recorte de cada jugador de la propuesta. Todo eso queda en la base
  (`MatchVideoAnalysis`); la carpeta de salida se conserva hasta que se elimina el vídeo.
- `--plantillas` lleva, si el partido tiene acta oficial (también provisional), los que jugaron por lado con su
  dorsal, si fueron titulares y sus minutos de entrada y salida, de qué parte es el vídeo y cuánto dura cada parte
  (`{"parte", "minutos_parte", "equipos": [{"lado", "nombre", "jugadores": [{"dorsal", "nombre", "titular", "entra",
  "sale"}]}]}`). Con eso la app sabe qué equipo del vídeo es la plantilla y propone la ficha de cada jugador: la que se
  cruza por nombre con la línea del acta de su lado que lleva ese dorsal. El cuerpo técnico solo corrige.
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

`sports`, `supervision`, `trackers`, WASB y RF-DETR son MIT/Apache. **Ultralytics (YOLO) es AGPL-3.0**: decidido el
17-11-2026 pasar todos los detectores a RF-DETR en vez de pagar su licencia comercial, y hecho el 18-11-2026: balón y
jugadores son RF-DETR Medium, el campo RF-DETR Keypoint y el seguidor el BoT-SORT/ByteTrack de `trackers` (Roboflow,
Apache 2.0). `ultralytics` ya no está en las dependencias, y los pesos YOLO públicos (`football-*.pt`) se quitaron de `modelos/`
el 18-11-2026.

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
| **Balón** (134 balones; a fotograma entero, umbral 0,25) | 97 encontrados (72 %), 45 falsos; mAP50 0,755 | **103 encontrados (77 %), 19 falsos; mAP50 0,825** | **Se usó el afinado** hasta el paso a RF-DETR (17-11-2026) |
| Jugadores (5.033 jugadores, 75 porteros, 675 árbitros) | jugador 0,99 / 0,94 (exhaustividad / precisión) | jugador 0,96 / 0,90; mAP50 0,81 → 0,86 | Se queda el público: las etiquetas salen de él (evaluación circular) y de noche ya acierta casi todo |
| Campo (248 fotogramas) | a 640 px: 78 % calibrables, 5,4 puntos correctos y 8,8 falsos por fotograma, error de homografía 13 px | a 640 px: 44 % calibrables, 1,2 correctos y 2,0 falsos, error 32 px | Se queda el público: con solo los puntos vistos aprende a callarse los demás |

Dos cosas más que salieron de la evaluación:

- Una segunda ronda de autoentrenamiento (el balón afinado vuelve a etiquetar, 603 balones) **empeora** (90 encontrados):
  el maestro afinado propone menos candidatos pequeños y dudosos, y el conjunto pierde justo los difíciles. Se queda la
  primera ronda.
- **El detector público del campo se entrenó a 640 px y a 1280 se inventa puntos**: a 1280 calibra el 46 % de los
  fotogramas con un error mediano de 351 px (basura); a 640, el 78 % con 13 px. El analizador lo usaba a 1280: desde
  este día la calibración va a 640 (`--imgsz-campo`), que es la mejora grande y gratis de esta tanda.

De los pesos afinados descartados (campo y jugadores, y el balón de la tanda 2 del 10-11) solo quedan sus métricas en
`entrenamientos/descartados/`: los pesos se borraron el 17-11-2026, al pasar el balón a RF-DETR.

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

## Trayectoria del balón y posesión con confianza (10-11-2026)

Lo que cambió en el analizador, medido sobre el mismo tramo de 5 min (20:00-25:00) del vídeo del Vallobín, con el balón
afinado del 06-11 y el campo a 640 px:

- **Más balón visto**: teselas en cada fotograma en que el balón está perdido (antes, las primeras ocho veces y luego una
  de cada cinco), con el umbral de las teselas en 0,2 y el prior de los jugadores para que los candidatos flojos solo
  valgan cerca de alguien o de donde se esperaba el balón. En la validación de la ronda 1 (134 balones), por teselas
  el afinado encuentra 112 (84 %) frente a 103 (77 %) a fotograma entero, a cambio de 51 falsos en vez de 19: el prior y
  la trayectoria son lo que deja fuera esos falsos en la pasada.
- **Trayectoria** (`balon.py`): se quitan los avistamientos sueltos (4 en el tramo) y se rellenan los huecos de hasta
  2 s con velocidad plausible (102 huecos, 351 instantes; 28 huecos más largos quedan sin rellenar).
- **Posesión solo con balón**: se reparte el tiempo con balón visto o interpolado (133 s de 300), nunca el resto, y
  tras 2 s sin balón nadie lo tiene hasta el siguiente toque. Sale con su **confianza**; la app no vuelca la de
  confianza baja. La confirmación del poseedor se mide en segundos, no en muestras.

| Qué | Balón afinado, campo a 640 (06-11) | Trayectoria (10-11) |
|---|---|---|
| Fotogramas con balón visto | 19,3 % | **27,3 %** |
| Con los huecos rellenados | — | **50,7 %** |
| Toques | 27 | 64 |
| Tiempo repartido | 291 s (todo el tramo, por «último que tocó») | 133 s (solo con balón) |
| Posesión | 20 / 80 | 48 / 52, confianza **media** |
| Tiempo (GPU compartida con un etiquetado) | 4,4 min | 8,6 min |

**Estabilidad**, lo que de verdad importaba: el mismo tramo con otra muestra de fotogramas (`--semilla 3`, los
fotogramas desplazados 0,06 s) y al doble de cadencia (`--fps 10`), emparejando los equipos por el color de la
camiseta (`tt-analisis comparar`):

| Pasada | Balón visto | Visto o interpolado | Toques | Posesión | Diferencia con la primera |
|---|---|---|---|---|---|
| 5 fotogramas/s | 27,3 % | 50,7 % | 64 | 48 / 52 | — |
| 5 fotogramas/s, `--semilla 3` | 26,7 % | 51,5 % | 64 | 52 / 48 | 4,2 puntos |
| 10 fotogramas/s | 26,2 % | 57,3 % | 80 | 52 / 48 | 4,4 puntos |

Antes, tres pasadas del 05 y el 06-11 sobre el mismo tramo daban 33/67, 75/25 y 20/80. Ahora las tres quedan a menos
de cinco puntos. La posesión **por minuto** sigue bailando (18-24 puntos de diferencia media entre pasadas): cada
minuto tiene 25-35 s repartidos y un cambio de poseedor en el borde mueve mucho; vale para ver tramos de dominio, no
como cifra. Los eventos candidatos también cambian de una pasada a otra (4, 9 y 12), como corresponde a candidatos.
Un partido entero reparte unas veinte veces más tiempo que este tramo, así que la posesión total será más estable que
en cinco minutos.

Lo que sigue igual de verdad: el detector ve el balón en uno de cada cuatro fotogramas (la cámara está lejos; en los
fotogramas sin balón ni a confianza 0,05 aparece nada) y la mitad del tramo se queda sin balón ni interpolado. La
confianza «media» dice justo eso.

### Segunda tanda de etiquetado: más muestras no dieron mejor balón (10-11-2026)

Para subir el detector se etiquetó el partido entero otra vez con **una muestra por segundo** (`--cada 1`) y los seis
vecinos: 5.781 muestras en 1 h 20 min, 1.422 balones confirmados (25 %) y 637 negativos difíciles, 2,1 veces la
ronda 1 (`datasets/vallobin-c1`: balón 1.676 de entrenamiento y 383 de validación; personas 2.992 y 696; campo 2.424 y
615). Reentrenado desde los pesos públicos como la ronda 1 (20 épocas, lote 4 a 1280 px, 27 min), evaluado sobre las
dos validaciones (la de la ronda 1 no la ha visto ningún entrenamiento; la nueva tampoco, pero sus etiquetas salen del
mismo maestro público, así que un «falso» puede ser un balón que la regla no confirmó):

| Detector | Ronda 1, 134 balones: entero 0,25 / teselas 0,2 | Tanda 2, 285 balones: entero 0,25 / teselas 0,2 | Tramo 20:00-25:00: balón visto / con relleno |
|---|---|---|---|
| Público | 97 (72 %), 45 falsos / 111 (83 %), 65 | 213 (75 %), 99 / — | 34 % (06-11) |
| **Ronda 1** (684 balones, 06-11) | **103 (77 %), 19 / 114 (85 %), 63** | 228 (80 %), 45 / 247 (87 %), 135 | **27,3 % / 50,7 %** |
| Tanda 2 (1.422 balones) | 98 (73 %), 11 / 109 (81 %), 48 | 223 (78 %), 29 / 240 (84 %), 96 | 21,3 % / 40,3 % |

El de la tanda 2 es más preciso (un tercio menos de falsos) pero más callado: encuentra un 3-4 % menos de balones en
las validaciones y, en el tramo, ve el balón en el 21 % de los fotogramas frente al 27 %, con menos tiempo repartido
(105 s frente a 133) y la posesión en 57/43. Como los falsos ya los filtran el prior de los jugadores y la trayectoria,
lo que cuenta para la posesión es ver más balón: **se quedó la ronda 1** y la tanda 2 se descartó (los dos pesos se
borraron el 17-11-2026, al pasar el balón a RF-DETR). Es la misma lección que la segunda ronda del 06-11: más
pseudoetiquetas del mismo partido y el mismo maestro no enseñan balones nuevos, solo hacen al detector más prudente.
El siguiente salto del balón pasa por etiquetas corregidas a mano (`exportar-revision` → CVAT) o por otros partidos.

## Lector de dorsales afinado (16-11-2026)

Cómo se hace:

```
uv run tt-analisis analizar partido.mp4 --salida salidas/p --plantillas acta.json --guardar-dorsales salidas/zonas-p
uv run tt-analisis entrenar-dorsales salidas/zonas-p [otras carpetas...]      # deja modelos/afinados/dorsales.pt
```

`--guardar-dorsales` deja las zonas del dorsal tal y como las ve el lector (estiradas a 32 × 128 con la misma
interpolación bicúbica) y, al final, la etiqueta de cada tracklet identificado: el dorsal que sale de todas sus
lecturas cruzadas con el acta. El afinado aprende de esas zonas (las que el lector leía bien y las que leía mal pero
veía claras) y de zonas sin número (de frente), con la respuesta vacía. Se valida con tracklets apartados.

Con el vídeo del Vallobín (entrenado con 0:00-20:00 y 30:00-final; evaluado en 20:00-30:00, que no ha visto):

| | Lector publicado | Afinado |
|---|---|---|
| Validación: espaldas con número bien leídas | 77,2 % | **92,4 %** |
| Validación: zonas sin número en las que dice que no hay número | 100 % | 94,0 % |
| Tramo 20:00-30:00: parte de las lecturas de cada tramo que van a su primer número (mediana) | 32 % | **53 %** |
| Tramo 20:00-30:00: tramos de lectura clara | 84 | **157** |
| Tramo 20:00-30:00: tiempo de jugador con identidad (equipo A / B) | 20,9 % / 22,9 % | 22,5 % / 25,7 % |
| Tramo 20:00-30:00: segundos identificados cuyo mejor recorte se lee con el dorsal asignado | — | 89 % |

Dos intentos fallidos antes, que dejan dos reglas:

- **Sin zonas sin número el afinado lee un número en todo**: el peso de las lecturas se triplicó y la cobertura bajó al
  8,5 %. Por eso `GuardaZonas` guarda también una de cada tres zonas en las que el lector no ve número.
- **Las zonas guardadas tienen que ser las que ve el lector**: guardadas con interpolación por áreas, el propio lector
  publicado pasaba del 86 % al 41 % de acierto en ellas, y el afinado aprendía de imágenes que no verá al analizar.

Con las lecturas más limpias se probó a bajar los umbrales de la resolución (evidencia 0,4, parte 0,45): la cobertura
sube al 29 %, pero en los recortes de lo ganado hay un 25-30 % de errores claros (un 4 bien visible dado como el 9),
así que se quedan los umbrales de antes. La continuidad sigue apagada por lo mismo. Ojo: el afinado solo ha visto un
partido (17 dorsales, dos equipaciones); conviene volver a entrenarlo juntando las zonas de cada partido nuevo. Los
intentos descartados dejan sus métricas en `modelos/afinados/descartados/` (los pesos se borraron el 17-11-2026).

## El balón con RF-DETR y TensorRT (17-11-2026)

Para no depender de Ultralytics (AGPL-3.0), el detector del balón pasa a **RF-DETR Medium** (Apache 2.0). Como el
balón mide unos 8 px en 1080p, no se mira el fotograma reducido: se entrena y se busca en **teselas de 384 px a
resolución nativa** que el detector amplía a 576 (el balón queda en unos 12 px), las mismas en el entrenamiento y en
el análisis (`teselas.py`). Un fotograma de 1080p son 24 teselas, que van en un solo lote a la GPU.

- **Datos** (`preparar-balon`): de los fotogramas de `datasets/vallobin` y `datasets/vallobin-c1` (2.125 distintos),
  dos teselas con el balón en sitios distintos por cada balón confirmado, dos al azar sin balón y una por cada
  candidato que el etiquetado descartó por quieto (marcas, focos, medias): 2.332 / 574 con balón, 3.468 / 782 al azar
  y 1.046 / 201 negativos difíciles (entrenamiento / validación, con los mismos bloques de tiempo que el etiquetado).
- **Entrenamiento** (`entrenar-balon`): 26 épocas en 69 min (lote 8 × 2, 7,5 GB de GPU), parado solo tras 8 épocas
  sin mejorar; en las teselas de validación, mAP50 0,94 y mAP50-95 0,71.
- **TensorRT** (`exportar-balon`): motor FP16 para la RTX 5070 Ti, lote de 1 a 32 afinado para 24, compilado en 70 s
  (`modelos/rfdetr/balon.trt`). Va atado a la GPU y a la versión de TensorRT (`tensorrt-cu12` 10.x, la que casa con
  torch cu128): en otra GPU o en Docker hay que volver a compilarlo; si no carga, el analizador sigue con PyTorch.

En los fotogramas enteros de validación de la ronda 1 (134 balones, los mismos con los que se midió el YOLO):

| Detector | Balones encontrados | «Falsos» | Tiempo por fotograma |
|---|---|---|---|
| YOLO afinado (06-11), fotograma entero a 0,25 | 103 (77 %) | 19 | — |
| YOLO afinado (06-11), teselas de 640 a 0,2 | 114 (85 %) | 63 | ≈ 56 ms (solo cuando se perdía) |
| RF-DETR v1, teselas de 384 a 0,2 | 119 (89 %) | 70 | 33 ms con TensorRT (59 ms con PyTorch FP16) |
| RF-DETR v1 a 0,5 | 110 (82 %) | 43 | |
| **RF-DETR v2 (segunda ronda) a 0,2** | **124 (92 %)** | 104 | **33 ms con TensorRT** |
| RF-DETR v2 a 0,5 | 118 (88 %) | 46 | |

Los «falsos» cuentan contra pseudoetiquetas: mirando uno a uno los 43 de confianza ≥ 0,5, **casi todos son balones
de verdad** (un segundo balón junto a la banda o en la portería, balones en el aire o rodando que la regla del
etiquetado no llegó a confirmar, en fotogramas marcados «sin balón»); de verdad falsos solo hay unos cinco (dos
destellos de focos, una media o un pantalón movido). Por eso el análisis usa 0,2 como suelo y 0,5 como confianza a
partir de la cual un candidato vale aunque no haya un jugador cerca ni lo espere la trayectoria (`deteccion.py`).
TensorRT da los mismos aciertos que PyTorch, 1,8 veces más rápido.

En el tramo 20:00-30:00 del Vallobín, contra la pasada del 16-11 con el YOLO afinado (`salidas/dorsales-20-30-v3`):

| Qué | YOLO afinado | RF-DETR v1 | RF-DETR v2 |
|---|---|---|---|
| Fotogramas con balón visto | 36,5 % | 32,8 % | 34,8 % |
| Con los huecos rellenados | 61,3 % | 54,1 % | 56,8 % |
| Toques / secuencias | 123 / 65 | 95 / 49 | 111 / 53 |
| Tiempo de la pasada (10 min de vídeo) | 357 s | **240 s** | 256 s |

Fotograma a fotograma coinciden en 704 de 2.997, en 557 los dos ven un balón en sitios distintos, en 579 solo el YOLO
y en 363 solo RF-DETR. Mirando muestras de cada grupo: donde discrepan suele acertar RF-DETR (el balón alto en el
aire, el saque de banda con el balón en las manos, el balón entre varios jugadores), y lo que solo él ve son casi
siempre balones de verdad (a los pies, lejos, junto a la valla; alguno fuera de juego). Lo que **se le escapa** es el
balón en el aire contra el cielo negro y el **balón parado a los pies** de un jugador: ahí ni a confianza 0,01.

La causa estaba en los datos: como negativos difíciles se cogían todos los candidatos quietos respecto al césped, y
un balón parado a los pies también lo está (328 candidatos quietos con confianza ≥ 0,4), y los trozos al azar podían
contener balones sin etiqueta (unos 940 candidatos que se movían sin llegar a confirmarse). `preparar-balon` ahora
solo toma como negativo lo que el propio etiquetado rechaza (quieto, sin la regla rápida y con confianza < 0,4) y
ningún trozo sin balón toca un candidato dudoso: `datasets/balon-teselas-v2` (851 / 159 negativos difíciles en vez
de 1.046 / 201) para una segunda ronda.

**Segunda ronda (v2, la que se usa desde el 17-11-2026)**: con las mismas opciones, 19 épocas en 52 min (mAP50-95 0,72
en sus teselas de validación). En los fotogramas enteros encuentra 124 balones de 134 (92 %) y, quedándose solo con el
mejor candidato de cada fotograma, acierta en 121 frente a 111 del v1, a la misma velocidad. En el tramo ve más balón
que el v1 (tabla de arriba), aunque menos fotogramas que el YOLO: contra él coinciden en 744, discrepan de sitio en
608, solo el YOLO ve balón en 488 y solo el v2 en 351. En las muestras, donde discrepan suele acertar el v2 y lo que
solo ve él son casi siempre balones reales (a los pies, entre varios jugadores, en el aire contra el cielo). Lo que se
le sigue escapando es el **balón a los pies entre jugadores** y el balón pequeño y lejano en césped vacío: en los cinco
fotogramas de prueba (1278,2, 1526,6, 1589,0, 1609,6 y 1616,6 s) sigue sin pasar de 0,02. El v1 queda en
`modelos/rfdetr/v1/balon.pth` por si hay que volver atrás.

Lo que falta ya no son negativos mal puestos sino positivos: el dataset del balón solo toma fotogramas `aprobado` y
`rechazado`, y los 3.038 `sin_confirmar` de `vallobin` y `vallobin-c1` (el balón disputado entre piernas, justo lo
difícil) nunca entran. El balón parado sí está (860 de los 2.106 balones elegidos estaban quietos respecto al césped),
pero casi siempre suelto. Para la tercera ronda: volver a etiquetar con RF-DETR como maestro (reajustando
`CONF_BALON_*` y `CONF_QUIETO_*`, que salieron de las puntuaciones del YOLO) para confirmar más fotogramas, y revisar
a mano una muestra de los `sin_confirmar` (`exportar-revision` → CVAT → `importar-revision`). Y, sobre todo, sumar
partidos distintos (de día, otros campos, otras cámaras, otro balón): hoy todo lo aprendido y todo lo medido es del
Vallobín de noche.

## Los jugadores con RF-DETR y el seguidor de `trackers` (17-11-2026)

El detector de personas (porteros, jugadores y árbitros) pasa también a **RF-DETR Medium**, y el seguimiento deja el
modo `track` de Ultralytics por el BoT-SORT / ByteTrack del paquete `trackers` de Roboflow (Apache 2.0), con los
mismos ajustes que tenían los yaml de antes (`rastreador.py`; BoT-SORT con compensación del movimiento de cámara por
flujo óptico y sin reidentificación por apariencia). Las personas miden 40-110 px en 1080p, así que aquí no hacen
falta teselas: el fotograma entero reescalado a 1280.

- **Datos** (`preparar-jugadores`): las etiquetas `personas/` de `vallobin`, `vallobin-c1` y `vallobin-r2` sin el
  balón (que es del otro detector) y sin instantes repetidos: 3.429 / 785 fotogramas (entrenamiento / validación) con
  838 / 187 porteros, 47.962 / 11.722 jugadores y 6.083 / 1.545 árbitros. Son pseudoetiquetas del YOLO público (≥ 0,45).
- **Entrenamiento** (`entrenar-jugadores`): 12 épocas en 75 min (lote 4 × 4, 10,5 GB de GPU); en su validación
  mAP50 0,81 y mAP50-95 0,67, aún subiendo despacio (0,665 → 0,671 en las tres últimas épocas).
- **TensorRT** (`exportar-jugadores`): motor FP16 de lote 1, compilado en 69 s; 14,4 ms por fotograma frente a 26,1
  con PyTorch, con los mismos aciertos.

Contra las etiquetas de validación (785 fotogramas, 13.454 personas):

| Umbral | Encontradas | De más | Misma clase (portero / jugador / árbitro) |
|---|---|---|---|
| 0,2 | 98 % | 6.831 | 109/176 · 11.213/11.431 · 1.223/1.508 |
| **0,3** (el del análisis) | **95 %** | 3.192 | 104/165 · 10.978/11.161 · 1.184/1.459 |
| 0,4 | 92 % | 1.580 | 91/145 · 10.688/10.824 · 1.122/1.368 |
| 0,5 | 88 % | 752 | 77/127 · 10.308/10.391 · 1.035/1.248 |

La evaluación es circular (mide cuánto se parece al YOLO), así que se miraron recortes. Lo que ve **de más** son casi
siempre personas reales que el YOLO no daba: jugadores movidos, tapados en un grupo, porteros, liniers y el árbitro;
de verdad mal solo hay alguna caja que abarca a dos jugadores juntos y jugadores del equipo oscuro clasificados como
árbitro. Lo que **pierde** (669 a 0,3) son sobre todo jugadores rojiblancos muy movidos o tapados, y alguna etiqueta
mala del YOLO (una sombra, el linier cortado por el borde). Por eso se mantiene 0,3.

En el tramo 20:00-30:00 del Vallobín, contra la pasada con el YOLO público y el BoT-SORT de Ultralytics (y el mismo
balón v2, `salidas/rfdetr-v2-20-30`):

| Qué | YOLO + Ultralytics | RF-DETR + `trackers` |
|---|---|---|
| Tracklets (de menos de 2 s) | 1.572 (875) | **1.090 (482)** |
| Tracklets de 10 s o más | 173 | 183 |
| Duración mediana / media de un tracklet | 1,6 / 5,2 s | **2,4 / 7,7 s** |
| Tracklets partidos por mezclar equipos | 85 | **54** |
| Segundos sin identificar (equipo A / B) | 5.989 / 4.574 | **4.459 / 3.228** |
| Jugadores identificados por el dorsal | 26 | 22 |
| Balón visto / con los huecos rellenados | 34,8 / 56,8 % | 35,0 / 57,4 % |
| Toques / secuencias | 111 / 53 | 117 / 60 |
| Posesión A / B | 52,7 / 47,3 % | 44,9 / 55,1 % |
| Tiempo (equipos + pasada) | 23 + 256 s | **14 + 230 s** |

El seguimiento se corta bastante menos (un tercio menos de tracklets, la mitad de los de menos de 2 s), y por eso un
29 % más de tiempo con identidad. Los cuatro dorsales que deja de identificar son el 13 y el 20 del equipo claro (8 y
14 s; salen también en el resto del partido), el 21 del oscuro (20 s; no sale en ningún otro tramo, seguramente era
una mala lectura) y el 7 del claro (menos de un segundo). La posesión cambia ocho puntos, pero ya cambiaba así de una
pasada a otra con solo cambiar el detector del balón (minuto a minuto, hasta 15 s): con un 47 % del tiempo atribuido,
no sirve para comparar detectores.

Los tracklets de árbitro bajan de 200 a 116, pero sigue habiendo jugadores marcados como árbitro: entre los más largos,
tres del equipo oscuro de 11 s y uno rojiblanco de 8 s (antes: un rojiblanco de 48 s, uno oscuro de 15 s...). Viene de
las etiquetas, en las que el YOLO ya los confundía, y la clase se decide por mayoría de votos del detector
(`seguimiento.py`). Lo mismo con los porteros, que con 838 ejemplos se confunden con jugadores.

**Efecto en el etiquetado**: `etiquetar` usa ya este detector para las personas, y el balón sobre una persona ya no se
confirma (ver «Afinar los detectores con partidos propios»).

## El campo con RF-DETR Keypoint (18-11-2026)

El último detector de Ultralytics, el de los 32 puntos del campo (YOLOv8-pose de Roboflow), pasa al modelo de puntos
clave de RF-DETR (`RFDETRKeypointPreview`, Apache 2.0; `detector_campo.py`), y con él `ultralytics` sale de las
dependencias. La calibración (`campo.py`) no cambia: puntos con confianza > 0,5 y homografía con RANSAC.

- **Datos** (`preparar-campo`, `dataset_campo.py`): por **destilación**. El YOLO público a 640 px mira un fotograma por
  segundo del Vallobín (y cuatro por segundo de los dos clips de muestra) y cada punto que da con confianza ≥ 0,5 queda
  etiquetado; los demás, sin etiqueta. No con solo los puntos coherentes de `etiquetar`: el modelo de puntos aprende
  también si cada punto «se encuentra», y un punto sin etiqueta le enseña que no está (el YOLO afinado así se calló).
  6.032 fotogramas (4.952 / 1.080), 14 puntos de media, 116 sin campo.
- **Memoria**: el modelo es del tamaño XL y cada consulta lleva una ficha por punto en el decodificador; con las 100
  consultas de serie (× 13 grupos en el entrenamiento) y 32 puntos no cabía en 16 GB ni con lote 4. Como en cada
  fotograma hay un solo campo, van **20 consultas** (`CONSULTAS`), lote 4 × 4: 9,6 GB y unos 6,5 min por época a 768 px.
- **Entrenamiento** (`entrenar-campo`): 16 épocas en 102 min, cortado a mano cuando la métrica de puntos (OKS) ya subía
  menos de 0,001 por época (mAP50-95 0,725; la caja del campo, 0,96). Probado y descartado: seis épocas más con la
  tasa de aprendizaje diez veces menor y más peso al error de los puntos (peor: 54 % de fotogramas calibrables), y cada
  vértice como un objeto pequeño para RF-DETR Medium (igual que el de puntos a la misma altura del entrenamiento).
- **TensorRT** (`exportar-campo`): 7-10 ms por fotograma frente a 16 con PyTorch y unos 10 el YOLO.

**Cómo medirlo.** Las etiquetas de `datasets/vallobin/campo` son los puntos coherentes **del propio YOLO**, así que
contra ellas el YOLO se mide a sí mismo: 76 % calibrables, 5,4 puntos a menos de 15 px y 13 px de error frente al 64-68
%, 3,0 puntos y 27-30 px del RF-DETR. Eso es parecido, no acierto. Con una medida que no depende de nadie (se
proyectan las líneas del campo con la homografía de cada uno y se mide la distancia a las líneas blancas de la imagen,
863 fotogramas de validación), cuando los dos calibran encajan igual: 50,6 px el YOLO y 52,0 el RF-DETR, que gana en el
47 % de los fotogramas. Los dos quedan lejos de las líneas porque con 120 × 70 la homografía no cuadra con el Vallobín
(se ve a ojo: el círculo central proyectado cae unos 40 px más abajo). Lo que sí pierde el RF-DETR es cobertura: calibra
el 69 % de esos fotogramas frente al 78 % (bajar su umbral no ayuda: entran puntos que RANSAC no acepta).

En el tramo 20:00-30:00, contra la pasada con el YOLO del campo (`salidas/rfdetr-jugadores-20-30`):

| Qué | YOLO del campo | RF-DETR Keypoint |
|---|---|---|
| Campo calibrado | 70,3 % | **70,7 %** |
| Calibraciones nuevas / homografías invalidadas | 1.620 / 282 | 1.336 / **189** |
| Balón visto / con los huecos rellenados | 35,0 / 57,4 % | 34,3 / 55,7 % |
| Posesión A / B | 44,9 / 55,1 % | 45,1 / 54,9 % |
| Eventos | 14 | 17 |
| Tiempo de la pasada | 230 s | **213 s** |

Con la cámara móvil la cobertura se iguala: calibra menos fotogramas desde cero, pero su homografía se invalida menos y
se reaprovecha más. Los eventos de más son candidatos flojos (goles a 0,45 sin saque de centro detrás, y uno repetido
a 3 s del otro en la misma jugada, que es cosa de `eventos.py`).

## Faltas, fueras de juego y lo que dice el acta (18-11-2026)

Lo que el vídeo no ve no quiere decir que no pasó: hay datos que salen **por descarte**. Medido sobre las posiciones
guardadas del J8 del Vallobín (0-1, cámara móvil, sin descanso en el vídeo):

- **Objetos fijos** (`balon.py`): tres sitios junto a la línea donde el «balón» se quedaba quieto una y otra vez (64
  veces entre el minuto 30 y el 95 en el peor, 216 s): se quitan 3.523 detecciones que no eran el balón y que, sin
  quitarlas, parecían faltas y saques.
- **Descanso por el cambio de campo** (`mitades.py`): el vídeo no tiene parón en el descanso; los 46 minutos de antes
  del 46 con un orden de los equipos y 49 de 51 con el contrario después: segunda parte desde el 46:00 y la dirección de
  ataque se da la vuelta bien.
- **Goles con el resultado del acta** (`oficial.py`): de 20 candidatos a gol se quitan 19 (balones por encima del
  larguero, que en el plano cruzan entre los postes) y queda uno del visitante. Con el minuto de cada gol, el candidato
  es el más cercano a ese minuto (el descuento, `45+2`, sigue en su parte); sin él, el de más confianza.
- **Faltas** (`interrupciones.py`): solo las paradas con un rival a menos de 5 m del que tenía el balón (en las
  demás no había nadie cerca: eran otra cosa). Salen tres (02:13, con un jugador en el suelo; 30:32; 1:20:24): pocas,
  porque el balón quieto se ve poco, pero las que salen son faltas. Los fueras de juego siguen la misma regla; en el J8
  no salió ninguno.
- **Minutos por descarte**: con el acta cerrada, el titular sin cambio juega el partido entero aunque la cámara no lo
  vea todo el rato. La app lo aplica al volcar (también a los que jugaron y no se identificaron en el vídeo), con la
  titularidad, los goles y las tarjetas del acta; con la provisional no, porque le pueden faltar los cambios.

El código de eventos se partió en módulos (`mitades.py`, `interrupciones.py`, `oficial.py`, `jugadas.py`) y el
detector de eventos es ahora una clase con un paso por instante (`eventos._Detector`), con el mismo resultado.

## Siguientes pasos

- Medidas reales del campo del Vallobín (o de cada campo) para etiquetar los 32 puntos por reproyección (completos y
  coherentes) y volver a entrenar el RF-DETR de puntos sin depender del YOLO, midiéndolo con las líneas blancas y no
  con sus puntos; a medio plazo, calibrar por las líneas (segmentación) y no por puntos.
- Revisar y corregir a mano una parte de las pseudoetiquetas (`exportar-revision` → CVAT → `importar-revision`) y
  volver a entrenar; con la clave de la API, `--juez claude` sobre los candidatos a balón.
- En la sección «Del vídeo» de la app: los eventos uno a uno con su clip (hoy se lanza con `--sin-clips`), el minimapa
  y la posesión por tramos; y separar mejor los dos equipos de noche (en las pruebas quedaron 6 y 21 jugadores seguidos).
- Volver a afinar el lector de dorsales con las zonas de más partidos (hoy solo uno) y unir mejor los tramos sin
  número (re-identificación por apariencia): es lo que falta para subir del 25 % de tiempo con identidad.
- Condiciones de grabación en el manual y aviso en la subida cuando el encuadre no sirve.
- Árbitros y porteros: no fiarse solo del voto de clase del detector; un «árbitro» cuya camiseta es claramente la de un
  equipo es un jugador. Y revisar a mano una muestra de personas (sobre todo árbitros y porteros) para la segunda
  ronda del detector de jugadores, con más partidos.
- Eventos: un gol o un tiro candidato repetido a pocos segundos en la misma jugada debería quedar en uno.
- Faltas: más cobertura sin perder precisión (el balón quieto se ve poco); probar el silbato en el audio y la postura
  de los jugadores (uno en el suelo) para confirmar las paradas por frenazo.
