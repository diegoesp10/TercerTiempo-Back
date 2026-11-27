"""Si falla la grabación misma de un grafo CUDA, el análisis se repite solo en un proceso limpio y sin grafos
(`repetir`): el error sube con su detector, el proceso nuevo es el mismo análisis con `--sin-grafos` y el porqué, recibe
las órdenes de pausa que lleguen al que falló y termina con el código de salida que vale para todo."""

import queue
import subprocess
import sys
import threading
from types import ModuleType, SimpleNamespace

import pytest

from tt_analisis import analizador, repetir, rfdetr_comun
from tt_analisis.__main__ import main
from tt_analisis.avance import ORDEN_PAUSA, ORDEN_SIGUE, Avance
from tt_analisis.modelos import Modelos
from tt_analisis.repetir import VARIABLE, CapturaFallida, orden_sin_grafos, repetir_sin_grafos


def _modelos() -> Modelos:
    detector = lambda: SimpleNamespace(prediccion=object())  # noqa: E731
    return Modelos(detector(), detector(), detector(), "cuda")


def _entrada(ordenes: queue.Queue):
    """La entrada estándar de un proceso: da las líneas según llegan y se cierra con None."""
    while (linea := ordenes.get()) is not None:
        yield linea


def _esperar(condicion, segundos: float = 10.0) -> bool:
    for _ in range(int(segundos * 100)):
        if condicion():
            return True
        threading.Event().wait(0.01)
    return False


def test_si_falla_la_grabacion_misma_no_sigue_y_dice_de_que_detector(monkeypatch):
    def grabar(prediccion, lote, device):
        if lote != 1:
            raise CapturaFallida("RuntimeError: CUDA Graph capture failed")

    monkeypatch.setattr(rfdetr_comun, "grabar", grabar)
    with pytest.raises(CapturaFallida) as fallo:
        _modelos().grabar(1920, 1080)
    assert fallo.value.detector == "balon"
    assert fallo.value.motivo == "balon: RuntimeError: CUDA Graph capture failed"
    assert fallo.value.aviso.startswith("Aviso: falló la grabación del grafo CUDA del balón.")


def test_lo_que_falle_dentro_del_grafo_de_rfdetr_es_una_grabacion_fallida(monkeypatch):
    import torch

    class Rechaza:
        def __init__(self, modelo, muestra, device):
            try:
                raise RuntimeError("operation not permitted when stream is capturing")
            except RuntimeError as causa:
                raise RuntimeError("CUDA Graph capture failed. Restart the process.") from causa

    # El grafo de RF-DETR, sin cargar RF-DETR entero.
    detr = ModuleType("rfdetr.detr")
    detr._CUDAGraphInferenceModel = Rechaza
    paquete = ModuleType("rfdetr")
    paquete.detr = detr
    monkeypatch.setitem(sys.modules, "rfdetr", paquete)
    monkeypatch.setitem(sys.modules, "rfdetr.detr", detr)
    modelo = torch.nn.Identity()
    nativo = SimpleNamespace(model=SimpleNamespace(resolution=8, inference_model=modelo), _optimized_dtype=torch.float32)
    with pytest.raises(CapturaFallida) as fallo:
        rfdetr_comun.grabar(SimpleNamespace(_native_model=nativo), 2, "cpu")
    assert fallo.value.detalle == (
        "RuntimeError: CUDA Graph capture failed. Restart the process. ← RuntimeError: operation not permitted when stream is capturing"
    )
    # Sin grafo a medias: el detector se queda como estaba.
    assert nativo.model.inference_model is modelo


def test_el_proceso_nuevo_es_el_mismo_analisis_sin_grafos():
    assert orden_sin_grafos(["analizar", "j8.mp4", "--progreso"]) == [
        sys.executable, "-m", "tt_analisis", "analizar", "j8.mp4", "--progreso", "--sin-grafos",
    ]
    assert orden_sin_grafos(["analizar", "j8.mp4", "--sin-grafos"]).count("--sin-grafos") == 1


def test_repite_con_el_porque_y_termina_con_su_codigo(monkeypatch):
    lanzados = []

    class Hijo:
        pid, stdin = 4321, None

        def wait(self):
            return 7

    def lanzar(orden, **opciones):
        lanzados.append((orden, opciones))
        return Hijo()

    monkeypatch.setattr(repetir, "liberar_gpu", lambda: None)
    assert repetir_sin_grafos(["analizar", "j8.mp4"], "balon: captura rechazada", lanzar=lanzar) == 7
    orden, opciones = lanzados[0]
    assert orden[-3:] == ["analizar", "j8.mp4", "--sin-grafos"]
    assert opciones["env"][VARIABLE] == "balon: captura rechazada"
    # Nadie le mandaba órdenes a este proceso: el nuevo no tiene una entrada aparte.
    assert opciones["stdin"] is None


