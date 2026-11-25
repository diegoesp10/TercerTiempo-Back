"""Detección y seguimiento de personas (jugadores, porteros, árbitros) y detección del balón en un fotograma.

Las personas las detecta RF-DETR en el fotograma entero (`detector_jugadores.py`) y se siguen entre fotogramas con
BoT-SORT o ByteTrack según la cámara (`rastreador.py`). El balón
lo busca en cada fotograma el detector RF-DETR por teselas de 384 px a resolución nativa, todas en un lote en la GPU
(`detector_balon.py`), así que el balón pequeño del fondo se ve siempre, no solo cuando se pierde. Se descartan los
balones de tamaño imposible y los saltos imposibles respecto al último visto; entre varios candidatos se elige el más
coherente con la trayectoria reciente y con dónde están los jugadores (el balón casi siempre está cerca de alguno), y
un candidato flojo solo vale cerca de un jugador o de donde se esperaba el balón: así el umbral puede ser bajo sin que
cada marca de la línea cuente."""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
import supervision as sv

from .config import ALTURA_JUGADOR_CM, CLASE_ARBITRO, Configuracion
from .modelos import Modelos
from .rastreador import Rastreador

BALON_MIN_PX = 4
BALON_MAX_PX = 70
SALTO_MAX_PX_S = 2400.0
# Medidos el 17-11-2026 con RF-DETR (segunda ronda) sobre los 134 balones de validación del Vallobín: a 0,2 encuentra
# 124 (92 %) y a 0,5 118 (88 %); por encima de 0,5 casi todo lo que no cuadra con la etiqueta es un balón de verdad
# (otro balón, o uno que la regla del etiquetado no confirmó), y entre 0,2 y 0,5 están los reflejos y las medias.
CONF_BALON = 0.2
# Por debajo de esta confianza un candidato solo vale cerca de un jugador o de donde se esperaba el balón.
CONF_FLOJO = 0.5
PRIOR_JUGADOR_M = 6.0
PRIOR_PREDICCION_PX = 150.0
# Al elegir entre candidatos, cuánto resta cada metro hasta el jugador más cercano (hasta un tope) y lo lejos que cae
# de la predicción (en fracción del radio de salto permitido).
PENALIZACION_POR_M = 0.03
LEJOS_MAX_M = 15.0
PENALIZACION_PREDICCION = 0.3
# Lapso máximo desde el último balón visto para predecir dónde está (y para rechazar saltos imposibles).
LAPSO_PREDICCION_S = 1.0
# Fotogramas vecinos de un muestreado sin balón (`Detector.balon_vecinos`): con el balón visto en el muestreado de al lado
# (o en un vecino de hace menos de VECINO_ANCLA_S), se busca solo en una ventana alrededor de donde tiene que estar, con un
# radio que crece con el tiempo; sin nada cerca, en todo el fotograma y con la misma elección que en los muestreados.
VENTANA_MIN_PX = 120.0
VENTANA_CRECE_PX_S = 600.0
VECINO_ANCLA_S = 0.25


@dataclass(frozen=True)
class Muestreado:
    """Lo que se sabe de un fotograma muestreado para buscar el balón en sus vecinos: su segundo, el balón (px) y su
    velocidad (px/s) si se vio, y las personas detectadas."""

    t: float
    balon: np.ndarray | None
    velocidad: np.ndarray | None
    personas: sv.Detections


def ventana_desde(t: float, ancla: tuple[float, np.ndarray, np.ndarray | None]) -> tuple[np.ndarray, float] | None:
    """Centro (px) y radio de la ventana en la que buscar el balón en `t` sabiendo dónde se vio en `ancla` (t, centro,
    velocidad en px/s o None), antes o después. None si está más lejos que LAPSO_PREDICCION_S."""
    lapso = t - ancla[0]
    if abs(lapso) > LAPSO_PREDICCION_S:
        return None
    centro = np.asarray(ancla[1], dtype=np.float32)
    if ancla[2] is not None:
        centro = centro + np.asarray(ancla[2], dtype=np.float32) * lapso
    return centro, VENTANA_MIN_PX + VENTANA_CRECE_PX_S * abs(lapso)


