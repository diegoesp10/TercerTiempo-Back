"""La cadena completa: separar equipos, detectar y seguir, calibrar el campo, posesión y eventos, y escribir los
informes. `analizar(config)` deja todo en la carpeta de salida y devuelve el JSON del análisis."""

from __future__ import annotations

import json
import os

import shutil
import sys
import time
from dataclasses import replace
from pathlib import Path

import numpy as np
import supervision as sv
from tqdm import tqdm

from . import NOMBRE, __version__
from . import apariencia as ap
from .avance import Avance
from .balon import cobertura, completar_con_intermedios, limpiar_trayectoria
from .campo import Calibrador, configurar_campo
from .colores import rasgos_equipacion, tapados
from .config import CLASE_ARBITRO, MARGEN_CAMPO_CM, Configuracion
from .geometria import Campo
from .deteccion import Detector, Muestreado
from .dorsales import ALTO_MIN_PX, ColaDorsales, GuardaZonas, HistorialLecturas, cargar_lector, junto_al_balon, zona_dorsal
from .identidades import Resolucion, Tramo, leer_plantillas, resolver
from .equipos import ClasificadorEquipos, color_hex
from .informes import acta_propuesta, escribir_json, escribir_mapas, escribir_posiciones, escribir_recortes, extraer_clips, resumen_markdown
from .llm import crear_modelo
from .llm.revision import confirmar_eventos, leer_dorsales, redactar_resumen
from .modelos import cargar_modelos, describir_gpu, describir_modelos, exigir_gpu
from .jugadas import analizar_jugadas, buscar_descanso
from .mitades import mitades
from .oficial import leer_acta
from .porteros import Porteros, colocar_porteros, describir_porteros
from .rehacer import EstadoPasada
from .rehacer import guardar as guardar_pasada
from .rehacer import leer as leer_pasada
from .repeticion import Directo, escribir_repeticion
from .seguimiento import MemoriaTracklet, Seguidor
from .tipos import Instante, Persona, formato_minuto
from .tuberia import Cronometro, TrabajadorGPU, adelantar, cerrar, en_segundo_plano, medir
from .video import PCT_LEIDO_MINIMO, Lectura, fotograma_en, fotogramas, fotogramas_elegidos, info_video, pct_leido, recortar

RUTA_FFMPEG_WINGET = Path.home() / "AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/ffmpeg.exe"
# El recorte del portero: de pie (la caja al menos esto más alta que ancha) y el más nítido de los tantos más altos.
PROPORCION_DE_PIE = 1.5
RECORTES_PORTERO = 5
# Fotogramas muestreados que la GPU lleva de adelanto en la pasada principal (con `solapar`): con dos, no se queda
# esperando a la CPU entre un fotograma y el siguiente.
ADELANTO = 2


def ruta_ffmpeg() -> str:
    encontrado = shutil.which("ffmpeg")
    if encontrado:
        return encontrado
    if RUTA_FFMPEG_WINGET.exists():
        return str(RUTA_FFMPEG_WINGET)
    return "ffmpeg"


def fijar_semilla(semilla: int) -> None:
    import random

    import torch

    random.seed(semilla)
    np.random.seed(semilla)
    torch.manual_seed(semilla)


def datos_del_video(config: Configuracion, info, fin_s: float) -> dict:
    return {"ruta": str(config.video), "ancho": info.ancho, "alto": info.alto, "fps": info.fps, "duracion_s": round(info.duracion_s, 1), "tramo_inicio_s": config.segmento.inicio, "tramo_fin_s": round(fin_s, 1)}


def analizar(config: Configuracion, informar=print) -> dict:
    # Lo que se va viendo, para seguir el análisis en el visor mientras se hace (`repeticion.Directo`). Si el análisis no
    # termina, lo provisional se queda con el porqué.
    directo = Directo(config.salida, activo=config.repeticion)
    try:
        return _analizar(config, informar, directo)
    except BaseException as error:
        directo.fallo(error)
        raise
    finally:
        directo.detener()


