import numpy as np

from tt_analisis.campo import VERTICES
from tt_analisis.dataset_campo import linea_destilada, puntos_visibles
from tt_analisis.metricas import leer_etiquetas

ANCHO, ALTO = 1920, 1080


def _maestro(vistos):
    xy = np.zeros((len(VERTICES), 2), dtype=np.float32)
    conf = np.zeros(len(VERTICES), dtype=np.float32)
    for i, (x, y, c) in vistos.items():
        xy[i] = (x, y)
        conf[i] = c
    return xy, conf


def test_solo_cuentan_los_puntos_seguros_y_dentro_de_la_imagen():
    xy, conf = _maestro({0: (100, 200, 0.9), 1: (300, 400, 0.3), 2: (-50, 400, 0.95), 3: (1919.5, 500, 0.9), 4: (800, 600, 0.5)})
    assert np.flatnonzero(puntos_visibles(xy, conf, ANCHO, ALTO)).tolist() == [0, 4]


def test_la_linea_lleva_los_puntos_vistos_y_deja_el_resto_sin_etiqueta(tmp_path):
    xy, conf = _maestro({0: (100, 200, 0.9), 5: (900, 300, 0.8), 13: (1500, 700, 0.7), 20: (400, 900, 0.2)})
    linea = linea_destilada(np.array([200.0, 250.0, 1000.0, 650.0]), xy, conf, ANCHO, ALTO)
    ruta = tmp_path / "f.txt"
    ruta.write_text(linea, encoding="utf-8")
    [(clase, caja, puntos)] = leer_etiquetas(ruta, ANCHO, ALTO)
    assert clase == 0 and puntos.shape == (len(VERTICES), 3)
    assert np.flatnonzero(puntos[:, 2]).tolist() == [0, 5, 13]
    assert set(puntos[:, 2]) == {0.0, 2.0}
    assert np.allclose(puntos[[0, 5, 13], :2], [[100, 200], [900, 300], [1500, 700]], atol=0.5)
    # La caja abarca la del maestro y todos los puntos vistos.
    assert np.allclose(caja, [100, 200, 1500, 700], atol=0.5)


def test_sin_puntos_vistos_el_fotograma_es_negativo():
    xy, conf = _maestro({3: (500, 500, 0.4)})
    assert linea_destilada(np.array([0.0, 0.0, 100.0, 100.0]), xy, conf, ANCHO, ALTO) is None
    assert linea_destilada(None, *_maestro({}), ANCHO, ALTO) is None
