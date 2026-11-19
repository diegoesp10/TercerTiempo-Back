"""Las paradas del juego con el balón dentro del campo que no son ni un fuera ni un gol: faltas, penaltis y fueras de
juego. Cuando el árbitro pita, el balón se queda quieto donde se va a sacar (el sitio de la falta o el del que estaba en
fuera de juego) y los jugadores dejan de correr: las paradas salen de lo uno (si el detector ve el balón quieto) o de lo
otro (el frenazo de todos a la vez, aunque el balón no se vea). Quién hizo la falta se lee como en el campo: el que
tenía el balón la recibió y el rival que tenía más cerca la cometió (si no había ningún rival cerca, no fue falta). Si saca el rival del que tenía el balón y, en su último pase hacia
delante, un compañero estaba más allá del penúltimo defensor, es un fuera de juego. Todo son candidatos con una
confianza; el cuerpo técnico confirma. Puro: se prueba sin GPU."""

from __future__ import annotations

from bisect import bisect_left
from collections import defaultdict
from collections.abc import Callable
from dataclasses import dataclass, field

import numpy as np

from .balon import Permanencia
from .geometria import PUNTO_PENALTI_CM, Campo, distancia
from .tipos import Evento, Instante, Persona, Punto, Toque

# Una parada vale si el balón está a más de esto de las líneas (cerca de ellas son saques de banda, córners y saques de
# puerta, que salen de los fueras), fuera del círculo central (saques de centro) y fuera del área pequeña (saques de
# puerta); en el área grande, solo si no tiene el balón el portero (en las manos) o es el punto de penalti.
LEJOS_DE_LINEAS_CM = 300.0
RADIO_SAQUE_CENTRO_CM = 250.0
RADIO_PENALTI_CM = 200.0
# Un balón que rueda despacio (una conducción al paso) también se queda dentro del radio de una permanencia; parado de
# verdad no se mueve más de esto de principio a fin.
DERIVA_MAXIMA_CM = 100.0
# Dos permanencias seguidas en el mismo sitio son la misma parada (el balón se mueve un poco al colocarlo).
JUNTAR_S = 6.0
JUNTAR_CM = 400.0
# Para que sea una parada del juego tiene que haber juego antes: un toque que acaba como mucho esto antes.
JUEGO_PREVIO_S = 15.0
# Un fuera o un gol poco antes: la parada es su saque (o la vuelta del balón), no una falta.
TRAS_CORTE_S = 30.0
# El que saca: el primer toque desde un segundo antes de que el balón se mueva hasta tres después.
SAQUE_ANTES_S = 1.0
SAQUE_DESPUES_S = 3.0
# Una falta es un choque: algún rival tiene que estar a menos de DISPUTA_CM del que tenía el balón entre DISPUTA_ANTES_S
# antes de perderlo y DISPUTA_DESPUES_S después (en el J8 del Vallobín, las paradas sin nadie a menos de 5 m eran otra
# cosa: el balón parado sin pitido, una lesión). El más cercano, si estuvo a menos de CERCA_FALTA_CM, es quien la hizo.
DISPUTA_CM = 500.0
DISPUTA_ANTES_S = 2.0
DISPUTA_DESPUES_S = 1.0
CERCA_FALTA_CM = 300.0
# Fuera de juego: el penúltimo defensor solo se sabe con al menos estos rivales a la vista y con el último cerca de su
# portería (si no, el portero no está en la imagen); margen por el error de la calibración.
DEFENSORES_MINIMOS = 4
ULTIMO_DEFENSOR_CM = 3000.0
MARGEN_FUERA_DE_JUEGO_CM = 100.0
# El pase que deja en fuera de juego: el último toque del equipo antes de la parada, como mucho esto antes, y el balón
# avanzando hacia la portería rival al menos AVANCE_PASE_CM en el segundo y medio siguiente.
PASE_ANTES_S = 8.0
AVANCE_PASE_CM = 300.0
# Los jugadores frenan cuando pita el árbitro: velocidad mediana durante la parada frente a la de antes.
FRENAZO_RELACION = 0.6
# Parada por frenazo, sin ver el balón: la velocidad mediana de los jugadores (suavizada a 3 s) pasa de al menos
# RITMO_DE_JUEGO_CM_S en los 5 s de antes a no más de FRENAZO_FUERTE de eso en los 5 s siguientes, con un toque como mucho
# TOQUE_ANTES_FRENAZO_S antes y otro (el saque) antes de REANUDA_S; si no, es el final de una parte o un parón largo.
# En el J8 del Vallobín salieron 14 frenazos sin un fuera cerca; en los que había un rival a menos de 5 m del que tenía
# el balón se veía el choque (en el 02:14, un jugador en el suelo).
RITMO_DE_JUEGO_CM_S = 250.0
FRENAZO_FUERTE = 0.55
FRENAZO_SEPARACION_S = 15
TOQUE_ANTES_FRENAZO_S = 4.0
REANUDA_S = 60.0
# Un fuera o un gol entre estos segundos antes y después del frenazo lo explica.
FUERA_ANTES_FRENAZO_S = 8.0
FUERA_DESPUES_FRENAZO_S = 3.0