def _analizar(config: Configuracion, informar, directo: Directo) -> dict:
    inicio_total = time.time()
    tiempos: dict[str, float] = {}
    # Lanzado como proceso, cada cosa que se sabe va como hito (y las órdenes de pausa llegan por la entrada estándar).
    progreso = Avance(config.progreso, entrada=sys.stdin if config.progreso else None)
    decir = progreso.hito if progreso.activo else informar
    progreso.etapa("modelos")
    info = info_video(config.video)
    if config.exigir_gpu:
        exigir_gpu(config.device)
    config.salida.mkdir(parents=True, exist_ok=True)
    progreso.fase("equipos", 0, 1)
    fin_s = min(config.segmento.fin, info.duracion_s) if config.segmento.fin is not None else info.duracion_s
    directo.empezar(
        analizador={"nombre": NOMBRE, "version": __version__}, video=datos_del_video(config, info, fin_s),
        equipos=[{"id": e, "nombre": config.nombres_equipos[e]} for e in (0, 1)], avance=progreso,
    )
    decir = directo.con_hitos(decir)
    decir(f"Vídeo: {info.ancho}×{info.alto} a {info.fps:.0f} fps, {formato_minuto(info.duracion_s)}. Tramo {formato_minuto(config.segmento.inicio)} – {formato_minuto(fin_s)}.")
    decir(f"Analizador: {NOMBRE} {__version__}.")
    decir(f"GPU: {describir_gpu()}")

    if config.campo:
        configurar_campo(config.campo[0] * 100, config.campo[1] * 100)
        decir(f"Campo de {config.campo[0]:g} × {config.campo[1]:g} m.")
    modelos = cargar_modelos(config.modelos, config.device)
    decir(f"Detectores: {', '.join(f'{n} ({r})' for n, r in modelos.rutas.items())}.")
    grabados: list[str] = []
    if config.grafos and modelos.device.startswith("cuda"):
        grabados, fallo = modelos.grabar(info.ancho, info.alto)
        if fallo:
            decir(f"Aviso: sin grafo CUDA para {fallo}. Va paso a paso: mismo resultado, más lento.")
    lector = cargar_lector(config.modelos, modelos.device, afinados=config.afinados) if config.dorsales else None
    if lector is not None:
        decir(f"Lector de dorsales: {lector.nombre}.")
    elif config.dorsales:
        decir("Aviso: falta el lector de dorsales (modelos/parseq-bb5792a6.pt; `tt-analisis descargar-modelos`): cada tracklet queda suelto.")
    apariencia = ap.cargar_apariencia(config.modelos, modelos.device) if lector is not None and config.apariencia else None
    if lector is not None and config.apariencia and apariencia is None:
        decir("Aviso: falta el modelo del aspecto (modelos/siglip): solo se identifica por el dorsal.")
    # Quién analiza el partido: el nombre y la versión del analizador y cada modelo, con su entrenamiento y su huella.
    analizador = {"nombre": NOMBRE, "version": __version__, "modelos": describir_modelos(modelos, config.modelos, lector, apariencia)}
    guarda = GuardaZonas(config.guardar_dorsales) if lector is not None and config.guardar_dorsales is not None else None
    # Cada lectura con su segundo: del portero cuyo tracklet saltó a otra persona solo cuentan las de su rato.
    historial = HistorialLecturas() if lector is not None else None
    cola = ColaDorsales(lector, guardar=guarda, historial=historial) if lector is not None else None
    cola_aspecto = ap.ColaApariencia(apariencia) if apariencia is not None else None
    paso = max(1, round(info.fps / config.fps_muestreo))
    fps_real = info.fps / paso
    dt = 1 / fps_real
    # Al analizado en el que no se ve el balón se le busca en los fotogramas de al lado (ver `Configuracion.vecinos_balon_s`).
    vecinos = max(0, min(int(round(config.vecinos_balon_s * info.fps)), paso - 1))
    if config.semilla is not None:
        fijar_semilla(config.semilla)
        # Con otra semilla se muestrean otros fotogramas del vídeo (desplazados dentro del paso): sirve para medir
        # cuánto cambia el resultado con una muestra distinta del mismo tramo.
        desfase_s = (config.semilla % paso) / info.fps
        config.segmento = type(config.segmento)(config.segmento.inicio + desfase_s, config.segmento.fin)

    # 1. Equipos: una pasada rápida por el tramo para aprender las dos equipaciones (y, con cámara fija, los puntos del
    # campo de esos mismos fotogramas para una sola homografía).
    t0 = time.time()
    clasificador = ClasificadorEquipos(modelos, config)
    calibrador = Calibrador(modelos.campo, modelos.device, modo=config.camara, cada_s=config.calibrar_cada_s)
    progreso.etapa("equipos", objetivo=config.muestras_equipos)

    def avance_equipos(n: int) -> None:
        progreso.punto_de_control()
        barra.update(n - barra.n)
        progreso.fase("equipos", n, config.muestras_equipos)
        progreso.estado(muestras=n)

    with tqdm(total=config.muestras_equipos, desc="Equipos (recortes)", unit="recorte", disable=config.progreso) as barra:
        muestras = clasificador.ajustar(info, avance=avance_equipos, por_fotograma=calibrador.acumular if config.camara == "fija" else None)
    if config.camara == "fija":
        fija = calibrador.fijar()
        if fija.valida:
            decir(f"Cámara fija: homografía única con {fija.puntos_usados} puntos del campo (error {fija.error_px:.1f} px).")
        else:
            decir("Cámara fija: no se vieron puntos suficientes del campo; se calibra fotograma a fotograma.")
            calibrador.modo = "movil"
    tiempos["equipos_s"] = time.time() - t0
    reparto = clasificador.reparto
    decir(f"Equipos separados por el color de la camiseta con {muestras} recortes: {color_hex(clasificador.colores[0])} ({reparto[0]}) y {color_hex(clasificador.colores[1])} ({reparto[1]}).")
    progreso.estado(forzar=True, colores=[color_hex(c) for c in clasificador.colores], muestras=muestras)
    if clasificador.desequilibrado:
        decir("Aviso: un equipo se queda con muy pocos recortes; puede que las dos camisetas se parezcan o que el césped se cuele en el torso.")

    # 2. Pasada principal.
    t0 = time.time()
    campo = Campo()
    detector = Detector(modelos, config)
    seguidor = Seguidor()
    fuera_de_campo = 0
    instantes: list[Instante] = []
    renderizador = None
    if config.video_anotado or config.radar:
        from .render import Renderizador

        renderizador = Renderizador(config.salida / "anotado.mp4", info.ancho, info.alto, fps_real, clasificador.colores)

    total = int((fin_s - config.segmento.inicio) * fps_real)
    if config.max_fotogramas:
        total = min(total, config.max_fotogramas)
    n = 0
    lectura = Lectura()
    progreso.etapa("pasada", tramo_inicio_s=round(config.segmento.inicio, 1), tramo_fin_s=round(fin_s, 1), fotogramas_total=total)
    decir(f"Siguiendo a jugadores y balón: {total} fotogramas a {fps_real:.1f} por segundo de vídeo.")
    progreso.fase("pasada", 0, total)
    directo.empezar_pasada(
        video=datos_del_video(config, info, fin_s), dt=dt, campo=campo, total=total,
        equipos=[{"id": e, "nombre": config.nombres_equipos[e], "color": color_hex(clasificador.colores[e])} for e in (0, 1)],
    )
    vistos = calibrados_hasta = 0
    # El balón visto en los fotogramas vecinos: [(t, centro en px, confianza)], en orden.
    pista: list[tuple[float, np.ndarray, float]] = []
    previo: Muestreado | None = None
    # Para el avance: instantes con balón en su fotograma o en uno de al lado (se sabe un tramo después).
    vistos_cerca = 0
    margen_cerca = vecinos / info.fps + 1e-3
    pasada_inicio = time.monotonic()
    pasada_pausa = 0.0
    # Lecturas del dorsal en el fotograma de la mitad entre dos muestreados (`dorsales.junto_al_balon`).
    lecturas_medio = 0
    # Sin esperas (`tuberia.py`): el vídeo se lee en otro hilo, y lo de la GPU que no depende de los fotogramas anteriores
    # (personas, candidatos a balón y puntos del campo) se lanza ADELANTO fotogramas antes en el hilo de la GPU, mientras la
    # CPU sigue con el actual. Lo que sí depende de los anteriores (seguir, elegir el balón, calibrar, los vecinos) va en
    # orden y con los mismos datos: el resultado no cambia.
    crono = Cronometro()
    detector.cronometro = crono
    gpu = TrabajadorGPU(sincrono=not config.solapar)
    campo_adelantado = calibrador.detecta_siempre
    leidos = fotogramas(config, info, paso=paso, lectura=lectura, vecinos=vecinos, medio=cola is not None)
    if config.solapar:
        leidos = en_segundo_plano(leidos, maximo=ADELANTO + 2)

    def lanzar(fotograma, k: int):
        return crono.medido("lanzar_gpu", gpu.enviar, k, detector.crudo, fotograma.imagen, campo_adelantado)

    adelantados = adelantar(medir(leidos, crono, "leer_video"), lanzar, ADELANTO if config.solapar else 0)
    try:
        for fotograma, de_la_gpu in tqdm(adelantados, total=total, desc="Analizando", unit="fot", disable=config.progreso):
            pasada_pausa += progreso.punto_de_control()
            n += 1
            progreso.fase("pasada", n, total)
            imagen = fotograma.imagen
            with crono.etapa("esperar_gpu"):
                crudo = de_la_gpu.result()
            with crono.etapa("seguir_y_balon"):
                det = detector.detectar(imagen, fotograma.t, crudo)
                seguidas = seguidor.registrar(det.personas)
            with crono.etapa("calibrar"):
                calibracion = calibrador.calibrar(imagen, fotograma.t, forzar=(n == 1), puntos=crudo.campo)
                xy_imagen = seguidas.get_anchors_coordinates(sv.Position.BOTTOM_CENTER)
                xy_campo = calibrador.a_campo(xy_imagen) if calibracion.valida and len(seguidas) else None

                # Con el campo calibrado, quien está lejos de las líneas (gradas, banquillo) no es del partido. Pero si la
                # homografía deja fuera a la mayoría, lo que está mal es la homografía: el fotograma queda sin calibrar.
                if xy_campo is not None:
                    en_campo = np.array([campo.dentro((float(p[0]), float(p[1])), margen=MARGEN_CAMPO_CM) for p in xy_campo])
                    if len(en_campo) >= 6 and en_campo.mean() < 0.5:
                        calibrador.invalidar(fotograma.t)
                        calibracion = calibrador.actual
                        xy_campo = None
                    else:
                        fuera_de_campo += int((~en_campo).sum())
                        seguidas, xy_imagen, xy_campo = seguidas[en_campo], xy_imagen[en_campo], xy_campo[en_campo]

            balon_imagen = None
            balon_campo = None
            balon_confianza = None
            if len(det.balon):
                centro = det.balon.get_anchors_coordinates(sv.Position.CENTER)[0]
                balon_imagen = (float(centro[0]), float(centro[1]))
                balon_confianza = float(det.balon.confidence[0]) if det.balon.confidence is not None else None
                if calibracion.valida:
                    balon_campo = calibrador.punto_a_campo(balon_imagen)
                    if balon_campo is not None and not campo.dentro(balon_campo, margen=MARGEN_CAMPO_CM):
                        # Un «balón» en la grada o en el banquillo no es el balón.
                        balon_imagen, balon_campo, balon_confianza = None, None, None

            # El balón de los muestreados en los que no se vio, en sus fotogramas vecinos (los de este tramo: los de después
            # del anterior y los de antes de este). Va en el hilo de la GPU (todo lo del detector del balón, en el mismo
            # hilo) mientras la CPU sigue con los equipos y los dorsales, y se espera antes de acabar el fotograma: lo que
            # encuentra cuenta para elegir el balón del siguiente. De las personas solo lee cajas y clases, que lo que viene
            # después no toca (solo cambia sus tracklets).
            ultimo = detector.ultimo_balon
            actual = Muestreado(
                fotograma.t, None if balon_imagen is None else np.asarray(balon_imagen, dtype=np.float32),
                ultimo[2] if balon_imagen is not None and ultimo is not None and ultimo[0] == fotograma.t else None, det.personas,
            )
            de_los_vecinos = None
            if fotograma.intermedios:
                de_los_vecinos = gpu.enviar(
                    n, crono.medido, "gpu_vecinos", detector.balon_vecinos, fotograma.intermedios, previo, actual, vecinos, 1 / info.fps, config.vecinos_enteros,
                )

            with crono.etapa("equipos"):
                equipos, pesos = clasificador.clasificar(imagen, seguidas)
                seguidas = seguidor.votar_equipos(seguidas, equipos, pesos)
                seguidas = seguidor.separar_cruces(seguidas, fotograma.t)
                tapada = tapados(seguidas.xyxy) if len(seguidas) else np.zeros(0, dtype=bool)

            personas = []
            equipo_fotograma: dict[int, int] = {}
            for i in range(len(seguidas)):
                tid = int(seguidas.tracker_id[i])
                memoria = seguidor.memorias[tid]
                equipo_fotograma[tid] = int(equipos[i])
                clase = memoria.clase
                caja = tuple(float(v) for v in seguidas.xyxy[i])
                if not tapada[i]:
                    # El color de la equipación, para encontrar a los porteros aunque el detector los tome por jugadores.
                    with crono.etapa("equipacion"):
                        rasgos = rasgos_equipacion(imagen, caja)
                        if rasgos is not None:
                            memoria.sumar_equipacion(rasgos, fotograma.t)
                personas.append(
                    Persona(
                        tracklet=tid,
                        clase=clase,
                        equipo=None if clase == CLASE_ARBITRO else memoria.equipo,
                        xy_imagen=(float(xy_imagen[i][0]), float(xy_imagen[i][1])),
                        xy_campo=None if xy_campo is None else (float(xy_campo[i][0]), float(xy_campo[i][1])),
                        caja=caja,
                        confianza=float(seguidas.confidence[i]) if seguidas.confidence is not None else 0.0,
                    )
                )
                if n % 10 == 0 and clase != CLASE_ARBITRO:
                    memoria.guardar_recorte(fotograma.t, caja, personas[-1].confianza, imagen, int(equipos[i]))
                if cola_aspecto is not None and clase != CLASE_ARBITRO and not tapada[i] and caja[3] - caja[1] >= ap.ALTO_MIN_PX and (n + tid) % ap.RECORTE_CADA == 0:
                    with crono.etapa("aspecto"):
                        cola_aspecto.poner(tid, recortar(imagen, caja, margen=2))
                if cola is not None and clase != CLASE_ARBITRO:
                    with crono.etapa("dorsales"):
                        zona = zona_dorsal(imagen, caja)
                        if zona is not None:
                            cola.poner(tid, zona.copy(), recortar(imagen, caja, margen=6).copy(), seguidor.memorias, int(equipos[i]), fotograma.t)

            alto_img, ancho_img = imagen.shape[:2]
            inst = Instante(
                fotograma.indice, fotograma.t, personas, balon_imagen, balon_campo, calibracion.valida, balon_confianza=balon_confianza,
                homografia=calibracion.homografia if calibracion.valida else None,
                vista_campo=calibrador.punto_a_campo((ancho_img / 2, alto_img / 2)) if calibracion.valida else None,
            )
            instantes.append(inst)
            vistos += balon_imagen is not None
            if cola is not None and fotograma.medio is not None and len(instantes) >= 2:
                medio = fotograma.medio
                with crono.etapa("dorsales"):
                    for tid, caja, equipo in junto_al_balon(instantes[-2], inst, medio.t, equipo_fotograma):
                        zona = zona_dorsal(medio.imagen, caja)
                        if zona is not None:
                            cola.poner(tid, zona.copy(), recortar(medio.imagen, caja, margen=6).copy(), seguidor.memorias, equipo, medio.t)
                            lecturas_medio += 1
            calibrados_hasta += calibracion.valida

            entre = []
            if de_los_vecinos is not None:
                with crono.etapa("esperar_vecinos"):
                    entre = de_los_vecinos.result()
                pista.extend(entre)
                if balon_imagen is None and entre:
                    detector.recordar_balon(entre[-1][0], entre[-1][1])
                if len(instantes) >= 2:
                    anterior = instantes[-2]
                    vistos_cerca += anterior.balon_imagen is not None or any(abs(t - anterior.t) <= margen_cerca for t, _, _ in pista[-8:])
            previo = actual
            with crono.etapa("directo"):
                directo.fotograma(inst, entre, tracklets=len(seguidor.memorias))
            if progreso.toca():
                activo_s = max(1e-6, time.monotonic() - pasada_inicio - pasada_pausa)
                fps_proceso = n / activo_s
                progreso.estado(
                    t_video=round(fotograma.t, 1),
                    fotogramas=n,
                    jugadores_en_imagen=sum(1 for p in personas if p.clase != CLASE_ARBITRO),
                    jugadores_seguidos=sum(1 for m in seguidor.memorias.values() if m.clase != CLASE_ARBITRO),
                    balon_pct=round(100 * (vistos_cerca if vecinos else vistos) / max(1, n - 1 if vecinos else n), 1),
                    calibrado_pct=round(100 * calibrados_hasta / n, 1),
                    fps=round(fps_proceso, 1),
                    velocidad=round(fps_proceso / fps_real, 2),
                    resta_s=round(max(0, total - n) / fps_proceso),
                )
            if renderizador:
                renderizador.dibujar(imagen, inst)
    finally:
        gpu.cerrar()
        cerrar(adelantados, leidos)
    pasada_activa_s = max(1e-6, time.monotonic() - pasada_inicio - pasada_pausa)
    # Lo provisional del último instante, antes de que se complete el balón y se decida quién es quién.
    directo.fin_pasada()
    if renderizador:
        renderizador.cerrar()
    if cola is not None:
        cola.vaciar(seguidor.memorias)
    apariencias = cola_aspecto.vectores() if cola_aspecto is not None else None
    if guarda is not None:
        guarda.vaciar()
    tiempos["pasada_s"] = time.time() - t0
    decir(f"Seguimiento terminado: {n} fotogramas en {formato_minuto(tiempos['pasada_s'] - pasada_pausa)}; balón visto en el {round(100 * vistos / max(1, n))} % y campo calibrado en el {round(100 * calibrados_hasta / max(1, n))} %.")
    if not instantes:
        raise RuntimeError("No se ha leído ningún fotograma del tramo indicado.")
    # OpenCV deja de dar fotogramas sin avisar si el archivo está dañado o cortado: se compara con lo esperado.
    leido_pct = pct_leido(n, total)
    if leido_pct < PCT_LEIDO_MINIMO:
        corte = f" (se cortó en {formato_minuto(lectura.cortado_en_s)})" if lectura.cortado_en_s is not None else ""
        decir(f"Aviso: solo se han podido leer {n} de los {total} fotogramas esperados del tramo ({leido_pct} %){corte}; el archivo puede estar dañado o cortado.")

    # Lo que entra en la capa de jugadas (pasos 3 a 5), que ya no necesita ni la GPU ni el vídeo. Con --guardar-pasada se
    # guarda, para rehacer esa capa con otro código en segundos (`rehacer.py`).
    datos_acta = json.loads(config.plantillas.read_text(encoding="utf-8")) if config.plantillas is not None else None
    estado = EstadoPasada(
        config=config, info=info, fin_s=fin_s, dt=dt, fps_real=fps_real, vecinos=vecinos, campo=campo, analizador=analizador,
        datos_acta=datos_acta, instantes=instantes, pista=pista, seguidor=seguidor, historial=historial, apariencias=apariencias,
        identificacion=None if cola is None else {
            "lecturas": cola.leidas, "junto_al_balon": lecturas_medio, "recortes_apariencia": cola_aspecto.recortes if cola_aspecto is not None else 0,
        },
        colores=clasificador.colores, reparto=reparto, muestras=muestras, calibracion=calibrador.resumen(),
        pasada={
            "fotogramas_esperados": total, "pct_leido": leido_pct, "lectura_cortada_s": lectura.cortado_en_s, "con_balon_fotograma": vistos,
            "personas_fuera_del_campo": fuera_de_campo, "vecinos_mirados": detector.intermedios_mirados, "vecinos_enteros_mirados": detector.vecinos_enteros,
            "balones_descartados": detector.descartados, "balones_flojos_descartados": detector.flojos_descartados,
        },
        parametros={
            "device": modelos.device, "seguidor": detector.rastreador.nombre, "motor_jugadores": modelos.jugadores.motor,
            "motor_balon": modelos.balon.motor, "motor_campo": modelos.campo.motor, "modelos": modelos.rutas,
        },
        rendimiento={**crono.resumen(n, pasada_activa_s, config.solapar), "grafos": grabados},
        tiempos=tiempos, anotado=renderizador is not None, antes_s=time.time() - inicio_total,
    )
    if config.guardar_pasada or os.environ.get("TT_GUARDAR_PASADA"):
        ruta = guardar_pasada(estado, config.salida)
        decir(f"Guardado lo que entra en la capa de jugadas ({ruta.name}, {ruta.stat().st_size / 2**20:.0f} MB): `tt-analisis rehacer-jugadas` la repite sin la GPU.")
    return _jugadas_y_salidas(estado, decir, progreso, directo, inicio_total, lector=lector, guarda=guarda)


