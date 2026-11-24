import numpy as np
import pytest
import supervision as sv

from tt_analisis.config import CLASE_JUGADOR, CLASE_PORTERO
from tt_analisis.seguimiento import CORTE_MUESTRAS, MAX_EQUIPACIONES, TRACKLET_CORTADO_BASE, MemoriaTracklet, Seguidor


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


def test_un_portero_no_tiene_equipo_por_la_camiseta_ni_se_parte_y_cuentan_todas_las_lecturas_de_su_dorsal():
    seguidor = Seguidor()
    portero = personas([5])
    portero.class_id = np.array([CLASE_PORTERO])
    for _ in range(60):
        seguidor.registrar(portero)
    # Algún fotograma lo toma por jugador de campo y le vota la camiseta: unas veces de un equipo y luego del otro (el
    # que tiene más cerca).
    for equipo in [0] * 5 + [1] * (CORTE_MUESTRAS * 2):
        seguidas = seguidor.votar_equipos(seguidor.registrar(personas([5])), np.array([equipo]), np.array([0.9]))
        assert int(seguidas.tracker_id[0]) == 5
    assert seguidor.cortes == 0
    assert seguidor.equipo_de(5) is None

    memoria = seguidor.memorias[5]
    trece = np.zeros(99)
    trece[12] = 1.0
    memoria.sumar_dorsal(trece, 0.5, np.zeros((4, 4, 3), dtype=np.uint8), equipo=0)
    memoria.sumar_dorsal(trece, 0.7, np.zeros((4, 4, 3), dtype=np.uint8), equipo=1)
    assert memoria.votos_dorsal[12] == pytest.approx(1.2)
    assert memoria.mejor_dorsal[:2] == (pytest.approx(0.7), 13)


def test_las_muestras_de_la_equipacion_se_guardan_repartidas_por_todo_el_tracklet():
    memoria = MemoriaTracklet(1)
    memoria.sumar_equipacion(np.zeros(6))
    memoria.sumar_equipacion(np.zeros(6))
    assert memoria.equipacion is None  # con dos muestras aún no se fía
    memoria = MemoriaTracklet(2)
    for k in range(1000):
        memoria.sumar_equipacion(np.full(6, float(k)), k / 5)
    valores = [float(m[0]) for m in memoria.equipaciones]
    assert len(valores) <= MAX_EQUIPACIONES
    # Desde el principio hasta casi el final, y la mediana, a mitad del tracklet.
    assert valores[0] == 0.0 and valores[-1] > 950
    assert abs(float(memoria.equipacion[0]) - 500) < 20
    # Cada muestra, con el momento en que se tomó (también tras quedarse con una de cada dos).
    assert memoria.tiempos_equipacion == [v / 5 for v in valores]
