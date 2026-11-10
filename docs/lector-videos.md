# Lector de vídeos: del vídeo del partido al acta interna

Evaluación hecha el 5 de noviembre de 2026 en la rama `lectorVideos`, antes de escribir una línea de código. La idea:
el club sube el vídeo del partido (ya lo hace), el programa lo analiza fotograma a fotograma, saca lo relevante a un
archivo (JSON + un resumen en Markdown) y lo convierte en el acta interna, para no meterla a mano. La posesión, por
ejemplo, sale mucho mejor del vídeo que a ojo.

## 1. Veredicto corto

- **Es viable y es lo que hace el mercado**, pero no como «un modelo de IA que mira el vídeo». Ni Claude ni ningún
  modelo local «ve» 90 minutos y devuelve la posesión: lo que funciona es una **cadena de visión por computador**
  (detectar jugadores y balón, seguirlos, saber de qué equipo es cada uno, proyectar sus posiciones al campo) y, sobre
  esos números, reglas y modelos de eventos. Es exactamente la arquitectura de Veo, Pixellot, SkillCorner o de los
  retos SoccerNet.
- **Lo que sale bien del vídeo**: posesión (%), posesión por zonas, mapas de calor, distancias, tiros y disparos a
  puerta (candidatos), córners, saques de banda, goles (con confirmación), tramos de dominio. **Lo que sale regular**:
  pases completados, recuperaciones, pérdidas, faltas. **Lo que sale mal o no sale**: tarjetas, asistencias, minutos
  por jugador y cambios, y sobre todo *quién es quién* (los dorsales en vídeo amateur a 1080p desde la banda no se
  leen; el mejor método del reto SoccerNet ronda el 85 % de aciertos por jugador... con vídeo de televisión).
- **Los LLM (la API de Claude o Ollama) tienen su sitio, pero no en el fotograma a fotograma**: en confirmar eventos
  dudosos sobre unos pocos fotogramas, en leer dorsales en recortes, en redactar el resumen y en volcar el análisis al
  acta. Pasar los 10 000 fotogramas de un partido por un modelo multimodal es caro, lento y, lo peor, no da seguimiento
  (no sabe que el jugador del fotograma 100 es el del 101), así que no puede calcular posesión.
- **Recomendación**: un servicio nuevo, `TercerTiempo.Analisis`, en Python con GPU, que se encadena detrás de la
  compresión de `TercerTiempo.Videos` y produce un **acta propuesta** que el cuerpo técnico revisa y confirma (igual
  que hoy el jugador «sugiere» y el técnico aprueba). Nunca se escribe el acta sola: el técnico asigna una vez cada
  «jugador detectado» a una ficha y corrige lo que haga falta. Empezar por una prueba de concepto con un partido real
  del Vallobín para medir qué precisión da con la cámara que tienen.

## 2. Lo que hay hoy en Tercer Tiempo

- **Vídeo**: subida por trozos hasta 20 GB (`MatchVideo`), compresión con ffmpeg sin bajar de 1080p en el servicio
  aparte `TercerTiempo.Videos` (`VideoProcessingWorker` coge de la cola y comprime de uno en uno). Ahí está el punto de
  enganche natural: cuando acaba la compresión, el vídeo entra en la cola de análisis.
- **Acta interna**: por jugador (`MatchSheetEntryInput`: jugó, titular, minutos, goles, asistencias, tiros, tiros a
  puerta, pases clave, entradas, intercepciones, paradas, tarjetas, faltas, pérdidas, recuperaciones, dorsal del
  partido, notas, nota) y del equipo (`TeamMatchStatsInput`: **posesión %**, córners, tiros, tiros a puerta, fueras de
  juego, faltas, goles a balón parado, penaltis, saques de banda, notas). Las sugerencias de los jugadores ya pasan por
  una revisión del técnico: el acta propuesta por el vídeo encaja en ese mismo patrón.
