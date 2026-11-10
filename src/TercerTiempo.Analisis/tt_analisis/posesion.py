"""Quién tiene el balón en cada momento y qué sale de ahí: posesión por equipo, toques, pases, pérdidas y
recuperaciones. Es el método estándar (jugador más cercano al balón con histéresis); sin GPU, se prueba con datos
sintéticos."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field

from .config import ALTURA_JUGADOR_CM, DISTANCIA_LEJOS_CM, DISTANCIA_POSESION_CM
from .geometria import Campo, distancia
from .tipos import Instante, Persona, Punto, Toque


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

    def a_dict(self) -> dict:
        return {"pases": self.pases, "pases_fallados": self.pases_fallados, "perdidas": self.perdidas, "recuperaciones": self.recuperaciones, "toques": self.toques}


def acciones_por_jugador(secs: list[Secuencia], distancia_pase_cm: float = 300.0, ventana_recuperacion_s: float = 4.0) -> dict[int, AccionesJugador]:
    """Pases (toques seguidos de compañeros con el balón desplazado), pérdidas (último toque antes de que el rival
    se haga con el balón sin que saliera fuera) y recuperaciones (primer toque del rival tras esa pérdida)."""
    acciones: dict[int, AccionesJugador] = defaultdict(AccionesJugador)
    for i, sec in enumerate(secs):
        for toque in sec.toques:
            acciones[toque.tracklet].toques += 1
        for anterior, siguiente in zip(sec.toques, sec.toques[1:]):
            if anterior.tracklet == siguiente.tracklet:
                continue
            # Sin campo calibrado no se sabe cuánto viajó el balón: dos compañeros seguidos con el balón cuentan como pase.
            if _se_desplazo(anterior, siguiente, distancia_pase_cm):
                acciones[anterior.tracklet].pases += 1
        siguiente_sec = secs[i + 1] if i + 1 < len(secs) else None
        if siguiente_sec is None or sec.cortada_por_fuera or siguiente_sec.equipo == sec.equipo:
            continue
        ultimo, primero = sec.toques[-1], siguiente_sec.toques[0]
        if primero.t_inicio - ultimo.t_fin <= ventana_recuperacion_s:
            acciones[ultimo.tracklet].perdidas += 1
            if ultimo.xy_fin and primero.xy_inicio and distancia(ultimo.xy_fin, primero.xy_inicio) >= distancia_pase_cm:
                acciones[ultimo.tracklet].pases_fallados += 1
            acciones[primero.tracklet].recuperaciones += 1
    return dict(acciones)


def _se_desplazo(anterior: Toque, siguiente: Toque, minimo_cm: float) -> bool:
    if anterior.xy_fin is None or siguiente.xy_inicio is None:
        return True
    return distancia(anterior.xy_fin, siguiente.xy_inicio) >= minimo_cm


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
            continue
        resumen.segundos_activos += dt
        if not con_balon or ultimo_equipo is None:
            continue
        resumen.segundos[ultimo_equipo] += dt
        resumen.por_mitad.setdefault(mitad, defaultdict(float))[ultimo_equipo] += dt
        minuto = int(inst.t // 60)
        resumen.por_minuto.setdefault(minuto, defaultdict(float))[ultimo_equipo] += dt
        if inst.balon_campo is not None:
            direccion = direccion_de(ultimo_equipo, mitad)
            if direccion:
                resumen.por_tercio[ultimo_equipo][campo.tercio_relativo(inst.balon_campo[0], direccion)] += dt
    return resumen