def _jugadas_y_salidas(
    estado: EstadoPasada, decir, progreso: Avance, directo: Directo, inicio_total: float, lector=None, guarda: GuardaZonas | None = None,
    rehecho: dict | None = None,
) -> dict:
    """Pasos 3 a 5: lo que sale de lo visto en la pasada (`estado`). Lo llama `analizar` al acabar la pasada y
    `rehacer_jugadas` con lo que esta guardó. `lector` y `guarda`, solo para el material del lector (`--guardar-dorsales`);
    `rehecho`, de dónde sale lo rehecho."""
    inicio = time.time()
    config, info, campo, instantes, seguidor = estado.config, estado.info, estado.campo, estado.instantes, estado.seguidor
    pista, historial, apariencias, dt, vecinos, fin_s = estado.pista, estado.historial, estado.apariencias, estado.dt, estado.vecinos, estado.fin_s
    analizador, datos_acta, identificacion, pasada, parametros = estado.analizador, estado.datos_acta, estado.identificacion, estado.pasada, estado.parametros
    tiempos = dict(estado.tiempos)

    # 3. Lo que sale de las posiciones. Primero la trayectoria del balón (fuera los objetos fijos y los avistamientos
    # sueltos, y se rellenan los huecos cortos); después quién es quién y, con eso, `jugadas.analizar_jugadas`.
    progreso.punto_de_control()
    progreso.etapa("jugadas")
    progreso.fase("salidas", 0, 3)
    t0 = time.time()
    # Para la repetición (`repeticion.py`): lo que vio el detector en cada instante, antes de completar y limpiar.
    balon_crudo = [(inst.balon_imagen, inst.balon_confianza) if inst.balon_imagen is not None else None for inst in instantes] if config.repeticion else None
    # El balón de los fotogramas vecinos, a los instantes que no lo tenían.
    pista_xy = [(t, (float(c[0]), float(c[1])), conf) for t, c, conf in pista]
    intermedios = completar_con_intermedios(instantes, pista_xy, 1 / info.fps, campo, MARGEN_CAMPO_CM, vecinos=max(1, vecinos))
    if vecinos:
        decir(
            f"Fotogramas vecinos de los que no tenían balón: visto en {intermedios.detecciones} de {pasada['vecinos_mirados']} mirados "
            f"({pasada['vecinos_enteros_mirados']} enteros); {intermedios.completados} instantes sin balón lo tienen por ellos."
        )
    relleno = limpiar_trayectoria(instantes)
    balon_cobertura = cobertura(instantes)
    for objeto in relleno.objetos_fijos:
        decir(f"Objeto quieto en el campo tomado por el balón en ({objeto.centro[0] / 100:.0f} m, {objeto.centro[1] / 100:.0f} m): {objeto.permanencias} veces entre {formato_minuto(objeto.t_primera)} y {formato_minuto(objeto.t_ultima)}; se descarta.")

    acta_oficial = leer_acta(datos_acta)

    # Las dos partes y, con ellas, los porteros: quién es portero, por la equipación que no es de ningún equipo ni del
    # árbitro y vive pegada a una portería, y de qué equipo, por la portería que defiende en esa parte (en el descanso
    # cambian de campo), no por quién tiene más cerca. Hacia dónde ataca cada equipo lo dicen sus jugadores de campo. Un
    # vídeo de una sola parte no tiene descanso que buscar.
    duracion_tramo = fin_s - config.segmento.inicio
    una_parte = acta_oficial is not None and acta_oficial.parte != "partido"
    descanso_previo = buscar_descanso(instantes, dt, duracion_tramo, config.descanso, una_parte)
    mitad_de = mitades(instantes, descanso_previo)
    if os.environ.get("TT_GUARDAR_TRAMOS"):
        guardar_para_porteros(config.salida / "porteros.pkl", instantes, mitad_de, descanso_previo, seguidor, datos_acta, config.saque, historial)
    porteros = colocar_porteros(instantes, mitad_de, seguidor.memorias, campo)
    # Para la repetición: el tracklet de cada persona ya con los porteros colocados, antes de pasar a su jugador.
    tracklets_previos = [[p.tracklet for p in inst.personas] for inst in instantes] if config.repeticion else None
    decir(describir_porteros(porteros, instantes, dt))
    # Para el material del lector: en la pasada aún no se sabía quién era portero.
    muestras_portero = muestras_de_porteros(instantes, porteros) if guarda is not None else []

    # Quién es quién: con el dorsal leído, los tracklets de un mismo jugador pasan a ser uno solo (y como mucho los que
    # jugaron por equipo) antes de contar toques, pases y acciones. Los minutos del acta necesitan el descanso.
    resolucion: Resolucion | None = None
    if identificacion is not None:
        plantillas = leer_plantillas(datos_acta, config.saque, descanso_previo) if datos_acta else []
        tramos = tramos_de(instantes, seguidor, porteros, mitad_de, historial)
        if os.environ.get("TT_GUARDAR_TRAMOS"):
            # Para afinar `identidades.resolver` sin repetir la pasada (solo en pruebas).
            import pickle

            mejores = {t.tracklet: seguidor.memorias[t.tracklet].mejor_dorsal_por_equipo for t in tramos if t.tracklet in seguidor.memorias}
            (config.salida / "tramos.pkl").write_bytes(pickle.dumps({"tramos": tramos, "plantillas": plantillas, "mejores": mejores}))
            if apariencias is not None:
                (config.salida / "apariencias.pkl").write_bytes(pickle.dumps(apariencias))
        resolucion = resolver(tramos, plantillas, apariencias=apariencias)
        if guarda is not None:
            etiquetas_portero: dict[str, dict] = {}
            # Sin el acta, el dorsal del portero no es seguro: sus zonas no se guardan.
            if plantillas and muestras_portero:
                etiquetas_portero = guardar_zonas_porteros(muestras_portero, resolucion, lector, config, info, guarda, decir)
            escribir_etiquetas_dorsales(resolucion, tramos, plantillas, guarda, config, etiquetas_portero)
        equipo_de = {x.id: x.equipo for x in resolucion.identidades}
        for inst in instantes:
            for persona in inst.personas:
                persona.tracklet = resolucion.asignacion.get(persona.tracklet, persona.tracklet)
                # Un jugador identificado es de su equipo en todos sus fotogramas, también en los que el voto de la
                # camiseta aún no se había decidido.
                persona.equipo = equipo_de.get(persona.tracklet, persona.equipo)
        por_equipo = {e: sum(1 for x in resolucion.identidades if x.equipo == e) for e in (0, 1)}
        decir(
            f"Dorsales: {identificacion['lecturas']} lecturas ({identificacion['junto_al_balon']} junto al balón entre dos fotogramas); {len(tramos)} tracklets en {resolucion.cadenas} cadenas; "
            f"{por_equipo[0]} y {por_equipo[1]} jugadores identificados por el dorsal"
            + (" (con el acta oficial)" if plantillas else "") + "."
        )
        for e, r in sorted(resolucion.apariencia.items()):
            if r.get("umbral") is None:
                decir(f"Aspecto, equipo {e}: no se usa; probado con {r['probadas']} cadenas identificadas, no acierta en el 95 % de las que daría.")
            else:
                decir(
                    f"Aspecto, equipo {e}: probado con {r['probadas']} cadenas identificadas, acierta {r['aciertos']} de {r['dadas']}; "
                    f"da su jugador a {r['asignadas']} cadenas sin dorsal ({formato_minuto(r['segundos'])} a la vista)."
                )
        progreso.estado(forzar=True, jugadores_identificados=por_equipo[0] + por_equipo[1])

    # Con el mismo descanso con el que se colocó a los porteros.
    jugadas = analizar_jugadas(instantes, dt, duracion_tramo, descanso_previo, acta_oficial, resolucion.lado_de_equipo if resolucion is not None else None, saque_s=config.saque, una_parte=una_parte)
    toques, resumenes, resultado_eventos = jugadas.toques, jugadas.resumenes, jugadas.eventos
    descanso, direcciones, posesion, secs, acciones = jugadas.descanso, jugadas.direcciones, jugadas.posesion, jugadas.secuencias, jugadas.acciones
    if seguidor.cortes:
        decir(f"Tracklets partidos porque el seguidor juntó a jugadores de los dos equipos: {seguidor.cortes}.")
    if seguidor.cruces:
        decir(f"Cruces de dos jugadores del mismo equipo: {seguidor.cruces}; sus tracklets se parten al separarse y quién es quién lo dice el dorsal.")
    if descanso is not None and not any(e.tipo == "paron" and e.t_fin == descanso for e in resultado_eventos) and config.descanso is None:
        decir(f"Sin pausa de descanso en el vídeo: la segunda parte empieza en {formato_minuto(descanso)}, cuando los equipos cambian de campo.")
    if jugadas.goles_quitados:
        decir(f"El acta oficial da menos goles que el vídeo: {jugadas.goles_quitados} candidatos a gol descartados.")
    tiempos["estadisticas_s"] = time.time() - t0
    faltas = sum(1 for e in resultado_eventos if e.tipo == "falta")
    fueras_de_juego = sum(1 for e in resultado_eventos if e.tipo == "fuera_de_juego")
    progreso.estado(forzar=True, toques=len(toques), eventos=len(resultado_eventos), jugadores_seguidos=len(resumenes))
    decir(f"Posesión y jugadas: {len(toques)} toques de balón y {len(resultado_eventos)} eventos detectados ({faltas} faltas y {fueras_de_juego} fueras de juego candidatos).")
    en_juego = jugadas.en_juego.a_dict(dt) if jugadas.en_juego is not None else None
    if en_juego is not None:
        decir(
            f"Balón en juego el {en_juego['pct_en_juego']} % del tramo; en ese tiempo, visto en el {en_juego['pct_visto']} % "
            f"y con trayectoria en el {en_juego['pct_con_trayectoria']} %."
        )

    # 4. El modelo de lenguaje, si se pidió.
    llm = crear_modelo(config.llm, config.modelo_llm)
    dorsales: dict[int, dict] = {}
    resumen_llm = None
    if llm is not None:
        progreso.punto_de_control()
        progreso.etapa("revision")
        t0 = time.time()
        leer = lambda t: fotograma_en(config.video, t)  # noqa: E731
        with tqdm(desc=f"Revisando eventos con {llm.nombre}", unit="evento", disable=config.progreso) as barra:
            confirmar_eventos(resultado_eventos, leer, llm, avance=lambda k: barra.update(k - barra.n))
        recortes_por_tracklet = {tid: [r.imagen for r in m.recortes] for tid, m in seguidor.memorias.items() if m.recortes and m.clase != CLASE_ARBITRO}
        with tqdm(desc="Leyendo dorsales", unit="jugador", disable=config.progreso) as barra:
            dorsales = leer_dorsales(recortes_por_tracklet, llm, avance=lambda k: barra.update(k - barra.n))
        tiempos["llm_s"] = time.time() - t0

    # 5. Salidas.
    progreso.punto_de_control()
    progreso.etapa("salidas")
    progreso.fase("salidas", 1, 3)
    t0 = time.time()
    recortes = escribir_recortes(seguidor.memorias, config.salida)
    if resolucion is not None:
        recortes.update(escribir_recortes_jugadores(resolucion, seguidor, config.salida, porteros, instantes, lambda t: fotograma_en(config.video, t)))
    mapas = escribir_mapas(resumenes, config.salida, config.nombres_equipos)
    posiciones = escribir_posiciones(instantes, config.salida)
    clips = 0
    if config.clips:
        with tqdm(desc="Clips de eventos", unit="clip", disable=config.progreso) as barra:
            clips = extraer_clips(resultado_eventos, config.video, config.salida, config.clip_margen_s, ruta_ffmpeg(), avance=lambda k: barra.update(k - barra.n))
    colores = [color_hex(c) for c in estado.colores]
    equipos_json = [{"id": e, "nombre": config.nombres_equipos[e], "color": colores[e]} for e in (0, 1)]
    video_json = datos_del_video(config, info, fin_s)
    repeticion = None
    if config.repeticion:
        # Para ver en el visor lo que hizo el analizador (`tt-analisis visor`); no cambia nada de lo que propone. Lo
        # provisional se borra después, con la definitiva ya escrita.
        directo.detener()
        repeticion = escribir_repeticion(
            config.salida, instantes, tracklets_previos, balon_crudo, pista_xy, analizador=analizador, video=video_json, dt=dt, equipos=equipos_json,
            campo=campo, jugadas=jugadas, resolucion=resolucion, recortes=recortes, objetos_fijos=relleno.objetos_fijos,
        )
        directo.terminar()
    tiempos["salidas_s"] = time.time() - t0
    tiempos["total_s"] = time.time() - inicio_total

    progreso.fase("salidas", 2, 3)
    con_balon = balon_cobertura["vistos"]
    vistos = pasada["con_balon_fotograma"]
    calibrados = sum(1 for i in instantes if i.calibrado)
    analisis = {
        "version": __version__,
        "analizador": analizador,
        "video": video_json,
        "parametros": {"fps_muestreo": round(estado.fps_real, 2), "vecinos_balon": vecinos, "vecinos_balon_s": config.vecinos_balon_s, "vecinos_enteros": config.vecinos_enteros, "device": parametros["device"], "camara": config.camara, "seguidor": parametros["seguidor"], "motor_jugadores": parametros["motor_jugadores"], "motor_balon": parametros["motor_balon"], "motor_campo": parametros["motor_campo"], "llm": llm.nombre if llm else None, "modelos": parametros["modelos"], "campo_m": [campo.largo / 100, campo.ancho / 100]},
        "llm": llm.nombre if llm else None,
        "equipos": equipos_json,
        "direcciones_ataque": {f"equipo{e}_mitad{m}": d for (e, m), d in sorted(direcciones.items())},
        "descanso_s": descanso,
        "calibracion": estado.calibracion,
        "recuento": {
            # `con_balon` y `pct_con_balon` son el balón **visto** por el detector (lo que guarda la app): en su fotograma o,
            # desde la 2.3.0, en uno de sus vecinos (`balon_vecinos`); solo en su fotograma, `con_balon_fotograma`.
            # Con los huecos rellenados por la trayectoria, `con_balon_interpolado` y `pct_con_balon_total`.
            "fotogramas": len(instantes), "con_balon": con_balon, "pct_con_balon": round(100 * con_balon / len(instantes), 1),
            "con_balon_fotograma": vistos, "pct_con_balon_fotograma": round(100 * vistos / len(instantes), 1),
            "balon_vecinos": {**intermedios.a_dict(), "mirados": pasada["vecinos_mirados"], "enteros": pasada["vecinos_enteros_mirados"], "vecinos": vecinos},
            "con_balon_interpolado": balon_cobertura["interpolados"], "pct_con_balon_total": balon_cobertura["pct_con_balon"], "balon_relleno": relleno.a_dict(),
            # Desde la 2.3.0: lo mismo solo con el balón en juego (`en_juego.py`), que es cuando importa verlo.
            "balon_en_juego": en_juego,
            "pct_calibrados": round(100 * calibrados / len(instantes), 1),
            # Lo que se pudo leer del archivo frente a lo esperado: por debajo del 95 % la app lo marca como calidad baja.
            "fotogramas_esperados": pasada["fotogramas_esperados"], "pct_leido": pasada["pct_leido"], "lectura_cortada_s": pasada["lectura_cortada_s"],
            "muestras_equipos": estado.muestras, "reparto_equipos": list(estado.reparto), "tracklets": len(resumenes), "tracklets_partidos": seguidor.cortes, "cruces_partidos": seguidor.cruces, "toques": len(toques), "secuencias": len(secs),
            "balones_descartados": pasada["balones_descartados"], "balones_flojos_descartados": pasada["balones_flojos_descartados"], "personas_fuera_del_campo": pasada["personas_fuera_del_campo"], "clips": clips,
        },
        "posesion": posesion.a_dict(),
        "eventos": [e.a_dict() for e in resultado_eventos],
        "tracklets": [dict(r.a_dict(), acciones=acciones.get(tid, None).a_dict() if tid in acciones else None, recorte=recortes.get(tid), dorsal_llm=dorsales.get(tid)) for tid, r in sorted(resumenes.items())],
        "identificacion": None if resolucion is None else {
            "lecturas": identificacion["lecturas"],
            "lecturas_junto_al_balon": identificacion["junto_al_balon"],
            "recortes_apariencia": identificacion["recortes_apariencia"],
            "apariencia": {str(e): r for e, r in resolucion.apariencia.items()},
            "cadenas": resolucion.cadenas,
            "lado_de_equipo": {str(e): lado for e, lado in resolucion.lado_de_equipo.items()},
            "segundos_sin_identificar": {str(e): s for e, s in resolucion.segundos_sin_identificar.items()},
            "jugadores": [
                {"id": x.id, "equipo": x.equipo, "dorsal": x.dorsal, "nombre": x.nombre, "tracklets": len(x.tracklets), "evidencia": round(x.evidencia, 2), "confianza": x.confianza, "por_continuidad": x.por_continuidad, "por_descarte": x.por_descarte}
                for x in resolucion.identidades
            ],
        },
        "archivos": {"mapas": mapas, "posiciones": posiciones, "anotado": "anotado.mp4" if estado.anotado else None, "repeticion": repeticion},
        "tiempos": {k: round(v, 1) for k, v in tiempos.items()},
        # Desde la 2.4.0: en qué se va el tiempo de la pasada principal (`tuberia.Cronometro.resumen`).
        "rendimiento": estado.rendimiento,
    }
    if rehecho is not None:
        analisis["rehecho"] = rehecho
    acta = acta_propuesta(resultado_eventos, posesion, resumenes, acciones, dorsales, recortes, config.nombres_equipos, colores, resolucion, acta_oficial, en_juego)
    if llm is not None:
        resumen_llm = redactar_resumen(analisis, llm)
    escribir_json(analisis, config.salida / "analisis.json")
    escribir_json(acta, config.salida / "acta-propuesta.json")
    (config.salida / "resumen.md").write_text(resumen_markdown(analisis, acta, resumen_llm), encoding="utf-8")
    decir(
        f"Balón visto en el {analisis['recuento']['pct_con_balon']} % de los fotogramas y, con los huecos rellenados, en el {balon_cobertura['pct_con_balon']} %; "
        f"posesión {posesion.porcentaje(0)} / {posesion.porcentaje(1)} sobre {posesion.segundos_atribuidos:.0f} s con balón (confianza {posesion.confianza})."
    )
    if rehecho is None:
        decir(f"Listo en {tiempos['total_s'] / 60:.1f} min: {config.salida / 'resumen.md'}")
    else:
        decir(f"Rehecho en {time.time() - inicio:.1f} s: {config.salida / 'resumen.md'}")
    progreso.fin()
    return analisis


