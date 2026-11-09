"""El color de la camiseta de cada jugador y el reparto en dos equipos por ese color. Las equipaciones de los dos
equipos son siempre distintas: con el torso del recorte (sin el césped que asoma) basta, y aguanta mejor que un modelo
de apariencia, que con el vídeo nocturno del Vallobín separaba por la luz y no por la camiseta.

Lo que se mide de cada torso es un resumen de sus píxeles en Lab: claridad media y su dispersión, tono medio (a, b),
saturación media y qué parte de los píxeles es oscura o clara. La claridad se mide respecto al césped que rodea al
jugador en el mismo recorte, que dice cuánta luz hay ahí: de noche, el mismo jugador bajo un foco y en la sombra tiene
que caer en el mismo equipo. De día separan los tonos (azul frente a amarillo); de noche, con los focos apagando los
colores, separa la claridad (una camiseta azul marino frente a una a rayas rojas y blancas). Los rasgos se normalizan
antes de agrupar para que mande el que de verdad distinga en ese vídeo."""

from __future__ import annotations

from dataclasses import dataclass, field

import cv2
import numpy as np

# Torso del recorte: por debajo de la cabeza y por encima del pantalón, sin los brazos.
TORSO_ALTO = (0.18, 0.55)
TORSO_ANCHO = (0.25, 0.75)
# Césped en HSV de OpenCV (tono 0-179): verde-amarillento de día y bajo los focos.
CESPED_TONO = (25, 95)
CESPED_SAT_MIN = 50
CESPED_VAL_MIN = 40
MIN_PIXELES = 40
# Claridad del césped cuando el recorte no trae césped (la mediana de los vídeos nocturnos probados) y umbrales,
# respecto al césped, para contar píxeles oscuros y claros.
L_CESPED_TIPICA = 116.0
OSCURO_BAJO_CESPED = 25.0
CLARO_SOBRE_CESPED = 50.0
MIN_PIXELES_CESPED = 30
# Rasgos: los siete primeros agrupan; el último (L media sin corregir) solo sirve para enseñar el color del equipo.
RASGOS = 8
AGRUPAN = slice(0, 7)


def _mascara_cesped(bgr: np.ndarray) -> np.ndarray:
    hsv = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV)
    tono, sat, val = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    return (tono >= CESPED_TONO[0]) & (tono <= CESPED_TONO[1]) & (sat >= CESPED_SAT_MIN) & (val >= CESPED_VAL_MIN)


def claridad_cesped(recorte_bgr: np.ndarray) -> float | None:
    """L media del césped que asoma en el recorte entero: cuánta luz hay donde está el jugador."""
    cesped = _mascara_cesped(recorte_bgr)
    if cesped.sum() < MIN_PIXELES_CESPED:
        return None
    return float(cv2.cvtColor(recorte_bgr, cv2.COLOR_BGR2LAB)[..., 0][cesped].mean())


def pixeles_camiseta(recorte_bgr: np.ndarray) -> np.ndarray | None:
    """Píxeles del torso que no son césped, en Lab (N × 3, float32); None si quedan muy pocos."""
    if recorte_bgr is None or recorte_bgr.size == 0:
        return None
    alto, ancho = recorte_bgr.shape[:2]
    torso = recorte_bgr[int(alto * TORSO_ALTO[0]):int(alto * TORSO_ALTO[1]), int(ancho * TORSO_ANCHO[0]):int(ancho * TORSO_ANCHO[1])]
    if torso.size == 0:
        return None
    cesped = _mascara_cesped(torso)
    lab = cv2.cvtColor(torso, cv2.COLOR_BGR2LAB).reshape(-1, 3).astype(np.float32)
    validos = lab[~cesped.reshape(-1)]
    if len(validos) < MIN_PIXELES:
        return None
    return validos


def rasgos_camiseta(recorte_bgr: np.ndarray) -> np.ndarray | None:
    """Resumen del torso: [L media respecto al césped, desviación de L, a media, b media, saturación media, parte
    oscura, parte clara, L media sin corregir]."""
    pixeles = pixeles_camiseta(recorte_bgr)
    if pixeles is None:
        return None
    referencia = claridad_cesped(recorte_bgr)
    if referencia is None:
        referencia = L_CESPED_TIPICA
    luz, a, b = pixeles[:, 0], pixeles[:, 1], pixeles[:, 2]
    saturacion = np.sqrt((a - 128.0) ** 2 + (b - 128.0) ** 2)
    return np.array(
        [
            luz.mean() - referencia,
            luz.std(),
            a.mean(),
            b.mean(),
            saturacion.mean(),
            (luz < referencia - OSCURO_BAJO_CESPED).mean(),
            (luz > referencia + CLARO_SOBRE_CESPED).mean(),
            luz.mean(),
        ],
        dtype=np.float32,
    )


