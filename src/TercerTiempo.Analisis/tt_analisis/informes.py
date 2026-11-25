"""Lo que sale del análisis: analisis.json, resumen.md, acta-propuesta.json, mapas de calor, recortes de cada jugador
seguido, posiciones.csv.gz y los clips de los eventos."""

from __future__ import annotations

import csv
import gzip
import json
import math
from collections import defaultdict
from pathlib import Path

import cv2
import numpy as np
from sports.annotators.soccer import draw_pitch

from .campo import CONFIG_CAMPO
from .estadisticas import COLUMNAS, FILAS, ResumenTracklet, mapa_equipo
from .oficial import ActaOficial, lados_completos
from .posesion import AccionesJugador, ResumenPosesion
from .tipos import Evento, Instante, formato_minuto
from .video import extraer_clip

TIPOS_CON_CLIP = ("gol", "tiro", "parada", "corner", "falta", "fuera_de_juego")
# Faltas y fueras de juego con al menos esta confianza cuentan en la propuesta del acta (los demás quedan como eventos).
CONFIANZA_PARADAS_ACTA = 0.4
# Sin el acta oficial cerrada, solo cuentan los goles confirmados (por el saque de centro o por el minuto del acta).
CONFIANZA_GOL_ACTA = 0.8
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


def eventos_de(eventos: list[Evento], tipo: str, equipo: int | None = None, confianza_minima: float = 0.0) -> list[Evento]:
    return [e for e in eventos if e.tipo == tipo and (equipo is None or e.equipo == equipo) and e.confianza >= confianza_minima]


def contar_eventos(eventos: list[Evento], tipo: str, equipo: int | None = None, confianza_minima: float = 0.0) -> int:
    return len(eventos_de(eventos, tipo, equipo, confianza_minima))


def a_puerta(tiro: Evento) -> bool:
    return tiro.detalle.startswith("A puerta") or "entra" in tiro.detalle


def segundos_de(eventos: list[Evento]) -> list[float]:
    """El segundo del vídeo de cada evento, en orden: la app lleva el vídeo a ese momento («Ver»)."""
    return redondear_segundos(e.t for e in eventos)


def redondear_segundos(segundos) -> list[float]:
    # Se muestrean 5 fotogramas por segundo: la décima basta.
    return sorted(round(float(t), 1) for t in segundos)


