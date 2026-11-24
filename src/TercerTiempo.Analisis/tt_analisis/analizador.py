"""La cadena completa: separar equipos, detectar y seguir, calibrar el campo, posesión y eventos, y escribir los
informes. `analizar(config)` deja todo en la carpeta de salida y devuelve el JSON del análisis."""

from __future__ import annotations

import os

import shutil
import sys
import time
from pathlib import Path

import numpy as np
import supervision as sv
from tqdm import tqdm

from . import NOMBRE, __version__
from .avance import Avance
from .balon import cobertura, limpiar_trayectoria
from .campo import Calibrador, configurar_campo
from .colores import rasgos_equipacion, tapados
from .config import CLASE_ARBITRO, MARGEN_CAMPO_CM, Configuracion
from .geometria import Campo
from .deteccion import Detector
from .dorsales import ColaDorsales, GuardaZonas, cargar_lector, zona_dorsal
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
from .seguimiento import MemoriaTracklet, Seguidor
from .tipos import Instante, Persona, formato_minuto
from .video import PCT_LEIDO_MINIMO, Lectura, fotograma_en, fotogramas, info_video, pct_leido, recortar

RUTA_FFMPEG_WINGET = Path.home() / "AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/ffmpeg.exe"


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


def analizar(config: Configuracion, informar=print) -> dict:
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
    decir(f"Vídeo: {info.ancho}×{info.alto} a {info.fps:.0f} fps, {formato_minuto(info.duracion_s)}. Tramo {formato_minuto(config.segmento.inicio)} – {formato_minuto(fin_s)}.")
    decir(f"Analizador: {NOMBRE} {__version__}.")
    decir(f"GPU: {describir_gpu()}")

    if config.campo:
        configurar_campo(config.campo[0] * 100, config.campo[1] * 100)
        decir(f"Campo de {config.campo[0]:g} × {config.campo[1]:g} m.")
    modelos = cargar_modelos(config.modelos, config.device)
    decir(f"Detectores: {', '.join(f'{n} ({r})' for n, r in modelos.rutas.items())}.")
    lector = cargar_lector(config.modelos, modelos.device, afinados=config.afinados) if config.dorsales else None
    if lector is not None:
        decir(f"Lector de dorsales: {lector.nombre}.")
    elif config.dorsales:
        decir("Aviso: falta el lector de dorsales (modelos/parseq-bb5792a6.pt; `tt-analisis descargar-modelos`): cada tracklet queda suelto.")
    # Quién analiza el partido: el nombre y la versión del analizador y cada modelo, con su entrenamiento y su huella.
    analizador = {"nombre": NOMBRE, "version": __version__, "modelos": describir_modelos(modelos, config.modelos, lector)}
    guarda = GuardaZonas(config.guardar_dorsales) if lector is not None and config.guardar_dorsales is not None else None
    cola = ColaDorsales(lector, guardar=guarda) if lector is not None else None
    paso = max(1, round(info.fps / config.fps_muestreo))
    fps_real = info.fps / paso
    dt = 1 / fps_real
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
    vistos = calibrados_hasta = 0
    pasada_inicio = time.monotonic()
    pasada_pausa = 0.0
    for fotograma in tqdm(fotogramas(config, info, paso=paso, lectura=lectura), total=total, desc="Analizando", unit="fot", disable=config.progreso):
        pasada_pausa += progreso.punto_de_control()
        n += 1
        progreso.fase("pasada", n, total)
        imagen = fotograma.imagen
        det = detector.detectar(imagen, fotograma.t)
        seguidas = seguidor.registrar(det.personas)
        calibracion = calibrador.calibrar(imagen, fotograma.t, forzar=(n == 1))
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

        equipos, pesos = clasificador.clasificar(imagen, seguidas)
        seguidas = seguidor.votar_equipos(seguidas, equipos, pesos)
        tapada = tapados(seguidas.xyxy) if len(seguidas) else np.zeros(0, dtype=bool)

        personas = []
        for i in range(len(seguidas)):
            tid = int(seguidas.tracker_id[i])
            memoria = seguidor.memorias[tid]
            clase = memoria.clase
            caja = tuple(float(v) for v in seguidas.xyxy[i])
            if not tapada[i]:
                # El color de la equipación, para encontrar a los porteros aunque el detector los tome por jugadores.
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
            if cola is not None and clase != CLASE_ARBITRO:
                zona = zona_dorsal(imagen, caja)
                if zona is not None:
                    cola.poner(tid, zona.copy(), recortar(imagen, caja, margen=6).copy(), seguidor.memorias, int(equipos[i]))

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

        inst = Instante(fotograma.indice, fotograma.t, personas, balon_imagen, balon_campo, calibracion.valida, balon_confianza=balon_confianza)
        instantes.append(inst)
        vistos += balon_imagen is not None
        calibrados_hasta += calibracion.valida
        if progreso.toca():
            activo_s = max(1e-6, time.monotonic() - pasada_inicio - pasada_pausa)
            fps_proceso = n / activo_s
            progreso.estado(
                t_video=round(fotograma.t, 1),
                fotogramas=n,
                jugadores_en_imagen=sum(1 for p in personas if p.clase != CLASE_ARBITRO),
                jugadores_seguidos=sum(1 for m in seguidor.memorias.values() if m.clase != CLASE_ARBITRO),
                balon_pct=round(100 * vistos / n, 1),
                calibrado_pct=round(100 * calibrados_hasta / n, 1),
                fps=round(fps_proceso, 1),
                velocidad=round(fps_proceso / fps_real, 2),
                resta_s=round(max(0, total - n) / fps_proceso),
            )
        if renderizador:
            renderizador.dibujar(imagen, inst)
    if renderizador:
        renderizador.cerrar()
    if cola is not None:
        cola.vaciar(seguidor.memorias)
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

    # 3. Lo que sale de las posiciones. Primero la trayectoria del balón (fuera los objetos fijos y los avistamientos
    # sueltos, y se rellenan los huecos cortos); después quién es quién y, con eso, `jugadas.analizar_jugadas`.
    progreso.punto_de_control()
    progreso.etapa("jugadas")
    progreso.fase("salidas", 0, 3)
    t0 = time.time()
    relleno = limpiar_trayectoria(instantes)
    balon_cobertura = cobertura(instantes)
    for objeto in relleno.objetos_fijos:
        decir(f"Objeto quieto en el campo tomado por el balón en ({objeto.centro[0] / 100:.0f} m, {objeto.centro[1] / 100:.0f} m): {objeto.permanencias} veces entre {formato_minuto(objeto.t_primera)} y {formato_minuto(objeto.t_ultima)}; se descarta.")

    datos_acta = None
    if config.plantillas is not None:
        import json as _json

        datos_acta = _json.loads(config.plantillas.read_text(encoding="utf-8"))
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
        guardar_para_porteros(config.salida / "porteros.pkl", instantes, mitad_de, descanso_previo, seguidor, datos_acta, config.saque)
    porteros = colocar_porteros(instantes, mitad_de, seguidor.memorias, campo)
    decir(describir_porteros(porteros, instantes, dt))

    # Quién es quién: con el dorsal leído, los tracklets de un mismo jugador pasan a ser uno solo (y como mucho los que
    # jugaron por equipo) antes de contar toques, pases y acciones. Los minutos del acta necesitan el descanso.
    resolucion: Resolucion | None = None
    if cola is not None:
        plantillas = leer_plantillas(datos_acta, config.saque, descanso_previo) if datos_acta else []
        tramos = tramos_de(instantes, seguidor, porteros)
        if os.environ.get("TT_GUARDAR_TRAMOS"):
            # Para afinar `identidades.resolver` sin repetir la pasada (solo en pruebas).
            import pickle

            mejores = {t.tracklet: seguidor.memorias[t.tracklet].mejor_dorsal_por_equipo for t in tramos if t.tracklet in seguidor.memorias}
            (config.salida / "tramos.pkl").write_bytes(pickle.dumps({"tramos": tramos, "plantillas": plantillas, "mejores": mejores}))
        resolucion = resolver(tramos, plantillas)
        if guarda is not None:
            escribir_etiquetas_dorsales(resolucion, tramos, plantillas, guarda, config)
        equipo_de = {x.id: x.equipo for x in resolucion.identidades}
        for inst in instantes:
            for persona in inst.personas:
                persona.tracklet = resolucion.asignacion.get(persona.tracklet, persona.tracklet)
                # Un jugador identificado es de su equipo en todos sus fotogramas, también en los que el voto de la
                # camiseta aún no se había decidido.
                persona.equipo = equipo_de.get(persona.tracklet, persona.equipo)
        por_equipo = {e: sum(1 for x in resolucion.identidades if x.equipo == e) for e in (0, 1)}
        decir(
            f"Dorsales: {cola.leidas} lecturas; {len(tramos)} tracklets en {resolucion.cadenas} cadenas; "
            f"{por_equipo[0]} y {por_equipo[1]} jugadores identificados por el dorsal"
            + (" (con el acta oficial)" if plantillas else "") + "."
        )
        progreso.estado(forzar=True, jugadores_identificados=por_equipo[0] + por_equipo[1])

    # Con el mismo descanso con el que se colocó a los porteros.
    jugadas = analizar_jugadas(instantes, dt, duracion_tramo, descanso_previo, acta_oficial, resolucion.lado_de_equipo if resolucion is not None else None, saque_s=config.saque, una_parte=una_parte)
    toques, resumenes, resultado_eventos = jugadas.toques, jugadas.resumenes, jugadas.eventos
    descanso, direcciones, posesion, secs, acciones = jugadas.descanso, jugadas.direcciones, jugadas.posesion, jugadas.secuencias, jugadas.acciones
    if seguidor.cortes:
        decir(f"Tracklets partidos porque el seguidor juntó a jugadores de los dos equipos: {seguidor.cortes}.")
    if descanso is not None and not any(e.tipo == "paron" and e.t_fin == descanso for e in resultado_eventos) and config.descanso is None:
        decir(f"Sin pausa de descanso en el vídeo: la segunda parte empieza en {formato_minuto(descanso)}, cuando los equipos cambian de campo.")
    if jugadas.goles_quitados:
        decir(f"El acta oficial da menos goles que el vídeo: {jugadas.goles_quitados} candidatos a gol descartados.")
    tiempos["estadisticas_s"] = time.time() - t0
    faltas = sum(1 for e in resultado_eventos if e.tipo == "falta")
    fueras_de_juego = sum(1 for e in resultado_eventos if e.tipo == "fuera_de_juego")
    progreso.estado(forzar=True, toques=len(toques), eventos=len(resultado_eventos), jugadores_seguidos=len(resumenes))
    decir(f"Posesión y jugadas: {len(toques)} toques de balón y {len(resultado_eventos)} eventos detectados ({faltas} faltas y {fueras_de_juego} fueras de juego candidatos).")

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
        recortes.update(escribir_recortes_jugadores(resolucion, seguidor, config.salida))
    mapas = escribir_mapas(resumenes, config.salida, config.nombres_equipos)
    posiciones = escribir_posiciones(instantes, config.salida)
    clips = 0
    if config.clips:
        with tqdm(desc="Clips de eventos", unit="clip", disable=config.progreso) as barra:
            clips = extraer_clips(resultado_eventos, config.video, config.salida, config.clip_margen_s, ruta_ffmpeg(), avance=lambda k: barra.update(k - barra.n))
    tiempos["salidas_s"] = time.time() - t0
    tiempos["total_s"] = time.time() - inicio_total

    progreso.fase("salidas", 2, 3)
    colores = [color_hex(c) for c in clasificador.colores]
    con_balon = balon_cobertura["vistos"]
    calibrados = sum(1 for i in instantes if i.calibrado)
    analisis = {
        "version": __version__,
        "analizador": analizador,
        "video": {"ruta": str(config.video), "ancho": info.ancho, "alto": info.alto, "fps": info.fps, "duracion_s": round(info.duracion_s, 1), "tramo_inicio_s": config.segmento.inicio, "tramo_fin_s": round(fin_s, 1)},
        "parametros": {"fps_muestreo": round(fps_real, 2), "device": modelos.device, "camara": config.camara, "seguidor": detector.rastreador.nombre, "motor_jugadores": modelos.jugadores.motor, "motor_balon": modelos.balon.motor, "motor_campo": modelos.campo.motor, "llm": llm.nombre if llm else None, "modelos": modelos.rutas, "campo_m": [campo.largo / 100, campo.ancho / 100]},
        "llm": llm.nombre if llm else None,
        "equipos": [{"id": e, "nombre": config.nombres_equipos[e], "color": colores[e]} for e in (0, 1)],
        "direcciones_ataque": {f"equipo{e}_mitad{m}": d for (e, m), d in sorted(direcciones.items())},
        "descanso_s": descanso,
        "calibracion": calibrador.resumen(),
        "recuento": {
            # `con_balon` y `pct_con_balon` son el balón **visto** por el detector (lo que guarda la app); con los huecos
            # rellenados por la trayectoria, `con_balon_interpolado` y `pct_con_balon_total`.
            "fotogramas": len(instantes), "con_balon": con_balon, "pct_con_balon": round(100 * con_balon / len(instantes), 1),
            "con_balon_interpolado": balon_cobertura["interpolados"], "pct_con_balon_total": balon_cobertura["pct_con_balon"], "balon_relleno": relleno.a_dict(),
            "pct_calibrados": round(100 * calibrados / len(instantes), 1),
            # Lo que se pudo leer del archivo frente a lo esperado: por debajo del 95 % la app lo marca como calidad baja.
            "fotogramas_esperados": total, "pct_leido": leido_pct, "lectura_cortada_s": lectura.cortado_en_s,
            "muestras_equipos": muestras, "reparto_equipos": list(reparto), "tracklets": len(resumenes), "tracklets_partidos": seguidor.cortes, "toques": len(toques), "secuencias": len(secs),
            "balones_descartados": detector.descartados, "balones_flojos_descartados": detector.flojos_descartados, "personas_fuera_del_campo": fuera_de_campo, "clips": clips,
        },
        "posesion": posesion.a_dict(),
        "eventos": [e.a_dict() for e in resultado_eventos],
        "tracklets": [dict(r.a_dict(), acciones=acciones.get(tid, None).a_dict() if tid in acciones else None, recorte=recortes.get(tid), dorsal_llm=dorsales.get(tid)) for tid, r in sorted(resumenes.items())],
        "identificacion": None if resolucion is None else {
            "lecturas": cola.leidas if cola is not None else 0,
            "cadenas": resolucion.cadenas,
            "lado_de_equipo": {str(e): lado for e, lado in resolucion.lado_de_equipo.items()},
            "segundos_sin_identificar": {str(e): s for e, s in resolucion.segundos_sin_identificar.items()},
            "jugadores": [
                {"id": x.id, "equipo": x.equipo, "dorsal": x.dorsal, "nombre": x.nombre, "tracklets": len(x.tracklets), "evidencia": round(x.evidencia, 2), "confianza": x.confianza, "por_continuidad": x.por_continuidad, "por_descarte": x.por_descarte}
                for x in resolucion.identidades
            ],
        },
        "archivos": {"mapas": mapas, "posiciones": posiciones, "anotado": "anotado.mp4" if renderizador else None},
        "tiempos": {k: round(v, 1) for k, v in tiempos.items()},
    }
    acta = acta_propuesta(resultado_eventos, posesion, resumenes, acciones, dorsales, recortes, config.nombres_equipos, colores, resolucion, acta_oficial)
    if llm is not None:
        resumen_llm = redactar_resumen(analisis, llm)
    escribir_json(analisis, config.salida / "analisis.json")
    escribir_json(acta, config.salida / "acta-propuesta.json")
    (config.salida / "resumen.md").write_text(resumen_markdown(analisis, acta, resumen_llm), encoding="utf-8")
    decir(
        f"Balón visto en el {analisis['recuento']['pct_con_balon']} % de los fotogramas y, con los huecos rellenados, en el {balon_cobertura['pct_con_balon']} %; "
        f"posesión {posesion.porcentaje(0)} / {posesion.porcentaje(1)} sobre {posesion.segundos_atribuidos:.0f} s con balón (confianza {posesion.confianza})."
    )
    decir(f"Listo en {tiempos['total_s'] / 60:.1f} min: {config.salida / 'resumen.md'}")
    progreso.fin()
    return analisis


