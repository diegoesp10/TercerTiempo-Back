# Revisar lo que propone el Ojeador

La pestaña **Revisión** del visor sirve para apuntar lo que de verdad pasa en el vídeo y medir con ello cada versión del
Ojeador: cuántas propuestas aciertan, cuántas son falsas y por qué, cuánto de lo que pasó se le escapa y, en las acciones
de cada jugador, si acierta con quién la hizo. Lo revisado se guarda por vídeo y vale para todas las pasadas de ese
vídeo: se revisa una vez y se mide cada versión nueva sin volver a mirarlo. La revisión no toca las salidas del análisis
ni la propuesta del acta.

## Qué se revisa

Cada tipo se anota en un segundo concreto, el que usa el Ojeador, y una propuesta se da por buena si queda a menos del
margen de ese segundo.

| Tipo | Segundo que se anota | Margen |
|---|---|---:|
| Gol | Cuando el balón cruza la línea de gol | 2 s |
| Tiro | El del disparo, cuando el balón sale hacia la portería | 2 s |
| Parada | Cuando el portero para el balón del rival o se queda con él | 2 s |
| Saque de puerta | Cuando el balón sale por la línea de fondo tocado por un atacante (no el del saque) | 2 s |
| Córner | Cuando el balón sale por la línea de fondo tocado por un defensor (no el del saque) | 2 s |
| Saque de banda | Cuando el balón sale por la banda (no el del saque) | 2 s |
| Pase | Cuando sale el pase, de un jugador a otro de su equipo | 1 s |
| Pase fallado | Cuando sale el pase que no llega a nadie de su equipo | 1 s |
| Pérdida | Cuando el rival le quita el balón o lo pierde | 1 s |
| Recuperación | Cuando le roba el balón al rival y se queda con él | 1 s |
| Saque del portero | Cuando el portero saca, con el pie o con la mano | 1 s |

Los pases van muy seguidos: con 2 s, uno se emparejaría con el siguiente. Las faltas, los fueras de juego, los saques de
centro, los parones y el balón fuera por el fondo sin saber quién lo tocó no se revisan: o no dicen qué pasó o su segundo
es dudoso.

## Cómo se revisa

Abre en el visor un análisis terminado cuyo vídeo original siga en su sitio, entra en **Revisión** y elige en **Qué
revisar** el tipo.

1. **La propuesta.** Sale elegida la primera sin revisar desde donde está el vídeo; al dar un veredicto se pasa a la
   siguiente. **Ver desde 5 s antes** reproduce lo que lleva a ella e **Ir a la propuesta** para el vídeo en su segundo.
   Con ← y → se mueve un segundo (cinco con Mayús), con `,` y `.` al fotograma analizado anterior o siguiente y con la
   barra espaciadora se para o sigue. Desmarca **Solo sin revisar** para ver y corregir lo ya revisado.
2. **El veredicto.**
   - **Real**: pasó y la propuesta está a menos del margen.
   - **Real en este segundo**: para el vídeo en el momento exacto (por ejemplo, cuando sale el pase) y púlsalo. Se
     guarda ese segundo, que es más justo para medir las versiones siguientes. Solo vale dentro del margen de la
     propuesta: si lo que pasó queda más lejos, márcala como falsa y anótalo aparte.
   - **Falsa**, con su motivo en **Si es falsa, por qué**: era otra cosa (un centro, un despeje…), repetida, balón
     falso, no veía el balón, salto del seguidor u otro. Con los motivos se ve qué hay que arreglar.
   - **No se ve**: no se puede saber si pasó. Ni esa propuesta ni lo que haya a menos del margen cuentan al medir, en
     ninguna versión.
   - **Deshacer veredicto** la deja sin revisar.
   - La nota es opcional.
3. **Quién la hizo** (solo en las acciones). De partida, **Fue él**: el jugador que dice el Ojeador. Si fue otro, elígelo
   en la lista; si no sabes quién, **Fue otro, no sé quién** o **No se ve quién**. En una propuesta sin identificar,
   **Sin decir quién** deja lo que hubiera anotado. Lo último que se dice es lo que vale.
4. **Lo que se le escapó.** Para el vídeo en su segundo y pulsa **Anotar aquí lo que pasó** (en las acciones, con quién
   la hizo, si se ve). Si el Ojeador lo propuso con otro tipo, marca aquella como falsa («Era otra cosa») y anótalo en
   el tipo que es.
