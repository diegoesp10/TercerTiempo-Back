"""Quién tiene el balón en cada momento y qué sale de ahí, con dos lecturas: la posesión de cada equipo, con el
jugador más cercano al balón (`MaquinaPosesion`, el método estándar), y las acciones de cada jugador (pases, pérdidas,
recuperaciones y saques del portero), solo con los toques que se ven (`MaquinaControl`). Sin GPU, se prueba con datos
sintéticos."""

from __future__ import annotations

import math
from bisect import bisect_right
from collections import defaultdict, deque
from collections.abc import Collection, Iterable
from dataclasses import dataclass, field

from .config import ALTURA_JUGADOR_CM, DISTANCIA_LEJOS_CM, DISTANCIA_POSESION_CM
from .geometria import Campo, distancia
from .tipos import Instante, Persona, Punto, Toque

# Las acciones de cada jugador (pases, pérdidas, recuperaciones y saques del portero) salen de toques controlados, no
# del jugador más cercano al balón (`MaquinaControl`). Medido el 25-11-2026 en el Vallobín - Real Titánico (Ojeador
# 2.1.2): con el más cercano a 2,5 m en dos muestras salían pases, pérdidas y recuperaciones de balones que pasaban
# cerca de alguien, de forcejeos, de balones parados y de cosas quietas en el césped, que en el vídeo no se veían.
DISTANCIA_CONTROL_CM = 200.0
CONFIRMACION_CONTROL = 2
CONFIRMACION_CONTROL_S = 0.2
VISTOS_CONTROL = 1
DERIVA_CONTROL_CM = 150.0
# Para quitarle el balón a quien lo tiene, el otro tiene que estar claramente más cerca: en un forcejeo los dos están
# encima y el balón no cambia de dueño cada vez que uno asoma la pierna.
HISTERESIS_CONTROL_CM = 50.0
# Más rápido que esto, dos avistamientos seguidos no son el mismo balón (un tiro fuerte va a unos 30 m/s).
VELOCIDAD_SALTO_BALON_CM_S = 3000.0
# Balón parado: en el segundo y medio antes de que alguien se haga con él no se movió más de un metro (un balón muerto
# antes de una falta o un saque, o algo quieto en el césped). Quien lo toca no se lo ha quitado a nadie.
QUIETO_ANTES_S = 1.5
QUIETO_MIN_S = 1.0
QUIETO_CM = 100.0
DISTANCIA_PASE_CM = 400.0
VELOCIDAD_PASE_CM_S = 400.0
VENTANA_PASE_S = 3.0
VENTANA_RECUPERACION_S = 2.5
CONTROL_RECUPERACION_S = 1.5
# Parada: el portero tiene el balón del rival al menos esto. Medido el 25-11-2026 en el mismo partido: los balones que
# el portero solo veía pasar a su lado (o tocaba sin quedárselo) duraban con él 0,4-0,6 s.
CONTROL_PORTERO_S = 1.0
# Saque del portero: el balón que suelta en su área (con margen, por lo que se desvía la calibración) está a 8 m o más
# de ahí en 3 s como mucho.
DISTANCIA_SAQUE_CM = 800.0
VENTANA_SAQUE_S = 3.0
MARGEN_AREA_CM = 300.0