def tramos_de(instantes: list[Instante], seguidor: Seguidor, porteros: Porteros | None = None) -> list[Tramo]:
    """Cada tracklet de jugador como tramo para `identidades.resolver`: equipo, primer y último sitio, alto y lecturas.
    El equipo, el de su camiseta; el de un portero y el del que el detector tomó por portero pero viste como un equipo,
    el que `porteros.aplicar_porteros` ya puso en cada fotograma (los fotogramas sin equipo no cuentan). De esos dos,
    todas las lecturas del dorsal: el color de la camiseta no dice bien de qué equipo son; pero ninguna del portero en
    cuyo tracklet el seguidor saltó a otra persona, que pueden ser de ella."""
    porteros = porteros or Porteros()
    datos: dict[int, dict] = {}
    for inst in instantes:
        for p in inst.personas:
            if p.equipo is None or not p.es_jugador:
                continue
            d = datos.get(p.tracklet)
            alto = p.caja[3] - p.caja[1]
            if d is None:
                d = datos[p.tracklet] = {"equipos": {}, "t_ini": inst.t, "ini": p.xy_imagen, "ini_campo": p.xy_campo, "t_fin": inst.t, "fin": p.xy_imagen, "fin_campo": p.xy_campo, "altos": [alto]}
            else:
                d.update(t_fin=inst.t, fin=p.xy_imagen, fin_campo=p.xy_campo)
                if len(d["altos"]) < 50:
                    d["altos"].append(alto)
            d["equipos"][p.equipo] = d["equipos"].get(p.equipo, 0) + 1
    tramos = []
    for tid, d in datos.items():
        memoria = seguidor.memorias.get(tid)
        visto = max(d["equipos"], key=d["equipos"].get)
        portero = porteros.es_portero(tid)
        if portero or tid in porteros.de_campo:
            votos = np.zeros(99) if tid in porteros.mezclados else _todas_las_lecturas(memoria)
            equipo = visto
        else:
            votos = memoria.votos_dorsal if memoria is not None and memoria.votos_dorsal is not None else np.zeros(99)
            equipo = memoria.equipo if memoria is not None and memoria.equipo is not None else visto
        tramos.append(Tramo(tid, equipo, d["t_ini"], d["t_fin"], d["ini"], d["fin"], float(np.median(d["altos"])), votos, d["ini_campo"], d["fin_campo"], portero, porteros.por_equipacion(tid)))
    return tramos


