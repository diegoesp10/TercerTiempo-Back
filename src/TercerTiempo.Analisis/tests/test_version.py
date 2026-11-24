import json
import tomllib
from pathlib import Path
from types import SimpleNamespace

from tt_analisis import NOMBRE, __version__
from tt_analisis.modelos import Modelos, describir_modelos, entrenamiento, huella

RAIZ = Path(__file__).resolve().parents[1]


def test_la_version_del_paquete_es_la_del_analizador():
    proyecto = tomllib.loads((RAIZ / "pyproject.toml").read_text(encoding="utf-8"))
    assert NOMBRE == "Ojeador"
    assert proyecto["project"]["version"] == __version__
    bloqueo = (RAIZ / "uv.lock").read_text(encoding="utf-8")
    assert f'name = "tt-analisis"\nversion = "{__version__}"' in bloqueo


def test_el_entrenamiento_es_la_carpeta_del_checkpoint_en_windows_y_en_linux():
    assert entrenamiento("entrenamientos\\rfdetr-balon-20261117-v2\\checkpoint_best_total.pth") == "rfdetr-balon-20261117-v2"
    assert entrenamiento("/datos/entrenamientos/rfdetr-campo-20261118/checkpoint_best_ema.pth") == "rfdetr-campo-20261118"
    assert entrenamiento(None) is None


def test_cada_modelo_dice_su_arquitectura_entrenamiento_motor_y_huella(tmp_path: Path):
    rfdetr = tmp_path / "rfdetr"
    rfdetr.mkdir()
    detectores = {}
    for que, extension, motor in (("jugadores", "trt", "tensorrt"), ("balon", "trt", "tensorrt"), ("campo", "pth", "pytorch")):
        pesos = rfdetr / f"{que}.{extension}"
        pesos.write_bytes(que.encode())
        detectores[que] = SimpleNamespace(ruta=pesos, motor=motor)
        datos = {"checkpoint": f"entrenamientos\\rfdetr-{que}-20261117\\checkpoint_best_total.pth"}
        if que == "campo":
            datos["variante"] = "puntos"
        (rfdetr / f"{que}.json").write_text(json.dumps(datos), encoding="utf-8")
    afinado = tmp_path / "afinados" / "dorsales.pt"
    afinado.parent.mkdir()
    afinado.write_bytes(b"parseq")

    modelos = Modelos(detectores["jugadores"], detectores["balon"], detectores["campo"], "cuda")
    descritos = describir_modelos(modelos, tmp_path, SimpleNamespace(ruta=afinado))

    assert [d["que"] for d in descritos] == ["jugadores", "balon", "campo", "dorsales"]
    assert descritos[0] == {"que": "jugadores", "modelo": "RF-DETR Medium", "entrenamiento": "rfdetr-jugadores-20261117", "motor": "TensorRT", "huella": huella(detectores["jugadores"].ruta)}
    assert descritos[2]["modelo"] == "RF-DETR Keypoint"
    assert descritos[2]["motor"] == "PyTorch"
    assert descritos[3] == {"que": "dorsales", "modelo": "PARSeq", "entrenamiento": "afinado con nuestros partidos", "motor": "PyTorch", "huella": huella(afinado)}
    assert len(descritos[3]["huella"]) == 8


def test_sin_lector_de_dorsales_ni_datos_del_entrenamiento_sigue_diciendo_lo_que_sabe(tmp_path: Path):
    pesos = tmp_path / "balon.pth"
    pesos.write_bytes(b"x")
    detector = SimpleNamespace(ruta=pesos, motor="pytorch")
    descritos = describir_modelos(Modelos(detector, detector, detector, "cpu"), tmp_path)
    assert len(descritos) == 3
    assert descritos[1]["entrenamiento"] is None
    assert descritos[1]["modelo"] == "RF-DETR Medium"
