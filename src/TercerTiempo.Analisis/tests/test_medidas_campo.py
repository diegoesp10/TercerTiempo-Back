import cv2
import numpy as np
import pytest

from tt_analisis import geometria
from tt_analisis.campo import CONFIG_CAMPO, VERTICES, configurar_campo, medir_dimensiones, vertices_para
from tt_analisis.config import a_medidas
from tt_analisis.geometria import Campo


@pytest.fixture
def campo_reglamentario():
    yield
    configurar_campo(12000, 7000)


def test_configurar_campo_cambia_vertices_y_geometria_en_sitio(campo_reglamentario):
    configurar_campo(10000, 6200)
    assert CONFIG_CAMPO.length == 10000 and CONFIG_CAMPO.width == 6200
    assert VERTICES[29].tolist() == [10000, 6200]
    # Las áreas no cambian: el vértice 9 (esquina del área grande) sigue a 20,15 m de la línea de fondo.
    assert VERTICES[9][0] == 2015 and VERTICES[9][1] == (6200 - 4100) / 2
    campo = Campo()
    assert campo.largo == 10000 and campo.ancho == 6200
    assert campo.centro == (5000, 3100)
    assert geometria.DIMENSIONES["largo"] == 10000


def _camara(largo, ancho, angulo_deg, escala, desplazamiento):
    # Una homografía "de cámara" cualquiera: giro, escala y perspectiva suave.
    a = np.deg2rad(angulo_deg)
    r = np.array([[np.cos(a), -np.sin(a), desplazamiento[0]], [np.sin(a), np.cos(a), desplazamiento[1]], [0, 0, 1]], dtype=np.float64)
    s = np.diag([escala, escala, 1.0])
    p = np.array([[1, 0, 0], [0, 1, 0], [1e-5, 2e-5, 1]], dtype=np.float64)
    return p @ r @ s


def test_medir_dimensiones_recupera_el_campo_real(campo_reglamentario):
    rng = np.random.default_rng(0)
    largo_real, ancho_real = 10000.0, 6200.0
    vertices = vertices_para(largo_real, ancho_real).astype(np.float64)
    observaciones = []
    for k in range(40):
        h = _camara(largo_real, ancho_real, rng.uniform(-8, 8), rng.uniform(0.08, 0.14), (rng.uniform(50, 400), rng.uniform(50, 300)))
        proyectados = cv2.perspectiveTransform(vertices.reshape(-1, 1, 2), h).reshape(-1, 2)
        # Cada "fotograma" ve un subconjunto distinto de puntos, con un poco de ruido.
        vistos = rng.choice(len(vertices), size=rng.integers(7, 14), replace=False)
        xy = np.zeros((len(vertices), 2), dtype=np.float32)
        conf = np.zeros(len(vertices), dtype=np.float32)
        xy[vistos] = proyectados[vistos] + rng.normal(0, 1.5, size=(len(vistos), 2))
        conf[vistos] = 0.9
        observaciones.append((xy, conf))
    largo, ancho, error, usados = medir_dimensiones(observaciones)
    assert usados == 40
    assert abs(largo - largo_real) <= 100 and abs(ancho - ancho_real) <= 100
    assert error < 5


def test_medir_dimensiones_sin_puntos_falla_claramente():
    with pytest.raises(ValueError):
        medir_dimensiones([(np.zeros((32, 2), dtype=np.float32), np.zeros(32, dtype=np.float32))])


def test_a_medidas():
    assert a_medidas("100x62") == (100.0, 62.0)
    assert a_medidas("98.5 x 60") == (98.5, 60.0)
    assert a_medidas("100,62") == (100.0, 62.0)
    assert a_medidas(None) is None
    with pytest.raises(ValueError):
        a_medidas("100")
    with pytest.raises(ValueError):
        a_medidas("300x20")
