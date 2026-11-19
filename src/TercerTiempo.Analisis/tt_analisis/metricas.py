"""Lectura de etiquetas YOLO y emparejamiento de detecciones con la verdad, para medir cualquier detector."""

from __future__ import annotations

from pathlib import Path

import numpy as np


def leer_etiquetas(ruta: Path, ancho: int, alto: int) -> list[tuple[int, np.ndarray, np.ndarray | None]]:
    """Etiquetas YOLO de una imagen → (clase, caja xyxy en px, puntos (n, 3) en px o None)."""
    salida = []
    if not ruta.exists():
        return salida
    for linea in ruta.read_text(encoding="utf-8").splitlines():
        partes = linea.split()
        if len(partes) < 5:
            continue
        clase = int(partes[0])
        cx, cy, w, h = (float(v) for v in partes[1:5])
        caja = np.array([(cx - w / 2) * ancho, (cy - h / 2) * alto, (cx + w / 2) * ancho, (cy + h / 2) * alto])
        puntos = None
        if len(partes) > 5:
            valores = np.array([float(v) for v in partes[5:]]).reshape(-1, 3)
            puntos = np.stack([valores[:, 0] * ancho, valores[:, 1] * alto, valores[:, 2]], axis=1)
        salida.append((clase, caja, puntos))
    return salida


def iou(a: np.ndarray, b: np.ndarray) -> float:
    x1, y1 = max(a[0], b[0]), max(a[1], b[1])
    x2, y2 = min(a[2], b[2]), min(a[3], b[3])
    inter = max(0.0, x2 - x1) * max(0.0, y2 - y1)
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / union if union > 0 else 0.0


def emparejar(predichas: np.ndarray, verdaderas: np.ndarray, umbral: float, por_centro: bool) -> tuple[int, int, int]:
    """Aciertos, falsos positivos y falsos negativos con emparejamiento codicioso. `por_centro`: acierto si el centro
    cae a menos del tamaño de la caja verdadera (mínimo 20 px), lo que vale para el balón; si no, por IoU ≥ `umbral`."""
    pares = emparejar_pares(predichas, verdaderas, umbral, por_centro)
    return len(pares), len(predichas) - len(pares), len(verdaderas) - len(pares)


def emparejar_pares(predichas: np.ndarray, verdaderas: np.ndarray, umbral: float, por_centro: bool) -> list[tuple[int, int]]:
    """Las parejas (índice de la predicha, índice de la verdadera) del emparejamiento codicioso de `emparejar`."""
    libres = list(range(len(verdaderas)))
    pares: list[tuple[int, int]] = []
    for k, p in enumerate(predichas):
        mejor, mejor_valor = None, None
        for i in libres:
            v = verdaderas[i]
            if por_centro:
                lado = max(20.0, v[2] - v[0], v[3] - v[1])
                d = np.linalg.norm((p[:2] + p[2:4]) / 2 - (v[:2] + v[2:4]) / 2)
                valor, vale = -d, d <= lado
            else:
                valor = iou(p[:4], v[:4])
                vale = valor >= umbral
            if vale and (mejor_valor is None or valor > mejor_valor):
                mejor, mejor_valor = i, valor
        if mejor is not None:
            libres.remove(mejor)
            pares.append((k, mejor))
    return pares
