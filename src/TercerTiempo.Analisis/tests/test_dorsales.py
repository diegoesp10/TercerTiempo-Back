import json
import random
from pathlib import Path

import cv2
import numpy as np
import pytest

from tt_analisis.analizador import guardar_zonas_porteros, muestras_de_porteros
from tt_analisis.config import CLASE_JUGADOR, CLASE_PORTERO, Configuracion
from tt_analisis.dorsales import (
    ALTO,
    ANCHO,
    TRACKLET_PORTERO_BASE,
    GuardaZonas,
    Lectura,
    etiquetar_portero,
    nota_portero,
    ratos_portero,
)
from tt_analisis.entrenar_dorsales import cargar, elegir_entreno
from tt_analisis.identidades import Identidad, Resolucion
from tt_analisis.porteros import Porteros
from tt_analisis.tipos import Instante, Persona
from tt_analisis.video import fotogramas_elegidos, info_video


def _lectura(dorsal: int, peso: float) -> Lectura:
    return Lectura(np.eye(99)[dorsal - 1], sin_numero=1.0 - peso)


def test_la_nota_del_portero_es_lo_mas_que_le_lee_su_numero_alguno_de_los_lectores():
    afinado = [_lectura(11, 0.9), _lectura(13, 0.2)]
    publicado = [_lectura(1, 0.8), _lectura(13, 0.6)]
    assert nota_portero([afinado, publicado], 1).tolist() == pytest.approx([0.8, 0.0])
    assert nota_portero([afinado, publicado], 13).tolist() == pytest.approx([0.0, 0.6])


def test_del_portero_se_guardan_sus_espaldas_y_las_sin_numero_lejos_de_cualquier_rastro_de_su_numero():
    t = np.arange(0.0, 20.0, 0.5)
    nota = np.zeros(len(t))
    nota[[4, 5]] = 0.9  # de espaldas, a los 2 s
    nota[30] = 0.2  # algo de su número a los 15 s: puede ser la espalda de lado
    tapado = np.zeros(len(t), dtype=bool)
    tapado[5] = True  # con alguien delante: la zona puede ser la del otro
    tapado[22] = True
    etiqueta = etiquetar_portero(t, nota, tapado)
    assert etiqueta[4] == 1
    assert etiqueta[5] == -1 and etiqueta[22] == -1
    # A 4 s o menos de una zona con algo de su número no se sabe si se le ve: ni con número ni sin él.
    assert (np.delete(etiqueta[:14], 4) == -1).all()
    assert (etiqueta[(t > 6.5) & (np.abs(t - 15.0) > 4.0) & ~tapado] == 0).all()
    assert (etiqueta[(np.abs(t - 15.0) <= 4.0)] == -1).all()


def test_los_ratos_del_portero_se_parten_donde_deja_de_verse_mas_de_dos_segundos():
    assert ratos_portero([5.0, 0.0, 0.5, 9.0, 1.0, 5.5]).tolist() == [1, 0, 0, 2, 0, 1]
    assert ratos_portero([]).tolist() == []


def test_las_zonas_del_portero_entran_en_el_afinado_con_su_numero_o_sin_numero(tmp_path):
    guarda = GuardaZonas(tmp_path)
    zonas = np.zeros((3, ALTO, ANCHO, 3), dtype=np.uint8)
    rato = TRACKLET_PORTERO_BASE + 13_000
    guarda.guardar_porteros(zonas, np.array([rato, rato, rato + 1]), np.array([0, 0, 0]), np.array([13, 0, 0]))
    etiquetas = {str(r): {"dorsal": 13, "equipo": 0, "confianza": 1.0, "parte_tracklet": 1.0} for r in (rato, rato + 1)}
    (tmp_path / "etiquetas.json").write_text(json.dumps({"tracklets": etiquetas}), encoding="utf-8")
    imagenes, dorsales, grupos, _ = cargar([tmp_path])
    assert imagenes.shape == (3, ALTO, ANCHO, 3)
    assert dorsales.tolist() == [13, 0, 0]
    assert grupos.tolist() == [rato, rato, rato + 1]


def test_los_dorsales_que_casi_no_se_ven_se_repiten_y_los_que_mas_se_ven_se_recortan():
    dorsales = np.array([13] * 3 + [8] * 50 + [0] * 100)
    elegidas, repetidos = elegir_entreno(dorsales, list(range(len(dorsales))), random.Random(1), maximo_por_dorsal=30, minimo_por_dorsal=12)
    cuenta = {d: sum(1 for i in elegidas if dorsales[i] == d) for d in (13, 8, 0)}
    assert repetidos == {13: 4}
    assert cuenta[13] == 12 and cuenta[8] == 30
    # Las sin número, el 30 % del total.
    assert cuenta[0] == int(42 * 0.3 / 0.7)


def test_un_dorsal_con_una_sola_zona_no_se_repite_mas_de_la_cuenta():
    dorsales = np.array([7] + [8] * 400)
    elegidas, repetidos = elegir_entreno(dorsales, list(range(len(dorsales))), random.Random(1), maximo_por_dorsal=2500, minimo_por_dorsal=300)
    assert repetidos == {7: 50}
    assert sum(1 for i in elegidas if dorsales[i] == 7) == 50


