import cv2
import numpy as np
import pytest

from tt_analisis.config import CLASE_ARBITRO, CLASE_JUGADOR, Configuracion
from tt_analisis.dorsales import MEDIO_JUGADORES, junto_al_balon
from tt_analisis.tipos import Instante, Persona
from tt_analisis.video import fotogramas, info_video


def _persona(tid, x, y=500.0, alto=100.0, clase=CLASE_JUGADOR):
    return Persona(tid, clase, 0, (x, y), None, (x - 20, y - alto, x + 20, y), 0.9)


def test_se_relee_a_los_de_junto_al_balon_con_la_caja_a_mitad_de_camino():
    anterior = Instante(0, 0.0, [_persona(1, 500), _persona(2, 560), _persona(3, 1500), _persona(9, 520, clase=CLASE_ARBITRO)], (520.0, 490.0), None, False)
    actual = Instante(10, 0.2, [_persona(1, 520), _persona(2, 580), _persona(3, 1500), _persona(4, 530), _persona(9, 520, clase=CLASE_ARBITRO)], None, None, False)
    elegidos = junto_al_balon(anterior, actual, 0.1, {1: 0, 2: 1})
    # El 3 está lejos, el 4 no estaba en el anterior y el árbitro no se lee; el más cercano primero.
    assert [tid for tid, _, _ in elegidos] == [1, 2]
    assert elegidos[0][1] == pytest.approx((490.0, 400.0, 530.0, 500.0)) and elegidos[0][2] == 0 and elegidos[1][2] == 1


def test_sin_balon_o_si_el_seguidor_salto_no_se_relee():
    anterior = Instante(0, 0.0, [_persona(1, 500)], None, None, False)
    actual = Instante(10, 0.2, [_persona(1, 520)], None, None, False)
    assert junto_al_balon(anterior, actual, 0.1, {}) == []
    con_balon = Instante(10, 0.2, [_persona(1, 900)], (900.0, 490.0), None, False)
    assert junto_al_balon(anterior, con_balon, 0.1, {}) == []


def test_como_mucho_los_mas_cercanos():
    muchos = [_persona(k, 500 + 10 * k) for k in range(1, 8)]
    anterior = Instante(0, 0.0, muchos, (500.0, 490.0), None, False)
    actual = Instante(10, 0.2, muchos, (500.0, 490.0), None, False)
    assert [tid for tid, _, _ in junto_al_balon(anterior, actual, 0.1, {})] == list(range(1, MEDIO_JUGADORES + 1))


def test_cada_fotograma_lleva_el_de_la_mitad_entre_el_anterior_y_el(tmp_path):
    ruta = tmp_path / "corto.avi"
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"MJPG"), 25.0, (64, 48))
    if not escritor.isOpened():
        pytest.skip("OpenCV no puede escribir vídeo de prueba en este equipo")
    for i in range(21):
        escritor.write(np.full((48, 64, 3), i * 10, dtype=np.uint8))
    escritor.release()
    config = Configuracion(video=ruta, salida=tmp_path)
    leidos = list(fotogramas(config, info_video(ruta), paso=10, inicio_s=0, fin_s=None, vecinos=2, medio=True))
    assert [f.indice for f in leidos] == [0, 10, 20]
    assert [None if f.medio is None else f.medio.indice for f in leidos] == [None, 5, 15]
    assert [[i.indice for i in f.intermedios] for f in leidos] == [[], [1, 2, 8, 9], [11, 12, 18, 19]]
