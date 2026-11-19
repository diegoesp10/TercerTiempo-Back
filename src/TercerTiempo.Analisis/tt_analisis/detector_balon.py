"""El detector del balón con RF-DETR (Apache 2.0): entrenamiento sobre teselas, exportación a TensorRT y búsqueda en
el fotograma por teselas (`rfdetr_comun.py` tiene lo que comparte con el de los jugadores)."""

from __future__ import annotations

import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from . import rfdetr_comun
from .teselas import LADO, a_fotograma, rejilla

QUE = "balon"
RESOLUCION = 576
LOTE_MAX_TRT = 32


def ruta_pesos(modelos: Path) -> Path:
    return rfdetr_comun.ruta_pesos(modelos, QUE)


def ruta_motor(modelos: Path) -> Path:
    return rfdetr_comun.ruta_motor(modelos, QUE)


def entrenar(dataset: Path, modelos: Path, salida: Path, epocas: int = 40, lote: int = 8, acumular: int = 2, lr: float = 1e-4, paciencia: int = 8, device: str = "cuda", informar=print) -> Path:
    return rfdetr_comun.entrenar(QUE, dataset, modelos, salida, RESOLUCION, ["ball"], epocas, lote, acumular, lr, paciencia, device, {"tesela": LADO}, informar)


def exportar_tensorrt(modelos: Path, lote: int = 24, lote_max: int = LOTE_MAX_TRT, informar=print) -> Path:
    """Lote dinámico afinado para 24: las teselas de un fotograma de 1080p."""
    return rfdetr_comun.exportar_tensorrt(QUE, modelos, RESOLUCION, lote, lote_max, informar)


@dataclass
class DetectorBalon:
    """Busca el balón en todo el fotograma por teselas de 384 px, en un solo lote en la GPU."""

    prediccion: object
    device: str
    ruta: Path
    motor: str

    @classmethod
    def cargar(cls, modelos: Path, device: str = "cuda", preferir_trt: bool = True) -> "DetectorBalon":
        prediccion, ruta, motor = rfdetr_comun.cargar(QUE, modelos, device, preferir_trt)
        return cls(prediccion, device, ruta, motor)

    def _teselas(self, imagen_bgr: np.ndarray, origenes: list[tuple[int, int]]):
        import torch

        alto, ancho = imagen_bgr.shape[:2]
        fotograma = torch.from_numpy(np.ascontiguousarray(imagen_bgr)).to(self.device, non_blocking=True)
        fotograma = fotograma.flip(-1).permute(2, 0, 1)
        if alto < LADO or ancho < LADO:
            relleno = torch.zeros((3, max(alto, LADO), max(ancho, LADO)), dtype=fotograma.dtype, device=fotograma.device)
            relleno[:, :alto, :ancho] = fotograma
            fotograma = relleno
        lote = torch.stack([fotograma[:, y : y + LADO, x : x + LADO] for x, y in origenes])
        return lote.float().div_(255.0)

    def detectar(self, imagen_bgr: np.ndarray, conf: float, zona: tuple[int, int, int, int] | None = None) -> tuple[np.ndarray, np.ndarray]:
        """Cajas (xyxy, en píxeles del fotograma) y puntuaciones de los balones con puntuación > `conf`. `zona` (x1, y1,
        x2, y2) limita la búsqueda a las teselas que la tocan (p. ej. el campo, sin la grada)."""
        alto, ancho = imagen_bgr.shape[:2]
        origenes = rejilla(ancho, alto)
        if zona is not None:
            x1, y1, x2, y2 = zona
            origenes = [(x, y) for x, y in origenes if x < x2 and x + LADO > x1 and y < y2 and y + LADO > y1] or origenes
        resultados = self.prediccion.predict(self._teselas(imagen_bgr, origenes), threshold=conf, include_source_image=False)
        if not isinstance(resultados, list):
            resultados = [resultados]
        cajas = [r.xyxy for r in resultados]
        puntuaciones = [r.confidence if r.confidence is not None else np.ones(len(r.xyxy)) for r in resultados]
        return a_fotograma(cajas, puntuaciones, origenes)


def evaluar(detector: DetectorBalon, carpetas: list[Path], umbrales: tuple[float, ...] = (0.2, 0.3, 0.4, 0.5, 0.6), informar=print) -> dict:
    """Sobre los fotogramas enteros de validación (los que ningún entrenamiento ha visto): balones encontrados y
    falsos, contando como acierto un balón detectado a menos de su tamaño (mínimo 20 px) del verdadero, igual que se
    midieron los detectores YOLO; y lo mismo quedándose solo con el mejor candidato de cada fotograma."""
    import cv2

    from .metricas import emparejar, leer_etiquetas

    filas = {u: {"encontrados": 0, "falsos": 0, "perdidos": 0, "top1_encontrados": 0, "top1_falsos": 0} for u in umbrales}
    balones = fotogramas = 0
    tiempo = 0.0
    for carpeta in carpetas:
        for ruta in sorted((carpeta / "images" / "val").glob("*.jpg")):
            imagen = cv2.imread(str(ruta))
            if imagen is None:
                continue
            alto, ancho = imagen.shape[:2]
            verdad = np.array([caja for _, caja, _ in leer_etiquetas(carpeta / "labels" / "val" / f"{ruta.stem}.txt", ancho, alto)]).reshape(-1, 4)
            balones += len(verdad)
            fotogramas += 1
            t0 = time.perf_counter()
            cajas, puntuaciones = detector.detectar(imagen, min(umbrales))
            tiempo += time.perf_counter() - t0
            for u in umbrales:
                elegidas = cajas[puntuaciones > u]
                a, f, n = emparejar(elegidas, verdad, 0.0, por_centro=True)
                filas[u]["encontrados"] += a
                filas[u]["falsos"] += f
                filas[u]["perdidos"] += n
                if len(elegidas):
                    mejor = cajas[puntuaciones > u][np.argmax(puntuaciones[puntuaciones > u])][None]
                    a1, f1, _ = emparejar(mejor, verdad, 0.0, por_centro=True)
                    filas[u]["top1_encontrados"] += a1
                    filas[u]["top1_falsos"] += f1
    resumen = {
        "motor": detector.motor,
        "pesos": detector.ruta.name,
        "fotogramas": fotogramas,
        "balones": balones,
        "ms_por_fotograma": round(1000 * tiempo / max(1, fotogramas), 1),
        "umbrales": {
            str(u): {**f, "recall": round(f["encontrados"] / max(1, balones), 3), "precision": round(f["encontrados"] / max(1, f["encontrados"] + f["falsos"]), 3)}
            for u, f in filas.items()
        },
    }
    informar(f"Balón ({detector.motor}): {balones} balones en {fotogramas} fotogramas, {resumen['ms_por_fotograma']} ms por fotograma.")
    for u, f in resumen["umbrales"].items():
        informar(f"  umbral {u}: {f['encontrados']} encontrados ({f['recall']:.0%}), {f['falsos']} falsos · solo el mejor: {f['top1_encontrados']} aciertos, {f['top1_falsos']} falsos")
    return resumen
