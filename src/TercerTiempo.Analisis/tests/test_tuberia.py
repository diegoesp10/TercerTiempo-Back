"""La pasada sin esperas (`tuberia.py`): lo mismo que haciendo cada cosa a su tiempo, en el orden de siempre."""

import threading
import time
from concurrent.futures import Future
from pathlib import Path

import numpy as np
import pytest

from tt_analisis.tuberia import Cronometro, TrabajadorGPU, adelantar, cerrar, en_segundo_plano, medir


def test_en_segundo_plano_da_lo_mismo_y_en_orden():
    assert list(en_segundo_plano(range(50), maximo=3)) == list(range(50))


def test_en_segundo_plano_lee_en_otro_hilo():
    hilos = []

    def fuente():
        for k in range(5):
            hilos.append(threading.current_thread().name)
            yield k

    assert list(en_segundo_plano(fuente())) == list(range(5))
    assert set(hilos) == {"leer-video"}


def test_en_segundo_plano_relanza_el_error_de_la_fuente():
    def fuente():
        yield 1
        raise ValueError("vídeo roto")

    leidos = en_segundo_plano(fuente())
    assert next(leidos) == 1
    with pytest.raises(ValueError, match="vídeo roto"):
        next(leidos)


def test_en_segundo_plano_cierra_la_fuente_si_se_deja_de_leer():
    cerrada = threading.Event()

    def fuente():
        try:
            for k in range(1000):
                yield k
        finally:
            cerrada.set()

    leidos = en_segundo_plano(fuente(), maximo=2)
    assert next(leidos) == 0
    cerrar(leidos)
    assert cerrada.wait(5)


def test_el_trabajador_va_por_prioridad_y_a_igual_prioridad_por_orden_de_llegada():
    trabajador = TrabajadorGPU()
    hecho = []
    puerta = threading.Event()
    # El primero ocupa el hilo hasta que se abre la puerta: los demás esperan en la cola.
    primero = trabajador.enviar(0, puerta.wait, 5)
    futuros = [trabajador.enviar(p, hecho.append, nombre) for p, nombre in ((3, "c"), (1, "a1"), (2, "b"), (1, "a2"))]
    puerta.set()
    for futuro in futuros:
        futuro.result(5)
    assert primero.result() is True
    assert hecho == ["a1", "a2", "b", "c"]
    trabajador.cerrar()


def test_el_error_del_trabajador_le_llega_a_quien_espera_y_el_hilo_sigue():
    trabajador = TrabajadorGPU()
    with pytest.raises(ZeroDivisionError):
        trabajador.enviar(0, lambda: 1 / 0).result(5)
    assert trabajador.enviar(1, lambda: 7).result(5) == 7
    trabajador.cerrar()


def test_el_trabajador_sincrono_lo_hace_en_el_momento_y_en_el_mismo_hilo():
    trabajador = TrabajadorGPU(sincrono=True)
    hilos = []
    futuro = trabajador.enviar(5, lambda: hilos.append(threading.current_thread()) or 3)
    assert futuro.done() and futuro.result() == 3 and hilos == [threading.current_thread()]
    trabajador.cerrar()


def test_cerrar_el_trabajador_acaba_lo_que_hace_y_cancela_lo_pendiente():
    trabajador = TrabajadorGPU()
    puerta = threading.Event()
    ocupado = trabajador.enviar(0, puerta.wait, 5)
    pendiente = trabajador.enviar(1, lambda: "no")
    threading.Timer(0.2, puerta.set).start()
    trabajador.cerrar()
    assert ocupado.result() is True
    assert pendiente.cancelled()


def _lanzador(lanzados):
    def lanzar(x, k):
        lanzados.append(k)
        futuro = Future()
        futuro.set_result(10 * x)
        return futuro

    return lanzar


def test_adelantar_lanza_los_siguientes_antes_de_que_toquen():
    lanzados = []
    salida = [(x, futuro.result(), list(lanzados)) for x, futuro in adelantar(range(1, 6), _lanzador(lanzados), adelanto=2)]
    assert [(x, r) for x, r, _ in salida] == [(k, 10 * k) for k in range(1, 6)]
    # Al llegar al primero ya están lanzados los dos siguientes; al final, ninguno de más.
    assert salida[0][2] == [1, 2, 3] and salida[-1][2] == [1, 2, 3, 4, 5]


def test_sin_adelanto_cada_uno_se_lanza_cuando_toca():
    lanzados = []
    salida = [list(lanzados) for _, _ in adelantar(range(1, 4), _lanzador(lanzados), adelanto=0)]
    assert salida == [[1], [1, 2], [1, 2, 3]]


def test_el_cronometro_separa_lo_del_hilo_principal_de_lo_de_la_gpu():
    crono = Cronometro()

    def lento():
        for k in range(3):
            time.sleep(0.01)
            yield k

    assert list(medir(lento(), crono, "leer_video")) == [0, 1, 2]
    assert crono.segundos["leer_video"] >= 0.025
    assert crono.medido("calibrar", lambda a, b: a + b, 2, b=3) == 5
    crono.sumar("gpu_detectores", 0.3)
    resumen = crono.resumen(3, 0.6, solapado=True)
    assert set(resumen["etapas_s"]) == {"leer_video", "calibrar"} and resumen["gpu_s"] == {"gpu_detectores": 0.3}
    assert resumen["ms_por_fotograma"] == 200.0 and resumen["gpu_ocupada_pct"] == 50.0
    assert resumen["otros_s"] == round(0.6 - sum(resumen["etapas_s"].values()), 1)
    assert crono.resumen(3, 0.6, solapado=False)["gpu_ocupada_pct"] is None


def test_las_teselas_del_fotograma_subido_una_vez_son_las_mismas():
    torch = pytest.importorskip("torch")
    from tt_analisis.detector_balon import DetectorBalon
    from tt_analisis.rfdetr_comun import subir
    from tt_analisis.teselas import rejilla

    imagen = np.random.default_rng(3).integers(0, 256, size=(1080, 1920, 3), dtype=np.uint8)
    detector = DetectorBalon(prediccion=None, device="cpu", ruta=Path("balon.pth"))
    origenes = rejilla(1920, 1080)
    aparte = detector._teselas(imagen, origenes)
    compartido = detector._teselas(imagen, origenes, subir(imagen, "cpu"))
    assert aparte.dtype == compartido.dtype == torch.float32 and aparte.shape == compartido.shape == (len(origenes), 3, 384, 384)
    assert aparte.is_contiguous() and compartido.is_contiguous()
    assert torch.equal(aparte, compartido)
    # Más pequeña que una tesela: con relleno.
    pequena = np.ascontiguousarray(imagen[:200, :300])
    assert torch.equal(detector._teselas(pequena, [(0, 0)]), detector._teselas(pequena, [(0, 0)], subir(pequena, "cpu")))
    # Y el fotograma subido es el que se subía a los detectores de jugadores y del campo.
    antes = torch.from_numpy(np.ascontiguousarray(imagen)).flip(-1).permute(2, 0, 1).unsqueeze(0).float().div_(255.0)
    ahora = subir(imagen, "cpu")
    assert torch.equal(antes, ahora) and antes.stride() == ahora.stride()
