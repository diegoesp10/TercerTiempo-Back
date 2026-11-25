# TercerTiempo.Analisis

Analizador del vídeo de un partido: detecta jugadores, árbitros y balón, los sigue, separa los equipos, calibra el
campo y de ahí saca la **posesión** (total, por mitad, por tercio, por minuto), mapas de calor, distancias y
velocidades por jugador, y **eventos candidatos** (balón fuera: saque de banda, córner, saque de puerta; tiros y tiros
a puerta; goles con el saque de centro que los confirma; paradas; parones y descanso). Lo deja en una carpeta como
`analisis.json`, `resumen.md`, `acta-propuesta.json` (con la forma que pide el acta interna de la API), mapas,
recortes de cada jugador seguido, `posiciones.csv.gz` y clips de los eventos. Opcionalmente, un modelo de lenguaje
con visión (la API de Claude o un modelo local vía Ollama) confirma los eventos con tres fotogramas, lee dorsales en
los recortes y redacta el resumen.

Se llama **Ojeador** y lleva versión (`tt_analisis/__init__.py`, la misma que `pyproject.toml` y `uv.lock`; lo comprueba
`tests/test_version.py`). Cada análisis deja en `analisis.json` el bloque `analizador`: nombre, versión y cada modelo con
su arquitectura, el entrenamiento del que salieron los pesos, el motor (TensorRT o PyTorch) y su huella (los 8 primeros
caracteres del SHA-256 del archivo cargado). La app lo guarda con el análisis y lo enseña en el acta interna («analizado
con Ojeador 2.0.0»; los modelos, solo al administrador). **Sube la versión con cada cambio que altere lo que propone**:
la mayor con modelos o una cadena nueva, la menor con reglas nuevas y la de parche con arreglos.

| Versión | Fecha | Qué cambia |
|---|---|---|
| 2.3.1 | 25-11-2026 | **Dos pasadas del mismo vídeo dan lo mismo**: el motor TensorRT del campo (FP16) no devolvía siempre lo mismo con la misma imagen (hasta 136 px en un punto) y la calibración arrastraba cada diferencia; en el J8 (20:00-21:30), cuatro pasadas iguales daban el campo calibrado entre el 64 y el 83 %, de 1 a 4 eventos y la posesión de 62/38 a 41/59. Ahora un motor TensorRT solo se usa si **da siempre lo mismo** con una imagen de prueba al cargarlo (`rfdetr_comun.da_siempre_lo_mismo`); si no, van los pesos de PyTorch. El del campo no la pasa y va con PyTorch FP16: dos pasadas salen idénticas, calibra igual o algo más y cuesta unos 10 ms por fotograma analizado («Dos pasadas, el mismo resultado») |
| 2.3.0 | 25-11-2026 | **El balón primero y quién es quién también por el aspecto**: al fotograma analizado sin balón se le busca en los de ±0,04 s (`vecinos_balon_s`), en una ventana junto al balón cercano y, sin él, en la imagen entera de un solo fotograma por hueco (`vecinos_enteros`); con el campo calibrado, los huecos de hasta 3 s se rellenan **siguiendo a la cámara**; **balón en juego** (`en_juego.py`): lo visto y la trayectoria se miden sobre ese tiempo (`recuento.balon_en_juego`, y `calidad.balonEnJuego` en la propuesta), la posesión solo reparte ese tiempo y la app enseña y avisa con el balón visto en juego; el seguidor recuerda 1,5 s al que pierde (antes 10) y parte los tracklets de dos del mismo equipo que se cruzan; el dorsal de los que están junto al balón se relee en el fotograma de la mitad; y **el aspecto** (SigLIP, modelo nuevo: tocaría la mayor, pero se pidió la 2.3): a la cadena sin dorsal se le da el jugador que dice una regresión aprendida en el mismo partido con las identificadas, solo si antes se comprueba que acierta en el 95 % de las que da y si podía ser él. En el J8, tramo 20:00-30:00: el balón con trayectoria pasa del 68,6 al 80 % del tiempo en juego (visto, del 41,3 al 48,6 %), el tiempo con identidad del 26 al 32 % y del 36 al 42 %, y la pasada de 239 a 359 s (pasos 2, 3 y 6; «El balón en juego y quién es quién por el aspecto») |
| 2.2.0 | 25-11-2026 | **Solo las acciones que se ven**: medido en el Vallobín - Real Titánico, casi todos los pases, pérdidas y recuperaciones de la 2.1.2 no estaban en el vídeo (balones que pasaban cerca de alguien, forcejeos, balones parados, cosas quietas en el césped y momentos sin el campo calibrado). Ahora las acciones de cada jugador salen de **toques controlados** (`MaquinaControl`: el balón a menos de 2 m en 0,2 s seguidos con el balón visto, sin cruzar de un lado a otro del jugador, quitárselo a quien lo tiene solo estando claramente más cerca y sin avistamientos más rápidos que un tiro); la posesión del equipo sigue con el más cercano. **Pase**: balón golpeado (4 m o más, entre 4 y 30 m/s) a un compañero. **Pérdida y recuperación**: el rival se hace con el balón en 2,5 s como mucho, no parado, y lo conserva 1,5 s o más. Sin el campo calibrado en los dos extremos no se cuenta. Un tiro no es pérdida ni pase. **El portero no pasa, ni pierde, ni recupera**: el balón del rival con el que se queda en su área 1 s o más es una **parada** (ya no sale del tiro a puerta) y el que pone en juego desde su área (a 8 m en 3 s), un **saque** (`distributions` en la propuesta y en `momentos`; no va al acta). El portero no tira. Junto a un objeto fijo solo vale el balón que va de paso, aunque haya un jugador al lado. Salen muchas menos acciones, pero las que salen se ven (paso 4) |
| 2.1.2 | 25-11-2026 | **Cada acción con su segundo del vídeo**: en `acta-propuesta.json`, cada jugador y cada equipo traen `momentos`, el segundo del vídeo (a la décima) de cada cosa que cuenta su propuesta: del jugador, cada pase (cuando el balón deja su pie), pase fallado, pérdida, recuperación (cuando le llega), tiro, tiro a puerta, gol, parada, falta cometida y recibida y fuera de juego; del equipo, cada tiro, tiro a puerta, gol, córner, saque de banda, saque de puerta, falta y fuera de juego. Llevan las mismas claves y los mismos filtros que sus datos, así que lo que se cuenta y lo que se puede ver es lo mismo (salvo los goles que salen del acta oficial cerrada sin que el vídeo los viera). La app los enseña con «Ver» en «Del vídeo». Lo que se propone no cambia (paso 8) |
| 2.1.1 | 24-11-2026 | **El tramo del detector que viste como el portero nunca acaba en una línea de campo**: si su portero no sale identificado, los tramos que solo marca el detector pero que en su rato visten como él (comprobado con su color) se quedan sin identificar, como los del portero por la equipación; hasta ahora se resolvían como los de campo. En una pasada del 20:00-30:00 del J8, 5 s del 13 (de morado) salen de la línea del 17 y es lo único que cambia; en el partido entero no cambia nada (paso 6) |
| 2.1.0 | 24-11-2026 | **Del portero cuyo tracklet saltó a otra persona, las lecturas de su rato**: cada lectura del dorsal guarda su segundo (`HistorialLecturas`) y, del tracklet de portero en que el seguidor saltó a otro jugador, cuentan las de su rato a 0,4 s o más de cada corte (hasta la 2.0.0, ninguna). En el J8, sobre la misma pasada, la evidencia del 1 del Vallobín pasa de 5,1 a 15,6 (confianza de 0,07 a 0,16) y la del 13 del Real Titánico de 12,5 a 17,5, y ningún jugador de campo cambia. En una pasada del tramo 20:00-30:00, con la regla vieja el portero salía como el 6 y con la nueva queda sin identificar (paso 6 y «Las lecturas del portero cuyo tracklet saltó») |
| 2.0.0 | 24-11-2026 | **Lector de dorsales afinado también con los porteros** (pesos nuevos): sus espaldas con su número y su pecho sin número, porque el escudo del pecho se leía como un número y al portero casi nunca se le ve de espaldas; al entrenar, los dorsales que casi no se ven se repiten (`--minimo-por-dorsal`), y `--guardar-dorsales` guarda también las zonas de los porteros identificados. En el J8, en el tramo 20:00-30:00 (que el lector no ha visto), al 13 del Real Titánico le van el 31 % de sus lecturas (antes el 8 %) y los números leídos en su pecho bajan del 28 % al 4 %. En el partido entero, el 1 del Vallobín se lee en sus 8 espaldas (antes en ninguna, aunque esas el lector nuevo las ha visto al entrenar) y, en la identificación, la confianza del 13 sube de 0,03 a 0,23 y el 1 sale por lecturas en vez de por descarte. Los jugadores de campo, igual (paso 6 y «Lector de dorsales que lee a los porteros») |
| 1.3.0 | 24-11-2026 | **El portero, solo si todo cuadra; si no, sin identificar**: pasa la mayor parte del tiempo al lado o debajo de la portería que defiende su equipo (en cada parte con el campo calibrado al menos 20 s, el 70 % o más en la zona del portero: 22 m de su línea y 25 m del eje), y la parte en que no, no es suya; **nunca viste como los jugadores de campo de ningún equipo** (el que el detector marca de portero sin muestras de su equipación para comprobarlo, como el rival que remata en el área, no es nadie); su dorsal sale del acta (solo los de su equipo en el campo y, si el acta dice quién es portero, uno de ellos) y **por descarte solo si la equipación y la posición lo confirman** (tramos por la equipación, un minuto con el campo calibrado junto a su portería), sin los tramos que solo marca el detector. Su **recorte**, de un momento en que viste como él dentro de su rato, leído del vídeo (si no se sabe cómo viste, uno en que se le leyó su dorsal; si no, ninguno): la línea del 1 del Vallobín en el J8 salía con un jugador del Real Titánico (paso 6) |
| 1.2.0 | 23-11-2026 | **Porteros por la equipación**: el color de torso y pantalón que no es de ningún equipo ni del árbitro y vive pegado a una portería es el del portero de quien la defiende, también cuando el detector lo toma por un jugador; el que el detector marca de portero pero viste como un equipo es un jugador de ese equipo. **Del tracklet del portero solo cuenta el rato en que viste como él** (el seguidor salta a veces al jugador que pasa pegado, y a veces vuelve: también se corta en medio) y, si saltó, sus lecturas del dorsal no cuentan; al revés, del jugador de campo que empieza, acaba o pasa por un portero se quita ese rato. Su dorsal, sin los números que ya llevan con claridad los jugadores de campo mientras se le ve y, si no basta, **por descarte con el acta**; y ningún jugador de campo de su equipo lleva su dorsal (pasos 1 y 6) |
| 1.1.0 | 21-11-2026 | El portero es del equipo que defiende su portería en cada parte (en el descanso cambian de campo), no del que tiene más cerca; sus tramos de las dos partes se juntan por su dorsal (paso 6) |
| 1.0.0 | 21-11-2026 | Nombre y versión en cada análisis, con los modelos y su huella |

