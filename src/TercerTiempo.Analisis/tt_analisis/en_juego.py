"""Cuándo está el balón en juego. En un partido el balón está parado buena parte del tiempo (fueras, faltas, córners,
saques de puerta, cambios): mientras tanto no verlo no es fallo del analizador, y lo que se mide del balón (visto, con
trayectoria) se cuenta sobre el tiempo de juego.

Parado, por tres señales que se ven en el vídeo:
- el balón fuera del campo, desde que sale hasta que se vuelve a ver dentro;
- el balón quieto en el mismo sitio (`balon.permanencias`: una falta o un córner a punto de sacarse);
- un rato sin ver el balón con los jugadores casi quietos (andando o esperando el saque). El movimiento de cada jugador se
  mide en la imagen respecto al de todos (así no cuenta el paneo de la cámara) y en altos de su caja por segundo (así da
  igual lo lejos que esté).
Y lo que ya paraban los eventos (`eventos.Resultado.juego_activo`) y las interrupciones.

Medido el 25-11-2026 en el J8 (minutos 20-30) contra 24 momentos mirados a ojo: acierta en 21 (en 19 de los 20 que no
dejaban duda). Se equivoca hacia «en juego»: un balón parado con el balón a la vista y los jugadores moviéndose (un saque
de banda rápido) cuenta como juego."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .balon import permanencias
from .geometria import Campo
from .tipos import Instante

# Movimiento de cada jugador entre MOVIMIENTO_INSTANTES antes y después; por instante, el percentil MOVIMIENTO_PERCENTIL
# de los que tienen sus dos puntos (al menos MOVIMIENTO_MIN_JUGADORES), suavizado en SUAVIZADO_S.
MOVIMIENTO_INSTANTES = 2
MOVIMIENTO_PERCENTIL = 75
MOVIMIENTO_MIN_JUGADORES = 4
SUAVIZADO_S = 4.0
# Sin balón (ni visto ni interpolado) SIN_BALON_S o más, y por debajo de QUIETOS altos por segundo: parado.
SIN_BALON_S = 3.0
QUIETOS = 0.35
# Fuera del campo por más de esto (lo que tiembla la calibración en la línea no es un fuera).
FUERA_MARGEN_CM = 100.0


@dataclass
class EnJuego:
    en_juego: list[bool]
    # Instantes con el balón visto y con trayectoria (visto o interpolado) de los que están en juego.
    vistos: int = 0
    con_trayectoria: int = 0

    @property
    def instantes(self) -> int:
        return sum(self.en_juego)

    def pct(self, n: int) -> float:
        return round(100 * n / self.instantes, 1) if self.instantes else 0.0

    def a_dict(self, dt: float) -> dict:
        total = len(self.en_juego)
        return {
            "segundos_en_juego": round(self.instantes * dt, 1),
            "pct_en_juego": round(100 * self.instantes / total, 1) if total else 0.0,
            "pct_visto": self.pct(self.vistos),
            "pct_con_trayectoria": self.pct(self.con_trayectoria),
        }


def movimiento_relativo(instantes: list[Instante], medio: int = MOVIMIENTO_INSTANTES) -> np.ndarray:
    """Por instante, cuánto se mueven los jugadores (altos de su caja por segundo) respecto a la mediana de todos; NaN
    si no hay bastantes con su punto a los dos lados."""
    posiciones: dict[int, dict[int, tuple[tuple[float, float], float]]] = {}
    for i, inst in enumerate(instantes):
        for p in inst.personas:
            if p.es_jugador:
                posiciones.setdefault(p.tracklet, {})[i] = (p.xy_imagen, p.caja[3] - p.caja[1])
    salida = np.full(len(instantes), np.nan)
    for i in range(medio, len(instantes) - medio):
        movimientos, altos = [], []
        for p in instantes[i].personas:
            serie = posiciones.get(p.tracklet)
            if serie is None or not p.es_jugador:
                continue
            a, b = serie.get(i - medio), serie.get(i + medio)
            if a is None or b is None:
                continue
            movimientos.append((b[0][0] - a[0][0], b[0][1] - a[0][1]))
            altos.append(max(20.0, (a[1] + b[1]) / 2))
        if len(movimientos) < MOVIMIENTO_MIN_JUGADORES:
            continue
        m = np.asarray(movimientos)
        relativo = m - np.median(m, axis=0)
        lapso = instantes[i + medio].t - instantes[i - medio].t
        if lapso <= 0:
            continue
        salida[i] = float(np.percentile(np.hypot(relativo[:, 0], relativo[:, 1]) / np.asarray(altos) / lapso, MOVIMIENTO_PERCENTIL))
    return salida


def _suavizar(valores: np.ndarray, dt: float) -> np.ndarray:
    if not np.isfinite(valores).any():
        return np.full(len(valores), np.inf)
    k = max(1, int(round(SUAVIZADO_S / max(dt, 1e-6))))
    relleno = np.where(np.isnan(valores), np.nanmedian(valores), valores)
    return np.convolve(relleno, np.ones(k) / k, mode="same")


def balon_en_juego(instantes: list[Instante], dt: float, juego_activo: list[bool] | None = None, campo: Campo | None = None) -> EnJuego:
    """Para cada instante, si el balón está en juego (ver el principio del módulo)."""
    campo = campo or Campo()
    n = len(instantes)
    parado = np.zeros(n, dtype=bool)
    if juego_activo is not None:
        parado |= ~np.asarray(juego_activo, dtype=bool)
    for p in permanencias(instantes):
        parado[p.indices[0]: p.indices[-1] + 1] = True
    tras_fuera = False
    for i, inst in enumerate(instantes):
        if inst.balon_campo is not None and not campo.dentro(inst.balon_campo, margen=FUERA_MARGEN_CM):
            tras_fuera = True
        elif inst.balon_imagen is not None:
            tras_fuera = False
        parado[i] |= tras_fuera
    movimiento = _suavizar(movimiento_relativo(instantes), dt)
    i = 0
    while i < n:
        if instantes[i].balon_imagen is not None:
            i += 1
            continue
        j = i
        while j < n and instantes[j].balon_imagen is None:
            j += 1
        if instantes[j - 1].t - instantes[i].t >= SIN_BALON_S:
            parado[i:j] |= movimiento[i:j] < QUIETOS
        i = j
    en_juego = [not x for x in parado]
    return EnJuego(
        en_juego,
        vistos=sum(1 for inst, si in zip(instantes, en_juego) if si and inst.balon_origen == "visto"),
        con_trayectoria=sum(1 for inst, si in zip(instantes, en_juego) if si and inst.balon_imagen is not None),
    )
