"""Lo que sale del análisis: analisis.json, resumen.md, acta-propuesta.json, mapas de calor, recortes de cada jugador
seguido, posiciones.csv.gz y los clips de los eventos."""

from __future__ import annotations

import csv
import gzip
import json
from collections import defaultdict
from pathlib import Path

import cv2
import numpy as np
from sports.annotators.soccer import draw_pitch

from .campo import CONFIG_CAMPO
from .estadisticas import COLUMNAS, FILAS, ResumenTracklet, mapa_equipo
from .posesion import AccionesJugador, ResumenPosesion
from .tipos import Evento, Instante, formato_minuto
from .video import extraer_clip

TIPOS_CON_CLIP = ("gol", "tiro", "parada", "corner")
ESCALA_MAPA = 0.1


def mapa_calor_png(mapa: np.ndarray, destino: Path, titulo: str = "") -> None:
    fondo = draw_pitch(CONFIG_CAMPO, scale=ESCALA_MAPA, padding=50)
    alto, ancho = fondo.shape[:2]
    lienzo_ancho = int(CONFIG_CAMPO.length * ESCALA_MAPA)
    lienzo_alto = int(CONFIG_CAMPO.width * ESCALA_MAPA)
    if mapa.max() > 0:
        normalizado = (mapa.astype(np.float32) / mapa.max() * 255).astype(np.uint8)
        calor = cv2.resize(normalizado, (lienzo_ancho, lienzo_alto), interpolation=cv2.INTER_CUBIC)
        calor = cv2.GaussianBlur(calor, (0, 0), sigmaX=12)
        color = cv2.applyColorMap(calor, cv2.COLORMAP_JET)
        alfa = (calor.astype(np.float32) / 255 * 0.7)[..., None]
        zona = fondo[50:50 + lienzo_alto, 50:50 + lienzo_ancho].astype(np.float32)
        fondo[50:50 + lienzo_alto, 50:50 + lienzo_ancho] = (zona * (1 - alfa) + color.astype(np.float32) * alfa).astype(np.uint8)
    if titulo:
        cv2.putText(fondo, titulo, (50, 32), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2, cv2.LINE_AA)
    destino.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(destino), fondo)


def escribir_mapas(resumenes: dict[int, ResumenTracklet], salida: Path, nombres: tuple[str, str], max_jugadores: int = 30) -> list[str]:
    rutas = []
    for equipo in (0, 1):
        ruta = salida / "mapas" / f"equipo-{equipo}.png"
        mapa_calor_png(mapa_equipo(resumenes, equipo), ruta, nombres[equipo])
        rutas.append(str(ruta.relative_to(salida)))
    jugadores = sorted((r for r in resumenes.values() if not r.arbitro and r.equipo is not None), key=lambda r: -r.segundos)[:max_jugadores]
    for r in jugadores:
        ruta = salida / "mapas" / f"tracklet-{r.tracklet}.png"
        mapa_calor_png(r.mapa, ruta, f"Tracklet {r.tracklet} · {nombres[r.equipo]}")
        rutas.append(str(ruta.relative_to(salida)))
    return rutas


def escribir_recortes(memorias, salida: Path) -> dict[int, str]:
    rutas: dict[int, str] = {}
    carpeta = salida / "recortes"
    carpeta.mkdir(parents=True, exist_ok=True)
    for tracklet, memoria in memorias.items():
        if not memoria.recortes:
            continue
        ruta = carpeta / f"tracklet-{tracklet}.jpg"
        mejor = memoria.recortes[0].imagen
        if mejor.size:
            cv2.imwrite(str(ruta), mejor, [int(cv2.IMWRITE_JPEG_QUALITY), 92])
            rutas[tracklet] = str(ruta.relative_to(salida))
    return rutas


def escribir_posiciones(instantes: list[Instante], salida: Path) -> str:
    ruta = salida / "posiciones.csv.gz"
    with gzip.open(ruta, "wt", newline="", encoding="utf-8") as f:
        escritor = csv.writer(f)
        escritor.writerow(["t", "tracklet", "equipo", "clase", "x_cm", "y_cm", "x_px", "y_px"])
        for inst in instantes:
            for p in inst.personas:
                x_cm, y_cm = ("", "") if p.xy_campo is None else (round(p.xy_campo[0]), round(p.xy_campo[1]))
                escritor.writerow([round(inst.t, 2), p.tracklet, "" if p.equipo is None else p.equipo, p.clase, x_cm, y_cm, round(p.xy_imagen[0]), round(p.xy_imagen[1])])
            if inst.balon_imagen is not None:
                x_cm, y_cm = ("", "") if inst.balon_campo is None else (round(inst.balon_campo[0]), round(inst.balon_campo[1]))
                escritor.writerow([round(inst.t, 2), "balon", "", 0, x_cm, y_cm, round(inst.balon_imagen[0]), round(inst.balon_imagen[1])])
    return str(ruta.relative_to(salida))