def acta_propuesta(eventos: list[Evento], posesion: ResumenPosesion, resumenes: dict[int, ResumenTracklet], acciones: dict[int, AccionesJugador], dorsales: dict[int, dict], recortes: dict[int, str], nombres: tuple[str, str], colores: list[str], resolucion=None, acta: ActaOficial | None = None) -> dict:
    """Lo mismo que pide la API del acta interna (TeamMatchStatsInput y las líneas por jugador), desde los dos lados.
    Con los dorsales leídos, una línea por jugador identificado (con su dorsal y, si se pasó el acta oficial, su nombre
    en ella); sin ellos, una por tracklet de más de 30 s, y la app decide qué tracklet es cada ficha. Con el acta oficial,
    los minutos y los goles salen de ella (por descarte: sin cambio, el partido entero) y en `actaOficial` van todos los
    que jugaron, se les viera o no.

    Desde la 2.1.2, cada equipo y cada jugador traen `momentos`: el segundo del vídeo de cada cosa que se cuenta, con las
    mismas claves que sus datos y los mismos filtros, para verla en el vídeo desde la app. Los goles que salen del acta
    oficial cerrada pueden no tener momento: el vídeo solo da los que vio."""
    identidades = {x.id: x for x in resolucion.identidades} if resolucion is not None else {}
    lados = lados_completos(resolucion.lado_de_equipo) if resolucion is not None else {}
    faltas = [x for x in eventos if x.tipo == "falta" and x.confianza >= CONFIANZA_PARADAS_ACTA]
    fueras_de_juego = [x for x in eventos if x.tipo == "fuera_de_juego" and x.confianza >= CONFIANZA_PARADAS_ACTA]
    equipos = []
    for e in (0, 1):
        rival = 1 - e
        tiros = [x for x in eventos if x.tipo == "tiro" and x.equipo == e]
        tiros_rival = [x for x in eventos if x.tipo == "tiro" and x.equipo == rival]
        goles_acta = acta.goles if acta is not None and acta.goles is not None and lados.get(e) in acta.goles else None
        equipos.append(
            {
                "id": e,
                "nombre": nombres[e],
                "color": colores[e] if e < len(colores) else None,
                # Lado del acta oficial («local» o «visitante») que tiene este equipo según los dorsales; null si no se sabe.
                "lado": lados.get(e),
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
                    "shotsOnTargetFor": sum(1 for x in tiros if a_puerta(x)),
                    "shotsOnTargetAgainst": sum(1 for x in tiros_rival if a_puerta(x)),
                    "throwInsFor": contar_eventos(eventos, "saque_banda", e),
                    "throwInsAgainst": contar_eventos(eventos, "saque_banda", rival),
                    "goalKicksFor": contar_eventos(eventos, "saque_puerta", e),
                    # Con el acta oficial cerrada, los goles son los suyos; si no, los candidatos confirmados por el saque de centro.
                    "goalsFor": goles_acta[lados[e]] if goles_acta else contar_eventos(eventos, "gol", e, CONFIANZA_GOL_ACTA),
                    "goalsAgainst": goles_acta[lados[rival]] if goles_acta and lados.get(rival) in goles_acta else contar_eventos(eventos, "gol", rival, CONFIANZA_GOL_ACTA),
                    "goalsFromSheet": goles_acta is not None,
                    "goalsCandidatesFor": contar_eventos(eventos, "gol", e),
                    # Fueras de juego en los que cayó este equipo (a favor del rival) y en los que cayó el rival.
                    "offsidesFor": sum(1 for x in fueras_de_juego if x.equipo == e),
                    "offsidesAgainst": sum(1 for x in fueras_de_juego if x.equipo == rival),
                    "foulsCommitted": sum(1 for x in faltas if x.equipo == e),
                    "foulsReceived": sum(1 for x in faltas if x.equipo == rival),
                    "notes": "Propuesta automática del vídeo: posesión, tiros, córners, saques de banda, faltas y fueras de juego (paradas del juego con el balón dentro del campo); las tarjetas no salen del vídeo.",
                },
                # Lo que hace este equipo, cada cosa en su segundo del vídeo: lo «en contra» son los momentos del otro.
                "momentos": {
                    "shots": segundos_de(tiros),
                    "shotsOnTarget": segundos_de([x for x in tiros if a_puerta(x)]),
                    "goals": segundos_de(eventos_de(eventos, "gol", e, CONFIANZA_GOL_ACTA)),
                    "corners": segundos_de(eventos_de(eventos, "corner", e)),
                    "throwIns": segundos_de(eventos_de(eventos, "saque_banda", e)),
                    "goalKicks": segundos_de(eventos_de(eventos, "saque_puerta", e)),
                    "fouls": segundos_de([x for x in faltas if x.equipo == e]),
                    "offsides": segundos_de([x for x in fueras_de_juego if x.equipo == e]),
                },
            }
        )

    jugadores = []
    for r in sorted(resumenes.values(), key=lambda r: (r.equipo if r.equipo is not None else 9, identidades[r.tracklet].dorsal if r.tracklet in identidades else 0, -r.segundos)):
        if r.arbitro or r.equipo is None:
            continue
        ident = identidades.get(r.tracklet)
        if resolucion is not None and ident is None:
            continue
        if resolucion is None and r.segundos < 30:
            continue
        acc = acciones.get(r.tracklet, AccionesJugador())
        suyos = [x for x in eventos if x.tracklet == r.tracklet]
        tiros = [x for x in suyos if x.tipo == "tiro"]
        oficial = acta.jugador(lados.get(r.equipo), ident.dorsal) if acta is not None and ident is not None else None
        jugadores.append(
            {
                "tracklet": r.tracklet,
                "equipo": r.equipo,
                "portero": r.portero,
                "recorte": recortes.get(r.tracklet),
                "dorsal_llm": dorsales.get(r.tracklet, {}).get("dorsal"),
                "dorsal": ident.dorsal if ident else None,
                "nombreActa": ident.nombre if ident else None,
                "confianzaDorsal": ident.confianza if ident else None,
                # Portero cuyo dorsal sale del acta por descarte, no de leerlo (la confianza del dorsal sale baja).
                "dorsalPorDescarte": bool(ident and ident.por_descarte),
                "tramos": len(ident.tracklets) if ident else 1,
                "minutosALaVista": int(round(r.segundos / 60)),
                # Del acta oficial cerrada, por descarte (sin cambio, el partido entero); None si no está en ella o
                # el acta es provisional (le pueden faltar los cambios).
                "minutos": acta.minutos(oficial) if oficial is not None and acta.cerrada else None,
                "titular": oficial.titular if oficial is not None else None,
                "desde": formato_minuto(r.primera_t or 0),
                "hasta": formato_minuto(r.ultima_t or 0),
                "distanciaM": int(r.distancia_m),
                "propuesta": {
                    "shots": len(tiros),
                    "shotsOnTarget": sum(1 for x in tiros if a_puerta(x)),
                    "goals": oficial.goles if oficial is not None and acta.cerrada else contar_eventos(suyos, "gol", None, CONFIANZA_GOL_ACTA),
                    "fouls": sum(1 for x in faltas if x.tracklet == r.tracklet),
                    "foulsReceived": sum(1 for x in faltas if x.tracklet_rival == r.tracklet),
                    "offsides": sum(1 for x in fueras_de_juego if x.tracklet == r.tracklet),
                    "saves": contar_eventos(suyos, "parada"),
                    "recoveries": acc.recuperaciones,
                    "ballLosses": acc.perdidas,
                    "passes": acc.pases,
                    "passesFailed": acc.pases_fallados,
                    "touches": acc.toques,
                    # Del portero: los balones que pone en juego desde su área (no son pases ni van al acta).
                    "distributions": acc.saques,
                },
                # Cada acción de la propuesta en su segundo del vídeo, con las mismas claves (los toques no: son
                # demasiados y no van al acta). Los goles del acta oficial que el vídeo no vio no tienen momento.
                "momentos": {
                    "shots": segundos_de(tiros),
                    "shotsOnTarget": segundos_de([x for x in tiros if a_puerta(x)]),
                    "goals": segundos_de(eventos_de(suyos, "gol", None, CONFIANZA_GOL_ACTA)),
                    "fouls": segundos_de([x for x in faltas if x.tracklet == r.tracklet]),
                    "foulsReceived": segundos_de([x for x in faltas if x.tracklet_rival == r.tracklet]),
                    "offsides": segundos_de([x for x in fueras_de_juego if x.tracklet == r.tracklet]),
                    "saves": segundos_de(eventos_de(suyos, "parada")),
                    "recoveries": redondear_segundos(acc.segundos_recuperaciones),
                    "ballLosses": redondear_segundos(acc.segundos_perdidas),
                    "passes": redondear_segundos(acc.segundos_pases),
                    "passesFailed": redondear_segundos(acc.segundos_pases_fallados),
                    "distributions": redondear_segundos(acc.segundos_saques),
                },
            }
        )
    calidad = {
        "posesionConfianza": posesion.confianza,
        "balonPct": posesion.cobertura_pct,
        "segundosConBalon": round(posesion.segundos_con_balon, 1),
        "segundosAtribuidos": round(posesion.segundos_atribuidos, 1),
    }
    if resolucion is not None:
        calidad["sinIdentificarS"] = {str(e): s for e, s in resolucion.segundos_sin_identificar.items()}
    vistos = {(lados.get(j["equipo"]), j["dorsal"]) for j in jugadores if j["dorsal"] is not None}
    acta_oficial = None if acta is None else {
        "minutosParte": acta.minutos_parte,
        "resultado": acta.goles,
        "cerrada": acta.cerrada,
        "jugadores": [
            {"lado": j.lado, "dorsal": j.dorsal, "nombre": j.nombre, "titular": j.titular, "minutos": acta.minutos(j) if acta.cerrada else None, "goles": j.goles, "visto": (j.lado, j.dorsal) in vistos}
            for j in acta.jugadores
        ],
    }
    return {"version": 2 if resolucion is not None else 1, "equipos": equipos, "jugadores": jugadores, "nuestroEquipo": None, "calidad": calidad, "identificados": resolucion is not None, "actaOficial": acta_oficial}


