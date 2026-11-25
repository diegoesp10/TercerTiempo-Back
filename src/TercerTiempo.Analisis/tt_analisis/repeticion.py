"""La repetición de un análisis, para verlo en el visor (`tt-analisis visor`): lo que vio el Ojeador en cada fotograma
analizado y lo que sacó de ello, en la carpeta `repeticion/` de la salida. No cambia nada del análisis y la app no la
lee: es para mirar a ojo qué hizo el analizador.

- `fotogramas.jsonl`: una línea por fotograma analizado, en orden de tiempo. `t` es el segundo del vídeo y va siempre lo
  primero de la línea: el visor indexa el archivo sin leerlo entero (`IndiceFotogramas`). `i` es el índice del
  fotograma y `h`, la homografía imagen → campo (cm), por filas, o null si no está calibrado. En `p` va cada persona:
  [id final, tracklet, clase, equipo, x1, y1, x2, y2] y, si el seguidor le dio otro tracklet, ese al final. El id final
  es el del jugador si se le identificó. El tracklet es el de después de colocar a los porteros. El equipo es -1 si no
  tiene. En `b` va el balón de ese instante, [x, y, origen, confianza], con origen "v" (visto en su fotograma), "n" (en
  uno vecino) o "i" (interpolado). `bc` es ese balón en el campo (cm). `bd` es lo que vio el detector y se descartó (un
  objeto quieto o un avistamiento suelto). `v` es el balón visto en los fotogramas vecinos, [t, x, y, confianza].
- `final.json`: lo de todo el tramo. Trae los equipos, el campo (vértices y aristas del modelo), cada jugador con cómo se
  le identificó y, de cada tracklet, su cadena y su método. Por instante, en cadenas de un carácter, el origen del balón
  (y "x" descartado, "-" sin balón), si está en juego, a quién se le cuenta la posesión, si está calibrado y la parte.
  Y los toques, los eventos, el segundo de cada acción que cuenta y los objetos fijos descartados.
"""

from __future__ import annotations

import json
import re
from bisect import bisect_left, bisect_right
from collections import defaultdict
from pathlib import Path

import numpy as np

from .geometria import Campo
from .identidades import ID_JUGADOR_BASE
from .porteros import TRACKLET_RESTO_BASE
from .tipos import Instante, Punto

FORMATO = 1
CARPETA = "repeticion"
FOTOGRAMAS = "fotogramas.jsonl"
FINAL = "final.json"
# Lo que se lee de una vez: el visor pide tramos cortos alrededor de donde está el vídeo.
MAX_VENTANA_S = 120.0


def origenes_balon(instantes: list[Instante], crudo: list[tuple[Punto, float | None] | None]) -> str:
    """De dónde sale el balón de cada instante, un carácter por instante: "v" visto en su fotograma, "n" visto en uno
    vecino, "i" interpolado, "x" lo vio el detector pero se descartó y "-" sin balón. `crudo` es lo que vio el detector
    en cada instante antes de completar y limpiar la trayectoria."""
    letras = []
    for inst, antes in zip(instantes, crudo):
        if inst.balon_imagen is None:
            letras.append("x" if antes is not None else "-")
        elif inst.balon_origen == "interpolado":
            letras.append("i")
        else:
            letras.append("v" if antes is not None else "n")
    return "".join(letras)


def repartir_vecinos(pista: list[tuple[float, Punto, float]], tiempos: list[float]) -> dict[int, list[list[float]]]:
    """Cada balón visto en un fotograma vecino, al instante analizado más cercano."""
    if not pista or not tiempos:
        return {}
    t = np.asarray(tiempos, dtype=np.float64)
    salida: dict[int, list[list[float]]] = defaultdict(list)
    for tp, xy, conf in pista:
        i = int(np.searchsorted(t, tp))
        if i > 0 and (i == len(t) or tp - t[i - 1] <= t[i] - tp):
            i -= 1
        salida[i].append([round(float(tp), 3), round(float(xy[0]), 1), round(float(xy[1]), 1), round(float(conf), 2)])
    return salida


def seguidor_de(tracklet: int) -> int:
    """El tracklet que dio el seguidor: el rato de un portero que es de otra persona pasa a TRACKLET_RESTO_BASE + él."""
    return tracklet - TRACKLET_RESTO_BASE if TRACKLET_RESTO_BASE <= tracklet < ID_JUGADOR_BASE else tracklet


def _redondeo(v: float) -> float:
    return float(f"{v:.6g}")


