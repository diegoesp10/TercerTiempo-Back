import cv2
import numpy as np

from tt_analisis.campo import VERTICES
from tt_analisis.entrenamiento import _emparejar, leer_etiquetas
from tt_analisis.etiquetado import Candidato, confirmar_balon, etiqueta_campo, indices_espejo, linea_yolo, particion_de, recorte_juez, resolver_con_juez, teselas


def _caja(x, y, lado=12.0, conf=0.5):
    return (x - lado / 2, y - lado / 2, x + lado / 2, y + lado / 2, conf)


def test_particion_por_bloques_de_tiempo():
    # Bloques de 2 min; el quinto de cada cinco es de validación (8:00-10:00, 18:00-20:00…).
    assert particion_de(0) == "train"
    assert particion_de(7 * 60 + 59) == "train"
    assert particion_de(8 * 60) == "val"
    assert particion_de(9 * 60 + 59) == "val"
    assert particion_de(10 * 60) == "train"
    assert particion_de(18 * 60 + 30) == "val"


def test_indices_espejo_es_una_involucion():
    espejo = indices_espejo()
    assert len(espejo) == len(VERTICES)
    for i, j in enumerate(espejo):
        assert espejo[j] == i
        assert np.isclose(VERTICES[i][1], VERTICES[j][1])
    # Los puntos de la línea de medio campo son su propio espejo; los del área izquierda van a la derecha.
    assert any(i == j for i, j in enumerate(espejo))
    assert espejo[0] != 0


def test_un_balon_con_mucha_confianza_vale_solo():
    r = confirmar_balon([_caja(500, 400, conf=0.7)], [[], []], [], np.zeros((0, 4)))
    assert r.caja is not None and r.candidatos == 1


def test_un_balon_flojo_necesita_vecinos_u_otro_detector():
    sin_apoyo = confirmar_balon([_caja(500, 400, conf=0.2)], [[], []], [], np.zeros((0, 4)))
    assert sin_apoyo.caja is None and sin_apoyo.candidatos == 1
    con_vecino = confirmar_balon([_caja(500, 400, conf=0.35)], [[_caja(520, 405, conf=0.2)], []], [], np.zeros((0, 4)))
    assert con_vecino.caja is not None
    con_otro = confirmar_balon([_caja(500, 400, conf=0.15)], [[_caja(505, 400, conf=0.15)], []], [_caja(498, 402, conf=0.4)], np.zeros((0, 4)))
    assert con_otro.caja is not None
    dos_vecinos = confirmar_balon([_caja(500, 400, conf=0.22)], [[_caja(480, 400)], [_caja(520, 400)]], [], np.zeros((0, 4)))
    assert dos_vecinos.caja is not None
    # Un vecino en otra parte de la imagen no es el mismo balón.
    lejos = confirmar_balon([_caja(500, 400, conf=0.35)], [[_caja(900, 400)], []], [], np.zeros((0, 4)))
    assert lejos.caja is None


def test_una_cabeza_no_es_un_balon():
    personas = np.array([[480.0, 300.0, 520.0, 420.0]])
    cabeza = confirmar_balon([_caja(500, 310, conf=0.9)], [[], []], [], personas)
    assert cabeza.caja is None and cabeza.candidatos == 0
    # A los pies vale, pero solo si el detector de jugadores también ve ahí el balón.
    assert confirmar_balon([_caja(500, 415, conf=0.9)], [[], []], [], personas).caja is None
    pies = confirmar_balon([_caja(500, 415, conf=0.9)], [[], []], [_caja(502, 414, conf=0.5)], personas)
    assert pies.caja is not None


