"""Memoria de cada tracklet (persona seguida): votos de clase y de equipo, y los mejores recortes para que el cuerpo
técnico (o el modelo de lenguaje) sepa quién es. El seguimiento en sí lo hace `rastreador.py`.

El seguidor a veces junta a dos jugadores en un mismo identificador (se cruzan, uno tapa al otro y sale el otro con el
mismo número). Como los dos equipos visten distinto, se nota: la camiseta del tracklet cambia de equipo y se queda así.
En cuanto pasa, el tracklet se parte y lo que viene después es otro tracklet, para que cada uno sea de un solo jugador
de un solo equipo (con el vídeo del Vallobín, dos de los 27 tracklets de un tramo de 5 min mezclaban equipos).

Entre dos del mismo equipo la camiseta no avisa. Desde la 2.3.0, cuando dos jugadores del mismo equipo se montan (uno
pasa por delante del otro) y se separan, los dos tracklets se parten ahí: lo de después puede ser cualquiera de los dos,
y quién es quién lo dicen el dorsal y el aspecto (`identidades.py`), no la continuidad."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field

import numpy as np
import supervision as sv

from .config import CLASE_ARBITRO, CLASE_PORTERO

MAX_RECORTES = 3
# Muestras de la equipación (`colores.rasgos_equipacion`) que se guardan por tracklet: al llenarse se queda una de cada
# dos y desde ahí se toma una de cada dos, así que siempre están repartidas por todo el tracklet.
MAX_EQUIPACIONES = 64
MIN_EQUIPACIONES = 3
# Corte de un tracklet por cambio de equipo: cuántas muestras seguidas del otro equipo hacen falta (a 5 fotogramas por
# segundo, dos segundos: un jugador tapado por uno del otro equipo lo está menos), lo claras que tienen que ser y cuánto
# voto tiene que llevar ya el equipo anterior.
CORTE_MUESTRAS = 10
CORTE_PESO_MIN = 0.3
CORTE_VOTOS_MIN = 3.0
# Los tracklets que nacen de un corte se numeran desde aquí, por encima de lo que da el seguidor.
TRACKLET_CORTADO_BASE = 100_000
# Cruce: dos del mismo equipo con las cajas montadas CRUCE_IOU o más (intersección sobre unión). Se parten los dos en el
# primer fotograma en que vuelven a verse los dos sin montarse; si uno deja de verse, el que queda se parte ya (el
# seguidor pudo darle el número del otro) y el otro, si vuelve antes de CRUCE_ESPERA_S.
CRUCE_IOU = 0.3
CRUCE_ESPERA_S = 2.0


@dataclass
class Recorte:
    t: float
    area: float
    confianza: float
    imagen: np.ndarray
    # Equipo que el color de la camiseta dio en ese fotograma (-1 si no se decidió).
    equipo: int = -1


@dataclass
class MemoriaTracklet:
    tracklet: int
    votos_clase: dict[int, int] = field(default_factory=lambda: defaultdict(int))
    # Votos de equipo ponderados por lo claro que se veía el color en cada fotograma.
    votos_equipo: dict[int, float] = field(default_factory=lambda: defaultdict(float))
    # Los mejores recortes (los más grandes), guardados por el equipo que se vio en cada uno: el que se enseña al cuerpo
    # técnico es del equipo que al final tiene el tracklet, no el de un jugador del otro equipo con el que el seguidor
    # lo haya confundido un momento.
    recortes_por_equipo: dict[int, list[Recorte]] = field(default_factory=lambda: defaultdict(list))
    # Suma de las lecturas del dorsal (probabilidad de cada número del 1 al 99 por lo legible que se veía) y el recorte
    # en el que mejor se leyó, por el equipo que se vio en ese fotograma: si el seguidor confunde un momento al jugador
    # con uno del otro equipo, el dorsal de ese otro no cuenta para él.
    votos_dorsal_por_equipo: dict[int, np.ndarray] = field(default_factory=dict)
    mejor_dorsal_por_equipo: dict[int, tuple[float, int, np.ndarray]] = field(default_factory=dict)
    lecturas_dorsal: int = 0
    # Muestras del color de la equipación (torso y pantalón) en los fotogramas en los que no lo tapaba nadie, y en qué
    # segundo del vídeo se tomó cada una.
    equipaciones: list[np.ndarray] = field(default_factory=list)
    tiempos_equipacion: list[float] = field(default_factory=list)
    _vistas_equipacion: int = 0
    _paso_equipacion: int = 1

    @property
    def clase(self) -> int:
        return max(self.votos_clase, key=self.votos_clase.get) if self.votos_clase else -1

    @property
    def equipo(self) -> int | None:
        """Por la camiseta. Ni los árbitros ni los porteros: el de un portero sale de la portería que defiende
        (`porteros.py`), aunque el detector lo haya tomado algún fotograma por un jugador de campo."""
        if self.clase in (CLASE_ARBITRO, CLASE_PORTERO) or not self.votos_equipo:
            return None
        return max(self.votos_equipo, key=self.votos_equipo.get)

    @property
    def votos_dorsal(self) -> np.ndarray | None:
        """Las lecturas del dorsal hechas con la equipación de su equipo (si no tiene equipo por la camiseta, como un
        portero, todas)."""
        equipo = self.equipo
        if equipo is not None:
            return self.votos_dorsal_por_equipo.get(equipo)
        return sum(self.votos_dorsal_por_equipo.values()) if self.votos_dorsal_por_equipo else None

    @property
    def mejor_dorsal(self) -> tuple[float, int, np.ndarray] | None:
        equipo = self.equipo
        if equipo is not None:
            return self.mejor_dorsal_por_equipo.get(equipo)
        return max(self.mejor_dorsal_por_equipo.values(), key=lambda m: m[0], default=None)

    @property
    def es_portero(self) -> bool:
        return self.clase == CLASE_PORTERO

    @property
    def recortes(self) -> list[Recorte]:
        """Los recortes del equipo que tiene el tracklet (de mayor a menor); si no hay ninguno de ese equipo, todos."""
        equipo = self.equipo
        if equipo is not None and self.recortes_por_equipo.get(equipo):
            return self.recortes_por_equipo[equipo]
        todos = [r for lista in self.recortes_por_equipo.values() for r in lista]
        return sorted(todos, key=lambda r: r.area, reverse=True)

    @property
    def equipacion(self) -> np.ndarray | None:
        """La mediana de las muestras de la equipación, o None si hay muy pocas."""
        if len(self.equipaciones) < MIN_EQUIPACIONES:
            return None
        return np.median(np.array(self.equipaciones), axis=0)

    def sumar_equipacion(self, rasgos: np.ndarray, t: float = float("nan")) -> None:
        self._vistas_equipacion += 1
        if (self._vistas_equipacion - 1) % self._paso_equipacion:
            return
        self.equipaciones.append(rasgos)
        self.tiempos_equipacion.append(t)
        if len(self.equipaciones) > MAX_EQUIPACIONES:
            self.equipaciones = self.equipaciones[::2]
            self.tiempos_equipacion = self.tiempos_equipacion[::2]
            self._paso_equipacion *= 2

    def sumar_dorsal(self, probabilidades: np.ndarray, peso: float, recorte: np.ndarray | None, equipo: int = -1) -> None:
        if peso <= 0:
            return
        votos = self.votos_dorsal_por_equipo.get(equipo)
        if votos is None:
            votos = self.votos_dorsal_por_equipo[equipo] = np.zeros(len(probabilidades))
        votos += peso * probabilidades
        self.lecturas_dorsal += 1
        i = int(probabilidades.argmax())
        nota = peso * float(probabilidades[i])
        mejor = self.mejor_dorsal_por_equipo.get(equipo)
        if recorte is not None and (mejor is None or nota > mejor[0]):
            self.mejor_dorsal_por_equipo[equipo] = (nota, i + 1, recorte)

    def guardar_recorte(self, t: float, caja, confianza: float, imagen: np.ndarray, equipo: int = -1) -> None:
        x1, y1, x2, y2 = caja
        area = float(max(0.0, x2 - x1) * max(0.0, y2 - y1))
        lista = self.recortes_por_equipo[equipo]
        if len(lista) >= MAX_RECORTES and area <= min(r.area for r in lista):
            return
        from .video import recortar

        lista.append(Recorte(t, area, confianza, recortar(imagen, caja, margen=6).copy(), equipo))
        lista.sort(key=lambda r: r.area, reverse=True)
        del lista[MAX_RECORTES:]


@dataclass
class Seguidor:
    memorias: dict[int, MemoriaTracklet] = field(default_factory=dict)
    # Identificador del seguidor -> tracklet vigente (cambia al partir el tracklet) y el camino de vuelta.
    alias: dict[int, int] = field(default_factory=dict)
    origen: dict[int, int] = field(default_factory=dict)
    # Por tracklet vigente, la racha de muestras seguidas del otro equipo: (equipo, peso).
    rachas: dict[int, list[tuple[int, float]]] = field(default_factory=dict)
    cortes: int = 0
    # Cruces sin resolver: par de tracklets del mismo equipo que se montaron -> último segundo en que se montaban; y los
    # que dejaron de verse en un cruce y se parten si vuelven (tracklet -> hasta qué segundo se espera).
    contactos: dict[tuple[int, int], float] = field(default_factory=dict)
    partir_al_volver: dict[int, float] = field(default_factory=dict)
    # Los tracklets que nacen al separarse un cruce y cuántos cruces se han partido.
    tras_cruce: set[int] = field(default_factory=set)
    cruces: int = 0
    _siguiente: int = TRACKLET_CORTADO_BASE

    def registrar(self, personas: sv.Detections) -> sv.Detections:
        """Apunta la clase de cada persona seguida (con el tracklet vigente, no el del seguidor); devuelve solo las que
        tienen identificador de seguimiento."""
        if len(personas) == 0 or personas.tracker_id is None:
            return sv.Detections.empty()
        personas.tracker_id = np.array([self.alias.get(int(t), int(t)) for t in personas.tracker_id], dtype=int)
        for i in range(len(personas)):
            tid = int(personas.tracker_id[i])
            memoria = self.memorias.get(tid)
            if memoria is None:
                memoria = self.memorias[tid] = MemoriaTracklet(tid)
            memoria.votos_clase[int(personas.class_id[i])] += 1
        return personas

    def votar_equipos(self, personas: sv.Detections, equipos: np.ndarray, pesos: np.ndarray) -> sv.Detections:
        """Suma el voto de equipo de cada persona y parte el tracklet de quien lleva más de un segundo viéndose con la
        camiseta del otro equipo: desde este fotograma esa persona es otro tracklet."""
        for i in range(len(personas)):
            if equipos[i] < 0 or pesos[i] <= 0:
                continue
            personas.tracker_id[i] = self.votar_equipo(int(personas.tracker_id[i]), int(equipos[i]), float(pesos[i]))
        return personas

    def votar_equipo(self, tracklet: int, equipo: int, peso: float = 1.0) -> int:
        """Suma el voto y devuelve el tracklet al que ha ido: el mismo, o uno nuevo si el tracklet se ha partido."""
        memoria = self.memorias.setdefault(tracklet, MemoriaTracklet(tracklet))
        votado = memoria.equipo
        racha = self.rachas.setdefault(tracklet, [])
        if votado is not None and equipo != votado and memoria.votos_equipo[votado] >= CORTE_VOTOS_MIN and peso >= CORTE_PESO_MIN:
            racha.append((equipo, peso))
        elif peso >= CORTE_PESO_MIN:
            racha.clear()
        memoria.votos_equipo[equipo] += peso
        if len(racha) < CORTE_MUESTRAS or sum(p for _, p in racha) < CORTE_MUESTRAS * CORTE_PESO_MIN:
            return tracklet
        return self._cortar(tracklet, memoria, racha)

    def _cortar(self, tracklet: int, memoria: MemoriaTracklet, racha: list[tuple[int, float]]) -> int:
        nuevo, memoria_nueva = self._nuevo(tracklet, memoria)
        self.cortes += 1
        for equipo, peso in racha:
            memoria.votos_equipo[equipo] = max(0.0, memoria.votos_equipo[equipo] - peso)
            memoria_nueva.votos_equipo[equipo] += peso
        racha.clear()
        return nuevo

    def _nuevo(self, tracklet: int, memoria: MemoriaTracklet) -> tuple[int, MemoriaTracklet]:
        """Desde ahora el identificador del seguidor que llevaba `tracklet` lleva otro tracklet, con su clase."""
        nuevo = self._siguiente
        self._siguiente += 1
        crudo = self.origen.get(tracklet, tracklet)
        self.alias[crudo] = nuevo
        self.origen[nuevo] = crudo
        memoria_nueva = self.memorias[nuevo] = MemoriaTracklet(nuevo)
        if memoria.votos_clase:
            memoria_nueva.votos_clase[memoria.clase] += 1
        return nuevo, memoria_nueva

    def separar_cruces(self, personas: sv.Detections, t: float) -> sv.Detections:
        """Apunta los pares del mismo equipo que se montan y parte sus tracklets al separarse (ver CRUCE_IOU)."""
        ids = [int(x) for x in personas.tracker_id] if len(personas) else []
        visibles = {tid: k for k, tid in enumerate(ids)}
        iou = _iou(personas.xyxy) if len(ids) >= 2 else np.zeros((len(ids), len(ids)))
        equipos = [self.equipo_de(tid) for tid in ids]
        for i in range(len(ids)):
            for j in range(i + 1, len(ids)):
                if equipos[i] is not None and equipos[i] == equipos[j] and iou[i, j] >= CRUCE_IOU:
                    self.contactos[(min(ids[i], ids[j]), max(ids[i], ids[j]))] = t
        partir: set[int] = {tid for tid, hasta in self.partir_al_volver.items() if tid in visibles and t <= hasta}
        self.partir_al_volver = {tid: hasta for tid, hasta in self.partir_al_volver.items() if tid not in partir and t <= hasta}
        for par, visto in list(self.contactos.items()):
            if visto >= t:
                continue
            a, b = par
            if a in visibles and b in visibles:
                partir.update(par)
            elif a in visibles or b in visibles:
                fuera = b if a in visibles else a
                partir.add(a if a in visibles else b)
                self.partir_al_volver[fuera] = t + CRUCE_ESPERA_S
            elif t - visto <= CRUCE_ESPERA_S:
                continue
            del self.contactos[par]
        if not partir:
            return personas
        nuevos = {}
        for tid in partir:
            nuevo, memoria_nueva = self._nuevo(tid, self.memorias.setdefault(tid, MemoriaTracklet(tid)))
            # Del mismo equipo: el voto pasa entero (el cruce no cambia de camiseta a nadie).
            for equipo, peso in self.memorias[tid].votos_equipo.items():
                memoria_nueva.votos_equipo[equipo] = peso
            self.tras_cruce.add(nuevo)
            nuevos[tid] = nuevo
        self.cruces += 1
        # Los cruces pendientes de los que se acaban de partir siguen con el tracklet nuevo.
        self.contactos = {(min(nuevos.get(a, a), nuevos.get(b, b)), max(nuevos.get(a, a), nuevos.get(b, b))): v for (a, b), v in self.contactos.items()}
        personas.tracker_id = np.array([nuevos.get(tid, tid) for tid in ids], dtype=int)
        return personas

    def equipo_de(self, tracklet: int) -> int | None:
        memoria = self.memorias.get(tracklet)
        return memoria.equipo if memoria else None


def _iou(cajas: np.ndarray) -> np.ndarray:
    c = np.asarray(cajas, dtype=np.float64)
    area = np.maximum(0.0, c[:, 2] - c[:, 0]) * np.maximum(0.0, c[:, 3] - c[:, 1])
    ix = np.clip(np.minimum(c[:, None, 2], c[None, :, 2]) - np.maximum(c[:, None, 0], c[None, :, 0]), 0, None)
    iy = np.clip(np.minimum(c[:, None, 3], c[None, :, 3]) - np.maximum(c[:, None, 1], c[None, :, 1]), 0, None)
    inter = ix * iy
    union = np.maximum(1e-6, area[:, None] + area[None, :] - inter)
    salida = inter / union
    np.fill_diagonal(salida, 0.0)
    return salida
