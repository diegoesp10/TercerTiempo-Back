"""Afinado de los detectores con el dataset etiquetado (`etiquetado.py`) y evaluación antes/después.

Se parte de los pesos públicos y se sigue entrenando con nuestras imágenes, con el tronco congelado y una tasa de
aprendizaje baja para no olvidar lo aprendido con televisión. Los pesos afinados van a `modelos/afinados/<que>.pt`, de
donde `analizar` los toma automáticamente si existen.

La evaluación mide lo que importa al analizador, no solo el mAP: para el balón, cuántos de los balones de validación se
encuentran a fotograma entero con el umbral del análisis; para las personas, precisión y exhaustividad por clase; para
el campo, en qué parte de los fotogramas se consigue calibrar y con qué error."""

from __future__ import annotations

import json
import shutil
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .campo import CONFIANZA_PUNTO, Calibrador, VERTICES
from .modelos import precision


@dataclass(frozen=True)
class Tarea:
    nombre: str
    carpeta: str
    pesos: str
    imgsz: int
    epocas: int
    # Lote que cabe en 16 GB de GPU con YOLOv8x y AMP (el lote automático de Ultralytics prueba hasta 16 imágenes a
    # 1280 px y revienta la memoria antes de decidir).
    batch: int


TAREAS = {
    "balon": Tarea("balon", "balon", "football-ball-detection.pt", 1280, 20, 4),
    "jugadores": Tarea("jugadores", "personas", "football-player-detection.pt", 1280, 20, 4),
    "campo": Tarea("campo", "campo", "football-pitch-detection.pt", 640, 40, 12),
}


def ruta_afinado(modelos: Path, que: str) -> Path:
    return modelos / "afinados" / f"{que}.pt"


def _yaml_con_mezcla(data: Path, mezclar: list[Path]) -> Path:
    """Un data.yaml que entrena también con otras carpetas de imágenes (p. ej. el dataset público original)."""
    lineas = data.read_text(encoding="utf-8").splitlines()
    extra = ", ".join(p.resolve().as_posix() for p in mezclar)
    lineas = [f"train: [images/train, {extra}]" if l.startswith("train:") else l for l in lineas]
    destino = data.with_name("data-mezcla.yaml")
    destino.write_text("\n".join(lineas) + "\n", encoding="utf-8")
    return destino


