"""Seguimiento de las personas entre fotogramas con los seguidores de `trackers` (Roboflow, Apache 2.0), en lugar de los
de Ultralytics (AGPL-3.0).

- Cámara móvil (alguien graba con paneo y zoom): BoT-SORT, que compensa el movimiento de la cámara con flujo óptico
  antes de asociar, para que un paneo no cambie el identificador de todos.
- Cámara fija (trípode o tipo Veo): ByteTrack.

Con los mismos ajustes que tenían los seguidores de Ultralytics: un tracklet aguanta 10 s sin verse, solo nace de una
detección clara (0,5) y las flojas (por debajo de 0,35 o 0,4) solo sirven para continuar a alguien que ya se seguía. Se
le pasa el instante de cada fotograma, así que da igual a cuántos fotogramas por segundo se analice."""

from __future__ import annotations

import numpy as np
import supervision as sv

AGUANTA_S = 10.0
CONF_NACER = 0.5
CONF_ALTA_MOVIL = 0.35
CONF_ALTA_FIJA = 0.4


SEGUIDORES = ("botsort", "bytetrack")


class Rastreador:
    def __init__(self, nombre: str = "botsort", fps: float = 5.0) -> None:
        from trackers import BoTSORTTracker, ByteTrackTracker

        if nombre not in SEGUIDORES:
            raise ValueError(f"Seguidor desconocido: {nombre} (vale {', '.join(SEGUIDORES)}).")
        # `lost_track_buffer` va en fotogramas a 30 por segundo y el seguidor lo escala con `frame_rate`.
        comunes = {"lost_track_buffer": int(round(AGUANTA_S * 30)), "frame_rate": fps, "track_activation_threshold": CONF_NACER}
        self.nombre = nombre
        self.movil = nombre == "botsort"
        if self.movil:
            self._seguidor = BoTSORTTracker(**comunes, high_conf_det_threshold=CONF_ALTA_MOVIL, enable_cmc=True, cmc_method="sparseOptFlow")
        else:
            self._seguidor = ByteTrackTracker(**comunes, high_conf_det_threshold=CONF_ALTA_FIJA, minimum_iou_threshold=0.15)

    def actualizar(self, personas: sv.Detections, imagen: np.ndarray, t: float) -> sv.Detections:
        """Las personas que se están siguiendo, con su `tracker_id`; las que aún no tienen identificador (una detección
        floja suelta o un tracklet que no se ha confirmado) se quedan fuera."""
        if personas.confidence is None:
            personas.confidence = np.ones(len(personas), dtype=np.float32)
        seguidas = self._seguidor.update(personas, frame=imagen if self.movil else None, timestamp=t)
        if len(seguidas) == 0 or seguidas.tracker_id is None:
            return sv.Detections.empty()
        return seguidas[seguidas.tracker_id >= 0]