# Confianzas: la base y lo que suma cada indicio a favor.
CONFIANZA_FALTA = 0.45
CONFIANZA_FALTA_FRENAZO = 0.4
CONFIANZA_FUERA_DE_JUEGO = 0.45
CONFIANZA_PENALTI = 0.5
SUMA_SAQUE_COHERENTE = 0.1
SUMA_FRENAZO = 0.1
SUMA_AUTOR = 0.05
RESTA_SAQUE_CONTRARIO = 0.1


@dataclass
class Parada:
    t_inicio: float
    t_fin: float
    xy: Punto
    # «balon» (el balón quieto) o «frenazo» (los jugadores frenan; el sitio es donde el que tenía el balón lo perdió).
    origen: str = "balon"
    poseedor: Toque | None = None


@dataclass
class Interrupciones:
    eventos: list[Evento] = field(default_factory=list)
    cortes: list[float] = field(default_factory=list)
    # Tramos (t_inicio, t_fin) con el juego parado, para no contarlos como juego activo.
    paradas: list[tuple[float, float]] = field(default_factory=list)


def paradas_en_el_campo(permanencias: list[Permanencia], campo: Campo) -> list[Parada]:
    """Las permanencias del balón dentro del campo, lejos de las líneas y del círculo central, juntando las seguidas."""
    paradas: list[Parada] = []
    for p in permanencias:
        if p.deriva_cm > DERIVA_MAXIMA_CM:
            continue
        x, y = p.centro
        if not (LEJOS_DE_LINEAS_CM < x < campo.largo - LEJOS_DE_LINEAS_CM and LEJOS_DE_LINEAS_CM < y < campo.ancho - LEJOS_DE_LINEAS_CM):
            continue
        if distancia(p.centro, campo.centro) <= RADIO_SAQUE_CENTRO_CM or campo.en_area_pequena(p.centro):
            continue
        anterior = paradas[-1] if paradas else None
        if anterior is not None and p.t_inicio - anterior.t_fin <= JUNTAR_S and distancia(p.centro, anterior.xy) <= JUNTAR_CM:
            anterior.t_fin = p.t_fin
            continue
        paradas.append(Parada(p.t_inicio, p.t_fin, p.centro))
    return paradas


