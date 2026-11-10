"""Lectura del vídeo: información del archivo, fotogramas muestreados de un tramo y recortes."""

from __future__ import annotations

import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator

import cv2
import numpy as np

from .config import Configuracion


@dataclass(frozen=True)
class InfoVideo:
    ruta: Path
    ancho: int
    alto: int
    fps: float
    fotogramas: int

    @property
    def duracion_s(self) -> float:
        return self.fotogramas / self.fps if self.fps else 0.0


def info_video(ruta: Path) -> InfoVideo:
    cap = cv2.VideoCapture(str(ruta))
    if not cap.isOpened():
        raise FileNotFoundError(f"No se puede abrir el vídeo {ruta}")
    try:
        return InfoVideo(
            ruta,
            int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
            int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)),
            float(cap.get(cv2.CAP_PROP_FPS)) or 25.0,
            int(cap.get(cv2.CAP_PROP_FRAME_COUNT)),
        )
    finally:
        cap.release()


@dataclass
class Fotograma:
    indice: int
    t: float
    imagen: np.ndarray


def fotogramas(config: Configuracion, info: InfoVideo, paso: int | None = None, inicio_s: float | None = None, fin_s: float | None = None) -> Iterator[Fotograma]:
    """Fotogramas del tramo, uno de cada `paso` (por defecto, los que hagan falta para `fps_muestreo`)."""
    paso = paso or max(1, round(info.fps / config.fps_muestreo))
    inicio = config.segmento.inicio if inicio_s is None else inicio_s
    fin = config.segmento.fin if fin_s is None else fin_s
    primero = int(inicio * info.fps)
    ultimo = info.fotogramas if fin is None else min(info.fotogramas, int(fin * info.fps))

    cap = cv2.VideoCapture(str(info.ruta))
    if not cap.isOpened():
        raise FileNotFoundError(f"No se puede abrir el vídeo {info.ruta}")
    try:
        if primero > 0:
            cap.set(cv2.CAP_PROP_POS_FRAMES, primero)
        indice = primero
        emitidos = 0
        while indice < ultimo:
            if not cap.grab():
                break
            if (indice - primero) % paso == 0:
                ok, imagen = cap.retrieve()
                if not ok:
                    break
                yield Fotograma(indice, indice / info.fps, imagen)
                emitidos += 1
                if config.max_fotogramas is not None and emitidos >= config.max_fotogramas:
                    break
            indice += 1
    finally:
        cap.release()


def fotogramas_en(ruta: Path, tiempos: list[float]) -> Iterator[tuple[float, np.ndarray]]:
    """Fotogramas sueltos en esos segundos, saltando (para pocos fotogramas repartidos por todo el vídeo)."""
    cap = cv2.VideoCapture(str(ruta))
    if not cap.isOpened():
        raise FileNotFoundError(f"No se puede abrir el vídeo {ruta}")
    try:
        for t in tiempos:
            cap.set(cv2.CAP_PROP_POS_MSEC, max(0.0, t) * 1000)
            ok, imagen = cap.read()
            if ok:
                yield t, imagen
    finally:
        cap.release()


def fotograma_en(ruta: Path, t: float) -> np.ndarray | None:
    cap = cv2.VideoCapture(str(ruta))
    try:
        cap.set(cv2.CAP_PROP_POS_MSEC, max(0.0, t) * 1000)
        ok, imagen = cap.read()
        return imagen if ok else None
    finally:
        cap.release()


def recortar(imagen: np.ndarray, caja, margen: int = 4) -> np.ndarray:
    x1, y1, x2, y2 = (int(v) for v in caja)
    alto, ancho = imagen.shape[:2]
    x1, y1 = max(0, x1 - margen), max(0, y1 - margen)
    x2, y2 = min(ancho, x2 + margen), min(alto, y2 + margen)
    return imagen[y1:y2, x1:x2]


def a_jpeg(imagen: np.ndarray, calidad: int = 88, lado_max: int | None = None) -> bytes:
    if lado_max:
        alto, ancho = imagen.shape[:2]
        escala = lado_max / max(alto, ancho)
        if escala < 1:
            imagen = cv2.resize(imagen, (int(ancho * escala), int(alto * escala)), interpolation=cv2.INTER_AREA)
    ok, datos = cv2.imencode(".jpg", imagen, [int(cv2.IMWRITE_JPEG_QUALITY), calidad])
    if not ok:
        raise RuntimeError("No se pudo codificar la imagen")
    return datos.tobytes()


def extraer_clip(ruta: Path, inicio_s: float, duracion_s: float, destino: Path, ffmpeg: str = "ffmpeg") -> bool:
    """Un clip reencodificado (corte exacto) del vídeo; devuelve False si ffmpeg falla o no está."""
    destino.parent.mkdir(parents=True, exist_ok=True)
    orden = [
        ffmpeg, "-y", "-loglevel", "error", "-ss", f"{max(0.0, inicio_s):.2f}", "-t", f"{duracion_s:.2f}", "-i", str(ruta),
        "-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-an", "-movflags", "+faststart", str(destino),
    ]
    try:
        return subprocess.run(orden, check=False, capture_output=True).returncode == 0
    except OSError:
        return False
