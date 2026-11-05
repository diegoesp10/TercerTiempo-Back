"""Eventos del partido deducidos de la trayectoria del balón y de la posesión: balón fuera (saque de banda, córner,
saque de puerta), tiros (a puerta o no), goles (con el saque de centro que los confirma), paradas, parones y saques de
centro. Todo son candidatos con una confianza; el cuerpo técnico (o el modelo de lenguaje) confirma."""

from __future__ import annotations

from dataclasses import dataclass, field

from .config import DISTANCIA_TIRO_CM, VELOCIDAD_TIRO_CM_S
from .geometria import Campo, corte_con_linea_de_fondo, distancia, hacia, modulo, velocidad
from .tipos import Evento, Instante, Toque

VELOCIDAD_SALTO_CM_S = 6000.0
# Más allá de esto fuera de las líneas, el «balón» es ruido (gradas, banquillo), no un balón fuera.
MARGEN_RUIDO_CM = 400.0
PARON_S = 20.0
GOL_CONFIRMACION_S = 90.0
TIRO_DEDUP_S = 4.0


@dataclass
class Parametros:
    paron_s: float = PARON_S
    velocidad_tiro: float = VELOCIDAD_TIRO_CM_S
    distancia_tiro: float = DISTANCIA_TIRO_CM
    gol_confirmacion_s: float = GOL_CONFIRMACION_S


@dataclass
class Resultado:
    eventos: list[Evento] = field(default_factory=list)
    # Por instante: True si el juego estaba activo (balón dentro y sin parón largo).
    juego_activo: list[bool] = field(default_factory=list)
    cortes: list[float] = field(default_factory=list)


def _ultimo_toque_antes(toques: list[Toque], t: float, margen_s: float = 3.0) -> Toque | None:
    mejor = None
    for toque in toques:
        if toque.t_inicio <= t + 0.01 and (mejor is None or toque.t_inicio > mejor.t_inicio):
            mejor = toque
    if mejor is None or t - mejor.t_fin > margen_s:
        return None
    return mejor


def _primer_toque_despues(toques: list[Toque], t: float, ventana_s: float) -> Toque | None:
    candidatos = [x for x in toques if t <= x.t_inicio <= t + ventana_s]
    return min(candidatos, key=lambda x: x.t_inicio) if candidatos else None


