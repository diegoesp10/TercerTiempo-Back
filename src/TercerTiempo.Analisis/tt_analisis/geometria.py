"""Geometría del campo en centímetros, con el origen en la esquina superior izquierda de la configuración de
`sports` (eje x a lo largo del campo, 0..12000; eje y a lo ancho, 0..7000). Todo lo de aquí es puro y se prueba sin GPU."""

from __future__ import annotations

import math
from dataclasses import dataclass, field

from .tipos import Punto

LARGO_CM = 12000.0
ANCHO_CM = 7000.0
ANCHO_PORTERIA_CM = 732.0
AREA_GRANDE_LARGO_CM = 2015.0
AREA_GRANDE_ANCHO_CM = 4100.0
MARGEN_FUERA_CM = 80.0
# Medidas del campo en uso (las cambia `campo.configurar_campo`: no todos los campos miden 105 × 68).
DIMENSIONES = {"largo": LARGO_CM, "ancho": ANCHO_CM}


@dataclass(frozen=True)
class Campo:
    largo: float = field(default_factory=lambda: DIMENSIONES["largo"])
    ancho: float = field(default_factory=lambda: DIMENSIONES["ancho"])

    @property
    def centro(self) -> Punto:
        return (self.largo / 2, self.ancho / 2)

    def porteria(self, lado: int) -> Punto:
        """Centro de la portería: lado -1 = izquierda (x=0), lado +1 = derecha (x=largo)."""
        return (0.0 if lado < 0 else self.largo, self.ancho / 2)

    def dentro(self, xy: Punto, margen: float = MARGEN_FUERA_CM) -> bool:
        x, y = xy
        return -margen <= x <= self.largo + margen and -margen <= y <= self.ancho + margen

    def fuera_por(self, xy: Punto, margen: float = MARGEN_FUERA_CM) -> str | None:
        """'fondo_izq', 'fondo_der', 'banda' o None si está dentro."""
        x, y = xy
        if x < -margen:
            return "fondo_izq"
        if x > self.largo + margen:
            return "fondo_der"
        if y < -margen or y > self.ancho + margen:
            return "banda"
        return None

    def en_boca_porteria(self, y: float, margen: float = 60.0) -> bool:
        return abs(y - self.ancho / 2) <= ANCHO_PORTERIA_CM / 2 + margen

    def tercio(self, x: float) -> int:
        """0, 1 o 2 según el tercio del campo (izquierda, centro, derecha)."""
        return min(2, max(0, int(x // (self.largo / 3))))

    def tercio_relativo(self, x: float, direccion: int) -> str:
        """'propio', 'medio' o 'rival' para un equipo que ataca hacia `direccion` (+1 derecha, -1 izquierda)."""
        tercio = self.tercio(x)
        if direccion < 0:
            tercio = 2 - tercio
        return ("propio", "medio", "rival")[tercio]

    def en_area(self, xy: Punto, lado: int) -> bool:
        x, y = xy
        dentro_y = abs(y - self.ancho / 2) <= AREA_GRANDE_ANCHO_CM / 2
        if lado < 0:
            return dentro_y and 0 <= x <= AREA_GRANDE_LARGO_CM
        return dentro_y and self.largo - AREA_GRANDE_LARGO_CM <= x <= self.largo

    def celda(self, xy: Punto, columnas: int = 12, filas: int = 8) -> tuple[int, int]:
        x, y = xy
        c = min(columnas - 1, max(0, int(x / self.largo * columnas)))
        f = min(filas - 1, max(0, int(y / self.ancho * filas)))
        return c, f


def distancia(a: Punto, b: Punto) -> float:
    return math.hypot(a[0] - b[0], a[1] - b[1])


def velocidad(anterior: Punto, actual: Punto, dt: float) -> tuple[float, float]:
    if dt <= 0:
        return (0.0, 0.0)
    return ((actual[0] - anterior[0]) / dt, (actual[1] - anterior[1]) / dt)


def modulo(v: tuple[float, float]) -> float:
    return math.hypot(v[0], v[1])


def hacia(origen: Punto, v: tuple[float, float], destino: Punto, coseno_minimo: float = 0.82) -> bool:
    """True si el vector v apunta hacia `destino` desde `origen` (ángulo ≤ ~35°)."""
    dx, dy = destino[0] - origen[0], destino[1] - origen[1]
    d = math.hypot(dx, dy)
    m = modulo(v)
    if d == 0 or m == 0:
        return False
    return (v[0] * dx + v[1] * dy) / (d * m) >= coseno_minimo


def corte_con_linea_de_fondo(origen: Punto, v: tuple[float, float], x_linea: float) -> float | None:
    """La y a la que la trayectoria recta desde `origen` con velocidad `v` cruza la vertical x = x_linea."""
    if v[0] == 0:
        return None
    s = (x_linea - origen[0]) / v[0]
    if s < 0:
        return None
    return origen[1] + v[1] * s
