"""Detección y seguimiento de personas (jugadores, porteros, árbitros) y detección del balón en un fotograma.

Las personas se siguen con el seguidor integrado en Ultralytics (modo `track`, con estado entre fotogramas). El balón
se busca primero en el fotograma entero; si no aparece, por teselas (más lento pero encuentra el balón pequeño del
fondo del campo). Se descartan los balones de tamaño imposible y los saltos imposibles entre fotogramas; entre varios
candidatos se elige el más coherente con las últimas posiciones."""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
import supervision as sv
from sports.common.ball import BallTracker

from .config import CLASE_BALON, Configuracion
from .modelos import Modelos

BALON_MIN_PX = 4
BALON_MAX_PX = 70
SALTO_MAX_PX_S = 2400.0


@dataclass
class Detecciones:
    personas: sv.Detections
    balon: sv.Detections


@dataclass
class Detector:
    modelos: Modelos
    config: Configuracion
    seguidor_balon: BallTracker = field(default_factory=lambda: BallTracker(buffer_size=12))
    perdido: int = 0
    busquedas_finas: int = 0
    descartados: int = 0
    _ultimo_balon: tuple[float, np.ndarray] | None = None

    def __post_init__(self) -> None:
        def callback(trozo: np.ndarray) -> sv.Detections:
            resultado = self.modelos.balon(trozo, imgsz=640, conf=0.3, device=self.modelos.device, half=self.modelos.half, verbose=False)[0]
            return sv.Detections.from_ultralytics(resultado)

        self._rebanador = sv.InferenceSlicer(callback=callback, overlap_filter=sv.OverlapFilter.NONE, slice_wh=(640, 640), overlap_wh=(64, 64))

    def detectar(self, imagen: np.ndarray, t: float) -> Detecciones:
        resultado = self.modelos.jugadores.track(
            imagen, persist=True, tracker=self.config.tracker_yaml(), imgsz=self.config.imgsz, conf=self.config.confianza, device=self.modelos.device, half=self.modelos.half, verbose=False
        )[0]
        todas = sv.Detections.from_ultralytics(resultado)
        if len(todas) == 0 or todas.tracker_id is None:
            personas = sv.Detections.empty()
            balon_general = sv.Detections.empty()
        else:
            personas = todas[todas.class_id != CLASE_BALON]
            balon_general = todas[todas.class_id == CLASE_BALON]
        return Detecciones(personas, self._detectar_balon(imagen, balon_general, t))

    def _detectar_balon(self, imagen: np.ndarray, del_modelo_general: sv.Detections, t: float) -> sv.Detections:
        resultado = self.modelos.balon(imagen, imgsz=self.config.imgsz, conf=0.25, device=self.modelos.device, half=self.modelos.half, verbose=False)[0]
        balon = self._plausibles(sv.Detections.from_ultralytics(resultado), t)
        if len(balon) == 0 and len(del_modelo_general) > 0:
            balon = self._plausibles(del_modelo_general, t)
        if len(balon) == 0 and self.config.balon_fino and self._toca_busqueda_fina():
            self.busquedas_finas += 1
            balon = self._plausibles(self._rebanador(imagen).with_nms(threshold=0.1), t)
        if len(balon) == 0:
            self.perdido += 1
            return balon
        self.perdido = 0
        elegido = self.seguidor_balon.update(balon)
        self._ultimo_balon = (t, elegido.get_anchors_coordinates(sv.Position.CENTER)[0])
        return elegido

    def _plausibles(self, balon: sv.Detections, t: float) -> sv.Detections:
        if len(balon) == 0:
            return balon
        anchos = balon.xyxy[:, 2] - balon.xyxy[:, 0]
        altos = balon.xyxy[:, 3] - balon.xyxy[:, 1]
        lado = np.maximum(anchos, altos)
        mascara = (lado >= BALON_MIN_PX) & (lado <= BALON_MAX_PX)
        if self._ultimo_balon is not None:
            t_prev, xy_prev = self._ultimo_balon
            lapso = t - t_prev
            if 0 < lapso <= 1.0:
                centros = balon.get_anchors_coordinates(sv.Position.CENTER)
                salto = np.linalg.norm(centros - xy_prev, axis=1)
                mascara &= salto <= SALTO_MAX_PX_S * lapso + 60
        self.descartados += int((~mascara).sum())
        return balon[mascara]

    def _toca_busqueda_fina(self) -> bool:
        # Las primeras veces que se pierde el balón se busca fino siempre; si sigue perdido, solo de vez en cuando.
        return self.perdido < 8 or self.perdido % 5 == 0
