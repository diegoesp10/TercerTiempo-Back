"""El detector de los 32 puntos del campo con el RF-DETR de puntos clave (Apache 2.0), en lugar del YOLOv8 de pose de
Roboflow (Ultralytics, AGPL-3.0). Devuelve lo mismo que daba aquel: la posición de cada vértice en el fotograma y la
confianza de que se vea (la probabilidad de que el punto «se encuentre» en la imagen), así que la calibración
(`campo.py`) no cambia: los puntos con confianza > 0,5 y la homografía con RANSAC.

Se entrena por destilación (`dataset_campo.py`): aprende a ver lo que veía el YOLO público en nuestros partidos."""

from __future__ import annotations

import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from . import rfdetr_comun
from .dataset_campo import CLASES

QUE = "campo"
# Múltiplo de 24 (parche de 12 por dos ventanas). El YOLO del campo trabajaba a 640: a más resolución se inventaba puntos.
RESOLUCION = 768
# Puntuación mínima del «campo» (la caja que agrupa los puntos) para mirar sus puntos.
CONF_CAMPO = 0.1
NUM_PUNTOS = 32
# En cada fotograma hay un solo campo: 20 consultas en vez de 100. Cada consulta lleva una ficha por punto en el
# decodificador (y en el entrenamiento hay 13 grupos de consultas), así que con 100 y 32 puntos no cabía en 16 GB.
CONSULTAS = 20


def entrenar(dataset: Path, modelos: Path, salida: Path, resolucion: int = RESOLUCION, epocas: int = 40, lote: int = 4, acumular: int = 4, lr: float = 1e-4, paciencia: int = 8, device: str = "cuda", informar=print) -> Path:
    opciones = {"num_queries": CONSULTAS, "num_select": CONSULTAS}
    return rfdetr_comun.entrenar(QUE, dataset, modelos, salida, resolucion, CLASES, epocas, lote, acumular, lr, paciencia, device, {"puntos": NUM_PUNTOS}, informar, variante="puntos", opciones=opciones)


@dataclass
class DetectorCampo:
    prediccion: object
    device: str
    ruta: Path
    motor: str = rfdetr_comun.MOTOR

    @classmethod
    def cargar(cls, modelos: Path, device: str = "cuda") -> "DetectorCampo":
        prediccion, ruta = rfdetr_comun.cargar(QUE, modelos, device)
        return cls(prediccion, device, ruta)

    def _tensor(self, imagen_bgr: np.ndarray):
        import torch

        fotograma = torch.from_numpy(np.ascontiguousarray(imagen_bgr)).to(self.device, non_blocking=True)
        return fotograma.flip(-1).permute(2, 0, 1).unsqueeze(0).float().div_(255.0)

    def detectar(self, imagen_bgr: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray] | None:
        """(caja xyxy, puntos (32, 2) en píxeles, confianza de cada punto (32,)) del campo más claro; None si no ve
        ninguno."""
        resultado = self.prediccion.predict(self._tensor(imagen_bgr), threshold=CONF_CAMPO, include_source_image=False)
        if isinstance(resultado, list):
            resultado = resultado[0]
        if resultado is None or len(resultado) == 0:
            return None
        puntuaciones = resultado.detection_confidence if getattr(resultado, "detection_confidence", None) is not None else np.ones(len(resultado))
        i = int(np.argmax(puntuaciones))
        xy = np.asarray(resultado.xy[i], dtype=np.float32)
        conf = np.asarray(resultado.keypoint_confidence[i], dtype=np.float32)
        caja = np.asarray(resultado.data["xyxy"][i], dtype=np.float32) if "xyxy" in resultado.data else None
        return caja, xy, conf

    def puntos(self, imagen_bgr: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """Los 32 vértices con su confianza (ceros si no ve el campo)."""
        detectado = self.detectar(imagen_bgr)
        if detectado is None:
            return np.zeros((NUM_PUNTOS, 2), dtype=np.float32), np.zeros(NUM_PUNTOS, dtype=np.float32)
        return detectado[1], detectado[2]


def evaluar(puntos, carpetas: list[Path], nombre: str, informar=print) -> dict:
    """Sobre los fotogramas de validación de uno o varios datasets de pose del campo (`campo/` de `etiquetar`, con solo
    los puntos coherentes; o el de `preparar-campo`, con todos los del maestro): fotogramas calibrables (5 o más puntos
    con confianza > 0,5 y una homografía que cuadra), puntos correctos (a menos de 15 px a escala 1280 de una
    etiqueta) y falsos (sin etiqueta o a más de 30 px), y error de la homografía contra las etiquetas. `puntos` es una
    función imagen → (xy, conf), para medir igual cualquier detector."""
    import cv2

    from .campo import CONFIANZA_PUNTO, VERTICES, Calibrador
    from .metricas import leer_etiquetas

    por_carpeta = {}
    for carpeta in carpetas:
        fotogramas = calibrables = 0
        correctos: list[int] = []
        falsos: list[int] = []
        errores: list[float] = []
        tiempo = 0.0
        for ruta in sorted((carpeta / "images" / "val").glob("*.jpg")):
            imagen = cv2.imread(str(ruta))
            if imagen is None:
                continue
            alto, ancho = imagen.shape[:2]
            etiquetas = leer_etiquetas(carpeta / "labels" / "val" / f"{ruta.stem}.txt", ancho, alto)
            if not etiquetas or etiquetas[0][2] is None:
                continue
            fotogramas += 1
            verdad = etiquetas[0][2]
            t0 = time.perf_counter()
            xy, conf = puntos(imagen)
            tiempo += time.perf_counter() - t0
            vistos = (conf > CONFIANZA_PUNTO) & (xy[:, 0] > 1) & (xy[:, 1] > 1)
            distancias = np.linalg.norm(xy - verdad[:, :2], axis=1)
            escala = ancho / 1280
            correctos.append(int((vistos & (verdad[:, 2] > 0) & (distancias <= 15.0 * escala)).sum()))
            falsos.append(int((vistos & ((verdad[:, 2] == 0) | (distancias > 30.0 * escala))).sum()))
            if vistos.sum() >= 5:
                calibracion = Calibrador._desde_puntos(xy[vistos], VERTICES[vistos], 0.0)
                if calibracion.valida:
                    calibrables += 1
                    visibles = verdad[:, 2] > 0
                    reproyectados = calibracion.homografia.a_imagen(VERTICES[visibles])
                    errores.append(float(np.mean(np.linalg.norm(reproyectados - verdad[visibles, :2], axis=1)) / escala))
        por_carpeta[str(carpeta)] = {
            "fotogramas": fotogramas,
            "pct_calibrables": round(100 * calibrables / max(1, fotogramas), 1),
            "puntos_correctos_media": round(float(np.mean(correctos)), 2) if correctos else 0.0,
            "puntos_falsos_media": round(float(np.mean(falsos)), 2) if falsos else 0.0,
            "error_homografia_px_mediana": round(float(np.median(errores)), 1) if errores else None,
            "ms_por_fotograma": round(1000 * tiempo / max(1, fotogramas), 1),
        }
        r = por_carpeta[str(carpeta)]
        informar(
            f"{nombre} en {carpeta}: {r['fotogramas']} fotogramas, {r['pct_calibrables']} % calibrables, {r['puntos_correctos_media']} puntos "
            f"correctos y {r['puntos_falsos_media']} falsos por fotograma, error {r['error_homografia_px_mediana']} px, {r['ms_por_fotograma']} ms."
        )
    return {"detector": nombre, "datasets": por_carpeta}
