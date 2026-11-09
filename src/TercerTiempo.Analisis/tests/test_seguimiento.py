import numpy as np
import supervision as sv

from tt_analisis.config import CLASE_JUGADOR
from tt_analisis.seguimiento import CORTE_MUESTRAS, TRACKLET_CORTADO_BASE, Seguidor


def personas(ids: list[int]) -> sv.Detections:
    n = len(ids)
    return sv.Detections(
        xyxy=np.array([[10 * i, 0, 10 * i + 8, 20] for i in range(n)], dtype=float),
        class_id=np.full(n, CLASE_JUGADOR),
        confidence=np.full(n, 0.9),
        tracker_id=np.array(ids, dtype=int),
    )


def test_un_tracklet_que_cambia_de_camiseta_se_parte_en_dos():
    seguidor = Seguidor()
    # Veinte fotogramas de un jugador del equipo 0, bien visto.
    for _ in range(20):
        seguidor.votar_equipos(seguidor.registrar(personas([7])), np.array([0]), np.array([0.9]))
    assert seguidor.equipo_de(7) == 0

    # El seguidor salta a un jugador del equipo 1: las primeras muestras siguen en el 7, luego se parte.
    vistos = []
    for _ in range(CORTE_MUESTRAS + 4):
        seguidas = seguidor.votar_equipos(seguidor.registrar(personas([7])), np.array([1]), np.array([0.8]))
        vistos.append(int(seguidas.tracker_id[0]))
    nuevo = TRACKLET_CORTADO_BASE
    assert vistos[: CORTE_MUESTRAS - 1] == [7] * (CORTE_MUESTRAS - 1)
    assert vistos[CORTE_MUESTRAS - 1 :] == [nuevo] * 5
    assert seguidor.cortes == 1
    assert seguidor.equipo_de(7) == 0
    assert seguidor.equipo_de(nuevo) == 1
    # Desde ahora el identificador 7 del seguidor es el tracklet nuevo, también al registrar.
    assert int(seguidor.registrar(personas([7])).tracker_id[0]) == nuevo


def test_una_racha_corta_o_floja_no_parte_el_tracklet():
    seguidor = Seguidor()
    for _ in range(20):
        seguidor.votar_equipos(seguidor.registrar(personas([3])), np.array([0]), np.array([1.0]))
    # Tres muestras del otro equipo y vuelta al suyo: un cruce, no un cambio.
    for equipo in [1, 1, 1, 0, 1, 1, 1, 0]:
        seguidas = seguidor.votar_equipos(seguidor.registrar(personas([3])), np.array([equipo]), np.array([0.9]))
        assert int(seguidas.tracker_id[0]) == 3
    # Muestras flojas del otro equipo (color poco claro) tampoco cuentan.
    for _ in range(CORTE_MUESTRAS * 2):
        seguidas = seguidor.votar_equipos(seguidor.registrar(personas([3])), np.array([1]), np.array([0.1]))
        assert int(seguidas.tracker_id[0]) == 3
    assert seguidor.cortes == 0
    assert seguidor.equipo_de(3) == 0


def test_los_arbitros_y_los_sin_equipo_no_votan():
    seguidor = Seguidor()
    seguidas = seguidor.votar_equipos(seguidor.registrar(personas([1, 2])), np.array([-1, 0]), np.array([0.0, 0.5]))
    assert list(seguidas.tracker_id) == [1, 2]
    assert seguidor.equipo_de(1) is None
    assert seguidor.equipo_de(2) == 0