def rehacer_jugadas(origen: Path, salida: Path, informar=print, estado: EstadoPasada | None = None) -> dict:
    """Los pasos 3 a 5 otra vez, con el código de ahora, sobre lo que guardó la pasada de `origen` (`--guardar-pasada`;
    `estado`, si ya se leyó): sin la GPU ni el vídeo, en segundos. Deja en `salida` un análisis completo, con su repetición
    para el visor y un bloque `rehecho` que dice de dónde sale. Sin clips, sin modelo de lenguaje y sin el material del
    lector."""
    if estado is None:
        estado = leer_pasada(origen)
    version_pasada = estado.analizador.get("version")
    estado.config = replace(estado.config, salida=salida, repeticion=True, clips=False, llm="none", guardar_dorsales=None, progreso=False, guardar_pasada=False)
    estado.analizador = {**estado.analizador, "version": __version__}
    # Las medidas del campo valen para todo el paquete (`campo.configurar_campo`): las mismas que en la pasada.
    configurar_campo(estado.campo.largo, estado.campo.ancho)
    salida.mkdir(parents=True, exist_ok=True)
    informar(f"Rehaciendo con {NOMBRE} {__version__} lo que va tras la pasada de {origen} ({NOMBRE} {version_pasada}, {len(estado.instantes)} fotogramas).")
    rehecho = {"de": str(origen.resolve()), "version_de_la_pasada": version_pasada}
    return _jugadas_y_salidas(estado, informar, Avance(False), Directo(salida, activo=False), time.time() - estado.antes_s, rehecho=rehecho)