- **Avisos y trabajos**: campanita para cualquier cuenta y `JobRuns` / «Descargas en directo» para ver qué está
  corriendo: el análisis de un vídeo se enseña ahí como un trabajo más.

## 3. Qué dato sale del vídeo y con qué fiabilidad

Con las condiciones de grabación «buenas» (cámara fija lateral a media altura, a la altura del centro del campo,
1080p a 25 fps, todo el campo encuadrado; es lo que graban las cámaras tipo Veo). Con un móvil desde detrás de la
portería o siguiendo el balón a mano, todo baja un escalón.

| Dato | Cómo se obtiene | Fiabilidad esperada | Va al acta |
|---|---|---|---|
| Posesión % (equipo) | Jugador más cercano al balón, con histéresis, sobre el seguimiento | Alta (±3-5 puntos; el proxy estándar de la industria) | `PossessionPct` |
| Posesión por zonas / mapa de calor | Posiciones proyectadas al campo (homografía) | Alta | Nuevo (no está en el acta) |
| Distancia y velocidad por jugador | Posiciones proyectadas por segundo | Media-alta (depende de la calibración) | Nuevo |
| Tiros y tiros a puerta (equipo) | Trayectoria del balón hacia la portería + evento «shot» | Media-alta como candidatos; el técnico confirma | `ShotsFor/Against`, `ShotsOnTarget…` |
| Goles | Balón que cruza la línea + parón + celebración; se cruza con el acta oficial (que ya tenemos) | Alta con confirmación | Goles por jugador tras asignar |
| Córners, saques de banda, saques de puerta | Posición del balón parado + reanudación | Media-alta | `CornersFor/Against`, `ThrowIns…` |
| Faltas | Parón del juego + pose del árbitro + reanudación con balón parado | Media-baja | `FoulsCommitted/Received` |
| Tarjetas | Gesto del árbitro (brazo arriba) + color | Baja a 1080p desde la banda | Mejor del acta oficial |
| Fueras de juego | Parón + banderín del asistente | Baja | Mejor del acta oficial |
| Pases y pases clave | Cambios de posesión entre compañeros | Media (los pases cortos se pierden si el balón se tapa) | `KeyPasses` solo como propuesta |
| Recuperaciones, pérdidas, entradas, intercepciones | Cambios de posesión entre rivales | Media-baja | Propuesta, el técnico ajusta |
| Minutos jugados, titulares, cambios | Entradas y salidas de un jugador del campo | Media (los cambios se ven; identificar a quién, no) | Propuesta tras asignar |
| Quién es quién (dorsales) | OCR del dorsal sobre el recorte del jugador, agregado por tracklet | Baja-media en amateur (85 % en TV) | El técnico asigna una vez por partido |
| Paradas del portero | Tiro a puerta + balón que no entra + portero más cercano | Media | `Saves` |
| Nota del jugador | No sale del vídeo | — | El técnico |

Conclusión de la tabla: **lo colectivo (equipo) sale casi solo; lo individual necesita al técnico**, sobre todo para
decir quién es cada jugador detectado. Por eso la pieza clave de interfaz es «asignar tracklets a fichas»: el programa
enseña a cada jugador seguido con su recorte, su mapa de calor y sus números, y el técnico dice «ese es Pelayo».

## 4. Qué hace el mercado (noviembre de 2026)

