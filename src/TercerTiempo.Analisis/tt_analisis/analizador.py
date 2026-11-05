"""La cadena completa: separar equipos, detectar y seguir, calibrar el campo, posesión y eventos, y escribir los
informes. `analizar(config)` deja todo en la carpeta de salida y devuelve el JSON del análisis."""

from __future__ import annotations

import shutil
import time
from pathlib import Path

import numpy as np
import supervision as sv
from tqdm import tqdm

from . import __version__
from .campo import Calibrador
from .config import CLASE_ARBITRO, MARGEN_CAMPO_CM, Configuracion
from .geometria import Campo
from .deteccion import Detector
from .equipos import ClasificadorEquipos, color_hex
from .estadisticas import direcciones_de_ataque, mitades, resumir_tracklets
from .eventos import detectar_descanso, detectar_eventos
from .informes import acta_propuesta, escribir_json, escribir_mapas, escribir_posiciones, escribir_recortes, extraer_clips, resumen_markdown
from .llm import crear_modelo
from .llm.revision import confirmar_eventos, leer_dorsales, redactar_resumen
from .modelos import cargar_modelos, describir_gpu
from .posesion import MaquinaPosesion, acciones_por_jugador, resumir_posesion, secuencias
from .seguimiento import Seguidor
from .tipos import Instante, Persona, formato_minuto
from .video import fotograma_en, fotogramas, info_video

RUTA_FFMPEG_WINGET = Path.home() / "AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin/ffmpeg.exe"


def ruta_ffmpeg() -> str:
    encontrado = shutil.which("ffmpeg")
    if encontrado:
        return encontrado
    if RUTA_FFMPEG_WINGET.exists():
        return str(RUTA_FFMPEG_WINGET)
    return "ffmpeg"


