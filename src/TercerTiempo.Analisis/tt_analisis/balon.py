"""La trayectoria del balón entre fotogramas. El detector no ve el balón en todos los fotogramas (de noche, lejos, tapado
por un jugador), pero entre dos avistamientos cercanos el balón solo ha podido ir de uno a otro: los huecos cortos se
rellenan interpolando, con un límite de tiempo y de velocidad para no inventar. Desde la 2.3.0 se interpola en la imagen
y se lleva al campo con la calibración de cada instante: quien graba sigue al balón con la cámara, así que adónde mira
dice mucho de por dónde fue (con avistamientos de verdad escondidos, en huecos de 2 a 3 s el error mediano baja de 3,3 m
en línea recta por el campo a 2,5 m siguiendo a la cámara). Y un avistamiento suelto, flojo y
lejos de todos los jugadores, sin nada parecido en un segundo y medio alrededor, es casi siempre una marca o un
reflejo: se quita. Tampoco vale lo que se queda quieto en el mismo sitio una y otra vez a lo largo del partido (una
botella, un cono, un balón de repuesto en el césped): el balón de verdad solo se para muchas veces en el mismo punto
donde lo manda el reglamento. Todo es puro (listas de instantes) y se prueba sin GPU."""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .config import ALTURA_JUGADOR_CM, MARGEN_CAMPO_CM
from .geometria import Campo, distancia
from .tipos import Instante, Punto

# Más de esto sin ver el balón ya no se sabe por dónde fue (un despeje, un cambio de banda). Con la cámara calibrada en todo
# el hueco, que sigue al balón, hasta MAX_HUECO_GUIADO_S: medido el 25-11-2026 en el J8 (minutos 20-30) escondiendo
# avistamientos, a 3 s siguiendo a la cámara se falla lo mismo que a 2 s en línea recta (mediana de 2,5 m).
MAX_HUECO_S = 2.0
MAX_HUECO_GUIADO_S = 3.0
# Velocidad por encima de la cual dos avistamientos no son el mismo balón: un tiro fuerte va a 30 m/s.
VELOCIDAD_MAX_CAMPO_CM_S = 3500.0
# Sin campo calibrado se mide en la imagen; con la cámara moviéndose se deja margen.
VELOCIDAD_MAX_IMAGEN_PX_S = 3000.0
# Un avistamiento aislado se quita si es flojo, está lejos de todos y no hay otro en esta ventana.
AISLADO_VENTANA_S = 1.5
AISLADO_CONF_MAX = 0.5
AISLADO_LEJOS_M = 8.0
# Una permanencia: el balón dentro de PERMANENCIA_RADIO_CM de su mediana durante PERMANENCIA_MIN_S o más, sin dejar de
# verse más de PERMANENCIA_HUECO_S seguidos.
PERMANENCIA_RADIO_CM = 150.0
PERMANENCIA_MIN_S = 2.0
PERMANENCIA_HUECO_S = 1.0
# Un objeto fijo: FIJO_MIN_PERMANENCIAS permanencias o más a menos de FIJO_AGRUPAR_CM unas de otras, repartidas en
# FIJO_MIN_LAPSO_S o más y fuera de los puntos de saque. Medido el 18-11-2026 en el J8 del Vallobín: un objeto blanco
# junto al círculo central dio 64 permanencias entre el minuto 30 y el 95 (216 s de «balón» quieto); fuera de él y de
# otros dos objetos, ningún punto pasó de 3. La calibración lo desplaza unos 3 m según adónde mire la cámara.
FIJO_AGRUPAR_CM = 400.0
FIJO_MIN_PERMANENCIAS = 4
FIJO_MIN_LAPSO_S = 600.0
FIJO_RADIO_MIN_CM = 250.0
FIJO_HOLGURA_CM = 100.0
# Dentro del radio de un objeto fijo, mientras está en el césped (de su primera a su última permanencia, con
# FIJO_MARGEN_S de margen), solo vale el balón que va de paso: visto también fuera del radio a menos de FIJO_PASO_S y a
# una velocidad posible. Lo que se queda dentro es el objeto aunque haya un jugador al lado: medido el 25-11-2026 en el
# Vallobín - Real Titánico, quedarse con lo que estaba cerca de alguien daba toques, pases y pérdidas que no existieron.
FIJO_MARGEN_S = 60.0
FIJO_PASO_S = 0.6
# El balón de los fotogramas vecinos (`deteccion.Detector.balon_vecinos`) vale para el instante muestreado de al lado: a
# uno o dos fotogramas (0,04-0,08 s a 25 fps) apenas se ha movido. Con uno a cada lado se toma el punto entre los dos y
# con uno solo, ese.
INTERMEDIO_VECINOS = 2


