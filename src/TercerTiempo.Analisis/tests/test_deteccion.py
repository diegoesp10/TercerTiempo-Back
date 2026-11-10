import numpy as np
import supervision as sv

from tt_analisis.config import CLASE_ARBITRO, CLASE_JUGADOR
from tt_analisis.deteccion import distancias_a_jugadores_m, elegir_balon, juntar


def _balones(*centros_conf):
    xyxy = np.array([[x - 5, y - 5, x + 5, y + 5] for x, y, _ in centros_conf], dtype=np.float32).reshape(-1, 4)
    conf = np.array([c for _, _, c in centros_conf], dtype=np.float32)
    return sv.Detections(xyxy=xyxy, confidence=conf, class_id=np.zeros(len(xyxy), dtype=int))


def _personas(*cajas_clase):
    xyxy = np.array([c[:4] for c in cajas_clase], dtype=np.float32).reshape(-1, 4)
    clases = np.array([c[4] for c in cajas_clase], dtype=int)
    return sv.Detections(xyxy=xyxy, class_id=clases, confidence=np.full(len(xyxy), 0.9, dtype=np.float32), tracker_id=np.arange(len(xyxy)))


def test_la_distancia_a_los_jugadores_se_mide_por_su_altura_y_el_arbitro_no_cuenta():
    # Jugador de 60 px (1,80 m) con los pies en (400, 500); el árbitro, pegado al candidato, no vale como prior.
    personas = _personas((385, 440, 415, 500, CLASE_JUGADOR), (595, 440, 625, 500, CLASE_ARBITRO))
    d = distancias_a_jugadores_m(np.array([[520.0, 500.0], [610.0, 500.0]]), personas)
    assert abs(d[0] - 3.6) < 1e-6 and abs(d[1] - 6.3) < 1e-6
    assert np.isinf(distancias_a_jugadores_m(np.array([[1.0, 1.0]]), sv.Detections.empty())).all()


def test_un_candidato_flojo_solo_vale_cerca_de_un_jugador_o_de_la_prediccion():
    personas = _personas((385, 440, 415, 500, CLASE_JUGADOR))
    lejos = _balones((1500, 500, 0.22))
    elegido, flojos = elegir_balon(lejos, personas)
    assert len(elegido) == 0 and flojos == 1
    cerca = _balones((440, 500, 0.22))
    elegido, flojos = elegir_balon(cerca, personas)
    assert len(elegido) == 1 and flojos == 0
    # Lejos del jugador pero donde se esperaba el balón (venía por ahí): vale.
    elegido, _ = elegir_balon(lejos, personas, prediccion=np.array([1480.0, 500.0]))
    assert len(elegido) == 1
    # Con confianza alta vale aunque esté lejos de todos.
    seguro = _balones((1500, 500, 0.6))
    assert len(elegir_balon(seguro, personas)[0]) == 1


def test_entre_varios_candidatos_gana_el_coherente_con_los_jugadores_y_la_trayectoria():
    personas = _personas((385, 440, 415, 500, CLASE_JUGADOR))
    # Una marca lejana algo más segura que el balón a los pies del jugador: gana el de los pies.
    candidatos = _balones((1500, 500, 0.45), (430, 495, 0.4))
    elegido, _ = elegir_balon(candidatos, personas)
    assert elegido.xyxy[0][0] < 500
    # Y si el balón venía volando hacia la marca lejana, gana la trayectoria.
    elegido, _ = elegir_balon(candidatos, personas, prediccion=np.array([1490.0, 500.0]), radio_prediccion=150.0)
    assert elegido.xyxy[0][0] > 1000


def test_juntar_quita_duplicados_y_no_arrastra_identificadores():
    con_id = sv.Detections(xyxy=np.array([[100, 100, 110, 110]], dtype=np.float32), confidence=np.array([0.5], dtype=np.float32), class_id=np.array([0]), tracker_id=np.array([7]))
    todas = juntar(_balones((105, 105, 0.6), (800, 300, 0.3)), con_id, sv.Detections.empty())
    assert len(todas) == 2 and todas.tracker_id is None
    assert len(juntar(sv.Detections.empty())) == 0