def test_dos_balones_igual_de_buenos_es_ambiguo_y_el_tamano_imposible_se_descarta():
    r = confirmar_balon([_caja(500, 400, conf=0.7), _caja(1200, 400, conf=0.65)], [[], []], [], np.zeros((0, 4)))
    assert r.caja is None and r.ambiguo
    claro = confirmar_balon([_caja(500, 400, conf=0.9), _caja(1200, 400, conf=0.3)], [[], []], [], np.zeros((0, 4)))
    assert claro.caja is not None and claro.caja[0] < 600
    gigante = confirmar_balon([_caja(500, 400, lado=200, conf=0.9)], [[], []], [], np.zeros((0, 4)))
    assert gigante.caja is None and gigante.candidatos == 0


def test_un_candidato_quieto_respecto_al_cesped_es_una_marca_y_uno_que_se_mueve_un_balon():
    from functools import partial

    from tt_analisis.etiquetado import clasificar_movimiento, puntuar_candidatos

    # Las pruebas se escribieron con los cuatro vecinos de ±0,2 y ±0,4 s.
    clasificar_movimiento = partial(clasificar_movimiento, desfases=(-20, -10, 10, 20))
    puntuar_candidatos = partial(puntuar_candidatos, desfases=(-20, -10, 10, 20))

    # La cámara se desplaza 30 px (a media resolución) hacia la derecha en cada vecino.
    afines = [np.array([[1.0, 0.0, 30.0], [0.0, 1.0, 0.0]]) for _ in range(4)]
    marca = _caja(500, 400, conf=0.45)
    # En los vecinos la marca aparece donde la lleva la cámara: (250+30)*2 = 560 a resolución completa.
    vecinos_marca = [[_caja(560, 400)] for _ in range(4)]
    assert clasificar_movimiento(marca, vecinos_marca, afines)[0] == "quieto"
    # El balón aparece más allá de donde lo llevaría la cámara, y cada vez más lejos: se mueve.
    vecinos_balon = [[_caja(560 + 2 * 4 * d, 400)] for d in (-20, -10, 10, 20)]
    assert clasificar_movimiento(marca, vecinos_balon, afines)[0] == "movil"
    # Si lo que se encontró en los vecinos está sobre una persona (una media que pasa), no cuenta.
    personas_vecinos = [np.array([[560 + 2 * 4 * d - 20, 300.0, 560 + 2 * 4 * d + 20, 410.0]]) for d in (-20, -10, 10, 20)]
    assert clasificar_movimiento(marca, vecinos_balon, afines, personas_vecinos=personas_vecinos)[0] == "indeterminado"
    # Sin vecinos donde encontrarlo no se sabe; con uno solo, vale si el desplazamiento es claro.
    assert clasificar_movimiento(marca, [[], [], [], []], afines)[0] == "indeterminado"
    assert clasificar_movimiento(marca, vecinos_marca, [None] * 4)[0] == "indeterminado"
    assert clasificar_movimiento(marca, [[_caja(560, 400)], [], [], []], afines)[0] == "quieto"
    assert clasificar_movimiento(marca, [[_caja(640, 400)], [], [], []], afines)[0] == "movil"
    # Si en un vecino «se movió» mucho y en otro nada, no es el mismo objeto; un desplazamiento igual en todos los
    # vecinos tampoco es una trayectoria.
    incoherente = [[_caja(560, 400)], [_caja(560, 400)], [_caja(760, 400)], [_caja(560, 400)]]
    assert clasificar_movimiento(marca, [[_caja(640, 400)] for _ in range(4)], afines)[0] == "indeterminado"
    assert clasificar_movimiento(marca, incoherente, afines)[0] == "indeterminado"
    # Lo que en el vecino corresponde a otra marca del fotograma central no cuenta como «el balón se ha movido».
    otra_marca = _caja(540, 400, conf=0.3)
    assert clasificar_movimiento(marca, [[_caja(600, 400)] for _ in range(4)], afines, otros_centro=[otra_marca])[0] == "indeterminado"

    quieto = puntuar_candidatos([marca], vecinos_marca, [], np.zeros((0, 4)), afines)[0]
    assert quieto.movimiento == "quieto" and not quieto.heuristico
    assert not puntuar_candidatos([_caja(500, 400, conf=0.7)], vecinos_marca, [], np.zeros((0, 4)), afines)[0].heuristico
    # Un balón parado en el césped (saque, falta) se queda quieto, pero el detector lo ve con mucha confianza.
    assert puntuar_candidatos([_caja(500, 400, conf=0.8)], vecinos_marca, [], np.zeros((0, 4)), afines)[0].heuristico
    movil = puntuar_candidatos([_caja(500, 400, conf=0.15)], vecinos_balon, [], np.zeros((0, 4)), afines)[0]
    assert movil.movimiento == "movil" and movil.heuristico
    # Sobre una persona (medias, botas) el movimiento no basta: hace falta que el detector de jugadores vea el balón.
    jugador = np.array([[470.0, 250.0, 530.0, 410.0]])
    media = puntuar_candidatos([_caja(500, 400, conf=0.5)], vecinos_balon, [], jugador, afines)[0]
    assert media.movimiento == "movil" and not media.heuristico
    a_los_pies = puntuar_candidatos([_caja(500, 400, conf=0.5)], vecinos_balon, [_caja(510, 405, conf=0.4)], jugador, afines)[0]
    assert a_los_pies.heuristico
    # Parado a los pies: con el otro detector y mucha confianza.
    assert puntuar_candidatos([_caja(500, 400, conf=0.7)], vecinos_marca, [_caja(505, 400)], jugador, afines)[0].heuristico
    assert not puntuar_candidatos([_caja(500, 400, conf=0.4)], vecinos_marca, [_caja(505, 400)], jugador, afines)[0].heuristico
    # Sin movimiento de cámara estimado se aplica la regla de antes.
    sin_camara = puntuar_candidatos([_caja(500, 400, conf=0.7)], [[], [], [], []], [], np.zeros((0, 4)), None)[0]
    assert sin_camara.movimiento == "indeterminado" and sin_camara.heuristico


