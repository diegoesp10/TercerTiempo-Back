import cv2
import numpy as np
import pytest
import supervision as sv

from tt_analisis.balon import completar_con_intermedios
from tt_analisis.config import Configuracion
from tt_analisis.deteccion import VENTANA_MIN_PX, Detector, Muestreado, elegir_en_ventana, ventana_desde
from tt_analisis.tipos import Instante
from tt_analisis.video import Fotograma, fotogramas, info_video

DT = 1 / 25


class _PorDiez:
    """Una calibración de prueba: el punto de la imagen por 10 es el del campo (cm)."""

    def a_campo(self, puntos):
        return np.asarray(puntos, dtype=np.float32) * 10


def _instante(i, balon=None, conf=None, calibrado=True):
    campo = None if balon is None or not calibrado else (balon[0] * 10, balon[1] * 10)
    return Instante(i, i * 0.2, [], balon, campo, calibrado, balon_confianza=conf, homografia=_PorDiez() if calibrado else None)


def test_la_ventana_parte_del_balon_de_al_lado_con_su_velocidad_y_crece_con_el_tiempo():
    centro, radio = ventana_desde(0.08, (0.0, np.array([100.0, 100.0]), np.array([1000.0, 0.0])))
    assert np.allclose(centro, [180, 100])
    assert radio == pytest.approx(VENTANA_MIN_PX + 600 * 0.08)
    # Hacia atrás desde el que se vio después.
    centro, _ = ventana_desde(0.12, (0.2, np.array([50.0, 60.0]), None))
    assert np.allclose(centro, [50, 60])
    assert ventana_desde(3.0, (0.0, np.array([1.0, 1.0]), None)) is None


def test_en_la_ventana_gana_el_de_tamano_posible_y_cerca_del_centro():
    cajas = np.array([
        [195, 95, 205, 105],    # 10 px, en el centro, 0,6
        [340, 95, 350, 105],    # 10 px, a 145 px, 0,7
        [180, 80, 280, 180],    # 100 px: no es el balón
        [600, 95, 610, 105],    # fuera del radio
        [199, 99, 209, 109],    # en el centro pero por debajo del mínimo
    ], dtype=np.float32)
    puntuaciones = np.array([0.6, 0.7, 0.9, 0.95, 0.1], dtype=np.float32)
    centro, conf = elegir_en_ventana(cajas, puntuaciones, np.array([200.0, 100.0]), 200.0)
    assert np.allclose(centro, [200, 100]) and conf == pytest.approx(0.6)
    assert elegir_en_ventana(cajas[2:4], puntuaciones[2:4], np.array([200.0, 100.0]), 200.0) is None


class _BalonFalso:
    """Detector del balón de prueba: en cada imagen, un balón de 10 px en `donde` con confianza 0,8; apunta las zonas."""

    def __init__(self, donde=(500.0, 300.0)):
        self.donde = donde
        self.zonas = []

    def detectar_zonas(self, imagenes, zonas, conf):
        self.zonas.extend(zonas)
        x, y = self.donde
        return [(np.array([[x - 5, y - 5, x + 5, y + 5]], dtype=np.float32), np.array([0.8], dtype=np.float32)) for _ in imagenes]


class _Modelos:
    def __init__(self, balon):
        self.balon = balon


def _detector(balon) -> Detector:
    detector = Detector.__new__(Detector)
    detector.modelos = _Modelos(balon)
    detector.perdido = detector.descartados = detector.flojos_descartados = 0
    detector.intermedios_mirados = detector.intermedios_vistos = detector.vecinos_enteros = 0
    detector._ultimo_balon = None
    return detector


def _jugador_en(x, y) -> sv.Detections:
    return sv.Detections(xyxy=np.array([[x - 10, y - 60, x + 10, y]], dtype=np.float32), class_id=np.array([2]), confidence=np.array([0.9], dtype=np.float32))


def _vecinos(indices):
    return [Fotograma(i, i * DT, np.zeros((1080, 1920, 3), dtype=np.uint8)) for i in indices]


def _sin_balon(t):
    return Muestreado(t, None, None, _jugador_en(500, 300))


