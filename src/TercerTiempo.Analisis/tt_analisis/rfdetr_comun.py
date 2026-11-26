"""Lo común a los detectores RF-DETR (Apache 2.0) del balón, de los jugadores y de los puntos del campo: entrenamiento
y carga para la inferencia.

La inferencia va con PyTorch (BSD) y los pesos de cada detector (`modelos/rfdetr/<que>.pth`), en cualquier GPU o en la
CPU, sin compilar nada para cada equipo. Nada de TensorRT: es gratis, pero su licencia no deja ofrecerlo como servicio
por internet sin permiso de NVIDIA (26-11-2026)."""

from __future__ import annotations

import json
import shutil
import time
from pathlib import Path

CARPETA = "rfdetr"
MOTOR = "pytorch"
# "medium": detector de cajas (balón y jugadores); "puntos": el modelo de puntos clave (campo), que lee el número de
# puntos y las parejas del volteo horizontal del `data.yaml` (kpt_shape y flip_idx).
VARIANTES = {"medium": ("RFDETRMedium", "RF-DETR Medium"), "puntos": ("RFDETRKeypointPreview", "RF-DETR Keypoint")}


def ruta_pesos(modelos: Path, que: str) -> Path:
    return modelos / CARPETA / f"{que}.pth"


def entrenar(
    que: str,
    dataset: Path,
    modelos: Path,
    salida: Path,
    resolucion: int,
    clases: list[str],
    epocas: int,
    lote: int,
    acumular: int,
    lr: float,
    paciencia: int,
    device: str = "cuda",
    datos: dict | None = None,
    informar=print,
    variante: str = "medium",
    opciones: dict | None = None,
) -> Path:
    """Afina RF-DETR (preentrenado en COCO) con un dataset en formato YOLO y deja el mejor checkpoint en
    `modelos/rfdetr/<que>.pth`, con los datos del entrenamiento en `<que>.json`. `opciones` va a la configuración del
    modelo (p. ej. menos consultas) y queda guardada en el checkpoint, que la inferencia lee de ahí."""
    import rfdetr

    clase, nombre = VARIANTES[variante]
    salida.mkdir(parents=True, exist_ok=True)
    informar(f"Entrenando {que} con {nombre} a {resolucion} px sobre {dataset}: {epocas} épocas, lote {lote}×{acumular}.")
    modelo = getattr(rfdetr, clase)(resolution=resolucion, **(opciones or {}))
    inicio = time.time()
    modelo.train(
        dataset_dir=str(dataset),
        dataset_file="yolo",
        epochs=epocas,
        batch_size=lote,
        grad_accum_steps=acumular,
        lr=lr,
        output_dir=str(salida),
        early_stopping=True,
        early_stopping_patience=paciencia,
        class_names=clases,
        num_workers=4,
        device=device,
        tensorboard=False,
        progress_bar="tqdm",
    )
    mejor = mejor_checkpoint(salida)
    destino = ruta_pesos(modelos, que)
    destino.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(mejor, destino)
    resumen = {
        "dataset": str(dataset),
        "checkpoint": str(mejor),
        "variante": variante,
        **({"opciones": opciones} if opciones else {}),
        "resolucion": resolucion,
        "clases": clases,
        **(datos or {}),
        "epocas": epocas,
        **resumen_metricas(salida / "metrics.csv"),
        "lote": lote,
        "acumular": acumular,
        "minutos": round((time.time() - inicio) / 60, 1),
    }
    destino.with_suffix(".json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    informar(f"Pesos de {que} en {destino} (desde {mejor.name}).")
    return destino


def resumen_metricas(metricas: Path) -> dict:
    """Épocas hechas y mejor mAP50-95 (de la media móvil de los pesos, la que guarda el mejor checkpoint) en la
    validación del entrenamiento, del `metrics.csv` que deja RF-DETR; en el modelo de puntos, el de los puntos (OKS)."""
    import csv

    if not metricas.exists():
        return {}
    with metricas.open(encoding="utf-8") as f:
        filas = list(csv.DictReader(f))
    for columnas in (("val/ema_keypoint_map_50_95", "val/keypoint_map_50_95"), ("val/ema_mAP_50_95", "val/mAP_50_95")):
        valores = [float(fila.get(columnas[0]) or fila[columnas[1]]) for fila in filas if fila.get(columnas[0]) or fila.get(columnas[1])]
        if valores:
            return {"epocas_hechas": len(valores), "mejor_map50_95_validacion": round(max(valores), 4)}
    return {}


def mejor_checkpoint(salida: Path) -> Path:
    for nombre in ("checkpoint_best_total.pth", "checkpoint_best_ema.pth", "checkpoint_best_regular.pth", "checkpoint.pth"):
        if (salida / nombre).exists():
            return salida / nombre
    candidatos = sorted(salida.glob("*.pth"), key=lambda p: p.stat().st_mtime)
    if not candidatos:
        raise FileNotFoundError(f"El entrenamiento no dejó ningún checkpoint en {salida}.")
    return candidatos[-1]


def subir(imagen_bgr, device: str):
    """El fotograma como lo esperan los tres detectores: en `device`, (1, 3, alto, ancho), RGB y en [0, 1]. Se sube una
    vez y lo usan los tres (`predict` no lo toca: redimensiona y normaliza una copia). Las teselas del balón salen de él
    con los mismos valores que si se subieran aparte."""
    import numpy as np
    import torch

    fotograma = torch.from_numpy(np.ascontiguousarray(imagen_bgr)).to(device, non_blocking=True)
    return fotograma.flip(-1).permute(2, 0, 1).unsqueeze(0).float().div_(255.0)


class _Grabado:
    """El modelo de inferencia con el paso de una forma de entrada grabado en un grafo CUDA (`grabar`); las demás formas
    van sin grafo."""

    def __init__(self, modelo, grafo, forma: tuple[int, ...]):
        self.modelo, self.grafo, self.forma = modelo, grafo, forma

    def __call__(self, lote):
        return self.grafo(lote) if tuple(lote.shape) == self.forma else self.modelo(lote)


def grabar(prediccion, lote: int, device: str) -> None:
    """Graba en un grafo CUDA el paso del detector para lotes de `lote` imágenes: luego se reproduce con los mismos
    kernels (los mismos números que sin grafo) sin lanzarlos uno a uno desde Python, que en un modelo con poco cálculo
    por imagen es casi todo lo que tarda. Sin trazar ni congelar el modelo, que sí cambiaría los números."""
    import torch
    from rfdetr.detr import _CUDAGraphInferenceModel

    nativo = prediccion._native_model
    forma = (lote, 3, nativo.model.resolution, nativo.model.resolution)
    modelo = nativo.model.inference_model
    # El transformer guarda en la GPU las medidas de cada nivel (los mismos números) en vez de subirlas en cada paso,
    # que dentro de un grafo no se puede.
    for modulo in modelo.modules():
        if hasattr(modulo, "enable_cuda_graph_capture"):
            modulo.enable_cuda_graph_capture()
    muestra = torch.zeros(forma, dtype=nativo._optimized_dtype, device=device)
    nativo.model.inference_model = _Grabado(modelo, _CUDAGraphInferenceModel(modelo, muestra, torch.device(device)), forma)


def cargar(que: str, modelos: Path, device: str = "cuda") -> tuple[object, Path]:
    """(predictor con `.predict` y `.class_names`, archivo de pesos cargado)."""
    import torch
    from rfdetr import RFDETR

    pesos = ruta_pesos(modelos, que)
    if not pesos.exists():
        raise FileNotFoundError(f"Falta el detector de {que}: no hay {pesos.name} en {pesos.parent}.")
    # Copia optimizada para inferencia, sin compilar (el lote puede cambiar entre llamadas); en la GPU, en FP16.
    modelo = RFDETR.from_checkpoint(str(pesos), device=device)
    prediccion = modelo.inference(compile=False, dtype=torch.float16 if device.startswith("cuda") else torch.float32)
    return prediccion, pesos