def tramos_de(
    instantes: list[Instante], seguidor: Seguidor, porteros: Porteros | None = None, mitad_de: list[int] | None = None,
    lecturas: HistorialLecturas | None = None,
) -> list[Tramo]:
    """Cada tracklet de jugador como tramo para `identidades.resolver`: equipo, primer y último sitio, alto y lecturas.
    El equipo, el de su camiseta; el de un portero y el del que el detector tomó por portero pero viste como un equipo,
    el que `porteros.aplicar_porteros` ya puso en cada fotograma (los fotogramas sin equipo no cuentan). De esos dos,
    todas las lecturas del dorsal: el color de la camiseta no dice bien de qué equipo son. Del portero en cuyo tracklet
    el seguidor saltó a otra persona, solo las de su rato y lejos de los cortes (`lecturas`, con el segundo de cada una):
    las de fuera pueden ser de la otra persona; sin el segundo, ninguna. De los porteros, con las partes
    (`mitad_de`, una por instante), en cuál empiezan y, en cada una, cuánto se les ve con el campo calibrado y cuánto de
    eso al lado o debajo de la portería que defiende su equipo."""
    porteros = porteros or Porteros()
    pasos = np.diff([inst.t for inst in instantes])
    dt = float(np.median(pasos)) if len(pasos) else 0.0
    datos: dict[int, dict] = {}
    for i, inst in enumerate(instantes):
        mitad = mitad_de[i] if mitad_de is not None and i < len(mitad_de) else None
        for p in inst.personas:
            if p.equipo is None or not p.es_jugador:
                continue
            d = datos.get(p.tracklet)
            alto = p.caja[3] - p.caja[1]
            if d is None:
                d = datos[p.tracklet] = {
                    "equipos": {}, "t_ini": inst.t, "ini": p.xy_imagen, "ini_campo": p.xy_campo, "t_fin": inst.t, "fin": p.xy_imagen, "fin_campo": p.xy_campo,
                    "altos": [alto], "mitad": mitad, "porteria": {},
                }
            else:
                d.update(t_fin=inst.t, fin=p.xy_imagen, fin_campo=p.xy_campo)
                if len(d["altos"]) < 50:
                    d["altos"].append(alto)
            d["equipos"][p.equipo] = d["equipos"].get(p.equipo, 0) + 1
            if mitad is not None and porteros.es_portero(p.tracklet):
                junto = porteros.junto_a_su_porteria(p.xy_campo, p.equipo, mitad)
                if junto is not None:
                    segundos, cerca = d["porteria"].get(mitad, (0.0, 0.0))
                    d["porteria"][mitad] = (segundos + dt, cerca + (dt if junto else 0.0))
    tramos = []
    for tid, d in datos.items():
        memoria = seguidor.memorias.get(tid)
        visto = max(d["equipos"], key=d["equipos"].get)
        portero = porteros.es_portero(tid)
        if portero or tid in porteros.de_campo:
            if tid not in porteros.mezclados:
                votos = _todas_las_lecturas(memoria)
            elif lecturas is not None:
                votos = lecturas.votos(tid, lambda tiempos, tid=tid: porteros.lejos_de_los_cortes(tid, tiempos))
            else:
                votos = np.zeros(99)
            equipo = visto
        else:
            votos = memoria.votos_dorsal if memoria is not None and memoria.votos_dorsal is not None else np.zeros(99)
            equipo = memoria.equipo if memoria is not None and memoria.equipo is not None else visto
        tramos.append(
            Tramo(
                tid, equipo, d["t_ini"], d["t_fin"], d["ini"], d["fin"], float(np.median(d["altos"])), votos, d["ini_campo"], d["fin_campo"], portero,
                porteros.por_equipacion(tid), d["mitad"], d["porteria"], vestido_de_portero=porteros.vestido_de_portero(tid),
                tras_cruce=tid in seguidor.tras_cruce,
            )
        )
    return tramos