@dataclass
class MaquinaPosesion:
    """Decide fotograma a fotograma quién tiene el balón. Un cambio de poseedor se confirma tras `confirmacion`
    muestras seguidas del mismo candidato que abarquen al menos `confirmacion_s` segundos (así a 10 fotogramas/s hace
    falta lo mismo que a 5); sin balón a la vista, el poseedor se mantiene `memoria_s` segundos."""

    umbral_cm: float = DISTANCIA_POSESION_CM
    lejos_cm: float = DISTANCIA_LEJOS_CM
    confirmacion: int = 2
    confirmacion_s: float = 0.2
    memoria_s: float = 2.0
    poseedor: Persona | None = None
    toque_actual: Toque | None = None
    toques: list[Toque] = field(default_factory=list)
    _candidato: int | None = None
    _candidato_veces: int = 0
    _candidato_desde: float = 0.0
    _candidato_xy: Punto | None = None
    _ultimo_balon_t: float | None = None

    @property
    def equipo(self) -> int | None:
        return self.poseedor.equipo if self.poseedor else None

    def actualizar(self, inst: Instante) -> Toque | None:
        """Devuelve el toque que acaba de terminar, si alguno. Un toque dura mientras el balón está al alcance del
        poseedor: cuando sale despedido (pase, tiro, despeje) el toque acaba, aunque la posesión del equipo siga.
        Sin campo calibrado se mide en la imagen, tomando la altura de cada jugador como 1,80 m."""
        terminado = None
        if inst.balon_imagen is None:
            if self.poseedor and self._ultimo_balon_t is not None and inst.t - self._ultimo_balon_t > self.memoria_s:
                terminado = self._cerrar()
            return terminado

        self._ultimo_balon_t = inst.t
        candidato, dist = self._mas_cercano(inst)
        if candidato is None or dist > self.umbral_cm:
            # Balón en vuelo o lejos de todos: no cambia la posesión.
            self._candidato, self._candidato_veces = None, 0
            return None

        if self.poseedor and candidato.tracklet == self.poseedor.tracklet:
            self._candidato, self._candidato_veces = None, 0
            self.toque_actual.t_fin = inst.t
            self.toque_actual.xy_fin = inst.balon_campo
            return None

        if self._candidato == candidato.tracklet:
            self._candidato_veces += 1
        else:
            self._candidato, self._candidato_veces = candidato.tracklet, 1
            self._candidato_desde, self._candidato_xy = inst.t, inst.balon_campo

        confirmado = self._candidato_veces >= self.confirmacion and inst.t - self._candidato_desde >= self.confirmacion_s - 1e-9
        if confirmado or self.poseedor is None:
            terminado = self._cerrar()
            self.poseedor = candidato
            self.toque_actual = Toque(candidato.tracklet, candidato.equipo, self._candidato_desde, inst.t, self._candidato_xy, inst.balon_campo)
            self._candidato, self._candidato_veces = None, 0
        return terminado

    def cerrar(self, t: float | None = None) -> Toque | None:
        return self._cerrar()

    def _cerrar(self) -> Toque | None:
        if not self.toque_actual:
            self.poseedor = None
            return None
        toque = self.toque_actual
        self.toques.append(toque)
        self.toque_actual = None
        self.poseedor = None
        return toque

    def _mas_cercano(self, inst: Instante) -> tuple[Persona | None, float]:
        """El jugador más cercano al balón y su distancia en cm (real si hay campo; estimada por su altura si no)."""
        mejor, mejor_dist = None, float("inf")
        for persona in inst.jugadores():
            if inst.balon_campo is not None and persona.xy_campo is not None:
                d = distancia(persona.xy_campo, inst.balon_campo)
            elif inst.balon_imagen is not None:
                alto_px = max(8.0, persona.caja[3] - persona.caja[1])
                d = distancia(persona.xy_imagen, inst.balon_imagen) / alto_px * ALTURA_JUGADOR_CM
            else:
                continue
            if d < mejor_dist:
                mejor, mejor_dist = persona, d
        return mejor, mejor_dist


@dataclass
class _Muestra:
    t: float
    # Dónde está el balón respecto al jugador (cm): si cambia de lado, pasaba de largo.
    rel: Punto
    visto: bool
    xy: Punto | None


