"""Quién es quién: de los miles de tracklets que deja el seguidor a un jugador por dorsal, como mucho los que jugaron.

El seguidor da otro número cada vez que pierde a un jugador (en un partido entero, unos 20.000), pero en el campo hay
como mucho 16 por equipo (once y los cambios). Para juntarlos:

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
    return min(a.t_fin, b.t_fin) - max(a.t_ini, b.t_ini) > SOLAPE_MAX_S


def resolver(tramos: list[Tramo], plantillas: list[Plantilla] | None = None, maximo: int = MAXIMO_POR_EQUIPO) -> Resolucion:
    plantillas = plantillas or []
    cadenas = enlazar(tramos)
    de_equipo = _emparejar_plantillas(cadenas, plantillas)
    lado_de_equipo = {e: (p.lado if p is not None else None) for e, p in de_equipo.items()}
    comun = _union(plantillas) if plantillas and any(p is None for p in de_equipo.values()) else None
    plantilla_de = {e: de_equipo[e] or comun for e in (0, 1)}

    asignada: dict[int, tuple[int, float, float]] = {}
    excluidos: dict[int, set[int]] = {i: set() for i in range(len(cadenas))}
    pendientes = list(range(len(cadenas)))
    # Por equipo y dorsal, las cadenas que lo tienen; se resuelven los choques hasta que no quede ninguno.
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
            lista.sort(key=lambda i: -asignada[i][1])
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

    # Sin acta: solo los dorsales con más evidencia, como mucho `maximo` por equipo.
    for e in (0, 1):
        evidencia: dict[int, float] = {}
        for i, (d, ev, _) in asignada.items():
            if cadenas[i].equipo == e:
                evidencia[d] = evidencia.get(d, 0.0) + ev
        sobran = sorted(evidencia, key=lambda d: -evidencia[d])[maximo:]
        for i in [i for i, (d, _, _) in asignada.items() if cadenas[i].equipo == e and d in sobran]:
            del asignada[i]

    por_continuidad = _continuidad(cadenas, asignada)

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
    orden = sorted(identidades.values(), key=lambda x: (x.equipo, x.dorsal))
    return Resolucion(asignacion, orden, lado_de_equipo, len(cadenas), {e: round(s, 1) for e, s in sin.items()})


def _continuidad(cadenas: list[Cadena], asignada: dict[int, tuple[int, float, float]]) -> set[int]:
    """Da su jugador a las cadenas sin dorsal que caben en el hueco de uno solo. Devuelve las que se rellenaron."""
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
                if e != c.equipo or any(_solapan(c, cadenas[k]) for k in lista):
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
    "nombre", "titular", "entra", "sale"}]}], "parte", "minutos_parte"}` con los minutos de entrada y salida (null: todo
    el partido), de qué parte es el vídeo («partido», «primera» o «segunda») y cuánto dura cada parte. Solo los que
    jugaron (titulares o que entraron) y, con una sola parte, los que estuvieron en ella."""
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
            jugadores.append(JugadorActa(dorsal, j.get("nombre") or "", desde, None if sale is None else segundo(sale)))
        if jugadores:
            plantillas.append(Plantilla(equipo.get("lado") or "", equipo.get("nombre") or "", jugadores))
    return plantillas
