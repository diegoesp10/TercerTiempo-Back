"""Rehacer sin la GPU lo que va tras la pasada (`tt-analisis rehacer-jugadas`).

La pasada (detectar, seguir, calibrar, leer dorsales y aspecto) es lo que tarda: una hora larga por partido. Lo de
después tarda segundos y es donde se interpreta lo visto: la trayectoria del balón, los porteros, quién es quién, las
jugadas y las acciones, y las salidas (`analizador._jugadas_y_salidas`). Con `--guardar-pasada` (o con la variable de
entorno TT_GUARDAR_PASADA), el análisis guarda al acabar la pasada todo lo que entra en esa capa (`EstadoPasada`, en
ARCHIVO), y `rehacer-jugadas` la repite con el código de ahora en otra carpeta. Así un cambio de lógica se mide contra
las etiquetas del visor (`evaluar_eventos`) en segundos, sin repetir la pasada.

Con el mismo código, lo rehecho es igual que el análisis: solo cambian los tiempos (`tests/test_rehacer_jugadas.py`).
Sin clips, sin modelo de lenguaje y sin el material del lector (`--guardar-dorsales`), que piden el vídeo o la GPU.

Se guarda con pickle, comprimido. Al leerlo solo se admiten las clases que guarda la pasada (`_PERMITIDAS`): un archivo
cambiado a propósito no puede ejecutar nada. Aun así, es para carpetas propias."""

from __future__ import annotations

import gzip
import json
import pickle
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from .config import Configuracion
    from .dorsales import HistorialLecturas
    from .geometria import Campo
    from .seguimiento import Seguidor
    from .tipos import Instante
    from .video import InfoVideo

ARCHIVO = "pasada.pkl.gz"
# Sube si cambia lo que se guarda: una pasada guardada con otro formato no se lee y hay que repetirla.
FORMATO = 1
# Comprimir más apenas ahorra (casi todo son números y recortes) y tarda bastante más.
NIVEL_GZIP = 3
_RUTAS = {"Path", "PurePath", "WindowsPath", "PosixPath", "PureWindowsPath", "PurePosixPath"}
# Lo único que puede salir de ARCHIVO: lo que guarda la pasada y lo que necesitan numpy y Python para rehacerlo.
_PERMITIDAS = {
    "builtins": {"int", "float", "complex", "str", "bytes", "list", "tuple", "dict", "set", "frozenset"},
    "collections": {"defaultdict", "OrderedDict"},
    "pathlib": _RUTAS,
    "pathlib._local": _RUTAS,
    "numpy": {"ndarray", "dtype"},
    "numpy._core.multiarray": {"_reconstruct", "scalar"},
    "numpy.core.multiarray": {"_reconstruct", "scalar"},
    "numpy._core.numeric": {"_frombuffer"},
    "numpy.core.numeric": {"_frombuffer"},
    "tt_analisis.rehacer": {"EstadoPasada"},
    "tt_analisis.config": {"Configuracion", "Segmento"},
    "tt_analisis.video": {"InfoVideo"},
    "tt_analisis.geometria": {"Campo"},
    "tt_analisis.tipos": {"Instante", "Persona"},
    "tt_analisis.campo": {"Homografia"},
    "tt_analisis.seguimiento": {"Seguidor", "MemoriaTracklet", "Recorte"},
    "tt_analisis.dorsales": {"HistorialLecturas"},
}


class SinPasada(FileNotFoundError):
    pass


@dataclass
class EstadoPasada:
    """Lo que entra en la capa de jugadas: cómo se analizó, lo que se vio en cada instante (antes de completar el balón y
    de colocar a los porteros, que lo cambian), cada tracklet con sus votos, recortes y lecturas, y lo que la pasada contó
    por el camino para el análisis (`pasada`, `identificacion`, `parametros`, `rendimiento`)."""

    config: Configuracion
    info: InfoVideo
    fin_s: float
    dt: float
    fps_real: float
    vecinos: int
    campo: Campo
    analizador: dict
    datos_acta: dict | None
    instantes: list[Instante]
    # El balón visto en los fotogramas vecinos: [(t, centro en px, confianza)].
    pista: list
    seguidor: Seguidor
    historial: HistorialLecturas | None
    apariencias: dict | None
    # Lecturas del dorsal y recortes del aspecto. None sin lector de dorsales (`--sin-dorsales`): nadie se identifica.
    identificacion: dict | None
    colores: list
    reparto: Any
    muestras: int
    calibracion: dict
    pasada: dict
    parametros: dict
    rendimiento: dict
    tiempos: dict
    anotado: bool
    # Segundos desde que empezó el análisis hasta el final de la pasada.
    antes_s: float
    formato: int = FORMATO