@dataclass
class MaquinaControl:
    """Como `MaquinaPosesion`, pero para las acciones de cada jugador, que tienen que verse en el vídeo: el balón es de un
    jugador si se le ve con él, no si pasa cerca. Hace falta el balón a menos de `umbral_cm` en `confirmacion` muestras
    seguidas que abarquen `confirmacion_s`, `vistos_minimos` de ellas con el balón visto por el detector (no
    interpolado), sin que el balón se mueva respecto a él más de `deriva_cm` (el que pasa de largo cruza de un lado a
    otro) y, si otro lo tenía, más cerca que él por `histeresis_cm` (en un forcejeo sigue siendo de quien lo tenía). Un
    avistamiento que salta desde el último balón visto más rápido que un tiro no es el balón (una cabeza, un guante, una
    media): se ignora, salvo que el siguiente lo confirme. Cada toque dice si empezó con el balón parado."""

    umbral_cm: float = DISTANCIA_CONTROL_CM
    confirmacion: int = CONFIRMACION_CONTROL
    confirmacion_s: float = CONFIRMACION_CONTROL_S
    vistos_minimos: int = VISTOS_CONTROL
    deriva_cm: float = DERIVA_CONTROL_CM
    histeresis_cm: float = HISTERESIS_CONTROL_CM
    velocidad_max_cm_s: float = VELOCIDAD_SALTO_BALON_CM_S
    memoria_s: float = 2.0
    poseedor: Persona | None = None
    toque_actual: Toque | None = None
    toques: list[Toque] = field(default_factory=list)
    saltos: int = 0
    _candidato: int | None = None
    _ventana: list[_Muestra] = field(default_factory=list)
    _ultimo_balon_t: float | None = None
    _ultimo_visto: tuple[float, Punto] | None = None
    _descartado: tuple[float, Punto] | None = None
    _recorrido: deque[tuple[float, Punto]] = field(default_factory=deque)

    def actualizar(self, inst: Instante) -> Toque | None:
        """Devuelve el toque que acaba de terminar, si alguno."""
        terminado = None
        if inst.balon_imagen is None:
            if self.poseedor and self._ultimo_balon_t is not None and inst.t - self._ultimo_balon_t > self.memoria_s:
                terminado = self._cerrar()
            return terminado
        visto = inst.balon_origen == "visto"
        if visto and inst.balon_campo is not None and self._es_salto(inst.t, inst.balon_campo):
            self.saltos += 1
            return None
        self._ultimo_balon_t = inst.t
        if inst.balon_campo is not None:
            self._recorrido.append((inst.t, inst.balon_campo))
            while self._recorrido[0][0] < inst.t - QUIETO_ANTES_S - 1.0:
                self._recorrido.popleft()

        candidato, dist, rel = self._mas_cercano(inst)
        if candidato is None or dist > self.umbral_cm:
            self._candidato, self._ventana = None, []
            return None

        if self.poseedor and (candidato.tracklet == self.poseedor.tracklet or self._sigue_encima(inst, dist)):
            self._candidato, self._ventana = None, []
            self.toque_actual.t_fin = inst.t
            if inst.balon_campo is not None:
                self.toque_actual.xy_fin = inst.balon_campo
            return None

        if self._candidato != candidato.tracklet:
            self._candidato, self._ventana = candidato.tracklet, []
        self._ventana.append(_Muestra(inst.t, rel, visto, inst.balon_campo))
        if not self._confirmado():
            return None
        terminado = self._cerrar()
        conocidas = [m.xy for m in self._ventana if m.xy is not None]
        t0 = self._ventana[0].t
        self.poseedor = candidato
        self.toque_actual = Toque(candidato.tracklet, candidato.equipo, t0, inst.t, conocidas[0] if conocidas else None, conocidas[-1] if conocidas else None, parado=self._quieto_antes(t0))
        self._candidato, self._ventana = None, []
        return terminado

    def _es_salto(self, t: float, xy: Punto) -> bool:
        """True si este balón visto salta desde el último visto (en los últimos 2 s) más rápido que un tiro. Dos saltos
        seguidos que cuadran entre sí son el balón de verdad (el que estaba mal era el anterior): el segundo se acepta."""
        previo = self._ultimo_visto
        if previo is not None and 0 < t - previo[0] <= 2.0 and distancia(previo[1], xy) / (t - previo[0]) > self.velocidad_max_cm_s:
            descartado = self._descartado
            if descartado is not None and 0 < t - descartado[0] <= 1.0 and distancia(descartado[1], xy) / (t - descartado[0]) <= self.velocidad_max_cm_s:
                self._ultimo_visto, self._descartado = (t, xy), None
                return False
            self._descartado = (t, xy)
            return True
        self._ultimo_visto, self._descartado = (t, xy), None
        return False

    def _confirmado(self) -> bool:
        ultimas = self._ventana[-self.confirmacion:]
        if len(ultimas) < self.confirmacion or ultimas[-1].t - ultimas[0].t < self.confirmacion_s - 1e-9:
            return False
        if sum(1 for m in ultimas if m.visto) < self.vistos_minimos:
            return False
        return distancia(ultimas[0].rel, ultimas[-1].rel) <= self.deriva_cm

    def _sigue_encima(self, inst: Instante, dist_candidato: float) -> bool:
        """El que tenía el balón sigue con él: está al alcance y el otro no está claramente más cerca."""
        actual = next((p for p in inst.jugadores() if p.tracklet == self.poseedor.tracklet), None)
        if actual is None:
            return False
        d, _ = _medir(inst, actual)
        return d is not None and d <= self.umbral_cm and d < dist_candidato + self.histeresis_cm

    def _quieto_antes(self, t0: float) -> bool:
        """El balón no se movió más de `QUIETO_CM` en los `QUIETO_ANTES_S` antes de `t0` (visto al menos `QUIETO_MIN_S`)."""
        tramo = [(t, xy) for t, xy in self._recorrido if t0 - QUIETO_ANTES_S - 1e-9 <= t <= t0 + 1e-9]
        if len(tramo) < 3 or tramo[-1][0] - tramo[0][0] < QUIETO_MIN_S - 1e-9:
            return False
        return max(distancia(xy, tramo[0][1]) for _, xy in tramo) <= QUIETO_CM

    def cerrar(self, t: float | None = None) -> Toque | None:
        return self._cerrar()

    def _cerrar(self) -> Toque | None:
        if not self.toque_actual:
            self.poseedor = None
            return None
        toque = self.toque_actual
        self.toques.append(toque)
        self.toque_actual = None
        self.poseedor = None
        return toque

    def _mas_cercano(self, inst: Instante) -> tuple[Persona | None, float, Punto]:
        mejor, mejor_dist, mejor_rel = None, float("inf"), (0.0, 0.0)
        for persona in inst.jugadores():
            d, rel = _medir(inst, persona)
            if d is not None and d < mejor_dist:
                mejor, mejor_dist, mejor_rel = persona, d, rel
        return mejor, mejor_dist, mejor_rel