def test_movimiento_camara_recupera_un_desplazamiento():
    from tt_analisis.etiquetado import movimiento_camara

    rng = np.random.default_rng(1)
    fondo = (rng.random((540, 960)) * 255).astype(np.uint8)
    fondo = cv2.GaussianBlur(fondo, (0, 0), 1.2)
    desplazado = np.roll(fondo, (3, 17), axis=(0, 1))
    m = movimiento_camara(fondo, desplazado)
    assert m is not None
    assert abs(m[0, 2] - 17) < 1.0 and abs(m[1, 2] - 3) < 1.0


def test_el_juez_manda_sobre_la_regla_rapida():
    bueno = Candidato(_caja(500, 400, conf=0.2), 0.2, heuristico=False, veredicto={"balon": True, "confianza": 0.9, "que_es": "balón"})
    marca = Candidato(_caja(900, 400, conf=0.7), 1.0, heuristico=True, veredicto={"balon": False, "confianza": 0.8, "que_es": "marca de la línea"})
    resultado, estado = resolver_con_juez([marca, bueno])
    assert estado == "aprobado" and resultado.caja[0] < 600
    resultado, estado = resolver_con_juez([marca])
    assert estado == "rechazado" and resultado.caja is None
    # Dos balones aprobados en sitios distintos: no se sabe cuál es.
    otro = Candidato(_caja(1500, 700, conf=0.5), 0.8, heuristico=True, veredicto={"balon": True, "confianza": 0.8, "que_es": "balón"})
    _, estado = resolver_con_juez([bueno, otro])
    assert estado == "ambiguo"
    # Si el juez no contesta, vale la regla rápida.
    sin_juez = Candidato(_caja(500, 400, conf=0.7), 0.7, heuristico=True, veredicto={"error": "ollama: timeout"})
    resultado, estado = resolver_con_juez([sin_juez])
    assert estado == "sin_juez" and resultado.caja is not None
    assert resolver_con_juez([])[1] == "sin_candidatos"