Es un programa aparte de la API y del servicio de vídeo (Python, GPU); la evaluación previa y el plan están en
[`docs/lector-videos.md`](../../docs/lector-videos.md). Desde el 06-11-2026 la app lo lanza sola: ver «Integración con
la app».

## Qué sale y con qué fiabilidad

| Dato | Cómo | Fiabilidad con cámara fija lateral a 1080p |
|---|---|---|
| Posesión por equipo, mitad, tercio y minuto | Jugador más cercano al balón con histéresis; el balón en vuelo es de quien lo lanzó. Solo se reparte el tiempo con el balón en juego (desde la 2.3.0) y visto o interpolado, y sale con su **confianza** (alta, media o baja) | Alta si el balón se ve; con confianza baja la app no la vuelca |
| Mapas de calor, distancia, velocidad máxima | Posiciones proyectadas al campo (homografía por los puntos de las líneas), solo mientras el vídeo tiene al jugador a la vista: la distancia no es la de todo el partido (la app dice «m mientras se le ve») | Alta si la calibración es buena |
| Saques de banda, córners, saques de puerta | Balón fuera + quién lo tocó + hacia dónde ataca cada equipo. Si vuelve a salir por la misma línea sin haber vuelto al juego (2 s dentro y a más de 10 m de donde salió), es el mismo parón: lo recogen o la calibración tiembla en la línea | Media-alta |
| Tiros / a puerta | Balón rápido hacia la portería desde menos de 35 m; corte con la línea de gol. Un toque es un solo tiro: el balón que acaba entrando es el mismo disparo que ya se vio | Media (candidatos: revisar el clip) |
| Goles | Balón que cruza entre los postes; se confirma si sigue un saque de centro. Con el acta oficial cerrada, **como mucho los del acta**: con el minuto de cada gol se queda el candidato más cercano (a menos de 5 min si lo confirmó el saque de centro, de 2 min si no; sin minutos, solo los confirmados) y los demás se quitan (balones por encima del larguero o recogidos detrás de la portería). Del tiro de un gol quitado, el disparo que se vio queda como tiro fuera y el que solo se dedujo del cruce se quita | Media-alta con confirmación; con el acta, alta |
| Faltas y penaltis | El juego se para con el balón en el campo (el balón quieto lejos de las líneas o el frenazo de todos a la vez) sin un fuera ni un gol que lo explique: **la recibe el que tenía el balón y la comete el rival más cercano**, que tiene que estar a menos de 5 m | Baja en cobertura (el balón quieto se ve poco), alta en lo que sale |
| Fueras de juego | Parada en la que saca el otro equipo desde donde estaba un atacante por delante del penúltimo defensor en el último pase hacia delante de su equipo | Baja en cobertura |
| Minutos de cada jugador | **Por descarte, del acta oficial cerrada**: sin cambio, el partido entero aunque la cámara no lo vea todo el rato; con cambio, desde o hasta ese minuto | La del acta |
| Paradas y saques del portero | Parada: el portero se queda 1 s o más en su área con un balón del rival. Saque: el balón que suelta en su área está a 8 m en 3 s. El portero no pasa, ni pierde, ni recupera | Alta en lo que propone; se escapan las que no se ven (cámara cerrada, balón por el aire) |
| Pases, pérdidas, recuperaciones por tracklet | Toques controlados (no el más cercano): pase con el balón golpeado a un compañero; pérdida y recuperación si el rival se hace con él poco después, no parado, y lo conserva; solo con el campo calibrado | Alta en lo que propone; son un mínimo |
| Quién es quién | **Dorsal leído en la espalda** (PARSeq) y sumado en todos los fotogramas de cada jugador; con el acta oficial (`--plantillas`), solo los dorsales de los que jugaron y en sus minutos. Desde la 2.3.0, también **por el aspecto** (SigLIP): solo si en el partido acierta en el 95 % de las cadenas identificadas que se apartan para probarlo | Alta en el número leído; el tiempo sin identificar (de frente, lejos) cuenta para el equipo y no para nadie en concreto |
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
campo con RF-DETR Keypoint»). Un motor TensorRT solo se usa si da siempre lo mismo con la misma imagen; el del campo no
lo hace y el análisis va con sus pesos de PyTorch (ver «Dos pasadas, el mismo resultado»). Desde el 18-11-2026 no queda
nada de Ultralytics (AGPL-3.0).

El modelo del aspecto (SigLIP, desde la 2.3.0) tampoco lo baja `descargar-modelos`: se guarda una vez la parte de la
imagen en `modelos/siglip` (unos 370 MB). Sale idéntico al que se midió (huella `fad78ac3`). Sin esa carpeta el
análisis lo avisa y solo identifica por el dorsal.

```powershell
uv run python -c "from transformers import SiglipVisionModel as M; M.from_pretrained('google/siglip-base-patch16-224').save_pretrained('modelos/siglip')"
```

ffmpeg hace falta solo para los clips de los eventos (en Windows se busca también el de winget).

## Uso

