import gzip
import json

import numpy as np

from tt_analisis.estadisticas import ResumenTracklet
from tt_analisis.informes import acta_propuesta, escribir_json, escribir_posiciones, mapa_calor_png, resumen_markdown
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
    posesion = ResumenPosesion(segundos={0: 600.0, 1: 400.0}, segundos_tramo=1500.0, segundos_con_balon=1100.0, segundos_vistos=900.0, segundos_activos=1300.0)
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
    # Balón en 1100 de 1500 s (73 %) y 1000 s repartidos (67 %): confianza alta, y la app sabe sobre cuánto se midió.
    assert a["teamStats"]["possessionConfidence"] == "alta" and a["teamStats"]["possessionBallPct"] == 73.3
    assert acta["calidad"] == {"posesionConfianza": "alta", "balonPct": 73.3, "segundosConBalon": 1100.0, "segundosAtribuidos": 1000.0}
    assert (a["teamStats"]["shotsFor"], a["teamStats"]["shotsOnTargetFor"], a["teamStats"]["shotsAgainst"]) == (2, 1, 0)
    assert a["teamStats"]["goalsFor"] == 1 and b["teamStats"]["goalsFor"] == 0 and b["teamStats"]["goalsCandidatesFor"] == 1
    assert a["teamStats"]["cornersAgainst"] == 1 and b["teamStats"]["cornersFor"] == 1
    assert a["teamStats"]["throwInsFor"] == 1
    # El árbitro y el tracklet fugaz no entran; el portero sí, con su parada.
    tracklets = [j["tracklet"] for j in acta["jugadores"]]
    assert tracklets == [3, 9, 8]
    jugador = acta["jugadores"][0]
    assert jugador["dorsal_llm"] == 7 and jugador["recorte"] == "recortes/tracklet-3.jpg"
    assert jugador["propuesta"] == {"shots": 2, "shotsOnTarget": 1, "goals": 1, "saves": 0, "recoveries": 4, "ballLosses": 3, "passes": 12, "passesFailed": 2, "touches": 30, "fouls": 0, "foulsReceived": 0, "offsides": 0, "distributions": 0}
    assert jugador["minutos"] is None and acta["actaOficial"] is None
    assert acta["jugadores"][1]["propuesta"]["saves"] == 1