def test_recorte_del_juez_es_un_jpeg_centrado_aunque_el_balon_este_en_el_borde():
    imagen = np.zeros((1080, 1920, 3), dtype=np.uint8)
    datos = recorte_juez(imagen, _caja(5, 5, conf=0.5))
    assert datos[:2] == b"\xff\xd8" and len(datos) > 500


def _proyeccion(escala=0.12, desplazamiento=(100.0, 80.0)):
    def f(puntos):
        return np.asarray(puntos, dtype=np.float32) * escala + np.array(desplazamiento, dtype=np.float32)

    return f


def test_etiqueta_campo_reproyecta_los_32_vertices():
    f = _proyeccion()
    ancho, alto = 1920, 1080
    xy = np.zeros((len(VERTICES), 2), dtype=np.float32)
    conf = np.zeros(len(VERTICES), dtype=np.float32)
    vistos = [0, 1, 5, 9, 13, 16, 24, 29]
    xy[vistos] = f(VERTICES[vistos])
    conf[vistos] = 0.8
    # Un punto visto pero mal situado no debe entrar en la homografía.
    xy[21] = f(VERTICES[21]) + np.array([80.0, -60.0], dtype=np.float32)
    conf[21] = 0.9
    etiqueta = etiqueta_campo(xy, conf, ancho, alto)
    assert etiqueta is not None
    assert etiqueta.puntos_vistos == len(vistos)
    assert etiqueta.error_px < 1.0
    visibles = etiqueta.puntos[:, 2] > 0
    # Con esta cámara el campo entero cabe en la imagen (12000 × 0,12 + 100 < 1920): todos visibles y donde toca.
    assert visibles.all()
    assert np.allclose(etiqueta.puntos[:, :2], f(VERTICES), atol=1.0)
    x1, y1, x2, y2 = etiqueta.caja
    assert x1 <= etiqueta.puntos[:, 0].min() and x2 >= etiqueta.puntos[:, 0].max() and y1 <= etiqueta.puntos[:, 1].min() and y2 >= etiqueta.puntos[:, 1].max()


def test_sin_medidas_fiables_solo_se_etiquetan_los_puntos_vistos():
    f = _proyeccion()
    xy = np.zeros((len(VERTICES), 2), dtype=np.float32)
    conf = np.zeros(len(VERTICES), dtype=np.float32)
    vistos = [0, 1, 5, 9, 13, 16, 24, 29]
    xy[vistos] = f(VERTICES[vistos])
    conf[vistos] = 0.8
    xy[21] = f(VERTICES[21]) + np.array([80.0, -60.0], dtype=np.float32)
    conf[21] = 0.9
    etiqueta = etiqueta_campo(xy, conf, 1920, 1080, reproyectar=False)
    assert etiqueta is not None
    etiquetados = np.flatnonzero(etiqueta.puntos[:, 2] > 0).tolist()
    assert etiquetados == vistos
    assert np.allclose(etiqueta.puntos[vistos, :2], f(VERTICES[vistos]), atol=0.5)


def test_etiqueta_campo_con_pocos_puntos_o_alineados_no_vale():
    f = _proyeccion()
    xy = np.zeros((len(VERTICES), 2), dtype=np.float32)
    conf = np.zeros(len(VERTICES), dtype=np.float32)
    pocos = [0, 1, 5, 9, 13]
    xy[pocos] = f(VERTICES[pocos])
    conf[pocos] = 0.8
    assert etiqueta_campo(xy, conf, 1920, 1080) is None
    # Seis puntos sobre la misma línea de fondo (x = 0): sin extensión horizontal no hay homografía fiable.
    xy[:] = 0
    conf[:] = 0
    linea = [i for i, v in enumerate(VERTICES) if v[0] == 0][:6]
    xy[linea] = f(VERTICES[linea])
    conf[linea] = 0.9
    assert etiqueta_campo(xy, conf, 1920, 1080) is None


