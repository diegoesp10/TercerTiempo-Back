"""El visor: un servidor local (solo 127.0.0.1) que enseña en una ventana propia el vídeo de un análisis con lo que vio y
sacó el Ojeador encima (`repeticion.py`). Solo lee: las carpetas de análisis, su vídeo y la página de `estatico/`. La
ventana es Edge (o Chrome) en modo aplicación con un perfil propio; al cerrarla, el visor termina.

Un análisis que se está haciendo (con `repeticion/directo.json` y sin `final.json` más nuevo) sale en la lista desde que
empieza y se puede abrir: la página sigue lo provisional (`directo.jsonl`) según avanza y, en cuanto aparece la
repetición definitiva, pasa a ella."""

from __future__ import annotations

import hashlib
import json
import mimetypes
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import webbrowser
from dataclasses import dataclass, field
from datetime import datetime
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

from ..repeticion import CARPETA, DIRECTO, ESTADO_DIRECTO, FINAL, FOTOGRAMAS, IndiceFotogramas

ESTATICO = Path(__file__).parent / "estatico"
BLOQUE = 1 << 20
# Un análisis en marcha reescribe directo.json cada pocos segundos (`repeticion.LATIDO_S`), también en pausa: sin noticias
# en este rato, se ha parado (el proceso se cortó o se mató, o el equipo se durmió).
SIN_NOTICIAS_S = 30.0
# Sin latido de la página en este tiempo, el visor se cierra (al cerrar la ventana avisa; esto es por si no puede). Una
# ventana minimizada sigue latiendo, aunque el navegador la frene a una vez por minuto.
SIN_LATIDO_S = 300.0
# Al cerrar o recargar, la página se despide; si en este rato no vuelve a cargarse, el visor se cierra.
ESPERA_ADIOS_S = 6.0
# Al buscar análisis en una carpeta, hasta esta profundidad (salidas/<partido>/<pasada>/analisis.json).
PROFUNDIDAD_BUSQUEDA = 3
NOMBRE_RECORTE = re.compile(r"^[\w.-]+\.(jpg|jpeg|png)$", re.IGNORECASE)
ARCHIVOS_JSON = {"analisis.json": "analisis.json", "acta-propuesta.json": "acta-propuesta.json", "final.json": f"{CARPETA}/{FINAL}"}


def identificador(carpeta: Path) -> str:
    return hashlib.sha1(str(carpeta).lower().encode("utf-8")).hexdigest()[:12]


