"""El detector de personas (porteros, jugadores y árbitros) con RF-DETR (Apache 2.0), en lugar del YOLOv8 de Roboflow
(Ultralytics, AGPL-3.0). Mira el fotograma entero de una vez, reescalado a un cuadrado de `RESOLUCION` px (los jugadores
lejanos de un 1080p miden unos 35 px de alto: a 1280 siguen siendo bastante más que un parche de 16 px). Las clases que
devuelve son las de siempre (`CLASE_PORTERO`, `CLASE_JUGADOR`, `CLASE_ARBITRO`), así que el resto del análisis no cambia."""

from __future__ import annotations

import json
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import supervision as sv

from . import rfdetr_comun
from .config import CLASE_ARBITRO, CLASE_JUGADOR, CLASE_PORTERO
from .dataset_jugadores import CLASES

QUE = "jugadores"
RESOLUCION = 1280
CLASE_POR_NOMBRE = {"goalkeeper": CLASE_PORTERO, "player": CLASE_JUGADOR, "referee": CLASE_ARBITRO}


def entrenar(dataset: Path, modelos: Path, salida: Path, resolucion: int = RESOLUCION, epocas: int = 30, lote: int = 2, acumular: int = 8, lr: float = 1e-4, paciencia: int = 6, device: str = "cuda", informar=print) -> Path:
    return rfdetr_comun.entrenar(QUE, dataset, modelos, salida, resolucion, CLASES, epocas, lote, acumular, lr, paciencia, device, None, informar)


def resolucion_entrenada(modelos: Path) -> int:
    datos = rfdetr_comun.ruta_pesos(modelos, QUE).with_suffix(".json")
    if datos.exists():
        return int(json.loads(datos.read_text(encoding="utf-8")).get("resolucion", RESOLUCION))
    return RESOLUCION


def exportar_tensorrt(modelos: Path, informar=print) -> Path:
    """Un fotograma por llamada: lote fijo de 1."""
    return rfdetr_comun.exportar_tensorrt(QUE, modelos, resolucion_entrenada(modelos), 1, 1, informar)


def clases_del_modelo(nombres) -> np.ndarray:
    """De los índices de clase del modelo a las clases del análisis (-1 si no es ninguna de las tres)."""
    if isinstance(nombres, dict):
        nombres = [nombres[k] for k in sorted(nombres, key=int)]
    return np.array([CLASE_POR_NOMBRE.get(str(n), -1) for n in nombres], dtype=int)


@dataclass
class DetectorJugadores:
    prediccion: object
    device: str
    ruta: Path
    motor: str
    clases: np.ndarray

    @classmethod
    def cargar(cls, modelos: Path, device: str = "cuda", preferir_trt: bool = True) -> "DetectorJugadores":
        prediccion, ruta, motor = rfdetr_comun.cargar(QUE, modelos, device, preferir_trt)
        return cls(prediccion, device, ruta, motor, clases_del_modelo(prediccion.class_names))

    def _tensor(self, imagen_bgr: np.ndarray):
        import torch

        fotograma = torch.from_numpy(np.ascontiguousarray(imagen_bgr)).to(self.device, non_blocking=True)
        return fotograma.flip(-1).permute(2, 0, 1).unsqueeze(0).float().div_(255.0)

    def detectar(self, imagen_bgr: np.ndarray, conf: float) -> sv.Detections:
        """Las personas con puntuación > `conf`, con las clases del análisis y en píxeles del fotograma."""
        resultado = self.prediccion.predict(self._tensor(imagen_bgr), threshold=conf, include_source_image=False)
        if isinstance(resultado, list):
            resultado = resultado[0]
        if len(resultado) == 0:
            return sv.Detections.empty()
        indices = np.asarray(resultado.class_id, dtype=int)
        clases = np.where((indices >= 0) & (indices < len(self.clases)), self.clases[np.clip(indices, 0, len(self.clases) - 1)], -1)
        conocidas = clases >= 0
        confianza = resultado.confidence if resultado.confidence is not None else np.ones(len(resultado), dtype=np.float32)
        return sv.Detections(
            xyxy=np.asarray(resultado.xyxy, dtype=np.float32)[conocidas],
            confidence=np.asarray(confianza, dtype=np.float32)[conocidas],
            class_id=clases[conocidas],
        )