| Producto | Qué es | Cómo saca los datos | Precio orientativo |
|---|---|---|---|
| **Veo Cam 3 + Veo Analytics 2** | Cámara automática + análisis en la nube; desde agosto de 2026 da datos por jugador, físicos, pases y defensa, mapas de calor y posesión | Detección + seguimiento + calibración con su propia cámara (dos lentes, panorámica) | Cámara ≈ 999 € y análisis 45-165 €/mes según equipo (precios de terceros, comprobar) |
| **Hudl Focus + Hudl Assist** | Cámara fija con IA; las estadísticas son un servicio aparte (Assist) en el que personas y modelos etiquetan el partido | Mezcla de IA y etiquetado humano | «Contactar» |
| **Pixellot Air** | Cámara automática para clubes y federaciones; vídeo etiquetado y resúmenes | Detección y seguimiento con su cámara | «Contactar» |
| **Spiideo Perform** | Cámaras panorámicas y análisis en la nube | Igual | ≈ 3 000 $/año (listado de terceros) |
| **Nacsport, Once Video Analyser, Metrica Play, LongoMatch** | Herramientas de etiquetado manual (el analista marca los eventos sobre el vídeo); Metrica añade seguimiento automático en planes de pago | Manual con ayudas | Desde gratis hasta cientos de €/año |
| **SkillCorner, Stats Perform (Opta Vision), TRACAB, Second Spectrum** | Seguimiento profesional desde la señal de TV o cámaras propias en estadios | Pipelines propietarias de visión | Fuera de alcance para amateur |
| **Twelve Labs (Pegasus/Marengo), Gemini** | APIs generales de «entender vídeo»: describen, buscan y dan marcas de tiempo | Modelos multimodales; sin seguimiento métrico | Twelve Labs ≈ 0,02-0,04 $/min analizado (cifras de terceros); Gemini por tokens de vídeo |

Dos lecciones: (1) nadie saca las estadísticas «leyendo» el vídeo con un modelo de lenguaje: todos detectan, siguen y
calibran; (2) los que dan datos por jugador controlan la cámara (ángulo, altura, resolución). Nosotros no controlamos
la cámara del club, así que hay que fijar unas **condiciones mínimas de grabación** y medir con ellas.

## 5. Las tres formas de hacerlo

### A. Un modelo multimodal mira el vídeo (Claude API, Gemini, Ollama)

**API de Claude** (comprobado en la documentación el 05-11-2026):

- **No admite vídeo**: solo imágenes JPEG, PNG, GIF y WebP (de un GIF usa el primer fotograma). Hay que extraer
  fotogramas con ffmpeg y mandarlos como imágenes.
- Hasta **600 imágenes por petición** en los modelos de 1M de contexto (100 en los de 200k), 10 MB por imagen y
  128 MB por petición; por encima de 20 imágenes en una petición, ninguna puede pasar de 3 000 px de lado. Con muchas
  imágenes, subirlas con la Files API y referenciarlas por `file_id`.
- Coste por imagen: `⌈ancho/28⌉ × ⌈alto/28⌉` tokens. Un fotograma 1920×1080 son **1 560 tokens** en el nivel estándar y
  **2 691** en el nivel de alta resolución (Claude 4.7 y posteriores lo aplican solos). Precios de entrada por millón de
  tokens: Opus 5.5 4 $, Sonnet 5.5 2 $, Haiku 5.5 0,10 $ (hasta 100k tokens de prompt).
- Cuentas para un partido de 90 minutos a 2 fotogramas por segundo (10 800 fotogramas, ≈ 17-29 millones de tokens):
  Haiku 5.5 ≈ 2-3 $, Sonnet 5.5 ≈ 35-60 $, Opus 5.5 ≈ 70-120 $ **por partido**, y aun así el modelo no sigue a los
  jugadores entre fotogramas, así que no da posesión ni distancias. Para lo que sí vale (apartado C) el volumen es de
  unos cientos de fotogramas: céntimos con Haiku, pocos euros con Sonnet u Opus.

**Gemini** sí admite vídeo nativo (hasta 3 h a baja resolución con 1M de contexto; 1 fotograma por segundo por defecto,
ajustable con `fps` en modo estático; ≈ 100 tokens por segundo a baja resolución y ≈ 300 a alta; marcas de tiempo
`MM:SS`). Un partido son 0,5-1,6 millones de tokens. Sirve para «dime los goles, córners y tiros con su minuto» como
primera pasada, con los mismos límites: no hay seguimiento métrico, se le escapan jugadas rápidas a 1 fps, y los que lo
usan en deporte recomiendan trocear en segmentos de 15-20 min y reencodar a fps constante. Supone sacar el vídeo del
servidor.

