"""Recuentos sobre las posiciones en el campo: distancia y velocidad por jugador seguido, tiempo a la vista, mapas de
calor por jugador y por equipo, y hacia dónde ataca cada equipo en cada mitad."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field

import numpy as np

from .config import VELOCIDAD_MAX_JUGADOR_CM_S
from .geometria import Campo, distancia
from .tipos import Instante

COLUMNAS, FILAS = 12, 8


@dataclass
class ResumenTracklet:
    tracklet: int
    equipo: int | None
    portero: bool
    arbitro: bool
    muestras: int = 0
    segundos: float = 0.0
    distancia_m: float = 0.0
    velocidad_max_kmh: float = 0.0
    primera_t: float | None = None
    ultima_t: float | None = None
    mapa: np.ndarray = field(default_factory=lambda: np.zeros((FILAS, COLUMNAS), dtype=np.int32))
    _ultimo: tuple[float, tuple[float, float]] | None = None
    # Posiciones del último segundo, para medir la velocidad punta sin el temblor muestra a muestra.
    _recientes: list[tuple[float, tuple[float, float]]] = field(default_factory=list)

    def a_dict(self) -> dict:
        return {
            "tracklet": self.tracklet,
            "equipo": self.equipo,
            "portero": self.portero,
            "arbitro": self.arbitro,
            "segundos_a_la_vista": round(self.segundos, 1),
            "desde": None if self.primera_t is None else round(self.primera_t, 1),
            "hasta": None if self.ultima_t is None else round(self.ultima_t, 1),
            "distancia_m": round(self.distancia_m, 0),
            "velocidad_max_kmh": round(self.velocidad_max_kmh, 1),
            "mapa_calor": self.mapa.tolist(),
        }


def resumir_tracklets(instantes: list[Instante], dt: float, campo: Campo | None = None) -> dict[int, ResumenTracklet]:
    campo = campo or Campo()
    resumen: dict[int, ResumenTracklet] = {}
    votos_equipo: dict[int, dict[int, int]] = defaultdict(lambda: defaultdict(int))
    votos_portero: dict[int, int] = defaultdict(int)
    votos_arbitro: dict[int, int] = defaultdict(int)

    for inst in instantes:
        for persona in inst.personas:
            r = resumen.get(persona.tracklet)
            if r is None:
                r = resumen[persona.tracklet] = ResumenTracklet(persona.tracklet, None, False, False)
            r.muestras += 1
            r.segundos += dt
            r.primera_t = inst.t if r.primera_t is None else r.primera_t
            r.ultima_t = inst.t
            if persona.equipo is not None:
                votos_equipo[persona.tracklet][persona.equipo] += 1
            if persona.es_portero:
                votos_portero[persona.tracklet] += 1
            if persona.es_arbitro:
                votos_arbitro[persona.tracklet] += 1
            if persona.xy_campo is None or not campo.dentro(persona.xy_campo, margen=300):
                continue
            c, f = campo.celda(persona.xy_campo, COLUMNAS, FILAS)
            r.mapa[f, c] += 1
            if r._ultimo is not None:
                t_prev, xy_prev = r._ultimo
                lapso = inst.t - t_prev
                if 0 < lapso <= 1.0:
                    paso = distancia(xy_prev, persona.xy_campo)
                    if paso / lapso <= VELOCIDAD_MAX_JUGADOR_CM_S:
                        r.distancia_m += paso / 100
                    else:
                        # Un salto imposible (cambio de identidad o de calibración): la ventana de velocidad empieza de nuevo.
                        r._recientes.clear()
                else:
                    r._recientes.clear()
            r._ultimo = (inst.t, persona.xy_campo)
            # Velocidad punta sobre una ventana de un segundo (la posición de ahora frente a la de hace ~1 s).
            r._recientes.append((inst.t, persona.xy_campo))
            while r._recientes and inst.t - r._recientes[0][0] > 1.2:
                r._recientes.pop(0)
            if len(r._recientes) >= 3:
                t_ini, xy_ini = r._recientes[0]
                lapso = inst.t - t_ini
                if 0.8 <= lapso <= 1.2:
                    v = distancia(xy_ini, persona.xy_campo) / lapso
                    if v <= VELOCIDAD_MAX_JUGADOR_CM_S:
                        r.velocidad_max_kmh = max(r.velocidad_max_kmh, v * 0.036)

    for tracklet, r in resumen.items():
        votos = votos_equipo.get(tracklet)
        if votos:
            r.equipo = max(votos, key=votos.get)
        r.portero = votos_portero[tracklet] > r.muestras / 2
        r.arbitro = votos_arbitro[tracklet] > r.muestras / 2
        if r.arbitro:
            r.equipo = None
    return resumen


def mapa_equipo(resumenes: dict[int, ResumenTracklet], equipo: int) -> np.ndarray:
    mapa = np.zeros((FILAS, COLUMNAS), dtype=np.int64)
    for r in resumenes.values():
        if r.equipo == equipo and not r.arbitro:
            mapa += r.mapa
    return mapa


def direcciones_de_ataque(instantes: list[Instante], mitad_de: list[int], resumenes: dict[int, ResumenTracklet], campo: Campo | None = None) -> dict[tuple[int, int], int]:
    """Para cada (equipo, mitad): +1 si ataca hacia x = largo, -1 si hacia x = 0. Se decide por dónde están sus
    porteros (y si no hay porteros fiables, por dónde está el grueso del equipo)."""
    campo = campo or Campo()
    x_porteros: dict[tuple[int, int], list[float]] = defaultdict(list)
    x_equipo: dict[tuple[int, int], list[float]] = defaultdict(list)
    for inst, mitad in zip(instantes, mitad_de):
        for persona in inst.personas:
            if persona.xy_campo is None:
                continue
            r = resumenes.get(persona.tracklet)
            equipo = r.equipo if r else persona.equipo
            if equipo is None or (r and r.arbitro):
                continue
            x_equipo[(equipo, mitad)].append(persona.xy_campo[0])
            if r and r.portero:
                x_porteros[(equipo, mitad)].append(persona.xy_campo[0])

    direcciones: dict[tuple[int, int], int] = {}
    mitades = {m for m in mitad_de}
    for mitad in mitades:
        por_equipo = {}
        for equipo in (0, 1):
            xs = x_porteros.get((equipo, mitad)) or []
            if len(xs) >= 10:
                por_equipo[equipo] = float(np.median(xs))
            elif x_equipo.get((equipo, mitad)):
                por_equipo[equipo] = float(np.mean(x_equipo[(equipo, mitad)]))
        if len(por_equipo) == 2:
            izquierda = min(por_equipo, key=por_equipo.get)
            direcciones[(izquierda, mitad)] = 1
            direcciones[(1 - izquierda, mitad)] = -1
        elif len(por_equipo) == 1:
            (equipo, x), = por_equipo.items()
            direcciones[(equipo, mitad)] = 1 if x < campo.largo / 2 else -1
            direcciones[(1 - equipo, mitad)] = -direcciones[(equipo, mitad)]
    return direcciones


def mitades(instantes: list[Instante], descanso_t: float | None) -> list[int]:
    return [1 if descanso_t is None or inst.t < descanso_t else 2 for inst in instantes]