@dataclass
class Relleno:
    interpolados: int = 0
    # De los interpolados, los que siguen a la cámara (en la imagen y al campo con la calibración de su instante).
    guiados: int = 0
    huecos: int = 0
    huecos_largos: int = 0
    huecos_implausibles: int = 0
    aislados_descartados: int = 0
    fijos_descartados: int = 0
    objetos_fijos: list[ObjetoFijo] = field(default_factory=list)

    def a_dict(self) -> dict:
        return {
            "interpolados": self.interpolados,
            "guiados_por_la_camara": self.guiados,
            "huecos_rellenados": self.huecos,
            "huecos_largos": self.huecos_largos,
            "huecos_implausibles": self.huecos_implausibles,
            "aislados_descartados": self.aislados_descartados,
            "fijos_descartados": self.fijos_descartados,
            "objetos_fijos": [o.a_dict() for o in self.objetos_fijos],
        }


@dataclass(frozen=True)
class Permanencia:
    t_inicio: float
    t_fin: float
    centro: Punto
    indices: tuple[int, ...]
    # Lo que se movió el balón del primer al último punto: casi nada si está parado; más si rueda despacio.
    deriva_cm: float = 0.0

    @property
    def duracion(self) -> float:
        return self.t_fin - self.t_inicio


@dataclass(frozen=True)
class ObjetoFijo:
    centro: Punto
    radio: float
    permanencias: int
    segundos: float
    t_primera: float
    t_ultima: float

    def a_dict(self) -> dict:
        return {"x_cm": round(self.centro[0]), "y_cm": round(self.centro[1]), "radio_cm": round(self.radio), "permanencias": self.permanencias, "segundos": round(self.segundos, 1), "desde_s": round(self.t_primera, 1), "hasta_s": round(self.t_ultima, 1)}


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


def permanencias(instantes: list[Instante], radio: float = PERMANENCIA_RADIO_CM, min_s: float = PERMANENCIA_MIN_S, hueco_s: float = PERMANENCIA_HUECO_S) -> list[Permanencia]:
    """Los tramos en que el balón (proyectado en el campo) se queda quieto: todos sus avistamientos dentro de `radio`
    de la mediana de los anteriores durante `min_s` o más."""
    salida: list[Permanencia] = []
    n = len(instantes)
    i = 0
    while i < n:
        if instantes[i].balon_campo is None:
            i += 1
            continue
        puntos, indices = [instantes[i].balon_campo], [i]
        j = i + 1
        while j < n and instantes[j].t - instantes[indices[-1]].t <= hueco_s:
            b = instantes[j].balon_campo
            if b is not None:
                cx, cy = np.median(np.asarray(puntos), axis=0)
                if distancia(b, (cx, cy)) > radio:
                    break
                puntos.append(b)
                indices.append(j)
            j += 1
        t0, t1 = instantes[i].t, instantes[indices[-1]].t
        if t1 - t0 < min_s:
            i += 1
            continue
        cx, cy = np.median(np.asarray(puntos), axis=0)
        # Lo que el balón aún rodaba al llegar no es parte de la permanencia: empieza en el primer punto ya cerca del centro.
        while len(indices) > 1 and distancia(instantes[indices[0]].balon_campo, (cx, cy)) > radio / 2:
            indices.pop(0)
        t0 = instantes[indices[0]].t
        if t1 - t0 >= min_s:
            deriva = distancia(instantes[indices[0]].balon_campo, instantes[indices[-1]].balon_campo)
            salida.append(Permanencia(t0, t1, (float(cx), float(cy)), tuple(indices), deriva))
        i = indices[-1] + 1
    return salida