def _todas_las_lecturas(memoria: MemoriaTracklet | None) -> np.ndarray:
    if memoria is None or not memoria.votos_dorsal_por_equipo:
        return np.zeros(99)
    return np.sum(list(memoria.votos_dorsal_por_equipo.values()), axis=0)


def guardar_para_porteros(ruta: Path, instantes: list[Instante], mitad_de: list[int], descanso: float | None, seguidor: Seguidor, datos_acta: dict | None, saque: float) -> None:
    """Para afinar la búsqueda de los porteros y `identidades.resolver` sin repetir la pasada (solo en pruebas): los
    instantes antes de ponerles portero, y de cada tracklet sus votos, su equipación y su recorte más grande."""
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
    datos = {"instantes": instantes, "mitad_de": mitad_de, "descanso": descanso, "memorias": memorias, "datos_acta": datos_acta, "saque": saque}
    ruta.write_bytes(pickle.dumps(datos))


def escribir_recortes_jugadores(resolucion: Resolucion, seguidor: Seguidor, salida: Path) -> dict[int, str]:
    """De cada jugador identificado, el recorte en el que mejor se leyó su dorsal."""
    import cv2

    rutas: dict[int, str] = {}
    carpeta = salida / "recortes"
    carpeta.mkdir(parents=True, exist_ok=True)
    for ident in resolucion.identidades:
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


def escribir_etiquetas_dorsales(resolucion: Resolucion, tramos: list[Tramo], plantillas, guarda: GuardaZonas, config: Configuracion) -> None:
    """Las etiquetas de las zonas guardadas: el dorsal de cada tracklet identificado, con lo que se fía uno de él (la parte
    de las lecturas de su identidad que dicen ese número y la del propio tracklet, entre los dorsales permitidos)."""
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
    datos = {"video": str(config.video), "segmento": [config.segmento.inicio, config.segmento.fin], "acta": bool(plantillas), "tracklets": por_tracklet}
    guarda.carpeta.joinpath("etiquetas.json").write_text(_json.dumps(datos, ensure_ascii=False), encoding="utf-8")