```powershell
uv run tt-analisis analizar partido.mp4 --salida salidas\partido --equipos "Vallobín,Pumarín"
uv run tt-analisis analizar partido.mp4 --segmento 20:00-25:00 --video-anotado      # un tramo, con el vídeo anotado
uv run tt-analisis analizar partido.mp4 --descanso 52:30 --llm claude                # confirma eventos y redacta con la API de Claude
uv run tt-analisis analizar partido.mp4 --llm ollama --modelo-llm gemma4:12b         # lo mismo con un modelo local
uv run tt-analisis analizar partido.mp4 --fps 3 --sin-clips                          # más rápido, menos fino
uv run tt-analisis comparar salidas\a salidas\b salidas\c                              # varias pasadas del mismo tramo, lado a lado
uv run tt-analisis visor salidas                                                     # verlo encima del vídeo (ver «El visor del Ojeador»)
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
desplazados dentro del paso) y `--sin-repeticion` (no guardar `repeticion/`, lo que lee el visor, ni lo que deja
verlo mientras se hace). `comparar` pone varias pasadas lado a lado (balón, calibración, toques, posesión y su
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
uv run tt-analisis exportar-campo                                # TensorRT → modelos\rfdetr\campo.trt (solo se usa si da siempre lo mismo)
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
| `analisis.json` | Todo: vídeo y parámetros, equipos y colores, dirección de ataque por mitad, descanso, calibración, recuentos (`pct_con_balon` es el balón visto, lo que guarda la app; `pct_con_balon_total` y `balon_relleno`, con los huecos rellenados; desde la 2.3.0, `balon_vecinos` y `balon_en_juego`, lo visto y la trayectoria sobre el tiempo en juego), identificación (desde la 2.3.0, con `apariencia`: la prueba del aspecto de cada equipo y lo que dio), posesión (con `confianza` y `cobertura`), eventos (con confianza, clip y veredicto del modelo), tracklets (acciones, distancia, mapa de calor, recorte, dorsal) y tiempos |
| `acta-propuesta.json` | Lo que pide la API del acta interna: `teamStats` desde los dos lados (posesión con `possessionConfidence` y `possessionBallPct`, tiros, córners, saques, goles), una línea por tracklet con la propuesta por jugador y `calidad`; la app decide cuál es «nuestro» equipo y qué tracklet es cada ficha. Desde la 2.1.2, cada equipo y cada jugador llevan también `momentos`: el segundo del vídeo de cada cosa que cuentan, para verla desde la app |
| `mapas/` | Mapas de calor PNG por equipo y por tracklet |
| `recortes/` | El mejor recorte de cada tracklet, para asignarlo a una ficha |
| `clips/` | Clip de 10 s de cada tiro, gol, córner y parada |
| `posiciones.csv.gz` | Posición de cada persona y del balón en cada fotograma analizado (campo en cm e imagen en px) |
| `repeticion/` | Desde el 25-11-2026: lo que vio el analizador en cada fotograma y lo que sacó de ello, para el visor (`fotogramas.jsonl` y `final.json`; mientras se analiza, `directo.json` y `directo.jsonl`, que se borran al terminar; ver «El visor del Ojeador»). No cambia nada del análisis y la app no lo lee |
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
   Los porteros no votan: visten distinto que los suyos y, cuando atacan los rivales, quien tienen más cerca es el
   rival (hasta la 1.1.0 iban con el equipo que tenían más cerca y, en el J8 del Vallobín, 26 tramos de portero, unos 18
   minutos, acababan en el otro equipo). Su equipo sale después de la portería que defienden en cada parte (paso 6).
   Desde la 1.2.0 cada tracklet guarda también, en los fotogramas en los que nadie lo tapa, el color de su **equipación
   entera** (`colores.rasgos_equipacion`: mediana del centro del torso y del pantalón en Lab, la claridad respecto al
   césped de los lados; sin la máscara de césped, que se come una camiseta verde): hasta 64 muestras repartidas por todo
   el tracklet, con el segundo en que se tomó cada una, para encontrar a los porteros (paso 6).
   El reparto sale en `recuento.reparto_equipos` y el analizador avisa si un grupo se queda con menos de la quinta
   parte de los recortes.
2. **Pasada principal** (5 fotogramas/s): detección de personas con el RF-DETR de jugadores en el fotograma entero a
   1280 px (`detector_jugadores.py`) y seguimiento con BoT-SORT o ByteTrack según la cámara (`rastreador.py`, paquete
   `trackers`), detección del balón (`deteccion.py` y `detector_balon.py`: RF-DETR en
   todo el fotograma por 24 teselas de 384 px a resolución nativa, en un lote con TensorRT; filtros de tamaño y de
   salto; entre varios candidatos gana el más coherente con la trayectoria
   reciente y con dónde están los jugadores, y un candidato flojo solo vale cerca de un jugador o de donde se
   esperaba el balón; desde la 2.3.0, al fotograma en el que no se ve se le busca en los de ±0,04 s, `vecinos_balon_s`:
   en una ventana alrededor del balón del fotograma analizado de al lado o de uno de hace un momento y, sin él, en la
   imagen entera, en un solo fotograma por tramo entre dos analizados, `vecinos_enteros`, que es lo caro), calibración del campo (`detector_campo.py` y `campo.py`: los 32 puntos clave de las líneas con
   el RF-DETR de puntos a 768 px → homografía con RANSAC y error de reproyección; por fotograma o única), filtro de personas fuera del campo y proyección de posiciones.
3. **Trayectoria del balón** (`balon.py`): primero se quitan los **objetos fijos** que el detector toma por el balón
   (un cono, una bolsa o una botella junto a la línea): sitios donde el «balón» se queda quieto al menos cuatro veces
   a lo largo de diez minutos o más, fuera de los puntos de saque; dentro de su radio, mientras está en el césped, solo vale
   el balón que va de paso (visto fuera del radio a menos de 0,6 s), aunque haya un jugador al lado. Luego los avistamientos sueltos (flojos, lejos de todos los jugadores y
   sin otro a menos de 1,5 s) y se rellenan por interpolación los huecos de hasta 2 s entre dos avistamientos con
   una velocidad plausible (en el campo si los dos extremos están proyectados, y siempre en la imagen). Desde la 2.3.0,
   con el campo calibrado en todo el hueco, **siguiendo a la cámara** y hasta 3 s: el punto se interpola en la imagen
   y se proyecta con la homografía de cada instante (la cámara sigue al balón; probado apartando trozos de balón visto,
   el error mediano en huecos de 2-3 s baja de 3,3 a 2,5 m y en los cortos, de 1,5 a 0,3 m). El balón de
   cada instante queda marcado como «visto» o «interpolado». Y desde la 2.3.0, **cuándo está el balón en juego**
   (`en_juego.py`): parado con el balón fuera del campo (hasta que se vuelve a ver dentro), quieto en el mismo sitio, o
   tres segundos o más sin verlo con los jugadores casi quietos (su movimiento en la imagen respecto al de todos, para
   que no cuente el paneo, y en altos de su caja por segundo), además de lo que ya paran los eventos. Mirado a ojo en
   24 momentos del J8, acierta en 21. Lo que se mide del balón (visto, con trayectoria) se cuenta sobre ese tiempo, y la
   posesión solo reparte el tiempo en juego.
4. **Posesión** (`posesion.py`): la máquina de «jugador más cercano al balón con histéresis» (un cambio de poseedor se
   confirma tras 0,2 s seguidos del mismo candidato, medidos en tiempo y no en muestras, para que la cadencia no la
   cambie) corre sobre esa
   trayectoria, y el reparto cuenta **solo el tiempo de juego activo con balón visto o interpolado**; tras más de 2 s
   sin balón ya no se sabe de quién es hasta el siguiente toque. Sale con su confianza: **alta** si hay balón en el
   60 % del tramo y se reparte al menos la mitad, **media** a partir del 35 % y la cuarta parte, **baja** si no (el
   35 % es el mismo umbral con el que la app avisa de calidad baja). Con confianza baja la posesión se enseña pero
   la app no la vuelca al acta. Las **acciones de cada jugador** no salen de esta máquina sino de una más exigente
   (`MaquinaControl`, desde la 2.2.0): el balón tiene que verse con él (a menos de 2 m en 0,2 s seguidos, con el
   balón visto y sin cruzar de un lado a otro), para quitárselo a quien lo tiene hay que estar 0,5 m más cerca, un
   avistamiento más rápido que un tiro no es el balón y cada toque dice si empezó con el balón parado. Pase, pérdida,
   recuperación, parada y saque del portero, como en la tabla de arriba.
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
   cuenta. Luego, sobre los tracklets ya cerrados: (0) **porteros** (`porteros.py`): con el descanso y hacia dónde ataca
   cada equipo en cada parte (por sus jugadores de campo, `mitades.py`). **Quién es portero, por la equipación** (desde
   la 1.2.0; el detector solo marca «portero» una parte pequeña del tiempo): se aprende cómo visten los jugadores de
   campo de cada equipo y el árbitro (media y covarianza robustas de la equipación de sus tracklets; distancia de
   Mahalanobis) y, en cada parte, entre los tracklets que viven pegados a la portería que defiende un equipo (el 80 % de
   sus posiciones a menos de 22 m de la línea de gol y 25 m del eje) y no visten como ninguno de esos tres, el color más
   repetido es el de su portero (tiene que juntar el 60 % de ellos y 25 posiciones). Todo tracklet de ese color (a menos
   de 5, el mismo límite que el de los equipos y el árbitro, de los que está más lejos) que está en el lado que defiende
   ese equipo en cada parte en que se le ve es su portero, aunque el detector lo tome por un jugador o por el árbitro
   (el verde lima de un portero y el verde menta del árbitro se le confunden); sin campo calibrado, solo si el color
   vale para un solo portero y con 6 muestras. **Solo en el rato en que viste como él**: cuando un jugador pasa pegado
   al portero, el seguidor salta a veces de uno a otro y el tracklet empieza en un defensa y sigue en el portero, o al
   revés (por la camiseta no se parte: la del portero no dice su equipo). Cuenta del primer par de muestras seguidas más
   cerca de su color que del de los dos equipos y el árbitro al último; lo de fuera pasa a otro tracklet, sin equipo, y
   una muestra suelta en medio es el mismo portero agachado, borroso o tapado. Dos o más seguidas en medio también se
   quitan (de la muestra anterior a la siguiente): o el seguidor saltó a otra persona y volvió al portero (en el J8, al
   menos cinco veces y hasta 4 s) o es el portero agachado o borroso, que por el color no se distingue. Si el seguidor
   saltó (dos muestras o más fuera de ese rato en un extremo, o un hueco en medio), de las lecturas del dorsal del
   tracklet solo cuentan las de su rato a 0,4 s o más de cada corte (`MARGEN_CORTE_S`, dos fotogramas de los
   analizados; cada lectura guarda su segundo en `HistorialLecturas`): las de fuera pueden ser del otro y, en el momento
   del salto, la caja puede llevar a los dos. Sin el segundo de cada muestra no se sabe dónde se cortó y no cuenta
   ninguna. El que nunca viste como él dos muestras seguidas se queda sin equipo. Y al revés: del tracklet de un
   jugador de campo que empieza, acaba o pasa en medio por un portero (dos muestras o más) se
   quita ese rato, y el que viste como un portero de principio a fin sin que se le haya podido colocar se queda sin
   equipo. Lo que se parece a un portero pero no se puede colocar (en el lado que no le toca o con muy pocas muestras)
   se queda sin equipo. El que el detector marca de portero pero viste como un equipo es un jugador de ese equipo; como
   el árbitro, sin equipo; y si no se sabe, como hasta la 1.1.0, del equipo que defiende la portería de su lado **en esa
   parte** (en el descanso cambian de campo; el lado, por la mediana de su x; en la franja central, el 15 % del largo a
   cada lado del medio, o sin campo calibrado, sin equipo), con el mismo recorte por el color del portero de esa
   portería si se sabe (el que no se le parece en ningún momento es un jugador de campo que el detector tomó por
   portero: sin equipo). **Nunca vestido como los de campo** (desde la 1.3.0): en el rato que se le da, ni el portero
   por la equipación ni el del detector pueden vestir como los jugadores de campo de ningún equipo ni como el árbitro, y
   el del detector con menos de 3 muestras de su equipación no se puede comprobar y se queda sin equipo (hasta la 1.2.0
   el rival que remataba en el área, con 2 muestras y sin dorsal visible, acababa en la línea de nuestro portero). Sin
   bastantes tracklets de los dos equipos para saber cómo visten, solo cuentan los que marca el detector. En el J8 del Vallobín el detector marcaba de portero unos 11 de los 96 minutos: el del Real Titánico iba
   de morado y el del Vallobín de verde lima, y casi siempre los tomaba por jugadores; con la 1.2.0 el 13 del Real
   Titánico queda identificado el 96 % del tiempo en que se le ve y el 1 del Vallobín, por descarte, el 98 %, sin ningún
   tracklet de otro en sus líneas (con la 1.1.0, el 13 salía con 0 minutos y el 1 no salía). Todos los tramos de portero
   de un equipo, los de las dos partes, son la misma persona mientras no lo cambien: su dorsal sale de las lecturas de
   todos juntos (de lejos se le lee poco el número; pide 1,5 de evidencia; desde la 2.0.0 el lector afinado ha
   aprendido también de los porteros, de espaldas con su número y de frente sin él, y el escudo del pecho ya no se lee
   como un número: «Lector de dorsales que lee a los porteros»), con el acta solo entre los de su equipo que
   estaban en el campo (al menos el 40 % del tiempo en que se le ve) y **sin los números ocupados**: antes se resuelven
   los jugadores de campo solos, y el dorsal que dicen con claridad cadenas que se ven a la vez que el portero 15 s o
   más es de otro. Y al revés, el dorsal del portero que se reconoce por la equipación no pasa a ninguna cadena de campo
   de su equipo, ni por las lecturas ni por continuidad: por la equipación no es él, y un defensa al que se lee su
   número cuando al portero no se le ve acabaría en su línea. Si el acta dice quién es portero (`"portero": true` en
   cada jugador; la app aún no lo manda), su dorsal solo puede ser uno de ellos. Si las lecturas no bastan y está el
   acta, **por descarte**: el único de su equipo que estuvo en el campo todo el tiempo en que se ve al portero y no es
   ningún jugador de campo identificado (visto un minuto o más); si queda más de uno, ninguno (la línea del acta
   propuesta sale con la confianza del dorsal baja). Desde la 1.3.0, sin leerle el número el descarte solo vale si **la
   equipación y la posición lo confirman**: cuentan solo sus tramos por la equipación (un minuto como poco) y tienen que
   estar al menos un minuto, con el campo calibrado, al lado o debajo de su portería; la línea por descarte no se queda
   con los tramos que solo marca el detector. **Junto a su portería** (desde la 1.3.0): en cada parte en que a los
   tramos del portero de un equipo se les ve con el campo calibrado 20 s o más, al menos el 70 % de ese tiempo tienen
   que estar en la zona del portero de la portería que defiende su equipo (22 m de la línea de gol y 25 m del eje); la
   parte que no llega no es suya y sus tramos se quedan sin identificar. Se mide sobre toda la parte y no tramo a tramo:
   en el J8 los dos porteros pasan ahí del 84 al 98 % de cada parte, pero salen a sacar faltas hasta a 40 m y la
   calibración a veces se va lejos un momento. El tramo del detector que coincide con otro de su equipo (dos porteros a
   la vez no puede ser) o que dice con claridad otro número se resuelve como uno de campo; el del portero por la
   equipación es el portero aunque se le lea otro número y, si no se identifica a ningún portero, se queda sin
   identificar (como jugador de campo, sus dorsales mal leídos lo juntarían con los defensas). Desde la 2.1.1, igual el
   del detector que en su rato viste como el portero de su equipo (`Porteros.vestidos`): si no es de ningún portero
   identificado, sin identificar, nunca en la línea de un jugador de campo (la continuidad lo juntaba con el que pasaba
   a su lado). Si lo cambian, el que
   entra se queda con los tramos de fuera de los minutos del primero. **El recorte del portero** (desde la 1.3.0) no es
   el más grande de sus tracklets, que puede ser de cuando el seguidor saltó a otra persona (en el J8 la línea del 1 del
   Vallobín salía con un jugador del Real Titánico), sino el de un momento en que viste como él dentro de su rato,
   leído del vídeo: de pie, de los cinco en que se le ve más alto, el más nítido; si no hay ninguno (por ejemplo, si no
   se sabe cómo viste el portero de su equipo), el recorte en que mejor se le leyó su dorsal en un tracklet en el que el
   seguidor no saltó a otra persona; si tampoco, sin recorte. Esto vale para la línea que sale de los tramos de un
   portero (`Identidad.portero`): el jugador de campo con un tracklet que el detector tomó por portero lleva la foto de
   su dorsal, como los demás. En un vídeo de una sola parte no se busca
   descanso; (1) **cadenas** de tracklets del mismo equipo que empiezan justo donde y cuando acaba otro (≤ 1 s,
   velocidad posible), nunca dos que dicen dorsales distintos; (2) **el dorsal de cada cadena** por la suma de sus
   lecturas, con el acta solo entre los que jugaron y en sus minutos (± 5 min por el desfase de relojes); el lado del
   acta (local o visitante) de cada equipo del vídeo sale de en qué reparto caen más lecturas; (3) **un dorsal, una
   persona a la vez**: si dos cadenas lo dicen a la vez, la más débil prueba con su segundo número; (4) **continuidad**
   (apagada: medida con el vídeo del Vallobín fallaba más que acertaba): una cadena sin número que encaja en el hueco de
   un solo jugador sería ese jugador; (5) **aspecto** (desde la 2.3.0, `apariencia.py`): de cada jugador sin tapar
   de 40 px o más, un recorte de cada dos fotogramas analizados pasa por SigLIP (google/siglip-base-patch16-224,
   Apache-2.0, solo la parte de la imagen, en `modelos/siglip`) y cada tracklet suma sus vectores de 768 números. Por
   equipo, una regresión logística (vectores estandarizados) aprende el aspecto de los jugadores identificados por el
   dorsal y se prueba antes apartando cadenas identificadas por bloques de tiempo: se usa con el umbral más bajo con el
   que acierta en el 95 % de las que da (y da al menos 10), y si no hay ninguno, no se usa. Entonces una cadena sin
   número es el jugador que sale con esa probabilidad (y 0,2 más que el segundo) si podía serlo: en el campo según el
   acta, sin verse a la vez en otro sitio y llegando a tiempo desde donde se le vio antes y hasta donde se le ve
   después. Fuera las que no se parecen a sí mismas (coherencia, el parecido medio entre sus recortes, por debajo del
   percentil 10 de las identificadas: el seguidor saltó a otra persona) y las que por el aspecto no son de su equipo
   (menos de 0,9; la camiseta del otro equipo o la del árbitro que el color medio dejó en este). Lo que no tiene número
   leído ni aspecto claro queda sin identificar. Como mucho 16 por equipo (once
   y los cambios; con el acta, los que jugaron). Desde ahí cada jugador identificado tiene un único identificador
   (`900000 + equipo·1000 + dorsal`) y la posesión, los pases y las acciones salen por jugador. Desde la 2.3.0 el
   seguidor recuerda a un jugador perdido 1,5 s con cámara móvil (antes 10 s, en los que se iba con otro) y parte los
   tracklets de dos del mismo equipo que se montan (IoU 0,3) al separarse: el tramo que nace de un cruce solo se enlaza
   con otro si los dos dicen con claridad el mismo dorsal. Y el dorsal de los que están junto al balón (hasta 3, con los
   pies a menos de dos altos de su caja) se vuelve a leer en el fotograma de la mitad entre dos analizados, con su caja a
   mitad de camino: a 0,1 s la postura ya es otra.
7. **Modelo de lenguaje** (opcional, `llm/`): confirma eventos, lee dorsales, redacta.
8. **Informes** (`informes.py`). `acta-propuesta.json` es la versión 2 cuando hay identificación: una línea por
   jugador identificado con `dorsal`, `nombreActa`, `confianzaDorsal` (parte de las lecturas que dicen ese número) y
   `tramos`, el `lado` de cada equipo y `calidad.sinIdentificarS`. Cada equipo trae `foulsCommitted`/`foulsReceived` y
   `offsidesFor`/`offsidesAgainst`; cada jugador, `fouls`, `foulsReceived` y `offsides`, y con el acta cerrada
   `minutos` (por descarte) y sus goles del acta. `actaOficial` resume el acta que se usó (resultado, si está cerrada y
   los minutos y goles de cada uno de los dos lados). Desde la 2.1.2, junto a `propuesta` y a `teamStats` van los
   `momentos`: por cada clave (`passes`, `passesFailed`, `ballLosses`, `recoveries`, `shots`, `shotsOnTarget`, `goals`,
   `saves`, `fouls`, `foulsReceived` y `offsides` del jugador; `shots`, `shotsOnTarget`, `goals`, `corners`, `throwIns`,
   `goalKicks`, `fouls` y `offsides` del equipo), la lista ordenada de segundos del archivo de vídeo, el mismo que
   reproduce la app (los tiempos son fotograma / fps desde el principio del archivo, también con `--segmento`). El pase
   y la pérdida son el final del toque de quien lo da; la recuperación, el principio del toque de quien se lo lleva; el
   saque de banda, el córner y el saque de puerta, cuando el balón sale; la falta, cuando se para el juego, y el fuera
   de juego, el pase al adelantado.

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
  la vuelca), `analisis.json` (quién analizó y con qué modelos, fotogramas, % con balón visto y % calibrados, tramo y medidas del
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

## El visor del Ojeador (25-11-2026)

Un programa aparte para ver, en este equipo y en su propia ventana, lo que hizo el analizador con un vídeo: el mismo
resultado que recibe la app, pero encima del vídeo. No forma parte de la app ni la toca: la app sigue lanzando
`tt-analisis analizar` como hasta ahora y el visor solo lee las carpetas de salida.

**Cómo se abre**

- Doble clic en `backend\Visor-Ojeador.cmd`, o `.\Start-Visor.ps1` desde `backend` (por defecto, los análisis de
  FakeDatos; `-Environment Development`, `-Folder <carpeta>` para otra y `-NoWindow` para abrirlo en el navegador de
  siempre). Busca los análisis que deja el analizador de la app en el volumen de Docker (`videos\analysis\<vídeo>`, por
  `\\wsl.localhost`) y los de prueba de `salidas\`.
- Desde aquí: `uv run tt-analisis visor [carpetas...] [--video ruta] [--puerto N] [--sin-ventana]`. Cada carpeta puede
  ser la de un análisis (se abre directamente) o una que los contenga (hasta tres niveles por debajo).
- Abre Edge (o Chrome) en modo aplicación, con un perfil propio, y se cierra al cerrar la ventana. El servidor solo
  escucha en 127.0.0.1 y solo atiende a la propia página del visor: ni a otras webs abiertas en el navegador ni a
  nombres que apunten a 127.0.0.1.
- Un análisis hecho antes del 25-11-2026 no tiene repetición: sale en la lista, pero para verlo hay que volver a
  analizar el vídeo («Volver a analizar» en el acta interna). Si no encuentra el vídeo, pide su ruta.

**Qué enseña**

- Encima del vídeo, sincronizado fotograma a fotograma: cada persona con su recuadro (del color de su equipo;
  discontinuo si no se le identificó), su dorsal y cómo se le identificó (A, por el aspecto; C, por continuidad; D, por
  descarte; sin letra, por el dorsal leído), el balón (visto, visto en un fotograma vecino o interpolado) con su estela,
  quién lo tiene, las líneas del campo como las calibró el analizador y un minimapa con los pies de cada uno llevados al
  campo y lo que abarca la cámara. Con «Depuración», los tracklets, las cadenas y los balones descartados.
- Las cajas se interpolan entre los fotogramas analizados (5 por segundo) por su tracklet, y el balón entre
  avistamientos cercanos: lo dibujado es lo que vio el analizador, no una detección nueva.
- Dos líneas de tiempo, los 30 s de alrededor y todo el tramo: balón, en juego, posesión, calibración, toques y cada
  acción que cuenta o jugada. Se salta con un clic.
- Un panel: «Estadísticas» hasta el segundo en que va el vídeo, contadas igual que el analizador (posesión, balón en
  juego, visto y con trayectoria, lo que cuenta para el acta y las jugadas); «Jugadores» (ficha con foto, cómo se le
  identificó, el segundo de cada acción y su mapa de calor); «Jugadas» (todas, para saltar a cada una); «Análisis» (quién
  analizó, con qué modelos, las medidas y el JSON).
- Teclas: espacio; ←/→ (1 s; con Mayús, 5 s); `,` y `.` (fotograma analizado anterior y siguiente); `[` y `]` (desfase
  de un fotograma si vídeo y dibujo no fueran a la par; `0` lo quita); la inicial de cada capa (J, E, B, T, P, C, M, D)
  y Esc.

**Mientras se analiza** (desde el 25-11-2026)

- Un análisis que se está haciendo sale en la lista como «Analizando», con la etapa en que va (los equipos por la
  camiseta, la pasada con su porcentaje, quién es quién, las jugadas...), y se puede abrir sin esperar a que termine.
  La lista se pone al día sola cada 4 s y lo abierto, cada segundo y medio.
- Durante la pasada principal se ve lo que lleva hecho: cada persona con su recuadro, del color del equipo que le da
  la camiseta hasta ese momento, y el número de su tramo del seguidor (`#12`: quién es quién y los porteros se deciden
  al terminar); el balón visto en su fotograma o en uno vecino y las líneas del campo donde ya está calibrado. En las
  líneas de tiempo, lo que falta sale rayado («Sin analizar todavía»).