def detectar_eventos(
    instantes: list[Instante],
    toques: list[Toque],
    mitad_de: list[int],
    direccion_de,
    porteros_de,
    campo: Campo | None = None,
    parametros: Parametros | None = None,
) -> Resultado:
    """`direccion_de(equipo, mitad)` → +1/-1/None; `porteros_de(equipo)` → conjunto de tracklets que son porteros."""
    campo = campo or Campo()
    p = parametros or Parametros()
    resultado = Resultado(juego_activo=[True] * len(instantes))
    toques = sorted(toques, key=lambda x: x.t_inicio)

    ultimo_pos = None
    ultimo_t = None
    ultimo_dentro_t = None
    fuera_desde = None
    ultimo_fuera_t = -1e9
    sin_balon_desde = None
    quieto_desde = None
    ultimo_tiro_t = -1e9
    gol_pendiente: Evento | None = None
    en_juego = True

    for i, inst in enumerate(instantes):
        mitad = mitad_de[i]
        # El parón se mide con el balón a la vista en la imagen; la geometría (fuera, tiros, goles) con el campo calibrado.
        if inst.balon_imagen is None:
            if sin_balon_desde is None:
                sin_balon_desde = inst.t
            if inst.t - sin_balon_desde >= p.paron_s:
                resultado.juego_activo[i] = False
            continue

        if sin_balon_desde is not None and inst.t - sin_balon_desde >= p.paron_s:
            resultado.eventos.append(Evento("paron", sin_balon_desde, t_fin=inst.t, confianza=0.6, detalle="Sin balón a la vista"))
            resultado.cortes.append(sin_balon_desde)
        sin_balon_desde = None

        xy = inst.balon_campo
        if xy is None:
            continue

        v = (0.0, 0.0)
        if ultimo_pos is not None and ultimo_t is not None and 0 < inst.t - ultimo_t <= 0.6:
            v = velocidad(ultimo_pos, xy, inst.t - ultimo_t)
            if modulo(v) > VELOCIDAD_SALTO_CM_S:
                # Un salto imposible: detección falsa, se ignora el movimiento.
                v = (0.0, 0.0)

        # Balón parado mucho tiempo dentro del campo: parón (lesión, cambio, discusión).
        if ultimo_pos is not None and distancia(ultimo_pos, xy) < 30:
            quieto_desde = quieto_desde if quieto_desde is not None else ultimo_t
            if quieto_desde is not None and inst.t - quieto_desde >= p.paron_s:
                resultado.juego_activo[i] = False
        else:
            if quieto_desde is not None and inst.t - quieto_desde >= p.paron_s:
                resultado.eventos.append(Evento("paron", quieto_desde, t_fin=inst.t, confianza=0.5, detalle="Balón parado"))
                resultado.cortes.append(quieto_desde)
            quieto_desde = None

        lado_fuera = campo.fuera_por(xy)
        # Solo cuenta como fuera si el balón estaba dentro hace poco (si no, es ruido de la calibración) y no se acaba
        # de marcar otro fuera.
        acaba_de_salir = ultimo_dentro_t is not None and inst.t - ultimo_dentro_t <= 1.0 and inst.t - ultimo_fuera_t > 3.0
        if lado_fuera and not campo.dentro(xy, margen=MARGEN_RUIDO_CM):
            lado_fuera = None
        if lado_fuera:
            resultado.juego_activo[i] = False
            if fuera_desde is None and acaba_de_salir:
                fuera_desde = inst.t
                ultimo_fuera_t = inst.t
                en_juego = False
                ultimo = _ultimo_toque_antes(toques, inst.t, margen_s=4.0)
                equipo_toco = ultimo.equipo if ultimo else None
                tipo, equipo_saca, detalle = _clasificar_fuera(lado_fuera, xy, equipo_toco, mitad, direccion_de, campo)
                if tipo == "gol":
                    gol_pendiente = Evento("gol", inst.t, equipo=equipo_saca, tracklet=ultimo.tracklet if ultimo else None, confianza=0.45, detalle=detalle)
                    resultado.eventos.append(gol_pendiente)
                    if ultimo and inst.t - ultimo_tiro_t > 1.0:
                        resultado.eventos.append(Evento("tiro", ultimo.t_fin, equipo=ultimo.equipo, tracklet=ultimo.tracklet, confianza=0.6, detalle="Balón que entra en la portería (a puerta)"))
                else:
                    resultado.eventos.append(Evento(tipo, inst.t, equipo=equipo_saca, confianza=0.6 if equipo_saca is not None else 0.4, detalle=detalle))
                resultado.cortes.append(inst.t)
        else:
            ultimo_dentro_t = inst.t
            if fuera_desde is not None:
                fuera_desde = None
            if not en_juego and modulo(v) > 150:
                en_juego = True

        # Saque de centro: balón en el círculo central casi parado tras un corte reciente.
        if distancia(xy, campo.centro) < 250 and modulo(v) < 150:
            corte_reciente = any(0 < inst.t - c <= p.gol_confirmacion_s for c in resultado.cortes)
            ultimo_saque = next((e for e in reversed(resultado.eventos) if e.tipo == "saque_centro"), None)
            if corte_reciente and (ultimo_saque is None or inst.t - ultimo_saque.t > 30):
                resultado.eventos.append(Evento("saque_centro", inst.t, confianza=0.6))
                if gol_pendiente and inst.t - gol_pendiente.t <= p.gol_confirmacion_s:
                    gol_pendiente.confianza = 0.85
                    gol_pendiente.detalle += " · saque de centro después"
                    gol_pendiente = None

        # Tiro: balón rápido hacia la portería que ataca el equipo que lo tocó.
        if modulo(v) >= p.velocidad_tiro and inst.t - ultimo_tiro_t > TIRO_DEDUP_S:
            ultimo = _ultimo_toque_antes(toques, inst.t, margen_s=2.0)
            if ultimo is not None:
                direccion = direccion_de(ultimo.equipo, mitad)
                if direccion:
                    porteria = campo.porteria(direccion)
                    if distancia(xy, porteria) <= p.distancia_tiro and hacia(xy, v, porteria):
                        y_corte = corte_con_linea_de_fondo(xy, v, porteria[0])
                        a_puerta = y_corte is not None and campo.en_boca_porteria(y_corte, margen=80)
                        cerca = y_corte is not None and abs(y_corte - campo.ancho / 2) <= 1200
                        if a_puerta or cerca:
                            ultimo_tiro_t = inst.t
                            detalle = "A puerta" if a_puerta else "Fuera"
                            resultado.eventos.append(Evento("tiro", inst.t, equipo=ultimo.equipo, tracklet=ultimo.tracklet, confianza=0.55 if a_puerta else 0.45, detalle=detalle))
                            if a_puerta:
                                siguiente = _primer_toque_despues(toques, inst.t, 3.0)
                                if siguiente and siguiente.equipo != ultimo.equipo and siguiente.tracklet in porteros_de(siguiente.equipo):
                                    resultado.eventos.append(Evento("parada", siguiente.t_inicio, equipo=siguiente.equipo, tracklet=siguiente.tracklet, confianza=0.5, detalle="El portero se hace con el balón tras un tiro a puerta"))

        ultimo_pos, ultimo_t = xy, inst.t

    resultado.eventos.sort(key=lambda e: e.t)
    return resultado


