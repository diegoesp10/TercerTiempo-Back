"""Quién es quién: de los miles de tracklets que deja el seguidor a un jugador por dorsal, como mucho los que jugaron.

El seguidor da otro número cada vez que pierde a un jugador (en un partido entero, unos 20.000), pero en el campo hay
como mucho 16 por equipo (once y los cambios). Para juntarlos:

0. **Porteros**: en el campo hay uno por equipo a la vez y no sale de su portería. Su equipo ya viene de la portería
   que defiende en cada parte (`porteros.py`), así que todos los tramos de portero de un equipo, los de las dos partes
   (cada una en un lado), son la misma persona mientras no lo cambien: su dorsal sale de las lecturas de todos juntos (de
   lejos se le lee poco el número) y se le dan todos. Antes se resuelven los jugadores de campo solos: el dorsal que
   dicen con claridad jugadores que se ven a la vez que el portero no es el suyo (en el J8 al 13 morado del Real
   Titánico se le leía sobre todo el 8 y el 6, sus defensas). Si las lecturas no bastan y está el acta, por descarte: el
   único de su equipo que jugó todos los minutos en que se ve al portero y no es ninguno de los jugadores de campo
   identificados (al verde lima del Vallobín casi no se le lee el 1); si queda más de uno, ninguno. Desde la 1.3.0:
   el portero pasa la mayor parte del tiempo al lado o debajo de la portería que defiende su equipo, y la parte en la
   que no (con el campo calibrado un rato) no es suya; su dorsal se compara con el acta (solo los de su equipo que
   estaban en el campo y, si el acta dice quién es portero, solo esos); y sin leerle el número, el descarte solo vale
   si la equipación y la posición lo confirman con claridad: tramos reconocidos por la equipación, no solo por el
   detector, y vistos junto a su portería con el campo calibrado un minuto como poco. Mejor sin identificar que con el
   nombre de otro.
1. **Cadenas**: el tramo que empieza justo donde y cuando acaba otro del mismo equipo es el mismo jugador (se enlazan
   de menor a mayor salto, cada tramo con un solo antecesor y un solo sucesor, y nunca dos que ya dicen dorsales
   distintos con claridad).
2. **Dorsal de cada cadena**: la suma de las lecturas del número de todos sus fotogramas. Con el acta oficial solo
   cuentan los dorsales de los que jugaron, y cada uno solo en sus minutos (el que sale en el 60 no está en el 70).
3. **Un dorsal, una persona a la vez**: si dos cadenas del mismo equipo dicen el mismo dorsal a la vez, se queda la que
   más lo dice y la otra prueba con su segundo dorsal.
4. **Continuidad** (desactivada por defecto, ver HUECO_CONTINUIDAD_S): una cadena sin dorsal que encaja en el hueco de
   un solo jugador (estaba donde él dejó de verse, y él no se ve mientras tanto) es ese jugador.

Lo que no se resuelve queda «sin identificar»: cuenta para el equipo (posesión, mapas), no para ningún jugador."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field

import numpy as np

from .tipos import Punto

ID_JUGADOR_BASE = 900_000
NUMEROS = 99

# Enlace de tramos: salto máximo en el tiempo, velocidad máxima de un jugador y holgura (detección que tiembla). Con el
# vídeo del Vallobín (20:00-30:00, ocultando las lecturas de tramos bien leídos) 2 s ya juntaba a jugadores distintos
# cuando se cruzan; con 1 s, ninguno.
HUECO_MAX_S = 1.0
VELOCIDAD_MAX_CM_S = 900.0
HOLGURA_CM = 300.0
# Sin campo calibrado, en altos de persona por segundo (la cámara también se mueve) y la holgura en altos.
VELOCIDAD_MAX_ALTOS_S = 3.0
HOLGURA_ALTOS = 0.6
# Un tramo «dice» un dorsal con claridad con esta evidencia (suma de lecturas) y esta parte de sus votos.
EVIDENCIA_CLARA = 1.0
PARTE_CLARA = 0.6
# Para dar un dorsal a una cadena.
EVIDENCIA_MIN = 0.8
PARTE_MIN = 0.55
# Solape (s) a partir del cual dos cadenas con el mismo dorsal no pueden ser el mismo jugador.
SOLAPE_MAX_S = 0.6
# Continuidad: hueco máximo hasta el último tramo conocido del jugador (en el campo calibrado y, sin él, en la imagen,
# que con la cámara moviéndose engaña antes), su velocidad media posible y duración mínima de la cadena que se rellena.
# Medido igual que el enlace, la continuidad daba a una cadena sin número el jugador equivocado tres veces por cada una
# que acertaba (en el campo hay varios del mismo equipo cerca y la cámara pierde a muchos a la vez): desactivada (0 s)
# hasta que haya reidentificación por apariencia que la respalde.
HUECO_CONTINUIDAD_S = 0.0
HUECO_CONTINUIDAD_IMAGEN_S = 4.0
VELOCIDAD_CONTINUIDAD_CM_S = 900.0
DURACION_CONTINUIDAD_S = 1.0
# La mejor candidata gana si su coste es menos de esta parte del de la segunda.
VENTAJA_CONTINUIDAD = 0.5
# Minutos del acta: margen (s) por el desfase entre el reloj del partido y el del vídeo.
MARGEN_MINUTOS_S = 300.0
MAXIMO_POR_EQUIPO = 16
# El dorsal de un portero da mucho tiempo de partido de una vez: pide más evidencia que el de una cadena. Por equipo, el
# titular y, si lo cambian, el que entra.
EVIDENCIA_PORTERO = 1.5
PORTEROS_POR_EQUIPO = 2
# Un dorsal está ocupado (no puede ser el del portero) si las cadenas de campo que lo dicen con claridad se ven a la vez
# que el portero al menos SOLAPE_OCUPADO_S: dos personas con el mismo número a la vez no puede ser. Para el descarte basta
# con que se vean en total al menos OCUPADO_MIN_S (ese número ya es de un jugador de campo).
SOLAPE_OCUPADO_S = 15.0
OCUPADO_MIN_S = 60.0
# Con el acta, el dorsal leído tiene que haber estado en el campo en al menos esta parte del tiempo en que se ve al
# portero (el resto es del otro portero si lo cambiaron); y el que sale por descarte, en todo ese tiempo, y solo si se
# ha visto al portero al menos PORTERO_MIN_S.
COBERTURA_LEIDO = 0.4
COBERTURA_DESCARTE = 0.95
PORTERO_MIN_S = 60.0
# El portero pasa la mayor parte del tiempo al lado o debajo de la portería que defiende su equipo: en cada parte en la
# que se le ve con el campo calibrado al menos CALIBRADO_PARTE_S, al menos PARTE_EN_PORTERIA de ese tiempo en la zona del
# portero (`porteros.ZONA_FONDO_CM` de su línea de gol y `porteros.ZONA_ANCHO_CM` del eje). En el J8 los dos porteros
# pasan ahí del 84 al 98 % de cada parte (salen a sacar faltas hasta a 40 m y la calibración a veces se va lejos); la
# parte que no llega no es suya.
PARTE_EN_PORTERIA = 0.7
CALIBRADO_PARTE_S = 20.0
# Sin leerle el dorsal, por descarte solo si la equipación y la posición lo confirman con claridad: sus tramos por la
# equipación (no los que solo marca el detector) y vistos junto a su portería con el campo calibrado al menos esto.
CALIBRADO_DESCARTE_S = 60.0


def id_jugador(equipo: int, dorsal: int) -> int:
    return ID_JUGADOR_BASE + equipo * 1000 + dorsal


def es_id_jugador(tracklet: int) -> bool:
    return tracklet >= ID_JUGADOR_BASE


def dorsal_de(tracklet: int) -> int:
    return (tracklet - ID_JUGADOR_BASE) % 1000


@dataclass
class Tramo:
    """Un tracklet: su equipo, cuándo y dónde empieza y acaba, y las lecturas de su dorsal."""

    tracklet: int
    equipo: int
    t_ini: float
    t_fin: float
    ini: Punto
    fin: Punto
    alto_px: float
    votos: np.ndarray = field(default_factory=lambda: np.zeros(NUMEROS))
    ini_campo: Punto | None = None
    fin_campo: Punto | None = None
    portero: bool = False
    # Portero por la equipación (`porteros.py`), no solo porque lo diga el detector: es el portero aunque una lectura
    # suelta diga otro número.
    por_equipacion: bool = False
    # Del portero: la parte del partido en la que empieza y, por parte, los segundos con el campo calibrado y, de ellos,
    # los que está al lado o debajo de la portería que defiende su equipo (`porteros.Porteros.junto_a_su_porteria`).
    mitad: int | None = None
    porteria: dict[int, tuple[float, float]] = field(default_factory=dict)
    # Viste como el portero de su equipo: por la equipación o, de los que solo marca el detector, en su rato
    # (`porteros.Porteros.vestido_de_portero`). Si no es de ningún portero identificado, se queda sin identificar.
    vestido_de_portero: bool = False

    @property
    def duracion(self) -> float:
        return max(0.0, self.t_fin - self.t_ini)


@dataclass
class JugadorActa:
    dorsal: int
    nombre: str = ""
    # Segundos del vídeo en los que pudo estar en el campo (None: desde el principio / hasta el final).
    desde_s: float | None = None
    hasta_s: float | None = None
    # Si el acta (o la ficha) dice que es portero: si en un equipo hay alguno, su portero solo puede ser uno de ellos.
    portero: bool = False

    def en_juego(self, t_ini: float, t_fin: float, margen: float = MARGEN_MINUTOS_S) -> bool:
        if self.desde_s is not None and t_fin < self.desde_s - margen:
            return False
        if self.hasta_s is not None and t_ini > self.hasta_s + margen:
            return False
        return True


@dataclass
class Plantilla:
    lado: str
    nombre: str
    jugadores: list[JugadorActa]

    def dorsales(self) -> set[int]:
        return {j.dorsal for j in self.jugadores}

    def porteros(self) -> set[int]:
        return {j.dorsal for j in self.jugadores if j.portero}


@dataclass
class Cadena:
    tramos: list[Tramo]

    @property
    def equipo(self) -> int:
        return self.tramos[0].equipo

    @property
    def t_ini(self) -> float:
        return self.tramos[0].t_ini

    @property
    def t_fin(self) -> float:
        return self.tramos[-1].t_fin

    @property
    def duracion(self) -> float:
        return sum(t.duracion for t in self.tramos)

    @property
    def votos(self) -> np.ndarray:
        return np.sum([t.votos for t in self.tramos], axis=0)


@dataclass
class Identidad:
    equipo: int
    dorsal: int
    nombre: str | None
    tracklets: list[int]
    evidencia: float
    # Parte de las lecturas de sus tramos que dicen su dorsal (entre los permitidos).
    confianza: float
    # Cadenas que se le dieron por continuidad, sin leerle el dorsal.
    por_continuidad: int = 0
    # Portero cuyo dorsal sale del acta por descarte, no de las lecturas.
    por_descarte: bool = False
    # Su línea sale de los tramos de un portero (`_porteros`), no solo de los de campo: un tracklet que el detector tomó
    # por portero y acabó en un jugador de campo no lo hace portero.
    portero: bool = False

    @property
    def id(self) -> int:
        return id_jugador(self.equipo, self.dorsal)


@dataclass
class Resolucion:
    asignacion: dict[int, int]
    identidades: list[Identidad]
    lado_de_equipo: dict[int, str | None]
    cadenas: int
    segundos_sin_identificar: dict[int, float]


def _distancia(a: Punto, b: Punto) -> float:
    return float(np.hypot(a[0] - b[0], a[1] - b[1]))


def coste_enlace(a: Tramo, b: Tramo) -> float | None:
    """Lo raro que es que `b` siga a `a` (0 = donde acabó y enseguida; 1 = en el límite), o None si no puede ser."""
    if a.equipo != b.equipo:
        return None
    hueco = b.t_ini - a.t_fin
    if hueco <= 0 or hueco > HUECO_MAX_S:
        return None
    if a.fin_campo is not None and b.ini_campo is not None:
        limite = VELOCIDAD_MAX_CM_S * hueco + HOLGURA_CM
        d = _distancia(a.fin_campo, b.ini_campo)
    else:
        alto = max(1.0, (a.alto_px + b.alto_px) / 2)
        limite = (VELOCIDAD_MAX_ALTOS_S * hueco + HOLGURA_ALTOS) * alto
        d = _distancia(a.fin, b.ini)
    if d > limite:
        return None
    if _dorsales_chocan(a.votos, b.votos):
        return None
    return 0.7 * d / limite + 0.3 * hueco / HUECO_MAX_S


def _claro(votos: np.ndarray) -> int | None:
    total = float(votos.sum())
    if total <= 0:
        return None
    i = int(votos.argmax())
    return i + 1 if votos[i] >= EVIDENCIA_CLARA and votos[i] / total >= PARTE_CLARA else None


def _dorsales_chocan(a: np.ndarray, b: np.ndarray) -> bool:
    da, db = _claro(a), _claro(b)
    return da is not None and db is not None and da != db


def enlazar(tramos: list[Tramo]) -> list[Cadena]:
    """Junta en cadenas los tramos que se siguen (cada tramo con un solo antecesor y un solo sucesor)."""
    por_inicio = sorted(tramos, key=lambda t: t.t_ini)
    inicios = np.array([t.t_ini for t in por_inicio])
    candidatos: list[tuple[float, int, int]] = []
    indice = {t.tracklet: i for i, t in enumerate(por_inicio)}
    for a in por_inicio:
        desde = int(np.searchsorted(inicios, a.t_fin, side="right"))
        hasta = int(np.searchsorted(inicios, a.t_fin + HUECO_MAX_S, side="right"))
        for j in range(desde, hasta):
            b = por_inicio[j]
            coste = coste_enlace(a, b)
            if coste is not None:
                candidatos.append((coste, indice[a.tracklet], j))
    candidatos.sort()
    siguiente: dict[int, int] = {}
    anterior: dict[int, int] = {}
    for _, i, j in candidatos:
        if i in siguiente or j in anterior:
            continue
        siguiente[i] = j
        anterior[j] = i
    cadenas = []
    for i in range(len(por_inicio)):
        if i in anterior:
            continue
        cadena = [por_inicio[i]]
        while i in siguiente:
            i = siguiente[i]
            cadena.append(por_inicio[i])
        cadenas.append(Cadena(cadena))
    return cadenas


def _permitidos(cadena: Cadena, plantilla: Plantilla | None) -> np.ndarray:
    """Máscara de los dorsales posibles para la cadena: los del acta que estaban en juego en esos minutos."""
    mascara = np.zeros(NUMEROS, dtype=bool)
    if plantilla is None:
        mascara[:] = True
        return mascara
    for j in plantilla.jugadores:
        if 1 <= j.dorsal <= NUMEROS and j.en_juego(cadena.t_ini, cadena.t_fin):
            mascara[j.dorsal - 1] = True
    return mascara


def _emparejar_plantillas(cadenas: list[Cadena], plantillas: list[Plantilla]) -> dict[int, Plantilla | None]:
    """Qué plantilla del acta es cada equipo del vídeo: el reparto en el que más lecturas caen en dorsales de los que
    jugaron. Si no se distingue (los dos equipos usan casi los mismos números), ninguno."""
    if not plantillas:
        return {0: None, 1: None}
    if len(plantillas) == 1:
        p = plantillas[0]
        puntos = [sum(float(c.votos[_permitidos(c, p)].sum()) for c in cadenas if c.equipo == e) / max(1e-6, sum(float(c.votos.sum()) for c in cadenas if c.equipo == e)) for e in (0, 1)]
        return {0: p, 1: None} if puntos[0] >= puntos[1] else {0: None, 1: p}

    def puntuacion(e: int, p: Plantilla) -> float:
        return sum(float(c.votos[_permitidos(c, p)].sum()) for c in cadenas if c.equipo == e)

    directo = puntuacion(0, plantillas[0]) + puntuacion(1, plantillas[1])
    cruzado = puntuacion(0, plantillas[1]) + puntuacion(1, plantillas[0])
    if max(directo, cruzado) <= 0 or abs(directo - cruzado) / max(directo, cruzado) < 0.05:
        return {0: None, 1: None}
    return {0: plantillas[0], 1: plantillas[1]} if directo > cruzado else {0: plantillas[1], 1: plantillas[0]}


def _union(plantillas: list[Plantilla]) -> Plantilla | None:
    if not plantillas:
        return None
    vistos: dict[int, JugadorActa] = {}
    for p in plantillas:
        for j in p.jugadores:
            # Sin saber de qué equipo es, cada dorsal vale para todo el partido.
            vistos.setdefault(j.dorsal, JugadorActa(j.dorsal))
    return Plantilla("", "", list(vistos.values()))


def _dorsal_de_cadena(votos: np.ndarray, permitidos: np.ndarray, excluidos: set[int]) -> tuple[int, float, float] | None:
    v = np.where(permitidos, votos, 0.0)
    for d in excluidos:
        v[d - 1] = 0.0
    total = float(v.sum())
    if total <= 0:
        return None
    i = int(v.argmax())
    evidencia, parte = float(v[i]), float(v[i]) / total
    if evidencia < EVIDENCIA_MIN or parte < PARTE_MIN:
        return None
    return i + 1, evidencia, parte


def _solapan(a: Cadena, b: Cadena) -> bool:
    """Si están a la vez más de SOLAPE_MAX_S, tramo a tramo: los de un portero dejan huecos cuando no se le ve."""
    if min(a.t_fin, b.t_fin) - max(a.t_ini, b.t_ini) <= SOLAPE_MAX_S:
        return False
    total = 0.0
    i = j = 0
    while i < len(a.tramos) and j < len(b.tramos):
        x, y = a.tramos[i], b.tramos[j]
        total += max(0.0, min(x.t_fin, y.t_fin) - max(x.t_ini, y.t_ini))
        if total > SOLAPE_MAX_S:
            return True
        if x.t_fin < y.t_fin:
            i += 1
        else:
            j += 1
    return False


def _votos_permitidos(tramo: Tramo, plantilla: Plantilla | None) -> np.ndarray:
    return np.where(_permitidos(Cadena([tramo]), plantilla), tramo.votos, 0.0)


def _jugador(plantilla: Plantilla | None, dorsal: int) -> JugadorActa | None:
    return next((j for j in plantilla.jugadores if j.dorsal == dorsal), None) if plantilla is not None else None


def _intervalos(tramos: list[Tramo]) -> list[tuple[float, float]]:
    """Los ratos en que se ve alguno de los tramos, juntos y en orden."""
    juntos: list[list[float]] = []
    for t in sorted(tramos, key=lambda t: t.t_ini):
        if juntos and t.t_ini <= juntos[-1][1]:
            juntos[-1][1] = max(juntos[-1][1], t.t_fin)
        else:
            juntos.append([t.t_ini, t.t_fin])
    return [(a, b) for a, b in juntos]


def _a_la_vez(a: list[tuple[float, float]], b: list[tuple[float, float]]) -> float:
    """Segundos en que coinciden dos listas de ratos de `_intervalos`."""
    total = 0.0
    i = j = 0
    while i < len(a) and j < len(b):
        total += max(0.0, min(a[i][1], b[j][1]) - max(a[i][0], b[j][0]))
        if a[i][1] < b[j][1]:
            i += 1
        else:
            j += 1
    return total


def _cobertura(tramos: list[Tramo], jugador: JugadorActa | None) -> float:
    """La parte del tiempo de los tramos en que el jugador del acta estaba en el campo (sin acta, todo)."""
    total = sum(t.duracion for t in tramos)
    if jugador is None or total <= 0:
        return 1.0
    return sum(t.duracion for t in tramos if jugador.en_juego(t.t_ini, t.t_fin)) / total


def _dorsales(
    cadenas: list[Cadena],
    fijas: dict[int, tuple[int, float, float]],
    plantilla_de: dict[int, Plantilla | None],
    vetados: dict[int, set[int]] | None = None,
) -> dict[int, tuple[int, float, float]]:
    """El dorsal de cada cadena y, por equipo y dorsal, los choques resueltos hasta que no quede ninguno: si dos cadenas
    lo dicen a la vez, se queda la que más lo dice y la otra prueba con su segundo número. Las `fijas` (los porteros) ya
    traen su dorsal y van delante: lo dicen las lecturas de todos sus tramos y no salen de su portería. Los `vetados` de
    cada equipo no son de ninguna de sus otras cadenas, que prueban con su segundo número."""
    vetados = vetados or {}
    asignada = dict(fijas)
    excluidos: dict[int, set[int]] = {i: set() if i in fijas else set(vetados.get(cadenas[i].equipo, ())) for i in range(len(cadenas))}
    pendientes = [i for i in range(len(cadenas)) if i not in fijas]
    for _ in range(4):
        for i in pendientes:
            c = cadenas[i]
            r = _dorsal_de_cadena(c.votos, _permitidos(c, plantilla_de[c.equipo]), excluidos[i])
            if r is not None:
                asignada[i] = r
            else:
                asignada.pop(i, None)
        pendientes = []
        por_dorsal: dict[tuple[int, int], list[int]] = {}
        for i, (d, _, _) in asignada.items():
            por_dorsal.setdefault((cadenas[i].equipo, d), []).append(i)
        for (e, d), lista in por_dorsal.items():
            lista.sort(key=lambda i: (i not in fijas, -asignada[i][1]))
            aceptadas: list[int] = []
            for i in lista:
                if any(_solapan(cadenas[i], cadenas[k]) for k in aceptadas):
                    excluidos[i].add(d)
                    pendientes.append(i)
                else:
                    aceptadas.append(i)
        if not pendientes:
            break
    for i in pendientes:
        asignada.pop(i, None)
    return asignada


def _ocupados(cadenas: list[Cadena], asignada: dict[int, tuple[int, float, float]], porteros: list[Tramo]) -> tuple[dict[int, set[int]], dict[int, set[int]]]:
    """Con los jugadores de campo resueltos sin los porteros, los dorsales que no pueden ser del portero de cada equipo:
    los que dicen con claridad cadenas que se ven a la vez que él (para elegir por las lecturas) y, además, los de los
    jugadores de campo que se ven un buen rato (para elegir por descarte)."""
    ratos = {e: _intervalos([t for t in porteros if t.equipo == e]) for e in (0, 1)}
    a_la_vez: dict[tuple[int, int], float] = defaultdict(float)
    vistos: dict[tuple[int, int], float] = defaultdict(float)
    for i, (d, _, _) in asignada.items():
        c = cadenas[i]
        if c.equipo not in ratos or _claro(c.votos) != d:
            continue
        a_la_vez[(c.equipo, d)] += _a_la_vez(_intervalos(c.tramos), ratos[c.equipo])
        vistos[(c.equipo, d)] += c.duracion
    ocupados = {e: {d for (x, d), s in a_la_vez.items() if x == e and s >= SOLAPE_OCUPADO_S} for e in (0, 1)}
    contados = {e: ocupados[e] | {d for (x, d), s in vistos.items() if x == e and s >= OCUPADO_MIN_S} for e in (0, 1)}
    return ocupados, contados


def _partes_lejos_de_porteria(tramos: list[Tramo]) -> set[int]:
    """Las partes en las que los tramos del portero de un equipo, vistos con el campo calibrado al menos
    CALIBRADO_PARTE_S, no pasan ni PARTE_EN_PORTERIA de ese tiempo al lado o debajo de la portería que defiende su equipo:
    en esa parte no es su portero. Se mide sobre toda la parte y no tramo a tramo: el portero sale a sacar faltas y la
    calibración a veces se va lejos un momento."""
    calibrado: dict[int, float] = defaultdict(float)
    junto: dict[int, float] = defaultdict(float)
    for t in tramos:
        for mitad, (segundos, cerca) in t.porteria.items():
            calibrado[mitad] += segundos
            junto[mitad] += cerca
    return {m for m, s in calibrado.items() if s >= CALIBRADO_PARTE_S and junto[m] < PARTE_EN_PORTERIA * s}


def _junto_a_su_porteria(tramos: list[Tramo]) -> float:
    """Segundos con el campo calibrado en que los tramos están al lado o debajo de la portería que defiende su equipo."""
    return sum(cerca for t in tramos for _, cerca in t.porteria.values())


def _mejor_portero(tramos: list[Tramo], votos: dict[int, np.ndarray], plantilla: Plantilla | None, fuera: set[int]) -> tuple[int, float, float] | None:
    """El dorsal del portero por las lecturas: el que más se lee de los que pueden ser (ni otro portero ya identificado ni
    un número ocupado por un jugador de campo, y si el acta dice quiénes son porteros, uno de ellos), contando solo los
    tramos de los minutos en que estuvo en el campo (si lo cambiaron, el titular y el que entró se reparten las lecturas
    del partido) y, con el acta, solo si estuvo en el campo al menos COBERTURA_LEIDO del tiempo en que se ve al
    portero."""
    marcados = plantilla.porteros() if plantilla is not None else set()
    mejor: tuple[int, float, float] | None = None
    for indice in np.flatnonzero(np.sum([votos[t.tracklet] for t in tramos], axis=0) > 0):
        dorsal = int(indice) + 1
        if dorsal in fuera or (marcados and dorsal not in marcados):
            continue
        jugador = _jugador(plantilla, dorsal)
        suyos = [votos[t.tracklet] for t in tramos if jugador is None or jugador.en_juego(t.t_ini, t.t_fin)]
        if not suyos or _cobertura(tramos, jugador) < COBERTURA_LEIDO:
            continue
        suma = np.sum(suyos, axis=0)
        for d in fuera:
            suma[d - 1] = 0.0
        evidencia, total = float(suma[indice]), float(suma.sum())
        if evidencia >= EVIDENCIA_PORTERO and evidencia / total >= PARTE_MIN and (mejor is None or evidencia > mejor[1]):
            mejor = (dorsal, evidencia, evidencia / total)
    return mejor


def _por_descarte(tramos: list[Tramo], votos: dict[int, np.ndarray], plantilla: Plantilla, fuera: set[int]) -> tuple[int, float, float] | None:
    """Si las lecturas no bastan, con el acta: el único de su equipo que estuvo en el campo todo el tiempo en que se ve al
    portero y no es otro portero ni un jugador de campo identificado (si el acta dice quiénes son porteros, uno de ellos).
    Sin leerle el número, solo si la equipación y la posición lo confirman con claridad: cuentan solo sus tramos por la
    equipación (no los que solo marca el detector), que tienen que sumar PORTERO_MIN_S y estar al menos
    CALIBRADO_DESCARTE_S, con el campo calibrado, al lado o debajo de su portería."""
    de_equipacion = [t for t in tramos if t.por_equipacion]
    if sum(t.duracion for t in de_equipacion) < PORTERO_MIN_S or _junto_a_su_porteria(de_equipacion) < CALIBRADO_DESCARTE_S:
        return None
    marcados = plantilla.porteros()
    candidatos = [
        j.dorsal
        for j in plantilla.jugadores
        if j.dorsal not in fuera and (not marcados or j.dorsal in marcados) and _cobertura(de_equipacion, j) >= COBERTURA_DESCARTE
    ]
    if len(candidatos) != 1:
        return None
    dorsal = candidatos[0]
    suma = np.sum([votos[t.tracklet] for t in tramos], axis=0)
    total = float(suma.sum())
    evidencia = float(suma[dorsal - 1])
    return dorsal, evidencia, evidencia / total if total > 0 else 0.0


def _porteros(
    tramos: list[Tramo],
    plantilla_de: dict[int, Plantilla | None],
    ocupados: dict[int, set[int]],
    contados: dict[int, set[int]],
    con_acta: dict[int, bool],
) -> tuple[list[tuple[Cadena, tuple[int, float, float], bool]], list[Tramo], list[Tramo]]:
    """Cada portero identificado (todos sus tramos, su dorsal y si sale por descarte); los tramos de portero que se quedan
    para resolverse como los de campo; y los que visten como su portero (por la equipación o, de los que solo marca el
    detector, en su rato) y no son de ningún portero identificado, que se quedan sin identificar: como jugadores de campo,
    sus dorsales mal leídos los juntarían con los defensas, o la continuidad con el que pasaba a su lado.

    Los tramos del portero por la equipación van delante. Los del detector se quedan para los de campo si coinciden en el
    tiempo con otro de su equipo ya aceptado (dos porteros a la vez no puede ser) o si dicen con claridad otro número (un
    jugador que el detector tomó por portero), salvo si visten como su portero; el del portero por la equipación es el
    portero aunque se le lea otro número. En la parte en la que no pasan la mayor parte del tiempo junto a la portería
    que defiende su equipo (`_partes_lejos_de_porteria`) ninguno es su portero. Con el acta, solo cuentan los dorsales de
    su equipo que estaban en el campo en cada tramo; y si cambian al portero, el que entra sale de los tramos que quedan
    fuera de los minutos del primero. El portero que sale por descarte solo se queda con sus tramos por la equipación:
    los que solo marca el detector, sin número que lo diga, no son suyos."""
    identificados: list[tuple[Cadena, tuple[int, float, float], bool]] = []
    sueltos: list[Tramo] = []
    perdidos: list[Tramo] = []

    def apartar(t: Tramo) -> None:
        (perdidos if t.por_equipacion or t.vestido_de_portero else sueltos).append(t)

    for equipo in (0, 1):
        aceptados: list[Tramo] = []
        for t in sorted((t for t in tramos if t.equipo == equipo), key=lambda t: (not t.por_equipacion, -t.duracion)):
            if any(min(t.t_fin, a.t_fin) - max(t.t_ini, a.t_ini) > SOLAPE_MAX_S for a in aceptados):
                apartar(t)
            else:
                aceptados.append(t)
        lejos = _partes_lejos_de_porteria(aceptados)
        if lejos:
            for t in aceptados:
                if t.mitad in lejos:
                    apartar(t)
            aceptados = [t for t in aceptados if t.mitad not in lejos]
        plantilla = plantilla_de.get(equipo)
        votos = {t.tracklet: _votos_permitidos(t, plantilla) for t in aceptados}
        pendientes = sorted(aceptados, key=lambda t: t.t_ini)
        vistos: set[int] = set()
        for _ in range(PORTEROS_POR_EQUIPO):
            if not pendientes:
                break
            descarte = False
            elegido = _mejor_portero(pendientes, votos, plantilla, vistos | ocupados.get(equipo, set()))
            if elegido is None and con_acta.get(equipo) and plantilla is not None:
                elegido = _por_descarte(pendientes, votos, plantilla, vistos | contados.get(equipo, set()))
                descarte = elegido is not None
            if elegido is None:
                break
            dorsal = elegido[0]
            vistos.add(dorsal)
            jugador = _jugador(plantilla, dorsal)
            suyos: list[Tramo] = []
            resto: list[Tramo] = []
            for t in pendientes:
                if jugador is not None and not jugador.en_juego(t.t_ini, t.t_fin):
                    resto.append(t)
                elif descarte and not t.por_equipacion:
                    # Sin leerle el número, solo es él lo que confirma la equipación.
                    apartar(t)
                elif t.por_equipacion or _claro(votos[t.tracklet]) in (None, dorsal):
                    suyos.append(t)
                else:
                    apartar(t)
            pendientes = resto
            if suyos:
                suma = np.sum([votos[t.tracklet] for t in suyos], axis=0)
                total = float(suma.sum())
                evidencia = float(suma[dorsal - 1])
                identificados.append((Cadena(suyos), (dorsal, evidencia, evidencia / total if total > 0 else 0.0), descarte))
        for t in pendientes:
            apartar(t)
    return identificados, sueltos, perdidos


def resolver(tramos: list[Tramo], plantillas: list[Plantilla] | None = None, maximo: int = MAXIMO_POR_EQUIPO) -> Resolucion:
    plantillas = plantillas or []
    de_campo = [t for t in tramos if not t.portero]
    del_portero = [t for t in tramos if t.portero]
    cadenas_campo = enlazar(de_campo)
    de_equipo = _emparejar_plantillas(cadenas_campo, plantillas)
    lado_de_equipo = {e: (p.lado if p is not None else None) for e, p in de_equipo.items()}
    comun = _union(plantillas) if plantillas and any(p is None for p in de_equipo.values()) else None
    plantilla_de = {e: de_equipo[e] or comun for e in (0, 1)}

    # Los porteros primero, con los jugadores de campo resueltos solos para saber qué dorsales ya llevan otros; los tramos
    # de portero del detector que no son de ninguno ni visten como su portero se resuelven como los de campo.
    ocupados: dict[int, set[int]] = {0: set(), 1: set()}
    contados: dict[int, set[int]] = {0: set(), 1: set()}
    if del_portero:
        ocupados, contados = _ocupados(cadenas_campo, _dorsales(cadenas_campo, {}, plantilla_de), del_portero)
    porteros, sueltos, perdidos = _porteros(del_portero, plantilla_de, ocupados, contados, {e: de_equipo[e] is not None for e in (0, 1)})
    cadenas = enlazar(de_campo + sueltos) if sueltos else cadenas_campo
    de_campo_hasta = len(cadenas)
    cadenas = cadenas + [c for c, _, _ in porteros]
    fijas = {de_campo_hasta + k: r for k, (_, r, _) in enumerate(porteros)}
    por_descarte = {de_campo_hasta + k for k, (_, _, descarte) in enumerate(porteros) if descarte}
    # El portero que se reconoce por la equipación no está en ninguna cadena de campo (por la equipación no son él): su
    # dorsal no pasa a ninguna otra de su equipo, ni por las lecturas ni por continuidad. Un defensa al que se lee su
    # número cuando al portero no se le ve acabaría en su línea.
    vetados: dict[int, set[int]] = defaultdict(set)
    for c, (d, _, _), _ in porteros:
        if any(t.por_equipacion for t in c.tramos):
            vetados[c.equipo].add(d)
    asignada = _dorsales(cadenas, fijas, plantilla_de, vetados)

    # Sin acta: solo los dorsales con más evidencia, como mucho `maximo` por equipo.
    for e in (0, 1):
        evidencia: dict[int, float] = {}
        for i, (d, ev, _) in asignada.items():
            if cadenas[i].equipo == e:
                evidencia[d] = evidencia.get(d, 0.0) + ev
        sobran = sorted(evidencia, key=lambda d: -evidencia[d])[maximo:]
        for i in [i for i, (d, _, _) in asignada.items() if cadenas[i].equipo == e and d in sobran]:
            del asignada[i]

    por_continuidad = _continuidad(cadenas, asignada, vetados)

    nombres: dict[tuple[int, int], str] = {}
    for e in (0, 1):
        p = plantilla_de[e]
        if p is not None and de_equipo[e] is not None:
            for j in p.jugadores:
                nombres[(e, j.dorsal)] = j.nombre
    identidades: dict[tuple[int, int], Identidad] = {}
    asignacion: dict[int, int] = {}
    for i, (d, ev, parte) in asignada.items():
        c = cadenas[i]
        clave = (c.equipo, d)
        ident = identidades.get(clave)
        if ident is None:
            ident = identidades[clave] = Identidad(c.equipo, d, nombres.get(clave) or None, [], 0.0, 0.0)
        ident.tracklets.extend(t.tracklet for t in c.tramos)
        ident.evidencia += ev
        if i in por_continuidad:
            ident.por_continuidad += 1
        if i in por_descarte:
            ident.por_descarte = True
        if i >= de_campo_hasta:
            ident.portero = True
        for t in c.tramos:
            asignacion[t.tracklet] = ident.id
    for ident in identidades.values():
        votos = np.sum([cadenas[i].votos for i, (d, _, _) in asignada.items() if (cadenas[i].equipo, d) == (ident.equipo, ident.dorsal)], axis=0)
        total = float(votos.sum())
        ident.confianza = round(float(votos[ident.dorsal - 1]) / total, 3) if total > 0 else 0.0

    sin = {e: 0.0 for e in (0, 1)}
    for i, c in enumerate(cadenas):
        if i not in asignada and c.equipo in sin:
            sin[c.equipo] += c.duracion
    for t in perdidos:
        if t.equipo in sin:
            sin[t.equipo] += t.duracion
    orden = sorted(identidades.values(), key=lambda x: (x.equipo, x.dorsal))
    return Resolucion(asignacion, orden, lado_de_equipo, len(cadenas), {e: round(s, 1) for e, s in sin.items()})


def _continuidad(cadenas: list[Cadena], asignada: dict[int, tuple[int, float, float]], vetados: dict[int, set[int]] | None = None) -> set[int]:
    """Da su jugador a las cadenas sin dorsal que caben en el hueco de uno solo (nunca uno de los `vetados` de su
    equipo). Devuelve las que se rellenaron."""
    vetados = vetados or {}
    rellenadas: set[int] = set()
    for _ in range(3):
        cambios = 0
        por_jugador: dict[tuple[int, int], list[int]] = {}
        for i, (d, _, _) in asignada.items():
            por_jugador.setdefault((cadenas[i].equipo, d), []).append(i)
        for lista in por_jugador.values():
            lista.sort(key=lambda i: cadenas[i].t_ini)
        for i, c in enumerate(cadenas):
            if i in asignada or c.duracion < DURACION_CONTINUIDAD_S:
                continue
            posibles: list[tuple[float, int]] = []
            for (e, d), lista in por_jugador.items():
                if e != c.equipo or d in vetados.get(e, ()) or any(_solapan(c, cadenas[k]) for k in lista):
                    continue
                antes = [k for k in lista if cadenas[k].t_fin <= c.t_ini]
                despues = [k for k in lista if cadenas[k].t_ini >= c.t_fin]
                costes = []
                if antes:
                    a = cadenas[antes[-1]].tramos[-1]
                    costes.append(_coste_hueco(a, c.tramos[0]))
                if despues:
                    b = cadenas[despues[0]].tramos[0]
                    costes.append(_coste_hueco(c.tramos[-1], b))
                if not costes or any(x is None for x in costes):
                    continue
                posibles.append((min(costes), d))
            if len(posibles) == 1 or (len(posibles) > 1 and sorted(posibles)[0][0] < VENTAJA_CONTINUIDAD * sorted(posibles)[1][0]):
                d = min(posibles)[1]
                asignada[i] = (d, 0.0, 0.0)
                rellenadas.add(i)
                cambios += 1
        if not cambios:
            break
    return rellenadas


def _coste_hueco(a: Tramo, b: Tramo) -> float | None:
    hueco = b.t_ini - a.t_fin
    if hueco < 0 or hueco > HUECO_CONTINUIDAD_S:
        return None
    if a.fin_campo is not None and b.ini_campo is not None:
        limite = VELOCIDAD_CONTINUIDAD_CM_S * hueco + HOLGURA_CM
        d = _distancia(a.fin_campo, b.ini_campo)
    else:
        if hueco > HUECO_CONTINUIDAD_IMAGEN_S:
            return None
        alto = max(1.0, (a.alto_px + b.alto_px) / 2)
        limite = (VELOCIDAD_MAX_ALTOS_S * hueco + HOLGURA_ALTOS) * alto
        d = _distancia(a.fin, b.ini)
    return None if d > limite else d / limite


def minuto_a_segundos(minuto: float, saque_s: float, descanso_s: float | None, parte: str = "partido", minutos_parte: float = 45) -> float:
    """Minuto del partido → segundo del vídeo. Con el partido entero, la primera parte desde el saque inicial y la segunda
    desde el descanso (si no se sabe, con un cuarto de hora de descanso); con un vídeo de una sola parte, desde su saque.
    En la segunda parte los minutos del acta siguen contando desde el inicio del partido."""
    if parte == "primera":
        return saque_s + minuto * 60
    if parte == "segunda":
        return saque_s + max(0.0, minuto - minutos_parte) * 60
    if minuto <= minutos_parte:
        return saque_s + minuto * 60
    inicio_segunda = descanso_s if descanso_s is not None else saque_s + (minutos_parte + 15) * 60
    return inicio_segunda + (minuto - minutos_parte) * 60


def leer_plantillas(datos: dict, saque_s: float = 0.0, descanso_s: float | None = None) -> list[Plantilla]:
    """Las plantillas del acta oficial que pasa la app: `{"equipos": [{"lado", "nombre", "jugadores": [{"dorsal",
    "nombre", "titular", "entra", "sale", "portero"}]}], "parte", "minutos_parte"}` con los minutos de entrada y salida
    (null: todo el partido), si es portero (si no viene, no se sabe), de qué parte es el vídeo («partido», «primera» o
    «segunda») y cuánto dura cada parte. Solo los que jugaron (titulares o que entraron) y, con una sola parte, los que
    estuvieron en ella."""
    parte = datos.get("parte") if datos.get("parte") in ("partido", "primera", "segunda") else "partido"
    mitad = float(datos.get("minutos_parte") or 45)

    def segundo(minuto) -> float:
        return minuto_a_segundos(float(minuto), saque_s, descanso_s, parte, mitad)

    plantillas = []
    for equipo in datos.get("equipos", []):
        jugadores = []
        for j in equipo.get("jugadores", []):
            dorsal = j.get("dorsal")
            if not isinstance(dorsal, int) or not 1 <= dorsal <= NUMEROS:
                continue
            titular, entra, sale = bool(j.get("titular")), j.get("entra"), j.get("sale")
            if not titular and entra is None:
                continue
            if parte == "primera" and not titular and float(entra) >= mitad:
                continue
            if parte == "segunda" and sale is not None and float(sale) <= mitad:
                continue
            desde = None if titular or not entra else segundo(entra)
            if parte == "segunda" and desde is not None and float(entra) <= mitad:
                desde = None
            jugadores.append(JugadorActa(dorsal, j.get("nombre") or "", desde, None if sale is None else segundo(sale), j.get("portero") is True))
        if jugadores:
            plantillas.append(Plantilla(equipo.get("lado") or "", equipo.get("nombre") or "", jugadores))
    return plantillas