- Si el vídeo alcanza a lo analizado, espera («Esperando al analizador») y sigue en cuanto hay más; «Lo último» (o `L`)
  lleva a lo último analizado. Al acabar la pasada ya no espera.
- El panel cambia: «Estadísticas» dice hasta dónde va, a qué ritmo (veces la velocidad del vídeo y lo que le queda de
  pasada), el balón visto y el campo calibrado hasta ahí, cuántos tramos lleva el seguidor, lo que llega al terminar y
  lo último que ha dicho el analizador; «Jugadores», el tramo pinchado en el vídeo; «Análisis», quién analiza y el vídeo.
- Al terminar, el visor pasa solo a la repetición definitiva (quién es quién, posesión, jugadas y lo que cuenta para el
  acta), sin recargar y con el vídeo donde estaba.
- Si se para (falla, se corta el proceso o el equipo se duerme: 30 s sin noticias), lo dice con el porqué. Si en la
  carpeta había un análisis anterior terminado, vuelve a verse ese: uno vuelto a lanzar en la misma carpeta lo tapa
  solo mientras se hace, y la página abierta pasa sola al nuevo. Si lo provisional desaparece (el analizador dejó de
  escribirlo o se vació la carpeta), dice «Ya no llega nada en directo» y sigue mirando por si termina.