def _todas_las_lecturas(memoria: MemoriaTracklet | None) -> np.ndarray:
    if memoria is None or not memoria.votos_dorsal_por_equipo:
        return np.zeros(99)
    return np.sum(list(memoria.votos_dorsal_por_equipo.values()), axis=0)


def guardar_para_porteros(
    ruta: Path, instantes: list[Instante], mitad_de: list[int], descanso: float | None, seguidor: Seguidor, datos_acta: dict | None, saque: float,
    historial: HistorialLecturas | None = None,
) -> None:
    """Para afinar la búsqueda de los porteros y `identidades.resolver` sin repetir la pasada (solo en pruebas): los
    instantes antes de ponerles portero, de cada tracklet sus votos, su equipación y su recorte más grande, y cada lectura
    del dorsal con su segundo."""
    import pickle

    import cv2

    memorias = {}
    for tid, m in seguidor.memorias.items():
        recorte = m.recortes[0].imagen if m.recortes else None
        jpeg = cv2.imencode(".jpg", recorte, [int(cv2.IMWRITE_JPEG_QUALITY), 85])[1].tobytes() if recorte is not None and recorte.size else None
        memorias[tid] = {
            "clase": m.clase, "votos_clase": dict(m.votos_clase), "equipo": m.equipo, "votos_equipo": dict(m.votos_equipo),
            "votos_dorsal_por_equipo": dict(m.votos_dorsal_por_equipo), "equipaciones": list(m.equipaciones), "tiempos_equipacion": list(m.tiempos_equipacion), "recorte": jpeg,
        }
    datos = {
        "instantes": instantes, "mitad_de": mitad_de, "descanso": descanso, "memorias": memorias, "datos_acta": datos_acta, "saque": saque,
        "lecturas": historial.para_guardar() if historial is not None else None,
    }
    ruta.write_bytes(pickle.dumps(datos))