def elegir_en_ventana(cajas: np.ndarray, puntuaciones: np.ndarray, centro: np.ndarray, radio: float, conf_min: float = CONF_BALON) -> tuple[np.ndarray, float] | None:
    """El candidato de la ventana: de tamaño posible, dentro del radio y con la puntuación mínima; gana la confianza,
    descontando lo lejos que está del centro."""
    if len(cajas) == 0:
        return None
    lado = np.maximum(cajas[:, 2] - cajas[:, 0], cajas[:, 3] - cajas[:, 1])
    centros = np.stack([(cajas[:, 0] + cajas[:, 2]) / 2, (cajas[:, 1] + cajas[:, 3]) / 2], axis=1)
    d = np.linalg.norm(centros - centro, axis=1)
    valen = (lado >= BALON_MIN_PX) & (lado <= BALON_MAX_PX) & (d <= radio) & (puntuaciones >= conf_min)
    if not valen.any():
        return None
    nota = np.where(valen, puntuaciones - PENALIZACION_PREDICCION * d / max(radio, 1.0), -np.inf)
    i = int(np.argmax(nota))
    return centros[i], float(puntuaciones[i])


def _zona(centro: np.ndarray, radio: float, imagen: np.ndarray) -> tuple[int, int, int, int]:
    alto, ancho = imagen.shape[:2]
    x, y = float(centro[0]), float(centro[1])
    return (max(0, int(x - radio)), max(0, int(y - radio)), min(ancho, int(x + radio) + 1), min(alto, int(y + radio) + 1))


@dataclass
class Detecciones:
    personas: sv.Detections
    balon: sv.Detections


