"""Lo común a los detectores RF-DETR (Apache 2.0) del balón, de los jugadores y de los puntos del campo: entrenamiento,
exportación a TensorRT y carga para la inferencia.

En el análisis se usa el motor TensorRT (`modelos/rfdetr/<que>.trt`) si existe, carga y da siempre lo mismo con la misma
imagen: va compilado para la GPU de este equipo y para la versión de TensorRT instalada, así que en otra GPU o en Docker
hay que volver a exportarlo. Si no, se usan los pesos de PyTorch (`<que>.pth`), más lentos pero portables y deterministas.
Dos análisis del mismo vídeo tienen que salir iguales, y el motor del campo en FP16 no lo cumple (ver `da_siempre_lo_mismo`)."""

from __future__ import annotations

import json
import shutil
import time
from pathlib import Path

import numpy as np

CARPETA = "rfdetr"
# La sonda de cada motor TensorRT: la misma imagen de ruido (siempre con la misma semilla), con un umbral casi cero para
# que salgan todas las consultas y no solo las que pasarían el del análisis. Con ella, el motor del campo en FP16 falla
# las tres repeticiones y los de jugadores y balón no fallan nunca, igual que con fotogramas reales (25-11-2026).
LADO_SONDA = 640
REPETICIONES_SONDA = 3
UMBRAL_SONDA = 0.001
# "medium": detector de cajas (balón y jugadores); "puntos": el modelo de puntos clave (campo), que lee el número de
# puntos y las parejas del volteo horizontal del `data.yaml` (kpt_shape y flip_idx).
VARIANTES = {"medium": ("RFDETRMedium", "RF-DETR Medium"), "puntos": ("RFDETRKeypointPreview", "RF-DETR Keypoint")}


def ruta_pesos(modelos: Path, que: str) -> Path:
    return modelos / CARPETA / f"{que}.pth"


def ruta_motor(modelos: Path, que: str) -> Path:
    return modelos / CARPETA / f"{que}.trt"


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


def exportar_tensorrt(que: str, modelos: Path, resolucion: int, lote: int, lote_max: int, informar=print) -> Path:
    """Compila el detector para la GPU de este equipo: FP16, con lote dinámico de 1 a `lote_max` afinado para `lote`."""
    from rfdetr import RFDETR

    pesos = ruta_pesos(modelos, que)
    if not pesos.exists():
        raise FileNotFoundError(f"No hay pesos de {que} en {pesos}: entrena antes con `tt-analisis entrenar-{que}`.")
    modelo = RFDETR.from_checkpoint(str(pesos))
    salida = pesos.parent / f"export-{que}"
    informar(f"Exportando {pesos.name} a TensorRT (FP16, lote 1-{lote_max}, afinado para {lote})…")
    motor = modelo.export(
        output_dir=str(salida),
        format="tensorrt",
        shape=(resolucion, resolucion),
        batch_size=lote,
        dynamic_batch=lote_max > 1,
        max_batch_size=lote_max,
        fp16=True,
        trt_metadata=True,
        verbose=False,
    )
    destino = ruta_motor(modelos, que)
    motor = Path(motor)
    shutil.copy2(motor, destino)
    # Junto al motor van sus metadatos (`.trt.rfdetr.json`, que lee la inferencia) y su descripción (`.json`, que
    # queda como `<que>.trt.json` para no pisar `<que>.json`, los datos del entrenamiento).
    for extra in motor.parent.glob(motor.stem + "*.json"):
        resto = extra.name[len(motor.stem):]
        shutil.copy2(extra, destino.parent / ((destino.stem if resto.startswith(".trt") else destino.name) + resto))
    shutil.rmtree(salida, ignore_errors=True)
    informar(f"Motor TensorRT en {destino}.")
    from rfdetr import RFDETRInference

    if da_siempre_lo_mismo(RFDETRInference(str(destino), device="cuda"), entrada_sonda("cuda")):
        informar("Da siempre lo mismo con la misma imagen: el análisis lo usará.")
    else:
        informar("Ojo: no da siempre lo mismo con la misma imagen, así que el análisis no lo usará y seguirá con los pesos de PyTorch.")
    return destino


def entrada_sonda(device: str):
    import torch

    ruido = np.random.RandomState(0).randint(0, 256, (1, 3, LADO_SONDA, LADO_SONDA), dtype=np.uint8)
    return torch.from_numpy(ruido).to(device).float().div_(255.0)


