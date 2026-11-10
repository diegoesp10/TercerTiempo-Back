import json

import numpy as np

from tt_analisis.estadisticas import ResumenTracklet
from tt_analisis.informes import acta_propuesta, escribir_json, mapa_calor_png, resumen_markdown
from tt_analisis.posesion import AccionesJugador, ResumenPosesion
from tt_analisis.tipos import Evento


def _datos():
    eventos = [
        Evento("tiro", 100.0, equipo=0, tracklet=3, confianza=0.55, detalle="A puerta"),
        Evento("tiro", 140.0, equipo=0, tracklet=3, confianza=0.45, detalle="Fuera"),
        Evento("gol", 101.0, equipo=0, tracklet=3, confianza=0.85, detalle="Entra · saque de centro después"),
        Evento("gol", 900.0, equipo=1, tracklet=8, confianza=0.45, detalle="Dudoso"),
        Evento("corner", 300.0, equipo=1, confianza=0.6),
        Evento("saque_banda", 320.0, equipo=0, confianza=0.6),
        Evento("parada", 700.0, equipo=1, tracklet=9, confianza=0.5),
        Evento("paron", 1500.0, t_fin=1600.0, confianza=0.6),
    ]
    posesion = ResumenPosesion(segundos={0: 600.0, 1: 400.0})
    posesion.por_mitad = {1: {0: 600.0, 1: 400.0}}
    posesion.por_tercio[0]["rival"] = 300.0
    posesion.por_tercio[0]["medio"] = 300.0
    resumenes = {
        3: ResumenTracklet(3, 0, False, False, muestras=1000, segundos=2000.0, distancia_m=5100.0, velocidad_max_kmh=29.0, primera_t=0.0, ultima_t=2000.0),
        8: ResumenTracklet(8, 1, False, False, muestras=1000, segundos=1800.0, distancia_m=4000.0),
        9: ResumenTracklet(9, 1, True, False, muestras=1000, segundos=2000.0),
        5: ResumenTracklet(5, None, False, True, muestras=1000, segundos=2000.0),
        6: ResumenTracklet(6, 0, False, False, muestras=10, segundos=2.0),
    }
    acciones = {3: AccionesJugador(pases=12, pases_fallados=2, perdidas=3, recuperaciones=4, toques=30)}
    return eventos, posesion, resumenes, acciones


def test_acta_propuesta_desde_los_dos_lados():
    eventos, posesion, resumenes, acciones = _datos()
    acta = acta_propuesta(eventos, posesion, resumenes, acciones, {3: {"dorsal": 7}}, {3: "recortes/tracklet-3.jpg"}, ("Vallobín", "Rival"), ["#0000ff", "#ff0000"])
    a, b = acta["equipos"]
    assert a["teamStats"]["possessionPct"] == 60 and b["teamStats"]["possessionPct"] == 40
    assert (a["teamStats"]["shotsFor"], a["teamStats"]["shotsOnTargetFor"], a["teamStats"]["shotsAgainst"]) == (2, 1, 0)
    assert a["teamStats"]["goalsFor"] == 1 and b["teamStats"]["goalsFor"] == 0 and b["teamStats"]["goalsCandidatesFor"] == 1
    assert a["teamStats"]["cornersAgainst"] == 1 and b["teamStats"]["cornersFor"] == 1
    assert a["teamStats"]["throwInsFor"] == 1
    # El árbitro y el tracklet fugaz no entran; el portero sí, con su parada.
    tracklets = [j["tracklet"] for j in acta["jugadores"]]
    assert tracklets == [3, 9, 8]
    jugador = acta["jugadores"][0]
    assert jugador["dorsal_llm"] == 7 and jugador["recorte"] == "recortes/tracklet-3.jpg"
    assert jugador["propuesta"] == {"shots": 2, "shotsOnTarget": 1, "goals": 1, "saves": 0, "recoveries": 4, "ballLosses": 3, "passes": 12, "passesFailed": 2, "touches": 30}
    assert acta["jugadores"][1]["propuesta"]["saves"] == 1


def test_resumen_markdown_y_json(tmp_path):
    eventos, posesion, resumenes, acciones = _datos()
    acta = acta_propuesta(eventos, posesion, resumenes, acciones, {}, {}, ("Vallobín", "Rival"), ["#0000ff", "#ff0000"])
    analisis = {
        "video": {"ruta": "partido.mp4", "tramo_inicio_s": 0, "tramo_fin_s": 2000, "ancho": 1920, "alto": 1080, "fps": 50.0},
        "parametros": {"fps_muestreo": 5, "device": "cuda"},
        "tiempos": {"total_s": 300},
        "descanso_s": None,
        "posesion": posesion.a_dict(),
        "eventos": [e.a_dict() for e in eventos],
        "tracklets": [r.a_dict() for r in resumenes.values()],
        "calibracion": {"intentos": 10, "aciertos": 9},
        "recuento": {"fotogramas": 10000, "con_balon": 8000, "pct_con_balon": 80.0, "pct_calibrados": 99.0, "muestras_equipos": 600},
    }
    md = resumen_markdown(analisis, acta, "Buen partido.")
    assert "| Vallobín | 60.0 % |" in md and "| Posesión % | 60 | 40 |" in md
    assert "| 01:41 | gol | Vallobín | 3 | 0.85 |" in md
    assert "Parones largos: 1" in md and "Buen partido." in md
    escribir_json(analisis, tmp_path / "analisis.json")
    assert json.loads((tmp_path / "analisis.json").read_text(encoding="utf-8"))["recuento"]["fotogramas"] == 10000
    mapa = np.zeros((8, 12), dtype=np.int64)
    mapa[3, 9] = 40
    mapa_calor_png(mapa, tmp_path / "mapas" / "equipo-0.png", "Vallobín")
    assert (tmp_path / "mapas" / "equipo-0.png").stat().st_size > 1000
