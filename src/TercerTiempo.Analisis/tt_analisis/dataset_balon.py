"""Dataset de teselas para el detector del balón (RF-DETR) a partir de los datasets de fotogramas que deja `etiquetar`.

De cada fotograma del dataset de origen salen trozos de 384 px a resolución nativa (los mismos que verá el análisis):
- con el balón confirmado, `por_balon` trozos que lo contienen en sitios distintos del trozo;
- `al_azar` trozos sin balón de cualquier parte del fotograma (césped, líneas, gente, grada);
- y uno por cada candidato que el etiquetado da por no-balón (quieto y con poca confianza: marcas de las líneas, focos,
  medias): los negativos difíciles, que son justo lo que el detector tiene que aprender a no ver.

Ningún trozo sin balón toca un candidato dudoso (un balón parado a los pies, un segundo balón, uno que se movía sin
llegar a confirmarse): enseñarlos como «aquí no hay balón» le quitaba al detector los balones parados.

La partición (entrenamiento / validación) es la del fotograma de origen, que sale de bloques de tiempo: los trozos de un
mismo momento nunca quedan a los dos lados. Varios datasets del mismo partido se pueden juntar porque los bloques de
validación son los mismos en todos."""

from __future__ import annotations

import json
import random
from dataclasses import dataclass, field
from pathlib import Path

import cv2
import numpy as np

from .etiquetado import CONF_QUIETO_NEGATIVO
from .teselas import LADO

MARGEN = 12


@dataclass
class Recuento:
    positivos: dict[str, int] = field(default_factory=lambda: {"train": 0, "val": 0})
    al_azar: dict[str, int] = field(default_factory=lambda: {"train": 0, "val": 0})
    dificiles: dict[str, int] = field(default_factory=lambda: {"train": 0, "val": 0})
    fotogramas: int = 0


def _caja_balon(etiqueta: Path, ancho: int, alto: int) -> tuple[float, float, float, float] | None:
    if not etiqueta.exists():
        return None
    for linea in etiqueta.read_text(encoding="utf-8").splitlines():
        partes = linea.split()
        if len(partes) == 5:
            cx, cy, w, h = (float(v) for v in partes[1:])
            return ((cx - w / 2) * ancho, (cy - h / 2) * alto, (cx + w / 2) * ancho, (cy + h / 2) * alto)
    return None


def _se_tocan(a: tuple[float, float, float, float], x: int, y: int, lado: int, holgura: float = 4) -> bool:
    return a[2] + holgura > x and a[0] - holgura < x + lado and a[3] + holgura > y and a[1] - holgura < y + lado


def _origen_con(caja: tuple[float, float, float, float], ancho: int, alto: int, lado: int, azar: random.Random) -> tuple[int, int] | None:
    """Un trozo que contiene `caja` entera, con la caja en un sitio al azar del trozo (no siempre en el centro)."""
    x_min = max(0, int(np.ceil(caja[2] + MARGEN - lado)))
    x_max = min(ancho - lado, int(caja[0] - MARGEN))
    y_min = max(0, int(np.ceil(caja[3] + MARGEN - lado)))
    y_max = min(alto - lado, int(caja[1] - MARGEN))
    if x_min > x_max or y_min > y_max:
        # Pegado al borde del fotograma: se acepta sin margen.
        x_min, x_max = max(0, int(np.ceil(caja[2] - lado))), min(ancho - lado, int(caja[0]))
        y_min, y_max = max(0, int(np.ceil(caja[3] - lado))), min(alto - lado, int(caja[1]))
        if x_min > x_max or y_min > y_max:
            return None
    return azar.randint(x_min, x_max), azar.randint(y_min, y_max)


Caja4 = tuple[float, float, float, float]


def _origen_sin(evitar: list[Caja4], ancho: int, alto: int, lado: int, azar: random.Random, cerca_de: tuple[float, float] | None = None) -> tuple[int, int] | None:
    """Un trozo que no toca ninguna de las cajas de `evitar` (el balón y lo que podría serlo)."""
    for _ in range(30):
        if cerca_de is None:
            x, y = azar.randint(0, ancho - lado), azar.randint(0, alto - lado)
        else:
            cx, cy = cerca_de
            x = int(np.clip(cx - azar.uniform(MARGEN, lado - MARGEN), 0, ancho - lado))
            y = int(np.clip(cy - azar.uniform(MARGEN, lado - MARGEN), 0, alto - lado))
        if not any(_se_tocan(caja, x, y, lado) for caja in evitar):
            return x, y
    return None


@dataclass
class Candidatos:
    # Centros de lo que el etiquetado da por no-balón: quieto respecto al césped, sin la regla rápida y con poca
    # confianza (las marcas de las líneas, los focos), con el mismo criterio con que rechaza un fotograma entero.
    negativos: list[tuple[float, float]] = field(default_factory=list)
    # Cajas de lo demás que no se eligió: un balón parado a los pies o en un saque (quieto pero con confianza), un
    # segundo balón, uno que se movía sin llegar a confirmarse. Ningún trozo sin balón puede tocarlas.
    dudosos: list[Caja4] = field(default_factory=list)