def _medir(inst: Instante, persona: Persona) -> tuple[float | None, Punto]:
    """Distancia del balón al jugador y dónde está el balón respecto a él, en cm: en el campo si está calibrado; si no,
    en la imagen tomando la altura del jugador como 1,80 m."""
    if inst.balon_campo is not None and persona.xy_campo is not None:
        rel = (inst.balon_campo[0] - persona.xy_campo[0], inst.balon_campo[1] - persona.xy_campo[1])
    elif inst.balon_imagen is not None:
        escala = ALTURA_JUGADOR_CM / max(8.0, persona.caja[3] - persona.caja[1])
        rel = ((inst.balon_imagen[0] - persona.xy_imagen[0]) * escala, (inst.balon_imagen[1] - persona.xy_imagen[1]) * escala)
    else:
        return None, (0.0, 0.0)
    return math.hypot(rel[0], rel[1]), rel


def toques_controlados(instantes: list[Instante]) -> list[Toque]:
    """Los toques de `MaquinaControl`, de los que salen las acciones de cada jugador."""
    maquina = MaquinaControl()
    for inst in instantes:
        maquina.actualizar(inst)
    if instantes:
        maquina.cerrar(instantes[-1].t)
    return maquina.toques


@dataclass
class Secuencia:
    """Posesión continuada de un equipo: una cadena de toques sin que el rival toque el balón ni se vaya fuera."""

    equipo: int
    toques: list[Toque]
    cortada_por_fuera: bool = False

    @property
    def t_inicio(self) -> float:
        return self.toques[0].t_inicio

    @property
    def t_fin(self) -> float:
        return self.toques[-1].t_fin


def secuencias(toques: list[Toque], cortes: list[float]) -> list[Secuencia]:
    """Agrupa los toques por equipo; `cortes` son los instantes en los que el balón salió fuera o el juego paró."""
    resultado: list[Secuencia] = []
    cortes_ordenados = sorted(cortes)
    for toque in toques:
        actual = resultado[-1] if resultado else None
        corte_entre = actual is not None and any(actual.t_fin <= c <= toque.t_inicio for c in cortes_ordenados)
        if actual is None or actual.equipo != toque.equipo or corte_entre:
            if actual is not None and corte_entre:
                actual.cortada_por_fuera = True
            resultado.append(Secuencia(toque.equipo, [toque]))
        else:
            actual.toques.append(toque)
    return resultado