def objetos_fijos(perms: list[Permanencia], campo: Campo | None = None) -> list[ObjetoFijo]:
    """Los sitios fuera de los puntos de saque donde el balón se «para» una y otra vez a lo largo del partido."""
    campo = campo or Campo()
    grupos: list[list[Permanencia]] = []
    for p in perms:
        if campo.en_punto_de_saque(p.centro):
            continue
        for g in grupos:
            if distancia(p.centro, _mediana(g)) <= FIJO_AGRUPAR_CM:
                g.append(p)
                break
        else:
            grupos.append([p])
    objetos = []
    for g in grupos:
        if len(g) < FIJO_MIN_PERMANENCIAS or g[-1].t_inicio - g[0].t_inicio < FIJO_MIN_LAPSO_S:
            continue
        centro = _mediana(g)
        dispersion = float(np.percentile([distancia(p.centro, centro) for p in g], 90))
        objetos.append(ObjetoFijo(centro, max(FIJO_RADIO_MIN_CM, dispersion + FIJO_HOLGURA_CM), len(g), sum(p.duracion for p in g), g[0].t_inicio, g[-1].t_fin))
    return objetos


def _mediana(perms: list[Permanencia]) -> Punto:
    cx, cy = np.median(np.asarray([p.centro for p in perms]), axis=0)
    return (float(cx), float(cy))


def quitar_objetos_fijos(instantes: list[Instante], objetos: list[ObjetoFijo]) -> int:
    """Quita el balón que cae dentro del radio de un objeto fijo mientras está en el césped, salvo que vaya de paso."""
    if not objetos:
        return 0
    dentro = [inst.balon_campo is not None and any(_en_objeto(inst, o) for o in objetos) for inst in instantes]
    quitar = [i for i, d in enumerate(dentro) if d and not _de_paso(instantes, dentro, i)]
    for i in quitar:
        inst = instantes[i]
        inst.balon_imagen, inst.balon_campo, inst.balon_origen, inst.balon_confianza = None, None, None, None
    return len(quitar)


def _en_objeto(inst: Instante, objeto: ObjetoFijo) -> bool:
    return objeto.t_primera - FIJO_MARGEN_S <= inst.t <= objeto.t_ultima + FIJO_MARGEN_S and distancia(inst.balon_campo, objeto.centro) <= objeto.radio


def _de_paso(instantes: list[Instante], dentro: list[bool], i: int) -> bool:
    """El balón de `i` se ve también fuera del radio a menos de `FIJO_PASO_S`, a una velocidad posible: va de paso."""
    inst = instantes[i]
    for paso in (-1, 1):
        j = i + paso
        while 0 <= j < len(instantes) and abs(instantes[j].t - inst.t) <= FIJO_PASO_S:
            otro = instantes[j]
            if otro.balon_campo is not None and not dentro[j] and distancia(otro.balon_campo, inst.balon_campo) / abs(otro.t - inst.t) <= VELOCIDAD_MAX_CAMPO_CM_S:
                return True
            j += paso
    return False


def _entre(a: Punto, b: Punto, f: float) -> Punto:
    return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)


def rellenar_huecos(
    instantes: list[Instante], max_hueco_s: float = MAX_HUECO_S, velocidad_max_campo: float = VELOCIDAD_MAX_CAMPO_CM_S,
    velocidad_max_imagen: float = VELOCIDAD_MAX_IMAGEN_PX_S, max_hueco_guiado_s: float = MAX_HUECO_GUIADO_S, campo: Campo | None = None,
    margen_campo_cm: float = MARGEN_CAMPO_CM,
) -> Relleno:
    """Interpola el balón en los instantes sin él que quedan entre dos avistamientos separados como mucho `max_hueco_s`
    (o `max_hueco_guiado_s` si todo el hueco tiene la cámara calibrada) y con una velocidad plausible. En la imagen
    siempre; en el campo, con la calibración de cada instante (sigue a la cámara) y, si no la tiene o cae fuera del campo,
    en línea recta entre los dos extremos si los dos están proyectados."""
    campo = campo or Campo()
    relleno = Relleno()
    vistos = [i for i, inst in enumerate(instantes) if inst.balon_imagen is not None]
    for a, b in zip(vistos, vistos[1:]):
        if b - a <= 1:
            continue
        ia, ib = instantes[a], instantes[b]
        lapso = ib.t - ia.t
        if lapso <= 0:
            continue
        guiado = all(instantes[k].homografia is not None for k in range(a + 1, b))
        if lapso > (max(max_hueco_s, max_hueco_guiado_s) if guiado else max_hueco_s):
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
            siguiendo = _a_campo(inst, inst.balon_imagen)
            if siguiendo is not None and campo.dentro(siguiendo, margen=margen_campo_cm):
                inst.balon_campo = siguiendo
                relleno.guiados += 1
            else:
                inst.balon_campo = _entre(ia.balon_campo, ib.balon_campo, f) if en_campo else None
            inst.balon_origen = "interpolado"
            inst.balon_confianza = None
            relleno.interpolados += 1
    return relleno


