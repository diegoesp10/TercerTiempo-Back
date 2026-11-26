# Revisar las acciones de Ojeador

En el visor, abre una repetición terminada con su vídeo original y entra en **Revisión**. Puedes revisar tiros, saques de puerta y paradas.

1. Elige el tipo de acción y una propuesta. **Ver desde 5 s antes** reproduce su contexto. Marca **Real**, **Falsa** o **No se ve** y, si ayuda, escribe una nota. Por ejemplo, «centro», «recogida sin tiro» o «el balón sale del plano». Desmarca **Solo pendientes** para recuperar y corregir cualquier veredicto.
2. Para un primer conjunto de evaluación, mira tramos completos y apunta las acciones que tú ves, incluidas las que el Ojeador omitió. Pausa en el segundo de la acción, elige su tipo y pulsa **Anotar acción real aquí**. Si la propuesta era de otro tipo, márcala como falsa y anota lo que ocurrió en la categoría correcta.
3. Marca inicio y final de un tramo solo después de haber comprobado todas las acciones de la categoría en él. Puedes marcar un tramo sin acciones reales. Sirve para detectar falsas alarmas. Si un intervalo no se puede revisar por el encuadre, déjalo fuera de los tramos revisados.
4. El visor muestra aciertos, falsas propuestas y omisiones para la categoría elegida. **Precisión** indica cuántas propuestas fueron correctas y **cobertura**, cuántas acciones reales detectó el Ojeador. Sin tramos revisados, el visor conserva las etiquetas pero no estima estas métricas.

Las etiquetas se guardan en el equipo, en `%LOCALAPPDATA%\TercerTiempo\revisiones-ojeador.sqlite3` en Windows. Se agrupan por archivo de vídeo y se reutilizan al abrir otra pasada del mismo vídeo. **Exportar JSON** permite guardar una copia, comparar versiones y preparar ejemplos positivos, negativos y omitidos. La revisión es independiente de las salidas del análisis y de la propuesta del acta.

## Comparar dos versiones

Desde la carpeta del analizador, usa el mismo JSON exportado para las dos pasadas.

```powershell
.venv/Scripts/python.exe -m tt_analisis.evaluar_eventos revision.json salidas/partido-2.3.2
.venv/Scripts/python.exe -m tt_analisis.evaluar_eventos revision.json salidas/partido-2.4.0
```

`--json` añade la lista de instantes de acierto, falsa propuesta y omisión. Si el vídeo ha cambiado de carpeta, añade `--video <ruta>`. La comparación usa el mismo archivo y empareja cada acción una sola vez, por tipo y con un margen de dos segundos. Conserva los rechazos explícitos de cada pasada. Cuenta solo el tiempo revisado de cada tipo dentro del tramo analizado y excluye una zona alrededor de las propuestas marcadas como «No se ve». La comparación mide detección de acciones. El equipo, el jugador y el resultado del tiro se pueden estudiar por separado.

Conviene usar varios partidos y cámaras, y reservar algunos partidos etiquetados para comprobar cambios sin ajustar con esos mismos ejemplos. Las jugadas ocultas o fuera de plano se quedan fuera de la evaluación. Este conjunto permite buscar la mayor precisión posible y medir cuántas acciones visibles se pierden al ser más exigentes.
