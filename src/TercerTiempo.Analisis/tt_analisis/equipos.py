"""De qué equipo es cada jugador: recortes → SigLIP → UMAP → KMeans en dos grupos (el clasificador de `sports`),
ajustado con recortes repartidos por todo el tramo. Los porteros van con el equipo cuyo grueso tienen más cerca."""

from __future__ import annotations

from dataclasses import dataclass, field

import cv2
import numpy as np
import supervision as sv
import sports.common.team as _team
from sports.common.team import TeamClassifier

from .config import CLASE_JUGADOR, CLASE_PORTERO, Configuracion
from .modelos import Modelos
from .video import InfoVideo, fotogramas, recortar

# El clasificador de `sports` saca una barra de progreso por cada lote de recortes: en cada fotograma sería ruido.
_team.tqdm = lambda iterable, **_: iterable


@dataclass
class ClasificadorEquipos:
    modelos: Modelos
    config: Configuracion
    clasificador: TeamClassifier = field(init=False)
    colores: list[tuple[int, int, int]] = field(default_factory=list)
    _muestras: list[np.ndarray] = field(default_factory=list)

    def __post_init__(self) -> None:
        self.clasificador = TeamClassifier(device=self.modelos.device)

    def ajustar(self, info: InfoVideo, avance=None, por_fotograma=None) -> int:
        """Primera pasada rápida: recortes de jugadores repartidos por el tramo. `por_fotograma(imagen)` deja que otro
        (la calibración de cámara fija) aproveche los mismos fotogramas."""
        duracion = (self.config.segmento.fin or info.duracion_s) - self.config.segmento.inicio
        objetivo = self.config.muestras_equipos
        # Unos 12 recortes por fotograma: fotogramas separados para cubrir luz y posiciones distintas.
        fotogramas_necesarios = max(8, objetivo // 12)
        paso = max(1, int(duracion * info.fps / fotogramas_necesarios))
        for fotograma in fotogramas(self.config, info, paso=paso):
            if por_fotograma:
                por_fotograma(fotograma.imagen)
            resultado = self.modelos.jugadores(fotograma.imagen, imgsz=self.config.imgsz, conf=0.4, device=self.modelos.device, half=self.modelos.half, verbose=False)[0]
            detecciones = sv.Detections.from_ultralytics(resultado)
            for caja in detecciones[detecciones.class_id == CLASE_JUGADOR].xyxy:
                recorte = recortar(fotograma.imagen, caja)
                if recorte.size and recorte.shape[0] >= 24 and recorte.shape[1] >= 12:
                    self._muestras.append(recorte)
            if avance:
                avance(len(self._muestras))
            if len(self._muestras) >= objetivo and not por_fotograma:
                break
        if len(self._muestras) < 20:
            raise RuntimeError(f"Solo se han encontrado {len(self._muestras)} jugadores en el tramo: ¿es un partido de fútbol con el campo a la vista?")
        self.clasificador.fit(self._muestras)
        self.colores = self._colores_dominantes()
        return len(self._muestras)

    def _colores_dominantes(self) -> list[tuple[int, int, int]]:
        etiquetas = self.clasificador.predict(self._muestras)
        colores = []
        for equipo in (0, 1):
            muestras = [m for m, e in zip(self._muestras, etiquetas) if e == equipo]
            if not muestras:
                colores.append((128, 128, 128))
                continue
            centros = []
            for m in muestras[:200]:
                alto, ancho = m.shape[:2]
                torso = m[int(alto * 0.2):int(alto * 0.55), int(ancho * 0.25):int(ancho * 0.75)]
                if torso.size:
                    centros.append(torso.reshape(-1, 3).mean(axis=0))
            bgr = np.median(np.array(centros), axis=0) if centros else np.array([128, 128, 128])
            colores.append(tuple(int(v) for v in bgr[::-1]))
        return colores

    def clasificar(self, imagen: np.ndarray, personas: sv.Detections) -> np.ndarray:
        """Equipo (0/1) de cada persona seguida; -1 para árbitros o sin decidir."""
        equipos = np.full(len(personas), -1, dtype=int)
        if len(personas) == 0:
            return equipos
        es_jugador = personas.class_id == CLASE_JUGADOR
        if es_jugador.any():
            recortes = [recortar(imagen, caja) for caja in personas.xyxy[es_jugador]]
            validos = [r.size > 0 and r.shape[0] >= 8 and r.shape[1] >= 4 for r in recortes]
            if any(validos):
                predicciones = self.clasificador.predict([r for r, v in zip(recortes, validos) if v])
                indices = np.flatnonzero(es_jugador)[np.array(validos)]
                equipos[indices] = predicciones
        es_portero = personas.class_id == CLASE_PORTERO
        if es_portero.any():
            equipos[es_portero] = self._porteros(personas, equipos, es_portero)
        return equipos

    @staticmethod
    def _porteros(personas: sv.Detections, equipos: np.ndarray, es_portero: np.ndarray) -> np.ndarray:
        xy = personas.get_anchors_coordinates(sv.Position.BOTTOM_CENTER)
        centroides = {}
        for equipo in (0, 1):
            puntos = xy[equipos == equipo]
            if len(puntos):
                centroides[equipo] = puntos.mean(axis=0)
        salida = []
        for punto in xy[es_portero]:
            if len(centroides) < 2:
                salida.append(-1)
                continue
            d0 = np.linalg.norm(punto - centroides[0])
            d1 = np.linalg.norm(punto - centroides[1])
            salida.append(0 if d0 < d1 else 1)
        return np.array(salida, dtype=int)


def color_hex(bgr_o_rgb: tuple[int, int, int]) -> str:
    r, g, b = bgr_o_rgb
    return f"#{r:02x}{g:02x}{b:02x}"
