import json
import random

import numpy as np

from tt_analisis.dataset_balon import _candidatos, _origen_con, _origen_sin, _se_tocan
from tt_analisis.teselas import LADO, SOLAPE, a_fotograma, posiciones, recortar, rejilla


def test_las_teselas_cubren_el_fotograma_y_se_solapan_lo_bastante():
    for total in (1080, 1920, 2160, 3840):
        inicios = posiciones(total)
        assert inicios[0] == 0 and inicios[-1] + LADO == total
        assert all(b - a <= LADO - SOLAPE for a, b in zip(inicios, inicios[1:]))
    assert posiciones(300) == [0]
    # Un fotograma de 1080p: 6 × 4 teselas, el lote que se compila en TensorRT.
    assert len(rejilla(1920, 1080)) == 24


def test_recortar_rellena_con_negro_si_la_imagen_es_pequena():
    imagen = np.full((200, 300, 3), 7, dtype=np.uint8)
    trozo = recortar(imagen, 0, 0)
    assert trozo.shape == (LADO, LADO, 3)
    assert trozo[:200, :300].min() == 7 and trozo[200:].max() == 0


def test_a_fotograma_suma_el_origen_y_quita_el_repetido_del_solape():
    # El mismo balón visto en dos teselas vecinas: queda una sola caja, la de más puntuación, en coordenadas del fotograma.
    cajas = [np.array([[330.0, 100.0, 342.0, 112.0]]), np.array([[23.0, 100.0, 35.0, 112.0]]), np.zeros((0, 4))]
    puntuaciones = [np.array([0.4]), np.array([0.8]), np.zeros(0)]
    xyxy, conf = a_fotograma(cajas, puntuaciones, [(0, 0), (307, 0), (614, 0)])
    assert xyxy.shape == (1, 4) and np.allclose(xyxy[0], [330, 100, 342, 112]) and np.isclose(conf[0], 0.8)
    vacias, sin = a_fotograma([np.zeros((0, 4))], [np.zeros(0)], [(0, 0)])
    assert vacias.shape == (0, 4) and sin.shape == (0,)


def test_las_teselas_del_entrenamiento_contienen_el_balon_entero_o_lo_evitan():
    azar = random.Random(1)
    balon = (900.0, 500.0, 910.0, 510.0)
    for _ in range(50):
        x, y = _origen_con(balon, 1920, 1080, LADO, azar)
        assert x + 12 <= balon[0] and balon[2] <= x + LADO - 12 and y + 12 <= balon[1] and balon[3] <= y + LADO - 12
        dudoso = (300.0, 700.0, 312.0, 712.0)
        sin = _origen_sin([balon, dudoso], 1920, 1080, LADO, azar)
        assert sin is not None and not _se_tocan(balon, *sin, LADO) and not _se_tocan(dudoso, *sin, LADO)
    # Pegado al borde del fotograma se acepta sin margen.
    esquina = (0.0, 0.0, 8.0, 8.0)
    assert _origen_con(esquina, 1920, 1080, LADO, azar) == (0, 0)


def test_solo_lo_quieto_y_flojo_es_negativo_dificil_y_lo_demas_se_evita(tmp_path):
    muestras = tmp_path / "muestras.jsonl"
    candidatos = [
        {"caja": [100, 100, 110, 110, 0.9], "heuristico": True, "movimiento": "movil"},  # el balón elegido
        {"caja": [500, 500, 510, 510, 0.2], "heuristico": False, "movimiento": "quieto"},  # una marca
        {"caja": [700, 500, 710, 510, 0.7], "heuristico": False, "movimiento": "quieto"},  # balón parado a los pies
        {"caja": [900, 500, 910, 510, 0.3], "heuristico": False, "movimiento": "movil"},  # otro que se movía
    ]
    muestras.write_text(json.dumps({"nombre": "f1", "balon": [100, 100, 110, 110, 0.9], "candidatos": candidatos}) + "\n", encoding="utf-8")
    grupo = _candidatos(muestras)["f1"]
    assert grupo.negativos == [(505.0, 505.0)]
    assert grupo.dudosos == [(700, 500, 710, 510), (900, 500, 910, 510)]