@dataclass
class Intermedios:
    """Lo que aportaron los fotogramas vecinos: balones vistos en ellos e instantes que no tenían balón y lo tienen por
    ellos."""

    detecciones: int = 0
    completados: int = 0

    def a_dict(self) -> dict:
        return {"detecciones": self.detecciones, "completados": self.completados}


def completar_con_intermedios(
    instantes: list[Instante], pista: list[tuple[float, Punto, float]], dt_fotograma: float, campo: Campo | None = None, margen_campo_cm: float = 0.0,
    vecinos: int = INTERMEDIO_VECINOS,
) -> Intermedios:
    """Da a cada instante sin balón el que se vio en sus fotogramas vecinos (`pista`: [(t, (x, y) px, confianza)] en orden
    de tiempo), a `vecinos` fotogramas o menos. Con el instante calibrado (`homografia`), el balón se proyecta al campo y si
    cae fuera no vale."""
    campo = campo or Campo()
    resumen = Intermedios(detecciones=len(pista))
    if not pista:
        return resumen
    tiempos = np.array([p[0] for p in pista], dtype=np.float64)
    cerca = vecinos * dt_fotograma + 1e-3
    for inst in instantes:
        if inst.balon_imagen is not None:
            continue
        i = int(np.searchsorted(tiempos, inst.t))
        izq = i - 1 if i > 0 and inst.t - tiempos[i - 1] <= cerca else None
        der = i if i < len(pista) and tiempos[i] - inst.t <= cerca else None
        if izq is not None and der is not None:
            ta, xya, ca = pista[izq]
            tb, xyb, cb = pista[der]
            f = (inst.t - ta) / max(1e-6, tb - ta)
            estimado, conf = _entre(xya, xyb, f), min(ca, cb)
        elif izq is not None or der is not None:
            _, estimado, conf = pista[izq if izq is not None else der]
        else:
            continue
        en_campo = _a_campo(inst, estimado)
        if inst.homografia is not None and (en_campo is None or not campo.dentro(en_campo, margen=margen_campo_cm)):
            continue
        resumen.completados += 1
        inst.balon_imagen = (float(estimado[0]), float(estimado[1]))
        inst.balon_campo = en_campo
        inst.balon_origen = "visto"
        inst.balon_confianza = conf
    return resumen


def _a_campo(inst: Instante, xy: Punto) -> Punto | None:
    if inst.homografia is None:
        return None
    salida = inst.homografia.a_campo(np.array([xy], dtype=np.float32))
    if salida is None or len(salida) == 0:
        return None
    return (float(salida[0][0]), float(salida[0][1]))


def limpiar_trayectoria(instantes: list[Instante], campo: Campo | None = None) -> Relleno:
    """Lo primero tras la pasada: se quitan los objetos fijos y los avistamientos sueltos y se rellenan los huecos
    cortos. Los objetos fijos se buscan antes de rellenar, con lo que vio el detector."""
    objetos = objetos_fijos(permanencias(instantes), campo)
    fijos = quitar_objetos_fijos(instantes, objetos)
    aislados = quitar_aislados(instantes)
    relleno = rellenar_huecos(instantes, campo=campo)
    relleno.aislados_descartados = aislados
    relleno.fijos_descartados = fijos
    relleno.objetos_fijos = objetos
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
