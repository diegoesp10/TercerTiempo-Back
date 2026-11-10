"""De qué equipo es cada jugador: el color de la camiseta de cada recorte (`colores.py`), con el clasificador ajustado
con recortes repartidos por todo el tramo. Los porteros van con el equipo cuyo grueso tienen más cerca."""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
import supervision as sv

from .colores import ClasificadorColor, rasgos_camiseta
from .config import CLASE_JUGADOR, CLASE_PORTERO, Configuracion
from .modelos import Modelos
from .video import InfoVideo, fotogramas, recortar


@dataclass
class ClasificadorEquipos:
    modelos: Modelos
    config: Configuracion
    clasificador: ClasificadorColor = field(default_factory=ClasificadorColor)
    colores: list[tuple[int, int, int]] = field(default_factory=list)
    _rasgos: list[np.ndarray] = field(default_factory=list)

    @property
    def reparto(self) -> tuple[int, int]:
        return self.clasificador.reparto

    @property
    def desequilibrado(self) -> bool:
        return self.clasificador.desequilibrado

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
                    rasgos = rasgos_camiseta(recorte)
                    if rasgos is not None:
                        self._rasgos.append(rasgos)
            if avance:
                avance(len(self._rasgos))
            if len(self._rasgos) >= objetivo and not por_fotograma:
                break
        if len(self._rasgos) < 20:
            raise RuntimeError(f"Solo se han encontrado {len(self._rasgos)} jugadores en el tramo: ¿es un partido de fútbol con el campo a la vista?")
        self.clasificador.ajustar(self._rasgos)
        self.colores = list(self.clasificador.colores)
        return len(self._rasgos)

    def clasificar(self, imagen: np.ndarray, personas: sv.Detections) -> tuple[np.ndarray, np.ndarray]:
        """Equipo (0/1) de cada persona seguida, -1 para árbitros o sin decidir, y el peso de cada decisión (0 a 1: lo
        claro que está el color; los porteros, colocados por cercanía, pesan la mitad)."""
        equipos = np.full(len(personas), -1, dtype=int)
        pesos = np.zeros(len(personas), dtype=np.float32)
        if len(personas) == 0:
            return equipos, pesos
        es_jugador = personas.class_id == CLASE_JUGADOR
        if es_jugador.any():
            indices = np.flatnonzero(es_jugador)
            rasgos = [rasgos_camiseta(recortar(imagen, caja)) for caja in personas.xyxy[es_jugador]]
            validos = np.array([r is not None for r in rasgos])
            if validos.any():
                puntuaciones = self.clasificador.puntuar([r for r in rasgos if r is not None])
                equipos[indices[validos]] = (puntuaciones > 0).astype(int)
                pesos[indices[validos]] = np.minimum(1.0, np.abs(puntuaciones))
        es_portero = personas.class_id == CLASE_PORTERO
        if es_portero.any():
            equipos[es_portero] = self._porteros(personas, equipos, es_portero)
            pesos[es_portero] = np.where(equipos[es_portero] >= 0, 0.5, 0.0)
        return equipos, pesos

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


def color_hex(rgb: tuple[int, int, int]) -> str:
    r, g, b = rgb
    return f"#{r:02x}{g:02x}{b:02x}"
