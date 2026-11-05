"""Los tres detectores (jugadores, balón y puntos del campo): descarga y carga en la GPU.

Son los pesos YOLOv8 que publica Roboflow con su ejemplo de fútbol (entrenados con vídeo de televisión); el plan es
afinarlos con partidos asturianos. Ultralytics es AGPL-3.0: antes de publicar hay licencia comercial o cambio a RF-DETR."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

PESOS = {
    "jugadores": ("football-player-detection.pt", "17PXFNlx-jI7VjVo_vQnB1sONjRyvoB-q"),
    "balon": ("football-ball-detection.pt", "1isw4wx-MK9h9LMr36VvIWlJD6ppUvw7V"),
    "campo": ("football-pitch-detection.pt", "1Ma5Kt86tgpdjCTKfum79YMgNnSjcoOyf"),
}

VIDEOS_MUESTRA = {
    "0bfacc_0.mp4": "12TqauVZ9tLAv8kWxTTBFWtgt2hNQ4_ZF",
    "2e57b9_0.mp4": "19PGw55V8aA6GZu5-Aac5_9mCy3fNxmEf",
}


def descargar_modelos(carpeta: Path, con_videos: bool = False) -> list[Path]:
    import gdown

    carpeta.mkdir(parents=True, exist_ok=True)
    rutas = []
    for nombre, (archivo, id_drive) in PESOS.items():
        destino = carpeta / archivo
        if not destino.exists():
            print(f"Descargando el detector de {nombre}…")
            gdown.download(id=id_drive, output=str(destino), quiet=False)
        rutas.append(destino)
    if con_videos:
        for archivo, id_drive in VIDEOS_MUESTRA.items():
            destino = carpeta / archivo
            if not destino.exists():
                print(f"Descargando el vídeo de muestra {archivo}…")
                gdown.download(id=id_drive, output=str(destino), quiet=False)
    return rutas


@dataclass
class Modelos:
    jugadores: object
    balon: object
    campo: object
    device: str
    # Media precisión (FP16) en GPU: casi el doble de rápido sin perder detecciones apreciables.
    half: bool = False


def cargar_modelos(carpeta: Path, device: str) -> Modelos:
    from ultralytics import YOLO

    faltan = [archivo for archivo, _ in PESOS.values() if not (carpeta / archivo).exists()]
    if faltan:
        raise FileNotFoundError(f"Faltan pesos en {carpeta}: {', '.join(faltan)}. Ejecuta `tt-analisis descargar-modelos`.")
    device = resolver_device(device)
    modelos = Modelos(
        YOLO(str(carpeta / PESOS["jugadores"][0])),
        YOLO(str(carpeta / PESOS["balon"][0])),
        YOLO(str(carpeta / PESOS["campo"][0])),
        device,
        half=device.startswith("cuda"),
    )
    for m in (modelos.jugadores, modelos.balon, modelos.campo):
        m.to(device)
    return modelos


def resolver_device(device: str) -> str:
    import torch

    if device.startswith("cuda") and not torch.cuda.is_available():
        print("Aviso: no hay CUDA disponible; se analiza en CPU (mucho más lento).")
        return "cpu"
    return device


def describir_gpu() -> str:
    import torch

    if not torch.cuda.is_available():
        return "Sin GPU CUDA (torch no la ve)"
    nombre = torch.cuda.get_device_name(0)
    memoria = torch.cuda.get_device_properties(0).total_memory / 2**30
    return f"{nombre} ({memoria:.0f} GB) · torch {torch.__version__} · CUDA {torch.version.cuda}"
