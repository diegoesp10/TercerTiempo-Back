"""La pasada principal sin esperas: mientras la GPU trabaja, la CPU sigue con lo suyo, y al revés.

- `en_segundo_plano`: lee el vídeo en otro hilo (OpenCV suelta el GIL al decodificar) y deja preparados los fotogramas
  siguientes.
- `TrabajadorGPU`: un solo hilo hace los encargos de la GPU de la pasada por prioridad (el número del fotograma): lo
  del fotograma actual va antes que lo del siguiente, que se adelanta mientras la CPU sigue con el seguidor, los colores
  y los recortes.
- `Cronometro`: cuánto se va en cada etapa, para `analisis.json`.

Cada cálculo es el mismo que antes, con los mismos datos y en el mismo orden para lo que depende de otro: solo se
solapa lo que no depende de nada. El resultado no cambia (lo comprueba `tests/test_tuberia.py` y, con el vídeo, dos
pasadas iguales)."""

from __future__ import annotations

import itertools
import queue
import threading
import time
from collections import defaultdict, deque
from concurrent.futures import Future
from contextlib import contextmanager
from dataclasses import dataclass
from typing import Callable, Iterable, Iterator, TypeVar

T = TypeVar("T")
# Las etapas que se hacen en el hilo de la GPU empiezan así (`Cronometro.resumen` las cuenta aparte).
PREFIJO_GPU = "gpu_"


@dataclass(frozen=True)
class _Fin:
    error: BaseException | None = None


def en_segundo_plano(iterable: Iterable[T], maximo: int = 2) -> Iterator[T]:
    """Los elementos de `iterable`, generados en otro hilo y con hasta `maximo` preparados de antemano. Un error del
    hilo sale aquí. Si se deja de leer antes del final, el hilo cierra el iterable (y con él su `finally`) y termina."""
    cola: queue.Queue = queue.Queue(maxsize=max(1, maximo))
    parar = threading.Event()

    def meter(elemento) -> bool:
        while not parar.is_set():
            try:
                cola.put(elemento, timeout=0.1)
                return True
            except queue.Full:
                continue
        return False

    def producir() -> None:
        iterador = iter(iterable)
        fin = _Fin()
        try:
            for elemento in iterador:
                if not meter(elemento):
                    break
        except BaseException as error:  # noqa: BLE001 - se relanza en el hilo que lee
            fin = _Fin(error)
        finally:
            cerrar = getattr(iterador, "close", None)
            if cerrar is not None:
                try:
                    cerrar()
                except Exception:  # noqa: BLE001
                    pass
        meter(fin)

    hilo = threading.Thread(target=producir, name="leer-video", daemon=True)
    hilo.start()
    try:
        while True:
            elemento = cola.get()
            if isinstance(elemento, _Fin):
                if elemento.error is not None:
                    raise elemento.error
                return
            yield elemento
    finally:
        parar.set()
        hilo.join(timeout=10.0)


def adelantar(iterable: Iterable[T], lanzar: Callable[[T, int], Future], adelanto: int = 1) -> Iterator[tuple[T, Future]]:
    """Cada elemento con el futuro de `lanzar(elemento, k)` (k = 1, 2...), lanzado `adelanto` elementos antes de que le
    toque: mientras se procesa uno, lo de los siguientes ya está en marcha."""
    pendientes: deque[tuple[T, Future]] = deque()
    for k, elemento in enumerate(iterable, start=1):
        pendientes.append((elemento, lanzar(elemento, k)))
        if len(pendientes) > max(0, adelanto):
            yield pendientes.popleft()
    while pendientes:
        yield pendientes.popleft()


def medir(iterable: Iterable[T], cronometro: "Cronometro", etapa: str) -> Iterator[T]:
    """Los elementos de `iterable`, sumando a `etapa` lo que se espera por cada uno."""
    iterador = iter(iterable)
    while True:
        t0 = time.perf_counter()
        try:
            elemento = next(iterador)
        except StopIteration:
            return
        finally:
            cronometro.sumar(etapa, time.perf_counter() - t0)
        yield elemento


def cerrar(*iterables) -> None:
    """Cierra los generadores (con ellos, el hilo que lee el vídeo) aunque no se hayan leído hasta el final."""
    for iterable in iterables:
        cierre = getattr(iterable, "close", None)
        if cierre is not None:
            cierre()