**Ollama (local)**: en el equipo de Diego hay `gemma4:12b`, `qwen3.5:9b`, `qwen3.6:27b-coding`, `qwen3.8:27b`,
`gpt-oss:20b`, `llama3.1:8b`, `qwen3:8b` y `qwen2.5-coder:7b` (Ollama estaba parado al mirar; hay que confirmar con
`ollama show` cuáles aceptan imágenes: Gemma 4 sí a partir de 4B; para visión la familia recomendada en Ollama es
**Qwen3-VL**, 8B para 16 GB de VRAM). Ollama no admite vídeo, solo imágenes, y la calidad por fotograma de un 8-12B está
por debajo de Claude. Ventaja decisiva: **el vídeo no sale del servidor** (en fútbol amateur hay menores: el RGPD pesa) y
no cuesta nada por partido. Vale para las mismas tareas del apartado C que Claude, a menor calidad y sin coste.

**Veredicto A**: solo con modelos multimodales no se llega a la posesión. Son la segunda capa, no la primera.

### B. Cadena de visión por computador (lo «profesional»)

Es lo que hay detrás de Veo y de los retos SoccerNet, y hoy se monta con piezas abiertas:

1. **Fotogramas**: ffmpeg saca 5-10 fotogramas por segundo (ya tenemos ffmpeg en el servicio de vídeo).
2. **Detección** de jugadores, porteros, árbitros y balón: **YOLO26** de Ultralytics (la generación actual) o
   **RF-DETR** de Roboflow, afinados con los datasets públicos de fútbol de Roboflow Universe y con 1-2 partidos
   asturianos anotados (los jugadores lejanos son muy pequeños en una cámara fija: inferencia por teselas, SAHI).
3. **Seguimiento**: BoT-SORT o ByteTrack (integrados en Ultralytics; BoT-SORT con compensación de cámara y
   re-identificación) → cada jugador tiene un identificador estable («tracklet»).
4. **Equipo de cada jugador**: solo por el color de la camiseta (torso del recorte sin césped → resumen en Lab con la
   claridad medida respecto al césped del propio recorte → KMeans + discriminante de Fisher en dos grupos, voto por
   tracklet ponderado y corte del tracklet si cambia de equipo). Hasta el 09-11-2026 se usaba el método del ejemplo
   de `roboflow/sports` (embedding SigLIP → UMAP → KMeans), que de noche separaba por la luz y no por la camiseta.
5. **Calibración del campo**: detección de puntos clave de las líneas → homografía → posición de cada jugador y del
   balón en metros. De aquí salen posesión por zonas, mapas de calor y distancias.
6. **Balón**: el punto débil; detector específico de objetos pequeños (**WASB**, F1 ≈ 88 en fútbol de TV) y
   seguimiento con suavizado.
7. **Posesión**: jugador más cercano al balón durante N fotogramas (histéresis) → equipo en posesión → % y tramos.
8. **Eventos**: reglas sobre el seguimiento (balón que sale, balón parado, portería) más un modelo de *action
   spotting* entrenado en SoccerNet (T-DEED o la cadena ganadora de 2025, mAP ≈ 60 en 12 acciones con balón: pase,
   tiro, córner, saque de banda, gol, falta…), afinado con nuestras grabaciones.
9. **Dorsales**: OCR por tracklet (el estado del arte de SoccerNet 2025 ronda el 85 %; también funcionan modelos de
   visión-lenguaje como Qwen-VL sobre los recortes). En amateur hay que asumir que el técnico asigna a mano y el OCR
   solo propone.
10. **Salida**: `analisis.json` (tracklets, posiciones por segundo, posesión, eventos con marca de tiempo y confianza)
    y recortes de vídeo de cada evento.

