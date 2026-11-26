import sys
import types
from pathlib import Path

import pytest
import torch

from tt_analisis.detector_balon import DetectorBalon
from tt_analisis.rfdetr_comun import cargar


@pytest.fixture
def cargados(monkeypatch) -> list[tuple]:
    """Un `rfdetr` de prueba que apunta qué pesos se cargan y cómo, sin GPU ni pesos de verdad."""
    llamadas: list[tuple] = []

    def desde_checkpoint(ruta, device=None):
        def inferencia(compile, dtype):
            llamadas.append((Path(ruta).name, device, compile, dtype))
            return types.SimpleNamespace(class_names=["ball"])

        return types.SimpleNamespace(inference=inferencia)

    falso = types.ModuleType("rfdetr")
    falso.RFDETR = types.SimpleNamespace(from_checkpoint=desde_checkpoint)
    monkeypatch.setitem(sys.modules, "rfdetr", falso)
    return llamadas


def _modelos(tmp_path: Path, *archivos: str) -> Path:
    (tmp_path / "rfdetr").mkdir()
    for nombre in archivos:
        (tmp_path / "rfdetr" / nombre).write_bytes(b"x")
    return tmp_path


def test_carga_los_pesos_con_pytorch_en_fp16_en_la_gpu_y_en_fp32_en_la_cpu(tmp_path: Path, cargados):
    modelos = _modelos(tmp_path, "campo.pth")

    _, ruta = cargar("campo", modelos, "cuda")
    cargar("campo", modelos, "cpu")

    assert ruta == modelos / "rfdetr" / "campo.pth"
    assert cargados == [("campo.pth", "cuda", False, torch.float16), ("campo.pth", "cpu", False, torch.float32)]


def test_un_motor_tensorrt_que_quede_en_la_carpeta_no_se_usa(tmp_path: Path, cargados):
    # Los `.trt` de antes de quitar TensorRT (26-11-2026): solo cuentan los pesos.
    modelos = _modelos(tmp_path, "balon.trt", "balon.pth")

    detector = DetectorBalon.cargar(modelos, "cuda")

    assert (detector.ruta.name, detector.motor) == ("balon.pth", "pytorch")
    assert [llamada[0] for llamada in cargados] == ["balon.pth"]


def test_sin_pesos_dice_que_falta_el_detector_aunque_haya_un_motor_tensorrt(tmp_path: Path, cargados):
    modelos = _modelos(tmp_path, "jugadores.trt")

    with pytest.raises(FileNotFoundError, match="jugadores.pth"):
        cargar("jugadores", modelos, "cuda")
    assert cargados == []