@dataclass
class AccionesJugador:
    pases: int = 0
    pases_fallados: int = 0
    perdidas: int = 0
    recuperaciones: int = 0
    toques: int = 0
    # Del portero, en vez de recuperaciones, pases y pérdidas: el balón del rival con el que se queda en su área
    # (parada) y el que pone en juego desde ella (saque de puerta, con la mano o tras blocar).
    paradas: int = 0
    saques: int = 0
    # El segundo del vídeo de cada uno de los que cuentan arriba (uno por cada pase, pase fallado, pérdida,
    # recuperación, parada y saque), para que la app lleve el vídeo a ese momento: el pase, la pérdida y el saque,
    # cuando el balón deja su pie; la recuperación y la parada, cuando le llega.
    segundos_pases: list[float] = field(default_factory=list)
    segundos_pases_fallados: list[float] = field(default_factory=list)
    segundos_perdidas: list[float] = field(default_factory=list)
    segundos_recuperaciones: list[float] = field(default_factory=list)
    segundos_paradas: list[float] = field(default_factory=list)
    segundos_saques: list[float] = field(default_factory=list)

    def a_dict(self) -> dict:
        return {"pases": self.pases, "pases_fallados": self.pases_fallados, "perdidas": self.perdidas, "recuperaciones": self.recuperaciones, "toques": self.toques, "paradas": self.paradas, "saques": self.saques}


def acciones_por_jugador(
    secs: list[Secuencia],
    porteros: Collection[int] = (),
    tiros: Iterable[tuple[int, float]] = (),
    instantes: list[Instante] | None = None,
    campo: Campo | None = None,
) -> dict[int, AccionesJugador]:
    """Pases, pérdidas y recuperaciones de los jugadores de campo y paradas y saques de los porteros, cada uno con su
    segundo del vídeo, a partir de toques controlados (`toques_controlados`). Solo cuenta lo que se ve con el campo
    calibrado: sin él no se sabe adónde fue el balón ni si salió.

    - Pase: de un jugador a un compañero con el balón golpeado (`_golpeado`); si no, era el mismo jugador partido en
      dos por el seguidor o una conducción.
    - Pérdida y recuperación: el rival se hace con el balón poco después (`_robo`), sin que saliera fuera ni se parase
      el juego entre medias, y lo conserva un rato; si no, era un balón dividido. La pérdida es además un pase fallado
      si el balón iba golpeado. Un tiro (`tiros`: tracklet y segundo) no es una pérdida, ni un pase.
    - El portero (`porteros`) no pasa, ni pierde, ni recupera: el balón del rival con el que se queda en su área es una
      parada y el que pone en juego desde ella, un saque (`_saque`, que sigue el balón en `instantes`)."""
    campo = campo or Campo()
    porteros = set(porteros)
    tiros_de: dict[int, list[float]] = defaultdict(list)
    for tracklet, t in tiros:
        tiros_de[tracklet].append(t)
    acciones: dict[int, AccionesJugador] = defaultdict(AccionesJugador)
    instantes = instantes or []
    tiempos = [inst.t for inst in instantes]

    def es_tiro(toque: Toque) -> bool:
        return any(toque.t_inicio - 0.5 <= t <= toque.t_fin + 2.0 for t in tiros_de.get(toque.tracklet, ()))

    for i, sec in enumerate(secs):
        for toque in sec.toques:
            acciones[toque.tracklet].toques += 1
        for anterior, siguiente in zip(sec.toques, sec.toques[1:]):
            if anterior.tracklet == siguiente.tracklet or anterior.tracklet in porteros or es_tiro(anterior):
                continue
            if _golpeado(anterior, siguiente, VENTANA_PASE_S):
                pasador = acciones[anterior.tracklet]
                pasador.pases += 1
                pasador.segundos_pases.append(anterior.t_fin)
        siguiente_sec = secs[i + 1] if i + 1 < len(secs) else None
        if siguiente_sec is None or sec.cortada_por_fuera or siguiente_sec.equipo == sec.equipo:
            continue
        ultimo, primero = sec.toques[-1], siguiente_sec.toques[0]
        robo = _robo(ultimo, primero, siguiente_sec)
        if primero.tracklet in porteros:
            if _parada(ultimo, primero, campo, instantes, tiempos):
                portero = acciones[primero.tracklet]
                portero.paradas += 1
                portero.segundos_paradas.append(primero.t_inicio)
        elif robo:
            recupero = acciones[primero.tracklet]
            recupero.recuperaciones += 1
            recupero.segundos_recuperaciones.append(primero.t_inicio)
        if not robo or es_tiro(ultimo) or ultimo.tracklet in porteros:
            continue
        perdio = acciones[ultimo.tracklet]
        perdio.perdidas += 1
        perdio.segundos_perdidas.append(ultimo.t_fin)
        if _golpeado(ultimo, primero, VENTANA_RECUPERACION_S):
            perdio.pases_fallados += 1
            perdio.segundos_pases_fallados.append(ultimo.t_fin)

    if instantes and porteros:
        for sec in secs:
            for toque in sec.toques:
                if toque.tracklet in porteros and _saque(toque, instantes, tiempos, campo):
                    portero = acciones[toque.tracklet]
                    portero.saques += 1
                    portero.segundos_saques.append(toque.t_fin)
    return dict(acciones)


