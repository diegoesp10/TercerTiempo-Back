"""Corregir a mano una parte de las pseudoetiquetas. `exportar` empaqueta un subconjunto de un dataset YOLO (imágenes y
etiquetas) en un zip con el formato «YOLO 1.1» que CVAT importa tal cual (y Label Studio con su conversor); se revisa
allí, se exporta en el mismo formato e `importar` vuelve a escribir las etiquetas corregidas en el dataset, de modo que
`entrenar` las use. Solo se tocan las etiquetas de las imágenes que vienen en el zip."""

from __future__ import annotations

import zipfile
from pathlib import Path

CARPETA_CVAT = "obj_train_data"


def leer_nombres(data_yaml: Path) -> list[str]:
    for linea in data_yaml.read_text(encoding="utf-8").splitlines():
        if linea.startswith("names:"):
            return [n.strip() for n in linea.split(":", 1)[1].strip().strip("[]").split(",") if n.strip()]
    raise ValueError(f"{data_yaml} no tiene la lista de nombres (names)")


def exportar(carpeta: Path, destino: Path, particion: str = "val", maximo: int | None = None, solo_con_etiqueta: bool = False) -> int:
    """Zip «YOLO 1.1» con las imágenes de la partición (y sus etiquetas) de la carpeta de un conjunto (`balon/`,
    `personas/`…); devuelve cuántas imágenes lleva."""
    nombres = leer_nombres(carpeta / "data.yaml")
    imagenes = sorted((carpeta / "images" / particion).glob("*.jpg"))
    if solo_con_etiqueta:
        imagenes = [i for i in imagenes if (carpeta / "labels" / particion / f"{i.stem}.txt").read_text(encoding="utf-8").strip()]
    if maximo is not None:
        imagenes = imagenes[:maximo]
    destino.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destino, "w", zipfile.ZIP_STORED) as zf:
        zf.writestr("obj.names", "\n".join(nombres) + "\n")
        zf.writestr("obj.data", f"classes = {len(nombres)}\nnames = obj.names\ntrain = train.txt\nbackup = backup/\n")
        zf.writestr("train.txt", "".join(f"{CARPETA_CVAT}/{i.name}\n" for i in imagenes))
        for imagen in imagenes:
            zf.write(imagen, f"{CARPETA_CVAT}/{imagen.name}")
            etiqueta = carpeta / "labels" / particion / f"{imagen.stem}.txt"
            zf.writestr(f"{CARPETA_CVAT}/{imagen.stem}.txt", etiqueta.read_text(encoding="utf-8") if etiqueta.exists() else "")
    return len(imagenes)


def importar(origen: Path, carpeta: Path) -> dict:
    """Escribe en el dataset las etiquetas del zip (las imágenes del zip tienen que estar en el dataset, en cualquier
    partición). Una imagen listada sin etiqueta en el zip queda sin etiquetas (es lo que CVAT exporta cuando se
    borran todas). Devuelve cuántas se cambiaron, cuántas quedaron igual y cuáles no están en el dataset."""
    particiones = {}
    for particion in ("train", "val"):
        for imagen in (carpeta / "images" / particion).glob("*.jpg"):
            particiones[imagen.stem] = particion
    cambiadas, iguales, desconocidas = 0, 0, []
    with zipfile.ZipFile(origen) as zf:
        miembros = {Path(n).name: n for n in zf.namelist() if n.startswith(CARPETA_CVAT + "/") or "/" not in n}
        listadas = [Path(l.strip()).stem for l in zf.read("train.txt").decode("utf-8").splitlines() if l.strip()] if "train.txt" in zf.namelist() else []
        nombres = {Path(n).stem for n in miembros if n.endswith((".jpg", ".txt")) and n not in ("obj.names", "obj.data", "train.txt")}
        for stem in sorted(set(listadas) | nombres):
            particion = particiones.get(stem)
            if particion is None:
                desconocidas.append(stem)
                continue
            etiqueta = miembros.get(f"{stem}.txt")
            contenido = zf.read(etiqueta).decode("utf-8") if etiqueta else ""
            lineas = [l.strip() for l in contenido.splitlines() if l.strip()]
            contenido = "\n".join(lineas) + ("\n" if lineas else "")
            destino = carpeta / "labels" / particion / f"{stem}.txt"
            if destino.exists() and destino.read_text(encoding="utf-8") == contenido:
                iguales += 1
                continue
            destino.parent.mkdir(parents=True, exist_ok=True)
            destino.write_text(contenido, encoding="utf-8")
            cambiadas += 1
    return {"cambiadas": cambiadas, "iguales": iguales, "desconocidas": desconocidas}