def detectar_interrupciones(
    instantes: list[Instante],
    toques: list[Toque],
    permanencias: list[Permanencia],
    cortes: list[float],
    mitad_de: list[int],
    direccion_de: Callable[[int, int], int | None],
    campo: Campo | None = None,
) -> Interrupciones:
    """`cortes` son los fueras, goles y parones ya detectados; `direccion_de(equipo, mitad)` → +1/-1/None."""
    campo = campo or Campo()
    resultado = Interrupciones()
    if not instantes:
        return resultado
    tiempos = [i.t for i in instantes]
    toques = sorted(toques, key=lambda x: x.t_inicio)
    ritmo = ritmo_por_segundo(instantes)
    candidatas = paradas_en_el_campo(permanencias, campo)
    for f in frenazos(ritmo, toques):
        if not any(abs(f.t_inicio - c.t_inicio) <= FRENAZO_SEPARACION_S for c in candidatas):
            candidatas.append(f)
    for parada in sorted(candidatas, key=lambda x: x.t_inicio):
        if parada.origen == "frenazo":
            explicada = any(parada.t_inicio - FUERA_ANTES_FRENAZO_S <= c <= parada.t_inicio + FUERA_DESPUES_FRENAZO_S for c in cortes)
        else:
            explicada = any(parada.t_inicio - TRAS_CORTE_S <= c <= parada.t_inicio for c in cortes)
        if explicada:
            continue
        antes = [x for x in toques if x.t_inicio < parada.t_inicio - 0.3 and x.t_fin >= parada.t_inicio - JUEGO_PREVIO_S]
        poseedor = parada.poseedor or (antes[-1] if antes else None)
        if poseedor is None:
            continue
        mitad = mitad_de[min(len(mitad_de) - 1, bisect_left(tiempos, parada.t_inicio))]
        # Tras un frenazo, el primer toque suele ser el de quien recoge el balón para dárselo al otro: no dice quién saca.
        saque = _quien_saca(toques, parada) if parada.origen == "balon" else None
        penalti = parada.origen == "balon" and _porteria_cercana(parada.xy, campo) is not None and distancia(parada.xy, _punto_penalti(parada.xy, campo)) <= RADIO_PENALTI_CM
        if not penalti and _porteria_cercana(parada.xy, campo) is not None and any(t is not None and _es_portero(instantes, tiempos, t) for t in (poseedor, saque)):
            # En el área con el portero: lo tiene en las manos o va a sacar de puerta.
            continue
        frenazo = parada.origen == "balon" and _frenazo(ritmo, parada)
        evento = _clasificar(instantes, tiempos, toques, poseedor, saque, parada, mitad, direccion_de, campo, frenazo, penalti)
        if evento is None:
            continue
        resultado.eventos.append(evento)
        resultado.cortes.append(min(parada.t_inicio, poseedor.t_fin))
        resultado.paradas.append((parada.t_inicio, parada.t_fin))
    return resultado


def _clasificar(instantes, tiempos, toques, poseedor: Toque, saque: Toque | None, parada: Parada, mitad: int, direccion_de, campo: Campo, frenazo: bool, penalti: bool) -> Evento | None:
    t_pitido = poseedor.t_fin
    extra = SUMA_FRENAZO if frenazo else 0.0
    porteria = _porteria_cercana(parada.xy, campo)
    if penalti and porteria is not None:
        # Penalti: lo recibe el equipo que ataca esa portería; lo comete el que la defiende.
        atacante = next((e for e in (0, 1) if direccion_de(e, mitad) == porteria), None)
        if atacante is None:
            return None
        if poseedor.equipo != atacante:
            return None
        rival, d = _rival_mas_cercano(instantes, tiempos, poseedor)
        if rival is None or d > DISPUTA_CM:
            return None
        autor = rival if d <= CERCA_FALTA_CM else None
        return Evento("falta", t_pitido, equipo=1 - atacante, tracklet=autor.tracklet if autor else None, confianza=min(0.9, CONFIANZA_PENALTI + extra),
                      detalle="Penalti", t_fin=parada.t_fin, tracklet_rival=poseedor.tracklet)

    if saque is not None and saque.equipo != poseedor.equipo:
        fuera = _fuera_de_juego(instantes, tiempos, toques, poseedor.equipo, parada, mitad, direccion_de, campo)
        if fuera is not None:
            return Evento("fuera_de_juego", fuera[1], equipo=poseedor.equipo, tracklet=fuera[0].tracklet, confianza=min(0.9, CONFIANZA_FUERA_DE_JUEGO + extra),
                          detalle="Más allá del penúltimo defensor en el último pase; saca el rival", t_fin=parada.t_fin)

    rival, d = _rival_mas_cercano(instantes, tiempos, poseedor)
    if rival is None or d > DISPUTA_CM:
        return None
    autor = rival if d <= CERCA_FALTA_CM else None
    base = CONFIANZA_FALTA if parada.origen == "balon" else CONFIANZA_FALTA_FRENAZO
    confianza = base + extra + (SUMA_AUTOR if autor is not None else 0.0)
    if saque is None:
        detalle = "Recibe el que tenía el balón"
    elif saque.equipo == poseedor.equipo:
        confianza += SUMA_SAQUE_COHERENTE
        detalle = "Recibe el que tenía el balón; saca su equipo"
    else:
        confianza -= RESTA_SAQUE_CONTRARIO
        detalle = "Recibe el que tenía el balón, pero saca el rival: revisar"
    if parada.origen == "frenazo":
        detalle = "Los jugadores frenan tras un choque. " + detalle
    return Evento("falta", t_pitido, equipo=1 - poseedor.equipo, tracklet=autor.tracklet if autor else None, confianza=round(min(0.9, confianza), 2),
                  detalle=detalle, t_fin=parada.t_fin, tracklet_rival=poseedor.tracklet)


