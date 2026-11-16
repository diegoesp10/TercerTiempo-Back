import json
import math

import cv2
import numpy as np
import pytest

from tt_analisis.config import Configuracion
from tt_analisis.informes import escribir_json, sanear_json
from tt_analisis.video import Lectura, fotogramas, info_video, pct_leido


def test_pct_leido_compara_con_lo_esperado():
    assert pct_leido(950, 1000) == 95.0
    assert pct_leido(400, 1000) == 40.0
    # El recuento de OpenCV es aproximado: leer alguno de más no pasa del 100 %.
    assert pct_leido(1003, 1000) == 100.0
    assert pct_leido(0, 0) == 100.0


def _video(ruta, n: int, fps: float = 10.0):
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"MJPG"), fps, (64, 48))
    if not escritor.isOpened():
        pytest.skip("OpenCV no puede escribir vídeo de prueba en este equipo")
    for i in range(n):
        escritor.write(np.full((48, 64, 3), i % 255, dtype=np.uint8))
    escritor.release()


def test_un_archivo_mas_corto_de_lo_que_dice_se_apunta_donde_se_corto(tmp_path):
    ruta = tmp_path / "corto.avi"
    _video(ruta, 20)
    info = info_video(ruta)
    # El contenedor dice 40 fotogramas, pero solo hay 20: así se ve un archivo cortado.
    engañosa = type(info)(info.ruta, info.ancho, info.alto, info.fps, 40)
    lectura = Lectura()

    leidos = list(fotogramas(Configuracion(video=ruta, salida=tmp_path), engañosa, paso=1, inicio_s=0, fin_s=None, lectura=lectura))

    assert len(leidos) == lectura.leidos == 20
    assert lectura.cortado_en_s == 2.0
    assert pct_leido(lectura.leidos, 40) == 50.0


def test_un_archivo_entero_no_se_da_por_cortado(tmp_path):
    ruta = tmp_path / "entero.avi"
    _video(ruta, 20)
    info = info_video(ruta)
    lectura = Lectura()

    list(fotogramas(Configuracion(video=ruta, salida=tmp_path), info, paso=1, inicio_s=0, fin_s=None, lectura=lectura))

    assert lectura.leidos == info.fotogramas
    assert lectura.cortado_en_s is None


def test_nan_e_infinitos_se_escriben_como_null(tmp_path):
    datos = {"a": float("nan"), "b": [1.5, float("inf"), np.float32("nan")], "c": {"d": np.float64(-np.inf), "e": np.int64(3)}, "f": np.array([0.5, np.nan]), "g": True}

    ruta = tmp_path / "salida.json"
    escribir_json(datos, ruta)

    texto = ruta.read_text(encoding="utf-8")
    assert "NaN" not in texto and "Infinity" not in texto
    leido = json.loads(texto)
    assert leido == {"a": None, "b": [1.5, None, None], "c": {"d": None, "e": 3}, "f": [0.5, None], "g": True}


def test_sanear_no_toca_lo_que_ya_es_json():
    datos = {"x": 1, "y": "texto", "z": [0.25, None, False]}
    assert sanear_json(datos) == datos
    assert math.isfinite(sanear_json(np.float32(2.5)))
