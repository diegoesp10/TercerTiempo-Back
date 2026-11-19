"""Dataset de los 32 puntos del campo para el RF-DETR de puntos clave, por destilación: un maestro (el detector del campo
que haya) mira fotogramas de uno o varios vídeos y cada punto que da con confianza ≥ 0,5 dentro de la imagen queda como
etiqueta, en su sitio; los demás quedan sin etiqueta.

Por qué todos los puntos del maestro y no solo los «coherentes» de `etiquetar`: el modelo de puntos aprende también si
cada punto se encuentra en la imagen, y un punto sin etiqueta le enseña que no está. Con solo los ~5 puntos coherentes
por fotograma el YOLO afinado aprendió a callarse (44 % de fotogramas calibrables frente al 78 % del público). Con todo
lo que da el maestro, el alumno ve lo mismo que él, y los puntos que no cuadran los sigue quitando la homografía con
RANSAC (`campo.py`), como hasta ahora.

El reparto entre entrenamiento y validación es el de `etiquetar` (bloques de 2 min, uno de cada cinco a validación),
así que la validación de `etiquetar` y la de aquí son los mismos minutos del partido. Los vídeos cortos (clips de
muestra) van enteros a entrenamiento."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Callable

import numpy as np

CLASES = ["pitch"]
CONF_PUNTO = 0.5
LADO_MAX = 1280

# (caja xyxy del campo o None, puntos (32, 2) en píxeles, confianza de cada punto (32,)), o None si no ve el campo.
Maestro = Callable[[np.ndarray], "tuple[np.ndarray | None, np.ndarray, np.ndarray] | None"]


def puntos_visibles(xy: np.ndarray, conf: np.ndarray, ancho: int, alto: int, conf_punto: float = CONF_PUNTO) -> np.ndarray:
    return (conf >= conf_punto) & (xy[:, 0] > 1) & (xy[:, 1] > 1) & (xy[:, 0] < ancho - 1) & (xy[:, 1] < alto - 1)


def linea_destilada(caja: np.ndarray | None, xy: np.ndarray, conf: np.ndarray, ancho: int, alto: int, conf_punto: float = CONF_PUNTO) -> str | None:
    """La línea YOLO de pose con los puntos que el maestro ve (visibilidad 2) y los demás sin etiqueta (0); None si no
    ve ninguno (el fotograma queda como negativo). La caja abarca la del maestro y todos los puntos vistos."""
    from .etiquetado import linea_yolo

    visibles = puntos_visibles(xy, conf, ancho, alto, conf_punto)
    if not visibles.any():
        return None
    vistos = xy[visibles]
    x1, y1 = vistos.min(axis=0)
    x2, y2 = vistos.max(axis=0)
    if caja is not None:
        x1, y1 = min(x1, float(caja[0])), min(y1, float(caja[1]))
        x2, y2 = max(x2, float(caja[2])), max(y2, float(caja[3]))
    x1, y1 = max(0.0, float(x1)), max(0.0, float(y1))
    x2, y2 = min(float(ancho), float(x2)), min(float(alto), float(y2))
    if x2 - x1 < 2 or y2 - y1 < 2:
        return None
    puntos = np.zeros((len(xy), 3), dtype=np.float32)
    puntos[visibles, :2] = xy[visibles]
    puntos[visibles, 2] = 2
    return linea_yolo(0, (x1, y1, x2, y2), ancho, alto, puntos)


def preparar(videos: list[Path], salida: Path, maestro: Maestro, cada_s: float = 1.0, cada_s_cortos: float = 0.25, informar=print) -> dict:
    """Un fotograma cada `cada_s` segundos de cada vídeo (`cada_s_cortos` en los que duran menos de 10 min), etiquetado
    por el maestro. Deja `data.yaml` (con kpt_shape y flip_idx) y `resumen.json`."""
    from tqdm import tqdm

    from .campo import VERTICES
    from .config import Configuracion
    from .etiquetado import BLOQUE_S, BLOQUES_POR_CICLO, EscritorYolo, indices_espejo, particion_de
    from .video import fotogramas, info_video

    escritor = EscritorYolo(salida, CLASES, kpt_shape=[len(VERTICES), 3], flip_idx=indices_espejo(), lado_max=LADO_MAX)
    resumen: dict = {"videos": {}, "fotogramas": {"train": 0, "val": 0}, "negativos": {"train": 0, "val": 0}, "puntos": {"train": 0, "val": 0}}
    for ruta in videos:
        info = info_video(ruta)
        corto = info.duracion_s < BLOQUE_S * BLOQUES_POR_CICLO
        paso = max(1, round(info.fps * (cada_s_cortos if corto else cada_s)))
        config = Configuracion(video=ruta, salida=salida)
        por_video = {"fotogramas": 0, "negativos": 0, "puntos": 0, "cada_s": round(paso / info.fps, 3)}
        for f in tqdm(fotogramas(config, info, paso=paso, inicio_s=0.0), total=info.fotogramas // paso + 1, desc=f"Puntos del campo de {ruta.name}", unit="fot"):
            particion = "train" if corto else particion_de(f.t)
            r = maestro(f.imagen)
            linea = None if r is None else linea_destilada(r[0], r[1], r[2], info.ancho, info.alto)
            escritor.escribir(f"{ruta.stem}-{f.indice:07d}", particion, f.imagen, [linea] if linea else [])
            n = 0 if r is None or linea is None else int(puntos_visibles(r[1], r[2], info.ancho, info.alto).sum())
            resumen["fotogramas"][particion] += 1
            resumen["puntos"][particion] += n
            por_video["fotogramas"] += 1
            por_video["puntos"] += n
            if linea is None:
                resumen["negativos"][particion] += 1
                por_video["negativos"] += 1
        resumen["videos"][str(ruta)] = por_video
        informar(f"{ruta.name}: {por_video['fotogramas']} fotogramas ({por_video['negativos']} sin puntos), {por_video['puntos'] / max(1, por_video['fotogramas']):.1f} puntos por fotograma.")
    escritor.cerrar()
    resumen["puntos_por_fotograma"] = {p: round(resumen["puntos"][p] / max(1, resumen["fotogramas"][p]), 2) for p in ("train", "val")}
    (salida / "resumen.json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    return resumen
