"""Avance del análisis para quien lo lanza como proceso (el servicio de vídeo de Tercer Tiempo). Con `--progreso`, en la
salida estándar:

- «##progreso N» (0-100) cada vez que el porcentaje sube;
- «##estado {json}» con lo que se está haciendo (etapa, minuto del vídeo, jugadores seguidos, balón visto, velocidad,
  cuánto queda, si está en pausa…), como mucho cada pocos segundos;
- «##hito texto» con cada cosa que se va sabiendo («Equipos separados…», «Cámara fija…»).

Y lee órdenes por la entrada estándar: «##pausa» para el análisis en el siguiente punto de control (entre fotogramas)
sin perder nada de lo hecho, y «##sigue» lo reanuda. En pausa sigue escribiendo su estado cada pocos segundos para que
quien lo vigila sepa que no se ha colgado. Si el análisis se repite en otro proceso porque falló la grabación de un grafo
CUDA (`repetir`), las órdenes que lleguen se le pasan a él."""

from __future__ import annotations

import json
import threading
import time
from typing import Callable, Iterable

PREFIJO = "##progreso "
PREFIJO_ESTADO = "##estado "
PREFIJO_HITO = "##hito "
ORDEN_PAUSA = "##pausa"
ORDEN_SIGUE = "##sigue"

# Reparto del 0 al 100 entre las fases: separar los equipos, la pasada principal (casi todo el tiempo) y el
# post-proceso con la escritura de las salidas.
FASES: dict[str, tuple[int, int]] = {"equipos": (0, 8), "pasada": (8, 95), "salidas": (95, 99)}

# Las etapas que ve quien sigue el análisis, en orden.
ETAPAS = ("modelos", "equipos", "pasada", "jugadas", "revision", "salidas")

MAX_HITO = 300


class Avance:
    def __init__(
        self,
        activo: bool,
        escribir: Callable[[str], None] | None = None,
        entrada: Iterable[str] | None = None,
        reloj: Callable[[], float] = time.monotonic,
        cada_s: float = 2.0,
        latido_pausa_s: float = 5.0,
        dormir: Callable[[float], None] = time.sleep,
    ):
        self.activo = activo
        self._escribir = escribir or (lambda linea: print(linea, flush=True))
        self._reloj = reloj
        self._cada_s = cada_s
        self._latido_pausa_s = latido_pausa_s
        self._dormir = dormir
        self._ultimo = -1
        self._ultimo_estado = float("-inf")
        self._datos: dict = {}
        self.etapa_actual: str | None = None
        self._pausa = threading.Event()
        self.pausa_total_s = 0.0
        # Si el análisis se repite en otro proceso (`repetir`), las órdenes van a su entrada: un solo hilo lee la de este.
        self._reenvio = None
        self._cerrojo_ordenes = threading.Lock()
        self.escucha = activo and entrada is not None
        if self.escucha:
            threading.Thread(target=self._escuchar, args=(entrada,), daemon=True, name="ordenes-del-analisis").start()

    # --- porcentaje -------------------------------------------------------------------------------------------------

    def fase(self, nombre: str, hecho: float, total: float) -> None:
        inicio, fin = FASES[nombre]
        fraccion = 0.0 if total <= 0 else min(1.0, max(0.0, hecho / total))
        self._emitir(int(inicio + (fin - inicio) * fraccion))

    def fin(self) -> None:
        self._emitir(100)

    @property
    def porcentaje(self) -> int:
        return max(0, self._ultimo)

    def _emitir(self, porcentaje: int) -> None:
        if not self.activo or porcentaje <= self._ultimo:
            return
        self._ultimo = porcentaje
        self._escribir(f"{PREFIJO}{porcentaje}")

    # --- estado detallado e hitos ---------------------------------------------------------------------------------

    def etapa(self, nombre: str, **datos) -> None:
        """Empieza una etapa: se olvidan los datos de la anterior que no sirven y se avisa enseguida."""
        self.etapa_actual = nombre
        conservar = {k: v for k, v in self._datos.items() if k in ("colores", "tramo_inicio_s", "tramo_fin_s", "fotogramas_total")}
        self._datos = conservar
        self.estado(forzar=True, **datos)

    def toca(self) -> bool:
        """Ha pasado el rato mínimo desde el último estado: merece la pena calcular uno nuevo."""
        return self.activo and self._reloj() - self._ultimo_estado >= self._cada_s

    def estado(self, forzar: bool = False, **datos) -> None:
        if not self.activo:
            return
        self._datos.update({k: v for k, v in datos.items() if v is not None})
        if not forzar and not self.toca():
            return
        self._ultimo_estado = self._reloj()
        cuerpo = dict(self._datos, etapa=self.etapa_actual, porcentaje=self.porcentaje, pausado=self.pausado, pausa_s=round(self.pausa_total_s))
        self._escribir(PREFIJO_ESTADO + json.dumps(cuerpo, ensure_ascii=False, separators=(",", ":")))

    def hito(self, texto: str) -> None:
        if not self.activo:
            return
        limpio = " ".join(str(texto).split())
        if limpio:
            self._escribir(PREFIJO_HITO + limpio[:MAX_HITO])

    # --- pausa ------------------------------------------------------------------------------------------------------

    @property
    def pausado(self) -> bool:
        return self._pausa.is_set()

    def pausar(self) -> None:
        self._pausa.set()

    def seguir(self) -> None:
        self._pausa.clear()

    def punto_de_control(self) -> float:
        """Si se ha pedido pausa, espera aquí hasta que se reanude (dando señales de vida). Devuelve los segundos que
        ha estado parado, para no contarlos en la velocidad."""
        if not self.pausado:
            return 0.0
        inicio = contado = self._reloj()
        self.hito("En pausa: el análisis espera, sin perder nada de lo hecho.")
        self.estado(forzar=True)
        while self.pausado:
            self._dormir(0.25)
            ahora = self._reloj()
            if ahora - contado >= self._latido_pausa_s:
                self.pausa_total_s += ahora - contado
                contado = ahora
                self.estado(forzar=True)
        fin = self._reloj()
        self.pausa_total_s += fin - contado
        self.hito("Reanudado.")
        self.estado(forzar=True)
        return fin - inicio

    def reenviar(self, destino) -> None:
        """Desde ya, las órdenes que lleguen van a `destino` (la entrada del proceso que repite el análisis, ver
        `repetir`), empezando por la pausa si estaba en pausa. Si se cierra la entrada de este, se cierra `destino`."""
        with self._cerrojo_ordenes:
            self._reenvio = destino
            if self.pausado:
                self._pasar(ORDEN_PAUSA)

    def _pasar(self, orden: str) -> None:
        try:
            self._reenvio.write(orden + "\n")
            self._reenvio.flush()
        except (OSError, ValueError):
            pass  # El otro proceso ya terminó.

    def _escuchar(self, entrada: Iterable[str]) -> None:
        try:
            for linea in entrada:
                orden = linea.strip()
                with self._cerrojo_ordenes:
                    if self._reenvio is not None:
                        if orden:
                            self._pasar(orden)
                    elif orden == ORDEN_PAUSA:
                        self.pausar()
                    elif orden == ORDEN_SIGUE:
                        self.seguir()
        except (OSError, ValueError):
            pass
        # Sin quien mande (se cerró la entrada), nunca se queda en pausa para siempre; el proceso que repite, tampoco.
        with self._cerrojo_ordenes:
            if self._reenvio is not None:
                try:
                    self._reenvio.close()
                except (OSError, ValueError):
                    pass
        self.seguir()