def recorte_de_portero(ident_id: int, momentos: list[tuple[float, int]], instantes: list[Instante], memorias: dict, leer=None) -> np.ndarray | None:
    """El recorte del portero identificado `ident_id`, solo de uno de sus `momentos` (`porteros.momentos_de_portero`: viste
    como él y cae en el rato en que su tracklet es él), leído del vídeo con `leer(t)`: de los RECORTES_PORTERO momentos en
    que se le ve más alto y de pie (la caja más grande suele ser la del portero agachado o estirado, borroso), el más
    nítido. Sin vídeo, uno de los que guardó el seguidor justo en uno de esos momentos. None si no hay ninguno: mejor sin
    foto que con la de otro."""
    import cv2

    cajas: dict[float, tuple] = {}
    for inst in instantes:
        for p in inst.personas:
            if p.tracklet == ident_id:
                cajas[round(inst.t, 3)] = p.caja
    elegibles = [(t, tid, cajas[round(t, 3)]) for t, tid in momentos if round(t, 3) in cajas]
    if not elegibles:
        return None
    de_pie = [x for x in elegibles if (x[2][3] - x[2][1]) >= PROPORCION_DE_PIE * max(1e-6, x[2][2] - x[2][0])]
    elegibles = sorted(de_pie or elegibles, key=lambda x: -(x[2][3] - x[2][1]))
    if leer is not None:
        mejor: tuple[float, np.ndarray] | None = None
        for t, _, caja in elegibles[:RECORTES_PORTERO]:
            imagen = leer(t)
            recorte = recortar(imagen, caja, margen=6) if imagen is not None else None
            if recorte is None or not recorte.size:
                continue
            nitidez = float(cv2.Laplacian(cv2.cvtColor(recorte, cv2.COLOR_BGR2GRAY), cv2.CV_64F).var())
            if mejor is None or nitidez > mejor[0]:
                mejor = (nitidez, recorte)
        if mejor is not None:
            return mejor[1]
    justos = {(tid, round(t, 3)) for t, tid, _ in elegibles}
    guardados = [
        r
        for tid in {tid for _, tid, _ in elegibles}
        if tid in memorias
        for lista in memorias[tid].recortes_por_equipo.values()
        for r in lista
        if (tid, round(r.t, 3)) in justos
    ]
    return max(guardados, key=lambda r: r.area).imagen if guardados else None


def recorte_con_su_dorsal(dorsal: int, tracklets: list[int], memorias: dict, mezclados: set[int]) -> np.ndarray | None:
    """Del portero sin ningún momento en que se le vea vestido como él (por ejemplo, si no se sabe cómo viste el portero
    de su equipo): el recorte en el que mejor se le leyó su dorsal, de un tracklet en el que el seguidor no saltó a otra
    persona. None si no se le leyó: mejor sin foto que con la de otro."""
    leidos = [
        m
        for t in tracklets
        if t in memorias and t not in mezclados
        for m in memorias[t].mejor_dorsal_por_equipo.values()
        if m[1] == dorsal
    ]
    return max(leidos, key=lambda m: m[0])[2] if leidos else None


