import numpy as np

from tt_analisis.colores import ClasificadorColor, lab_a_rgb, pixeles_camiseta, rasgos_camiseta
from tt_analisis.equipos import color_hex

CESPED = (40, 120, 60)  # BGR: verde de césped
RNG = np.random.default_rng(7)


def recorte(camiseta_bgr, rayas_bgr=None, alto=80, ancho=36, luz=1.0):
    """Un jugador de pie sobre césped: cabeza, camiseta (lisa o a rayas), pantalón y medias, con la luz que toque."""
    imagen = np.empty((alto, ancho, 3), dtype=np.uint8)
    imagen[:] = CESPED
    imagen[: int(alto * 0.15), int(ancho * 0.35):int(ancho * 0.65)] = (90, 140, 190)  # piel
    torso = imagen[int(alto * 0.15):int(alto * 0.58), int(ancho * 0.15):int(ancho * 0.85)]
    torso[:] = camiseta_bgr
    if rayas_bgr is not None:
        torso[:, ::2] = rayas_bgr
    imagen[int(alto * 0.58):int(alto * 0.8), int(ancho * 0.2):int(ancho * 0.8)] = (30, 30, 30)  # pantalón
    ruido = RNG.integers(-8, 9, size=imagen.shape)
    return np.clip(imagen.astype(int) * luz + ruido, 0, 255).astype(np.uint8)


def test_el_cesped_no_cuenta_como_camiseta():
    assert pixeles_camiseta(np.full((60, 30, 3), CESPED, dtype=np.uint8)) is None
    assert rasgos_camiseta(np.zeros((0, 0, 3), dtype=np.uint8)) is None
    rasgos = rasgos_camiseta(recorte((160, 60, 20)))
    assert rasgos is not None and rasgos.shape == (8,)


def test_dos_equipaciones_distintas_se_separan_aunque_cambie_la_luz():
    azul_marino = (90, 30, 20)
    rojo, blanco = (30, 30, 200), (235, 235, 235)
    muestras, esperado = [], []
    for luz in (0.6, 0.8, 1.0, 1.15):
        for _ in range(8):
            muestras.append(rasgos_camiseta(recorte(azul_marino, luz=luz)))
            esperado.append(0)
            muestras.append(rasgos_camiseta(recorte(rojo, rayas_bgr=blanco, luz=luz)))
            esperado.append(1)
    clasificador = ClasificadorColor()
    clasificador.ajustar(muestras)

    predicciones = clasificador.predecir(muestras)
    # Los grupos salen en el orden de tamaño, así que el acierto se mide salvo etiqueta.
    aciertos = max(np.mean(predicciones == esperado), np.mean(predicciones != esperado))
    assert aciertos == 1.0
    assert clasificador.reparto == (32, 32)
    assert not clasificador.desequilibrado

    nuevo = clasificador.predecir([rasgos_camiseta(recorte(rojo, rayas_bgr=blanco, luz=0.9))])[0]
    assert nuevo == predicciones[1]

    # La puntuación dice de qué lado cae cada recorte y cuánto: ±1 en el centro de cada grupo.
    puntuaciones = clasificador.puntuar(muestras)
    assert np.all((puntuaciones > 0) == (predicciones == 1))
    assert abs(np.median(np.abs(puntuaciones)) - 1.0) < 0.5
    # Un torso a medio camino entre las dos equipaciones puntúa flojo.
    mezcla = rasgos_camiseta(recorte((60, 30, 110), rayas_bgr=(160, 140, 130)))
    assert abs(clasificador.puntuar([mezcla])[0]) < np.median(np.abs(puntuaciones))


def test_un_grupo_minusculo_se_marca_como_desequilibrado():
    muestras = [rasgos_camiseta(recorte((90, 30, 20), luz=l)) for l in np.linspace(0.6, 1.1, 30)]
    muestras += [rasgos_camiseta(recorte((30, 30, 200)))] * 2
    clasificador = ClasificadorColor()
    clasificador.ajustar(muestras)
    assert clasificador.desequilibrado


def test_los_colores_se_convierten_a_hex():
    assert color_hex((255, 0, 16)) == "#ff0010"
    r, g, b = lab_a_rgb(np.array([255, 128, 128], dtype=np.float32))
    assert (r, g, b) == (255, 255, 255)
