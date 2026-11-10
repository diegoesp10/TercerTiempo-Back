"""Lo que el análisis sabe de cada fotograma muestreado: quién está dónde y dónde está el balón."""

from __future__ import annotations

from dataclasses import dataclass, field

from .config import CLASE_ARBITRO, CLASE_JUGADOR, CLASE_PORTERO

Punto = tuple[float, float]


@dataclass
class Persona:
    tracklet: int
    clase: int
    equipo: int | None
    xy_imagen: Punto
    xy_campo: Punto | None
    caja: tuple[float, float, float, float]
    confianza: float

    @property
    def es_jugador(self) -> bool:
        return self.clase in (CLASE_JUGADOR, CLASE_PORTERO)

    @property
    def es_portero(self) -> bool:
        return self.clase == CLASE_PORTERO

    @property
    def es_arbitro(self) -> bool:
        return self.clase == CLASE_ARBITRO


@dataclass
class Instante:
    """Un fotograma muestreado ya interpretado. `t` son segundos desde el principio del archivo."""

    indice: int
    t: float
    personas: list[Persona] = field(default_factory=list)
    balon_imagen: Punto | None = None
    balon_campo: Punto | None = None
    calibrado: bool = False

    def jugadores(self) -> list[Persona]:
        return [p for p in self.personas if p.es_jugador and p.equipo is not None]


@dataclass
class Toque:
    """Un jugador ha tenido el balón desde t_inicio hasta t_fin."""

    tracklet: int
    equipo: int
    t_inicio: float
    t_fin: float
    xy_inicio: Punto | None
    xy_fin: Punto | None


@dataclass
class Evento:
    tipo: str
    t: float
    equipo: int | None = None
    tracklet: int | None = None
    confianza: float = 0.5
    detalle: str = ""
    t_fin: float | None = None
    veredicto_llm: str | None = None
    clip: str | None = None

    def a_dict(self) -> dict:
        return {
            "tipo": self.tipo,
            "t": round(self.t, 2),
            "t_fin": None if self.t_fin is None else round(self.t_fin, 2),
            "minuto": formato_minuto(self.t),
            "equipo": self.equipo,
            "tracklet": self.tracklet,
            "confianza": round(self.confianza, 2),
            "detalle": self.detalle,
            "veredicto_llm": self.veredicto_llm,
            "clip": self.clip,
        }


def formato_minuto(t: float) -> str:
    total = int(t)
    horas, resto = divmod(total, 3600)
    minutos, segundos = divmod(resto, 60)
    return f"{horas}:{minutos:02d}:{segundos:02d}" if horas else f"{minutos:02d}:{segundos:02d}"
