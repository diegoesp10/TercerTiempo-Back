"""Las dos partes del partido: dónde empieza la segunda y hacia dónde ataca cada equipo en cada una. El descanso es el
parón más largo del medio del tramo; si el vídeo lo cortó (no hay parón), el cambio de campo: en la segunda parte cada
equipo se coloca en el lado contrario, así que el orden de las medias de los dos equipos a lo largo del campo se da la
vuelta. Todo es puro y se prueba sin GPU."""

from __future__ import annotations

from collections import defaultdict

import numpy as np

from .estadisticas import ResumenTracklet
from .geometria import Campo
from .tipos import Evento, Instante

# Con menos tramo no hay dos partes que buscar.
TRAMO_MINIMO_S = 20 * 60
# El descanso cae entre el 30 % y el 70 % del tramo analizado y, si es un parón, dura al menos un minuto.
DESCANSO_DESDE = 0.3
DESCANSO_HASTA = 0.7
DESCANSO_MINIMO_S = 60.0
# Cambio de campo: la x media de cada equipo minuto a minuto (con al menos MUESTRAS_POR_MINUTO posiciones de cada uno);
# antes y después del corte, al menos ACUERDO_MINIMO de los minutos con el mismo orden y al menos MINUTOS_POR_LADO
# minutos en cada lado. Medido el 18-11-2026 en el J8 del Vallobín (vídeo sin descanso): los 46 minutos con un orden
# antes del minuto 46 y 49 de 51 con el contrario después.
MINUTO_S = 60.0
MUESTRAS_POR_MINUTO = 30
ACUERDO_MINIMO = 0.75
MINUTOS_POR_LADO = 10
# Posiciones de cada equipo en una parte para decidir hacia dónde ataca por su x media.
MUESTRAS_DIRECCION = 50


def detectar_descanso(instantes: list[Instante], eventos: list[Evento], duracion_s: float, descanso_dado: float | None) -> float | None:
    """Dónde empieza la segunda parte: la que se dio a mano; si no, el final del parón más largo del medio del tramo;
    si no lo hay, el cambio de campo de los equipos."""
    if descanso_dado is not None:
        return descanso_dado
    if not instantes:
        return None
    t0, t1 = instantes[0].t, instantes[-1].t
    if t1 - t0 < TRAMO_MINIMO_S:
        return None
    desde, hasta = t0 + DESCANSO_DESDE * (t1 - t0), t0 + DESCANSO_HASTA * (t1 - t0)
    parones = [e for e in eventos if e.tipo == "paron" and e.t_fin and desde <= (e.t + e.t_fin) / 2 <= hasta]
    if parones:
        mejor = max(parones, key=lambda e: (e.t_fin or e.t) - e.t)
        if (mejor.t_fin or 0) - mejor.t >= DESCANSO_MINIMO_S:
            return mejor.t_fin
    return cambio_de_campo(instantes)


