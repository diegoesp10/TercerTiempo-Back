"""Lo que dice el acta oficial y no hace falta sacar del vídeo. Los minutos de cada jugador salen por descarte: si no
hubo cambio, jugó el partido entero aunque la cámara no lo viera todo el rato; si entró o salió, desde o hasta ese
minuto. Y el resultado manda sobre los goles: el vídeo no puede dar a un equipo más goles que el acta. Con el minuto de
cada gol, se queda el candidato más cercano a ese minuto; sin él, solo los confirmados por el saque de centro. Los que
sobran (balones por encima del larguero, que en el plano cruzan la línea entre los postes, o recogidos detrás de la
portería) se quitan. Puro: se prueba sin GPU."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass

from .tipos import Evento

LADOS = ("local", "visitante")
# Un candidato confirmado por el saque de centro es el gol del acta si cae a menos de esto del minuto que dice (el vídeo
# puede empezar unos minutos antes del saque inicial y el acta redondea). El que no se confirmó solo vale muy cerca de
# ese minuto: cruzar la línea entre los postes sin saque de centro después casi nunca es un gol.
GOL_VENTANA_S = 300.0
GOL_VENTANA_SIN_CONFIRMAR_S = 120.0
GOL_CONFIRMADO = 0.85
# Hasta cuánto antes del gol puede ir el tiro del que sale.
TIRO_DEL_GOL_S = 8.0


@dataclass(frozen=True)
class JugadorOficial:
    lado: str
    dorsal: int
    nombre: str
    titular: bool
    entra: int | None
    sale: int | None
    goles: int = 0


@dataclass(frozen=True)
class ActaOficial:
    minutos_parte: int
    jugadores: tuple[JugadorOficial, ...]
    # Goles de cada lado («local», «visitante») con el acta cerrada; None si el árbitro aún no la cerró.
    goles: dict[str, int] | None = None
    # (lado que marca, minuto, minutos de descuento) de cada gol, si el acta los trae: el 45+2 es (lado, 45, 2).
    minutos_goles: tuple[tuple[str, int, int], ...] = ()
    parte: str = "partido"

    @property
    def duracion(self) -> int:
        return 2 * self.minutos_parte

    @property
    def cerrada(self) -> bool:
        """El árbitro cerró el acta (trae el resultado). La provisional ya tiene las alineaciones, pero le pueden faltar
        los cambios: con ella los minutos por descarte no valen."""
        return self.goles is not None

    def jugador(self, lado: str | None, dorsal: int | None) -> JugadorOficial | None:
        if lado is None or dorsal is None:
            return None
        return next((j for j in self.jugadores if j.lado == lado and j.dorsal == dorsal), None)

    def minutos(self, jugador: JugadorOficial) -> int:
        """Por descarte: del minuto en que entró (0 si fue titular) al que salió (el final si no salió)."""
        if not jugador.titular and jugador.entra is None:
            return 0
        desde = 0 if jugador.titular else jugador.entra
        hasta = self.duracion if jugador.sale is None else jugador.sale
        return max(0, min(self.duracion, hasta) - desde)


def leer_acta(datos: dict | None) -> ActaOficial | None:
    """El mismo JSON de plantillas que pasa la app (`identidades.leer_plantillas`), con el resultado en `resultado`
    ({"local", "visitante"}) y los goles de cada jugador en `goles`."""
    if not datos or not datos.get("equipos"):
        return None
    jugadores = []
    for equipo in datos["equipos"]:
        lado = equipo.get("lado")
        if lado not in LADOS:
            continue
        for j in equipo.get("jugadores", []):
            if not isinstance(j.get("dorsal"), int):
                continue
            jugadores.append(JugadorOficial(lado, j["dorsal"], j.get("nombre") or "", bool(j.get("titular")), _entero(j.get("entra")), _entero(j.get("sale")), int(j.get("goles") or 0)))
    resultado = datos.get("resultado")
    goles = None
    if isinstance(resultado, dict) and all(isinstance(resultado.get(lado), int) for lado in LADOS):
        goles = {lado: resultado[lado] for lado in LADOS}
    minutos = tuple(
        (g["lado"], int(g["minuto"]), int(g.get("descuento") or 0))
        for g in datos.get("goles") or []
        if g.get("lado") in LADOS and isinstance(g.get("minuto"), int)
    )
    parte = datos.get("parte") if datos.get("parte") in ("partido", "primera", "segunda") else "partido"
    return ActaOficial(int(datos.get("minutos_parte") or 45), tuple(jugadores), goles, minutos, parte)


def _entero(valor) -> int | None:
    return None if valor is None else int(valor)


def lados_completos(lado_de_equipo: dict[int, str | None]) -> dict[int, str | None]:
    """Si se sabe el lado de un equipo, el otro es el contrario."""
    lados = dict(lado_de_equipo)
    for e in (0, 1):
        otro = lados.get(1 - e)
        if lados.get(e) is None and otro in LADOS:
            lados[e] = LADOS[1 - LADOS.index(otro)]
    return lados


def ajustar_goles(eventos: list[Evento], acta: ActaOficial, lado_de_equipo: dict[int, str | None], segundo_de_minuto: Callable[[float], float] | None = None) -> int:
    """Deja a cada equipo como mucho los goles del acta y quita el resto. Con los minutos de los goles y
    `segundo_de_minuto` (minuto del acta → segundo del vídeo), cada gol del acta se queda con el candidato de su equipo
    más cercano a su minuto (a menos de GOL_VENTANA_S si lo confirmó el saque de centro, de GOL_VENTANA_SIN_CONFIRMAR_S
    si no), que pasa a confianza alta; sin minutos, los confirmados de más confianza. Del tiro de un gol quitado: si se
    vio el disparo, queda como tiro fuera (por encima del larguero); si solo se dedujo del cruce de la línea, se quita.
    Devuelve cuántos candidatos se quitaron."""
    if acta.goles is None:
        return 0
    lados = lados_completos(lado_de_equipo)
    quitados: list[Evento] = []
    for equipo in (0, 1):
        lado = lados.get(equipo)
        if lado not in LADOS:
            continue
        candidatos = sorted((e for e in eventos if e.tipo == "gol" and e.equipo == equipo), key=lambda e: (-e.confianza, e.t))
        confirmados = [e for e in candidatos if e.confianza >= GOL_CONFIRMADO]
        minutos = [(m, d) for lado_gol, m, d in acta.minutos_goles if lado_gol == lado]
        if segundo_de_minuto is not None and minutos and len(minutos) == acta.goles[lado]:
            elegidos: list[Evento] = []
            for minuto, descuento in minutos:
                # El descuento sigue en la misma parte: el 45+2 es dos minutos después del 45 de la primera.
                esperado = segundo_de_minuto(minuto) + descuento * 60
                cerca = [
                    e for e in candidatos
                    if not any(e is x for x in elegidos)
                    and abs(e.t - esperado) <= (GOL_VENTANA_S if any(e is x for x in confirmados) else GOL_VENTANA_SIN_CONFIRMAR_S)
                ]
                if cerca:
                    gol = min(cerca, key=lambda e: abs(e.t - esperado))
                    gol.confianza = max(gol.confianza, GOL_CONFIRMADO)
                    texto = f"{minuto}+{descuento}" if descuento else f"{minuto}"
                    gol.detalle = (gol.detalle + f" · en el acta, minuto {texto}").strip(" ·")
                    elegidos.append(gol)
        else:
            elegidos = confirmados[:acta.goles[lado]]
        quitados += [e for e in candidatos if not any(e is x for x in elegidos)]
    for gol in quitados:
        eventos[:] = [e for e in eventos if e is not gol]
        tiro = next((e for e in eventos if e.tipo == "tiro" and gol.tracklet is not None and e.tracklet == gol.tracklet and 0 <= gol.t - e.t <= TIRO_DEL_GOL_S and "entra" in e.detalle), None)
        if tiro is None:
            continue
        if tiro.detalle.startswith("Balón que entra"):
            eventos[:] = [e for e in eventos if e is not tiro]
        else:
            tiro.detalle = "Fuera: cruza la línea entre los postes, pero el acta no da el gol (por encima del larguero)"
    return len(quitados)
