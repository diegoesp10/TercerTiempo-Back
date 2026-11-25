"""Los tres detectores (jugadores, balón y puntos del campo) y el lector de dorsales: descarga y carga en la GPU.

Los tres detectores son RF-DETR (Apache 2.0) entrenados con nuestros partidos (`detector_balon.py`,
`detector_jugadores.py`, `detector_campo.py`) y no se descargan: están en `modelos/rfdetr/`. Lo que se descarga es el
lector de dorsales (PARSeq) y los clips de muestra."""

from __future__ import annotations

import hashlib
import json
import re
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


MOTORES = {"tensorrt": "TensorRT", "pytorch": "PyTorch"}


def describir_modelos(modelos: Modelos, carpeta: Path, lector=None, apariencia=None) -> list[dict]:
    """Con qué modelo se hizo cada cosa, para que cada análisis lo diga: la arquitectura, el entrenamiento del que
    salieron los pesos (la carpeta del entrenamiento, que lleva la fecha), el motor y la huella (el principio del SHA-256
    del archivo cargado: dos análisis con la misma huella usaron exactamente los mismos pesos)."""
    from .rfdetr_comun import CARPETA, VARIANTES

    descritos = []
    for que in ("jugadores", "balon", "campo"):
        detector = getattr(modelos, que)
        datos = _leer_json(carpeta / CARPETA / f"{que}.json")
        descritos.append({
            "que": que,
            "modelo": VARIANTES.get(str(datos.get("variante", "medium")), ("", "RF-DETR"))[1],
            "entrenamiento": entrenamiento(datos.get("checkpoint")),
            "motor": MOTORES.get(detector.motor, detector.motor),
            "huella": huella(detector.ruta),
        })
    ruta = getattr(lector, "ruta", None)
    if ruta is not None:
        descritos.append({
            "que": "dorsales",
            "modelo": "PARSeq",
            "entrenamiento": "afinado con nuestros partidos" if ruta.parent.name == "afinados" else "publicado",
            "motor": "PyTorch",
            "huella": huella(ruta),
        })
    ruta = getattr(apariencia, "ruta", None)
    if ruta is not None:
        descritos.append({
            "que": "apariencia",
            "modelo": "SigLIP",
            "entrenamiento": "publicado",
            "motor": "PyTorch",
            "huella": huella(ruta),
        })
    return descritos


def entrenamiento(checkpoint: object) -> str | None:
    """La carpeta del entrenamiento del checkpoint («entrenamientos\\rfdetr-balon-20261117-v2\\checkpoint_best_total.pth»
    → «rfdetr-balon-20261117-v2»), venga la ruta de Windows o de Linux."""
    partes = [p for p in re.split(r"[\\/]", str(checkpoint or "")) if p]
    if not partes:
        return None
    return partes[-2] if len(partes) >= 2 else partes[-1]


def huella(ruta: Path) -> str:
    with open(ruta, "rb") as archivo:
        return hashlib.file_digest(archivo, "sha256").hexdigest()[:8]


def _leer_json(ruta: Path) -> dict:
    try:
        datos = json.loads(ruta.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return datos if isinstance(datos, dict) else {}


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