def _golpeado(anterior: Toque, siguiente: Toque, ventana_s: float) -> bool:
    """El balón sale golpeado de `anterior` hacia `siguiente`: recorre `DISTANCIA_PASE_CM` o más, a `VELOCIDAD_PASE_CM_S`
    o más de media (y no más que un tiro), en `ventana_s` como mucho. Sin el campo calibrado en los dos extremos no se
    sabe (False)."""
    if anterior.xy_fin is None or siguiente.xy_inicio is None:
        return False
    hueco = siguiente.t_inicio - anterior.t_fin
    if hueco > ventana_s:
        return False
    recorrido = distancia(anterior.xy_fin, siguiente.xy_inicio)
    return recorrido >= DISTANCIA_PASE_CM and VELOCIDAD_PASE_CM_S <= recorrido / max(hueco, 0.2) <= VELOCIDAD_SALTO_BALON_CM_S


def _robo(ultimo: Toque, primero: Toque, despues: Secuencia) -> bool:
    """El rival se hace con el balón que tenía `ultimo`: en `VENTANA_RECUPERACION_S` como mucho, con el campo calibrado
    en los dos extremos y un recorrido posible, no con el balón parado (antes de una falta o un saque nadie se lo quita
    a nadie) y conservándolo su equipo `CONTROL_RECUPERACION_S` o más."""
    if ultimo.xy_fin is None or primero.xy_inicio is None or primero.parado:
        return False
    hueco = primero.t_inicio - ultimo.t_fin
    if hueco > VENTANA_RECUPERACION_S or distancia(ultimo.xy_fin, primero.xy_inicio) / max(hueco, 0.2) > VELOCIDAD_SALTO_BALON_CM_S:
        return False
    return despues.t_fin - despues.t_inicio >= CONTROL_RECUPERACION_S


def _parada(ultimo: Toque, primero: Toque, campo: Campo, instantes: list[Instante], tiempos: list[float]) -> bool:
    """El portero (`primero`) se queda con un balón del rival: le llega en `VENTANA_RECUPERACION_S` como mucho, no con
    el balón parado, lo tiene él `CONTROL_PORTERO_S` o más (lo bloca o lo controla: el balón que solo pasa a su lado no
    cuenta) y en su área. Dónde está lo dice el balón o, si el balón no tiene campo (va por el aire, o el portero lo
    tapa), el propio portero; sin ninguno de los dos vale igual, porque junto a la portería la cámara se acerca a menudo
    y deja de ver las líneas."""
    if primero.parado or primero.t_inicio - ultimo.t_fin > VENTANA_RECUPERACION_S or primero.t_fin - primero.t_inicio < CONTROL_PORTERO_S:
        return False
    donde = primero.xy_inicio or _sitio_del_jugador(primero, instantes, tiempos)
    return donde is None or campo.en_area(donde, -1, MARGEN_AREA_CM) or campo.en_area(donde, 1, MARGEN_AREA_CM)


def _sitio_del_jugador(toque: Toque, instantes: list[Instante], tiempos: list[float]) -> Punto | None:
    """Dónde está en el campo quien da el toque, la primera vez que se sabe mientras lo da."""
    for inst in instantes[bisect_right(tiempos, toque.t_inicio - 1e-6):]:
        if inst.t > toque.t_fin:
            return None
        for persona in inst.personas:
            if persona.tracklet == toque.tracklet and persona.xy_campo is not None:
                return persona.xy_campo
    return None


