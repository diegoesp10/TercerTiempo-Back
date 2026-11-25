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

Mientras se analiza (`Directo`, desde el 25-11-2026), para seguir el análisis en el visor:

- `directo.json`: el analizador, el vídeo y la etapa desde que empieza y, desde la pasada principal, los equipos, el
  campo, el paso entre instantes y cuántos fotogramas se esperan. Se reescribe entero cada LATIDO_S, aunque no haya nada
  nuevo (también en pausa o en las etapas sin fotogramas), con `actualizado`, lo último que ha dicho el analizador
  (`hitos`) y, si algo falla, `error`.
- `directo.jsonl`: lo provisional de cada instante de la pasada, con el formato de `fotogramas.jsonl`. El id final y el
  tracklet son el del seguidor (quién es quién y los porteros se deciden al terminar), el equipo es el de su voto hasta
  ese momento y el balón es el visto en su fotograma ("v") y el de sus vecinos (`v`).

Al terminar se escribe la repetición definitiva y se borra lo provisional; si el análisis falla, se queda con el error.
"""

from __future__ import annotations

import json
import os
import re
import sys
import threading
import time
from bisect import bisect_left, bisect_right
from collections import defaultdict
from pathlib import Path
from typing import Callable, Iterable

import numpy as np

from .geometria import Campo
from .identidades import ID_JUGADOR_BASE
from .porteros import TRACKLET_RESTO_BASE
from .tipos import Instante, Punto

FORMATO = 1
CARPETA = "repeticion"
FOTOGRAMAS = "fotogramas.jsonl"
FINAL = "final.json"
DIRECTO = "directo.jsonl"
ESTADO_DIRECTO = "directo.json"
FORMATO_DIRECTO = 1
# Lo que se lee de una vez: el visor pide tramos cortos alrededor de donde está el vídeo.
MAX_VENTANA_S = 120.0
# Cada cuánto se reescribe directo.json mientras se analiza. El visor da el análisis por parado si pasa mucho más.
LATIDO_S = 2.0
MAX_HITOS = 12
# Windows no deja reemplazar ni borrar un archivo mientras otro proceso (el visor) lo tiene abierto: se reintenta un rato.
INTENTOS_ARCHIVO = 40
ESPERA_ARCHIVO_S = 0.025


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
    return json.dumps(datos_fotograma(inst, tracklets, origen, crudo, vecinos), ensure_ascii=False, separators=(",", ":"))


def datos_fotograma(inst: Instante, tracklets: list[int] | None, origen: str, crudo: tuple[Punto, float | None] | None, vecinos: list[list[float]]) -> dict:
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
    return linea


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


def _reemplazar(origen: Path, destino: Path) -> None:
    for intento in range(INTENTOS_ARCHIVO):
        try:
            os.replace(origen, destino)
            return
        except PermissionError:
            if intento == INTENTOS_ARCHIVO - 1:
                raise
            time.sleep(ESPERA_ARCHIVO_S)


def _borrar(ruta: Path) -> bool:
    for intento in range(INTENTOS_ARCHIVO):
        try:
            ruta.unlink(missing_ok=True)
            return True
        except PermissionError:
            time.sleep(ESPERA_ARCHIVO_S)
        except OSError:
            return False
    return False


class Directo:
    """La repetición mientras se analiza (ver el principio del módulo), para seguir el análisis en el visor. Solo lee lo
    que le pasa el análisis, sin tocarlo, y nada de lo que haga puede pararlo: si no puede escribir, se apaga (y quita lo
    provisional) y el análisis sigue igual. Cada línea de `directo.jsonl` se escribe al llegar el instante siguiente, cuando ya se sabe qué
    balón de los fotogramas vecinos es suyo (el más cercano, como en `repartir_vecinos`)."""

    def __init__(self, salida: Path, activo: bool = True, latido_s: float = LATIDO_S, reloj: Callable[[], float] = time.time) -> None:
        self.activo = activo
        self.carpeta = salida / CARPETA
        self.empezado = False
        self._latido_s = latido_s
        self._reloj = reloj
        self._avance = None
        self._estado: dict = {}
        self._hitos: list[str] = []
        self._archivo = None
        self._pendiente: dict | None = None
        self._pendiente_t = 0.0
        # Los balones de los fotogramas vecinos que aún no tienen instante: (su segundo, cómo van en la línea).
        self._vecinos: list[tuple[float, list[float]]] = []
        self._escritos = 0
        # Lo que se reescribe en directo.json lo cambian la pasada y el latido, cada uno en su hilo.
        self._cerrojo = threading.Lock()
        self._escritura = threading.Lock()
        self._parar = threading.Event()
        self._hilo: threading.Thread | None = None

    def empezar(self, *, analizador: dict, video: dict, equipos: list[dict] | None = None, avance=None) -> None:
        """Al empezar el análisis, para que el visor lo enseñe en su lista (con el nombre de los equipos, si se sabe).
        `avance` (`avance.Avance`): de él se toman la etapa y si está en pausa. Lo provisional de otro análisis en esta
        carpeta que se cortó ya no vale."""
        if not self.activo or self.empezado:
            return
        try:
            self.carpeta.mkdir(parents=True, exist_ok=True)
            _borrar(self.carpeta / DIRECTO)
            self._avance = avance
            self._estado = {"formato": FORMATO_DIRECTO, "analizador": analizador, "video": video, "inicio": round(self._reloj(), 3)}
            if equipos:
                self._estado["equipos"] = equipos
            self.empezado = True
            self._escribir_estado()
        except Exception as error:  # noqa: BLE001 - el visor no puede parar el análisis
            self._apagar(error)
            return
        self._parar.clear()
        self._hilo = threading.Thread(target=self._latir, name="repeticion-en-directo", daemon=True)
        self._hilo.start()

    def con_hitos(self, decir: Callable[[str], None]) -> Callable[[str], None]:
        """`decir` que además apunta el texto entre lo último que ha dicho el analizador."""
        if not self.empezado:
            return decir

        def decir_y_apuntar(texto: str) -> None:
            limpio = " ".join(str(texto).split())[:300]
            with self._cerrojo:
                self._hitos.append(limpio)
                del self._hitos[:-MAX_HITOS]
            decir(texto)

        return decir_y_apuntar

    def empezar_pasada(self, *, video: dict, dt: float, equipos: list[dict], campo: Campo, total: int) -> None:
        if not self.empezado or not self.activo:
            return
        try:
            self._archivo = (self.carpeta / DIRECTO).open("w", encoding="utf-8", newline="\n")
            with self._cerrojo:
                self._estado.update(
                    video=video, dt=round(dt, 4), equipos=equipos, campo=campo_para_visor(campo), fotogramas_total=total, fotogramas=0,
                    inicio_pasada=round(self._reloj(), 3),
                )
            self._escribir_estado()
        except Exception as error:  # noqa: BLE001
            self._apagar(error)

    def fotograma(self, inst: Instante, vecinos: Iterable[tuple[float, Punto, float]] = (), tracklets: int | None = None) -> None:
        """El instante recién analizado. `vecinos`: el balón visto en los fotogramas de entre el anterior y este (los que
        da `Detector.balon_vecinos`); `tracklets`: cuántos lleva el seguidor."""
        if self._archivo is None:
            return
        try:
            for tp, xy, conf in vecinos:
                self._vecinos.append((float(tp), [round(float(tp), 3), round(float(xy[0]), 1), round(float(xy[1]), 1), round(float(conf), 2)]))
            if self._pendiente is not None:
                self._soltar(inst.t)
            self._pendiente = datos_fotograma(inst, None, "v" if inst.balon_imagen is not None else "-", None, [])
            self._pendiente_t = inst.t
            if tracklets is not None:
                with self._cerrojo:
                    self._estado["tracklets"] = int(tracklets)
        except Exception as error:  # noqa: BLE001
            self._apagar(error)

    def fin_pasada(self) -> None:
        if self._archivo is None:
            return
        try:
            if self._pendiente is not None:
                self._soltar(None)
            self._archivo.close()
            self._archivo = None
            self._escribir_estado()
        except Exception as error:  # noqa: BLE001
            self._apagar(error)

    def detener(self) -> None:
        """Para el latido; antes de escribir la repetición definitiva, para que directo.json quede más viejo que ella."""
        self._parar.set()
        if self._hilo is not None:
            self._hilo.join(timeout=10.0)
            self._hilo = None
        if self._archivo is not None:
            try:
                self._archivo.close()
            except OSError:
                pass
            self._archivo = None

    def terminar(self) -> None:
        """Con la repetición definitiva ya escrita, fuera lo provisional. Si el visor lo tiene abierto y no se puede borrar,
        se queda: con final.json más nuevo, el visor ya no lo mira."""
        if not self.empezado:
            return
        self.detener()
        for nombre in (DIRECTO, ESTADO_DIRECTO):
            _borrar(self.carpeta / nombre)
        self.empezado = False

    def fallo(self, error: BaseException) -> None:
        """El análisis no ha terminado: lo provisional se queda, con el porqué."""
        if not self.empezado or not self.activo:
            return
        self.detener()
        texto = "Parado a mano." if isinstance(error, KeyboardInterrupt) else (" ".join(str(error).split()) or type(error).__name__)
        with self._cerrojo:
            self._estado["error"] = texto[:500]
        try:
            self._escribir_estado()
        except Exception:  # noqa: BLE001
            pass

    def _soltar(self, siguiente: float | None) -> None:
        datos, self._pendiente = self._pendiente, None
        t = self._pendiente_t
        suyos, quedan = [], []
        for tp, fila in self._vecinos:
            if siguiente is None or tp - t <= siguiente - tp:
                suyos.append(fila)
            else:
                quedan.append((tp, fila))
        self._vecinos = quedan
        if suyos:
            datos["v"] = suyos
        self._archivo.write(json.dumps(datos, ensure_ascii=False, separators=(",", ":")) + "\n")
        self._archivo.flush()
        self._escritos += 1
        with self._cerrojo:
            self._estado["fotogramas"] = self._escritos
            self._estado["t"] = datos["t"]

    def _escribir_estado(self) -> None:
        with self._cerrojo:
            estado = dict(self._estado, hitos=list(self._hitos))
        if self._avance is not None:
            estado["etapa"] = self._avance.etapa_actual
            estado["pausado"] = bool(self._avance.pausado)
        estado["actualizado"] = round(self._reloj(), 3)
        texto = json.dumps(estado, ensure_ascii=False, separators=(",", ":"))
        ruta = self.carpeta / ESTADO_DIRECTO
        with self._escritura:
            temporal = ruta.with_name(ESTADO_DIRECTO + ".tmp")
            temporal.write_text(texto, encoding="utf-8")
            _reemplazar(temporal, ruta)

    def _latir(self) -> None:
        while not self._parar.wait(self._latido_s):
            try:
                self._escribir_estado()
            except Exception:  # noqa: BLE001 - el siguiente latido lo vuelve a intentar
                pass

    def _apagar(self, error: BaseException) -> None:
        self.activo = False
        self.detener()
        # Sin latido, el visor acabaría dando por parado un análisis que sigue: mejor que deje de verlo en directo.
        for nombre in (DIRECTO, ESTADO_DIRECTO):
            _borrar(self.carpeta / nombre)
        print(f"Aviso: la repetición en directo se ha parado ({type(error).__name__}: {error}); el análisis sigue igual.", file=sys.stderr, flush=True)


class IndiceFotogramas:
    """`fotogramas.jsonl` (o `directo.jsonl`) sin leerlo entero: dónde empieza y acaba cada línea, su segundo y, en una
    letra, de dónde sale su balón y si está calibrado (para seguir un análisis en marcha sin pedir todas sus líneas).
    `actualizar()` solo lee lo que se haya añadido desde la última vez, y una última línea a medio escribir se deja para
    la siguiente. Si el archivo encoge (otro análisis en la misma carpeta), se vuelve a indexar desde el principio."""

    _SEGUNDO = re.compile(rb'^\{"t":(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)[,}]')
    _ORIGEN = re.compile(rb'"b":\[[^\]"]*"([vnix])"')

    def __init__(self, ruta: Path) -> None:
        self.ruta = ruta
        self._vaciar()
        self.actualizar()

    def _vaciar(self) -> None:
        self.tiempos: list[float] = []
        self.balon = bytearray()
        self.calibrado = bytearray()
        self._inicios: list[int] = []
        self._finales: list[int] = []
        self._leido = 0

    def __len__(self) -> int:
        return len(self.tiempos)

    def actualizar(self) -> None:
        try:
            if self.ruta.stat().st_size < self._leido:
                self._vaciar()
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
                        self.balon += _origen_linea(linea, self._ORIGEN)
                        self.calibrado += b"0" if b'"h":null' in linea else b"1"
                    posicion += len(linea)
                self._leido = posicion
        except OSError:
            return

    def resumen(self, desde: int = 0) -> dict:
        """Desde el instante `desde`: su segundo, de dónde sale el balón ("v" visto en su fotograma, "n" en uno vecino,
        "i" interpolado, "x" descartado, "-" sin balón) y si está calibrado ("1"/"0")."""
        desde = max(0, desde)
        return {
            "desde": desde,
            "t": self.tiempos[desde:],
            "balon": self.balon[desde:].decode("ascii"),
            "calibrado": self.calibrado[desde:].decode("ascii"),
        }

    def entre(self, desde: float, hasta: float) -> bytes:
        """Las líneas de los fotogramas de `desde` a `hasta` (como mucho MAX_VENTANA_S), como un array JSON."""
        hasta = min(hasta, desde + MAX_VENTANA_S)
        a = bisect_left(self.tiempos, desde)
        b = bisect_right(self.tiempos, hasta)
        if a >= b:
            return b"[]"
        try:
            with self.ruta.open("rb") as f:
                f.seek(self._inicios[a])
                bloque = f.read(self._finales[b - 1] - self._inicios[a])
        except OSError:
            # Lo provisional se borra al terminar el análisis: el visor pasa entonces a la repetición definitiva.
            return b"[]"
        lineas = [linea.strip() for linea in bloque.split(b"\n")]
        return b"[" + b",".join(linea for linea in lineas if linea) + b"]"


def _origen_linea(linea: bytes, patron: re.Pattern) -> bytes:
    encontrado = patron.search(linea)
    if encontrado:
        return encontrado.group(1)
    if b'"bd":[' in linea:
        return b"x"
    # Lo provisional no trae balón de vecinos puesto en el instante: los vecinos vistos cuentan como vistos en uno vecino.
    return b"n" if b'"v":[' in linea else b"-"