**La repetición** (`repeticion/`, formato 1; el detalle, al principio de `repeticion.py`)

- `fotogramas.jsonl`: una línea por fotograma analizado, con `t` lo primero para indexar el archivo sin leerlo entero.
  Lleva cada persona (id final, tracklet, clase, equipo, caja y, si el seguidor le dio otro tracklet, ese), el balón con
  su origen, lo que se vio y se descartó, el balón de los fotogramas vecinos y la homografía.
- `final.json`: lo del tramo entero. Los equipos, el modelo del campo y, por instante, el origen del balón, si está en
  juego, a quién se le cuenta la posesión, si está calibrado y la parte. Y los toques, los jugadores (con cómo se les
  identificó), los tracklets (cadena y método), las acciones con su segundo, los eventos y los objetos fijos.
- Ocupa unos 1,8 MB por cada 10 minutos (≈ 16 MB un partido) más unos 120 KB. Se va con la carpeta del análisis (la app
  la vacía antes de volver a analizar y al eliminar el vídeo).
- No cambia la propuesta: se escribe al final, leyendo lo ya calculado, y por eso la versión sigue siendo la 2.3.0.
  Comprobado en el J8 (20:00-30:00): posesión 42,7 %, balón en juego 79,6 %, visto 49,6 % y con trayectoria 81,0 %,
  igual en el visor que en `analisis.json`.
- Al comprobarlo, dos pasadas del mismo tramo (20:00-21:30) salieron distintas ya en la calibración del campo (199 y 176
  aciertos), antes de escribir nada de la repetición. Venía de antes, del motor TensorRT del campo, y se arregla en la
  2.3.1 (ver «Dos pasadas, el mismo resultado»).