**Hardware**: el PC de Diego tiene una **RTX 5070 Ti de 16 GB**, un Ryzen 9 9950X3D y 31 GB de RAM: de sobra. YOLO26
«medium» con seguimiento va a 50-100 fps en esa GPU: un partido a 5 fps (27 000 fotogramas) son **5-15 minutos** de
análisis; a CPU serían horas. Docker Desktop corre en WSL2 con `--gpus all` (es lo que soporta en Windows) y hoy la
máquina de WSL2 tiene 6 GB de memoria por `.wslconfig`: para el análisis habría que subirla (o correr el analizador
fuera de Docker). En producción el servidor no tendrá GPU: o el análisis corre en el PC de Diego como «trabajador»
que tira de la cola, o se alquila GPU por horas en la nube solo mientras analiza.

**Licencias**: Ultralytics (YOLO) es AGPL-3.0: en un servicio cerrado hay que pagar su licencia comercial o usar
detectores Apache 2.0 (RF-DETR, RT-DETR, YOLOX). El resto (supervision, ByteTrack, SoccerNet, WASB) es MIT o
Apache. Hay que decidirlo antes de escribir el analizador.

### C. Híbrido (recomendado): B para los números, un LLM para lo que hay que «entender»

Sobre la salida de B, un modelo de lenguaje con visión hace lo que la cadena no hace bien:

- **Confirmar eventos dudosos**: para cada candidato (gol, tiro, falta) se le enseñan 3-5 fotogramas y la marca de
  tiempo y contesta «es un gol / es un despeje / no se ve». Unos 200-400 fotogramas por partido.
- **Leer dorsales** en los mejores recortes de cada tracklet (el que esté de espaldas, grande y nítido).
- **Redactar** el resumen del partido en Markdown y proponer las notas del acta («Pelayo: 2 tiros, 1 a puerta, 6
  recuperaciones; dominio del segundo tiempo») cruzando con el acta oficial de asturfutbol.es que ya tenemos
  (alineaciones, minutos, goles y tarjetas oficiales: eso **no** hay que sacarlo del vídeo, ya lo tenemos).
- **Volcar** el análisis al acta interna como propuesta (`MatchSheetEntryInput` + `TeamMatchStatsInput`), que el
  técnico revisa.

Con la **API de Claude** (Opus 5.5 o Sonnet 5.5 para la calidad, Haiku 5.5 para el volumen) esto son céntimos o pocos
euros por partido; con **Ollama** (Qwen3-VL 8B o Gemma 4 12B en la 5070 Ti) es gratis y el vídeo no sale de casa, a
cambio de menos acierto. Lo sensato es dejar la elección del modelo en configuración (`Analysis:Reasoner = claude |
ollama`) y medir con la misma prueba las dos.

## 6. Cómo encajaría en Tercer Tiempo

```
subida → TercerTiempo.Videos (ffmpeg, 1080p) → cola de análisis → TercerTiempo.Analisis (Python + GPU)
      → analisis.json + resumen.md + clips → API: acta propuesta → campanita al cuerpo técnico → revisión → acta interna
```

- **`TercerTiempo.Analisis`**: servicio aparte en Python (PyTorch, Ultralytics o RF-DETR, supervision, OpenCV), en su
  contenedor con GPU, que consume la cola por HTTP/BD igual que hace hoy el servicio de vídeo (`JobRuns` lo enseña en
  «Descargas en directo»). Entrada: ruta del vídeo comprimido y la plantilla con las fotos de las fichas (para
  reconocer a quién se pueda). Salida: carpeta `analisis/` junto al vídeo.
- **Dominio**: `MatchVideoAnalysis` (estado: en cola / analizando / listo / fallido; posesión, eventos, tracklets con
  sus números, versión del modelo) y `ProposedMatchSheet` (la propuesta por jugador y de equipo, con confianza por
  dato). El acta interna real no se toca hasta que el técnico pulsa «Aceptar».