def cambio_de_campo(instantes: list[Instante]) -> float | None:
    """El minuto (en segundos del vídeo) desde el que los dos equipos están cada uno en el lado contrario, o None si
    no se ve claro."""
    if not instantes:
        return None
    t0, t1 = instantes[0].t, instantes[-1].t
    if t1 - t0 < TRAMO_MINIMO_S:
        return None
    sumas: dict[int, list[float]] = defaultdict(lambda: [0.0, 0, 0.0, 0])
    for inst in instantes:
        minuto = int(inst.t // MINUTO_S)
        for p in inst.personas:
            if not p.es_jugador or p.equipo not in (0, 1) or p.xy_campo is None:
                continue
            s = sumas[minuto]
            s[2 * p.equipo] += p.xy_campo[0]
            s[2 * p.equipo + 1] += 1
    orden: list[tuple[int, int]] = []
    for minuto in sorted(sumas):
        x0, n0, x1, n1 = sumas[minuto]
        if n0 >= MUESTRAS_POR_MINUTO and n1 >= MUESTRAS_POR_MINUTO and x0 / n0 != x1 / n1:
            orden.append((minuto, 1 if x0 / n0 > x1 / n1 else -1))
    desde, hasta = t0 + DESCANSO_DESDE * (t1 - t0), t0 + DESCANSO_HASTA * (t1 - t0)
    mejor: tuple[int, float] | None = None
    for k in range(MINUTOS_POR_LADO, len(orden) - MINUTOS_POR_LADO + 1):
        corte = orden[k][0] * MINUTO_S
        if not desde <= corte <= hasta:
            continue
        antes, despues = [s for _, s in orden[:k]], [s for _, s in orden[k:]]
        signo = 1 if sum(antes) > 0 else -1
        aciertos = sum(1 for s in antes if s == signo) + sum(1 for s in despues if s == -signo)
        if sum(1 for s in antes if s == signo) < ACUERDO_MINIMO * len(antes) or sum(1 for s in despues if s == -signo) < ACUERDO_MINIMO * len(despues):
            continue
        if mejor is None or aciertos > mejor[0]:
            mejor = (aciertos, corte)
    return None if mejor is None else mejor[1]


def mitades(instantes: list[Instante], descanso_t: float | None) -> list[int]:
    return [1 if descanso_t is None or inst.t < descanso_t else 2 for inst in instantes]


def direcciones_de_ataque(instantes: list[Instante], mitad_de: list[int], resumenes: dict[int, ResumenTracklet], campo: Campo | None = None) -> dict[tuple[int, int], int]:
    """Para cada (equipo, mitad): +1 si ataca hacia x = largo, -1 si hacia x = 0. Defiende el lado de la izquierda el
    equipo con la x media más baja (cada uno tiene a su portero y a sus defensas en su campo); sin bastantes posiciones
    de los dos, deciden sus porteros o el lado en el que está el único que se ve. En el J8 del Vallobín la x media
    ordenó bien 94 de los 96 minutos, mejor que los porteros, que el detector confunde a veces con un jugador."""
    campo = campo or Campo()
    x_porteros: dict[tuple[int, int], list[float]] = defaultdict(list)
    x_equipo: dict[tuple[int, int], list[float]] = defaultdict(list)
    for inst, mitad in zip(instantes, mitad_de):
        for persona in inst.personas:
            if persona.xy_campo is None:
                continue
            r = resumenes.get(persona.tracklet)
            equipo = r.equipo if r else persona.equipo
            if equipo is None or (r and r.arbitro):
                continue
            x_equipo[(equipo, mitad)].append(persona.xy_campo[0])
            if r and r.portero:
                x_porteros[(equipo, mitad)].append(persona.xy_campo[0])

    direcciones: dict[tuple[int, int], int] = {}
    for mitad in set(mitad_de):
        por_equipo: dict[int, float] = {}
        if all(len(x_equipo.get((e, mitad), [])) >= MUESTRAS_DIRECCION for e in (0, 1)):
            por_equipo = {e: float(np.mean(x_equipo[(e, mitad)])) for e in (0, 1)}
        else:
            for equipo in (0, 1):
                xs = x_porteros.get((equipo, mitad)) or []
                if len(xs) >= 10:
                    por_equipo[equipo] = float(np.median(xs))
                elif x_equipo.get((equipo, mitad)):
                    por_equipo[equipo] = float(np.mean(x_equipo[(equipo, mitad)]))
        if len(por_equipo) == 2 and por_equipo[0] != por_equipo[1]:
            izquierda = min(por_equipo, key=por_equipo.get)
            direcciones[(izquierda, mitad)] = 1
            direcciones[(1 - izquierda, mitad)] = -1
        elif len(por_equipo) == 1:
            (equipo, x), = por_equipo.items()
            direcciones[(equipo, mitad)] = 1 if x < campo.largo / 2 else -1
            direcciones[(1 - equipo, mitad)] = -direcciones[(equipo, mitad)]
    return direcciones
