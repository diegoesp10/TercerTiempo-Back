import numpy as np

from tt_analisis.colores import ClasificadorColor, lab_a_rgb, pixeles_camiseta, rasgos_camiseta, rasgos_equipacion, tapados
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


def escena(torso_bgr, pantalon_bgr, luz=1.0, alto=80, ancho=30):
    """Un jugador de pie en medio de un trozo de césped, con su caja: camiseta y pantalón lisos."""
    imagen = np.empty((alto + 20, ancho * 3, 3), dtype=np.uint8)
    imagen[:] = CESPED
    x1, y1 = ancho, 10
    imagen[y1 + int(0.15 * alto):y1 + int(0.5 * alto), x1 + int(0.1 * ancho):x1 + int(0.9 * ancho)] = torso_bgr
    imagen[y1 + int(0.5 * alto):y1 + int(0.75 * alto), x1 + int(0.15 * ancho):x1 + int(0.85 * ancho)] = pantalon_bgr
    ruido = RNG.integers(-6, 7, size=imagen.shape)
    return np.clip(imagen.astype(int) * luz + ruido, 0, 255).astype(np.uint8), (x1, y1, x1 + ancho, y1 + alto)


def test_la_equipacion_de_un_portero_verde_lima_no_se_la_come_el_cesped():
    lima, blanco = (40, 230, 180), (235, 235, 235)
    rasgos = rasgos_equipacion(*escena(lima, lima))
    assert rasgos is not None and rasgos.shape == (6,)
    # Más clara que el césped, verde (a < 128) y amarillenta (b > 128): el torso es la camiseta, no el césped.
    assert rasgos[0] > 40 and rasgos[1] < 110 and rasgos[2] > 150
    # Con menos luz sigue muy por encima del césped y igual de verde.
    oscura = rasgos_equipacion(*escena(lima, lima, luz=0.7))
    assert oscura[0] > 40 and oscura[1] < 110
    assert rasgos_equipacion(*escena(blanco, blanco)) is not None


def test_el_pantalon_separa_al_arbitro_de_un_portero_con_una_camiseta_parecida():
    menta, negro, lima = (170, 230, 150), (25, 25, 25), (40, 230, 180)
    arbitro = rasgos_equipacion(*escena(menta, negro))
    portero = rasgos_equipacion(*escena(lima, lima))
    assert portero[3] - arbitro[3] > 100


def test_una_persona_muy_pequena_no_da_equipacion():
    imagen, _ = escena((40, 230, 180), (40, 230, 180))
    assert rasgos_equipacion(imagen, (30, 10, 40, 30)) is None


def test_la_equipacion_de_quien_esta_tapado_por_otro_que_esta_delante_no_cuenta():
    # El 1 tapa el cuerpo del 0 y está delante (sus pies, más abajo); el 2 está solo.
    cajas = np.array([[0, 0, 30, 80], [10, 10, 40, 95], [200, 0, 230, 80]], dtype=float)
    assert tapados(cajas).tolist() == [True, False, False]
    # Si el que se cruza está detrás, el tapado es él.
    assert tapados(np.array([[0, 0, 30, 80], [10, 10, 40, 70]], dtype=float)).tolist() == [False, True]
    assert tapados(np.zeros((1, 4))).tolist() == [False]