def resumen_markdown(analisis: dict, acta: dict, resumen_llm: str | None) -> str:
    v = analisis["video"]
    nombres = [e["nombre"] for e in acta["equipos"]]
    lineas = [
        f"# Análisis del vídeo: {Path(v['ruta']).name}",
        "",
        *([f"Analizado con {analisis['analizador']['nombre']} {analisis['analizador']['version']}.", ""] if analisis.get("analizador") else []),
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
    claves = [("possessionPct", "Posesión %"), ("shotsFor", "Tiros"), ("shotsOnTargetFor", "Tiros a puerta"), ("goalsFor", "Goles (confirmados)"), ("goalsCandidatesFor", "Goles candidatos"), ("cornersFor", "Córners"), ("throwInsFor", "Saques de banda"), ("goalKicksFor", "Saques de puerta"), ("foulsCommitted", "Faltas cometidas"), ("offsidesFor", "Fueras de juego")]
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
               "| Tracklet | Equipo | Minutos | Distancia | Vel. máx. | Toques | Pases | Recup. | Pérdidas | Tiros | Paradas | Saques | Dorsal (IA) |", "|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for j in acta["jugadores"][:40]:
        t = next((x for x in analisis["tracklets"] if x["tracklet"] == j["tracklet"]), {})
        p = j["propuesta"]
        lineas.append(
            f"| {j['tracklet']}{' (POR)' if j['portero'] else ''} | {nombres[j['equipo']]} | {j['minutosALaVista']} | {j['distanciaM']} m | {t.get('velocidad_max_kmh', 0):.0f} km/h | "
            f"{p['touches']} | {p['passes']} | {p['recoveries']} | {p['ballLosses']} | {p['shots']} | {p['saves']} | {p.get('distributions', 0)} | {j['dorsal_llm'] if j['dorsal_llm'] is not None else '—'} |"
        )
    recuento = analisis["recuento"]
    relleno = recuento.get("balon_relleno", {})
    balon = f"- Fotogramas analizados: {recuento['fotogramas']}; con balón visto: {recuento['con_balon']} ({recuento['pct_con_balon']} %)"
    if "pct_con_balon_total" in recuento:
        balon += (f"; con los huecos cortos rellenados por la trayectoria: {recuento['pct_con_balon_total']} % ({recuento.get('con_balon_interpolado', 0)} interpolados, "
                  f"{relleno.get('huecos_largos', 0)} huecos largos sin rellenar, {relleno.get('aislados_descartados', 0)} avistamientos sueltos y {relleno.get('fijos_descartados', 0)} de objetos fijos descartados)")
    balon += f"; con campo calibrado: {recuento['pct_calibrados']} %."
    lineas += ["", "## Calidad del análisis", "", f"- Calibración del campo: {analisis['calibracion']}", balon,
               f"- Muestras para separar los equipos: {analisis['recuento']['muestras_equipos']}.", "- Los eventos son candidatos: tiros, goles, córners, faltas y fueras de juego hay que confirmarlos con el clip; las tarjetas no se deducen del vídeo."]
    if resumen_llm:
        lineas += ["", f"## Resumen ({analisis.get('llm') or 'modelo de lenguaje'})", "", resumen_llm.strip()]
    return "\n".join(lineas) + "\n"


def escribir_json(datos: dict, ruta: Path) -> None:
    """JSON estricto: un NaN o un infinito (una media sin muestras, una división por cero) no es JSON y la app no lo
    leería, así que se escribe como null; si aun así quedara alguno, falla aquí y no al leerlo."""
    ruta.parent.mkdir(parents=True, exist_ok=True)
    ruta.write_text(json.dumps(sanear_json(datos), ensure_ascii=False, indent=2, default=_json_default, allow_nan=False), encoding="utf-8")


def sanear_json(valor):
    """Copia de `valor` lista para JSON estricto: NaN e infinitos pasan a None y los tipos de numpy a los de Python."""
    if isinstance(valor, dict):
        return {k: sanear_json(v) for k, v in valor.items()}
    if isinstance(valor, (list, tuple)):
        return [sanear_json(v) for v in valor]
    if isinstance(valor, np.ndarray):
        return sanear_json(valor.tolist())
    if isinstance(valor, (bool, np.bool_)):
        return bool(valor)
    if isinstance(valor, np.integer):
        return int(valor)
    if isinstance(valor, (float, np.floating)):
        numero = float(valor)
        return numero if math.isfinite(numero) else None
    return valor


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