def extraer_clips(eventos: list[Evento], video: Path, salida: Path, margen_s: float, ffmpeg: str, max_clips: int = 60, avance=None) -> int:
    candidatos = [e for e in eventos if e.tipo in TIPOS_CON_CLIP and e.confianza >= 0.4]
    candidatos.sort(key=lambda e: (e.tipo != "gol", -e.confianza))
    hechos = 0
    for n, evento in enumerate(candidatos[:max_clips]):
        nombre = f"{formato_minuto(evento.t).replace(':', '-')}-{evento.tipo}.mp4"
        destino = salida / "clips" / nombre
        if extraer_clip(video, evento.t - margen_s, 2 * margen_s, destino, ffmpeg=ffmpeg):
            evento.clip = str(destino.relative_to(salida))
            hechos += 1
        if avance:
            avance(n + 1)
    return hechos


def contar_eventos(eventos: list[Evento], tipo: str, equipo: int | None = None, confianza_minima: float = 0.0) -> int:
    return sum(1 for e in eventos if e.tipo == tipo and (equipo is None or e.equipo == equipo) and e.confianza >= confianza_minima)


def acta_propuesta(eventos: list[Evento], posesion: ResumenPosesion, resumenes: dict[int, ResumenTracklet], acciones: dict[int, AccionesJugador], dorsales: dict[int, dict], recortes: dict[int, str], nombres: tuple[str, str], colores: list[str]) -> dict:
    """Lo mismo que pide la API del acta interna (TeamMatchStatsInput y las líneas por jugador), desde los dos lados:
    la app decide cuál es «nuestro» equipo y qué tracklet es cada ficha."""
    equipos = []
    for e in (0, 1):
        rival = 1 - e
        tiros = [x for x in eventos if x.tipo == "tiro" and x.equipo == e]
        tiros_rival = [x for x in eventos if x.tipo == "tiro" and x.equipo == rival]
        equipos.append(
            {
                "id": e,
                "nombre": nombres[e],
                "color": colores[e] if e < len(colores) else None,
                "teamStats": {
                    "possessionPct": None if posesion.porcentaje(e) is None else int(round(posesion.porcentaje(e))),
                    # La posesión solo se mide sobre el tiempo con balón visto o interpolado: `possessionBallPct` dice qué
                    # parte del tramo es eso y `possessionConfidence` («alta», «media» o «baja») si se puede uno fiar.
                    # Con «baja» la app la enseña pero no la vuelca al acta.
                    "possessionConfidence": posesion.confianza,
                    "possessionBallPct": posesion.cobertura_pct,
                    "cornersFor": contar_eventos(eventos, "corner", e),
                    "cornersAgainst": contar_eventos(eventos, "corner", rival),
                    "shotsFor": len(tiros),
                    "shotsAgainst": len(tiros_rival),
                    "shotsOnTargetFor": sum(1 for x in tiros if x.detalle.startswith("A puerta") or "entra" in x.detalle),
                    "shotsOnTargetAgainst": sum(1 for x in tiros_rival if x.detalle.startswith("A puerta") or "entra" in x.detalle),
                    "throwInsFor": contar_eventos(eventos, "saque_banda", e),
                    "throwInsAgainst": contar_eventos(eventos, "saque_banda", rival),
                    "goalKicksFor": contar_eventos(eventos, "saque_puerta", e),
                    "goalsFor": contar_eventos(eventos, "gol", e, 0.8),
                    "goalsAgainst": contar_eventos(eventos, "gol", rival, 0.8),
                    "goalsCandidatesFor": contar_eventos(eventos, "gol", e),
                    "offsidesFor": None,
                    "foulsCommitted": None,
                    "notes": "Propuesta automática del vídeo: posesión, tiros, córners y saques de banda; faltas, fueras de juego y tarjetas no salen del vídeo.",
                },
            }
        )

    jugadores = []
    for r in sorted(resumenes.values(), key=lambda r: (r.equipo if r.equipo is not None else 9, -r.segundos)):
        if r.arbitro or r.equipo is None or r.segundos < 30:
            continue
        acc = acciones.get(r.tracklet, AccionesJugador())
        tiros = [x for x in eventos if x.tipo == "tiro" and x.tracklet == r.tracklet]
        jugadores.append(
            {
                "tracklet": r.tracklet,
                "equipo": r.equipo,
                "portero": r.portero,
                "recorte": recortes.get(r.tracklet),
                "dorsal_llm": dorsales.get(r.tracklet, {}).get("dorsal"),
                "minutosALaVista": int(round(r.segundos / 60)),
                "desde": formato_minuto(r.primera_t or 0),
                "hasta": formato_minuto(r.ultima_t or 0),
                "distanciaM": int(r.distancia_m),
                "propuesta": {
                    "shots": len(tiros),
                    "shotsOnTarget": sum(1 for x in tiros if x.detalle.startswith("A puerta") or "entra" in x.detalle),
                    "goals": contar_eventos([x for x in eventos if x.tracklet == r.tracklet], "gol", None, 0.8),
                    "saves": contar_eventos([x for x in eventos if x.tracklet == r.tracklet], "parada"),
                    "recoveries": acc.recuperaciones,
                    "ballLosses": acc.perdidas,
                    "passes": acc.pases,
                    "passesFailed": acc.pases_fallados,
                    "touches": acc.toques,
                },
            }
        )
    calidad = {
        "posesionConfianza": posesion.confianza,
        "balonPct": posesion.cobertura_pct,
        "segundosConBalon": round(posesion.segundos_con_balon, 1),
        "segundosAtribuidos": round(posesion.segundos_atribuidos, 1),
    }
    return {"version": 1, "equipos": equipos, "jugadores": jugadores, "nuestroEquipo": None, "calidad": calidad}