- Mientras se analiza (`Directo`), dos archivos más que se borran al terminar. `directo.jsonl`, con el formato de
  `fotogramas.jsonl` y lo provisional de cada instante de la pasada: el tracklet del seguidor hace de id, el equipo es
  el de su voto hasta ese momento y va el balón visto, el de los fotogramas vecinos y la homografía. Cada instante se
  escribe al llegar el siguiente, para repartir los balones vecinos igual que la definitiva. Y `directo.json`, el
  estado: el analizador, el vídeo, la etapa, lo último que ha dicho y, desde la pasada, los equipos con sus colores, el
  campo, el paso entre instantes y los fotogramas hechos y por hacer; si falla, `error`. Se reescribe entero cada 2 s
  aunque no haya nada nuevo (también en pausa) y de golpe (archivo temporal y `os.replace`): el visor nunca lee uno a
  medias.
- Cuál se enseña: lo de en directo si `directo.json` es más nuevo que `final.json` (o no hay final) y tiene noticias de
  los últimos 30 s (o no hay final); si no, la definitiva. Al terminar bien, `directo.json` deja de escribirse antes de
  escribir la definitiva, y después se borra.
- Lo de en directo tampoco cambia la propuesta y la versión no sube: copia lo ya calculado en cada instante, no toca los
  generadores aleatorios y, si no puede escribir, se apaga (y quita lo provisional, para que el visor no dé por parado
  un análisis que sigue) y el análisis sigue igual. Lo prueba `tests/test_repeticion.py` pasando el analizador entero
  (detector, calibrador y equipos de prueba; el seguidor y todo lo de después de la pasada, los de verdad) con y sin
  repetición: `acta-propuesta.json` y `analisis.json` salen iguales, salvo los tiempos y la ruta de `repeticion/`.
- Probado el 25-11-2026 con un análisis de verdad (en CPU, 20:00-20:04 del Vallobín, con la GPU ocupada): sale en la
  lista desde que empieza, se ve avanzar, dice «Se ha parado» al cortarlo, la página pasa sola al relanzado en la misma
  carpeta y, al terminar, a la repetición definitiva.

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
el 18-11-2026. El modelo del aspecto (desde la 2.3.0) es SigLIP de Google (`google/siglip-base-patch16-224`, Apache
2.0), con `transformers` (Apache 2.0); en `modelos/siglip` va solo la parte de la imagen, guardada con
`SiglipVisionModel.save_pretrained` desde los pesos publicados.

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

## Lector de dorsales que lee a los porteros (24-11-2026, Ojeador 2.0.0)

En el J8 al portero casi no se le leía el número: de las lecturas de los tramos del 13 del Real Titánico (de morado)
solo el 2 % iban al 13 (el 8 y el 6, sus defensas, se llevaban dos tercios) y el 1 del Vallobín (de verde lima) salía
solo por descarte (0,3 de 61 lecturas). Tres causas, vistas en las zonas del dorsal de los dos porteros en todo el
partido:

- **El pecho se leía como un número**: el escudo ovalado del morado, el cuello y el escudo del lima. El afinado del
  16-11 veía un número con peso en el 28 % de las zonas de frente del 13 (el publicado, en el 11 %).
- **El afinado del 16-11 no sabía decir un 1 solo**: 0 de las 8 espaldas del 1 (el publicado, 7 de 8). No había
  visto ninguno: el 1 solo lo lleva el portero.
- **Al portero casi nunca se le ve de espaldas y grande**: en todo el partido, 17 zonas de espaldas del 13 y 8 del 1
  con 60 px o más (las porterías quedan lejos de la cámara), y el material del 16-11 no tenía ninguna, porque entonces
  no se identificaba a los porteros.

Qué cambia:

- `--guardar-dorsales` guarda también las zonas de los **porteros identificados** (`guardar_zonas_porteros`, al final
  de la pasada y solo con el acta), de los fotogramas en que se les ve dentro de su rato y solo con el dorsal de la
  línea del portero: las que algún lector (el que se usa o el publicado) lee con su número (probabilidad por peso
  ≥ 0,35) van con su número, y las que ninguno lee con nada de su número (< 0,1) y están a más de 4 s de cualquier
  rastro de él van **sin número** (es su pecho); nunca con alguien delante. Cada rato (sin verle 2 s empieza otro) es
  un grupo para la validación. Para eso vuelve a leer el vídeo seguido hasta el último momento del portero.
- `entrenar-dorsales` repite las zonas de los dorsales que casi no se ven hasta `--minimo-por-dorsal` (300; cada vez
  con otra variación, y cada zona como mucho 50 veces, por si alguna está mal etiquetada): unas pocas zonas entre
  veinte mil no se aprenden. En el J8, el 1 ×42 y el 13 ×30.
- Probado y descartado: añadir como «sin número» las zonas de jugadores de campo que el lector leía mal y con mucho
  peso. No mejoró nada.

```
uv run tt-analisis entrenar-dorsales salidas/zonas-00-20-v3 salidas/zonas-30-fin-v3 salidas/zonas-porteros-j8 --epocas 6
```

`salidas/zonas-porteros-j8` son las zonas de los porteros del J8 sin el tramo 20:00-30:00 (1.245: del 13, 10 de
espaldas y 842 sin número; del 1, 8 y 385). En la validación (tracklets apartados), las espaldas bien leídas quedan
igual (92,4 % el del 16-11, 92,2 % el nuevo) y las zonas sin número en que dice que no hay número, 94,0 % y 93,1 %.
Sobre las zonas del dorsal del J8 (en 20:00-30:00 ninguno de los dos ha visto nada):

| | Afinado del 16-11 | Afinado del 24-11 |
|---|---|---|
| 20:00-30:00, portero 13: parte de sus lecturas que van al 13 | 8 % | **31 %** |
| 20:00-30:00, portero 13: zonas de frente en las que lee un número | 27,9 % | **4,4 %** |
| 20:00-30:00, portero 13: espaldas bien leídas | 7 de 7 | 7 de 7 |
| Todo el partido, portero 1: espaldas bien leídas (las 8 están en el entrenamiento del nuevo) | 0 de 8 | 8 de 8 |
| Todo el partido, portero 1: parte de sus lecturas que van al 1 | 0,5 % | 19 % |
| 20:00-30:00, jugadores de campo: zonas con número bien leídas | 85,2 % | 83,7 % |
| 20:00-30:00, jugadores de campo: parte de sus lecturas que van a su dorsal | 73,0 % | 72,5 % |
| 20:00-30:00, jugadores de campo: confianza media por jugador | 0,693 | 0,694 |

Ojo: el 1 del Vallobín no sale de espaldas en 20:00-30:00, así que lo del 1 solo está medido con zonas que el nuevo ha
visto al entrenar; hasta otro partido no se sabe si lee un 1 en zonas nuevas. Los pesos del 16-11 quedan en
`modelos/afinados/descartados/dorsales-16-11.pt` por si hubiera que volver.

En la identificación, con las reglas de la 2.0.0 aplicadas a una pasada del J8 con cada lector (`TT_GUARDAR_TRAMOS=1`):

| | Afinado del 16-11 | Afinado del 24-11 |
|---|---|---|
| Todo el partido, línea del 13: lecturas de sus tramos que van al 13 | 2 % (7,9 de 372) | 23 % (25,6 de 112), el número más votado |
| Todo el partido, línea del 13: confianza (evidencia) | 0,03 (7,8) | **0,23 (24,6)** |
| Todo el partido, línea del 1 | por descarte (0,3 de 61 lecturas) | **por lecturas**: confianza 0,07 (evidencia 5,1) |
| Todo el partido, tiempo de los de campo con identidad (Real Titánico · Vallobín) | 33,5 % · 31,6 % | 34,2 % · 32,9 % |
| Todo el partido, confianza media de los de campo (Real Titánico · Vallobín) | 0,723 · 0,749 | 0,724 · 0,737 |

Ninguna línea de campo se queda con el 1 o el 13. Ningún jugador de campo pierde más de medio minuto, y el que más
gana es el 17 del Vallobín, con 1,7 minutos más. Ojo: en todo el partido, el nuevo ya ha visto al entrenar las zonas de
los porteros de fuera de 20:00-30:00, así que la medida justa es la del tramo apartado.

En la identificación del tramo apartado (una pasada solo de 20:00-30:00 con cada lector), ninguno de los dos identifica
al 13:
- **Por qué no:** todas las lecturas de su número están en un tracklet en que el seguidor saltó a otra persona, y las de
  esos tracklets no cuentan (regla de la 1.2.0).
- **Lo que se lee en ese tracklet:** con el nuevo, el 70 % va al 13 (7,9 de 11,3). Con el viejo, el 21 % (7,6 de 36,5),
  por detrás del 8 y del 6.
- **En los demás tracklets del portero:** lo leído baja de 39,5 a 8,4. Eran sobre todo números falsos leídos en su pecho.
- **En el partido entero:** con el lector nuevo, esa regla deja fuera el 24 % de lo que se lee al 13 y el 69 % de lo que
  se lee al 1.

La 2.1.0 recupera esas lecturas: guarda el segundo de cada una y cuenta solo las del rato en que el tracklet es el
portero (ver «Las lecturas del portero cuyo tracklet saltó»).

