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
[`docs/lector-videos.md`](../../docs/lector-videos.md).

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
`--muestras-equipos` (600), `--max-fotogramas N` (para probar), `--device cpu`.

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

1. **Equipos**: una pasada rápida por el tramo recoge recortes de jugadores y ajusta el clasificador de `sports`
   (SigLIP → UMAP → KMeans, dos grupos). Los porteros van con el equipo cuyo grueso tienen más cerca.
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

## Siguientes pasos

- Afinar los detectores con 1-2 partidos asturianos anotados (cajas de jugadores y balón): los modelos públicos están
  entrenados con televisión.
- Enganche con la API: cola tras la compresión de `TercerTiempo.Videos`, entidad `MatchVideoAnalysis`, endpoint de
  análisis y pestaña «Del vídeo» en el acta interna con la asignación de tracklets a fichas.
- Unir tracklets de la misma persona (re-identificación) y leer dorsales con un modelo específico.
- Condiciones de grabación en el manual y aviso en la subida cuando el encuadre no sirve.