def test_solo_se_miran_los_vecinos_del_muestreado_sin_balon_y_enteros_si_no_hay_balon_cerca():
    balon = _BalonFalso()
    detector = _detector(balon)
    previo = Muestreado(0.0, np.array([480.0, 300.0], dtype=np.float32), None, _jugador_en(500, 300))
    salida = detector.balon_vecinos(_vecinos([1, 2, 3, 4]), previo, _sin_balon(5 * DT), 2, DT)
    # Al previo no le falta: solo los dos de antes del actual, en una ventana desde el balón del previo.
    assert [round(t / DT) for t, _, _ in salida] == [3, 4]
    assert detector.vecinos_enteros == 0 and all(z != (0, 0, 1920, 1080) for z in balon.zonas)
    # Sin balón en ninguno de los dos ni uno reciente: la imagen entera, en uno solo, el más alejado del actual.
    balon.zonas.clear()
    salida = detector.balon_vecinos(_vecinos([6, 7, 8, 9]), _sin_balon(5 * DT), _sin_balon(10 * DT), 2, DT)
    assert [round(t / DT) for t, _, _ in salida] == [8]
    assert detector.vecinos_enteros == 1 and balon.zonas == [(0, 0, 1920, 1080)]
    # Con más enteros por tramo, después el más alejado del previo; sin tope, los cuatro.
    salida = detector.balon_vecinos(_vecinos([6, 7, 8, 9]), _sin_balon(5 * DT), _sin_balon(10 * DT), 2, DT, max_enteros=2)
    assert [round(t / DT) for t, _, _ in salida] == [7, 8]
    salida = detector.balon_vecinos(_vecinos([6, 7, 8, 9]), _sin_balon(5 * DT), _sin_balon(10 * DT), 2, DT, max_enteros=4)
    assert [round(t / DT) for t, _, _ in salida] == [6, 7, 8, 9]
    assert detector.vecinos_enteros == 7


def test_con_un_vecino_por_lado_solo_se_mira_el_pegado():
    detector = _detector(_BalonFalso())
    salida = detector.balon_vecinos(_vecinos([1, 2, 3, 4]), _sin_balon(0.0), _sin_balon(5 * DT), 1, DT, max_enteros=2)
    assert [round(t / DT) for t, _, _ in salida] == [1, 4]


def test_el_balon_de_los_vecinos_completa_el_instante_que_no_lo_tenia():
    instantes = [_instante(0, (100.0, 100.0), 0.8), _instante(1), _instante(2)]
    pista = [(0.2 - DT, (110.0, 100.0), 0.6), (0.2 + DT, (130.0, 100.0), 0.5)]
    resumen = completar_con_intermedios(instantes, pista, DT)
    assert (resumen.detecciones, resumen.completados) == (2, 1)
    assert instantes[1].balon_imagen == pytest.approx((120.0, 100.0))
    assert instantes[1].balon_campo == pytest.approx((1200.0, 1000.0))
    assert instantes[1].balon_origen == "visto" and instantes[1].balon_confianza == 0.5
    assert instantes[2].balon_imagen is None


def test_con_uno_solo_de_al_lado_vale_hasta_dos_fotogramas_y_no_toca_el_balon_que_ya_habia():
    a_dos = [_instante(0), _instante(1)]
    completar_con_intermedios(a_dos, [(0.2 + 2 * DT, (130.0, 100.0), 0.6)], DT)
    assert a_dos[1].balon_imagen == (130.0, 100.0)
    a_tres = [_instante(0), _instante(1)]
    completar_con_intermedios(a_tres, [(0.2 + 3 * DT, (130.0, 100.0), 0.6)], DT)
    assert a_tres[1].balon_imagen is None
    ya = [_instante(0), _instante(1, (500.0, 500.0), 0.3)]
    assert completar_con_intermedios(ya, [(0.2 + DT, (130.0, 100.0), 0.9)], DT).completados == 0
    assert ya[1].balon_imagen == (500.0, 500.0)


def test_lo_que_cae_fuera_del_campo_no_vale_y_sin_calibrar_queda_solo_en_la_imagen():
    fuera = [_instante(0), _instante(1)]
    completar_con_intermedios(fuera, [(0.2 + DT, (2000.0, 2000.0), 0.9)], DT)
    assert fuera[1].balon_imagen is None
    sin_campo = [_instante(0, calibrado=False), _instante(1, calibrado=False)]
    completar_con_intermedios(sin_campo, [(0.2 + DT, (2000.0, 2000.0), 0.9)], DT)
    assert sin_campo[1].balon_imagen == (2000.0, 2000.0) and sin_campo[1].balon_campo is None


def test_cada_fotograma_lleva_sus_vecinos_y_los_del_anterior(tmp_path):
    ruta = tmp_path / "corto.avi"
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"MJPG"), 25.0, (64, 48))
    if not escritor.isOpened():
        pytest.skip("OpenCV no puede escribir vídeo de prueba en este equipo")
    for i in range(16):
        escritor.write(np.full((48, 64, 3), i * 10, dtype=np.uint8))
    escritor.release()
    info = info_video(ruta)
    config = Configuracion(video=ruta, salida=tmp_path)
    leidos = list(fotogramas(config, info, paso=5, inicio_s=0, fin_s=None, vecinos=1))
    assert [f.indice for f in leidos] == [0, 5, 10, 15]
    assert [[i.indice for i in f.intermedios] for f in leidos] == [[], [1, 4], [6, 9], [11, 14]]
    dos = list(fotogramas(config, info, paso=5, inicio_s=0, fin_s=None, vecinos=2))
    assert [[i.indice for i in f.intermedios] for f in dos] == [[], [1, 2, 3, 4], [6, 7, 8, 9], [11, 12, 13, 14]]
    assert all(not f.intermedios for f in fotogramas(config, info, paso=5, inicio_s=0, fin_s=None))
