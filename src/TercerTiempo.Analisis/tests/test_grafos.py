"""Los grafos CUDA de los detectores (`rfdetr_comun.grabar`, `Modelos.grabar`): el grafo solo para la forma grabada, el
resto sin él, y un detector que no se deja grabar sigue sin grafo sin parar el análisis."""

from types import SimpleNamespace

import numpy as np

from tt_analisis import rfdetr_comun
from tt_analisis.modelos import Modelos


def test_el_grafo_solo_se_usa_con_la_forma_grabada():
    grabado = rfdetr_comun._Grabado(lambda lote: "sin grafo", lambda lote: "grafo", (24, 3, 576, 576))
    assert grabado(np.zeros((24, 3, 576, 576))) == "grafo"
    assert grabado(np.zeros((7, 3, 576, 576))) == "sin grafo"
    assert grabado(np.zeros((24, 3, 384, 384))) == "sin grafo"


def _modelos() -> Modelos:
    detector = lambda: SimpleNamespace(prediccion=object())  # noqa: E731
    return Modelos(detector(), detector(), detector(), "cuda")


def test_graba_los_tres_con_las_teselas_del_video(monkeypatch):
    lotes = []
    monkeypatch.setattr(rfdetr_comun, "grabar", lambda prediccion, lote, device: lotes.append(lote))
    assert _modelos().grabar(1920, 1080) == (["jugadores", "balon", "campo"], None)
    assert lotes == [1, 24, 1]


def test_si_uno_no_se_deja_grabar_para_ahi_y_dice_por_que(monkeypatch):
    def grabar(prediccion, lote, device):
        if lote != 1:
            raise RuntimeError("captura rechazada")

    monkeypatch.setattr(rfdetr_comun, "grabar", grabar)
    grabados, fallo = _modelos().grabar(1920, 1080)
    assert grabados == ["jugadores"]
    assert fallo == "balon: captura rechazada"