def frenazos(ritmo: dict[int, float], toques: list[Toque]) -> list[Parada]:
    """Las paradas que se ven en los jugadores (todos frenan a la vez) aunque no se vea el balón quieto."""
    if not ritmo:
        return []
    suave = {s: float(np.median([ritmo[x] for x in range(s - 1, s + 2) if x in ritmo])) for s in ritmo}
    paradas: list[Parada] = []
    ultimo = -1e9
    for s in sorted(suave):
        if s - ultimo < FRENAZO_SEPARACION_S:
            continue
        previos = [suave[x] for x in range(s - 6, s - 1) if x in suave]
        despues = [suave[x] for x in range(s, s + 5) if x in suave]
        if len(previos) < 3 or len(despues) < 3:
            continue
        ritmo_previo = float(np.median(previos))
        if ritmo_previo < RITMO_DE_JUEGO_CM_S or max(despues) > FRENAZO_FUERTE * ritmo_previo:
            continue
        ultimo = s
        poseedor = next((x for x in reversed(toques) if x.t_inicio < s and s - x.t_fin <= TOQUE_ANTES_FRENAZO_S), None)
        reanuda = next((x for x in toques if s + 2 <= x.t_inicio <= s + REANUDA_S), None)
        if poseedor is None or reanuda is None or poseedor.xy_fin is None:
            continue
        paradas.append(Parada(float(s), reanuda.t_inicio, poseedor.xy_fin, origen="frenazo", poseedor=poseedor))
    return paradas


def _quien_saca(toques: list[Toque], parada: Parada) -> Toque | None:
    candidatos = [x for x in toques if parada.t_fin - SAQUE_ANTES_S <= x.t_inicio <= parada.t_fin + SAQUE_DESPUES_S]
    if candidatos:
        return candidatos[0]
    return next((x for x in toques if x.t_inicio <= parada.t_fin <= x.t_fin + 0.5), None)


def _instante_en(instantes: list[Instante], tiempos: list[float], t: float) -> Instante:
    k = bisect_left(tiempos, t)
    if k >= len(instantes):
        return instantes[-1]
    if k > 0 and abs(tiempos[k - 1] - t) < abs(tiempos[k] - t):
        return instantes[k - 1]
    return instantes[k]


def _persona(inst: Instante, tracklet: int) -> Persona | None:
    return next((p for p in inst.personas if p.tracklet == tracklet), None)


def _es_portero(instantes: list[Instante], tiempos: list[float], toque: Toque) -> bool:
    persona = _persona(_instante_en(instantes, tiempos, toque.t_inicio), toque.tracklet)
    return persona is not None and persona.es_portero


def _rival_mas_cercano(instantes: list[Instante], tiempos: list[float], poseedor: Toque) -> tuple[Persona | None, float]:
    """El rival que más se acercó al que tenía el balón alrededor del momento en que lo perdió, y a cuánto (cm)."""
    mejor, mejor_d = None, float("inf")
    for inst in instantes[bisect_left(tiempos, poseedor.t_fin - DISPUTA_ANTES_S):bisect_left(tiempos, poseedor.t_fin + DISPUTA_DESPUES_S)]:
        jugador = _persona(inst, poseedor.tracklet)
        if jugador is None or jugador.xy_campo is None:
            continue
        for p in inst.jugadores():
            if p.equipo == 1 - poseedor.equipo and p.xy_campo is not None:
                d = distancia(p.xy_campo, jugador.xy_campo)
                if d < mejor_d:
                    mejor, mejor_d = p, d
    return mejor, mejor_d