def test_cada_accion_de_la_propuesta_lleva_su_segundo_del_video(tmp_path):
    eventos, posesion, resumenes, _ = _datos()
    eventos += [
        Evento("falta", 400.04, equipo=1, tracklet=8, tracklet_rival=3, confianza=0.6),
        # Con poca confianza no cuenta en la propuesta, así que tampoco tiene momento.
        Evento("falta", 410.0, equipo=1, tracklet=8, tracklet_rival=3, confianza=0.3),
        Evento("fuera_de_juego", 500.0, equipo=0, tracklet=3, confianza=0.5),
        Evento("saque_puerta", 600.0, equipo=1, confianza=0.6),
    ]
    acciones = {
        3: AccionesJugador(pases=2, pases_fallados=1, perdidas=1, recuperaciones=1, toques=9, segundos_pases=[250.26, 12.0], segundos_pases_fallados=[260.0],
                           segundos_perdidas=[260.0], segundos_recuperaciones=[240.0]),
        9: AccionesJugador(saques=2, segundos_saques=[800.04, 790.0]),
    }
    acta = acta_propuesta(eventos, posesion, resumenes, acciones, {}, {}, ("Vallobín", "Rival"), ["#0000ff", "#ff0000"])
    a, b = acta["equipos"]
    assert a["momentos"] == {"shots": [100.0, 140.0], "shotsOnTarget": [100.0], "goals": [101.0], "corners": [], "throwIns": [320.0], "goalKicks": [], "fouls": [], "offsides": [500.0]}
    # El gol del rival no está confirmado: no cuenta ni tiene momento.
    assert b["momentos"] == {"shots": [], "shotsOnTarget": [], "goals": [], "corners": [300.0], "throwIns": [], "goalKicks": [600.0], "fouls": [400.0], "offsides": []}
    jugador = acta["jugadores"][0]
    # En orden y a la décima.
    assert jugador["momentos"] == {
        "shots": [100.0, 140.0], "shotsOnTarget": [100.0], "goals": [101.0], "fouls": [], "foulsReceived": [400.0], "offsides": [500.0], "saves": [],
        "recoveries": [240.0], "ballLosses": [260.0], "passes": [12.0, 250.3], "passesFailed": [260.0], "distributions": [],
    }
    assert acta["jugadores"][1]["momentos"]["saves"] == [700.0]
    assert acta["jugadores"][1]["propuesta"]["distributions"] == 2 and acta["jugadores"][1]["momentos"]["distributions"] == [790.0, 800.0]
    assert next(j for j in acta["jugadores"] if j["tracklet"] == 8)["momentos"]["fouls"] == [400.0]
    # Lo que cuenta la propuesta y lo que se puede ver en el vídeo es lo mismo, dato a dato (sin acta oficial).
    for j in acta["jugadores"]:
        for clave, momentos in j["momentos"].items():
            assert len(momentos) == j["propuesta"][clave], (j["tracklet"], clave)
    claves = {"shots": "shotsFor", "shotsOnTarget": "shotsOnTargetFor", "goals": "goalsFor", "corners": "cornersFor", "throwIns": "throwInsFor", "goalKicks": "goalKicksFor", "fouls": "foulsCommitted", "offsides": "offsidesFor"}
    for equipo in acta["equipos"]:
        for clave, dato in claves.items():
            assert len(equipo["momentos"][clave]) == equipo["teamStats"][dato], (equipo["id"], clave)
    escribir_json(acta, tmp_path / "acta-propuesta.json")
    assert json.loads((tmp_path / "acta-propuesta.json").read_text(encoding="utf-8"))["jugadores"][0]["momentos"]["passes"] == [12.0, 250.3]


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
        "recuento": {"fotogramas": 10000, "con_balon": 8000, "pct_con_balon": 80.0, "con_balon_interpolado": 500, "pct_con_balon_total": 85.0, "balon_relleno": {"huecos_largos": 3, "aislados_descartados": 2}, "pct_calibrados": 99.0, "muestras_equipos": 600},
    }
    md = resumen_markdown(analisis, acta, "Buen partido.")
    assert "| Vallobín | 60.0 % |" in md and "| Posesión % | 60 | 40 |" in md
    assert "Confianza de la posesión: **alta**" in md and "1000 s de los 1500 s del tramo (66.7 %; balón en el 73.3 %, visto en el 60.0 %)" in md
    assert "| 01:41 | gol | Vallobín | 3 | 0.85 |" in md
    assert "Parones largos: 1" in md and "Buen partido." in md
    assert "con balón visto: 8000 (80.0 %); con los huecos cortos rellenados por la trayectoria: 85.0 % (500 interpolados, 3 huecos largos sin rellenar, 2 avistamientos sueltos y 0 de objetos fijos descartados)" in md
    escribir_json(analisis, tmp_path / "analisis.json")
    assert json.loads((tmp_path / "analisis.json").read_text(encoding="utf-8"))["recuento"]["fotogramas"] == 10000
    mapa = np.zeros((8, 12), dtype=np.int64)
    mapa[3, 9] = 40
    mapa_calor_png(mapa, tmp_path / "mapas" / "equipo-0.png", "Vallobín")
    assert (tmp_path / "mapas" / "equipo-0.png").stat().st_size > 1000


