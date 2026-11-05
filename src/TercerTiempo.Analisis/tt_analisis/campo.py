"""Calibración del campo: los puntos clave de las líneas que ve el detector → homografía imagen → campo (cm).

Dos modos. **Cámara móvil** (alguien graba con zoom y paneo, como en televisión): se calibra en cada fotograma y, cuando
no hay puntos suficientes, se comprueba si la homografía anterior sigue valiendo (reproyectando los pocos puntos que sí
se ven); si no, el fotograma queda sin calibrar antes que proyectar mal. **Cámara fija** (tipo Veo o trípode): se
acumulan los puntos de muchos fotogramas, se toma la mediana de cada uno y se ajusta una sola homografía robusta."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field

import cv2
import numpy as np
import supervision as sv
from sports.configs.soccer import SoccerPitchConfiguration

from .tipos import Punto

CONFIG_CAMPO = SoccerPitchConfiguration()
VERTICES = np.array(CONFIG_CAMPO.vertices, dtype=np.float32)
MIN_PUNTOS = 5
CONFIANZA_PUNTO = 0.5
ERROR_MAX_PX = 40.0
ERROR_COHERENCIA_PX = 60.0
SALTO_MAX_CM = 4000.0


class Homografia:
    def __init__(self, origen: np.ndarray, destino: np.ndarray) -> None:
        origen = np.asarray(origen, dtype=np.float32).reshape(-1, 1, 2)
        destino = np.asarray(destino, dtype=np.float32).reshape(-1, 1, 2)
        metodo = cv2.RANSAC if len(origen) >= 6 else 0
        self.m, _ = cv2.findHomography(origen, destino, metodo, 12.0)
        if self.m is None:
            raise ValueError("No se pudo calcular la homografía")
        self.inversa = np.linalg.inv(self.m)

    def a_campo(self, puntos: np.ndarray) -> np.ndarray:
        puntos = np.asarray(puntos, dtype=np.float32)
        if len(puntos) == 0:
            return puntos
        return cv2.perspectiveTransform(puntos.reshape(-1, 1, 2), self.m).reshape(-1, 2)

    def a_imagen(self, puntos: np.ndarray) -> np.ndarray:
        puntos = np.asarray(puntos, dtype=np.float32)
        if len(puntos) == 0:
            return puntos
        return cv2.perspectiveTransform(puntos.reshape(-1, 1, 2), self.inversa).reshape(-1, 2)

    def error_px(self, origen: np.ndarray, destino: np.ndarray) -> float:
        reproyectados = self.a_imagen(destino)
        return float(np.mean(np.linalg.norm(reproyectados - np.asarray(origen, dtype=np.float32), axis=1)))


@dataclass
class Calibracion:
    homografia: Homografia | None = None
    puntos_usados: int = 0
    error_px: float = float("inf")
    t: float = -1.0

    @property
    def valida(self) -> bool:
        return self.homografia is not None


@dataclass
class Calibrador:
    modelo: object
    device: str
    modo: str = "movil"
    cada_s: float = 0.0
    imgsz: int = 1280
    # Sin puntos a la vista, la homografía anterior vale como mucho este tiempo (cámara móvil).
    max_antiguedad_s: float = 1.0
    actual: Calibracion = field(default_factory=Calibracion)
    mejor: Calibracion = field(default_factory=Calibracion)
    fija: Calibracion | None = None
    intentos: int = 0
    aciertos: int = 0
    invalidaciones: int = 0
    saltos_rechazados: int = 0
    _acumulado: dict[int, list[tuple[float, float]]] = field(default_factory=lambda: defaultdict(list))

    def _detectar(self, imagen: np.ndarray) -> tuple[np.ndarray, np.ndarray] | None:
        resultado = self.modelo(imagen, imgsz=self.imgsz, device=self.device, half=self.device.startswith("cuda"), verbose=False)[0]
        puntos = sv.KeyPoints.from_ultralytics(resultado)
        if len(puntos) == 0 or puntos.confidence is None:
            return None
        xy = puntos.xy[0]
        conf = puntos.confidence[0]
        mascara = (conf > CONFIANZA_PUNTO) & (xy[:, 0] > 1) & (xy[:, 1] > 1)
        return xy, mascara

    # ---- Cámara fija ----

    def acumular(self, imagen: np.ndarray) -> int:
        """Guarda los puntos de un fotograma para la calibración global; devuelve cuántos puntos se vieron."""
        detectado = self._detectar(imagen)
        if detectado is None:
            return 0
        xy, mascara = detectado
        for indice in np.flatnonzero(mascara):
            self._acumulado[int(indice)].append((float(xy[indice][0]), float(xy[indice][1])))
        return int(mascara.sum())

    def fijar(self, minimo_vistas: int = 3) -> Calibracion:
        """Una sola homografía con la mediana de cada punto visto al menos `minimo_vistas` veces."""
        indices = [i for i, v in self._acumulado.items() if len(v) >= minimo_vistas]
        if len(indices) < MIN_PUNTOS:
            self.fija = Calibracion()
            return self.fija
        origen = np.array([np.median(np.array(self._acumulado[i]), axis=0) for i in indices], dtype=np.float32)
        destino = VERTICES[indices]
        try:
            h = Homografia(origen, destino)
        except ValueError:
            self.fija = Calibracion()
            return self.fija
        self.fija = Calibracion(h, len(indices), h.error_px(origen, destino), 0.0)
        self.actual = self.mejor = self.fija
        return self.fija

    # ---- Por fotograma ----

    def calibrar(self, imagen: np.ndarray, t: float, forzar: bool = False) -> Calibracion:
        if self.modo == "fija" and self.fija is not None and self.fija.valida:
            return self.fija
        if not forzar and self.actual.valida and self.cada_s > 0 and t - self.actual.t < self.cada_s:
            return self.actual
        self.intentos += 1
        detectado = self._detectar(imagen)
        xy, mascara = detectado if detectado is not None else (None, None)
        n = 0 if mascara is None else int(mascara.sum())

        if n >= MIN_PUNTOS:
            nueva = self._desde_puntos(xy[mascara], VERTICES[mascara], t)
            if nueva.valida and self._salto_imposible(nueva, t, imagen):
                # Con una sola portería a la vista el detector confunde a veces un lado del campo con el otro: una
                # homografía que mueve el centro de la imagen 40 m en menos de un segundo, con pocos puntos, es eso.
                # Se conserva la anterior (si de verdad no cuadra, la pasada principal la invalida al ver a los
                # jugadores fuera del campo).
                self.saltos_rechazados += 1
                return self.actual
            if nueva.valida:
                self.aciertos += 1
                self.actual = nueva
                if nueva.error_px < self.mejor.error_px:
                    self.mejor = nueva
                return self.actual

        if self.actual.valida:
            if n >= 2:
                # Pocos puntos: solo sirven para saber si la cámara se ha movido desde la última calibración.
                error = self.actual.homografia.error_px(xy[mascara], VERTICES[mascara])
                if error <= ERROR_COHERENCIA_PX:
                    return self.actual
                self.invalidaciones += 1
                self.actual = Calibracion(t=t)
            elif t - self.actual.t > self.max_antiguedad_s:
                self.invalidaciones += 1
                self.actual = Calibracion(t=t)
        return self.actual

    def _salto_imposible(self, nueva: Calibracion, t: float, imagen) -> bool:
        if not self.actual.valida or t - self.actual.t > 1.0 or nueva.puntos_usados >= 8:
            return False
        alto, ancho = (imagen.shape[:2] if imagen is not None else (1080, 1920))
        centro = np.array([[ancho / 2, alto / 2]], dtype=np.float32)
        antes = self.actual.homografia.a_campo(centro)[0]
        ahora = nueva.homografia.a_campo(centro)[0]
        return float(np.linalg.norm(ahora - antes)) > SALTO_MAX_CM

    def invalidar(self, t: float) -> None:
        """La pasada principal ha visto que la homografía no cuadra (los jugadores caen fuera del campo)."""
        if self.actual.valida:
            self.invalidaciones += 1
            self.actual = Calibracion(t=t)

    @staticmethod
    def _desde_puntos(origen: np.ndarray, destino: np.ndarray, t: float) -> Calibracion:
        try:
            h = Homografia(origen.astype(np.float32), destino.astype(np.float32))
        except ValueError:
            return Calibracion(t=t)
        error = h.error_px(origen, destino)
        if not np.isfinite(error) or error > ERROR_MAX_PX:
            return Calibracion(t=t)
        return Calibracion(h, len(origen), error, t)

    def a_campo(self, puntos: np.ndarray) -> np.ndarray | None:
        if not self.actual.valida or len(puntos) == 0:
            return None
        return self.actual.homografia.a_campo(np.asarray(puntos, dtype=np.float32))

    def punto_a_campo(self, xy: Punto) -> Punto | None:
        salida = self.a_campo(np.array([xy], dtype=np.float32))
        if salida is None:
            return None
        return (float(salida[0][0]), float(salida[0][1]))

    def resumen(self) -> dict:
        return {
            "modo": self.modo,
            "intentos": self.intentos,
            "aciertos": self.aciertos,
            "invalidaciones": self.invalidaciones,
            "saltos_rechazados": self.saltos_rechazados,
            "puntos_mejor": self.mejor.puntos_usados,
            "error_px_mejor": None if self.mejor.error_px == float("inf") else round(self.mejor.error_px, 1),
            "fija": None if self.fija is None else {"puntos": self.fija.puntos_usados, "error_px": None if self.fija.error_px == float("inf") else round(self.fija.error_px, 1)},
        }
