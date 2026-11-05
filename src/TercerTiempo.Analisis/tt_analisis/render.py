"""Vídeo anotado del tramo: cajas por equipo, número de tracklet, balón y, abajo a la izquierda, el radar del campo."""

from __future__ import annotations

from pathlib import Path

import cv2
import numpy as np
from sports.annotators.soccer import draw_pitch

from .campo import CONFIG_CAMPO
from .tipos import Instante

COLOR_ARBITRO = (0, 215, 255)
COLOR_BALON = (255, 255, 255)
ESCALA_RADAR = 0.06


class Renderizador:
    def __init__(self, destino: Path, ancho: int, alto: int, fps: float, colores: list[tuple[int, int, int]]) -> None:
        destino.parent.mkdir(parents=True, exist_ok=True)
        self.escritor = cv2.VideoWriter(str(destino), cv2.VideoWriter_fourcc(*"mp4v"), max(1.0, fps), (ancho, alto))
        self.colores = [tuple(int(v) for v in c[::-1]) for c in colores]
        self.fondo_radar = draw_pitch(CONFIG_CAMPO, scale=ESCALA_RADAR, padding=20)

    def dibujar(self, imagen: np.ndarray, inst: Instante) -> None:
        lienzo = imagen.copy()
        for p in inst.personas:
            x1, y1, x2, y2 = (int(v) for v in p.caja)
            if p.es_arbitro:
                color = COLOR_ARBITRO
            elif p.equipo is None:
                color = (160, 160, 160)
            else:
                color = self.colores[p.equipo]
            centro = (int((x1 + x2) / 2), y2)
            eje = (max(8, int((x2 - x1) * 0.6)), max(4, int((x2 - x1) * 0.2)))
            cv2.ellipse(lienzo, centro, eje, 0, -45, 235, color, 2, cv2.LINE_AA)
            etiqueta = f"{p.tracklet}{'P' if p.es_portero else ''}"
            cv2.putText(lienzo, etiqueta, (x1, max(12, y1 - 4)), cv2.FONT_HERSHEY_SIMPLEX, 0.45, color, 1, cv2.LINE_AA)
        if inst.balon_imagen is not None:
            cv2.circle(lienzo, (int(inst.balon_imagen[0]), int(inst.balon_imagen[1])), 8, COLOR_BALON, 2, cv2.LINE_AA)

        radar = self.fondo_radar.copy()
        for p in inst.personas:
            if p.xy_campo is None or p.es_arbitro:
                continue
            color = (160, 160, 160) if p.equipo is None else self.colores[p.equipo]
            cv2.circle(radar, self._a_radar(p.xy_campo), 5, color, -1, cv2.LINE_AA)
        if inst.balon_campo is not None:
            cv2.circle(radar, self._a_radar(inst.balon_campo), 4, COLOR_BALON, -1, cv2.LINE_AA)
        alto_r, ancho_r = radar.shape[:2]
        alto, ancho = lienzo.shape[:2]
        if alto_r < alto and ancho_r < ancho:
            zona = lienzo[alto - alto_r - 10:alto - 10, 10:10 + ancho_r]
            cv2.addWeighted(radar, 0.85, zona, 0.15, 0, zona)
        self.escritor.write(lienzo)

    @staticmethod
    def _a_radar(xy) -> tuple[int, int]:
        return (int(20 + xy[0] * ESCALA_RADAR), int(20 + xy[1] * ESCALA_RADAR))

    def cerrar(self) -> None:
        self.escritor.release()
