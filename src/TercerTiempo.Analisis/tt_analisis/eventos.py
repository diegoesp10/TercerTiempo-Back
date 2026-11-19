"""Eventos del partido deducidos de la trayectoria del balón y de la posesión: balón fuera (saque de banda, córner,
saque de puerta), tiros (a puerta o no), goles (con el saque de centro que los confirma), paradas, parones y saques de
centro. Todo son candidatos con una confianza; el cuerpo técnico (o el modelo de lenguaje) confirma."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass, field

from .config import DISTANCIA_TIRO_CM, VELOCIDAD_TIRO_CM_S
from .geometria import Campo, corte_con_linea_de_fondo, distancia, hacia, modulo, velocidad
from .tipos import Evento, Instante, Punto, Toque

VELOCIDAD_SALTO_CM_S = 6000.0
# Más allá de esto fuera de las líneas, el «balón» es ruido (gradas, banquillo), no un balón fuera.
MARGEN_RUIDO_CM = 400.0
PARON_S = 20.0
GOL_CONFIRMACION_S = 90.0
TIRO_DEDUP_S = 4.0
# El balón que vuelve a salir por la misma línea sin haber vuelto al juego (un rato dentro y lejos de donde salió) es el
# mismo parón: lo recogen, lo colocan o la calibración tiembla en la línea, no otro fuera.
REANUDACION_CM = 1000.0
REANUDACION_S = 2.0
MISMO_PARON_S = 15.0


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
    detector = _Detector(sorted(toques, key=lambda x: x.t_inicio), direccion_de, porteros_de, campo or Campo(), parametros or Parametros(), Resultado(juego_activo=[True] * len(instantes)))
    for i, inst in enumerate(instantes):
        detector.paso(i, inst, mitad_de[i])
    detector.resultado.eventos.sort(key=lambda e: e.t)
    return detector.resultado


@dataclass
class _Detector:
    """Recorre los instantes uno a uno. Cada regla mira el balón de ahora y lo que recuerda del anterior: el parón (sin
    balón a la vista o con él quieto mucho rato), el fuera (y el gol, que es un fuera entre los postes), el saque de
    centro que confirma un gol y el tiro (con la parada del portero, si la hay)."""

    toques: list[Toque]
    direccion_de: Callable[[int, int], int | None]
    porteros_de: Callable[[int], set[int]]
    campo: Campo
    p: Parametros
    resultado: Resultado
    ultimo_pos: Punto | None = None
    ultimo_t: float | None = None
    ultimo_dentro_t: float | None = None
    fuera_desde: float | None = None
    ultimo_fuera_t: float = -1e9
    # Línea y punto por donde salió el último fuera contado y, desde entonces, si el balón se alejó de ahí y cuánto rato
    # estuvo dentro.
    ultimo_fuera_lado: str | None = None
    ultimo_fuera_xy: Punto | None = None
    balon_se_alejo: bool = False
    dentro_desde_fuera_s: float = 0.0
    sin_balon_desde: float | None = None
    quieto_desde: float | None = None
    ultimo_tiro_t: float = -1e9
    ultimo_tiro: Evento | None = None
    ultimo_tiro_toque: Toque | None = None
    gol_pendiente: Evento | None = None
    en_juego: bool = True

    def paso(self, i: int, inst: Instante, mitad: int) -> None:
        # El parón se mide con el balón a la vista en la imagen; la geometría (fuera, tiros, goles) con el campo calibrado.
        if not self._balon_a_la_vista(i, inst):
            return
        xy = inst.balon_campo
        if xy is None:
            return
        v = self._velocidad(inst, xy)
        self._balon_quieto(i, inst, xy)
        self._fuera(i, inst, xy, v, mitad)
        self._saque_de_centro(inst, xy, v)
        self._tiro(inst, xy, v, mitad)
        self.ultimo_pos, self.ultimo_t = xy, inst.t

    def _balon_a_la_vista(self, i: int, inst: Instante) -> bool:
        if inst.balon_imagen is None:
            if self.sin_balon_desde is None:
                self.sin_balon_desde = inst.t
            if inst.t - self.sin_balon_desde >= self.p.paron_s:
                self.resultado.juego_activo[i] = False
            return False
        if self.sin_balon_desde is not None and inst.t - self.sin_balon_desde >= self.p.paron_s:
            self.resultado.eventos.append(Evento("paron", self.sin_balon_desde, t_fin=inst.t, confianza=0.6, detalle="Sin balón a la vista"))
            self.resultado.cortes.append(self.sin_balon_desde)
        self.sin_balon_desde = None
        return True

    def _velocidad(self, inst: Instante, xy: Punto) -> tuple[float, float]:
        if self.ultimo_pos is None or self.ultimo_t is None or not 0 < inst.t - self.ultimo_t <= 0.6:
            return (0.0, 0.0)
        v = velocidad(self.ultimo_pos, xy, inst.t - self.ultimo_t)
        # Un salto imposible: detección falsa, se ignora el movimiento.
        return (0.0, 0.0) if modulo(v) > VELOCIDAD_SALTO_CM_S else v

    def _balon_quieto(self, i: int, inst: Instante, xy: Punto) -> None:
        """Balón parado mucho tiempo dentro del campo: parón (lesión, cambio, discusión)."""
        if self.ultimo_pos is not None and distancia(self.ultimo_pos, xy) < 30:
            self.quieto_desde = self.quieto_desde if self.quieto_desde is not None else self.ultimo_t
            if self.quieto_desde is not None and inst.t - self.quieto_desde >= self.p.paron_s:
                self.resultado.juego_activo[i] = False
            return
        if self.quieto_desde is not None and inst.t - self.quieto_desde >= self.p.paron_s:
            self.resultado.eventos.append(Evento("paron", self.quieto_desde, t_fin=inst.t, confianza=0.5, detalle="Balón parado"))
            self.resultado.cortes.append(self.quieto_desde)
        self.quieto_desde = None

    def _fuera(self, i: int, inst: Instante, xy: Punto, v: tuple[float, float], mitad: int) -> None:
        lado_fuera = self.campo.fuera_por(xy)
        if lado_fuera and not self.campo.dentro(xy, margen=MARGEN_RUIDO_CM):
            lado_fuera = None
        if not lado_fuera:
            self.ultimo_dentro_t = inst.t
            self.fuera_desde = None
            if self.ultimo_fuera_xy is not None and distancia(xy, self.ultimo_fuera_xy) > REANUDACION_CM:
                self.balon_se_alejo = True
            if self.ultimo_t is not None and 0 < inst.t - self.ultimo_t <= 0.6:
                self.dentro_desde_fuera_s += inst.t - self.ultimo_t
            if not self.en_juego and modulo(v) > 150:
                self.en_juego = True
            return
        self.resultado.juego_activo[i] = False
        # Solo cuenta como fuera si el balón estaba dentro hace poco (si no, es ruido de la calibración) y no se acaba
        # de marcar otro fuera.
        acaba_de_salir = self.ultimo_dentro_t is not None and inst.t - self.ultimo_dentro_t <= 1.0 and inst.t - self.ultimo_fuera_t > 3.0
        if self.fuera_desde is not None or not acaba_de_salir:
            return
        volvio_al_juego = self.balon_se_alejo and self.dentro_desde_fuera_s >= REANUDACION_S
        mismo_paron = lado_fuera == self.ultimo_fuera_lado and not volvio_al_juego and inst.t - self.ultimo_fuera_t <= MISMO_PARON_S
        self.fuera_desde = self.ultimo_fuera_t = inst.t
        self.en_juego = False
        if mismo_paron:
            return
        self.ultimo_fuera_lado, self.ultimo_fuera_xy = lado_fuera, xy
        self.balon_se_alejo, self.dentro_desde_fuera_s = False, 0.0
        ultimo = _ultimo_toque_antes(self.toques, inst.t, margen_s=4.0)
        tipo, equipo_saca, detalle = _clasificar_fuera(lado_fuera, xy, ultimo.equipo if ultimo else None, mitad, self.direccion_de, self.campo)
        if tipo == "gol":
            self.gol_pendiente = Evento("gol", inst.t, equipo=equipo_saca, tracklet=ultimo.tracklet if ultimo else None, confianza=0.45, detalle=detalle)
            self.resultado.eventos.append(self.gol_pendiente)
            self._tiro_que_entra(ultimo, inst.t)
        else:
            self.resultado.eventos.append(Evento(tipo, inst.t, equipo=equipo_saca, confianza=0.6 if equipo_saca is not None else 0.4, detalle=detalle))
        self.resultado.cortes.append(inst.t)

    def _tiro_que_entra(self, ultimo: Toque | None, t: float) -> None:
        """El balón que cruza la línea entre los postes sale de un tiro: el que ya se vio con ese mismo toque o, si no se
        llegó a ver, uno nuevo. Un toque es un solo tiro."""
        if ultimo is None:
            return
        if self.ultimo_tiro is not None and self.ultimo_tiro_toque is ultimo:
            self.ultimo_tiro.detalle = "A puerta · el balón entra en la portería"
        else:
            self.ultimo_tiro = Evento("tiro", min(ultimo.t_fin, t), equipo=ultimo.equipo, tracklet=ultimo.tracklet, confianza=0.6, detalle="Balón que entra en la portería (a puerta)")
            self.ultimo_tiro_toque = ultimo
            self.resultado.eventos.append(self.ultimo_tiro)
        self.ultimo_tiro_t = t

    def _saque_de_centro(self, inst: Instante, xy: Punto, v: tuple[float, float]) -> None:
        """Balón en el círculo central casi parado tras un corte reciente; si hay un gol pendiente, lo confirma."""
        if distancia(xy, self.campo.centro) >= 250 or modulo(v) >= 150:
            return
        corte_reciente = any(0 < inst.t - c <= self.p.gol_confirmacion_s for c in self.resultado.cortes)
        ultimo_saque = next((e for e in reversed(self.resultado.eventos) if e.tipo == "saque_centro"), None)
        if not corte_reciente or (ultimo_saque is not None and inst.t - ultimo_saque.t <= 30):
            return
        self.resultado.eventos.append(Evento("saque_centro", inst.t, confianza=0.6))
        if self.gol_pendiente and inst.t - self.gol_pendiente.t <= self.p.gol_confirmacion_s:
            self.gol_pendiente.confianza = 0.85
            self.gol_pendiente.detalle += " · saque de centro después"
            self.gol_pendiente = None

    def _tiro(self, inst: Instante, xy: Punto, v: tuple[float, float], mitad: int) -> None:
        """Balón rápido hacia la portería que ataca el equipo que lo tocó."""
        if modulo(v) < self.p.velocidad_tiro or inst.t - self.ultimo_tiro_t <= TIRO_DEDUP_S:
            return
        ultimo = _ultimo_toque_antes(self.toques, inst.t, margen_s=2.0)
        if ultimo is None or ultimo is self.ultimo_tiro_toque:
            return
        direccion = self.direccion_de(ultimo.equipo, mitad)
        if not direccion:
            return
        porteria = self.campo.porteria(direccion)
        if distancia(xy, porteria) > self.p.distancia_tiro or not hacia(xy, v, porteria):
            return
        y_corte = corte_con_linea_de_fondo(xy, v, porteria[0])
        a_puerta = y_corte is not None and self.campo.en_boca_porteria(y_corte, margen=80)
        cerca = y_corte is not None and abs(y_corte - self.campo.ancho / 2) <= 1200
        if not (a_puerta or cerca):
            return
        self.ultimo_tiro_t = inst.t
        self.ultimo_tiro = Evento("tiro", inst.t, equipo=ultimo.equipo, tracklet=ultimo.tracklet, confianza=0.55 if a_puerta else 0.45, detalle="A puerta" if a_puerta else "Fuera")
        self.ultimo_tiro_toque = ultimo
        self.resultado.eventos.append(self.ultimo_tiro)
        if a_puerta:
            siguiente = _primer_toque_despues(self.toques, inst.t, 3.0)
            if siguiente and siguiente.equipo != ultimo.equipo and siguiente.tracklet in self.porteros_de(siguiente.equipo):
                self.resultado.eventos.append(Evento("parada", siguiente.t_inicio, equipo=siguiente.equipo, tracklet=siguiente.tracklet, confianza=0.5, detalle="El portero se hace con el balón tras un tiro a puerta"))


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