def analizar(config: Configuracion, informar=print) -> dict:
    inicio_total = time.time()
    tiempos: dict[str, float] = {}
    info = info_video(config.video)
    config.salida.mkdir(parents=True, exist_ok=True)
    fin_s = min(config.segmento.fin, info.duracion_s) if config.segmento.fin is not None else info.duracion_s
    informar(f"Vídeo: {info.ancho}×{info.alto} a {info.fps:.0f} fps, {formato_minuto(info.duracion_s)}. Tramo {formato_minuto(config.segmento.inicio)} – {formato_minuto(fin_s)}.")
    informar(f"GPU: {describir_gpu()}")

    modelos = cargar_modelos(config.modelos, config.device)
    paso = max(1, round(info.fps / config.fps_muestreo))
    fps_real = info.fps / paso
    dt = 1 / fps_real

    # 1. Equipos: una pasada rápida por el tramo para aprender las dos equipaciones (y, con cámara fija, los puntos del
    # campo de esos mismos fotogramas para una sola homografía).
    t0 = time.time()
    clasificador = ClasificadorEquipos(modelos, config)
    calibrador = Calibrador(modelos.campo, modelos.device, modo=config.camara, cada_s=config.calibrar_cada_s, imgsz=config.imgsz)
    with tqdm(total=config.muestras_equipos, desc="Equipos (recortes)", unit="recorte") as barra:
        muestras = clasificador.ajustar(info, avance=lambda n: barra.update(n - barra.n), por_fotograma=calibrador.acumular if config.camara == "fija" else None)
    if config.camara == "fija":
        fija = calibrador.fijar()
        if fija.valida:
            informar(f"Cámara fija: homografía única con {fija.puntos_usados} puntos del campo (error {fija.error_px:.1f} px).")
        else:
            informar("Cámara fija: no se vieron puntos suficientes del campo; se calibra fotograma a fotograma.")
            calibrador.modo = "movil"
    tiempos["equipos_s"] = time.time() - t0
    informar(f"Equipos separados con {muestras} recortes: {color_hex(clasificador.colores[0])} y {color_hex(clasificador.colores[1])}.")

    # 2. Pasada principal.
    t0 = time.time()
    campo = Campo()
    detector = Detector(modelos, config)
    seguidor = Seguidor()
    maquina = MaquinaPosesion()
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
    for fotograma in tqdm(fotogramas(config, info, paso=paso), total=total, desc="Analizando", unit="fot"):
        n += 1
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

        equipos = clasificador.clasificar(imagen, seguidas)
        for i in range(len(seguidas)):
            if equipos[i] >= 0:
                seguidor.votar_equipo(int(seguidas.tracker_id[i]), int(equipos[i]))

        personas = []
        for i in range(len(seguidas)):
            tid = int(seguidas.tracker_id[i])
            memoria = seguidor.memorias[tid]
            clase = memoria.clase
            caja = tuple(float(v) for v in seguidas.xyxy[i])
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
                memoria.guardar_recorte(fotograma.t, caja, personas[-1].confianza, imagen)

        balon_imagen = None
        balon_campo = None
        if len(det.balon):
            centro = det.balon.get_anchors_coordinates(sv.Position.CENTER)[0]
            balon_imagen = (float(centro[0]), float(centro[1]))
            if calibracion.valida:
                balon_campo = calibrador.punto_a_campo(balon_imagen)
                if balon_campo is not None and not campo.dentro(balon_campo, margen=MARGEN_CAMPO_CM):
                    # Un «balón» en la grada o en el banquillo no es el balón.
                    balon_imagen, balon_campo = None, None

        inst = Instante(fotograma.indice, fotograma.t, personas, balon_imagen, balon_campo, calibracion.valida)
        maquina.actualizar(inst)
        instantes.append(inst)
        if renderizador:
            renderizador.dibujar(imagen, inst)
    if renderizador:
        renderizador.cerrar()
    if instantes:
        maquina.cerrar(instantes[-1].t)
    tiempos["pasada_s"] = time.time() - t0
    if not instantes:
        raise RuntimeError("No se ha leído ningún fotograma del tramo indicado.")

    # 3. Lo que sale de las posiciones.
    t0 = time.time()
    toques = maquina.toques
    resumenes = resumir_tracklets(instantes, dt)
    porteros_por_equipo = {e: {tid for tid, r in resumenes.items() if r.portero and r.equipo == e} for e in (0, 1)}
    porteros_de = lambda equipo: porteros_por_equipo.get(equipo, set())  # noqa: E731

    primera = detectar_eventos(instantes, toques, [1] * len(instantes), lambda e, m: None, porteros_de)
    descanso = detectar_descanso(instantes, primera.eventos, fin_s - config.segmento.inicio, config.descanso)
    mitad_de = mitades(instantes, descanso)
    direcciones = direcciones_de_ataque(instantes, mitad_de, resumenes)
    direccion_de = lambda equipo, mitad: direcciones.get((equipo, mitad))  # noqa: E731
    resultado = detectar_eventos(instantes, toques, mitad_de, direccion_de, porteros_de)
    posesion = resumir_posesion(toques, instantes, resultado.juego_activo, mitad_de, direccion_de, dt)
    secs = secuencias(toques, resultado.cortes)
    acciones = acciones_por_jugador(secs)
    tiempos["estadisticas_s"] = time.time() - t0

    # 4. El modelo de lenguaje, si se pidió.
    llm = crear_modelo(config.llm, config.modelo_llm)
    dorsales: dict[int, dict] = {}
    resumen_llm = None
    if llm is not None:
        t0 = time.time()
        leer = lambda t: fotograma_en(config.video, t)  # noqa: E731
        with tqdm(desc=f"Revisando eventos con {llm.nombre}", unit="evento") as barra:
            confirmar_eventos(resultado.eventos, leer, llm, avance=lambda k: barra.update(k - barra.n))
        recortes_por_tracklet = {tid: [r.imagen for r in m.recortes] for tid, m in seguidor.memorias.items() if m.recortes and m.clase != CLASE_ARBITRO}
        with tqdm(desc="Leyendo dorsales", unit="jugador") as barra:
            dorsales = leer_dorsales(recortes_por_tracklet, llm, avance=lambda k: barra.update(k - barra.n))
        tiempos["llm_s"] = time.time() - t0

    # 5. Salidas.
    t0 = time.time()
    recortes = escribir_recortes(seguidor.memorias, config.salida)
    mapas = escribir_mapas(resumenes, config.salida, config.nombres_equipos)
    posiciones = escribir_posiciones(instantes, config.salida)
    clips = 0
    if config.clips:
        with tqdm(desc="Clips de eventos", unit="clip") as barra:
            clips = extraer_clips(resultado.eventos, config.video, config.salida, config.clip_margen_s, ruta_ffmpeg(), avance=lambda k: barra.update(k - barra.n))
    tiempos["salidas_s"] = time.time() - t0
    tiempos["total_s"] = time.time() - inicio_total

    colores = [color_hex(c) for c in clasificador.colores]
    con_balon = sum(1 for i in instantes if i.balon_imagen is not None)
    calibrados = sum(1 for i in instantes if i.calibrado)
    analisis = {
        "version": __version__,
        "video": {"ruta": str(config.video), "ancho": info.ancho, "alto": info.alto, "fps": info.fps, "duracion_s": round(info.duracion_s, 1), "tramo_inicio_s": config.segmento.inicio, "tramo_fin_s": round(fin_s, 1)},
        "parametros": {"fps_muestreo": round(fps_real, 2), "imgsz": config.imgsz, "device": modelos.device, "camara": config.camara, "tracker": Path(config.tracker_yaml()).name, "balon_fino": config.balon_fino, "llm": llm.nombre if llm else None},
        "llm": llm.nombre if llm else None,
        "equipos": [{"id": e, "nombre": config.nombres_equipos[e], "color": colores[e]} for e in (0, 1)],
        "direcciones_ataque": {f"equipo{e}_mitad{m}": d for (e, m), d in sorted(direcciones.items())},
        "descanso_s": descanso,
        "calibracion": calibrador.resumen(),
        "recuento": {
            "fotogramas": len(instantes), "con_balon": con_balon, "pct_con_balon": round(100 * con_balon / len(instantes), 1), "pct_calibrados": round(100 * calibrados / len(instantes), 1),
            "muestras_equipos": muestras, "tracklets": len(resumenes), "toques": len(toques), "secuencias": len(secs), "busquedas_finas_balon": detector.busquedas_finas,
            "balones_descartados": detector.descartados, "personas_fuera_del_campo": fuera_de_campo, "clips": clips,
        },
        "posesion": posesion.a_dict(),
        "eventos": [e.a_dict() for e in resultado.eventos],
        "tracklets": [dict(r.a_dict(), acciones=acciones.get(tid, None).a_dict() if tid in acciones else None, recorte=recortes.get(tid), dorsal_llm=dorsales.get(tid)) for tid, r in sorted(resumenes.items())],
        "archivos": {"mapas": mapas, "posiciones": posiciones, "anotado": "anotado.mp4" if renderizador else None},
        "tiempos": {k: round(v, 1) for k, v in tiempos.items()},
    }
    acta = acta_propuesta(resultado.eventos, posesion, resumenes, acciones, dorsales, recortes, config.nombres_equipos, colores)
    if llm is not None:
        resumen_llm = redactar_resumen(analisis, llm)
    escribir_json(analisis, config.salida / "analisis.json")
    escribir_json(acta, config.salida / "acta-propuesta.json")
    (config.salida / "resumen.md").write_text(resumen_markdown(analisis, acta, resumen_llm), encoding="utf-8")
    informar(f"Listo en {tiempos['total_s'] / 60:.1f} min: {config.salida / 'resumen.md'}")
    return analisis