def en_fuera_de_juego(inst: Instante, equipo: int, direccion: int, campo: Campo) -> list[Persona]:
    """Los jugadores de `equipo` (que ataca hacia `direccion`, +1 o -1) en posición de fuera de juego en ese instante:
    en el campo rival y más allá del balón y del penúltimo defensor. Nadie si no se ven bastantes defensores."""
    defensores = [p for p in inst.jugadores() if p.equipo == 1 - equipo and p.xy_campo is not None]
    if len(defensores) < DEFENSORES_MINIMOS:
        return []
    fondo = sorted((p.xy_campo[0] * direccion for p in defensores), reverse=True)
    linea_de_gol = campo.largo if direccion > 0 else 0.0
    if linea_de_gol * direccion - fondo[0] > ULTIMO_DEFENSOR_CM:
        return []
    limite = max(fondo[1], campo.largo / 2 * direccion)
    if inst.balon_campo is not None:
        limite = max(limite, inst.balon_campo[0] * direccion)
    return [p for p in inst.jugadores() if p.equipo == equipo and p.xy_campo is not None and p.xy_campo[0] * direccion > limite + MARGEN_FUERA_DE_JUEGO_CM]


def _fuera_de_juego(instantes, tiempos, toques: list[Toque], equipo: int, parada: Parada, mitad: int, direccion_de, campo: Campo) -> tuple[Persona, float] | None:
    """El jugador en fuera de juego en el último pase hacia delante de `equipo` antes de la parada, y cuándo."""
    direccion = direccion_de(equipo, mitad)
    if not direccion:
        return None
    pases = [x for x in toques if x.equipo == equipo and parada.t_inicio - PASE_ANTES_S <= x.t_fin <= parada.t_inicio]
    for pase in reversed(pases):
        inst = _instante_en(instantes, tiempos, pase.t_fin)
        if inst.balon_campo is None:
            continue
        despues = [i for i in instantes[bisect_left(tiempos, pase.t_fin):bisect_left(tiempos, pase.t_fin + 1.5)] if i.balon_campo is not None]
        if not despues or max(i.balon_campo[0] * direccion for i in despues) - inst.balon_campo[0] * direccion < AVANCE_PASE_CM:
            continue
        candidatos = [p for p in en_fuera_de_juego(inst, equipo, direccion, campo) if p.tracklet != pase.tracklet]
        if candidatos:
            return min(candidatos, key=lambda p: distancia(p.xy_campo, parada.xy)), pase.t_fin
        return None
    return None


def _porteria_cercana(xy: Punto, campo: Campo) -> int | None:
    """-1 o +1 si `xy` está en el área grande de esa portería; None si no."""
    if campo.en_area(xy, -1):
        return -1
    if campo.en_area(xy, 1):
        return 1
    return None


def _punto_penalti(xy: Punto, campo: Campo) -> Punto:
    return (PUNTO_PENALTI_CM, campo.ancho / 2) if xy[0] < campo.largo / 2 else (campo.largo - PUNTO_PENALTI_CM, campo.ancho / 2)


def ritmo_por_segundo(instantes: list[Instante]) -> dict[int, float]:
    """Velocidad mediana (cm/s) de los jugadores en cada segundo, cada uno medido sobre el último segundo."""
    series: dict[int, list[tuple[float, Punto]]] = defaultdict(list)
    for inst in instantes:
        for p in inst.personas:
            if p.es_jugador and p.xy_campo is not None:
                series[p.tracklet].append((inst.t, p.xy_campo))
    por_segundo: dict[int, list[float]] = defaultdict(list)
    for serie in series.values():
        j = 0
        for t, xy in serie:
            while serie[j][0] < t - 1.0:
                j += 1
            t0, xy0 = serie[j]
            if t - t0 >= 0.8:
                v = distancia(xy, xy0) / (t - t0)
                if v < 1200:
                    por_segundo[int(t)].append(v)
    return {s: float(np.median(v)) for s, v in por_segundo.items() if len(v) >= 4}


def _frenazo(ritmo: dict[int, float], parada: Parada) -> bool:
    antes = [ritmo[s] for s in range(int(parada.t_inicio) - 12, int(parada.t_inicio) - 2) if s in ritmo]
    durante = [ritmo[s] for s in range(int(parada.t_inicio), int(parada.t_fin) + 1) if s in ritmo]
    if len(antes) < 3 or not durante:
        return False
    return float(np.median(durante)) <= FRENAZO_RELACION * float(np.median(antes))