def entrenar(que: str, dataset: Path, modelos: Path, salida: Path, epocas: int | None = None, batch: int | None = None, imgsz: int | None = None, device: str = "cuda", congelar: int = 10, paciencia: int = 10, mezclar: list[Path] | None = None, informar=print) -> Path:
    from ultralytics import YOLO

    from .modelos import resolver_device

    tarea = TAREAS[que]
    data = dataset / tarea.carpeta / "data.yaml"
    if not data.exists():
        raise FileNotFoundError(f"No existe {data}: ejecuta antes `tt-analisis etiquetar`.")
    if mezclar:
        data = _yaml_con_mezcla(data, mezclar)
    base = modelos / tarea.pesos
    if not base.exists():
        raise FileNotFoundError(f"Faltan los pesos base {base}: ejecuta `tt-analisis descargar-modelos`.")
    epocas = epocas or tarea.epocas
    imgsz = imgsz or tarea.imgsz
    batch = tarea.batch if batch is None else batch
    device = resolver_device(device)
    informar(f"Afinando {que} desde {base.name}: {epocas} épocas a {imgsz} px, tronco congelado ({congelar} capas), lote {'automático' if batch == -1 else batch}.")
    modelo = YOLO(str(base))
    resultados = modelo.train(
        data=str(data), epochs=epocas, imgsz=imgsz, batch=batch, device=device, project=str(salida), name=que, exist_ok=True,
        freeze=congelar if congelar > 0 else None, optimizer="AdamW", lr0=0.001, lrf=0.1, cos_lr=True, warmup_epochs=1, weight_decay=0.0005,
        mosaic=0.0, close_mosaic=0, scale=0.3, translate=0.1, fliplr=0.5, flipud=0.0, degrees=0.0, hsv_h=0.01, hsv_s=0.5, hsv_v=0.5, erasing=0.0,
        # Pocos procesos de carga (cada uno importa torch y se lleva ~1 GB de RAM; la GPU es el cuello de botella).
        patience=paciencia, amp=True, cache=False, workers=2, plots=True, seed=0, deterministic=False, verbose=True,
    )
    mejor = Path(resultados.save_dir) / "weights" / "best.pt"
    destino = ruta_afinado(modelos, que)
    destino.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(mejor, destino)
    (destino.with_suffix(".json")).write_text(json.dumps({
        "base": tarea.pesos, "dataset": str(data), "epocas": epocas, "imgsz": imgsz, "congelar": congelar,
        "metricas": {k: round(float(v), 4) for k, v in resultados.results_dict.items()}, "carpeta": str(resultados.save_dir),
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    informar(f"Pesos afinados en {destino} ({', '.join(f'{k.split('/')[-1]}={float(v):.3f}' for k, v in resultados.results_dict.items() if 'metrics' in k)})")
    return destino


# ---- Evaluación ----


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


def _iou(a: np.ndarray, b: np.ndarray) -> float:
    x1, y1 = max(a[0], b[0]), max(a[1], b[1])
    x2, y2 = min(a[2], b[2]), min(a[3], b[3])
    inter = max(0.0, x2 - x1) * max(0.0, y2 - y1)
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / union if union > 0 else 0.0


def _emparejar(predichas: np.ndarray, verdaderas: np.ndarray, umbral: float, por_centro: bool) -> tuple[int, int, int]:
    """Aciertos, falsos positivos y falsos negativos con emparejamiento codicioso."""
    libres = list(range(len(verdaderas)))
    aciertos = 0
    for p in predichas:
        mejor, mejor_valor = None, None
        for i in libres:
            v = verdaderas[i]
            if por_centro:
                lado = max(20.0, v[2] - v[0], v[3] - v[1])
                d = np.linalg.norm((p[:2] + p[2:4]) / 2 - (v[:2] + v[2:4]) / 2)
                valor, vale = -d, d <= lado
            else:
                valor = _iou(p[:4], v[:4])
                vale = valor >= umbral
            if vale and (mejor_valor is None or valor > mejor_valor):
                mejor, mejor_valor = i, valor
        if mejor is not None:
            libres.remove(mejor)
            aciertos += 1
    return aciertos, len(predichas) - aciertos, len(libres)


def _imagenes_val(carpeta: Path) -> list[Path]:
    return sorted((carpeta / "images" / "val").glob("*.jpg"))


def _metricas_balon(modelo, carpeta: Path, imgsz: int, device: str, conf: float = 0.25) -> dict:
    import cv2

    tp = fp = fn = 0
    for ruta in _imagenes_val(carpeta):
        imagen = cv2.imread(str(ruta))
        alto, ancho = imagen.shape[:2]
        verdad = np.array([caja for _, caja, _ in leer_etiquetas(carpeta / "labels" / "val" / f"{ruta.stem}.txt", ancho, alto)]).reshape(-1, 4)
        r = modelo(imagen, imgsz=imgsz, conf=conf, device=device, **precision(device), verbose=False)[0]
        pred = r.boxes.xyxy.cpu().numpy().reshape(-1, 4)
        a, f, n = _emparejar(pred, verdad, 0.0, por_centro=True)
        tp, fp, fn = tp + a, fp + f, fn + n
    return {"conf": conf, "balones": tp + fn, "encontrados": tp, "falsos": fp, "recall_fotograma_entero": round(tp / max(1, tp + fn), 3), "precision_fotograma_entero": round(tp / max(1, tp + fp), 3)}


def _metricas_personas(modelo, carpeta: Path, imgsz: int, device: str, conf: float = 0.3) -> dict:
    import cv2

    from .etiquetado import NOMBRES_PERSONAS

    cuentas = {c: [0, 0, 0] for c in range(len(NOMBRES_PERSONAS))}
    for ruta in _imagenes_val(carpeta):
        imagen = cv2.imread(str(ruta))
        alto, ancho = imagen.shape[:2]
        etiquetas = leer_etiquetas(carpeta / "labels" / "val" / f"{ruta.stem}.txt", ancho, alto)
        r = modelo(imagen, imgsz=imgsz, conf=conf, device=device, **precision(device), verbose=False)[0]
        cajas = r.boxes.xyxy.cpu().numpy().reshape(-1, 4)
        clases = r.boxes.cls.cpu().numpy().astype(int)
        for c in cuentas:
            verdad = np.array([caja for clase, caja, _ in etiquetas if clase == c]).reshape(-1, 4)
            pred = cajas[clases == c]
            a, f, n = _emparejar(pred, verdad, 0.5, por_centro=(c == 0))
            cuentas[c][0] += a
            cuentas[c][1] += f
            cuentas[c][2] += n
    salida = {"conf": conf}
    for c, (tp, fp, fn) in cuentas.items():
        salida[NOMBRES_PERSONAS[c]] = {"etiquetas": tp + fn, "recall": round(tp / max(1, tp + fn), 3), "precision": round(tp / max(1, tp + fp), 3)}
    return salida


def _metricas_campo(modelo, carpeta: Path, imgsz: int, device: str) -> dict:
    import cv2
    import supervision as sv

    fotogramas = calibrables = 0
    correctos: list[int] = []
    falsos: list[int] = []
    errores: list[float] = []
    for ruta in _imagenes_val(carpeta):
        imagen = cv2.imread(str(ruta))
        alto, ancho = imagen.shape[:2]
        etiquetas = leer_etiquetas(carpeta / "labels" / "val" / f"{ruta.stem}.txt", ancho, alto)
        if not etiquetas or etiquetas[0][2] is None:
            continue
        fotogramas += 1
        verdad = etiquetas[0][2]
        r = modelo(imagen, imgsz=imgsz, device=device, **precision(device), verbose=False)[0]
        puntos = sv.KeyPoints.from_ultralytics(r)
        if len(puntos) == 0 or puntos.confidence is None:
            correctos.append(0)
            falsos.append(0)
            continue
        xy, conf = puntos.xy[0], puntos.confidence[0]
        vistos = (conf > CONFIANZA_PUNTO) & (xy[:, 0] > 1) & (xy[:, 1] > 1)
        distancias = np.linalg.norm(xy - verdad[:, :2], axis=1)
        bien = vistos & (verdad[:, 2] > 0) & (distancias <= 15.0 * ancho / 1280)
        mal = vistos & ((verdad[:, 2] == 0) | (distancias > 30.0 * ancho / 1280))
        correctos.append(int(bien.sum()))
        falsos.append(int(mal.sum()))
        if vistos.sum() >= 5:
            calibracion = Calibrador._desde_puntos(xy[vistos], VERTICES[vistos], 0.0)
            if calibracion.valida:
                calibrables += 1
                visibles = verdad[:, 2] > 0
                reproyectados = calibracion.homografia.a_imagen(VERTICES[visibles])
                errores.append(float(np.mean(np.linalg.norm(reproyectados - verdad[visibles, :2], axis=1)) * 1280 / ancho))
    return {
        "fotogramas": fotogramas, "pct_calibrables": round(100 * calibrables / max(1, fotogramas), 1),
        "puntos_correctos_media": round(float(np.mean(correctos)), 1) if correctos else 0.0, "puntos_falsos_media": round(float(np.mean(falsos)), 2) if falsos else 0.0,
        "error_homografia_px_mediana": round(float(np.median(errores)), 1) if errores else None,
    }


def evaluar(que: str, dataset: Path, modelos: Path, salida: Path, device: str = "cuda", imgsz: int | None = None, informar=print) -> dict:
    """Base contra afinado sobre la partición de validación: el mAP de Ultralytics y las métricas de uso."""
    from ultralytics import YOLO

    from .modelos import resolver_device

    tarea = TAREAS[que]
    carpeta = dataset / tarea.carpeta
    data = carpeta / "data.yaml"
    device = resolver_device(device)
    imgsz = imgsz or tarea.imgsz
    comparativa: dict[str, dict] = {}
    for etiqueta, pesos in (("base", modelos / tarea.pesos), ("afinado", ruta_afinado(modelos, que))):
        if not pesos.exists():
            continue
        informar(f"Evaluando {que} ({etiqueta}: {pesos.name}) a {imgsz} px…")
        modelo = YOLO(str(pesos))
        r = modelo.val(data=str(data), imgsz=imgsz, batch=8, device=device, split="val", plots=False, verbose=False, project=str(salida), name=f"val-{que}-{etiqueta}", exist_ok=True, **precision(device))
        fila = {"pesos": pesos.name, "imgsz": imgsz, "precision": round(float(r.box.mp), 3), "recall": round(float(r.box.mr), 3), "mAP50": round(float(r.box.map50), 3), "mAP50_95": round(float(r.box.map), 3)}
        if que == "campo":
            fila["pose_mAP50"] = round(float(r.pose.map50), 3)
            fila["pose_mAP50_95"] = round(float(r.pose.map), 3)
            fila["uso"] = {f"{s}px": _metricas_campo(modelo, carpeta, s, device) for s in sorted({640, 1280})}
        elif que == "balon":
            fila["uso"] = _metricas_balon(modelo, carpeta, imgsz, device)
        else:
            fila["uso"] = _metricas_personas(modelo, carpeta, imgsz, device)
        comparativa[etiqueta] = fila
    salida.mkdir(parents=True, exist_ok=True)
    (salida / f"evaluacion-{que}.json").write_text(json.dumps(comparativa, ensure_ascii=False, indent=2), encoding="utf-8")
    informar(resumen_evaluacion(que, comparativa))
    return comparativa


def resumen_evaluacion(que: str, comparativa: dict) -> str:
    lineas = [f"## {que}", "", "| Métrica | Base | Afinado |", "|---|---|---|"]
    base, afinado = comparativa.get("base", {}), comparativa.get("afinado", {})

    def fila(nombre: str, a, b) -> None:
        lineas.append(f"| {nombre} | {'—' if a is None else a} | {'—' if b is None else b} |")

    for m in ("mAP50", "mAP50_95", "precision", "recall", "pose_mAP50", "pose_mAP50_95"):
        if m in base or m in afinado:
            fila(m, base.get(m), afinado.get(m))
    ub, ua = base.get("uso", {}), afinado.get("uso", {})
    if que == "balon":
        fila("Balones encontrados a fotograma entero (conf 0.25)", f"{ub.get('encontrados')}/{ub.get('balones')} ({100 * ub.get('recall_fotograma_entero', 0):.0f} %)" if ub else None, f"{ua.get('encontrados')}/{ua.get('balones')} ({100 * ua.get('recall_fotograma_entero', 0):.0f} %)" if ua else None)
        fila("Falsos balones", ub.get("falsos"), ua.get("falsos"))
    elif que == "jugadores":
        for clase in ("player", "goalkeeper", "referee"):
            fila(f"{clase}: recall / precisión", f"{ub[clase]['recall']} / {ub[clase]['precision']}" if clase in ub else None, f"{ua[clase]['recall']} / {ua[clase]['precision']}" if clase in ua else None)
    else:
        for escala in ("640px", "1280px"):
            b, a = ub.get(escala, {}), ua.get(escala, {})
            fila(f"Calibrables a {escala}", f"{b.get('pct_calibrables')} %" if b else None, f"{a.get('pct_calibrables')} %" if a else None)
            fila(f"Puntos correctos por fotograma a {escala}", b.get("puntos_correctos_media"), a.get("puntos_correctos_media"))
            fila(f"Puntos falsos por fotograma a {escala}", b.get("puntos_falsos_media"), a.get("puntos_falsos_media"))
            fila(f"Error de homografía (px, mediana) a {escala}", b.get("error_homografia_px_mediana"), a.get("error_homografia_px_mediana"))
    return "\n".join(lineas)
