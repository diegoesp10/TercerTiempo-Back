import sys
import types
from pathlib import Path

import numpy as np
import pytest
import supervision as sv

from tt_analisis import rfdetr_comun
from tt_analisis.rfdetr_comun import cargar, da_siempre_lo_mismo, salidas_en_bruto


class _Predictor:
    """Devuelve siempre la misma detección; con `ruido`, la puntuación cambia un poco a partir de la segunda llamada."""

    def __init__(self, ruido: float = 0.0, puntos: bool = False) -> None:
        self.ruido = ruido
        self.puntos = puntos
        self.llamadas = 0

    def predict(self, entrada, threshold: float, include_source_image: bool):
        self.llamadas += 1
        extra = self.ruido * (self.llamadas - 1)
        if self.puntos:
            return sv.KeyPoints(
                xy=np.array([[[10.0, 20.0], [30.0, 40.0]]], dtype=np.float32),
                keypoint_confidence=np.array([[0.9, 0.2 + extra]], dtype=np.float32),
                class_id=np.array([0]),
                data={"precision": np.array([[np.nan, 1.0]], dtype=np.float32)},
            )
        return sv.Detections(
            xyxy=np.array([[1.0, 2.0, 3.0, 4.0]], dtype=np.float32),
            confidence=np.array([0.8 + extra], dtype=np.float32),
            class_id=np.array([1]),
            data={"class_name": np.array(["ball"])},
        )


def test_la_sonda_acepta_lo_que_da_siempre_lo_mismo_bit_a_bit_aunque_lleve_nan():
    assert da_siempre_lo_mismo(_Predictor(), entrada=None)
    assert da_siempre_lo_mismo(_Predictor(puntos=True), entrada=None)


def test_la_sonda_rechaza_un_cambio_minimo_en_las_cajas_o_en_los_puntos():
    assert not da_siempre_lo_mismo(_Predictor(ruido=1e-6), entrada=None)
    assert not da_siempre_lo_mismo(_Predictor(ruido=1e-6, puntos=True), entrada=None)


def test_la_sonda_mira_todo_lo_que_devuelve_una_prediccion_tambien_en_lote():
    detecciones = _Predictor().predict(None, 0.5, False)
    assert len(salidas_en_bruto(detecciones)) == 4
    assert len(salidas_en_bruto([detecciones, detecciones])) == 8
    # De los puntos clave no se lee `confidence`, que está en desuso y avisa.
    assert len(salidas_en_bruto(_Predictor(puntos=True).predict(None, 0.5, False))) == 4


@pytest.fixture
def modelos(tmp_path: Path, monkeypatch) -> Path:
    (tmp_path / "rfdetr").mkdir()
    (tmp_path / "rfdetr" / "campo.trt").write_bytes(b"motor")
    (tmp_path / "rfdetr" / "campo.pth").write_bytes(b"pesos")
    # Sin GPU en las pruebas: la imagen de la sonda no hace falta, los predictores de prueba no la miran.
    monkeypatch.setattr(rfdetr_comun, "entrada_sonda", lambda device: None)
    return tmp_path


def _rfdetr_falso(monkeypatch, motor: _Predictor, pesos: _Predictor) -> None:
    falso = types.ModuleType("rfdetr")
    falso.RFDETRInference = lambda ruta, device: motor
    modelo = types.SimpleNamespace(inference=lambda compile, dtype: pesos)
    falso.RFDETR = types.SimpleNamespace(from_checkpoint=lambda ruta, device=None: modelo)
    monkeypatch.setitem(sys.modules, "rfdetr", falso)


def test_un_motor_tensorrt_que_no_da_siempre_lo_mismo_no_se_usa(modelos: Path, monkeypatch, capsys):
    pesos = _Predictor()
    _rfdetr_falso(monkeypatch, motor=_Predictor(ruido=1e-6, puntos=True), pesos=pesos)

    prediccion, ruta, motor = cargar("campo", modelos, "cuda")

    assert (prediccion, ruta.name, motor) == (pesos, "campo.pth", "pytorch")
    assert "no da siempre lo mismo" in capsys.readouterr().out


def test_un_motor_tensorrt_que_da_siempre_lo_mismo_se_usa(modelos: Path, monkeypatch):
    trt = _Predictor(puntos=True)
    _rfdetr_falso(monkeypatch, motor=trt, pesos=_Predictor())

    prediccion, ruta, motor = cargar("campo", modelos, "cuda")

    assert (prediccion, ruta.name, motor) == (trt, "campo.trt", "tensorrt")
    assert trt.llamadas == 1 + rfdetr_comun.REPETICIONES_SONDA
