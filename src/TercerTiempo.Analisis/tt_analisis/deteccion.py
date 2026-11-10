"""Detección y seguimiento de personas (jugadores, porteros, árbitros) y detección del balón en un fotograma.

Las personas se siguen con el seguidor integrado en Ultralytics (modo `track`, con estado entre fotogramas). El balón
se busca a fotograma entero (el detector de balón y la clase balón del detector de jugadores) y, si no aparece, por
teselas en cada fotograma en que está perdido (encuentra el balón pequeño del fondo; cuesta unos 56 ms por fotograma en
la RTX 5070 Ti, medido con el vídeo del Vallobín). Se descartan los balones de tamaño imposible y los saltos imposibles
respecto al último visto; entre varios candidatos se elige el más coherente con la trayectoria reciente y con dónde
están los jugadores (el balón casi siempre está cerca de alguno), y un candidato flojo solo vale cerca de un jugador o
de donde se esperaba el balón: así las teselas pueden bajar el umbral sin que cada marca de la línea cuente."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import supervision as sv

from .config import ALTURA_JUGADOR_CM, CLASE_ARBITRO, CLASE_BALON, Configuracion
from .modelos import Modelos

BALON_MIN_PX = 4
BALON_MAX_PX = 70
SALTO_MAX_PX_S = 2400.0
CONF_BALON = 0.25
CONF_TESELA = 0.2
# Por debajo de esta confianza un candidato solo vale cerca de un jugador o de donde se esperaba el balón.
CONF_FLOJO = 0.3
PRIOR_JUGADOR_M = 6.0
PRIOR_PREDICCION_PX = 150.0
# Al elegir entre candidatos, cuánto resta cada metro hasta el jugador más cercano (hasta un tope) y lo lejos que cae
# de la predicción (en fracción del radio de salto permitido).
PENALIZACION_POR_M = 0.03
LEJOS_MAX_M = 15.0
PENALIZACION_PREDICCION = 0.3
# Lapso máximo desde el último balón visto para predecir dónde está (y para rechazar saltos imposibles).
LAPSO_PREDICCION_S = 1.0


@dataclass
class Detecciones:
    personas: sv.Detections
    balon: sv.Detections


def _cajas(xyxy: np.ndarray, confianza: np.ndarray) -> sv.Detections:
    xyxy = np.asarray(xyxy, dtype=np.float32).reshape(-1, 4)
    if len(xyxy) == 0:
        return sv.Detections.empty()
    return sv.Detections(xyxy=xyxy, confidence=np.asarray(confianza, dtype=np.float32).reshape(-1), class_id=np.zeros(len(xyxy), dtype=int))


def juntar(*grupos: sv.Detections) -> sv.Detections:
    """Las detecciones de balón de varias fuentes, sin duplicados (y sin arrastrar identificadores de seguimiento)."""
    con_algo = [g for g in grupos if len(g)]
    if not con_algo:
        return sv.Detections.empty()
    todas = _cajas(np.concatenate([g.xyxy for g in con_algo]), np.concatenate([g.confidence if g.confidence is not None else np.ones(len(g)) for g in con_algo]))
    return todas.with_nms(threshold=0.3)


def distancias_a_jugadores_m(centros: np.ndarray, personas: sv.Detections) -> np.ndarray:
    """Metros de cada centro al jugador más cercano, en la imagen, tomando la altura de cada jugador como 1,80 m
    (lo que vale para el prior: cerca o lejos del grupo). Infinito si no hay jugadores."""
    salida = np.full(len(centros), np.inf, dtype=np.float32)
    if len(centros) == 0 or len(personas) == 0:
        return salida
    jugadores = personas[personas.class_id != CLASE_ARBITRO] if personas.class_id is not None else personas
    if len(jugadores) == 0:
        return salida
    pies = jugadores.get_anchors_coordinates(sv.Position.BOTTOM_CENTER)
    altos = np.maximum(8.0, jugadores.xyxy[:, 3] - jugadores.xyxy[:, 1])
    for i, c in enumerate(centros):
        d = np.linalg.norm(pies - c, axis=1) / altos * ALTURA_JUGADOR_CM / 100
        salida[i] = float(d.min())
    return salida


def elegir_balon(candidatos: sv.Detections, personas: sv.Detections, prediccion: np.ndarray | None = None, radio_prediccion: float = PRIOR_PREDICCION_PX) -> tuple[sv.Detections, int]:
    """El candidato más plausible, o ninguno. Los flojos (confianza < CONF_FLOJO) solo valen cerca de un jugador o de la
    predicción; entre los que quedan gana la confianza, descontando lo lejos que está de los jugadores y de la
    predicción. Devuelve (elegido o vacío, cuántos flojos se descartaron)."""
    if len(candidatos) == 0:
        return candidatos, 0
    centros = candidatos.get_anchors_coordinates(sv.Position.CENTER)
    conf = candidatos.confidence if candidatos.confidence is not None else np.ones(len(candidatos), dtype=np.float32)
    lejos_m = distancias_a_jugadores_m(centros, personas)
    d_pred = np.linalg.norm(centros - prediccion, axis=1) if prediccion is not None else np.full(len(centros), np.inf)
    cerca = (lejos_m <= PRIOR_JUGADOR_M) | (d_pred <= radio_prediccion)
    valen = (conf >= CONF_FLOJO) | cerca
    flojos = int((~valen).sum())
    if not valen.any():
        return sv.Detections.empty(), flojos
    # Lejos de los jugadores resta, salvo que esté donde la trayectoria reciente lo esperaba (un pase largo, un
    # despeje): entonces la trayectoria ya lo explica. Y lejos de la predicción resta hasta un tope.
    lejos_penaliza = np.minimum(np.where(np.isinf(lejos_m), 0.0, lejos_m), LEJOS_MAX_M) * (d_pred > radio_prediccion)
    puntuacion = conf - PENALIZACION_POR_M * lejos_penaliza
    if prediccion is not None:
        puntuacion = puntuacion - PENALIZACION_PREDICCION * np.minimum(1.0, d_pred / max(radio_prediccion, 1.0))
    puntuacion = np.where(valen, puntuacion, -np.inf)
    return candidatos[[int(np.argmax(puntuacion))]], flojos


@dataclass
class Detector:
    modelos: Modelos
    config: Configuracion
    perdido: int = 0
    busquedas_finas: int = 0
    descartados: int = 0
    flojos_descartados: int = 0
    # Último balón visto: (t, centro en px, velocidad en px/s o None).
    _ultimo_balon: tuple[float, np.ndarray, np.ndarray | None] | None = None

    def __post_init__(self) -> None:
        def callback(trozo: np.ndarray) -> sv.Detections:
            resultado = self.modelos.balon(trozo, imgsz=640, conf=CONF_TESELA, device=self.modelos.device, **self.modelos.precision, verbose=False)[0]
            return sv.Detections.from_ultralytics(resultado)

        self._rebanador = sv.InferenceSlicer(callback=callback, overlap_filter=sv.OverlapFilter.NONE, slice_wh=(640, 640), overlap_wh=(64, 64))

    def detectar(self, imagen: np.ndarray, t: float) -> Detecciones:
        resultado = self.modelos.jugadores.track(
            imagen, persist=True, tracker=self.config.tracker_yaml(), imgsz=self.config.imgsz, conf=self.config.confianza, device=self.modelos.device, **self.modelos.precision, verbose=False
        )[0]
        todas = sv.Detections.from_ultralytics(resultado)
        if len(todas) == 0 or todas.tracker_id is None:
            personas = sv.Detections.empty()
            balon_general = sv.Detections.empty()
        else:
            personas = todas[todas.class_id != CLASE_BALON]
            balon_general = todas[todas.class_id == CLASE_BALON]
        return Detecciones(personas, self._detectar_balon(imagen, balon_general, personas, t))

    def _detectar_balon(self, imagen: np.ndarray, del_modelo_general: sv.Detections, personas: sv.Detections, t: float) -> sv.Detections:
        resultado = self.modelos.balon(imagen, imgsz=self.config.imgsz, conf=CONF_BALON, device=self.modelos.device, **self.modelos.precision, verbose=False)[0]
        candidatos = self._plausibles(juntar(sv.Detections.from_ultralytics(resultado), del_modelo_general), t)
        if len(candidatos) == 0 and self.config.balon_fino and self._toca_busqueda_fina():
            self.busquedas_finas += 1
            candidatos = self._plausibles(self._rebanador(imagen).with_nms(threshold=0.1), t)
        prediccion, radio = self._prediccion(t)
        balon, flojos = elegir_balon(candidatos, personas, prediccion, radio)
        self.flojos_descartados += flojos
        if len(balon) == 0:
            self.perdido += 1
            return balon
        self.perdido = 0
        centro = balon.get_anchors_coordinates(sv.Position.CENTER)[0]
        velocidad = None
        if self._ultimo_balon is not None and 0 < t - self._ultimo_balon[0] <= 0.5:
            velocidad = (centro - self._ultimo_balon[1]) / (t - self._ultimo_balon[0])
        self._ultimo_balon = (t, centro, velocidad)
        return balon

    def _prediccion(self, t: float) -> tuple[np.ndarray | None, float]:
        """Dónde debería estar el balón ahora según el último visto (si fue hace poco) y el radio en el que se admite."""
        if self._ultimo_balon is None:
            return None, PRIOR_PREDICCION_PX
        t_prev, xy_prev, v = self._ultimo_balon
        lapso = t - t_prev
        if not 0 < lapso <= LAPSO_PREDICCION_S:
            return None, PRIOR_PREDICCION_PX
        esperado = xy_prev if v is None else xy_prev + v * lapso
        return esperado, max(PRIOR_PREDICCION_PX, 600.0 * lapso)

    def _plausibles(self, balon: sv.Detections, t: float) -> sv.Detections:
        if len(balon) == 0:
            return balon
        anchos = balon.xyxy[:, 2] - balon.xyxy[:, 0]
        altos = balon.xyxy[:, 3] - balon.xyxy[:, 1]
        lado = np.maximum(anchos, altos)
        mascara = (lado >= BALON_MIN_PX) & (lado <= BALON_MAX_PX)
        if self._ultimo_balon is not None:
            t_prev, xy_prev, _ = self._ultimo_balon
            lapso = t - t_prev
            if 0 < lapso <= LAPSO_PREDICCION_S:
                centros = balon.get_anchors_coordinates(sv.Position.CENTER)
                salto = np.linalg.norm(centros - xy_prev, axis=1)
                mascara &= salto <= SALTO_MAX_PX_S * lapso + 60
        self.descartados += int((~mascara).sum())
        return balon[mascara]

    def _toca_busqueda_fina(self) -> bool:
        # Cada fotograma en que el balón está perdido: hasta el 06-11-2026 se buscaba fino solo las primeras ocho veces y
        # luego una de cada cinco, y con el balón visto en uno de cada cinco fotogramas eso dejaba sin buscar la mitad.
        return True
