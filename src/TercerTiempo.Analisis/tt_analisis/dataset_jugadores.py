"""Dataset del detector de personas (RF-DETR) a partir de las carpetas `personas/` que deja `etiquetar`.

Se juntan los fotogramas de varios datasets (el mismo instante de dos datasets del mismo partido se usa una vez, con la
partición del primero: los bloques de validación son los mismos en todos), se quita la clase balón (es cosa de su propio
detector) y las clases quedan portero, jugador y árbitro. Las imágenes se enlazan (enlace duro) en vez de copiarse
cuando el disco lo permite: son fotogramas enteros de 1080p."""

from __future__ import annotations

import json
import os
import shutil
from pathlib import Path

from .config import CLASE_ARBITRO, CLASE_JUGADOR, CLASE_PORTERO

CLASES = ["goalkeeper", "player", "referee"]
# De las clases de `etiquetar` (las del detector de Roboflow: balón, portero, jugador, árbitro) a las del dataset.
NUEVA_CLASE = {CLASE_PORTERO: 0, CLASE_JUGADOR: 1, CLASE_ARBITRO: 2}


def convertir_etiquetas(texto: str) -> tuple[list[str], dict[int, int]]:
    """Las líneas YOLO sin balón y con las clases nuevas, y cuántas cajas hay de cada clase nueva."""
    lineas: list[str] = []
    recuento: dict[int, int] = {}
    for linea in texto.splitlines():
        partes = linea.split()
        if len(partes) != 5:
            continue
        nueva = NUEVA_CLASE.get(int(partes[0]))
        if nueva is None:
            continue
        lineas.append(" ".join([str(nueva), *partes[1:]]))
        recuento[nueva] = recuento.get(nueva, 0) + 1
    return lineas, recuento


def _enlazar(origen: Path, destino: Path) -> None:
    if destino.exists():
        destino.unlink()
    try:
        os.link(origen, destino)
    except OSError:
        shutil.copy2(origen, destino)


def preparar(origenes: list[Path], salida: Path, informar=print) -> dict:
    for particion in ("train", "val"):
        (salida / "images" / particion).mkdir(parents=True, exist_ok=True)
        (salida / "labels" / particion).mkdir(parents=True, exist_ok=True)

    vistos: set[str] = set()
    fotogramas = {"train": 0, "val": 0}
    cajas = {"train": [0, 0, 0], "val": [0, 0, 0]}
    for origen in origenes:
        personas = origen / "personas"
        for particion in ("train", "val"):
            for imagen in sorted((personas / "images" / particion).glob("*.jpg")):
                if imagen.stem in vistos:
                    continue
                vistos.add(imagen.stem)
                etiqueta = personas / "labels" / particion / f"{imagen.stem}.txt"
                lineas, recuento = convertir_etiquetas(etiqueta.read_text(encoding="utf-8") if etiqueta.exists() else "")
                _enlazar(imagen, salida / "images" / particion / imagen.name)
                (salida / "labels" / particion / f"{imagen.stem}.txt").write_text("".join(f"{linea}\n" for linea in lineas), encoding="utf-8")
                fotogramas[particion] += 1
                for clase, n in recuento.items():
                    cajas[particion][clase] += n

    nombres = ", ".join(CLASES)
    (salida / "data.yaml").write_text(f"path: {salida.resolve().as_posix()}\ntrain: images/train\nval: images/val\nnc: {len(CLASES)}\nnames: [{nombres}]\n", encoding="utf-8")
    resumen = {
        "origenes": [str(o) for o in origenes],
        "clases": CLASES,
        "fotogramas": fotogramas,
        "cajas": {p: dict(zip(CLASES, n)) for p, n in cajas.items()},
    }
    (salida / "resumen.json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    informar(
        f"Personas en {salida}: {fotogramas['train']} / {fotogramas['val']} fotogramas (entrenamiento / validación); "
        + "; ".join(f"{c} {cajas['train'][i]} / {cajas['val'][i]}" for i, c in enumerate(CLASES))
        + "."
    )
    return resumen