def _clasificar_fuera(lado: str, xy, equipo_toco: int | None, mitad: int, direccion_de, campo: Campo) -> tuple[str, int | None, str]:
    rival = None if equipo_toco is None else 1 - equipo_toco
    if lado == "banda":
        return "saque_banda", rival, "Balón fuera por la banda"
    lado_porteria = -1 if lado == "fondo_izq" else 1
    if campo.en_boca_porteria(xy[1]):
        # El balón ha cruzado la línea entre los postes: gol a favor del que ataca esa portería.
        for equipo in (0, 1):
            if direccion_de(equipo, mitad) == lado_porteria:
                return "gol", equipo, "El balón cruza la línea de gol entre los postes"
        return "gol", equipo_toco, "El balón cruza la línea de gol entre los postes"
    if equipo_toco is None:
        return "fuera_fondo", None, "Balón fuera por la línea de fondo"
    ataca_esa = direccion_de(equipo_toco, mitad) == lado_porteria
    if ataca_esa:
        return "saque_puerta", rival, "Balón fuera por la línea de fondo tras tocarlo el atacante"
    return "corner", rival, "Balón fuera por la línea de fondo tras tocarlo el defensor"


def detectar_descanso(instantes: list[Instante], eventos: list[Evento], duracion_s: float, descanso_dado: float | None) -> float | None:
    """El descanso es el parón más largo entre el 30 % y el 70 % del tramo analizado; si se dio a mano, ese."""
    if descanso_dado is not None:
        return descanso_dado
    if not instantes:
        return None
    t0, t1 = instantes[0].t, instantes[-1].t
    if t1 - t0 < 20 * 60:
        return None
    candidatos = [e for e in eventos if e.tipo == "paron" and e.t_fin and t0 + 0.3 * (t1 - t0) <= (e.t + e.t_fin) / 2 <= t0 + 0.7 * (t1 - t0)]
    if not candidatos:
        return None
    mejor = max(candidatos, key=lambda e: (e.t_fin or e.t) - e.t)
    return mejor.t_fin if (mejor.t_fin or 0) - mejor.t >= 60 else None