def _cajas(xyxy: np.ndarray, confianza: np.ndarray) -> sv.Detections:
    xyxy = np.asarray(xyxy, dtype=np.float32).reshape(-1, 4)
    if len(xyxy) == 0:
        return sv.Detections.empty()
    return sv.Detections(xyxy=xyxy, confidence=np.asarray(confianza, dtype=np.float32).reshape(-1), class_id=np.zeros(len(xyxy), dtype=int))


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
    descartados: int = 0
    flojos_descartados: int = 0
    intermedios_mirados: int = 0
    intermedios_vistos: int = 0
    # De los vecinos mirados, los que se miraron enteros (sin balón cerca que diga dónde buscar).
    vecinos_enteros: int = 0
    # Último balón visto: (t, centro en px, velocidad en px/s o None).
    _ultimo_balon: tuple[float, np.ndarray, np.ndarray | None] | None = None
    rastreador: Rastreador = field(init=False)

    def __post_init__(self) -> None:
        self.rastreador = Rastreador(self.config.seguidor_efectivo(), self.config.fps_muestreo, self.config.camara)

    def detectar(self, imagen: np.ndarray, t: float) -> Detecciones:
        personas = self.rastreador.actualizar(self.modelos.jugadores.detectar(imagen, self.config.confianza), imagen, t)
        return Detecciones(personas, self._detectar_balon(imagen, personas, t))

    def _detectar_balon(self, imagen: np.ndarray, personas: sv.Detections, t: float) -> sv.Detections:
        cajas, puntuaciones = self.modelos.balon.detectar(imagen, CONF_BALON)
        candidatos = self._plausibles(_cajas(cajas, puntuaciones), t)
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

    def balon_vecinos(
        self, intermedios: list, previo: Muestreado | None, actual: Muestreado, vecinos: int, dt: float, max_enteros: int = 1,
    ) -> list[tuple[float, np.ndarray, float]]:
        """El balón en los fotogramas intermedios (`video.Fotograma`) que están a `vecinos` o menos de un muestreado sin
        balón, todos en los mismos lotes a la GPU: [(t, centro en px, confianza)] en orden de tiempo. Con balón cerca se
        mira una ventana; sin él, la imagen entera en `max_enteros` fotogramas como mucho: los más alejados de su
        muestreado (los que menos se le parecen) y, a igual distancia, los de antes del actual, porque si en ellos aparece,
        el tramo siguiente ya tiene ancla para sus ventanas."""
        if not intermedios or previo is None or vecinos <= 0:
            return []
        tareas, enteros = [], []
        for f in intermedios:
            de_previo = round((f.t - previo.t) / dt)
            a_actual = round((actual.t - f.t) / dt)
            if previo.balon is None and 1 <= de_previo <= vecinos:
                otro, personas, prioridad = actual, previo.personas, (-de_previo, 1)
            elif actual.balon is None and 1 <= a_actual <= vecinos:
                otro, personas, prioridad = previo, actual.personas, (-a_actual, 0)
            else:
                continue
            ancla = (otro.t, otro.balon, otro.velocidad) if otro.balon is not None else self._ancla_reciente(f.t)
            ventana = ventana_desde(f.t, ancla) if ancla is not None else None
            if ventana is None:
                enteros.append((prioridad, len(tareas)))
            tareas.append((f, ventana, personas))
        if len(enteros) > max_enteros:
            fuera = {k for _, k in sorted(enteros)[max(0, max_enteros):]}
            tareas = [tarea for k, tarea in enumerate(tareas) if k not in fuera]
        if not tareas:
            return []
        zonas = []
        for f, ventana, _ in tareas:
            alto, ancho = f.imagen.shape[:2]
            zonas.append(_zona(ventana[0], ventana[1], f.imagen) if ventana is not None else (0, 0, ancho, alto))
        salida = []
        for (f, ventana, personas), (cajas, puntuaciones) in zip(tareas, self.modelos.balon.detectar_zonas([f.imagen for f, _, _ in tareas], zonas, CONF_BALON)):
            if ventana is not None:
                elegido = elegir_en_ventana(cajas, puntuaciones, ventana[0], ventana[1])
            else:
                self.vecinos_enteros += 1
                candidatos = self._plausibles(_cajas(cajas, puntuaciones), f.t, contar=False)
                balon, _ = elegir_balon(candidatos, personas, *self._prediccion(f.t))
                elegido = (balon.get_anchors_coordinates(sv.Position.CENTER)[0], float(balon.confidence[0])) if len(balon) else None
            if elegido is not None:
                salida.append((f.t, np.asarray(elegido[0], dtype=np.float32), elegido[1]))
        self.intermedios_mirados += len(tareas)
        self.intermedios_vistos += len(salida)
        return sorted(salida, key=lambda x: x[0])

    def _ancla_reciente(self, t: float) -> tuple[float, np.ndarray, np.ndarray | None] | None:
        if self._ultimo_balon is not None and abs(t - self._ultimo_balon[0]) <= VECINO_ANCLA_S:
            return self._ultimo_balon
        return None

    def recordar_balon(self, t: float, centro: np.ndarray) -> None:
        """Un balón visto fuera de la búsqueda de los muestreados (en un intermedio): si es el más reciente, la próxima
        predicción parte de él."""
        if self._ultimo_balon is not None and t <= self._ultimo_balon[0]:
            return
        velocidad = None
        if self._ultimo_balon is not None and 0 < t - self._ultimo_balon[0] <= 0.5:
            velocidad = (np.asarray(centro) - self._ultimo_balon[1]) / (t - self._ultimo_balon[0])
        self._ultimo_balon = (t, np.asarray(centro, dtype=np.float32), velocidad)

    @property
    def ultimo_balon(self) -> tuple[float, np.ndarray, np.ndarray | None] | None:
        return self._ultimo_balon

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

    def _plausibles(self, balon: sv.Detections, t: float, contar: bool = True) -> sv.Detections:
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
        if contar:
            self.descartados += int((~mascara).sum())
        return balon[mascara]