5. **Tramos revisados de principio a fin.** **Marcar inicio aquí** y, más adelante, **Guardar final aquí**, solo cuando
   hayas anotado todo lo de ese tipo que pasa entre ambos. Sin tramos se guardan los veredictos, pero no se mide nada.
   Un tramo sin nada real también vale: así se cuentan las falsas alarmas. Si un rato no se puede revisar (la cámara no
   lo enseña), déjalo fuera.
6. **El resultado**, en los tramos revisados: aciertos, falsas, omitidas, **precisión** (de lo propuesto, cuánto era
   real) y **cobertura** (de lo que pasó, cuánto propuso). En las acciones, también quién la hizo: bien, mal, sin
   identificar (el Ojeador no dice quién) y sin saber (no se sabe quién fue). Y por qué son falsas: las que no tienen
   veredicto dentro de un tramo revisado cuentan como falsas, porque el tramo dice que lo que pasó está todo anotado.

## Dónde se guarda

- En `%LOCALAPPDATA%\TercerTiempo\revisiones-ojeador.sqlite3`, un documento por vídeo. El vídeo se reconoce por su
  huella (tamaño y unos trozos del archivo), aunque cambie de carpeta.
- Una copia por vídeo en `datasets/revisiones/<huella>.json`, que se pone al día con cada cambio (no va a git). Si el
  SQLite se pierde, el visor lo recupera de ahí. Si la copia no se puede escribir, se guarda igual en el equipo y el
  visor avisa.
- Con dos ventanas abiertas, lo que cambie una no pisa lo de la otra: la segunda pide actualizar.
- **Exportar JSON** descarga lo mismo que la copia.

## Medir una versión

Desde la carpeta del analizador, con la copia (o el JSON exportado) y la carpeta de cada pasada:

```powershell
.venv/Scripts/python.exe -m tt_analisis.evaluar_eventos datasets/revisiones/<huella>.json salidas/partido-2.3.2
.venv/Scripts/python.exe -m tt_analisis.evaluar_eventos datasets/revisiones/<huella>.json salidas/partido-2.4
```

Lee la repetición de la pasada (`repeticion/final.json`, con las jugadas y las acciones); sin ella, su `analisis.json`,
que solo trae las jugadas. Saca una tabla por tipo con los segundos revisados, aciertos, falsas, omisiones, precisión,
cobertura y cuántas veces acierta con el jugador, y debajo por qué son falsas. `--json` añade cada pareja y cada fallo
con su segundo; `--video <ruta>`, si el vídeo ha cambiado de carpeta; y `--margen N`, el mismo margen para todos los
tipos.

Cómo cuenta:

- Lo que pasó y los tramos valen para todas las pasadas del vídeo. Los veredictos de cada propuesta, para las pasadas que
  proponen exactamente lo mismo en el mismo tramo, aunque sean de otra versión. En una versión nueva no hace falta volver
  a marcar nada: lo que propone cerca de lo anotado cuenta como acierto, y lo demás de los tramos revisados, como falso.
- Cada cosa anotada se empareja con una sola propuesta: dos pases a un segundo uno del otro son dos.
- Una propuesta hasta un margen fuera del tramo puede ser lo que pasó dentro de él, pero lo propuesto fuera del tramo
  nunca cuenta como falso.
- Lo marcado como falso en esa pasada cuenta como falso aunque haya algo real cerca: sería otra acción.
- Alrededor de lo marcado «No se ve», en cualquier pasada, no se cuentan ni falsas ni omisiones, para que todas las
  versiones se midan igual.

## Medir un cambio sin repetir la pasada

Para un cambio en cómo se interpreta lo visto (jugadas, acciones, porteros, quién es quién), no hace falta otra pasada
con la GPU. Basta con analizar el partido una vez con `--guardar-pasada` y después, tras cada cambio:

```powershell
uv run tt-analisis rehacer-jugadas salidas/j8
```

Rehace esa capa con el código de ahora en unos segundos, en `salidas/j8-rehecho`, y saca la misma tabla que
`evaluar_eventos` con lo revisado de ese vídeo en el visor. No hace falta exportar nada: lee el SQLite del visor, solo
para leer, o su copia. Lo rehecho se abre en el visor como cualquier análisis y comparte los veredictos con las pasadas
que propongan lo mismo. El detalle, en el README, apartado «Rehacer las jugadas sin la GPU».

## Buenas costumbres

- Primero la precisión: mejor no proponer que proponer mal.
- Varios partidos y cámaras. Deja algún partido revisado sin usarlo para afinar, para comprobar con él que una mejora
  no es solo de los ejemplos con los que se ajustó.
- Para empezar, basta un rato de diez minutos revisado entero con todos los tipos.