def _saque(toque: Toque, instantes: list[Instante], tiempos: list[float], campo: Campo) -> bool:
    """El portero pone el balón en juego desde su área: lo suelta dentro de ella y el balón, siguiéndolo sin saltos
    imposibles, está a `DISTANCIA_SAQUE_CM` o más de ahí en `VENTANA_SAQUE_S` como mucho."""
    if toque.xy_fin is None or not (campo.en_area(toque.xy_fin, -1, MARGEN_AREA_CM) or campo.en_area(toque.xy_fin, 1, MARGEN_AREA_CM)):
        return False
    t_previo, xy_previo = toque.t_fin, toque.xy_fin
    for inst in instantes[bisect_right(tiempos, toque.t_fin):]:
        if inst.t - toque.t_fin > VENTANA_SAQUE_S:
            return False
        xy = inst.balon_campo
        if xy is None or distancia(xy_previo, xy) / max(inst.t - t_previo, 0.2) > VELOCIDAD_SALTO_BALON_CM_S:
            continue
        if distancia(xy, toque.xy_fin) >= DISTANCIA_SAQUE_CM:
            return True
        t_previo, xy_previo = inst.t, xy
    return False


# Cobertura del balón (parte del tramo con balón visto o interpolado) y parte del tramo atribuida a algún equipo a
# partir de las cuales la posesión se da por fiable («alta»), orientativa («media») o se retiene («baja»). El 35 % es
# el mismo umbral con el que la app avisa de «calidad baja».
COBERTURA_ALTA_PCT = 60.0
ATRIBUIDO_ALTA_PCT = 50.0
COBERTURA_MEDIA_PCT = 35.0
ATRIBUIDO_MEDIA_PCT = 25.0
# Sin balón a la vista, quien lo tenía lo sigue teniendo este tiempo (un pase, un despeje corto); más, ya no se sabe.
MEMORIA_BALON_S = 2.0


def confianza_posesion(cobertura_pct: float, atribuido_pct: float) -> str:
    if cobertura_pct >= COBERTURA_ALTA_PCT and atribuido_pct >= ATRIBUIDO_ALTA_PCT:
        return "alta"
    if cobertura_pct >= COBERTURA_MEDIA_PCT and atribuido_pct >= ATRIBUIDO_MEDIA_PCT:
        return "media"
    return "baja"


@dataclass
class ResumenPosesion:
    segundos: dict[int, float] = field(default_factory=lambda: {0: 0.0, 1: 0.0})
    por_mitad: dict[int, dict[int, float]] = field(default_factory=dict)
    por_tercio: dict[int, dict[str, float]] = field(default_factory=lambda: {0: defaultdict(float), 1: defaultdict(float)})
    por_minuto: dict[int, dict[int, float]] = field(default_factory=dict)
    # Del tramo que cuenta (sin el descanso): cuánto hay, cuánto tiene balón visto o interpolado y cuánto se ha
    # repartido entre los equipos. La posesión se calcula solo sobre el tiempo con balón.
    segundos_tramo: float = 0.0
    segundos_con_balon: float = 0.0
    segundos_vistos: float = 0.0
    segundos_activos: float = 0.0
    # A qué equipo se le cuenta cada instante (None: a nadie), para el visor (`repeticion.py`).
    por_instante: list[int | None] = field(default_factory=list)

    def porcentaje(self, equipo: int) -> float | None:
        total = self.segundos[0] + self.segundos[1]
        return None if total == 0 else round(100 * self.segundos[equipo] / total, 1)

    @property
    def segundos_atribuidos(self) -> float:
        return self.segundos[0] + self.segundos[1]

    @property
    def cobertura_pct(self) -> float:
        return round(100 * self.segundos_con_balon / self.segundos_tramo, 1) if self.segundos_tramo else 0.0

    @property
    def atribuido_pct(self) -> float:
        return round(100 * self.segundos_atribuidos / self.segundos_tramo, 1) if self.segundos_tramo else 0.0

    @property
    def confianza(self) -> str:
        return confianza_posesion(self.cobertura_pct, self.atribuido_pct)

    def a_dict(self) -> dict:
        return {
            "confianza": self.confianza,
            "fiable": self.confianza != "baja",
            "cobertura": {
                "segundos_tramo": round(self.segundos_tramo, 1),
                "segundos_activos": round(self.segundos_activos, 1),
                "segundos_con_balon": round(self.segundos_con_balon, 1),
                "segundos_vistos": round(self.segundos_vistos, 1),
                "segundos_atribuidos": round(self.segundos_atribuidos, 1),
                "pct_con_balon": self.cobertura_pct,
                "pct_atribuido": self.atribuido_pct,
            },
            "segundos_con_posesion": round(self.segundos_atribuidos, 1),
            "equipos": [
                {
                    "equipo": e,
                    "porcentaje": self.porcentaje(e),
                    "segundos": round(self.segundos[e], 1),
                    "por_mitad": {m: round(v.get(e, 0.0), 1) for m, v in sorted(self.por_mitad.items())},
                    "por_tercio": {k: round(v, 1) for k, v in sorted(self.por_tercio[e].items())},
                }
                for e in (0, 1)
            ],
            "por_minuto": [{"minuto": m, "equipo_0": round(v.get(0, 0.0), 1), "equipo_1": round(v.get(1, 0.0), 1)} for m, v in sorted(self.por_minuto.items())],
        }