def leer_json(ruta: Path) -> dict | None:
    try:
        return json.loads(ruta.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def absoluta(ruta: Path) -> Path:
    # Sin resolve(): con las carpetas de Docker Desktop (\\wsl.localhost\...) puede fallar.
    return Path(os.path.abspath(os.path.expanduser(str(ruta))))


def es_analisis(carpeta: Path) -> bool:
    """Una carpeta de análisis: con analisis.json o con un análisis empezado (aún sin analisis.json)."""
    return (carpeta / "analisis.json").exists() or (carpeta / CARPETA / ESTADO_DIRECTO).exists()


def _mtime(ruta: Path) -> float | None:
    try:
        return ruta.stat().st_mtime
    except OSError:
        return None


def en_curso(estado: dict) -> dict:
    """Lo de un análisis en marcha que se enseña en la lista."""
    return {k: estado.get(k) for k in ("etapa", "vivo", "pausado", "error", "fotogramas", "fotogramas_total", "t", "inicio", "actualizado")}


@dataclass
class Analisis:
    carpeta: Path
    # El vídeo indicado a mano; si no, el de analisis.json (o el del análisis en marcha).
    video: Path | None = None
    _indice: IndiceFotogramas | None = None
    # De qué repetición es el índice: ("final", fecha de final.json) o ("directo", cuándo empezó ese análisis).
    _generacion: tuple | None = None
    # Lo de analisis.json que sale en la lista, por su fecha (la lista se pide cada pocos segundos).
    _resumen: tuple[int, dict] | None = None
    _cerrojo: threading.Lock = field(default_factory=threading.Lock)

    @property
    def id(self) -> str:
        return identificador(self.carpeta)

    def _ruta(self, nombre: str) -> Path:
        return self.carpeta / CARPETA / nombre

    def modo(self) -> tuple[str | None, dict | None]:
        """("directo", estado) si en la carpeta se está analizando, ("final", None) con la repetición definitiva y (None,
        None) sin ninguna. Manda lo más nuevo: un análisis vuelto a lanzar en la misma carpeta tapa al anterior mientras
        se hace; si se para sin terminar, vuelve a verse el anterior terminado, si lo había."""
        final = _mtime(self._ruta(FINAL))
        estado = leer_json(self._ruta(ESTADO_DIRECTO))
        if estado is not None:
            vivo = not estado.get("error") and time.time() - float(estado.get("actualizado") or 0) <= SIN_NOTICIAS_S
            fecha = _mtime(self._ruta(ESTADO_DIRECTO))
            mas_nuevo = final is None or (fecha is not None and fecha > final)
            if mas_nuevo and (vivo or final is None):
                estado["vivo"] = vivo
                return "directo", estado
        return ("final" if final is not None else None), None

    def ruta_video(self, modo: str | None = None, estado: dict | None = None) -> Path | None:
        if self.video is not None:
            return self.video
        if modo is None:
            modo, estado = self.modo()
        ruta = ((estado if modo == "directo" else self._de_analisis_json()).get("video") or {}).get("ruta")
        return Path(ruta) if ruta else None

    def indice(self, modo: str | None = None, estado: dict | None = None) -> IndiceFotogramas | None:
        if modo is None:
            modo, estado = self.modo()
        if modo is None:
            return None
        if modo == "final":
            ruta, marca = self._ruta(FOTOGRAMAS), _mtime(self._ruta(FINAL))
        else:
            ruta, marca = self._ruta(DIRECTO), estado.get("inicio")
        with self._cerrojo:
            if self._indice is None or self._generacion != (modo, marca):
                self._indice = IndiceFotogramas(ruta)
                self._generacion = (modo, marca)
            else:
                self._indice.actualizar()
            return self._indice

    def _de_analisis_json(self) -> dict:
        ruta = self.carpeta / "analisis.json"
        try:
            marca = ruta.stat().st_mtime_ns
        except OSError:
            return {}
        with self._cerrojo:
            if self._resumen is None or self._resumen[0] != marca:
                datos = leer_json(ruta) or {}
                self._resumen = (marca, {
                    "video": datos.get("video"), "analizador": datos.get("analizador"), "equipos": datos.get("equipos"),
                    "pct_con_balon": (datos.get("recuento") or {}).get("pct_con_balon"),
                    "jugadores": len((datos.get("identificacion") or {}).get("jugadores") or []),
                })
            return self._resumen[1]

    def ficha(self) -> dict:
        """Lo que se enseña en la lista de análisis."""
        modo, estado = self.modo()
        if modo == "directo":
            # De un análisis en marcha, lo suyo: no lo que dejó en la carpeta uno anterior.
            datos = {"video": estado.get("video"), "analizador": estado.get("analizador"), "equipos": estado.get("equipos")}
            fecha = estado.get("inicio")
        else:
            datos = self._de_analisis_json()
            fecha = _mtime(self.carpeta / "analisis.json")
        video = datos.get("video") or {}
        analizador = datos.get("analizador") or {}
        ruta_video = self.ruta_video(modo, estado)
        try:
            analizado = datetime.fromtimestamp(float(fecha)).isoformat(timespec="minutes") if fecha is not None else None
        except (OverflowError, OSError, ValueError):
            analizado = None
        return {
            "id": self.id,
            "carpeta": str(self.carpeta),
            "equipos": [e.get("nombre") for e in datos.get("equipos") or []],
            "analizador": " ".join(str(x) for x in (analizador.get("nombre"), analizador.get("version")) if x) or None,
            "tramo": [video.get("tramo_inicio_s"), video.get("tramo_fin_s")],
            "duracion_video_s": video.get("duracion_s"),
            "analizado": analizado,
            "repeticion": modo == "final",
            "en_curso": en_curso(estado) if modo == "directo" else None,
            "video": str(ruta_video) if ruta_video else None,
            "video_existe": bool(ruta_video and ruta_video.exists()),
            "balon_pct": datos.get("pct_con_balon"),
            "jugadores": datos.get("jugadores") or 0,
        }

    def directo(self, desde: int) -> dict:
        """Lo que pide la página para seguir un análisis en marcha: su estado y, desde el instante `desde`, el segundo de
        cada uno, de dónde sale su balón y si está calibrado. Con la repetición definitiva ya escrita, `terminado`, y
        `parado` si esa repetición es la de un análisis anterior porque el que se hacía se ha parado sin terminar. Sin lo
        uno ni lo otro, `sin_directo` (el analizador dejó de escribir lo provisional o se vació la carpeta)."""
        modo, estado = self.modo()
        if modo != "directo":
            respuesta = {"terminado": modo == "final"}
            if modo == "final":
                # Al terminar bien, directo.json deja de escribirse antes de final.json (y luego se borra).
                quedo = leer_json(self._ruta(ESTADO_DIRECTO))
                fecha, final = _mtime(self._ruta(ESTADO_DIRECTO)), _mtime(self._ruta(FINAL))
                if quedo is not None and fecha is not None and final is not None and fecha > final:
                    respuesta["parado"] = quedo.get("error") or "sin noticias del análisis"
            else:
                respuesta["sin_directo"] = True
            return respuesta
        indice = self.indice(modo, estado)
        return dict(estado, terminado=False, instantes=indice.resumen(desde) if indice is not None else {"desde": desde, "t": [], "balon": "", "calibrado": ""})


class Biblioteca:
    """Los análisis que el visor puede abrir: los de las carpetas que se le dan (o los que haya dentro) y los que se
    abren desde la página con su ruta."""

    def __init__(self, carpetas: list[Path], video: Path | None = None) -> None:
        self.raices: list[Path] = []
        self.analisis: dict[str, Analisis] = {}
        self.inicial: str | None = None
        # El servidor atiende cada petición en su hilo.
        self._cerrojo = threading.RLock()
        for carpeta in carpetas:
            carpeta = absoluta(carpeta)
            if es_analisis(carpeta):
                a = self.registrar(carpeta, video)
                self.inicial = self.inicial or a.id
            else:
                self.raices.append(carpeta)

    def registrar(self, carpeta: Path, video: Path | None = None) -> Analisis:
        carpeta = absoluta(carpeta)
        ident = identificador(carpeta)
        with self._cerrojo:
            if ident not in self.analisis:
                self.analisis[ident] = Analisis(carpeta, absoluta(video) if video else None)
            return self.analisis[ident]

    def buscar(self) -> list[Analisis]:
        with self._cerrojo:
            for raiz in self.raices:
                for carpeta in _con_analisis(raiz, PROFUNDIDAD_BUSQUEDA):
                    self.registrar(carpeta)
            # Uno que se ha quedado sin nada que ver (se vació su carpeta o se borró) no sale en la lista.
            todos = [a for a in self.analisis.values() if es_analisis(a.carpeta)]
        return sorted(todos, key=_fecha, reverse=True)

    def get(self, ident: str) -> Analisis | None:
        with self._cerrojo:
            # Uno de las carpetas que aún no se han recorrido (la página recargada con el visor recién abierto).
            if ident not in self.analisis and self.raices:
                self.buscar()
            return self.analisis.get(ident)


def _con_analisis(raiz: Path, profundidad: int):
    if es_analisis(raiz):
        yield raiz
        return
    if profundidad <= 0:
        return
    try:
        hijos = [Path(e.path) for e in os.scandir(raiz) if e.is_dir()]
    except OSError:
        return
    for hijo in hijos:
        yield from _con_analisis(hijo, profundidad - 1)


def _fecha(a: Analisis) -> float:
    # Los que se están haciendo, los primeros.
    fechas = [_mtime(a.carpeta / "analisis.json"), _mtime(a.carpeta / CARPETA / ESTADO_DIRECTO)]
    return max((f for f in fechas if f is not None), default=0.0)


class Vida:
    """Si la ventana sigue abierta: la página late cada poco y se despide al cerrarse o recargarse."""

    def __init__(self) -> None:
        self.ultimo = time.monotonic()
        self.visto = False
        self.adios: float | None = None
        self._cerrojo = threading.Lock()

    def latido(self) -> None:
        with self._cerrojo:
            self.ultimo = time.monotonic()
            self.visto = True
            self.adios = None

    def despedida(self) -> None:
        with self._cerrojo:
            self.adios = time.monotonic()

    def acabada(self) -> bool:
        with self._cerrojo:
            ahora = time.monotonic()
            if self.adios is not None and ahora - self.adios > ESPERA_ADIOS_S:
                return True
            return self.visto and ahora - self.ultimo > SIN_LATIDO_S

    def reciente(self, segundos: float) -> bool:
        with self._cerrojo:
            return self.visto and time.monotonic() - self.ultimo <= segundos


class Servidor(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, direccion, biblioteca: Biblioteca) -> None:
        super().__init__(direccion, Manejador)
        self.biblioteca = biblioteca
        self.vida = Vida()

    def handle_error(self, request, client_address) -> None:
        # Al saltar en el vídeo el navegador corta las peticiones a medias: es lo normal y no hace falta contarlo.
        if isinstance(sys.exc_info()[1], (ConnectionAbortedError, ConnectionResetError, BrokenPipeError, TimeoutError)):
            return
        super().handle_error(request, client_address)


class Manejador(BaseHTTPRequestHandler):
    server: Servidor
    protocol_version = "HTTP/1.1"
    server_version = "OjeadorVisor"

    def log_message(self, formato: str, *args) -> None:
        pass

    def do_HEAD(self) -> None:
        self.do_GET()

    def do_GET(self) -> None:
        if not self._host_valido():
            return self._error(HTTPStatus.FORBIDDEN, "Solo desde este equipo.")
        url = urlparse(self.path)
        partes = [unquote(p) for p in url.path.split("/") if p]
        try:
            if not partes:
                self.server.vida.latido()
                return self._archivo(ESTATICO / "index.html", "text/html; charset=utf-8")
            if partes[0] == "estatico" and len(partes) == 2:
                return self._estatico(partes[1])
            if partes[:2] == ["api", "analisis"] and len(partes) == 2:
                return self._json([a.ficha() for a in self.server.biblioteca.buscar()])
            if partes[:2] == ["api", "inicio"]:
                return self._json({"analisis": self.server.biblioteca.inicial})
            if partes[0] == "api" and len(partes) >= 4 and partes[1] == "a":
                return self._de_analisis(partes[2], partes[3:], parse_qs(url.query))
        except (ConnectionAbortedError, ConnectionResetError, BrokenPipeError):
            return
        return self._error(HTTPStatus.NOT_FOUND, "No existe.")

    def do_POST(self) -> None:
        if not self._host_valido():
            return self._error(HTTPStatus.FORBIDDEN, "Solo desde este equipo.")
        url = urlparse(self.path)
        largo = int(self.headers.get("Content-Length") or 0)
        cuerpo = self.rfile.read(min(largo, 1 << 16)) if largo else b""
        if url.path == "/api/latido":
            self.server.vida.latido()
            return self._json({"ok": True})
        if url.path == "/api/adios":
            self.server.vida.despedida()
            return self._json({"ok": True})
        if url.path == "/api/abrir":
            try:
                carpeta = Path(str(json.loads(cuerpo.decode("utf-8") or "{}").get("carpeta") or "").strip().strip('"'))
            except ValueError:
                return self._error(HTTPStatus.BAD_REQUEST, "Petición no válida.")
            if not str(carpeta) or not es_analisis(absoluta(carpeta)):
                return self._error(HTTPStatus.NOT_FOUND, "En esa carpeta no hay ningún análisis (ni analisis.json ni uno en marcha).")
            return self._json(self.server.biblioteca.registrar(carpeta).ficha())
        partes = [p for p in url.path.split("/") if p]
        if len(partes) == 4 and partes[:2] == ["api", "a"] and partes[3] == "video":
            analisis = self.server.biblioteca.get(partes[2])
            if analisis is None:
                return self._error(HTTPStatus.NOT_FOUND, "Ese análisis no está abierto en el visor.")
            try:
                ruta = Path(str(json.loads(cuerpo.decode("utf-8") or "{}").get("ruta") or "").strip().strip('"'))
            except ValueError:
                return self._error(HTTPStatus.BAD_REQUEST, "Petición no válida.")
            if not str(ruta) or not absoluta(ruta).is_file():
                return self._error(HTTPStatus.NOT_FOUND, "No se encuentra ese archivo.")
            analisis.video = absoluta(ruta)
            return self._json(analisis.ficha())
        return self._error(HTTPStatus.NOT_FOUND, "No existe.")

    def _host_valido(self) -> bool:
        # Contra una web que apunte su nombre a 127.0.0.1 (DNS rebinding): solo las direcciones locales.
        host = (self.headers.get("Host") or "").rsplit(":", 1)[0].strip("[]").lower()
        if host not in ("127.0.0.1", "localhost"):
            return False
        # Y contra otra web abierta en el navegador que llame aquí: solo lo que pide la propia página del visor (o la
        # dirección escrita a mano).
        if self.headers.get("Sec-Fetch-Site", "same-origin") not in ("same-origin", "none"):
            return False
        origen = self.headers.get("Origin")
        if origen is None:
            return True
        partes = urlparse(origen)
        return partes.hostname in ("127.0.0.1", "localhost") and partes.port == self.server.server_port

    def _de_analisis(self, ident: str, resto: list[str], consulta: dict) -> None:
        analisis = self.server.biblioteca.get(ident)
        if analisis is None:
            return self._error(HTTPStatus.NOT_FOUND, "Ese análisis no está abierto en el visor.")
        que = resto[0]
        if que == "ficha" and len(resto) == 1:
            return self._json(analisis.ficha())
        if que in ARCHIVOS_JSON and len(resto) == 1:
            return self._archivo(analisis.carpeta / ARCHIVOS_JSON[que], "application/json; charset=utf-8")
        if que == "directo" and len(resto) == 1:
            try:
                desde = int(consulta.get("desde", ["0"])[0])
            except ValueError:
                return self._error(HTTPStatus.BAD_REQUEST, "Instante no válido.")
            return self._json(analisis.directo(desde))
        if que == "fotogramas" and len(resto) == 1:
            indice = analisis.indice()
            if indice is None:
                return self._error(HTTPStatus.NOT_FOUND, "Este análisis no tiene repetición: vuelve a analizar el vídeo con el Ojeador de ahora.")
            try:
                desde = float(consulta.get("desde", ["0"])[0])
                hasta = float(consulta.get("hasta", [str(desde + 30)])[0])
            except ValueError:
                return self._error(HTTPStatus.BAD_REQUEST, "Tramo no válido.")
            return self._bytes(indice.entre(desde, hasta), "application/json; charset=utf-8")
        if que == "video" and len(resto) == 1:
            ruta = analisis.ruta_video()
            if ruta is None or not ruta.exists():
                return self._error(HTTPStatus.NOT_FOUND, "No se encuentra el vídeo del análisis.")
            return self._archivo(ruta, rangos=True)
        if que == "recortes" and len(resto) == 2 and NOMBRE_RECORTE.match(resto[1]):
            return self._archivo(analisis.carpeta / "recortes" / resto[1])
        return self._error(HTTPStatus.NOT_FOUND, "No existe.")

    def _estatico(self, nombre: str) -> None:
        ruta = ESTATICO / nombre
        if "/" in nombre or "\\" in nombre or nombre.startswith(".") or not ruta.is_file():
            return self._error(HTTPStatus.NOT_FOUND, "No existe.")
        tipos = {".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8", ".svg": "image/svg+xml"}
        self._archivo(ruta, tipos.get(ruta.suffix.lower()))

    def _json(self, datos) -> None:
        self._bytes(json.dumps(datos, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8")

    def _bytes(self, cuerpo: bytes, tipo: str, estado: int = HTTPStatus.OK) -> None:
        self.send_response(estado)
        self.send_header("Content-Type", tipo)
        self.send_header("Content-Length", str(len(cuerpo)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(cuerpo)

    def _error(self, estado: int, mensaje: str) -> None:
        self._bytes(json.dumps({"error": mensaje}, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8", estado)

    def _archivo(self, ruta: Path, tipo: str | None = None, rangos: bool = False) -> None:
        """Un archivo entero o, con `rangos`, el trozo que pida la cabecera Range (el vídeo, para poder saltar)."""
        try:
            tamano = ruta.stat().st_size
        except OSError:
            return self._error(HTTPStatus.NOT_FOUND, "No se encuentra el archivo.")
        tipo = tipo or mimetypes.guess_type(ruta.name)[0] or "application/octet-stream"
        inicio, fin, estado = 0, tamano - 1, HTTPStatus.OK
        rango = self.headers.get("Range") if rangos else None
        if rango:
            pedido = re.match(r"^bytes=(\d*)-(\d*)$", rango.strip())
            if pedido and (pedido.group(1) or pedido.group(2)):
                a, b = pedido.groups()
                if not a:
                    inicio = max(0, tamano - int(b))
                else:
                    inicio = int(a)
                    fin = min(tamano - 1, int(b)) if b else tamano - 1
                if inicio >= tamano or inicio > fin:
                    self.send_response(HTTPStatus.REQUESTED_RANGE_NOT_SATISFIABLE)
                    self.send_header("Content-Range", f"bytes */{tamano}")
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
                estado = HTTPStatus.PARTIAL_CONTENT
        self.send_response(estado)
        self.send_header("Content-Type", tipo)
        self.send_header("Content-Length", str(fin - inicio + 1))
        if rangos:
            self.send_header("Accept-Ranges", "bytes")
        if estado == HTTPStatus.PARTIAL_CONTENT:
            self.send_header("Content-Range", f"bytes {inicio}-{fin}/{tamano}")
        if not rangos:
            self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command == "HEAD":
            return
        with ruta.open("rb") as f:
            f.seek(inicio)
            quedan = fin - inicio + 1
            while quedan > 0:
                trozo = f.read(min(BLOQUE, quedan))
                if not trozo:
                    break
                self.wfile.write(trozo)
                quedan -= len(trozo)


def navegador() -> str | None:
    """Edge (siempre está en Windows 11) o, si no, Chrome."""
    for nombre in ("msedge", "chrome"):
        encontrado = shutil.which(nombre)
        if encontrado:
            return encontrado
    for base in (os.environ.get("PROGRAMFILES(X86)"), os.environ.get("PROGRAMFILES"), os.environ.get("LOCALAPPDATA")):
        if not base:
            continue
        for relativo in (r"Microsoft\Edge\Application\msedge.exe", r"Google\Chrome\Application\chrome.exe"):
            ruta = Path(base) / relativo
            if ruta.exists():
                return str(ruta)
    return None


def abrir_ventana(url: str) -> subprocess.Popen | None:
    """La página en una ventana propia (modo aplicación, sin barra de direcciones), con un perfil aparte del del
    navegador de siempre."""
    ejecutable = navegador()
    if ejecutable is None:
        return None
    perfil = Path(os.environ.get("LOCALAPPDATA") or Path.home()) / "TercerTiempo" / "visor-ojeador"
    perfil.mkdir(parents=True, exist_ok=True)
    try:
        return subprocess.Popen(
            [ejecutable, f"--app={url}", f"--user-data-dir={perfil}", "--no-first-run", "--no-default-browser-check", "--window-size=1600,960"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
    except OSError:
        return None


def abrir_visor(carpetas: list[Path], video: Path | None = None, puerto: int = 0, ventana: bool = True) -> int:
    biblioteca = Biblioteca(carpetas, video)
    servidor = Servidor(("127.0.0.1", puerto), biblioteca)
    url = f"http://127.0.0.1:{servidor.server_port}/"
    hilo = threading.Thread(target=servidor.serve_forever, daemon=True)
    hilo.start()
    print(f"Visor del Ojeador en {url} (se cierra al cerrar la ventana; Ctrl+C para pararlo).", flush=True)
    proceso = abrir_ventana(url) if ventana else None
    if ventana and proceso is None:
        webbrowser.open(url)
    try:
        while True:
            time.sleep(0.5)
            if servidor.vida.acabada():
                break
            # Si la ventana se cierra sin despedirse, el proceso del navegador acaba. Si acaba enseguida es que le pasó
            # la página a una ventana que ya estaba abierta: se sigue mientras esa página lata.
            if proceso is not None and proceso.poll() is not None and not servidor.vida.reciente(60.0) and time.monotonic() - servidor.vida.ultimo > 15.0:
                break
    except KeyboardInterrupt:
        pass
    finally:
        servidor.shutdown()
        servidor.server_close()
        if proceso is not None and proceso.poll() is None:
            proceso.terminate()
    return 0
