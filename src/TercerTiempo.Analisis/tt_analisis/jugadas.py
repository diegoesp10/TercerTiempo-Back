"""Todo lo que sale de las posiciones una vez seguido el vídeo, en orden y sin GPU: quién tiene el balón (toques), las
dos partes y hacia dónde ataca cada equipo, los eventos (fueras, tiros, goles, parones), las paradas del juego (faltas,
penaltis y fueras de juego), los goles que deja el acta oficial y, al final, la posesión, las secuencias y las acciones
de cada jugador. Lo usa `analizador` tras la pasada y sirve igual para volver a calcularlo desde `posiciones.csv.gz`."""

from __future__ import annotations

from dataclasses import dataclass

from .balon import permanencias
from .estadisticas import ResumenTracklet, resumir_tracklets
from .eventos import Resultado, detectar_eventos
from .geometria import Campo
from .identidades import minuto_a_segundos
from .interrupciones import Interrupciones, detectar_interrupciones
from .mitades import detectar_descanso, direcciones_de_ataque, mitades
from .oficial import ActaOficial, ajustar_goles
from .posesion import AccionesJugador, MaquinaPosesion, ResumenPosesion, Secuencia, acciones_por_jugador, resumir_posesion, secuencias, toques_controlados
from .tipos import Evento, Instante, Toque


@dataclass
class Jugadas:
    toques: list[Toque]
    resumenes: dict[int, ResumenTracklet]
    descanso: float | None
    mitad_de: list[int]
    direcciones: dict[tuple[int, int], int]
    eventos: list[Evento]
    juego_activo: list[bool]
    cortes: list[float]
    interrupciones: Interrupciones
    posesion: ResumenPosesion
    secuencias: list[Secuencia]
    acciones: dict[int, AccionesJugador]
    goles_quitados: int = 0

    def direccion_de(self, equipo: int, mitad: int) -> int | None:
        return self.direcciones.get((equipo, mitad))


def toques_de(instantes: list[Instante]) -> list[Toque]:
    maquina = MaquinaPosesion()
    for inst in instantes:
        maquina.actualizar(inst)
    if instantes:
        maquina.cerrar(instantes[-1].t)
    return maquina.toques


def _porteros(resumenes: dict[int, ResumenTracklet]):
    por_equipo = {e: {tid for tid, r in resumenes.items() if r.portero and r.equipo == e} for e in (0, 1)}
    return lambda equipo: por_equipo.get(equipo, set())


def buscar_descanso(instantes: list[Instante], dt: float, duracion_s: float, descanso_dado: float | None = None, una_parte: bool = False) -> float | None:
    """El descanso antes de juntar los tracklets por dorsal (los minutos del acta lo necesitan para pasar a segundos y
    los porteros, para saber qué portería defiende cada equipo en cada parte). En un vídeo de una sola parte no lo hay:
    un parón largo no es el descanso."""
    if descanso_dado is not None:
        return descanso_dado
    if una_parte:
        return None
    resumenes = resumir_tracklets(instantes, dt)
    primera = detectar_eventos(instantes, toques_de(instantes), [1] * len(instantes), lambda e, m: None, _porteros(resumenes))
    return detectar_descanso(instantes, primera.eventos, duracion_s, None)


def analizar_jugadas(
    instantes: list[Instante],
    dt: float,
    duracion_s: float,
    descanso_dado: float | None = None,
    acta: ActaOficial | None = None,
    lado_de_equipo: dict[int, str | None] | None = None,
    campo: Campo | None = None,
    saque_s: float = 0.0,
    una_parte: bool = False,
) -> Jugadas:
    """`saque_s`: segundo del vídeo en que empieza el partido, para llevar los minutos del acta al vídeo. `una_parte`:
    el vídeo es de una sola parte (sin descanso que buscar)."""
    campo = campo or Campo()
    toques = toques_de(instantes)
    resumenes = resumir_tracklets(instantes, dt, campo)
    porteros_de = _porteros(resumenes)

    # El descanso se busca con una primera pasada sin direcciones (le basta con los parones y las posiciones).
    primera = detectar_eventos(instantes, toques, [1] * len(instantes), lambda e, m: None, porteros_de, campo)
    descanso = None if una_parte and descanso_dado is None else detectar_descanso(instantes, primera.eventos, duracion_s, descanso_dado)
    mitad_de = mitades(instantes, descanso)
    direcciones = direcciones_de_ataque(instantes, mitad_de, resumenes, campo)
    direccion_de = lambda equipo, mitad: direcciones.get((equipo, mitad))  # noqa: E731
    resultado: Resultado = detectar_eventos(instantes, toques, mitad_de, direccion_de, porteros_de, campo)

    interrupciones = detectar_interrupciones(instantes, toques, permanencias(instantes), resultado.cortes, mitad_de, direccion_de, campo)
    eventos = sorted(resultado.eventos + interrupciones.eventos, key=lambda e: e.t)
    cortes = sorted(resultado.cortes + interrupciones.cortes)
    juego_activo = list(resultado.juego_activo)
    if interrupciones.paradas:
        for i, inst in enumerate(instantes):
            if any(t0 <= inst.t <= t1 for t0, t1 in interrupciones.paradas):
                juego_activo[i] = False

    goles_quitados = 0
    if acta is not None:
        segundo = lambda minuto: minuto_a_segundos(minuto, saque_s, descanso, acta.parte, acta.minutos_parte)  # noqa: E731
        goles_quitados = ajustar_goles(eventos, acta, lado_de_equipo or {}, segundo)

    # El descanso no cuenta ni como tramo sin balón: se saca de la cobertura y de la posesión.
    paron = next((e for e in eventos if e.tipo == "paron" and descanso is not None and e.t_fin == descanso), None)
    en_descanso = [paron is not None and paron.t <= inst.t <= (paron.t_fin or inst.t) for inst in instantes]
    posesion = resumir_posesion(toques, instantes, juego_activo, mitad_de, direccion_de, dt, campo, excluidos=en_descanso)
    secs = secuencias(toques, cortes)
    # Las acciones de cada jugador, con los toques que se ven (más exigentes que los de la posesión), sin los tiros y con
    # los porteros aparte. Sus paradas van también a los eventos, para los clips y la revisión.
    tiros = [(e.tracklet, e.t) for e in eventos if e.tipo == "tiro" and e.tracklet is not None]
    acciones = acciones_por_jugador(secuencias(toques_controlados(instantes), cortes), porteros_de(0) | porteros_de(1), tiros, instantes, campo)
    paradas = [
        Evento("parada", t, equipo=resumenes[tid].equipo if tid in resumenes else None, tracklet=tid, confianza=0.55, detalle="El portero se queda en su área con un balón del rival")
        for tid, a in acciones.items()
        for t in a.segundos_paradas
    ]
    eventos = sorted(eventos + paradas, key=lambda e: e.t)
    return Jugadas(
        toques=toques,
        resumenes=resumenes,
        descanso=descanso,
        mitad_de=mitad_de,
        direcciones=direcciones,
        eventos=eventos,
        juego_activo=juego_activo,
        cortes=cortes,
        interrupciones=interrupciones,
        posesion=posesion,
        secuencias=secs,
        acciones=acciones,
        goles_quitados=goles_quitados,
    )