def lab_a_rgb(lab: np.ndarray) -> tuple[int, int, int]:
    pixel = np.clip(np.asarray(lab, dtype=np.float32), 0, 255).astype(np.uint8).reshape(1, 1, 3)
    bgr = cv2.cvtColor(pixel, cv2.COLOR_LAB2BGR)[0, 0]
    return int(bgr[2]), int(bgr[1]), int(bgr[0])


# Pasadas de refinado de la dirección que separa las dos equipaciones.
REFINADOS = 3


@dataclass
class ClasificadorColor:
    """Dos grupos por el color de la camiseta. KMeans sobre los rasgos normalizados de muchos recortes da un primer
    reparto; con él se calcula la dirección que mejor separa los dos grupos (discriminante de Fisher: pesa cada rasgo
    por lo que distingue y descuenta lo que solo mete ruido, como la dispersión o la saturación cuando no separan) y se
    vuelve a repartir proyectando sobre ella, un par de veces. Cada recorte recibe una puntuación: negativa hacia el
    equipo 0, positiva hacia el 1, de tamaño 1 en el centro de cada grupo y cerca de 0 cuando no está claro."""

    direccion: np.ndarray | None = None
    origen: np.ndarray | None = None
    media: np.ndarray | None = None
    escala: np.ndarray | None = None
    colores: list[tuple[int, int, int]] = field(default_factory=list)
    reparto: tuple[int, int] = (0, 0)

    def ajustar(self, rasgos: list[np.ndarray]) -> None:
        from sklearn.cluster import KMeans

        crudos = np.array(rasgos, dtype=np.float32).reshape(-1, RASGOS)
        self.media = crudos[:, AGRUPAN].mean(axis=0)
        self.escala = np.maximum(crudos[:, AGRUPAN].std(axis=0), 1e-3)
        datos = self._normalizar(crudos)
        etiquetas = KMeans(n_clusters=2, n_init=10, random_state=0).fit(datos).labels_
        for _ in range(REFINADOS):
            if min(np.bincount(etiquetas, minlength=2)) < 2:
                break
            self._orientar(datos, etiquetas)
            nuevas = (self.puntuar_normalizados(datos) > 0).astype(int)
            if np.array_equal(nuevas, etiquetas):
                break
            etiquetas = nuevas
        if self.direccion is None:
            self._orientar(datos, etiquetas)
        # El equipo 0 es el que más recortes tiene: así el orden no depende de la semilla.
        cuentas = np.bincount(etiquetas, minlength=2)
        if cuentas[1] > cuentas[0]:
            self.direccion = -self.direccion
            etiquetas = 1 - etiquetas
            cuentas = cuentas[::-1]
        self.reparto = (int(cuentas[0]), int(cuentas[1]))
        self.colores = [lab_a_rgb(np.median(crudos[etiquetas == equipo][:, [7, 2, 3]], axis=0)) for equipo in (0, 1)]

    def _orientar(self, datos: np.ndarray, etiquetas: np.ndarray) -> None:
        grupos = [datos[etiquetas == equipo] for equipo in (0, 1)]
        medias = [g.mean(axis=0) for g in grupos]
        dispersion = sum(np.cov(g, rowvar=False, bias=True) * len(g) for g in grupos) / len(datos)
        dispersion += np.eye(datos.shape[1]) * 0.05
        direccion = np.linalg.solve(dispersion, medias[1] - medias[0])
        proyecciones = [float(m @ direccion) for m in medias]
        separacion = max(proyecciones[1] - proyecciones[0], 1e-6)
        self.direccion = (direccion * 2 / separacion).astype(np.float32)
        self.origen = ((medias[0] + medias[1]) / 2).astype(np.float32)

    def puntuar(self, rasgos: list[np.ndarray]) -> np.ndarray:
        """Puntuación de cada recorte: < 0 equipo 0, > 0 equipo 1; ±1 en el centro de cada grupo."""
        if self.direccion is None:
            raise RuntimeError("El clasificador de equipos no está ajustado.")
        return self.puntuar_normalizados(self._normalizar(np.array(rasgos, dtype=np.float32).reshape(-1, RASGOS)))

    def puntuar_normalizados(self, datos: np.ndarray) -> np.ndarray:
        return (datos - self.origen) @ self.direccion

    def predecir(self, rasgos: list[np.ndarray]) -> np.ndarray:
        return (self.puntuar(rasgos) > 0).astype(int)

    def _normalizar(self, datos: np.ndarray) -> np.ndarray:
        return (datos[:, AGRUPAN] - self.media) / self.escala

    @property
    def desequilibrado(self) -> bool:
        """Un grupo con menos de la quinta parte de los recortes: el reparto no parece de dos equipos."""
        total = sum(self.reparto)
        return total > 0 and min(self.reparto) < total / 5