def escribir_recortes_jugadores(
    resolucion: Resolucion, seguidor: Seguidor, salida: Path, porteros: Porteros | None = None, instantes: list[Instante] | None = None, leer=None
) -> dict[int, str]:
    """De cada jugador identificado, el recorte en el que mejor se leyó su dorsal. El del portero (su línea sale de los
    tramos de un portero), solo de un momento en que viste claramente como él dentro del rato en que su tracklet es él
    (`recorte_de_portero`) o, si no lo hay, uno en el que se le leyó su dorsal (`recorte_con_su_dorsal`); nunca uno
    cualquiera de sus tracklets: los recortes que guarda el seguidor pueden ser de cuando saltó a otra persona, y en el
    J8 la línea del 1 del Vallobín salía con un jugador del Real Titánico."""
    import cv2

    from .porteros import momentos_de_portero

    rutas: dict[int, str] = {}
    carpeta = salida / "recortes"
    carpeta.mkdir(parents=True, exist_ok=True)
    for ident in resolucion.identidades:
        if ident.portero and porteros is not None:
            de_portero = [t for t in ident.tracklets if porteros.es_portero(t)]
            momentos = momentos_de_portero(porteros, ident.equipo, de_portero, seguidor.memorias)
            imagen = recorte_de_portero(ident.id, momentos, instantes or [], seguidor.memorias, leer)
            if imagen is None:
                imagen = recorte_con_su_dorsal(ident.dorsal, de_portero, seguidor.memorias, porteros.mezclados)
            if imagen is not None and imagen.size:
                ruta = carpeta / f"jugador-{ident.id}.jpg"
                cv2.imwrite(str(ruta), imagen, [int(cv2.IMWRITE_JPEG_QUALITY), 92])
                rutas[ident.id] = str(ruta.relative_to(salida))
            continue
        mejor = None
        for tid in ident.tracklets:
            m = seguidor.memorias.get(tid)
            if m is None or m.mejor_dorsal is None or m.mejor_dorsal[1] != ident.dorsal:
                continue
            if mejor is None or m.mejor_dorsal[0] > mejor[0]:
                mejor = m.mejor_dorsal
        imagen = mejor[2] if mejor is not None else None
        if imagen is None:
            con_recorte = [seguidor.memorias[t] for t in ident.tracklets if t in seguidor.memorias and seguidor.memorias[t].recortes]
            if con_recorte:
                imagen = max((m.recortes[0] for m in con_recorte), key=lambda r: r.area).imagen
        if imagen is None or not imagen.size:
            continue
        ruta = carpeta / f"jugador-{ident.id}.jpg"
        cv2.imwrite(str(ruta), imagen, [int(cv2.IMWRITE_JPEG_QUALITY), 92])
        rutas[ident.id] = str(ruta.relative_to(salida))
    return rutas


def muestras_de_porteros(instantes: list[Instante], porteros: Porteros) -> list[tuple[float, int, int, tuple, bool]]:
    """Para el material del lector (`--guardar-dorsales`): en cada fotograma, los porteros por la equipación dentro de su
    rato (lo de fuera ya pasó a otro tracklet) con tamaño para leer el dorsal: segundo, tracklet, equipo, caja y si
    alguien lo tapa."""
    muestras = []
    for inst in instantes:
        suyos = [k for k, p in enumerate(inst.personas) if p.tracklet in porteros.equipo_de and p.caja[3] - p.caja[1] >= ALTO_MIN_PX]
        if not suyos:
            continue
        tapada = tapados(np.array([p.caja for p in inst.personas], dtype=np.float64))
        for k in suyos:
            p = inst.personas[k]
            muestras.append((inst.t, p.tracklet, porteros.equipo_de[p.tracklet], tuple(p.caja), bool(tapada[k])))
    return muestras


def guardar_zonas_porteros(muestras, resolucion: Resolucion, lector, config: Configuracion, info, guarda: GuardaZonas, decir=print) -> dict[str, dict]:
    """Las zonas del dorsal de los porteros identificados, etiquetadas con su número o sin número
    (`dorsales.etiquetar_portero`), en un bloque más del material de `entrenar-dorsales`. Devuelve las etiquetas de cada
    rato (su «tracklet» en el material)."""
    from collections import defaultdict

    import cv2

    from .dorsales import ALTO, ANCHO, PESOS_PARSEQ, TRACKLET_PORTERO_BASE, LectorDorsales, etiquetar_portero, nota_portero, ratos_portero

    identidad = {x.id: x for x in resolucion.identidades}
    suyas = []
    for t, tracklet, equipo, caja, tapado in muestras:
        ident = identidad.get(resolucion.asignacion.get(tracklet))
        # Solo con el dorsal de la línea del portero de su equipo: con el de un jugador de campo, sus espaldas enseñarían
        # el número de otro.
        if ident is not None and ident.portero and ident.equipo == equipo and ident.dorsal:
            suyas.append((t, equipo, ident.dorsal, caja, tapado))
    # Los fotogramas otra vez, desde el principio del tramo como en la pasada para que cada índice sea el mismo.
    por_indice: dict[int, list[int]] = defaultdict(list)
    for k, muestra in enumerate(suyas):
        por_indice[int(round(muestra[0] * info.fps))].append(k)
    zonas: dict[int, np.ndarray] = {}
    for indice, imagen in fotogramas_elegidos(info.ruta, por_indice, desde=int(config.segmento.inicio * info.fps)):
        for k in por_indice[indice]:
            zona = zona_dorsal(imagen, suyas[k][3])
            if zona is not None:
                zonas[k] = cv2.resize(zona, (ANCHO, ALTO), interpolation=cv2.INTER_CUBIC)
    if not zonas:
        return {}
    validas = sorted(zonas)
    imagenes = [zonas[k] for k in validas]
    lectores = [lector]
    publicado = config.modelos / PESOS_PARSEQ
    if getattr(lector, "ruta", None) != publicado and publicado.exists():
        lectores.append(LectorDorsales(publicado, lector.device))
    lecturas = [x.leer(imagenes) for x in lectores]

    elegidas, tracklets, equipos, dorsales, etiquetas, partes = [], [], [], [], {}, []
    for equipo, dorsal in sorted({(suyas[k][1], suyas[k][2]) for k in validas}):
        posiciones = [i for i, k in enumerate(validas) if suyas[k][1:3] == (equipo, dorsal)]
        t = np.array([suyas[validas[i]][0] for i in posiciones])
        nota = nota_portero([[ls[i] for i in posiciones] for ls in lecturas], dorsal)
        etiqueta = etiquetar_portero(t, nota, [suyas[validas[i]][4] for i in posiciones])
        for i, e, r in zip(posiciones, etiqueta, ratos_portero(t)):
            if e < 0:
                continue
            # Los ratos de más allá del 999 van juntos, para no pisar los del dorsal siguiente.
            tracklet = TRACKLET_PORTERO_BASE + equipo * 100_000 + dorsal * 1_000 + min(int(r), 999)
            elegidas.append(imagenes[i])
            tracklets.append(tracklet)
            equipos.append(equipo)
            dorsales.append(dorsal if e > 0 else 0)
            etiquetas[str(tracklet)] = {"dorsal": dorsal, "equipo": equipo, "confianza": 1.0, "parte_tracklet": 1.0}
        partes.append(f"el {dorsal} (equipo {equipo}), {int(np.sum(etiqueta == 1))} de espaldas y {int(np.sum(etiqueta == 0))} sin número")
    if elegidas:
        guarda.guardar_porteros(np.stack(elegidas), np.array(tracklets), np.array(equipos), np.array(dorsales))
    decir(f"Zonas de los porteros para afinar el lector: {'; '.join(partes)}.")
    return etiquetas


def escribir_etiquetas_dorsales(resolucion: Resolucion, tramos: list[Tramo], plantillas, guarda: GuardaZonas, config: Configuracion, porteros: dict[str, dict] | None = None) -> None:
    """Las etiquetas de las zonas guardadas: el dorsal de cada tracklet identificado, con lo que se fía uno de él (la parte
    de las lecturas de su identidad que dicen ese número y la del propio tracklet, entre los dorsales permitidos), y las
    de los ratos de los porteros (`guardar_zonas_porteros`)."""
    import json as _json

    identidad = {x.id: x for x in resolucion.identidades}
    por_tracklet = {}
    for t in tramos:
        ident_id = resolucion.asignacion.get(t.tracklet)
        if ident_id is None or ident_id not in identidad:
            continue
        ident = identidad[ident_id]
        total = float(t.votos.sum())
        por_tracklet[str(t.tracklet)] = {
            "dorsal": ident.dorsal,
            "equipo": ident.equipo,
            "confianza": ident.confianza,
            "parte_tracklet": round(float(t.votos[ident.dorsal - 1]) / total, 3) if total > 0 else 0.0,
        }
    por_tracklet.update(porteros or {})
    datos = {"video": str(config.video), "segmento": [config.segmento.inicio, config.segmento.fin], "acta": bool(plantillas), "tracklets": por_tracklet}
    guarda.carpeta.joinpath("etiquetas.json").write_text(_json.dumps(datos, ensure_ascii=False), encoding="utf-8")
