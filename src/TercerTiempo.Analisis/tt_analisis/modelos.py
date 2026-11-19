"""Los tres detectores (jugadores, balón y puntos del campo) y el lector de dorsales: descarga y carga en la GPU.

Los tres detectores son RF-DETR (Apache 2.0) entrenados con nuestros partidos (`detector_balon.py`,
`detector_jugadores.py`, `detector_campo.py`) y no se descargan: están en `modelos/rfdetr/`. Lo que se descarga es el
lector de dorsales (PARSeq) y los clips de muestra."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

VIDEOS_MUESTRA = {
    "0bfacc_0.mp4": "12TqauVZ9tLAv8kWxTTBFWtgt2hNQ4_ZF",
    "2e57b9_0.mp4": "19PGw55V8aA6GZu5-Aac5_9mCy3fNxmEf",
}


def descargar_modelos(carpeta: Path, con_videos: bool = False) -> list[Path]:
    import gdown

    carpeta.mkdir(parents=True, exist_ok=True)
    rutas = [descargar_lector_dorsales(carpeta)]
    if con_videos:
        for archivo, id_drive in VIDEOS_MUESTRA.items():
            destino = carpeta / archivo
            if not destino.exists():
                print(f"Descargando el vídeo de muestra {archivo}…")
                gdown.download(id=id_drive, output=str(destino), quiet=False)
    return rutas


def descargar_lector_dorsales(carpeta: Path) -> Path:
    """El lector de dorsales (PARSeq, Apache-2.0) desde las versiones publicadas de su repositorio. Su nombre lleva el
    principio del SHA-256 del archivo (como hace torch.hub): si no cuadra, se borra y falla."""
    import hashlib
    import urllib.request

    from .dorsales import PESOS_PARSEQ, URL_PARSEQ

    destino = carpeta / PESOS_PARSEQ
    if destino.exists():
        return destino
    print("Descargando el lector de dorsales (PARSeq)…")
    temporal = destino.with_suffix(".part")
    urllib.request.urlretrieve(URL_PARSEQ, temporal)
    esperado = PESOS_PARSEQ.rsplit("-", 1)[1].split(".")[0]
    if not hashlib.sha256(temporal.read_bytes()).hexdigest().startswith(esperado):
        temporal.unlink(missing_ok=True)
        raise RuntimeError(f"El lector de dorsales descargado no es el esperado (SHA-256 sin {esperado} al principio).")
    temporal.replace(destino)
    return destino


@dataclass
class Modelos:
    jugadores: object
    balon: object
    campo: object
    device: str
    # Qué archivo de pesos se ha cargado para cada detector (relativo a la carpeta de modelos).
    rutas: dict[str, str] = field(default_factory=dict)


def cargar_modelos(carpeta: Path, device: str) -> Modelos:
    from .detector_balon import DetectorBalon
    from .detector_campo import DetectorCampo
    from .detector_jugadores import DetectorJugadores

    device = resolver_device(device)
    jugadores = DetectorJugadores.cargar(carpeta, device)
    balon = DetectorBalon.cargar(carpeta, device)
    campo = DetectorCampo.cargar(carpeta, device)
    rutas = {"jugadores": jugadores.ruta, "balon": balon.ruta, "campo": campo.ruta}
    return Modelos(jugadores, balon, campo, device, rutas={nombre: ruta.relative_to(carpeta).as_posix() for nombre, ruta in rutas.items()})


def resolver_device(device: str) -> str:
    import torch

    if device.startswith("cuda") and not torch.cuda.is_available():
        print("Aviso: no hay CUDA disponible; se analiza en CPU (mucho más lento).")
        return "cpu"
    return device


class SinGpu(RuntimeError):
    """Se pidió GPU (--exigir-gpu) y torch no ve ninguna."""


def exigir_gpu(device: str) -> None:
    import torch

    if device.startswith("cuda") and not torch.cuda.is_available():
        raise SinGpu("No hay GPU CUDA disponible (torch no la ve): no se analiza en CPU porque se pidió --exigir-gpu.")


def describir_gpu() -> str:
    import torch

    if not torch.cuda.is_available():
        return "Sin GPU CUDA (torch no la ve)"
    nombre = torch.cuda.get_device_name(0)
    memoria = torch.cuda.get_device_properties(0).total_memory / 2**30
    return f"{nombre} ({memoria:.0f} GB) · torch {torch.__version__} · CUDA {torch.version.cuda}"