def linea_fotograma(inst: Instante, tracklets: list[int] | None, origen: str, crudo: tuple[Punto, float | None] | None, vecinos: list[list[float]]) -> str:
    linea: dict = {"t": round(inst.t, 3), "i": inst.indice}
    matriz = getattr(inst.homografia, "m", None) if inst.homografia is not None else None
    linea["h"] = None if matriz is None else [_redondeo(float(v)) for v in np.asarray(matriz, dtype=np.float64).ravel()]
    personas = []
    for j, p in enumerate(inst.personas):
        tracklet = tracklets[j] if tracklets is not None and j < len(tracklets) else p.tracklet
        x1, y1, x2, y2 = (int(round(v)) for v in p.caja)
        fila = [p.tracklet, tracklet, p.clase, -1 if p.equipo is None else p.equipo, x1, y1, x2, y2]
        seguidor = seguidor_de(tracklet)
        if seguidor != tracklet:
            fila.append(seguidor)
        personas.append(fila)
    linea["p"] = personas
    if inst.balon_imagen is not None:
        conf = None if inst.balon_confianza is None else round(float(inst.balon_confianza), 2)
        linea["b"] = [round(float(inst.balon_imagen[0]), 1), round(float(inst.balon_imagen[1]), 1), origen, conf]
        if inst.balon_campo is not None:
            linea["bc"] = [int(round(inst.balon_campo[0])), int(round(inst.balon_campo[1]))]
    elif origen == "x" and crudo is not None:
        (x, y), conf = crudo
        linea["bd"] = [round(float(x), 1), round(float(y), 1), None if conf is None else round(float(conf), 2)]
    if vecinos:
        linea["v"] = vecinos
    return json.dumps(linea, ensure_ascii=False, separators=(",", ":"))


def campo_para_visor(campo: Campo) -> dict:
    """El modelo del campo con el que se calibró: sus 32 vértices (cm) y las líneas que los unen (índices desde 0)."""
    from .campo import CONFIG_CAMPO

    return {
        "largo_cm": campo.largo,
        "ancho_cm": campo.ancho,
        "vertices": [[float(x), float(y)] for x, y in CONFIG_CAMPO.vertices],
        "aristas": [[a - 1, b - 1] for a, b in CONFIG_CAMPO.edges],
        "circulo_cm": float(CONFIG_CAMPO.centre_circle_radius),
        "penalti_cm": float(CONFIG_CAMPO.penalty_spot_distance),
        "area_largo_cm": float(CONFIG_CAMPO.penalty_box_length),
    }


def _acciones(jugadas) -> dict[str, dict]:
    salida = {}
    for tid, a in sorted(jugadas.acciones.items()):
        resumen = jugadas.resumenes.get(tid)
        salida[str(tid)] = {
            "equipo": getattr(resumen, "equipo", None),
            "toques": a.toques,
            "pases": [round(t, 2) for t in a.segundos_pases],
            "pases_fallados": [round(t, 2) for t in a.segundos_pases_fallados],
            "perdidas": [round(t, 2) for t in a.segundos_perdidas],
            "recuperaciones": [round(t, 2) for t in a.segundos_recuperaciones],
            "paradas": [round(t, 2) for t in a.segundos_paradas],
            "saques": [round(t, 2) for t in a.segundos_saques],
        }
    return salida