HIJO = """
import os, sys
for linea in sys.stdin:
    print("orden", linea.strip(), flush=True)
print("porque", os.environ["TT_ANALISIS_SIN_GRAFOS_POR"], flush=True)
sys.exit(5)
"""


def test_el_proceso_nuevo_recibe_las_ordenes_del_que_fallo(monkeypatch, capfd):
    monkeypatch.setattr(repetir, "liberar_gpu", lambda: None)
    ordenes: queue.Queue = queue.Queue()
    avance = Avance(True, lambda linea: None, entrada=_entrada(ordenes))
    # La pausa llega antes de que falle la grabación: el proceso nuevo tiene que empezar en pausa.
    ordenes.put(ORDEN_PAUSA + "\n")
    assert _esperar(lambda: avance.pausado)

    reenviado = threading.Event()
    reenviar = avance.reenviar

    def espiar(destino):
        reenviar(destino)
        reenviado.set()

    avance.reenviar = espiar
    resultado = {}

    def puente():
        lanzar = lambda orden, **opciones: subprocess.Popen([sys.executable, "-c", HIJO], **opciones)  # noqa: E731
        resultado["codigo"] = repetir_sin_grafos(["analizar", "j8.mp4"], "campo: rechazada", avance, lanzar=lanzar)

    hilo = threading.Thread(target=puente)
    hilo.start()
    assert reenviado.wait(30)
    ordenes.put(ORDEN_SIGUE + "\n")
    # Se cierra la entrada del que falló: el nuevo ve cerrarse la suya y acaba.
    ordenes.put(None)
    hilo.join(30)
    assert resultado["codigo"] == 5
    salida = capfd.readouterr()
    assert salida.out.splitlines() == ["orden ##pausa", "orden ##sigue", "porque campo: rechazada"]
    assert "Se repite sin grafos en el proceso" in salida.err


def test_si_el_proceso_nuevo_ya_acabo_las_ordenes_se_pierden_sin_romper_nada():
    class Cerrada:
        cierre_pedido = False

        def write(self, texto):
            raise OSError("tubería rota")

        def flush(self):
            pass

        def close(self):
            self.cierre_pedido = True
            raise OSError("tubería rota")

    ordenes: queue.Queue = queue.Queue()
    avance = Avance(True, lambda linea: None, entrada=_entrada(ordenes))
    destino = Cerrada()
    avance.reenviar(destino)
    ordenes.put(ORDEN_PAUSA + "\n")
    ordenes.put(None)
    # Sin poder pasar la pausa, el hilo sigue: llega al final de la entrada e intenta cerrar la del otro proceso.
    assert _esperar(lambda: destino.cierre_pedido)
    # La pausa iba para el otro proceso: este no se para.
    assert not avance.pausado


def test_la_linea_de_ordenes_repite_el_analisis_si_falla_la_grabacion(monkeypatch, tmp_path):
    def analizar(config):
        error = CapturaFallida("RuntimeError: CUDA Graph capture failed", detector="jugadores")
        error.ordenes = "quien lee las órdenes"
        raise error

    llamadas = []
    monkeypatch.setattr(analizador, "analizar", analizar)
    monkeypatch.setattr(repetir, "repetir_sin_grafos", lambda argumentos, motivo, ordenes: llamadas.append((argumentos, motivo, ordenes)) or 9)
    argv = ["analizar", str(tmp_path / "j8.mp4"), "--salida", str(tmp_path / "salida")]
    assert main(argv) == 9
    assert llamadas == [(argv, "jugadores: RuntimeError: CUDA Graph capture failed", "quien lee las órdenes")]


def test_para_el_visor_sigue_en_marcha_mientras_se_repite(monkeypatch, tmp_path):
    vistos = []

    class Directo:
        def __init__(self, salida, activo):
            pass

        def fallo(self, error):
            vistos.append("fallo")

        def detener(self):
            vistos.append("detener")

    def _analizar(config, informar, directo):
        raise CapturaFallida("rechazada", detector="campo")

    monkeypatch.setattr(analizador, "Directo", Directo)
    monkeypatch.setattr(analizador, "_analizar", _analizar)
    with pytest.raises(CapturaFallida):
        analizador.analizar(SimpleNamespace(salida=tmp_path, repeticion=True))
    assert vistos == ["detener"]
