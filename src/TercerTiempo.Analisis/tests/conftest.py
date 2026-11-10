import pytest

from tt_analisis.config import CLASE_JUGADOR, CLASE_PORTERO
from tt_analisis.tipos import Instante, Persona


def persona(tracklet: int, equipo: int, xy, clase: int = CLASE_JUGADOR) -> Persona:
    return Persona(tracklet, clase, equipo, (float(xy[0]) / 10, float(xy[1]) / 10), (float(xy[0]), float(xy[1])), (0, 0, 10, 20), 0.9)


def portero(tracklet: int, equipo: int, xy) -> Persona:
    return persona(tracklet, equipo, xy, CLASE_PORTERO)


@pytest.fixture
def dt():
    return 0.2


def instante(indice: int, t: float, personas, balon):
    return Instante(indice, t, list(personas), None if balon is None else (balon[0] / 10, balon[1] / 10), balon, True)