def resumir_posesion(
    toques: list[Toque],
    instantes: list[Instante],
    juego_activo: list[bool],
    mitad_de: list[int],
    direccion_de,
    dt: float,
    campo: Campo | None = None,
    excluidos: list[bool] | None = None,
    memoria_s: float = MEMORIA_BALON_S,
) -> ResumenPosesion:
    """Reparte entre los equipos el tiempo de juego activo **con balón a la vista (visto o interpolado)** según quién lo
    tenía o lo tuvo por última vez: el balón en vuelo sigue siendo de quien lo lanzó. Tras más de `memoria_s` sin
    balón ya no se sabe de quién es hasta el siguiente toque, y ese tiempo no se reparte. `excluidos` marca los
    instantes que no cuentan ni para la cobertura (el descanso). `direccion_de(equipo, mitad)` dice hacia dónde ataca
    cada uno."""
    campo = campo or Campo()
    resumen = ResumenPosesion()
    toques_ordenados = sorted(toques, key=lambda t: t.t_inicio)
    excluidos = excluidos or [False] * len(instantes)
    i = 0
    ultimo_equipo: int | None = None
    ultimo_toque: Toque | None = None
    ultimo_balon_t: float | None = None
    for inst, activo, mitad, excluido in zip(instantes, juego_activo, mitad_de, excluidos):
        while i < len(toques_ordenados) and toques_ordenados[i].t_inicio <= inst.t:
            ultimo_toque = toques_ordenados[i]
            ultimo_equipo = ultimo_toque.equipo
            i += 1
        if excluido:
            resumen.por_instante.append(None)
            continue
        resumen.segundos_tramo += dt
        con_balon = inst.balon_imagen is not None
        if con_balon:
            if ultimo_balon_t is not None and inst.t - ultimo_balon_t > memoria_s and (ultimo_toque is None or ultimo_toque.t_inicio < inst.t - 1e-6):
                # El balón vuelve tras un hueco largo y nadie lo ha tocado todavía: no se sabe de quién es.
                ultimo_equipo = None
            ultimo_balon_t = inst.t
            resumen.segundos_con_balon += dt
            if inst.balon_origen == "visto":
                resumen.segundos_vistos += dt
        if not activo:
            resumen.por_instante.append(None)
            continue
        resumen.segundos_activos += dt
        if not con_balon or ultimo_equipo is None:
            resumen.por_instante.append(None)
            continue
        resumen.por_instante.append(ultimo_equipo)
        resumen.segundos[ultimo_equipo] += dt
        resumen.por_mitad.setdefault(mitad, defaultdict(float))[ultimo_equipo] += dt
        minuto = int(inst.t // 60)
        resumen.por_minuto.setdefault(minuto, defaultdict(float))[ultimo_equipo] += dt
        if inst.balon_campo is not None:
            direccion = direccion_de(ultimo_equipo, mitad)
            if direccion:
                resumen.por_tercio[ultimo_equipo][campo.tercio_relativo(inst.balon_campo[0], direccion)] += dt
    return resumen
