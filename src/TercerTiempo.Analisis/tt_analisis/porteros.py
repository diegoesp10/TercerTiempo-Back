"""Quiénes son los porteros y de qué equipo. Puro: se prueba sin GPU.

**El equipo, por la portería que defiende** (desde la 1.1.0). No por la camiseta (viste distinto que los suyos) ni por
quién tiene más cerca (cuando atacan los rivales, son ellos): en cada parte cada equipo defiende un lado y en el
descanso cambian de campo, así que el portero que está en la portería de la izquierda en la primera parte es del equipo
que en la segunda la defiende a la derecha. Hacia dónde ataca cada equipo en cada parte lo dicen sus jugadores de campo
(`mitades.direcciones_de_ataque`). Con la regla vieja (el equipo que tenía más cerca en la imagen), en el J8 del
Vallobín 26 tramos de portero, unos 18 minutos, quedaban en el equipo equivocado.

**Quién es portero, por la equipación** (desde la 1.2.0). El detector solo marca «portero» una parte pequeña del tiempo
(en el J8, unos 11 de los 96 minutos) y a veces marca así a un jugador de campo. Pero el portero viste distinto que los
jugadores de campo de los dos equipos y no sale de su área: en cada parte, entre los que viven pegados a la portería que
defiende un equipo, el color de equipación (torso y pantalón, `colores.rasgos_equipacion`) que no es de ninguno de los
dos equipos ni del árbitro es el de su portero. Con ese color se reconoce al portero en todos sus tracklets, también en
los que el detector lo toma por un jugador de campo o por el árbitro, y el tracklet que el detector marca de portero
pero viste como un equipo es un jugador de ese equipo. En el J8 el portero del Real Titánico iba de morado y el del
Vallobín de verde lima; el detector los tomaba casi siempre por jugadores, el color dejaba al del Vallobín en el Real
Titánico y sus dorsales mal leídos los juntaban con los defensas (6 y 8) o con el 9 y el 21 del rival.

Lo que se parece a un portero pero no se puede colocar (su color vale para los dos porteros y no hay campo calibrado, o
está en el lado que no le toca) se queda sin equipo: mejor sin identificar que en otro jugador.

**Solo el rato en que viste de portero** (desde la 1.2.0). Cuando un jugador pasa pegado al portero, el seguidor salta
a veces de uno a otro: el tracklet empieza en un defensa y sigue en el portero, o al revés. Como el color de la
camiseta no dice de qué equipo es el portero, no se parte el tracklet al saltar. Cada muestra de la equipación guarda
cuándo se tomó: del tracklet de un portero se queda el rato que va del primer par de muestras seguidas que visten como
él (más cerca de su color que del de los dos equipos y el árbitro) al último. Lo de fuera es otra persona y pasa a otro
tracklet sin equipo; una muestra suelta que no se le parece en medio es el mismo portero agachado, borroso o tapado.
Dos o más seguidas en medio también se quitan: o el seguidor saltó a otra persona y volvió al portero (en el J8, al
menos cinco veces y hasta 4 s) o es el portero agachado o borroso, que por el color no se distingue, y mejor sin
identificar que con otro. Si el seguidor saltó a otra persona, las lecturas del dorsal de ese tracklet pueden ser de
ella y no cuentan. Y al revés: del tracklet de un jugador de campo que empieza o acaba vistiendo como un portero, o
pasa por él en medio, se quita ese rato (sin él, el portero de un equipo acababa en la ficha de un jugador del otro).

**Nunca vestido como los de campo** (desde la 1.3.0). El portero viste distinto de los jugadores de campo de los dos
equipos: el que el detector marca de portero solo lo es si tiene un rato en que viste más como el portero de esa
portería que como nadie más y en él no viste como los jugadores de campo de ningún equipo ni como el árbitro (si se
sabe cómo viste ese portero) o, si no se sabe, si no viste como ningún equipo; con muy pocas muestras de su equipación
no se le puede comprobar y se queda sin equipo. Al portero por la equipación también se le mira su rato. Así un rival que remata en el área y el detector toma por portero no acaba en
la línea de nuestro portero. Lo que se dice de cada portero (`Porteros.junto_a_su_porteria`, `momentos_de_portero`)
sirve después para exigir que pase la mayor parte del tiempo junto a su portería (`identidades.PARTE_EN_PORTERIA`) y
para enseñar de él solo un recorte en el que viste como él: en el J8 la línea del 1 del Vallobín salía con el recorte de
un jugador del Real Titánico, de un rato de un tracklet suyo que ya se había quitado porque el seguidor había saltado al
rival."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field

import numpy as np

from .colores import L_CESPED_TIPICA, lab_a_rgb
from .config import CLASE_ARBITRO, CLASE_JUGADOR, CLASE_PORTERO
from .geometria import Campo
from .mitades import direcciones_de_ataque
from .tipos import Instante

# Un portero está en su mitad del campo. Si la mediana de sus posiciones en una parte cae en la franja central (a menos
# de este tanto del largo de la línea de medio campo), no se sabe qué portería defiende (o no es un portero: el
# detector se equivoca a veces) y se queda sin equipo.
FRANJA_CENTRAL = 0.15
# Posiciones con el campo calibrado que hacen falta en una parte para saber en qué lado está.
MUESTRAS_MINIMAS = 3

# Zona del portero: el área grande con margen (a menos de esto de su línea de gol y de esto del eje del campo).
ZONA_FONDO_CM = 2200.0
ZONA_ANCHO_CM = 2500.0
# Un tracklet vive pegado a una portería en una parte si al menos esta parte de sus posiciones calibradas cae en su zona.
PARTE_EN_ZONA = 0.8
# Distancias en el espacio de la equipación (Mahalanobis): más allá de DISTANCIA_FUERA no es de ese grupo (un equipo, el
# árbitro); hasta DISTANCIA_DENTRO, sí.
DISTANCIA_FUERA = 5.0
DISTANCIA_DENTRO = 3.5
# El que no viste como nadie más (más allá de DISTANCIA_FUERA de los dos equipos y el árbitro) es del color de un
# portero mientras no se salga tampoco de él: el mismo límite. Con DISTANCIA_DENTRO, en el J8 se quedaban fuera 24
# tracklets del portero, casi 2 minutos, y como jugadores de campo uno acababa en la ficha de un defensa.
DISTANCIA_PORTERO = DISTANCIA_FUERA
# Dispersión mínima de cada rasgo de la equipación (claridad del torso, a, b, claridad del pantalón, a, b): un grupo con
# pocos tracklets no se queda tan estrecho que deje fuera al mismo portero con otra luz.
DISPERSION_MINIMA = np.array([12.0, 3.0, 3.0, 12.0, 3.0, 3.0])
# El color de un portero es el más repetido entre los candidatos pegados a su portería (radio en estas escalas): tiene
# que juntar esta parte de ellos y al menos estas posiciones (a 5 fotogramas por segundo, 5 s a la vista).
ESCALA_MODA = np.array([20.0, 5.0, 5.0, 20.0, 5.0, 5.0])
RADIO_MODA = 2.5
PARTE_MODA = 0.6
MUESTRAS_PORTERO = 25
# Muestras de equipación (`MemoriaTracklet.equipaciones`) para fiarse del color de un tracklet; sin campo calibrado, que
# no hay posición que lo respalde, más.
EQUIPACIONES_MINIMAS = 3
EQUIPACIONES_SIN_CAMPO = 6
# Tracklets de un grupo (equipo o árbitro) para saber cómo viste.
TRACKLETS_POR_GRUPO = 10
# Muestras que no visten como él al principio o al final de un tracklet de portero, o seguidas en medio, para darlo por
# mezclado: el seguidor saltó a otra persona y sus lecturas del dorsal de fuera de su rato pueden ser de ella.
MUESTRAS_MEZCLA = 2
# De un tracklet de portero mezclado solo cuentan las lecturas del dorsal de su rato a este margen o más de cada corte
# (su principio o su fin, si se recortaron, y cada hueco): en el momento del salto la caja puede llevar a los dos. Dos
# fotogramas de los analizados: en el J8 las lecturas de otro caen todas fuera del rato, ninguna limpia dentro ni junto
# al corte, y con 1 s se perdían lecturas limpias del portero.
MARGEN_CORTE_S = 0.4
# Los fotogramas de un tracklet de portero fuera del rato en que viste como él pasan a este tracklet más el suyo: por
# encima de los que nacen de un corte (`seguimiento.TRACKLET_CORTADO_BASE`) y por debajo de los jugadores identificados
# (`identidades.ID_JUGADOR_BASE`).
TRACKLET_RESTO_BASE = 500_000


@dataclass
class Modelo:
    """Un color de equipación: media y covarianza (con DISPERSION_MINIMA), para medir la distancia de Mahalanobis."""

    media: np.ndarray
    inversa: np.ndarray
    peso: float

    def distancia(self, rasgos) -> np.ndarray:
        d = np.atleast_2d(np.asarray(rasgos, dtype=np.float64)) - self.media
        return np.sqrt(np.maximum(0.0, np.einsum("ij,jk,ik->i", d, self.inversa, d)))

    def color(self) -> str:
        r, g, b = lab_a_rgb([self.media[0] + L_CESPED_TIPICA, self.media[1], self.media[2]])
        return f"#{r:02x}{g:02x}{b:02x}"


def _mediana_ponderada(x: np.ndarray, w: np.ndarray) -> np.ndarray:
    """La mediana ponderada de cada columna."""
    salida = np.empty(x.shape[1])
    for j in range(x.shape[1]):
        orden = np.argsort(x[:, j])
        acumulado = np.cumsum(w[orden])
        salida[j] = x[orden[int(np.searchsorted(acumulado, acumulado[-1] / 2))], j]
    return salida


def ajustar_modelo(rasgos: np.ndarray, pesos: np.ndarray, recorte: float = DISTANCIA_FUERA, vueltas: int = 3) -> Modelo | None:
    """Media y covarianza ponderadas sin los que quedan más allá de `recorte` (en un equipo se cuelan sus porteros y algún
    tracklet que mezcla a dos personas). Se empieza por la mediana y la desviación mediana de cada rasgo, que no se dejan
    arrastrar por ellos, y en cada vuelta se recalcula con los que quedan dentro."""
    x = np.asarray(rasgos, dtype=np.float64)
    w = np.asarray(pesos, dtype=np.float64)
    if len(x) < 3 or w.sum() <= 0:
        return None
    centro = _mediana_ponderada(x, w)
    escala = np.maximum(1.4826 * _mediana_ponderada(np.abs(x - centro), w), DISPERSION_MINIMA)
    usar = np.linalg.norm((x - centro) / escala, axis=1) <= recorte
    modelo = None
    for _ in range(vueltas):
        if usar.sum() < 3 or w[usar].sum() <= 0:
            return modelo
        ws = w[usar] / w[usar].sum()
        media = ws @ x[usar]
        d = x[usar] - media
        cov = (d * ws[:, None]).T @ d + np.diag(DISPERSION_MINIMA**2)
        modelo = Modelo(media, np.linalg.inv(cov), float(w[usar].sum()))
        nuevos = modelo.distancia(x) <= recorte
        if np.array_equal(nuevos, usar):
            break
        usar = nuevos
    return modelo


@dataclass
class Porteros:
    """Lo que se decide de cada tracklet: el portero (por la equipación) y su equipo para todo el partido; el de los que
    solo marca el detector, por parte; los que el detector marca de porteros pero visten como un equipo; los que no se
    pueden colocar; el rato en que es quien es (el portero mientras viste como él, el jugador de campo mientras no viste
    como un portero; fuera, otra persona) y los huecos de en medio en que el portero no viste como él; y en qué
    tracklets de portero el seguidor saltó a otra persona."""

    equipo_de: dict[int, int] = field(default_factory=dict)
    por_parte: dict[tuple[int, int], int] = field(default_factory=dict)
    del_detector: set[int] = field(default_factory=set)
    de_campo: dict[int, int] = field(default_factory=dict)
    sin_equipo: set[int] = field(default_factory=set)
    modelos: dict[int, Modelo] = field(default_factory=dict)
    ratos: dict[int, tuple[float, float]] = field(default_factory=dict)
    huecos: dict[int, list[tuple[float, float]]] = field(default_factory=dict)
    mezclados: set[int] = field(default_factory=set)
    # Con lo que se decidió: hacia dónde ataca cada equipo en cada parte, el campo y cómo visten los jugadores de campo
    # de los dos equipos y el árbitro.
    direcciones: dict[tuple[int, int], int] = field(default_factory=dict)
    campo: Campo | None = None
    ajenos: list[Modelo] = field(default_factory=list)

    def es_portero(self, tracklet: int) -> bool:
        return tracklet in self.equipo_de or tracklet in self.del_detector

    def por_equipacion(self, tracklet: int) -> bool:
        return tracklet in self.equipo_de

    def en_su_rato(self, tracklet: int, t: float) -> bool:
        rato = self.ratos.get(tracklet)
        if rato is not None and not rato[0] <= t <= rato[1]:
            return False
        return not any(a < t < b for a, b in self.huecos.get(tracklet, ()))

    def lejos_de_los_cortes(self, tracklet: int, tiempos) -> np.ndarray:
        """De cada segundo, si cae en el rato del tracklet a MARGEN_CORTE_S o más de cada corte. Si el tracklet está
        mezclado y no se sabe dónde se cortó (sin el segundo de cada muestra), en ninguno."""
        tiempos = np.asarray(tiempos, dtype=np.float64)
        if tracklet in self.mezclados and tracklet not in self.ratos and tracklet not in self.huecos:
            return np.zeros(tiempos.shape, dtype=bool)
        desde, hasta = self.ratos.get(tracklet, (-np.inf, np.inf))
        dentro = (tiempos >= desde + MARGEN_CORTE_S) & (tiempos <= hasta - MARGEN_CORTE_S)
        for a, b in self.huecos.get(tracklet, ()):
            dentro &= (tiempos <= a - MARGEN_CORTE_S) | (tiempos >= b + MARGEN_CORTE_S)
        return dentro

    def junto_a_su_porteria(self, xy, equipo: int | None, mitad: int) -> bool | None:
        """Si en esa parte está al lado o debajo de la portería que defiende ese equipo (en la zona del portero: a menos de
        ZONA_FONDO_CM de su línea de gol y ZONA_ANCHO_CM del eje); None si no se sabe (sin campo calibrado o sin saber
        hacia dónde ataca)."""
        ataca = self.direcciones.get((equipo, mitad)) if equipo is not None else None
        if xy is None or ataca is None:
            return None
        return bool(_en_zona(np.asarray([xy], dtype=np.float64), -ataca, self.campo or Campo())[0])

    def viste_de_portero(self, equipo: int, rasgos) -> bool:
        """Si una muestra de la equipación (la de un fotograma) es la del portero de ese equipo: dentro de su color (hasta
        DISTANCIA_PORTERO) y a más de DISTANCIA_FUERA de los jugadores de campo de los dos equipos y del árbitro. Sin
        saber cómo visten, no."""
        modelo = self.modelos.get(equipo)
        if modelo is None or rasgos is None or not self.ajenos:
            return False
        r = np.asarray(rasgos, dtype=np.float64)
        return float(modelo.distancia(r)[0]) <= DISTANCIA_PORTERO and all(float(m.distancia(r)[0]) > DISTANCIA_FUERA for m in self.ajenos)


def lados_de_porteros(instantes: list[Instante], mitad_de: list[int], porteros: set[int], campo: Campo | None = None) -> dict[tuple[int, int], int]:
    """(tracklet, mitad) → -1 si defiende la portería de la izquierda (x = 0) y +1 si la de la derecha (x = largo)."""
    campo = campo or Campo()
    xs: dict[tuple[int, int], list[float]] = defaultdict(list)
    for inst, mitad in zip(instantes, mitad_de):
        for p in inst.personas:
            if p.tracklet in porteros and p.xy_campo is not None:
                xs[(p.tracklet, mitad)].append(p.xy_campo[0])
    lados: dict[tuple[int, int], int] = {}
    medio = campo.largo / 2
    for clave, valores in xs.items():
        if len(valores) < MUESTRAS_MINIMAS:
            continue
        x = float(np.median(valores))
        if abs(x - medio) <= FRANJA_CENTRAL * campo.largo:
            continue
        lados[clave] = -1 if x < medio else 1
    return lados


def equipo_que_defiende(lado: int, mitad: int, direcciones: dict[tuple[int, int], int]) -> int | None:
    """El equipo que defiende ese lado en esa parte: el que defiende la izquierda ataca hacia la derecha (+1)."""
    for equipo in (0, 1):
        if direcciones.get((equipo, mitad)) == -lado:
            return equipo
    return None


def asignar_porteros(
    instantes: list[Instante],
    mitad_de: list[int],
    direcciones: dict[tuple[int, int], int],
    porteros: set[int],
    campo: Campo | None = None,
) -> dict[tuple[int, int], int]:
    """Pone a cada portero el equipo de la portería que defiende en cada parte, también en los fotogramas de esa parte
    sin el campo calibrado (en una parte no cambia de portería). Si no se sabe, sin equipo: mejor que el del rival.
    Devuelve (tracklet, mitad) → equipo."""
    lados = lados_de_porteros(instantes, mitad_de, porteros, campo)
    equipos: dict[tuple[int, int], int] = {}
    for (tracklet, mitad), lado in lados.items():
        equipo = equipo_que_defiende(lado, mitad, direcciones)
        if equipo is not None:
            equipos[(tracklet, mitad)] = equipo
    for inst, mitad in zip(instantes, mitad_de):
        for p in inst.personas:
            if p.tracklet in porteros:
                p.equipo = equipos.get((p.tracklet, mitad))
    return equipos


def _posiciones(instantes: list[Instante], mitad_de: list[int]) -> dict[tuple[int, int], np.ndarray]:
    """(tracklet, mitad) → posiciones con el campo calibrado (N × 2), también en los fotogramas en los que el detector lo
    toma por el árbitro."""
    puntos: dict[tuple[int, int], list[tuple[float, float]]] = defaultdict(list)
    for inst, mitad in zip(instantes, mitad_de):
        for p in inst.personas:
            if p.xy_campo is not None:
                puntos[(p.tracklet, mitad)].append(p.xy_campo)
    return {clave: np.asarray(v, dtype=np.float64) for clave, v in puntos.items()}


def _en_zona(puntos: np.ndarray, lado: int, campo: Campo) -> np.ndarray:
    fondo = puntos[:, 0] <= ZONA_FONDO_CM if lado < 0 else puntos[:, 0] >= campo.largo - ZONA_FONDO_CM
    return fondo & (np.abs(puntos[:, 1] - campo.ancho / 2) <= ZONA_ANCHO_CM)


def _moda(rasgos: np.ndarray, pesos: np.ndarray) -> np.ndarray:
    """Los candidatos alrededor del color más repetido: el que más peso junta a menos de RADIO_MODA, recentrado una vez."""
    escalados = rasgos / ESCALA_MODA
    distancias = np.linalg.norm(escalados[:, None, :] - escalados[None, :, :], axis=2)
    vecinos = (distancias <= RADIO_MODA) @ pesos
    centro = escalados[int(np.argmax(vecinos))]
    dentro = np.linalg.norm(escalados - centro, axis=1) <= RADIO_MODA
    centro = (pesos[dentro] @ escalados[dentro]) / pesos[dentro].sum()
    return np.linalg.norm(escalados - centro, axis=1) <= RADIO_MODA


def _rachas(marcas: np.ndarray) -> list[tuple[int, int]]:
    """(primera, última) de cada racha de marcas seguidas."""
    bordes = np.diff(np.concatenate([[0], marcas.astype(np.int8), [0]]))
    return list(zip(np.flatnonzero(bordes == 1).tolist(), (np.flatnonzero(bordes == -1) - 1).tolist()))


def _rato_del_portero(
    muestras: np.ndarray, tiempos: np.ndarray | None, propio: Modelo, ajenos: list[Modelo]
) -> tuple[float, float, bool, list[tuple[float, float]]] | None:
    """Del primer par de muestras seguidas que visten como el portero (más cerca de su color que del de los dos equipos
    y el árbitro) al último, sin los huecos de en medio de al menos MUESTRAS_MEZCLA muestras seguidas que no visten como
    él (de la muestra anterior a la siguiente, sin ellas). Devuelve (desde, hasta, mezclado, huecos): -inf o inf en el
    lado que no se recorta (o en los dos, y sin huecos, si no se sabe cuándo se tomó cada muestra), y mezclado si hay
    algún hueco o si fuera del rato quedan al menos MUESTRAS_MEZCLA muestras que no visten como él en uno de los
    extremos. None si no viste como él en ningún momento."""
    suyo = propio.distancia(muestras) < np.min([m.distancia(muestras) for m in ajenos], axis=0)
    pares = np.flatnonzero(suyo[:-1] & suyo[1:])
    if not len(pares):
        return None
    i, j = int(pares[0]), int(pares[-1]) + 1
    rachas = [(i + a, i + b) for a, b in _rachas(~suyo[i : j + 1]) if b - a + 1 >= MUESTRAS_MEZCLA]
    mezclado = bool(rachas) or int((~suyo[:i]).sum()) >= MUESTRAS_MEZCLA or int((~suyo[j + 1 :]).sum()) >= MUESTRAS_MEZCLA
    if tiempos is None or len(tiempos) != len(suyo) or not np.all(np.isfinite(tiempos)):
        return -np.inf, np.inf, mezclado, []
    huecos = [(float(tiempos[a - 1]), float(tiempos[b + 1])) for a, b in rachas]
    return (float(tiempos[i]) if i > 0 else -np.inf), (float(tiempos[j]) if j < len(suyo) - 1 else np.inf), mezclado, huecos


def _rato_sin_portero(
    muestras: np.ndarray, tiempos: np.ndarray | None, porteros: list[Modelo], ajenos: list[Modelo]
) -> tuple[float, float, list[tuple[float, float]]] | None:
    """Lo mismo al revés, para un jugador de campo: si empieza o acaba vistiendo como un portero (al menos
    MUESTRAS_MEZCLA muestras más cerca de un portero que de los dos equipos y el árbitro antes del primer par de muestras
    seguidas que no visten como un portero, o después del último), el rato sin ese extremo, y sin los huecos de en medio
    de al menos MUESTRAS_MEZCLA muestras seguidas que visten como un portero (el seguidor saltó al portero y volvió).
    (-inf, inf, []) si no hay que cortar (o no se sabe cuándo se tomó cada muestra); None si no hay ningún par así:
    viste como un portero de principio a fin."""
    otros = np.min([m.distancia(muestras) for m in ajenos], axis=0)
    portero = np.any([m.distancia(muestras) < otros for m in porteros], axis=0)
    if int(portero.sum()) < MUESTRAS_MEZCLA:
        return -np.inf, np.inf, []
    pares = np.flatnonzero(~portero[:-1] & ~portero[1:])
    if not len(pares):
        return None
    if tiempos is None or len(tiempos) != len(portero) or not np.all(np.isfinite(tiempos)):
        return -np.inf, np.inf, []
    i, j = int(pares[0]), int(pares[-1]) + 1
    desde = float(tiempos[i]) if int(portero[:i].sum()) >= MUESTRAS_MEZCLA else -np.inf
    hasta = float(tiempos[j]) if int(portero[j + 1 :].sum()) >= MUESTRAS_MEZCLA else np.inf
    huecos = [(float(tiempos[i + a - 1]), float(tiempos[i + b + 1])) for a, b in _rachas(portero[i : j + 1]) if b - a + 1 >= MUESTRAS_MEZCLA]
    return desde, hasta, huecos


def buscar_porteros(
    instantes: list[Instante],
    mitad_de: list[int],
    direcciones: dict[tuple[int, int], int],
    equipaciones: dict[int, np.ndarray],
    equipo_camiseta: dict[int, int | None],
    clases: dict[int, int],
    campo: Campo | None = None,
    tiempos: dict[int, np.ndarray] | None = None,
) -> Porteros:
    """`equipaciones`: las muestras de la equipación de cada tracklet, en orden (`MemoriaTracklet.equipaciones`), y
    `tiempos`, en qué segundo del vídeo se tomó cada una (sin ellos no se recorta ningún tracklet);
    `equipo_camiseta`: el equipo que le da el color de la camiseta (`MemoriaTracklet.equipo`, None para porteros y
    árbitros); `clases`: la clase que le da el detector por mayoría de votos."""
    campo = campo or Campo()
    tiempos = tiempos or {}
    resultado = Porteros(direcciones=dict(direcciones), campo=campo)
    posiciones = _posiciones(instantes, mitad_de)
    series = {t: np.atleast_2d(np.asarray(x, dtype=np.float64)) for t, x in equipaciones.items() if x is not None and len(x)}
    muestras = {t: len(x) for t, x in series.items()}
    todos = {t: np.median(x, axis=0) for t, x in series.items()}
    fiables = {t: r for t, r in todos.items() if muestras[t] >= EQUIPACIONES_MINIMAS}

    def modelo_de(tracklets: list[int]) -> Modelo | None:
        if len(tracklets) < TRACKLETS_POR_GRUPO:
            return None
        return ajustar_modelo(np.array([fiables[t] for t in tracklets]), np.array([muestras[t] for t in tracklets], dtype=np.float64))

    equipos = {e: modelo_de([t for t in fiables if equipo_camiseta.get(t) == e and clases.get(t) == CLASE_JUGADOR]) for e in (0, 1)}
    arbitro = modelo_de([t for t in fiables if clases.get(t) == CLASE_ARBITRO])
    if any(m is None for m in equipos.values()):
        _solo_detector(instantes, mitad_de, direcciones, clases, campo, resultado)
        return resultado

    def lejos_de_todos(rasgos: np.ndarray) -> bool:
        grupos = [equipos[0], equipos[1]] + ([arbitro] if arbitro is not None else [])
        return all(float(m.distancia(rasgos)[0]) > DISTANCIA_FUERA for m in grupos)

    # El color de cada portero: entre los que viven pegados a la portería que defiende su equipo en cada parte y no visten
    # como nadie más, el más repetido.
    for equipo in (0, 1):
        candidatos: list[tuple[int, float]] = []
        for mitad in sorted(set(mitad_de)):
            ataca = direcciones.get((equipo, mitad))
            if ataca is None:
                continue
            for (tracklet, m), puntos in posiciones.items():
                if m != mitad or len(puntos) < MUESTRAS_MINIMAS or tracklet not in fiables:
                    continue
                if _en_zona(puntos, -ataca, campo).mean() >= PARTE_EN_ZONA and lejos_de_todos(fiables[tracklet]):
                    candidatos.append((tracklet, float(len(puntos))))
        if not candidatos:
            continue
        rasgos = np.array([fiables[t] for t, _ in candidatos])
        pesos = np.array([p for _, p in candidatos])
        dentro = _moda(rasgos, pesos)
        if pesos[dentro].sum() < PARTE_MODA * pesos.sum() or pesos[dentro].sum() < MUESTRAS_PORTERO:
            continue
        modelo = ajustar_modelo(rasgos[dentro], pesos[dentro])
        if modelo is not None:
            resultado.modelos[equipo] = modelo

    # También los que el detector toma por el árbitro: el verde lima de un portero y el verde menta del árbitro se le
    # confunden (el que viste como el árbitro no está lejos de todos).
    medio = campo.largo / 2
    for tracklet, rasgos in todos.items():
        if not lejos_de_todos(rasgos):
            continue
        parecidos = [e for e, m in resultado.modelos.items() if float(m.distancia(rasgos)[0]) <= DISTANCIA_PORTERO]
        if not parecidos:
            continue
        if tracklet not in fiables:
            # Con tan pocas muestras no se le pone de portero, pero tampoco se arriesga a juntarlo con un jugador.
            resultado.sin_equipo.add(tracklet)
            continue
        # Con campo calibrado, tiene que estar en el lado que defiende su equipo en cada parte en la que se le ve.
        lados_vistos = {}
        for mitad in set(mitad_de):
            puntos = posiciones.get((tracklet, mitad))
            if puntos is None or not len(puntos):
                continue
            x = float(np.median(puntos[:, 0]))
            lados_vistos[mitad] = 0 if abs(x - medio) <= FRANJA_CENTRAL * campo.largo else (-1 if x < medio else 1)
        posibles = [e for e in parecidos if all(lado != 0 and direcciones.get((e, mitad)) == -lado for mitad, lado in lados_vistos.items())]
        if lados_vistos and len(posibles) == 1:
            resultado.equipo_de[tracklet] = posibles[0]
        elif not lados_vistos and len(parecidos) == 1 and muestras.get(tracklet, 0) >= EQUIPACIONES_SIN_CAMPO:
            resultado.equipo_de[tracklet] = parecidos[0]
        else:
            resultado.sin_equipo.add(tracklet)

    ajenos = [equipos[0], equipos[1]] + ([arbitro] if arbitro is not None else [])
    resultado.ajenos = ajenos

    def recortar(tracklet: int, equipo: int) -> bool:
        """Deja en `ratos` y `huecos` el rato en que el tracklet viste como el portero de ese equipo. False si no lo hace
        en ningún momento o si tiene muy pocas muestras para saberlo."""
        if muestras.get(tracklet, 0) < EQUIPACIONES_MINIMAS:
            return False
        rato = _rato_del_portero(series[tracklet], tiempos.get(tracklet), resultado.modelos[equipo], ajenos)
        if rato is None:
            return False
        desde, hasta, mezclado, huecos = rato
        if mezclado:
            resultado.mezclados.add(tracklet)
        if np.isfinite(desde) or np.isfinite(hasta):
            resultado.ratos[tracklet] = (desde, hasta)
        if huecos:
            resultado.huecos[tracklet] = huecos
        return True

    def viste_en_su_rato(tracklet: int) -> bool:
        """Si en el rato que se le da (la mediana de sus muestras en él, que ya visten más como su portero que como
        nadie más) no viste como los jugadores de campo de ningún equipo ni como el árbitro. No se le pide el color
        exacto del portero: con otra luz se sale de él sin parecerse a nadie más."""
        serie = series[tracklet]
        momentos = tiempos.get(tracklet)
        if momentos is not None and len(momentos) == len(serie) and np.all(np.isfinite(momentos)):
            dentro = np.array([resultado.en_su_rato(tracklet, float(t)) for t in momentos])
            if dentro.any():
                serie = serie[dentro]
        return lejos_de_todos(np.median(serie, axis=0))

    def descartar(tracklet: int) -> None:
        """No es un portero que se pueda dar por bueno: sin equipo y sin recorte."""
        resultado.equipo_de.pop(tracklet, None)
        resultado.del_detector.discard(tracklet)
        resultado.ratos.pop(tracklet, None)
        resultado.huecos.pop(tracklet, None)
        resultado.mezclados.discard(tracklet)
        resultado.sin_equipo.add(tracklet)

    for tracklet, equipo in list(resultado.equipo_de.items()):
        if not recortar(tracklet, equipo) or not viste_en_su_rato(tracklet):
            descartar(tracklet)

    # Los que el detector marca de porteros y no son el portero por la equipación: si visten como un equipo, son un
    # jugador de ese equipo; si como el árbitro, el árbitro; si no se sabe, el equipo de la portería que defienden.
    for tracklet, clase in clases.items():
        if clase != CLASE_PORTERO or tracklet in resultado.equipo_de or tracklet in resultado.sin_equipo:
            continue
        rasgos = fiables.get(tracklet)
        if rasgos is not None:
            d = [float(equipos[e].distancia(rasgos)[0]) for e in (0, 1)]
            cerca = [e for e in (0, 1) if d[e] <= DISTANCIA_DENTRO and d[1 - e] > DISTANCIA_FUERA]
            if cerca:
                resultado.de_campo[tracklet] = cerca[0]
                continue
            if arbitro is not None and float(arbitro.distancia(rasgos)[0]) <= DISTANCIA_DENTRO:
                resultado.sin_equipo.add(tracklet)
                continue
        resultado.del_detector.add(tracklet)
    resultado.por_parte = asignar_porteros(instantes, mitad_de, direcciones, resultado.del_detector, campo)
    # Si se sabe cómo viste el portero del equipo de esa portería, solo el rato en que viste como él; si no hay rato así,
    # si en él viste como los jugadores de campo de algún equipo o como el árbitro, o si tiene muy pocas muestras para
    # saberlo, no es su portero: un jugador de campo que el detector tomó por portero, como el rival que remata en el
    # área, o alguien que no se puede comprobar. Si no se sabe cómo viste, al menos que no vista como los jugadores de
    # campo de ningún equipo: el portero viste distinto de todos ellos.
    equipos_del_detector: dict[int, set[int]] = defaultdict(set)
    for (tracklet, _), equipo in resultado.por_parte.items():
        equipos_del_detector[tracklet].add(equipo)
    for tracklet, suyos in equipos_del_detector.items():
        equipo = next(iter(suyos))
        if len(suyos) == 1 and equipo in resultado.modelos:
            valido = recortar(tracklet, equipo) and viste_en_su_rato(tracklet)
        else:
            rasgos = fiables.get(tracklet)
            valido = rasgos is None or all(float(equipos[e].distancia(rasgos)[0]) > DISTANCIA_FUERA for e in (0, 1))
        if not valido:
            descartar(tracklet)
            resultado.por_parte = {k: e for k, e in resultado.por_parte.items() if k[0] != tracklet}

    # Al revés: el jugador de campo cuyo tracklet empieza o acaba en un portero, o pasa por él en medio (el seguidor saltó
    # entre los dos), se queda sin ese rato, y el que viste como un portero de principio a fin sin que se le haya podido
    # colocar no es nadie.
    if resultado.modelos:
        decididos = set(resultado.equipo_de) | set(resultado.de_campo) | resultado.del_detector | resultado.sin_equipo
        de_porteros = list(resultado.modelos.values())
        for tracklet, serie in series.items():
            if tracklet in decididos or clases.get(tracklet) != CLASE_JUGADOR or muestras[tracklet] < EQUIPACIONES_MINIMAS:
                continue
            rato = _rato_sin_portero(serie, tiempos.get(tracklet), de_porteros, ajenos)
            if rato is None:
                resultado.sin_equipo.add(tracklet)
                continue
            desde, hasta, huecos = rato
            if np.isfinite(desde) or np.isfinite(hasta):
                resultado.ratos[tracklet] = (desde, hasta)
            if huecos:
                resultado.huecos[tracklet] = huecos
    return resultado


def _solo_detector(instantes, mitad_de, direcciones, clases, campo, resultado: Porteros) -> None:
    """Sin saber cómo visten los dos equipos, como hasta la 1.1.0: los que marca el detector, por la portería."""
    resultado.del_detector = {t for t, c in clases.items() if c == CLASE_PORTERO}
    resultado.por_parte = asignar_porteros(instantes, mitad_de, direcciones, resultado.del_detector, campo)


def colocar_porteros(instantes: list[Instante], mitad_de: list[int], memorias: dict, campo: Campo | None = None) -> Porteros:
    """Busca a los porteros con lo que el seguidor sabe de cada tracklet (`seguimiento.MemoriaTracklet`: muestras de la
    equipación y cuándo se tomaron, equipo por la camiseta y clase) y deja en cada fotograma su clase y su equipo."""
    direcciones = direcciones_de_ataque(instantes, mitad_de, {}, campo)
    porteros = buscar_porteros(
        instantes,
        mitad_de,
        direcciones,
        {t: np.asarray(m.equipaciones, dtype=np.float64) for t, m in memorias.items() if m.equipaciones},
        {t: m.equipo for t, m in memorias.items()},
        {t: m.clase for t, m in memorias.items()},
        campo,
        {t: np.asarray(m.tiempos_equipacion, dtype=np.float64) for t, m in memorias.items() if m.equipaciones},
    )
    aplicar_porteros(instantes, mitad_de, porteros)
    return porteros


def momentos_de_portero(porteros: Porteros, equipo: int, tracklets, memorias: dict) -> list[tuple[float, int]]:
    """(segundo del vídeo, tracklet) de cada muestra de la equipación de esos tracklets que viste claramente como el
    portero de ese equipo y cae en el rato que es suyo: de esos momentos, y solo de esos, se enseña un recorte del
    portero (nunca de alguien con quien el seguidor lo confundió). Vacío si no se sabe cómo viste."""
    if equipo not in porteros.modelos:
        return []
    salida = []
    for tid in tracklets:
        memoria = memorias.get(tid)
        if memoria is None:
            continue
        for rasgos, t in zip(memoria.equipaciones, memoria.tiempos_equipacion):
            if np.isfinite(t) and porteros.en_su_rato(tid, t) and porteros.viste_de_portero(equipo, rasgos):
                salida.append((float(t), tid))
    return salida


def describir_porteros(porteros: Porteros, instantes: list[Instante], dt: float) -> str:
    """Una línea para el registro del análisis: el color de cada portero y cuánto se le ve, y lo que se aparta."""
    vistos: dict[int, int] = defaultdict(int)
    for inst in instantes:
        for p in inst.personas:
            if p.tracklet in porteros.equipo_de:
                vistos[p.tracklet] += 1
    partes = []
    for equipo, modelo in sorted(porteros.modelos.items()):
        suyos = [t for t, e in porteros.equipo_de.items() if e == equipo]
        minutos = sum(vistos[t] for t in suyos) * dt / 60
        partes.append(f"el del equipo {equipo} viste de {modelo.color()} ({len(suyos)} tracklets, {minutos:.1f} min a la vista)")
    if partes:
        texto = "Porteros por la equipación: " + " y ".join(partes)
    else:
        texto = "Porteros: ningún color de equipación se distingue del de los equipos y el árbitro junto a las porterías"
    aparte = []
    if porteros.de_campo:
        aparte.append(f"{len(porteros.de_campo)} tracklets que el detector tomó por porteros visten como un equipo y son jugadores de campo")
    if porteros.del_detector:
        aparte.append(f"{len(porteros.del_detector)} porteros solo por el detector, del equipo de la portería que defienden")
    if porteros.sin_equipo:
        aparte.append(f"{len(porteros.sin_equipo)} parecidos a un portero sin equipo (no se pueden colocar)")
    recortados = set(porteros.ratos) | set(porteros.huecos)
    de_portero = sum(1 for t in recortados if porteros.es_portero(t))
    if de_portero or porteros.mezclados:
        aparte.append(
            f"{de_portero} tracklets de portero recortados al rato en que visten como él "
            f"(en {len(porteros.mezclados)} el seguidor saltó a otra persona y de sus lecturas del dorsal solo cuentan las de su rato)"
        )
    if len(recortados) > de_portero:
        aparte.append(f"{len(recortados) - de_portero} de jugadores de campo que empezaban, acababan o pasaban por un portero, sin ese rato")
    return texto + ("; " + "; ".join(aparte) if aparte else "") + "."


def aplicar_porteros(instantes: list[Instante], mitad_de: list[int], porteros: Porteros) -> None:
    """Deja en cada fotograma la clase y el equipo de lo decidido: el portero por la equipación es portero de su equipo en
    todos sus fotogramas; el del detector, del equipo de la portería de esa parte (ya lo puso `asignar_porteros`); el que
    viste como un equipo, jugador de ese equipo; y el que no se puede colocar, sin equipo. Los fotogramas de un portero
    fuera del rato en que viste como él, o en un hueco de en medio, son de otra persona: pasan a otro tracklet, sin
    equipo."""
    for inst, mitad in zip(instantes, mitad_de):
        for p in inst.personas:
            t = p.tracklet
            if not porteros.en_su_rato(t, inst.t):
                p.tracklet, p.clase, p.equipo = TRACKLET_RESTO_BASE + t, CLASE_JUGADOR, None
            elif t in porteros.equipo_de:
                p.clase, p.equipo = CLASE_PORTERO, porteros.equipo_de[t]
            elif t in porteros.de_campo:
                p.clase, p.equipo = CLASE_JUGADOR, porteros.de_campo[t]
            elif t in porteros.sin_equipo:
                p.equipo = None
            elif t in porteros.del_detector:
                p.equipo = porteros.por_parte.get((t, mitad))