def da_siempre_lo_mismo(prediccion, entrada, repeticiones: int = REPETICIONES_SONDA) -> bool:
    """Si el predictor devuelve exactamente lo mismo, bit a bit, cada vez que ve la misma imagen.

    El motor TensorRT del campo en FP16 no lo hace: con la misma imagen, la confianza de un punto cambia hasta 0,24 y su
    posición hasta 136 px de una llamada a otra (en FP32 sí da lo mismo, pero tarda más que PyTorch en FP16). Como la
    calibración arrastra cada diferencia de un fotograma al siguiente, dos pasadas del mismo vídeo salían distintas: en
    el J8 (20:00-21:30), el campo calibrado entre el 64 y el 83 % y la posesión de 62/38 a 41/59."""
    base = salidas_en_bruto(prediccion.predict(entrada, threshold=UMBRAL_SONDA, include_source_image=False))
    for _ in range(repeticiones):
        otra = salidas_en_bruto(prediccion.predict(entrada, threshold=UMBRAL_SONDA, include_source_image=False))
        if len(otra) != len(base) or not all(_iguales(a, b) for a, b in zip(base, otra)):
            return False
    return True


def salidas_en_bruto(resultado) -> list[np.ndarray]:
    """Todo lo que devuelve una predicción (cajas o puntos, puntuaciones, clases y sus datos), de una imagen o de un lote."""
    if isinstance(resultado, (list, tuple)):
        return [a for r in resultado for a in salidas_en_bruto(r)]
    # De los puntos clave, la confianza es `keypoint_confidence` (`confidence` está en desuso y avisa al leerla).
    nombres = ("xy", "keypoint_confidence", "class_id") if hasattr(resultado, "keypoint_confidence") else ("xyxy", "confidence", "class_id")
    arrays = [np.asarray(v) for v in (getattr(resultado, n, None) for n in nombres) if v is not None]
    datos = getattr(resultado, "data", None) or {}
    return arrays + [np.asarray(datos[k]) for k in sorted(datos)]


def _iguales(a: np.ndarray, b: np.ndarray) -> bool:
    # Por sus bytes: así también son iguales dos NaN que salen de la misma cuenta.
    if a.shape != b.shape or a.dtype != b.dtype:
        return False
    if a.dtype == object:
        return a.tolist() == b.tolist()
    return a.tobytes() == b.tobytes()


def cargar(que: str, modelos: Path, device: str = "cuda", preferir_trt: bool = True) -> tuple[object, Path, str]:
    """(predictor con `.predict` y `.class_names`, archivo cargado, "tensorrt" o "pytorch"). El motor TensorRT, solo si
    da siempre lo mismo con la misma imagen (`da_siempre_lo_mismo`)."""
    import torch
    from rfdetr import RFDETR, RFDETRInference

    motor = ruta_motor(modelos, que)
    if preferir_trt and device.startswith("cuda") and motor.exists():
        try:
            prediccion = RFDETRInference(str(motor), device=device)
        except Exception as error:
            # Un motor compilado en otra GPU, otro sistema u otra versión de TensorRT no carga: se sigue con PyTorch.
            print(f"Aviso: no se pudo cargar el motor TensorRT de {que} ({error}); se usa PyTorch. Vuelve a compilarlo en este equipo con `tt-analisis exportar-{que}`.")
        else:
            if da_siempre_lo_mismo(prediccion, entrada_sonda(device)):
                return prediccion, motor, "tensorrt"
            print(f"El motor TensorRT de {que} no da siempre lo mismo con la misma imagen: se usa PyTorch, para que dos análisis del mismo vídeo salgan iguales.")
            del prediccion
    pesos = ruta_pesos(modelos, que)
    if not pesos.exists():
        raise FileNotFoundError(f"Falta el detector de {que}: ni {motor.name} ni {pesos.name} en {pesos.parent}.")
    # Copia optimizada para inferencia, sin compilar (el lote puede cambiar entre llamadas).
    modelo = RFDETR.from_checkpoint(str(pesos), device=device)
    prediccion = modelo.inference(compile=False, dtype=torch.float16 if device.startswith("cuda") else torch.float32)
    return prediccion, pesos, "pytorch"