Los de campo en 20:00-30:00:

| | Afinado del 16-11 | Afinado del 24-11 | Afinado del 24-11, repetida |
|---|---|---|---|
| Real Titánico: tiempo con identidad (confianza) | 26,8 % (0,715) | 26,4 % (0,712) | 29,0 % (0,682) |
| Vallobín: tiempo con identidad (confianza) | 37,8 % (0,772) | 35,5 % (0,809) | 36,2 % (0,789) |

La diferencia entre los dos lectores es menor que la que sale al repetir la misma pasada con el mismo lector, que además
da 868 cadenas en vez de 854 y 117 toques de balón en vez de 127. Hasta la 2.3.1 la pasada no era determinista (por el
motor del campo, ver «Dos pasadas, el mismo resultado») y para comparar dos versiones en un tramo corto había que
repetirlo; desde la 2.3.1, la diferencia entre dos versiones en el mismo tramo es del cambio.

## Las lecturas del portero cuyo tracklet saltó (24-11-2026, Ojeador 2.1.0)

Hasta la 2.0.0, del tracklet de portero en que el seguidor saltó a otra persona no contaba ninguna lectura del dorsal
(regla de la 1.2.0): no se sabía en qué segundo se había leído cada una. En el J8 eso dejaba fuera el 24 % de lo que
se lee al 13 y el 69 % de lo que se lee al 1, y en el tramo 20:00-30:00, todas las lecturas del 13.

Qué cambia:

- Cada lectura del dorsal guarda su segundo, su tracklet, su equipo, su peso y sus 8 números más probables
  (`dorsales.HistorialLecturas`, unos 12 MB en memoria en un partido entero). Con `TT_GUARDAR_TRAMOS=1` van también a
  `porteros.pkl`.
- Del tracklet de portero mezclado cuentan las lecturas de su rato (en el que viste como él, sin los huecos en que no)
  a `MARGEN_CORTE_S` = 0,4 s o más de cada corte (`Porteros.lejos_de_los_cortes`). Si no se sabe dónde se cortó (sin
  el segundo de cada muestra de la equipación), no cuenta ninguna, como antes.

El margen se fijó mirando a ojo las zonas del dorsal de una pasada del tramo (`--guardar-dorsales`) y contando, en el
partido entero, las lecturas con probabilidad por peso de al menos 0,1 de los 76 tracklets de portero mezclados con
corte conocido:

- **Las lecturas de otra persona caen fuera del rato.** En el tramo, el 20 y el 12 dentro del hueco de un tracklet del
  13 y el 11 después de acabar el rato de otro. En el partido entero, las 48 lecturas limpias (0,8 o más) de otro
  número están todas fuera. Esas ya las quitan el rato y sus huecos, sin margen.
- **Dentro del rato no hay ninguna lectura limpia de otro número, tampoco junto al corte.** Las que leen otro número
  con poca seguridad (58 en el partido) son borrones del mismo portero (en el tramo, un 6, un 3 y un 1 sobre su
  camiseta morada) y salen por todo el rato, no junto al corte.
- **Con 1 s se perdían lecturas limpias del portero**: en el tramo, cinco del 13 entre 0 y 0,8 s después de un hueco,
  y el 6 le pasaba por delante. Con 0,4 s (dos fotogramas de los analizados) solo quedan fuera el fotograma del salto
  y el siguiente, en los que la caja puede llevar a los dos.

| J8, sobre la misma pasada | Sin las lecturas de los mezclados (2.0.0) | Margen 1 s | **Margen 0,4 s** | Margen 0 |
|---|---|---|---|---|
| Partido, línea del 13: evidencia (confianza) | 12,5 (0,143) | 14,5 (0,119) | **17,5 (0,136)** | 19,5 (0,147) |
| Partido, línea del 1: evidencia (confianza) | 5,1 (0,071) | 14,1 (0,150) | **15,6 (0,161)** | 15,6 (0,159) |
| Partido, jugadores de campo que cambian | | ninguno | ninguno | ninguno |
| 20:00-30:00, lecturas de los tramos del portero que van al 13 (al 6) | 0 (2,4): sale como el 6 | 2,0 (3,2) | **5,0 (4,2)** | 7,0 (4,4) |

La confianza del 13 apenas se mueve porque en su rato también se leen borrones como el 8 o el 6; la línea no cambia de
dorsal porque esos números los llevan a la vez jugadores de campo. En el tramo, con la regla nueva el 13 se queda sin
identificar (su número es el 30 % de lo leído) y el 6 recupera sus minutos (4,3): mejor sin identificar que equivocado.

Probado y sin hacer, **un piso para el peso de cada lectura**: no contar las de peso (1 − probabilidad de «sin
número») menor que 0,25, que son la mitad, casi todas de frente o de lado. Rehaciendo los votos desde las lecturas
guardadas, en el partido entero sube la confianza del 13 de 0,14 a 0,24 y la del 1 de 0,18 a 0,26, y el tiempo con
identidad de los de campo de 35,2 a 38,5 % (Real Titánico) y de 33,5 a 35,0 % (Vallobín); en el tramo identifica al
13. Pero cambia lo que deciden todos los umbrales. De los 30 tracklets que pasan a tener jugador en el tramo, mirados
a ojo, la mayoría aciertan y dos fallan: uno de 2 minutos en el que el seguidor mezcla al 3 y al 14 (lecturas: del 3,
70; del 14, 31) pasa a ser el 3 y le quita el sitio al 3 de verdad, y otro que lee 4 y 11 acaba en el 17 por
continuidad, unos 75 de los 412 s nuevos. Haría falta reajustar los umbrales con el piso y comprobarlo a ojo en un
partido entero.

Arreglado en la 2.1.1 (ya pasaba antes de la 2.1.0): cuando el portero de un equipo no salía identificado, los tramos
que solo marca el detector se resolvían como los de campo (regla de la 1.3.0), también los que visten como el portero
en su rato. En el tramo, sin el 13 identificado, 5 s del portero (de morado) acababan en la línea del 17 (de rojo y
blanco), y ahora quedan sin identificar. En el partido entero, con los dos porteros identificados, no cambia nada.

## El balón en juego y quién es quién por el aspecto (25-11-2026, Ojeador 2.3.0)

Diego quiere el balón como eje del análisis (que se vea o se sepa por dónde va el 80–90 % del juego) y a cada jugador
único por su dorsal o por su aspecto. Medido en el J8 (Vallobín - Real Titánico, vídeo a 50 fps), tramo 20:00-30:00,
como lo lanza la app (`--camara movil`, acta oficial con `--plantillas`, `--sin-clips`), sumando los cambios por tandas.
El balón en juego, igual en todas: `en_juego.py` sobre los instantes guardados, sin las paradas de los eventos (en la
pasada de la 2.3.0, con ellas, sale en juego el 79,5 %, visto el 48,8 % y con trayectoria el 80,0 %). «Visto» y «con
trayectoria» son sobre todos los fotogramas analizados; la pureza, la parte de las lecturas de cada tracklet que dicen su
dorsal más leído (baja si el seguidor se va con otro).

| Pasada | Visto | Con trayectoria | En juego | Visto en juego | Trayectoria en juego | Tiempo con identidad (equipo 0 / 1) | Pureza | Pasada |
|---|---|---|---|---|---|---|---|---|
| 2.2.0 | 34,1 % | 56,3 % | 78,8 % | 41,3 % | 68,6 % | 26 % / 36 % | 0,601 | 239 s |
| + vecinos sin tope, relleno con la cámara, memoria corta y cruces | 43,7 % | 71,5 % | 82,6 % | 49,6 % | 82,1 % | 29 % / 35 % | 0,628 | 452 s |
| + una sola imagen entera por hueco y relectura junto al balón | 42,3 % | 68,8 % | 81,0 % | 49,1 % | 80,5 % | 26 % / 35 % | 0,628 | 340 s |
| 2.3.0 (+ aspecto) | 42,0 % | 68,0 % | 80,1 % | 48,6 % | 79,8 % | 32 % / 42 % | 0,630 | 359 s |

- **Vecinos** (`vecinos_balon_s`, `vecinos_enteros`): al fotograma analizado en el que no se ve el balón se le busca en
  los de ±0,04 s (a 50 fps, el de cada lado): de un fotograma al siguiente cambian la borrosidad y quién lo tapa. Sin
  tope, 4.931 búsquedas en la imagen entera (el 62 % de las miradas, casi todo el coste añadido) completaban el 13 % de
  los instantes que miraban, frente al 28-38 % de las ventanas alrededor de un balón cercano, y la pasada casi se
  doblaba; con una sola imagen entera por hueco entre dos analizados quedan 1.382 y se pierde poco: en la 2.3.0, 231
  instantes sin balón lo tienen por sus vecinos.
- **Relleno siguiendo a la cámara**: apartando trozos de balón visto y rellenándolos, el error mediano en huecos de 2-3 s
  baja de 3,3 a 2,5 m y en los cortos, de 1,5 a 0,3 m; por eso, con el campo calibrado, el hueco que se rellena pasa de
  2 a 3 s.