- **API**: `GET /api/squads/{id}/matches/{matchId}/video-analysis` (estado y resultado),
  `PUT …/video-analysis/tracklets` (el técnico asigna «jugador 7 azul» → ficha) y `POST …/video-analysis/apply`
  (vuelca la propuesta al acta interna, línea a línea, respetando lo que el técnico cambie).
- **Frontal**: en el acta interna, una pestaña «Del vídeo» con el minimapa (radar), la posesión por tramos, la lista
  de eventos con su clip de 10 s y los botones «Es gol / no es», y la asignación de tracklets con el recorte del
  jugador al lado de las fotos de la plantilla.
- **Condiciones de grabación** que la app explica al club antes de subir: cámara fija, lateral, a la altura del
  centro, lo más alta posible (grada o trípode alto), todo el campo en plano, 1080p o más, sin zoom; con un móvil
  a mano los números no valen y la app lo avisa (el analizador detecta el encuadre y marca el vídeo como «no
  analizable»).

**Hecho el 06-11-2026** (rama `lectorVideos`): la cola tras la compresión, `MatchVideoAnalysis` con la propuesta y su
calidad, los endpoints `GET/POST …/video-analysis`, `POST …/video-analysis/apply` y los recortes, y la sección «Del
vídeo» del acta interna con la elección del equipo, la asignación de jugadores seguidos a fichas y el volcado. La
asignación viaja con el volcado (no hay `PUT …/tracklets` aparte) y no hay `ProposedMatchSheet`: la propuesta es el
`acta-propuesta.json` guardado tal cual. Queda fuera, de momento, la campanita al terminar, el minimapa y los eventos
con su clip. Cómo funciona: README del backend, «Análisis del vídeo: la propuesta de acta interna».

## 7. Riesgos y límites, sin maquillar

- **La cámara manda.** Sin encuadre fijo lateral, la posesión es basura. Hay que probar con un vídeo real del
  Vallobín antes de prometer nada.
- **El balón** es un punto de 3-6 px en el fondo del campo a 1080p: cuando se pierde, la posesión se estima por el
  jugador más cercano al último punto conocido. Grabar a 4K ayuda mucho (hoy admitimos hasta 20 GB, pero comprimimos a
  1080p: el analizador debería trabajar sobre el original antes de comprimir).
- **Identidad de los jugadores.** No prometer «estadísticas por jugador automáticas»: prometer «propuesta por jugador
  tras una asignación de un minuto».
- **Entrenar con nuestros datos.** Los modelos públicos están entrenados con fútbol de televisión; con 1-2 partidos
  anotados a mano (cajas de jugadores y balón, unos 500 fotogramas) la detección mejora de forma clara. Es trabajo de
  anotación, no de programación.
- **Datos personales.** Vídeo de personas identificables, muchas veces menores: el análisis local (Ollama / GPU
  propia) evita sacarlo a terceros; si se usa la API de Claude, se mandan recortes y fotogramas sueltos, no el partido,
  y hay que reflejarlo en la política de privacidad y en el contrato con el club.
- **Coste de GPU en producción**: o el PC de Diego hace de analizador (vale para el piloto), o GPU por horas en la
  nube; conviene decidirlo antes del piloto con clubes.
- **Licencia de Ultralytics** (AGPL) frente a RF-DETR (Apache): decidir antes de escribir.

## 8. Plan propuesto

1. **PoC 0 (una tarde)**: con un vídeo real del Vallobín, correr el ejemplo de `roboflow/sports` (modo RADAR) y WASB en
   la 5070 Ti y mirar con los ojos si detecta a todos, si sigue el balón y si la homografía cuadra. Decide si la cámara
   que tienen sirve.