def test_de_los_porteros_solo_cuentan_los_que_tienen_tamano_para_leer_el_dorsal_y_se_dice_si_alguien_los_tapa():
    portero = Persona(5, CLASE_PORTERO, 0, (60.0, 110.0), None, (40.0, 10.0, 80.0, 110.0), 0.9)
    lejos = Persona(5, CLASE_PORTERO, 0, (60.0, 50.0), None, (40.0, 10.0, 60.0, 50.0), 0.9)
    delante = Persona(7, CLASE_JUGADOR, 1, (65.0, 130.0), None, (45.0, 20.0, 85.0, 130.0), 0.9)
    instantes = [
        Instante(0, 0.0, [portero]),
        Instante(1, 0.2, [lejos]),
        Instante(2, 0.4, [portero, delante]),
    ]
    muestras = muestras_de_porteros(instantes, Porteros(equipo_de={5: 0}))
    assert [(m[0], m[1], m[2], m[4]) for m in muestras] == [(0.0, 5, 0, False), (0.4, 5, 0, True)]


class _LectorDePrueba:
    """Lee el 13 con seguridad en las zonas claras (la espalda) y un 8 flojo en las oscuras (el escudo del pecho)."""

    ruta = Path("modelos/afinados/dorsales.pt")
    device = "cpu"

    def leer(self, zonas):
        return [_lectura(13, 0.95) if float(np.mean(z)) > 128 else _lectura(8, 0.05) for z in zonas]


_CAJA_PORTERO = (40.0, 10.0, 80.0, 110.0)


def _video_del_portero(tmp_path: Path, fps: float = 2.0) -> Path:
    """40 fotogramas del portero (tracklet 5): de espaldas, con la zona del dorsal clara, solo en el 4 y el 5."""
    ruta = tmp_path / "partido.avi"
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"MJPG"), fps, (160, 120))
    if not escritor.isOpened():
        pytest.skip("OpenCV no puede escribir vídeo de prueba en este equipo")
    for k in range(40):
        imagen = np.full((120, 160, 3), 90, dtype=np.uint8)
        imagen[26:52, 48:72] = 220 if k in (4, 5) else 40
        escritor.write(imagen)
    escritor.release()
    return ruta


def test_las_zonas_de_los_porteros_se_sacan_del_video_etiquetadas_y_con_su_rato(tmp_path):
    fps = 2.0
    ruta = _video_del_portero(tmp_path, fps)
    muestras = [(k / fps, 5, 0, _CAJA_PORTERO, k == 20) for k in range(40)]
    ident = Identidad(0, 13, "CARCEDO", [5], 2.0, 0.03, portero=True)
    resolucion = Resolucion({5: ident.id}, [ident], {}, 1, {})
    guarda = GuardaZonas(tmp_path / "zonas")
    lineas = []
    config = Configuracion(video=ruta, salida=tmp_path, modelos=tmp_path / "modelos")

    etiquetas = guardar_zonas_porteros(muestras, resolucion, _LectorDePrueba(), config, info_video(ruta), guarda, lineas.append)

    rato = TRACKLET_PORTERO_BASE + 13_000
    assert etiquetas == {str(rato): {"dorsal": 13, "equipo": 0, "confianza": 1.0, "parte_tracklet": 1.0}}
    with np.load(tmp_path / "zonas" / "zonas-0000.npz") as z:
        leido, peso, tracklets = z["leido"].tolist(), z["peso"].tolist(), z["tracklet"].tolist()
    # Las dos espaldas con el 13; sin número, las que están a más de 4 s de ellas y sin nadie delante (de 7 s a 19,5 s
    # menos la tapada).
    assert leido == [13, 13] + [0] * 25 and peso == [1.0, 1.0] + [0.0] * 25
    assert set(tracklets) == {rato}
    assert lineas == ["Zonas de los porteros para afinar el lector: el 13 (equipo 0), 2 de espaldas y 25 sin número."]


def test_el_portero_que_acaba_en_la_linea_de_un_jugador_de_campo_no_deja_zonas(tmp_path):
    ruta = _video_del_portero(tmp_path)
    muestras = [(k / 2.0, 5, 0, _CAJA_PORTERO, False) for k in range(40)]
    defensa = Identidad(0, 4, "DEFENSA", [5], 3.0, 0.8)
    resolucion = Resolucion({5: defensa.id}, [defensa], {}, 1, {})
    guarda = GuardaZonas(tmp_path / "zonas")
    config = Configuracion(video=ruta, salida=tmp_path, modelos=tmp_path / "modelos")

    etiquetas = guardar_zonas_porteros(muestras, resolucion, _LectorDePrueba(), config, info_video(ruta), guarda, print)

    assert etiquetas == {}
    assert not list((tmp_path / "zonas").glob("zonas-*.npz"))


def test_fotogramas_elegidos_da_esos_y_solo_esos_aunque_se_empiece_mas_atras(tmp_path):
    ruta = tmp_path / "v.avi"
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"MJPG"), 10.0, (64, 48))
    if not escritor.isOpened():
        pytest.skip("OpenCV no puede escribir vídeo de prueba en este equipo")
    for i in range(30):
        escritor.write(np.full((48, 64, 3), 8 * i, dtype=np.uint8))
    escritor.release()
    leidos = list(fotogramas_elegidos(ruta, [17, 3, 25, 3, 40], desde=2))
    assert [i for i, _ in leidos] == [3, 17, 25]
    assert [round(float(imagen.mean()) / 8) for _, imagen in leidos] == [3, 17, 25]