def _hacer(futuro: Future, funcion: Callable, args: tuple, kwargs: dict) -> None:
    try:
        futuro.set_result(funcion(*args, **kwargs))
    except BaseException as error:  # noqa: BLE001 - lo recoge quien espera el resultado
        futuro.set_exception(error)


class TrabajadorGPU:
    """Un hilo que hace los encargos de uno en uno: primero el de menor prioridad (el número del fotograma) y, a igual
    prioridad, el que llegó antes. Con `sincrono`, cada encargo se hace en el momento y en el hilo que lo manda: en la
    CPU, o para medir lo que gana solapar."""

    def __init__(self, sincrono: bool = False) -> None:
        self.sincrono = sincrono
        self._orden = itertools.count()
        self._cola: queue.PriorityQueue = queue.PriorityQueue()
        self._hilo: threading.Thread | None = None
        if not sincrono:
            self._hilo = threading.Thread(target=self._trabajar, name="gpu", daemon=True)
            self._hilo.start()

    def enviar(self, prioridad: float, funcion: Callable[..., T], *args, **kwargs) -> Future:
        futuro: Future = Future()
        if self.sincrono:
            futuro.set_running_or_notify_cancel()
            _hacer(futuro, funcion, args, kwargs)
            return futuro
        self._cola.put((prioridad, next(self._orden), futuro, funcion, args, kwargs))
        return futuro

    def _trabajar(self) -> None:
        while True:
            _, _, futuro, funcion, args, kwargs = self._cola.get()
            if funcion is None:
                return
            if futuro.set_running_or_notify_cancel():
                _hacer(futuro, funcion, args, kwargs)

    def cerrar(self) -> None:
        """Termina lo que esté haciendo y cancela lo que quede en la cola (si la pasada se cortó)."""
        if self._hilo is None:
            return
        self._cola.put((float("-inf"), -1, None, None, (), {}))
        self._hilo.join(timeout=60.0)
        self._hilo = None
        while True:
            try:
                _, _, futuro, _, _, _ = self._cola.get_nowait()
            except queue.Empty:
                break
            if futuro is not None:
                futuro.cancel()


class Cronometro:
    """Segundos acumulados por etapa. Lo del hilo de la GPU va en etapas aparte (`gpu_…`): se solapa con lo demás, así
    que las etapas no suman el tiempo de la pasada."""

    def __init__(self) -> None:
        self.segundos: dict[str, float] = defaultdict(float)
        self._cerrojo = threading.Lock()

    @contextmanager
    def etapa(self, nombre: str):
        t0 = time.perf_counter()
        try:
            yield
        finally:
            self.sumar(nombre, time.perf_counter() - t0)

    def sumar(self, nombre: str, segundos: float) -> None:
        with self._cerrojo:
            self.segundos[nombre] += segundos

    def medido(self, nombre: str, funcion: Callable[..., T], *args, **kwargs) -> T:
        with self.etapa(nombre):
            return funcion(*args, **kwargs)

    def a_dict(self, decimales: int = 1) -> dict[str, float]:
        with self._cerrojo:
            return {k: round(v, decimales) for k, v in sorted(self.segundos.items(), key=lambda kv: -kv[1])}

    def resumen(self, fotogramas: int, segundos: float, solapado: bool) -> dict:
        """Para `analisis.json`: ms por fotograma, segundos de cada etapa del hilo principal (no se solapan entre sí; `otros`
        es lo que no entra en ninguna) y los del hilo de la GPU, con lo ocupada que ha estado si se solapaba."""
        etapas = self.a_dict()
        principal = {k: v for k, v in etapas.items() if not k.startswith(PREFIJO_GPU)}
        gpu = {k: v for k, v in etapas.items() if k.startswith(PREFIJO_GPU)}
        return {
            "solapado": solapado,
            "fotogramas": fotogramas,
            "segundos": round(segundos, 1),
            "ms_por_fotograma": round(1000 * segundos / max(1, fotogramas), 1),
            "etapas_s": principal,
            "otros_s": round(max(0.0, segundos - sum(principal.values())), 1),
            "gpu_s": gpu,
            "gpu_ocupada_pct": round(100 * sum(gpu.values()) / max(segundos, 1e-6), 1) if solapado else None,
        }