def guardar(estado: EstadoPasada, carpeta: Path) -> Path:
    """En `carpeta`/ARCHIVO, sin la hora en la cabecera del gzip: la misma pasada da el mismo archivo."""
    ruta = carpeta / ARCHIVO
    with ruta.open("wb") as crudo, gzip.GzipFile(fileobj=crudo, mode="wb", compresslevel=NIVEL_GZIP, mtime=0) as archivo:
        pickle.dump(estado, archivo, protocol=pickle.HIGHEST_PROTOCOL)
    return ruta


class _Lector(pickle.Unpickler):
    def find_class(self, modulo: str, nombre: str):
        if nombre in _PERMITIDAS.get(modulo, ()):
            return super().find_class(modulo, nombre)
        raise pickle.UnpicklingError(f"lleva {modulo}.{nombre}, que no es de un análisis")


def leer(carpeta: Path) -> EstadoPasada:
    ruta = carpeta / ARCHIVO
    if not ruta.is_file():
        raise SinPasada(f"En {carpeta} no está lo que entra en la capa de jugadas ({ARCHIVO}): hay que analizar con --guardar-pasada.")
    try:
        with gzip.open(ruta, "rb") as archivo:
            estado = _Lector(archivo).load()
    except pickle.UnpicklingError as error:
        raise ValueError(f"{ruta} no se lee: {error}.") from error
    except (AttributeError, ImportError) as error:
        raise ValueError(f"{ruta} se guardó con otro código ({error}): repite el análisis con --guardar-pasada.") from error
    except (EOFError, OSError) as error:
        raise ValueError(f"{ruta} está incompleto o dañado ({error}).") from error
    if not isinstance(estado, EstadoPasada):
        raise ValueError(f"{ruta} no es lo que guarda una pasada.")
    if getattr(estado, "formato", None) != FORMATO:
        raise ValueError(f"{ruta} es del formato {getattr(estado, 'formato', '?')} y este analizador lee el {FORMATO}: repite el análisis con --guardar-pasada.")
    return estado


def salida_por_defecto(origen: Path) -> Path:
    return Path("salidas") / f"{origen.resolve().name}-rehecho"


def es_rehecho(carpeta: Path) -> bool:
    try:
        return "rehecho" in json.loads((carpeta / "analisis.json").read_text(encoding="utf-8"))
    except (OSError, ValueError, TypeError):
        return False


def preparar_salida(origen: Path, salida: Path) -> None:
    """Deja `salida` vacía para el análisis rehecho. Solo sustituye lo que ya era un análisis rehecho (y nada más): nunca
    la carpeta de la pasada, una que la contenga o esté dentro de ella, ni una con otras cosas."""
    from .visor.servidor import CONTENIDO_DE_UN_ANALISIS

    origen, salida = origen.resolve(), salida.resolve()
    if salida == origen or origen in salida.parents or salida in origen.parents:
        raise ValueError("La salida tiene que ser una carpeta aparte de la del análisis.")
    if salida.exists():
        if not salida.is_dir():
            raise ValueError(f"{salida} no es una carpeta.")
        nombres = {p.name.lower() for p in salida.iterdir()}
        if nombres:
            if not es_rehecho(salida) or not nombres <= CONTENIDO_DE_UN_ANALISIS:
                raise ValueError(f"{salida} ya tiene otras cosas: elige otra con --salida.")
            shutil.rmtree(salida)
    salida.mkdir(parents=True, exist_ok=True)


def medir(carpeta: Path, revision: Path | None = None, margen: float | None = None, ruta_revisiones: Path | None = None, copias: Path | None = None) -> str:
    """La tabla de `evaluar_eventos` del análisis de `carpeta` con las etiquetas de `revision` (un JSON exportado del
    visor) o, sin ella, con las del visor para ese vídeo (sin crear ni tocar nada). Si no se puede medir, dice por qué."""
    from .evaluar_eventos import evaluar, leer_pasada, tabla
    from .revision_eventos import buscar_revision, fuente_revision, huella_video

    pasada = leer_pasada(carpeta)
    try:
        video = huella_video(Path(pasada["video"]["ruta"]))
    except OSError:
        return f"Sin medir: no se encuentra el vídeo ({pasada['video']['ruta']})."
    if revision is not None:
        etiquetas = json.loads(revision.read_text(encoding="utf-8"))
        if etiquetas.get("video") != video:
            return "Sin medir: esas etiquetas son de otro archivo de vídeo."
    else:
        etiquetas = buscar_revision(video, ruta_revisiones, copias)
        if etiquetas is None:
            return "Sin medir: no hay etiquetas del visor para este vídeo."
    if not etiquetas.get("tramos"):
        return "Sin medir: las etiquetas de este vídeo no tienen ningún tramo revisado."
    return tabla(evaluar(etiquetas, fuente_revision(pasada), margen))