def escribir_repeticion(
    salida: Path,
    instantes: list[Instante],
    tracklets: list[list[int]] | None,
    balon_crudo: list[tuple[Punto, float | None] | None],
    pista: list[tuple[float, Punto, float]],
    *,
    analizador: dict,
    video: dict,
    dt: float,
    equipos: list[dict],
    campo: Campo,
    jugadas,
    resolucion=None,
    recortes: dict[int, str] | None = None,
    objetos_fijos: list | None = None,
) -> str:
    """Escribe `repeticion/fotogramas.jsonl` y `repeticion/final.json` (ver el principio del módulo). `tracklets`: de
    cada instante, el tracklet de cada persona antes de pasar a su jugador (None si no se juntaron por dorsal);
    `balon_crudo`: lo que vio el detector en cada instante antes de completar y limpiar la trayectoria; `pista`: el
    balón visto en los fotogramas vecinos. Devuelve la ruta de `final.json` relativa a la salida."""
    carpeta = salida / CARPETA
    carpeta.mkdir(parents=True, exist_ok=True)
    origen = origenes_balon(instantes, balon_crudo)
    vecinos = repartir_vecinos(pista, [inst.t for inst in instantes])
    with (carpeta / FOTOGRAMAS).open("w", encoding="utf-8", newline="\n") as f:
        for k, inst in enumerate(instantes):
            f.write(linea_fotograma(inst, tracklets[k] if tracklets is not None else None, origen[k], balon_crudo[k], vecinos.get(k, [])))
            f.write("\n")

    en_juego = jugadas.en_juego.en_juego if jugadas.en_juego is not None else jugadas.juego_activo
    posesion = jugadas.posesion.por_instante
    recortes = recortes or {}
    final = {
        "formato": FORMATO,
        "analizador": {"nombre": analizador.get("nombre"), "version": analizador.get("version")},
        "video": video,
        "dt": round(dt, 4),
        "equipos": equipos,
        "campo": campo_para_visor(campo),
        "descanso_s": jugadas.descanso,
        "direcciones_ataque": {f"equipo{e}_mitad{m}": d for (e, m), d in sorted(jugadas.direcciones.items())},
        "instantes": {
            "t": [round(inst.t, 3) for inst in instantes],
            "balon": origen,
            "en_juego": "".join("1" if x else "0" for x in en_juego),
            "posesion": "".join("-" if e is None else str(e) for e in posesion),
            "calibrado": "".join("1" if inst.calibrado else "0" for inst in instantes),
            "mitad": "".join(str(m) for m in jugadas.mitad_de),
        },
        "toques": [[round(t.t_inicio, 2), round(t.t_fin, 2), t.tracklet, t.equipo, int(t.parado)] for t in jugadas.toques],
        "jugadores": [] if resolucion is None else [
            {
                "id": x.id, "equipo": x.equipo, "dorsal": x.dorsal, "nombre": x.nombre, "portero": x.portero, "confianza": x.confianza,
                "por_continuidad": x.por_continuidad, "por_apariencia": x.por_apariencia, "por_descarte": x.por_descarte,
                "tracklets": len(x.tracklets), "recorte": recortes.get(x.id),
            }
            for x in resolucion.identidades
        ],
        "tracklets": {} if resolucion is None else {str(t): {"cadena": c, "metodo": resolucion.metodo.get(t)} for t, c in sorted(resolucion.cadena.items())},
        "acciones": _acciones(jugadas),
        "eventos": [e.a_dict() for e in jugadas.eventos],
        "objetos_fijos": [o.a_dict() for o in objetos_fijos or []],
    }
    (carpeta / FINAL).write_text(json.dumps(final, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    return f"{CARPETA}/{FINAL}"


class IndiceFotogramas:
    """`fotogramas.jsonl` sin leerlo entero: dónde empieza y acaba cada línea y su segundo. `actualizar()` solo lee lo
    que se haya añadido desde la última vez, y una última línea a medio escribir se deja para la siguiente."""

    _SEGUNDO = re.compile(rb'^\{"t":(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)[,}]')

    def __init__(self, ruta: Path) -> None:
        self.ruta = ruta
        self.tiempos: list[float] = []
        self._inicios: list[int] = []
        self._finales: list[int] = []
        self._leido = 0
        self.actualizar()

    def __len__(self) -> int:
        return len(self.tiempos)

    def actualizar(self) -> None:
        if not self.ruta.exists():
            return
        with self.ruta.open("rb") as f:
            f.seek(self._leido)
            posicion = self._leido
            for linea in f:
                if not linea.endswith(b"\n"):
                    break
                encontrado = self._SEGUNDO.match(linea)
                if encontrado:
                    self.tiempos.append(float(encontrado.group(1)))
                    self._inicios.append(posicion)
                    self._finales.append(posicion + len(linea.rstrip(b"\r\n")))
                posicion += len(linea)
            self._leido = posicion

    def entre(self, desde: float, hasta: float) -> bytes:
        """Las líneas de los fotogramas de `desde` a `hasta` (como mucho MAX_VENTANA_S), como un array JSON."""
        hasta = min(hasta, desde + MAX_VENTANA_S)
        a = bisect_left(self.tiempos, desde)
        b = bisect_right(self.tiempos, hasta)
        if a >= b:
            return b"[]"
        with self.ruta.open("rb") as f:
            f.seek(self._inicios[a])
            bloque = f.read(self._finales[b - 1] - self._inicios[a])
        lineas = [linea.strip() for linea in bloque.split(b"\n")]
        return b"[" + b",".join(linea for linea in lineas if linea) + b"]"