def evaluar(detector: DetectorJugadores, carpetas: list[Path], umbrales: tuple[float, ...] = (0.2, 0.3, 0.4, 0.5), iou: float = 0.5, informar=print) -> dict:
    """Sobre los fotogramas de validación de `personas/` (los que ningún entrenamiento ha visto): personas encontradas
    (IoU ≥ 0,5 con una etiqueta) y sobrantes, y de las encontradas cuántas con la misma clase. Ojo: las etiquetas son
    del detector YOLO de Roboflow, así que esto mide cuánto se parece a él, no cuánto acierta; lo que encuentra de más
    hay que mirarlo (`tt-analisis` deja las cuentas, no las imágenes)."""
    import cv2

    from .dataset_jugadores import NUEVA_CLASE
    from .metricas import emparejar_pares, leer_etiquetas

    filas ={u: {"encontradas": 0, "sobrantes": 0, "perdidas": 0, "misma_clase": 0, "por_clase": {c: [0, 0] for c in CLASES}} for u in umbrales}
    personas = fotogramas = 0
    tiempo = 0.0
    for carpeta in carpetas:
        for ruta in sorted((carpeta / "images" / "val").glob("*.jpg")):
            imagen = cv2.imread(str(ruta))
            if imagen is None:
                continue
            alto, ancho = imagen.shape[:2]
            etiquetas = leer_etiquetas(carpeta / "labels" / "val" / f"{ruta.stem}.txt", ancho, alto)
            # Las etiquetas pueden venir de `etiquetar` (clases de Roboflow) o de `preparar-jugadores` (las del dataset).
            verdad = [(c if carpeta.name != "personas" else NUEVA_CLASE.get(c, -1), caja) for c, caja, _ in etiquetas]
            verdad = [(c, caja) for c, caja in verdad if 0 <= c < len(CLASES)]
            cajas_verdad = np.array([caja for _, caja in verdad]).reshape(-1, 4)
            personas += len(verdad)
            fotogramas += 1
            t0 = time.perf_counter()
            detectadas = detector.detectar(imagen, min(umbrales))
            tiempo += time.perf_counter() - t0
            orden = np.argsort(-detectadas.confidence) if len(detectadas) else np.zeros(0, dtype=int)
            detectadas = detectadas[orden] if len(detectadas) else detectadas
            for u in umbrales:
                elegidas = detectadas[detectadas.confidence > u] if len(detectadas) else detectadas
                pares = emparejar_pares(elegidas.xyxy if len(elegidas) else np.zeros((0, 4)), cajas_verdad, iou, por_centro=False)
                filas[u]["encontradas"] += len(pares)
                filas[u]["sobrantes"] += len(elegidas) - len(pares)
                filas[u]["perdidas"] += len(verdad) - len(pares)
                for k, i in pares:
                    clase_verdad = verdad[i][0]
                    nombre = CLASES[clase_verdad]
                    filas[u]["por_clase"][nombre][1] += 1
                    if NUEVA_CLASE.get(int(elegidas.class_id[k])) == clase_verdad:
                        filas[u]["misma_clase"] += 1
                        filas[u]["por_clase"][nombre][0] += 1
    resumen = {
        "motor": detector.motor,
        "pesos": detector.ruta.name,
        "fotogramas": fotogramas,
        "personas": personas,
        "ms_por_fotograma": round(1000 * tiempo / max(1, fotogramas), 1),
        "umbrales": {
            str(u): {
                "encontradas": f["encontradas"],
                "sobrantes": f["sobrantes"],
                "perdidas": f["perdidas"],
                "recall": round(f["encontradas"] / max(1, personas), 3),
                "precision": round(f["encontradas"] / max(1, f["encontradas"] + f["sobrantes"]), 3),
                "misma_clase": round(f["misma_clase"] / max(1, f["encontradas"]), 3),
                "misma_clase_por_clase": {c: f"{a}/{n}" for c, (a, n) in f["por_clase"].items()},
            }
            for u, f in filas.items()
        },
    }
    informar(f"Personas ({detector.motor}): {personas} en {fotogramas} fotogramas, {resumen['ms_por_fotograma']} ms por fotograma.")
    for u, f in resumen["umbrales"].items():
        informar(
            f"  umbral {u}: {f['encontradas']} encontradas ({f['recall']:.0%}), {f['sobrantes']} de más; clase igual en el {f['misma_clase']:.0%} "
            f"({', '.join(f'{c} {v}' for c, v in f['misma_clase_por_clase'].items())})"
        )
    return resumen