def test_linea_yolo_normaliza_y_marca_los_puntos_no_visibles():
    linea = linea_yolo(2, (100, 200, 300, 600), 1000, 1000)
    assert linea == "2 0.200000 0.400000 0.200000 0.400000"
    puntos = np.array([[500, 250, 2], [0, 0, 0]], dtype=np.float32)
    con_puntos = linea_yolo(0, (-10, 0, 1000, 500), 1000, 1000, puntos)
    assert con_puntos.endswith("0.500000 0.250000 2 0 0 0")
    assert con_puntos.startswith("0 0.500000 0.250000 1.000000 0.500000")


def test_las_muestras_llevan_sus_seis_vecinos(tmp_path):
    from tt_analisis.config import Segmento
    from tt_analisis.etiquetado import ConfiguracionEtiquetado, _muestras
    from tt_analisis.video import InfoVideo

    ruta = tmp_path / "v.mp4"
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"mp4v"), 25.0, (64, 48))
    for i in range(120):
        escritor.write(np.full((48, 64, 3), i % 255, dtype=np.uint8))
    escritor.release()
    info = InfoVideo(ruta, 64, 48, 25.0, 120)
    config = ConfiguracionEtiquetado(video=ruta, salida=tmp_path, cada_s=1.0, segmento=Segmento(), max_muestras=3)
    muestras = list(_muestras(info, config))
    assert len(muestras) == 3
    # A 25 fps los vecinos de ±0,1, ±0,2 y ±0,4 s están a ±2, ±5 y ±10 fotogramas.
    assert muestras[0].indice == 10 and len(muestras[0].vecinos) == 6
    assert muestras[1].indice == 35
    # El códec altera el valor plano de cada fotograma, pero el orden temporal (−10, −5, −2, +2, +5, +10) se conserva.
    valores = [int(v[0, 0, 0]) for v in muestras[0].vecinos]
    assert valores == sorted(valores) and valores[0] < int(muestras[0].imagen[0, 0, 0]) < valores[-1]


def test_teselas_cubren_la_imagen_y_se_pegan_al_borde():
    posiciones = teselas(1920, 1080)
    assert len(posiciones) == 8
    assert (0, 0) in posiciones and (1280, 440) in posiciones
    assert all(x + 640 <= 1920 and y + 640 <= 1080 for x, y in posiciones)
    assert teselas(640, 480) == [(0, 0)]


def test_emparejar_por_centro_y_por_iou():
    verdad = np.array([[100, 100, 112, 112], [500, 500, 512, 512]], dtype=float)
    pred = np.array([[104, 103, 116, 115], [900, 900, 912, 912]], dtype=float)
    assert _emparejar(pred, verdad, 0.0, por_centro=True) == (1, 1, 1)
    grandes_v = np.array([[0, 0, 100, 200]], dtype=float)
    grandes_p = np.array([[5, 5, 105, 205], [300, 300, 400, 500]], dtype=float)
    assert _emparejar(grandes_p, grandes_v, 0.5, por_centro=False) == (1, 1, 0)


def test_leer_etiquetas_yolo(tmp_path):
    archivo = tmp_path / "x.txt"
    archivo.write_text("0 0.5 0.5 0.2 0.4 0.1 0.2 2 0 0 0\n2 0.25 0.25 0.1 0.1\n", encoding="utf-8")
    etiquetas = leer_etiquetas(archivo, 1000, 500)
    assert len(etiquetas) == 2
    clase, caja, puntos = etiquetas[0]
    assert clase == 0 and np.allclose(caja, [400, 150, 600, 350])
    assert puntos.shape == (2, 3) and np.allclose(puntos[0], [100, 100, 2]) and puntos[1][2] == 0
    assert etiquetas[1][2] is None
    assert leer_etiquetas(tmp_path / "no-existe.txt", 10, 10) == []