2. **PoC 1 (una semana)**: posesión %, posesión por zonas y mapa de calor de un partido entero, comparado con la
   posesión que el técnico apuntó a ojo y con lo que diga Veo en un partido grabado con Veo si alguien del entorno
   tiene una. Criterio: ±5 puntos de posesión.
3. **PoC 2 (dos semanas)**: eventos (tiros, tiros a puerta, córners, saques de banda, goles) como candidatos con
   clip, y confirmación por LLM (Claude y Ollama con la misma prueba, medidos). Criterio: recuperar ≥ 90 % de los tiros
   y los goles con ≤ 20 % de falsos positivos antes de la revisión.
4. **PoC 3 (dos semanas)**: asignación de tracklets a fichas y acta propuesta volcada en el acta interna con revisión
   del técnico. Aquí entra ya el dominio, la API y el frontal descritos en el apartado 6.
5. Decisión de producción: dónde corre la GPU, licencia del detector, condiciones de grabación en el manual y en la
   subida.

**Lo que hace falta para empezar**: un vídeo real de un partido (ideal: cámara fija lateral), decidir Ultralytics
(AGPL/licencia) o RF-DETR, y subir la memoria de WSL2 (`.wslconfig`) o correr el analizador fuera de Docker durante las
pruebas.

## 9. Fuentes consultadas (05-11-2026)

- Documentación de visión de la API de Claude: formatos, límites (600 imágenes, 10 MB, 128 MB), coste por imagen —
  https://platform.claude.com/docs/en/build-with-claude/vision
- Gemini, comprensión de vídeo: duración, fps, tokens por segundo — https://aistudio.google.com/docs/video-understanding
- Modelos de visión en Ollama (Qwen3-VL, Gemma) — https://insiderllm.com/guides/vision-models-locally/ y
  https://computingforgeeks.com/ollama-models-cheat-sheet/
- `roboflow/sports` (detección, seguimiento, equipos, radar; MIT) — https://github.com/roboflow/sports y su ejemplo
  https://github.com/roboflow/sports/tree/main/examples/soccer
- Ultralytics YOLO26 y seguimiento (BoT-SORT, ByteTrack) — https://docs.ultralytics.com/modes/track ; Docker con GPU en
  Windows — https://docs.ultralytics.com/guides/docker-quickstart
- SoccerNet: tareas, retos 2026 y Game State Reconstruction — https://www.soccer-net.org/ ,
  https://www.soccer-net.org/challenges/2026 , https://arxiv.org/abs/2404.11335 (baseline `sn-gamestate`),
  resultados 2025 (action spotting, dorsales) — https://arxiv.org/abs/2508.19182
- Reconocimiento de dorsales (85 % por tracklet) — https://lukaszgrad.github.io/jnr
- WASB, detección y seguimiento del balón — https://arxiv.org/abs/2311.05237 , https://github.com/nttcom/WASB-SBDT
- SAHI (inferencia por teselas para objetos pequeños) — https://blog.roboflow.com/how-to-use-sahi-to-detect-small-objects
- Veo Analytics 2 (datos por jugador desde agosto de 2026) — https://www.veo.com/product/veo-analytics y
  https://support.veo.com/hc/en-us/articles/49953564466321 ; precios de terceros —
  https://www.renderfoot.com/ar/blog/football-video-analysis-software
- Hudl, Pixellot, Spiideo — https://www.athleticbusiness.com/industry-press-room/article/15140099/hudl-unveils-portable-smart-camera-solution-for-club-sports ,
  https://www.svgeurope.org/blog/headlines/pixellot-to-help-pitchero-bring-video-to-its-online-platform-that-serves-uk-clubs-and-teams/
- Twelve Labs (API de vídeo) — https://www.twelvelabs.io/pricing-calculator , https://chatgate.ai/post/pegasus-1-5-by-twelvelabs
- Proyectos abiertos de estadísticas desde una cámara — https://github.com/Similly/FootballVision ,
  https://github.com/mradovic38/football_analysis , https://pypi.org/project/sportvision/
