import numpy as np

from tt_analisis.campo import VERTICES, Calibrador, Homografia


def _proyeccion(escala=0.1, desplazamiento=(100.0, 50.0)):
    # Una cámara "ideal" que ve el campo en planta: imagen = campo * escala + desplazamiento.
    def f(puntos):
        return np.asarray(puntos, dtype=np.float32) * escala + np.array(desplazamiento, dtype=np.float32)

    return f


def test_homografia_ida_y_vuelta():
    f = _proyeccion()
    indices = [0, 5, 13, 16, 24, 29, 8, 21]
    h = Homografia(f(VERTICES[indices]), VERTICES[indices])
    campo = h.a_campo(f(np.array([[6000, 3500], [1000, 1000]])))
    assert np.allclose(campo, [[6000, 3500], [1000, 1000]], atol=1.0)
    assert h.error_px(f(VERTICES[indices]), VERTICES[indices]) < 0.5
    assert np.allclose(h.a_imagen(np.array([[0, 0]])), [[100, 50]], atol=0.5)


def test_camara_fija_toma_la_mediana_de_cada_punto():
    f = _proyeccion()
    calibrador = Calibrador(modelo=None, device="cpu", modo="fija")
    indices = [0, 5, 13, 16, 24, 29, 8, 21]
    for ruido in (-3.0, 0.0, 3.0, 40.0):
        for i in indices:
            x, y = f(VERTICES[i])
            calibrador._acumulado[i].append((float(x + ruido), float(y)))
    fija = calibrador.fijar(minimo_vistas=3)
    assert fija.valida and fija.puntos_usados == len(indices)
    assert np.allclose(calibrador.punto_a_campo(tuple(f(np.array([6000, 3500])))), (6000, 3500), atol=15)


def test_camara_movil_invalida_la_homografia_cuando_la_camara_se_mueve():
    f = _proyeccion()
    calibrador = Calibrador(modelo=None, device="cpu", modo="movil", max_antiguedad_s=1.0)
    indices = [0, 5, 13, 16, 24, 29]
    calibrador.actual = calibrador._desde_puntos(f(VERTICES[indices]), VERTICES[indices], t=0.0)
    assert calibrador.actual.valida

    # Dos puntos coherentes con la homografía: se conserva.
    g = _proyeccion(desplazamiento=(100.0, 50.0))
    calibrador._detectar = lambda imagen: (g(VERTICES), np.isin(np.arange(len(VERTICES)), [0, 29]))
    assert calibrador.calibrar(None, 0.2).valida

    # La cámara se ha movido 300 px: con dos puntos se nota y se invalida.
    g2 = _proyeccion(desplazamiento=(400.0, 50.0))
    calibrador._detectar = lambda imagen: (g2(VERTICES), np.isin(np.arange(len(VERTICES)), [0, 29]))
    assert not calibrador.calibrar(None, 0.4).valida
    assert calibrador.invalidaciones == 1

    # Con puntos suficientes se recalibra a la nueva posición.
    calibrador._detectar = lambda imagen: (g2(VERTICES), np.isin(np.arange(len(VERTICES)), indices))
    nueva = calibrador.calibrar(None, 0.6)
    assert nueva.valida and np.allclose(calibrador.punto_a_campo(tuple(g2(np.array([6000, 3500])))), (6000, 3500), atol=2)

    # Sin puntos, la homografía vale un segundo y luego caduca.
    calibrador._detectar = lambda imagen: None
    assert calibrador.calibrar(None, 1.2).valida
    assert not calibrador.calibrar(None, 2.0).valida


def test_un_salto_de_medio_campo_con_pocos_puntos_se_rechaza():
    f = _proyeccion()
    calibrador = Calibrador(modelo=None, device="cpu", modo="movil")
    indices = [0, 5, 13, 16, 24, 29]
    calibrador._detectar = lambda imagen: (f(VERTICES), np.isin(np.arange(len(VERTICES)), indices))
    assert calibrador.calibrar(np.zeros((1080, 1920, 3), dtype=np.uint8), 0.0).valida
    # La misma imagen, pero el detector confunde el lado: los puntos del área izquierda aparecen donde están los de
    # la derecha (una homografía reflejada, con el centro de la imagen 50 m más allá).
    espejo = f(VERTICES).copy()
    izquierda, derecha = [0, 1, 5, 9, 10, 12], [24, 25, 29, 17, 18, 20]
    espejo[izquierda] = f(VERTICES)[derecha]
    calibrador._detectar = lambda imagen: (espejo, np.isin(np.arange(len(VERTICES)), izquierda))
    calibrador.calibrar(np.zeros((1080, 1920, 3), dtype=np.uint8), 0.2)
    assert calibrador.saltos_rechazados == 1 and calibrador.actual.valida
    # La homografía buena sigue en pie si los pocos puntos coherentes la avalan; aquí no lo hacen, así que cae.
    calibrador.invalidar(0.4)
    assert not calibrador.actual.valida