def test_con_el_acta_oficial_los_minutos_y_los_goles_salen_de_ella():
    from tt_analisis.identidades import Identidad, Resolucion, id_jugador
    from tt_analisis.oficial import leer_acta

    _, posesion, _, _ = _datos()
    siete, cuatro = id_jugador(0, 7), id_jugador(1, 4)
    resumenes = {
        siete: ResumenTracklet(siete, 0, False, False, muestras=1000, segundos=1500.0),
        cuatro: ResumenTracklet(cuatro, 1, False, False, muestras=1000, segundos=1200.0),
    }
    eventos = [
        Evento("gol", 101.0, equipo=0, tracklet=siete, confianza=0.85),
        Evento("falta", 500.0, equipo=1, tracklet=cuatro, confianza=0.6, tracklet_rival=siete),
        Evento("falta", 600.0, equipo=1, tracklet=None, confianza=0.45, tracklet_rival=siete),
        Evento("falta", 650.0, equipo=0, tracklet=siete, confianza=0.3, tracklet_rival=cuatro),
        Evento("fuera_de_juego", 800.0, equipo=0, tracklet=siete, confianza=0.5),
    ]
    datos = {
        "parte": "partido", "minutos_parte": 40, "resultado": {"local": 2, "visitante": 0},
        "equipos": [
            {"lado": "local", "nombre": "Vallobín", "jugadores": [{"dorsal": 7, "nombre": "Siete", "titular": True, "entra": None, "sale": None, "goles": 2},
                                                                  {"dorsal": 9, "nombre": "Nueve", "titular": False, "entra": 60, "sale": None}]},
            {"lado": "visitante", "nombre": "Rival", "jugadores": [{"dorsal": 4, "nombre": "Cuatro", "titular": True, "entra": None, "sale": 70}]},
        ],
    }
    resolucion = Resolucion({}, [Identidad(0, 7, "Siete", [3], 1.0, 0.9), Identidad(1, 4, "Cuatro", [8], 1.0, 0.9)], {0: "local", 1: None}, 2, {})
    acta = acta_propuesta(eventos, posesion, resumenes, {}, {}, {}, ("Vallobín", "Rival"), ["#0000ff", "#ff0000"], resolucion, leer_acta(datos))
    a, b = acta["equipos"]
    # El acta manda en los goles (2-0) aunque el vídeo solo confirmó uno; el lado del rival se deduce.
    assert (a["teamStats"]["goalsFor"], a["teamStats"]["goalsAgainst"], b["teamStats"]["goalsFor"]) == (2, 0, 0) and a["teamStats"]["goalsFromSheet"]
    # La falta floja (0,3) no cuenta.
    assert (a["teamStats"]["foulsCommitted"], a["teamStats"]["foulsReceived"], b["teamStats"]["foulsCommitted"]) == (0, 2, 2)
    assert (a["teamStats"]["offsidesFor"], b["teamStats"]["offsidesAgainst"]) == (1, 1)
    j7 = next(j for j in acta["jugadores"] if j["dorsal"] == 7)
    assert j7["minutos"] == 80 and j7["titular"] is True and j7["propuesta"]["goals"] == 2
    assert j7["propuesta"]["foulsReceived"] == 2 and j7["propuesta"]["offsides"] == 1 and j7["propuesta"]["fouls"] == 0
    j4 = next(j for j in acta["jugadores"] if j["dorsal"] == 4)
    assert j4["minutos"] == 70 and j4["propuesta"]["fouls"] == 1
    oficiales = {(j["lado"], j["dorsal"]): j for j in acta["actaOficial"]["jugadores"]}
    assert oficiales[("local", 9)] == {"lado": "local", "dorsal": 9, "nombre": "Nueve", "titular": False, "minutos": 20, "goles": 0, "visto": False}
    assert oficiales[("local", 7)]["visto"] and acta["actaOficial"]["resultado"] == {"local": 2, "visitante": 0}


def test_las_posiciones_no_llevan_la_hora_en_la_cabecera_del_gzip(tmp_path):
    (tmp_path / "a").mkdir()
    (tmp_path / "b").mkdir()

    escribir_posiciones([], tmp_path / "a")
    escribir_posiciones([], tmp_path / "b")

    a = (tmp_path / "a" / "posiciones.csv.gz").read_bytes()
    # Bytes 4 a 7 de la cabecera: la hora de escritura, que haría distintos dos archivos con el mismo contenido.
    assert a[4:8] == b"\0\0\0\0"
    assert a == (tmp_path / "b" / "posiciones.csv.gz").read_bytes()
    assert gzip.decompress(a).decode("utf-8").splitlines() == ["t,tracklet,equipo,clase,x_cm,y_cm,x_px,y_px"]