def _candidatos(muestras: Path) -> dict[str, Candidatos]:
    if not muestras.exists():
        return {}
    salida: dict[str, Candidatos] = {}
    with muestras.open(encoding="utf-8") as f:
        for linea in f:
            m = json.loads(linea)
            balon = m.get("balon")
            for c in m.get("candidatos", []):
                caja = tuple(c["caja"][:4])
                if balon and list(caja) == balon[:4]:
                    continue
                grupo = salida.setdefault(m["nombre"], Candidatos())
                if c.get("movimiento") == "quieto" and not c.get("heuristico") and c["caja"][4] < CONF_QUIETO_NEGATIVO:
                    grupo.negativos.append(((caja[0] + caja[2]) / 2, (caja[1] + caja[3]) / 2))
                else:
                    grupo.dudosos.append(caja)
    return salida


def preparar(origenes: list[Path], salida: Path, por_balon: int = 2, al_azar: int = 2, max_dificiles: int = 2, lado: int = LADO, semilla: int = 0, informar=print) -> dict:
    azar = random.Random(semilla)
    recuento = Recuento()
    for particion in ("train", "val"):
        (salida / "images" / particion).mkdir(parents=True, exist_ok=True)
        (salida / "labels" / particion).mkdir(parents=True, exist_ok=True)

    vistos: set[str] = set()
    for origen in origenes:
        balon_dir = origen / "balon"
        candidatos = _candidatos(origen / "muestras.jsonl")
        prefijo = origen.name
        for particion in ("train", "val"):
            for imagen_path in sorted((balon_dir / "images" / particion).glob("*.jpg")):
                # El mismo instante puede estar en dos datasets del mismo partido: se usa una vez.
                if imagen_path.stem in vistos:
                    continue
                vistos.add(imagen_path.stem)
                imagen = cv2.imread(str(imagen_path))
                if imagen is None:
                    continue
                alto, ancho = imagen.shape[:2]
                balon = _caja_balon(balon_dir / "labels" / particion / f"{imagen_path.stem}.txt", ancho, alto)
                recuento.fotogramas += 1

                del_fotograma = candidatos.get(imagen_path.stem, Candidatos())
                evitar = ([balon] if balon is not None else []) + del_fotograma.dudosos
                trozos: list[tuple[str, int, int, bool]] = []
                if balon is not None:
                    for _ in range(por_balon):
                        o = _origen_con(balon, ancho, alto, lado, azar)
                        if o:
                            trozos.append(("positivos", *o, True))
                for _ in range(al_azar):
                    o = _origen_sin(evitar, ancho, alto, lado, azar)
                    if o:
                        trozos.append(("al_azar", *o, False))
                for centro in del_fotograma.negativos[:max_dificiles]:
                    o = _origen_sin(evitar, ancho, alto, lado, azar, cerca_de=centro)
                    if o:
                        trozos.append(("dificiles", *o, False))

                for n, (tipo, x, y, con_balon) in enumerate(trozos):
                    nombre = f"{prefijo}-{imagen_path.stem}-{n}"
                    cv2.imwrite(str(salida / "images" / particion / f"{nombre}.jpg"), imagen[y : y + lado, x : x + lado], [cv2.IMWRITE_JPEG_QUALITY, 95])
                    etiqueta = ""
                    if con_balon and balon is not None:
                        x1, y1, x2, y2 = balon
                        cx, cy = ((x1 + x2) / 2 - x) / lado, ((y1 + y2) / 2 - y) / lado
                        w, h = (x2 - x1) / lado, (y2 - y1) / lado
                        etiqueta = f"0 {cx:.6f} {cy:.6f} {w:.6f} {h:.6f}\n"
                    (salida / "labels" / particion / f"{nombre}.txt").write_text(etiqueta, encoding="utf-8")
                    getattr(recuento, tipo)[particion] += 1

    (salida / "data.yaml").write_text(
        f"path: {salida.resolve().as_posix()}\ntrain: images/train\nval: images/val\nnc: 1\nnames: [ball]\n", encoding="utf-8"
    )
    resumen = {
        "origenes": [str(o) for o in origenes],
        "lado": lado,
        "fotogramas": recuento.fotogramas,
        "positivos": recuento.positivos,
        "al_azar": recuento.al_azar,
        "dificiles": recuento.dificiles,
    }
    (salida / "resumen.json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    informar(
        f"Teselas del balón en {salida}: {recuento.fotogramas} fotogramas → "
        f"con balón {recuento.positivos['train']}/{recuento.positivos['val']}, al azar {recuento.al_azar['train']}/{recuento.al_azar['val']}, "
        f"negativos difíciles {recuento.dificiles['train']}/{recuento.dificiles['val']} (entrenamiento/validación)."
    )
    return resumen