- **Balón en juego**: mirado a ojo en 24 momentos, acierta en 21. Con trayectoria, el balón está en el 80 % del tiempo
  en juego (antes, el 68,6 %); visto, en la mitad. El 80–90 % se alcanza con la trayectoria; subir lo visto pide un
  detector del balón mejor, con más partidos (abajo).
- **Memoria corta y cruces**: con 10 s de memoria el seguidor se iba con otro jugador. Con 1,5 s y los tracklets
  partidos en los 286 cruces de dos del mismo equipo, la pureza sube de 0,601 a 0,628; una pasada igual sin esto daba la
  misma identidad con la pureza de antes.
- **Relectura junto al balón**: 1.120 lecturas más, de los hasta 3 que están junto al balón, en el fotograma de la mitad
  entre dos analizados. En el tramo no cambia lo identificado (17 toques de jugadores identificados frente a 16): se deja
  porque cuesta poco y suma lecturas donde está la jugada.
- **Aspecto** (SigLIP, `apariencia.py`): 17.651 recortes y 19 s más de pasada (+5 %). Probado en cada equipo apartando
  por bloques de tiempo las cadenas ya identificadas por el dorsal, el equipo 0 acierta 41 de 43 (umbral 0,97) y el 1,
  48 de 50 (0,95). Da su jugador a 19 y 17 cadenas sin dorsal (3:03 y 2:05 a la vista): el tiempo con identidad pasa
  del 26 al 32 % y del 35 al 42 %. En la primera prueba, sin los filtros de coherencia y de equipo, se colaban cadenas
  del árbitro, del otro equipo y de dos personas; con ellos, en la hoja de contactos de las 36 cadenas no hay ninguna del
  otro equipo ni del árbitro, y en cuatro se ve el número que se les dio.
- **Coste**: la pasada tarda un 50 % más que con la 2.2.0 (de 239 a 359 s en el tramo, con la RTX 5070 Ti); un partido
  entero sigue tardando menos de lo que dura.
- **Lo que no se ha hecho**: volver a entrenar el detector del balón, que es lo que subiría lo visto: solo hay vídeo del
  Vallobín, y la segunda tanda de etiquetado con él no mejoró el balón.

## Dos pasadas, el mismo resultado (25-11-2026, Ojeador 2.3.1)

Dos pasadas iguales del J8 (tramo 20:00-21:30, mismos argumentos, una detrás de otra) no daban lo mismo. Para saber
dónde se separaban, un arnés apuntó, llamada a llamada, una huella de lo que entra y lo que sale de cada pieza (los
fotogramas leídos, los tres detectores, el seguidor, la calibración, los equipos, el lector de dorsales y el del aspecto)
y se compararon dos pasadas:

- Los fotogramas, el detector de jugadores y el del balón dan exactamente lo mismo, bit a bit (450, 491 y 450 llamadas).
- **El detector de los puntos del campo, no**: con la misma imagen da otra cosa en los 450 fotogramas. La mitad de los
  puntos se mueve más de 1,9 px y alguno hasta 176, y 58 puntos cambian de lado del umbral de 0,5. Como la calibración
  arrastra lo anterior (la homografía que se conserva, los saltos que se rechazan, las que se invalidan), cada
  diferencia cambia los fotogramas siguientes y, con ellos, el balón en el campo, la posesión y los eventos.
- Solo, con el mismo fotograma tres veces seguidas en el mismo proceso (20 fotogramas del J8):

| Detector del campo | Repeticiones distintas | Por fotograma |
|---|---|---|
| TensorRT FP16 (el `campo.trt` de siempre) | 40 de 40 (hasta 136 px y 0,24 de confianza) | 8,4 ms |
| TensorRT FP32 (compilado aparte para probar) | 0 de 40 | 25,7 ms |
| PyTorch FP16 | 0 de 40 | 18,9 ms |
| PyTorch FP32 | 0 de 40 | 31,2 ms |

Los motores de jugadores y balón son deterministas; el del campo en FP16, no. Dentro del motor no se ha aislado qué
capa es; cambia algo de una llamada a otra con la misma entrada, que es lo que importa.

**Qué cambia** (`rfdetr_comun.py`): un motor TensorRT solo se usa si **da siempre lo mismo con la misma imagen**. Al
cargarlo se le pasa cuatro veces una imagen de ruido con semilla, con umbral casi cero para que salgan todas las
consultas (`da_siempre_lo_mismo`), y si alguna vez cambia algo, se usan los pesos de PyTorch y se dice. Con un fotograma
real o con un campo dibujado pasa lo mismo: la sonda pilla siempre al del campo y nunca a los otros dos. Al compilar un
motor (`exportar-<que>`) también se avisa de si el análisis lo va a usar, lo que importará al recompilarlos en otra GPU.

Hoy el del campo va con PyTorch FP16, y dos pasadas del mismo tramo salen idénticas: las 3.643 llamadas registradas,
`analisis.json` y `acta-propuesta.json` (solo cambian el tiempo de cálculo del resumen y la fecha que gzip pone en la
cabecera de `posiciones.csv.gz`). En el mismo equipo, el mismo vídeo da siempre lo mismo; en otra GPU o con otras
versiones de torch o de los controladores, el resultado puede ser otro, pero también fijo.

- **Calidad**: igual o algo mejor que el motor de antes. En las evaluaciones del 18-11, PyTorch daba más fotogramas
  calibrables en los tres conjuntos (67,7, 68,9 y 65,0 % frente a 64,1, 68,1 y 62,4 %) con prácticamente los mismos
  puntos correctos y falsos.
- **Coste**: unos 10 ms más por fotograma analizado, unos 5 minutos en un partido entero.

En el tramo 20:00-21:30 del J8:

| Pasada | Campo calibrado | Balón visto | Eventos | Posesión |
|---|---|---|---|---|
| 2.3.0, TensorRT, 1.ª | 83 % | 16 % | 4 | 61,6 / 38,4 |
| 2.3.0, TensorRT, 2.ª | 64 % | 17 % | 1 | 47,3 / 52,7 |
| 2.3.0, TensorRT, 3.ª | 68 % | 17 % | 4 | 41,0 / 59,0 |
| 2.3.0, TensorRT, 4.ª | 70 % | 18 % | 4 | 60,6 / 39,4 |
| **2.3.1, todas** | **77 %** | **15 %** | **5** | **37,0 / 63,0** |

Lo de la 2.3.1 no es «la buena»: es una pasada como las otras, pero ahora es siempre la misma. Por eso, desde la 2.3.1,
la diferencia entre dos versiones del analizador en el mismo tramo es del cambio y no de la pasada.

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
- **TensorRT** (`exportar-campo`): 7-10 ms por fotograma frente a 16 con PyTorch y unos 10 el YOLO. Desde la 2.3.1 no se
  usa: con la misma imagen no da siempre lo mismo, y el análisis va con PyTorch FP16 (ver «Dos pasadas, el mismo
  resultado»).

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
- Volver a afinar el lector de dorsales con las zonas de más partidos (hoy solo uno; `--guardar-dorsales` ya guarda
  también las de los porteros). Desde la 2.3.0 el aspecto da su jugador a parte de los tramos sin número (en el tramo
  medido, del 26-35 % al 32-42 % del tiempo con identidad): probarlo en más partidos (de noche, con equipaciones
  parecidas) y bajar su umbral si la prueba lo permite.
- El balón: con trayectoria ya está el 80 % del tiempo en juego, pero visto, solo la mitad; subirlo pide volver a
  entrenar su detector con más partidos (y varios fotogramas seguidos) o analizar más fotogramas por segundo junto a él.
- Condiciones de grabación en el manual y aviso en la subida cuando el encuadre no sirve.
- Árbitros y porteros: no fiarse solo del voto de clase del detector; un «árbitro» cuya camiseta es claramente la de un
  equipo es un jugador. Y revisar a mano una muestra de personas (sobre todo árbitros y porteros) para la segunda
  ronda del detector de jugadores, con más partidos.
- Eventos: un gol o un tiro candidato repetido a pocos segundos en la misma jugada debería quedar en uno.
- Faltas: más cobertura sin perder precisión (el balón quieto se ve poco); probar el silbato en el audio y la postura
  de los jugadores (uno en el suelo) para confirmar las paradas por frenazo.
- Dorsales: un piso para el peso de cada lectura, reajustando con él los umbrales de la identificación (sin reajustar
  sube la cobertura pero mete errores; ver «Las lecturas del portero cuyo tracklet saltó»).
- El campo otra vez con TensorRT (unos 10 ms por fotograma analizado): hace falta un motor que pase la sonda y vaya más
  rápido que PyTorch FP16. En FP32 la pasa, pero tarda 25,7 ms; queda probar FP16 con solo algunas capas en FP32 o una
  versión nueva de TensorRT (ver «Dos pasadas, el mismo resultado»).
- Lanzar el análisis desde el propio visor, con los mismos argumentos que la app (`UvVideoAnalyzer.Arguments`); verlo
  mientras se hace ya se puede (ver «El visor del Ojeador»).
