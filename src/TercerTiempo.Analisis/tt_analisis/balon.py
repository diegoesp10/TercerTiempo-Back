"""La trayectoria del balón entre fotogramas. El detector no ve el balón en todos los fotogramas (de noche, lejos, tapado
por un jugador), pero entre dos avistamientos cercanos el balón solo ha podido ir de uno a otro: los huecos cortos se
rellenan interpolando, con un límite de tiempo y de velocidad para no inventar. Y un avistamiento suelto, flojo y
lejos de todos los jugadores, sin nada parecido en un segundo y medio alrededor, es casi siempre una marca o un
reflejo: se quita. Todo es puro (listas de instantes) y se prueba sin GPU."""

from __future__ import annotations

from dataclasses import dataclass

from .config import ALTURA_JUGADOR_CM
from .geometria import distancia
from .tipos import Instante, Punto

# Más de esto sin ver el balón ya no se sabe por dónde fue (un despeje, un cambio de banda).
MAX_HUECO_S = 2.0
# Velocidad por encima de la cual dos avistamientos no son el mismo balón: un tiro fuerte va a 30 m/s.
VELOCIDAD_MAX_CAMPO_CM_S = 3500.0
# Sin campo calibrado se mide en la imagen; con la cámara moviéndose se deja margen.
VELOCIDAD_MAX_IMAGEN_PX_S = 3000.0
# Un avistamiento aislado se quita si es flojo, está lejos de todos y no hay otro en esta ventana.
AISLADO_VENTANA_S = 1.5
AISLADO_CONF_MAX = 0.5
AISLADO_LEJOS_M = 8.0


@dataclass
class Relleno:
    interpolados: int = 0
    huecos: int = 0
    huecos_largos: int = 0
    huecos_implausibles: int = 0
    aislados_descartados: int = 0

    def a_dict(self) -> dict:
        return {"interpolados": self.interpolados, "huecos_rellenados": self.huecos, "huecos_largos": self.huecos_largos, "huecos_implausibles": self.huecos_implausibles, "aislados_descartados": self.aislados_descartados}


def distancia_a_jugadores_m(inst: Instante) -> float | None:
    """Metros del balón al jugador más cercano: en el campo si está calibrado, si no en la imagen tomando la altura de
    cada jugador como 1,80 m. None sin balón o sin jugadores."""
    if inst.balon_imagen is None:
        return None
    mejor = None
    for persona in inst.personas:
        if persona.es_arbitro:
            continue
        if inst.balon_campo is not None and persona.xy_campo is not None:
            d = distancia(persona.xy_campo, inst.balon_campo) / 100
        else:
            alto_px = max(8.0, persona.caja[3] - persona.caja[1])
            d = distancia(persona.xy_imagen, inst.balon_imagen) / alto_px * ALTURA_JUGADOR_CM / 100
        if mejor is None or d < mejor:
            mejor = d
    return mejor


def quitar_aislados(instantes: list[Instante], ventana_s: float = AISLADO_VENTANA_S, conf_max: float = AISLADO_CONF_MAX, lejos_m: float = AISLADO_LEJOS_M) -> int:
    """Quita los avistamientos sueltos (ninguno otro a menos de `ventana_s`), flojos y lejos de todos los jugadores."""
    vistos = [i for i, inst in enumerate(instantes) if inst.balon_imagen is not None]
    quitados = 0
    for k, i in enumerate(vistos):
        inst = instantes[i]
        anterior = instantes[vistos[k - 1]].t if k > 0 else None
        siguiente = instantes[vistos[k + 1]].t if k + 1 < len(vistos) else None
        acompañado = (anterior is not None and inst.t - anterior <= ventana_s) or (siguiente is not None and siguiente - inst.t <= ventana_s)
        if acompañado or (inst.balon_confianza is not None and inst.balon_confianza >= conf_max):
            continue
        lejos = distancia_a_jugadores_m(inst)
        if lejos is not None and lejos < lejos_m:
            continue
        inst.balon_imagen, inst.balon_campo, inst.balon_origen, inst.balon_confianza = None, None, None, None
        quitados += 1
    return quitados


def _entre(a: Punto, b: Punto, f: float) -> Punto:
    return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)


def rellenar_huecos(instantes: list[Instante], max_hueco_s: float = MAX_HUECO_S, velocidad_max_campo: float = VELOCIDAD_MAX_CAMPO_CM_S, velocidad_max_imagen: float = VELOCIDAD_MAX_IMAGEN_PX_S) -> Relleno:
    """Interpola el balón en los instantes sin él que quedan entre dos avistamientos separados como mucho `max_hueco_s`
    y con una velocidad plausible. En el campo (cm) si los dos extremos están proyectados, y siempre en la imagen."""
    relleno = Relleno()
    vistos = [i for i, inst in enumerate(instantes) if inst.balon_imagen is not None]
    for a, b in zip(vistos, vistos[1:]):
        if b - a <= 1:
            continue
        ia, ib = instantes[a], instantes[b]
        lapso = ib.t - ia.t
        if lapso <= 0:
            continue
        if lapso > max_hueco_s:
            relleno.huecos_largos += 1
            continue
        en_campo = ia.balon_campo is not None and ib.balon_campo is not None
        if en_campo:
            plausible = distancia(ia.balon_campo, ib.balon_campo) / lapso <= velocidad_max_campo
        else:
            plausible = distancia(ia.balon_imagen, ib.balon_imagen) / lapso <= velocidad_max_imagen
        if not plausible:
            relleno.huecos_implausibles += 1
            continue
        relleno.huecos += 1
        for k in range(a + 1, b):
            inst = instantes[k]
            f = (inst.t - ia.t) / lapso
            inst.balon_imagen = _entre(ia.balon_imagen, ib.balon_imagen, f)
            inst.balon_campo = _entre(ia.balon_campo, ib.balon_campo, f) if en_campo else None
            inst.balon_origen = "interpolado"
            inst.balon_confianza = None
            relleno.interpolados += 1
    return relleno


def cobertura(instantes: list[Instante]) -> dict:
    """Cuántos instantes tienen balón visto, interpolado o ninguno."""
    vistos = sum(1 for i in instantes if i.balon_origen == "visto")
    interpolados = sum(1 for i in instantes if i.balon_origen == "interpolado")
    total = len(instantes)
    return {
        "fotogramas": total,
        "vistos": vistos,
        "interpolados": interpolados,
        "pct_vistos": round(100 * vistos / total, 1) if total else 0.0,
        "pct_con_balon": round(100 * (vistos + interpolados) / total, 1) if total else 0.0,
    }