def resumen_markdown(analisis: dict, acta: dict, resumen_llm: str | None) -> str:
    v = analisis["video"]
    nombres = [e["nombre"] for e in acta["equipos"]]
    lineas = [
        f"# Análisis del vídeo: {Path(v['ruta']).name}",
        "",
        f"Tramo analizado: {formato_minuto(v['tramo_inicio_s'])} – {formato_minuto(v['tramo_fin_s'])} · {v['ancho']}×{v['alto']} a {v['fps']:.0f} fps · "
        f"{analisis['parametros']['fps_muestreo']} fotogramas/s analizados · {analisis['tiempos']['total_s'] / 60:.1f} min de cálculo en {analisis['parametros']['device']}.",
        "",
        f"Equipos: **{nombres[0]}** ({acta['equipos'][0]['color']}) y **{nombres[1]}** ({acta['equipos'][1]['color']}). "
        + (f"Descanso detectado en {formato_minuto(analisis['descanso_s'])}." if analisis.get("descanso_s") else "Sin descanso detectado (un solo tramo)."),
        "",
        "## Posesión",
        "",
        "| Equipo | Total | 1.ª parte | 2.ª parte | Tercio propio | Medio | Tercio rival |",
        "|---|---|---|---|---|---|---|",
    ]
    cobertura = analisis["posesion"].get("cobertura", {})
    confianza = analisis["posesion"].get("confianza")
    for e in analisis["posesion"]["equipos"]:
        mitades = e["por_mitad"]
        tercios = e["por_tercio"]
        total = sum(tercios.values()) or 1
        lineas.append(
            f"| {nombres[e['equipo']]} | {e['porcentaje'] if e['porcentaje'] is not None else '—'} % | {mitades.get(1, 0) / 60:.1f} min | {mitades.get(2, 0) / 60:.1f} min | "
            f"{100 * tercios.get('propio', 0) / total:.0f} % | {100 * tercios.get('medio', 0) / total:.0f} % | {100 * tercios.get('rival', 0) / total:.0f} % |"
        )
    if confianza:
        vistos_pct = 100 * cobertura.get("segundos_vistos", 0) / max(1.0, cobertura.get("segundos_tramo", 1))
        lineas += [
            "",
            f"Confianza de la posesión: **{confianza}**. Se reparte solo el tiempo con balón visto o interpolado: {cobertura.get('segundos_atribuidos', 0):.0f} s "
            f"de los {cobertura.get('segundos_tramo', 0):.0f} s del tramo ({cobertura.get('pct_atribuido', 0)} %; balón en el {cobertura.get('pct_con_balon', 0)} %, visto en el {vistos_pct:.1f} %)."
            + (" **Con confianza baja la posesión no se vuelca al acta.**" if confianza == "baja" else ""),
        ]
    lineas += ["", "## Datos de equipo propuestos para el acta", "", "| | " + " | ".join(nombres) + " |", "|---|---|---|"]
    claves = [("possessionPct", "Posesión %"), ("shotsFor", "Tiros"), ("shotsOnTargetFor", "Tiros a puerta"), ("goalsFor", "Goles (confirmados)"), ("goalsCandidatesFor", "Goles candidatos"), ("cornersFor", "Córners"), ("throwInsFor", "Saques de banda"), ("goalKicksFor", "Saques de puerta")]
    for clave, etiqueta in claves:
        lineas.append(f"| {etiqueta} | " + " | ".join(str(e["teamStats"].get(clave, "—")) for e in acta["equipos"]) + " |")

    lineas += ["", "## Eventos detectados (candidatos)", ""]
    eventos = [e for e in analisis["eventos"] if e["tipo"] not in ("paron",)]
    if not eventos:
        lineas.append("Ninguno.")
    else:
        lineas += ["| Minuto | Evento | Equipo | Tracklet | Confianza | Detalle | Revisión |", "|---|---|---|---|---|---|---|"]
        for e in eventos:
            equipo = "—" if e["equipo"] is None else nombres[e["equipo"]]
            lineas.append(f"| {e['minuto']} | {e['tipo']} | {equipo} | {e['tracklet'] if e['tracklet'] is not None else '—'} | {e['confianza']:.2f} | {e['detalle']} | {e['veredicto_llm'] or ''} |")
    parones = [e for e in analisis["eventos"] if e["tipo"] == "paron"]
    if parones:
        lineas += ["", f"Parones largos: {len(parones)} (" + ", ".join(f"{p['minuto']}→{formato_minuto(p['t_fin'] or p['t'])}" for p in parones[:12]) + ")."]

    lineas += ["", "## Jugadores seguidos (tracklets)", "", "Un tracklet es una persona seguida por el programa; se corta al perderla de vista. El cuerpo técnico asigna cada tracklet a una ficha.", "",
               "| Tracklet | Equipo | Minutos | Distancia | Vel. máx. | Toques | Pases | Recup. | Pérdidas | Tiros | Dorsal (IA) |", "|---|---|---|---|---|---|---|---|---|---|---|"]
    for j in acta["jugadores"][:40]:
        t = next((x for x in analisis["tracklets"] if x["tracklet"] == j["tracklet"]), {})
        p = j["propuesta"]
        lineas.append(
            f"| {j['tracklet']}{' (POR)' if j['portero'] else ''} | {nombres[j['equipo']]} | {j['minutosALaVista']} | {j['distanciaM']} m | {t.get('velocidad_max_kmh', 0):.0f} km/h | "
            f"{p['touches']} | {p['passes']} | {p['recoveries']} | {p['ballLosses']} | {p['shots']} | {j['dorsal_llm'] if j['dorsal_llm'] is not None else '—'} |"
        )
    recuento = analisis["recuento"]
    relleno = recuento.get("balon_relleno", {})
    balon = f"- Fotogramas analizados: {recuento['fotogramas']}; con balón visto: {recuento['con_balon']} ({recuento['pct_con_balon']} %)"
    if "pct_con_balon_total" in recuento:
        balon += (f"; con los huecos cortos rellenados por la trayectoria: {recuento['pct_con_balon_total']} % ({recuento.get('con_balon_interpolado', 0)} interpolados, "
                  f"{relleno.get('huecos_largos', 0)} huecos largos sin rellenar, {relleno.get('aislados_descartados', 0)} avistamientos sueltos descartados)")
    balon += f"; con campo calibrado: {recuento['pct_calibrados']} %."
    lineas += ["", "## Calidad del análisis", "", f"- Calibración del campo: {analisis['calibracion']}", balon,
               f"- Muestras para separar los equipos: {analisis['recuento']['muestras_equipos']}.", "- Los eventos son candidatos: tiros, goles y córners hay que confirmarlos con el clip; faltas, fueras de juego y tarjetas no se deducen del vídeo."]
    if resumen_llm:
        lineas += ["", f"## Resumen ({analisis.get('llm') or 'modelo de lenguaje'})", "", resumen_llm.strip()]
    return "\n".join(lineas) + "\n"


def escribir_json(datos: dict, ruta: Path) -> None:
    ruta.parent.mkdir(parents=True, exist_ok=True)
    ruta.write_text(json.dumps(datos, ensure_ascii=False, indent=2, default=_json_default), encoding="utf-8")


def _json_default(valor):
    if isinstance(valor, (np.integer,)):
        return int(valor)
    if isinstance(valor, (np.floating,)):
        return float(valor)
    if isinstance(valor, np.ndarray):
        return valor.tolist()
    if isinstance(valor, defaultdict):
        return dict(valor)
    raise TypeError(f"No serializable: {type(valor)}")
