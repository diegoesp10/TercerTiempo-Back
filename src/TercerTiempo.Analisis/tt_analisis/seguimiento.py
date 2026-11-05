"""Memoria de cada tracklet (persona seguida): votos de clase y de equipo, y los mejores recortes para que el cuerpo
técnico (o el modelo de lenguaje) sepa quién es. El seguimiento en sí lo hace Ultralytics en modo `track`."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field

import numpy as np
import supervision as sv

from .config import CLASE_ARBITRO, CLASE_PORTERO

MAX_RECORTES = 3


@dataclass
class Recorte:
    t: float
    area: float
    confianza: float
    imagen: np.ndarray


@dataclass
class MemoriaTracklet:
    tracklet: int
    votos_clase: dict[int, int] = field(default_factory=lambda: defaultdict(int))
    votos_equipo: dict[int, int] = field(default_factory=lambda: defaultdict(int))
    recortes: list[Recorte] = field(default_factory=list)

    @property
    def clase(self) -> int:
        return max(self.votos_clase, key=self.votos_clase.get) if self.votos_clase else -1

    @property
    def equipo(self) -> int | None:
        if self.clase == CLASE_ARBITRO or not self.votos_equipo:
            return None
        return max(self.votos_equipo, key=self.votos_equipo.get)

    @property
    def es_portero(self) -> bool:
        return self.clase == CLASE_PORTERO

    def guardar_recorte(self, t: float, caja, confianza: float, imagen: np.ndarray) -> None:
        x1, y1, x2, y2 = caja
        area = float(max(0.0, x2 - x1) * max(0.0, y2 - y1))
        if len(self.recortes) >= MAX_RECORTES and area <= min(r.area for r in self.recortes):
            return
        from .video import recortar

        self.recortes.append(Recorte(t, area, confianza, recortar(imagen, caja, margen=6).copy()))
        self.recortes.sort(key=lambda r: r.area, reverse=True)
        del self.recortes[MAX_RECORTES:]


@dataclass
class Seguidor:
    memorias: dict[int, MemoriaTracklet] = field(default_factory=dict)

    def registrar(self, personas: sv.Detections) -> sv.Detections:
        """Apunta la clase de cada persona seguida; devuelve solo las que tienen identificador de seguimiento."""
        if len(personas) == 0 or personas.tracker_id is None:
            return sv.Detections.empty()
        for i in range(len(personas)):
            tid = int(personas.tracker_id[i])
            memoria = self.memorias.get(tid)
            if memoria is None:
                memoria = self.memorias[tid] = MemoriaTracklet(tid)
            memoria.votos_clase[int(personas.class_id[i])] += 1
        return personas

    def votar_equipo(self, tracklet: int, equipo: int) -> None:
        self.memorias.setdefault(tracklet, MemoriaTracklet(tracklet)).votos_equipo[equipo] += 1

    def equipo_de(self, tracklet: int) -> int | None:
        memoria = self.memorias.get(tracklet)
        return memoria.equipo if memoria else None
