"""Avance del análisis para quien lo lanza como proceso (el servicio de vídeo de Tercer Tiempo): con `--progreso`,
una línea «##progreso N» (0-100) en la salida estándar cada vez que el porcentaje sube."""

from __future__ import annotations

from typing import Callable

PREFIJO = "##progreso "

# Reparto del 0 al 100 entre las fases: separar los equipos, la pasada principal (casi todo el tiempo) y el
# post-proceso con la escritura de las salidas.
FASES: dict[str, tuple[int, int]] = {"equipos": (0, 8), "pasada": (8, 95), "salidas": (95, 99)}


class Avance:
    def __init__(self, activo: bool, escribir: Callable[[str], None] | None = None):
        self.activo = activo
        self._escribir = escribir or (lambda linea: print(linea, flush=True))
        self._ultimo = -1

    def fase(self, nombre: str, hecho: float, total: float) -> None:
        inicio, fin = FASES[nombre]
        fraccion = 0.0 if total <= 0 else min(1.0, max(0.0, hecho / total))
        self._emitir(int(inicio + (fin - inicio) * fraccion))

    def fin(self) -> None:
        self._emitir(100)

    def _emitir(self, porcentaje: int) -> None:
        if not self.activo or porcentaje <= self._ultimo:
            return
        self._ultimo = porcentaje
        self._escribir(f"{PREFIJO}{porcentaje}")
