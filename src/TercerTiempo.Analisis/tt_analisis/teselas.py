"""Teselas: el balón mide unos 8 px en un fotograma de 1080p, demasiado poco para un detector que mira la imagen
reducida. Se busca en trozos cuadrados a resolución nativa que el detector amplía (384 px → 576), donde el balón queda
en unos 12 px. La misma rejilla sirve para preparar el entrenamiento y para analizar, así que el detector ve en el
análisis exactamente lo que vio al aprender."""

from __future__ import annotations

import numpy as np

LADO = 384
SOLAPE = 48


def posiciones(total: int, lado: int = LADO, solape: int = SOLAPE) -> list[int]:
    """Inicios de las teselas a lo largo de un eje: cubren todo el eje, se solapan al menos `solape` px (para que un
    balón en el borde de una caiga entero en la vecina) y la última acaba justo en el borde."""
    if total <= lado:
        return [0]
    paso = lado - solape
    n = int(np.ceil((total - lado) / paso)) + 1
    # Repartidas por igual: el solape real queda entre `solape` y algo más, nunca menos.
    return [round(i * (total - lado) / (n - 1)) for i in range(n)]


def rejilla(ancho: int, alto: int, lado: int = LADO, solape: int = SOLAPE) -> list[tuple[int, int]]:
    return [(x, y) for y in posiciones(alto, lado, solape) for x in posiciones(ancho, lado, solape)]


def recortar(imagen: np.ndarray, x: int, y: int, lado: int = LADO) -> np.ndarray:
    """El trozo `lado`×`lado` desde (x, y); si la imagen es más pequeña, se rellena con negro."""
    trozo = imagen[y : y + lado, x : x + lado]
    if trozo.shape[0] == lado and trozo.shape[1] == lado:
        return trozo
    lleno = np.zeros((lado, lado, *imagen.shape[2:]), dtype=imagen.dtype)
    lleno[: trozo.shape[0], : trozo.shape[1]] = trozo
    return lleno


def a_fotograma(cajas: list[np.ndarray], puntuaciones: list[np.ndarray], origenes: list[tuple[int, int]], iou: float = 0.3) -> tuple[np.ndarray, np.ndarray]:
    """Junta las detecciones de cada tesela (en sus coordenadas) en las del fotograma y quita las repetidas en las
    zonas de solape (se queda la de más puntuación)."""
    todas_cajas, todas_puntuaciones = [], []
    for c, p, (x, y) in zip(cajas, puntuaciones, origenes):
        if len(c):
            todas_cajas.append(np.asarray(c, dtype=np.float32) + np.array([x, y, x, y], dtype=np.float32))
            todas_puntuaciones.append(np.asarray(p, dtype=np.float32))
    if not todas_cajas:
        return np.zeros((0, 4), dtype=np.float32), np.zeros((0,), dtype=np.float32)
    c = np.concatenate(todas_cajas)
    p = np.concatenate(todas_puntuaciones)
    quedan = nms(c, p, iou)
    return c[quedan], p[quedan]


def nms(cajas: np.ndarray, puntuaciones: np.ndarray, iou: float) -> np.ndarray:
    orden = np.argsort(-puntuaciones)
    quedan: list[int] = []
    areas = np.maximum(cajas[:, 2] - cajas[:, 0], 0) * np.maximum(cajas[:, 3] - cajas[:, 1], 0)
    while len(orden):
        i = int(orden[0])
        quedan.append(i)
        resto = orden[1:]
        x1 = np.maximum(cajas[i, 0], cajas[resto, 0])
        y1 = np.maximum(cajas[i, 1], cajas[resto, 1])
        x2 = np.minimum(cajas[i, 2], cajas[resto, 2])
        y2 = np.minimum(cajas[i, 3], cajas[resto, 3])
        inter = np.maximum(x2 - x1, 0) * np.maximum(y2 - y1, 0)
        union = areas[i] + areas[resto] - inter
        solape = np.where(union > 0, inter / np.maximum(union, 1e-9), 0)
        orden = resto[solape <= iou]
    return np.array(quedan, dtype=np.int64)
