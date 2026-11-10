"""Etiquetado automático de un partido propio para afinar los detectores (pseudoetiquetas).

Los detectores públicos están entrenados con televisión; para que aprendan nuestros campos (focos, césped, cámara)
hacen falta imágenes nuestras etiquetadas. En vez de etiquetar a mano, se usan los propios detectores como «maestro»
en condiciones mucho más favorables que las del análisis, y se guarda solo lo que se confirma:

- **Campo**: primero se miden el largo y el ancho reales del campo (no todos miden 105 × 68; las áreas y el círculo sí
  son siempre iguales) buscando las medidas que hacen coherentes los puntos detectados en muchos fotogramas. Luego, con
  6 o más puntos coherentes se ajusta la homografía y se reproyectan los 32 vértices: así quedan etiquetados también
  los que el detector no vio, que es justo lo que queremos que aprenda.
- **Balón**: se busca por teselas (el balón pequeño del fondo) y a fotograma entero en el fotograma y en cuatro
  vecinos (±0,2 y ±0,4 s). Lo que distingue un balón de una marca de la línea o un reflejo es que **se mueve respecto
  al césped**: se estima el movimiento de la cámara entre el fotograma y cada vecino (flujo óptico sobre el fondo) y se
  mira si el candidato sigue en el mismo sitio del campo (marca: se rechaza, y queda como negativo difícil) o se ha
  desplazado (balón en juego: se confirma). Un candidato con mucha confianza que no se demuestra quieto también vale.
  Se descartan cabezas, balones fuera del campo y fotogramas con dos balones igual de buenos. Opcionalmente, un **juez**
  con visión (Claude por API) revisa un recorte ampliado de cada candidato que queda; el modelo local probado
  (gemma4:12b vía Ollama) es lento y demasiado permisivo, así que no es el valor por defecto.
- **Personas**: las del detector de jugadores a confianza alta. Un fotograma entra en este conjunto solo si el balón
  está resuelto (para no enseñarle que «ahí no hay balón» cuando sí lo hay).

Sale un dataset en formato YOLO (`balon/`, `personas/`, `campo/`, cada uno con `data.yaml`), partido en entrenamiento y
validación por bloques de tiempo (no por fotogramas sueltos, que serían casi iguales), y una carpeta `revision/` con
vistas previas para echar un vistazo. Las pseudoetiquetas se pueden corregir a mano en cualquier herramienta que lea
YOLO (CVAT, Label Studio) antes de entrenar."""

from __future__ import annotations

import json
import shutil
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterator

import cv2
import numpy as np
import supervision as sv

from .campo import CONFIG_CAMPO, VERTICES, Homografia, configurar_campo, medir_dimensiones
from .config import CLASE_BALON, CLASE_JUGADOR, MARGEN_CAMPO_CM, Segmento
from .geometria import Campo
from .modelos import Modelos
from .video import InfoVideo, a_jpeg, fotogramas_en, info_video

NOMBRES_PERSONAS = ["ball", "goalkeeper", "player", "referee"]
CONF_CANDIDATO = 0.12
CONF_BALON_SOLO = 0.6
CONF_BALON_CON_VECINO = 0.3
CONF_BALON_DOS_VECINOS = 0.2
RADIO_VECINO_PX = 150.0
# Vecinos del fotograma central, en fotogramas del vídeo (a 50 fps: ±0,1, ±0,2 y ±0,4 s). Los de ±0,1 s están para los
# balones rápidos (un pase largo o un tiro recorre 100-300 px en 0,2 s y se salía del radio de búsqueda).
VECINOS = (-20, -10, -5, 5, 10, 20)
# Radio en el que se busca el balón en cada vecino: crece con el desfase (un balón a 750 px/s se mueve 75 px en 0,1 s
# y 300 en 0,4 s), con un mínimo para el temblor de la detección.
RADIO_VECINO_MIN_PX = 60.0


def radio_vecino(desfase: int, base: float = RADIO_VECINO_PX) -> float:
    return max(RADIO_VECINO_MIN_PX, base * abs(desfase) / 10)
# Desplazamiento respecto al césped (compensada la cámara) por debajo del cual el candidato está quieto, y a partir del
# cual se mueve; en píxeles a media resolución.
QUIETO_PX = 4.0
MOVIL_PX = 10.0
MOVIL_PX_UN_VECINO = 16.0
CONF_QUIETO_NEGATIVO = 0.4
# Sobre una persona, el balón solo vale si el detector de jugadores también lo ve a esta distancia; parado, además con
# esta confianza (balón parado a los pies).
RADIO_OTRO_PX = 40.0
CONF_QUIETO_A_LOS_PIES = 0.6
# Quieto en el césped (sin nadie encima) vale como balón parado solo con esta confianza; las marcas no la alcanzan.
CONF_QUIETO_SOLO = 0.75
BALON_MIN_PX = 4
BALON_MAX_PX = 80
CANDIDATOS_POR_FOTOGRAMA = 3
CONF_PERSONA = 0.45
CONF_PUNTO = 0.5
MIN_PUNTOS_CAMPO = 6
ERROR_MAX_CAMPO_PX = 10.0
BLOQUE_S = 120.0
BLOQUES_POR_CICLO = 5
TESELA = 640
SOLAPE = 64
IMGSZ_CAMPO = 640
RECORTE_JUEZ_PX = 160
# Negativos difíciles (candidatos que el juez rechaza) como mucho esta fracción de los positivos.
FRACCION_NEGATIVOS = 0.5
# Error medio de reproyección por encima del cual la medida automática del campo no vale.
ERROR_MEDIDA_FIABLE_PX = 12.0

Caja = tuple[float, float, float, float, float]

INSTRUCCIONES_JUEZ = (
    "Recorte ampliado de un vídeo de un partido de fútbol grabado de noche con focos. Un detector automático cree que hay "
    "un balón justo en el centro, dentro del círculo amarillo, pero la mayoría de estos candidatos son falsos. Describe "
    "primero qué hay exactamente en el centro del círculo y luego decide si es un balón de fútbol (una pelota redonda, "
    "aunque se vea pequeña o movida). No son balón: una marca, cruce o trozo de las líneas blancas del campo, una cabeza, "
    "una media o bota blanca, un foco o reflejo, un trozo de cartel o de la red, césped sin nada. Si no ves claramente una "
    "pelota, di que no. Responde solo con el JSON pedido."
)
ESQUEMA_JUEZ = {
    "type": "object",
    "properties": {"que_es": {"type": "string"}, "balon": {"type": "boolean"}, "confianza": {"type": "number"}},
    "required": ["que_es", "balon", "confianza"],
    "additionalProperties": False,
}


@dataclass
class ConfiguracionEtiquetado:
    video: Path
    salida: Path
    modelos: Path = Path("modelos")
    device: str = "cuda"
    # Segundos entre fotogramas etiquetados.
    cada_s: float = 2.0
    segmento: Segmento = field(default_factory=Segmento)
    imgsz: int = 1280
    # Medidas del campo en metros; si no se dan, se miden desde el vídeo.
    campo: tuple[float, float] | None = None
    # Usar como maestro los pesos afinados (modelos/afinados) si existen: segunda ronda de autoentrenamiento.
    afinados: bool = False
    # Quién propone los candidatos a balón: "publico", "afinado" o "ambos" (la unión de los dos: más candidatos, que
    # la regla de movimiento filtra igual; el afinado solo mejora la precisión, el público ve más balones pequeños).
    maestro_balon: str = "publico"
    # Juez de los candidatos a balón: "claude", "ollama" o "none" (solo la regla rápida).
    juez: str = "none"
    modelo_juez: str | None = None
    # Una vista previa anotada de cada N fotogramas etiquetados.
    vista_previa_cada: int = 10
    # Para probar: solo las primeras N muestras.
    max_muestras: int | None = None


@dataclass
class Candidato:
    caja: Caja
    puntuacion: float
    # Confirmado por la regla rápida (movimiento, confianza, otro detector).
    heuristico: bool
    # "movil", "quieto" o "indeterminado" respecto al césped, y el desplazamiento máximo visto (px a media resolución).
    movimiento: str = "indeterminado"
    desplazamiento_px: float = 0.0
    vecinos: int = 0
    recorte: bytes | None = None
    veredicto: dict | None = None


@dataclass
class ResultadoBalon:
    caja: Caja | None
    candidatos: int
    ambiguo: bool = False


# ---- Lógica pura (se prueba sin GPU) ----


def particion_de(t: float, bloque_s: float = BLOQUE_S, bloques_por_ciclo: int = BLOQUES_POR_CICLO) -> str:
    """Validación un bloque de cada `bloques_por_ciclo` (el último), para que cubra todo el partido."""
    return "val" if int(t // bloque_s) % bloques_por_ciclo == bloques_por_ciclo - 1 else "train"


def indices_espejo() -> list[int]:
    """Para cada vértice del campo, el simétrico respecto al centro (x → largo − x): lo necesita el volteo horizontal
    al entrenar el modelo de pose."""
    espejo = np.stack([CONFIG_CAMPO.length - VERTICES[:, 0], VERTICES[:, 1]], axis=1)
    indices = []
    for p in espejo:
        d = np.linalg.norm(VERTICES - p, axis=1)
        if d.min() > 1.0:
            raise ValueError("Un vértice del campo no tiene simétrico")
        indices.append(int(np.argmin(d)))
    return indices


def _centro(caja) -> np.ndarray:
    return np.array([(caja[0] + caja[2]) / 2, (caja[1] + caja[3]) / 2])


def _lado(caja) -> float:
    return float(max(caja[2] - caja[0], caja[3] - caja[1]))


def _en_cabeza(centro: np.ndarray, personas: np.ndarray) -> bool:
    for x1, y1, x2, y2 in personas:
        if x1 <= centro[0] <= x2 and y1 <= centro[1] <= y1 + 0.3 * (y2 - y1):
            return True
    return False


def movimiento_camara(centro_gris: np.ndarray, vecino_gris: np.ndarray) -> np.ndarray | None:
    """Transformación afín (giro, escala y desplazamiento) del fotograma central al vecino, estimada con el flujo
    óptico de puntos del fondo (líneas, vallas, carteles); los jugadores que se mueven quedan fuera por RANSAC.
    Las dos imágenes van en gris y a media resolución. None si no hay fondo suficiente."""
    puntos = cv2.goodFeaturesToTrack(centro_gris, maxCorners=500, qualityLevel=0.01, minDistance=10, blockSize=7)
    if puntos is None or len(puntos) < 30:
        return None
    siguientes, estado, _ = cv2.calcOpticalFlowPyrLK(centro_gris, vecino_gris, puntos, None, winSize=(21, 21), maxLevel=3)
    ok = estado.ravel() == 1
    if ok.sum() < 30:
        return None
    m, inliers = cv2.estimateAffinePartial2D(puntos[ok], siguientes[ok], method=cv2.RANSAC, ransacReprojThreshold=2.5)
    if m is None or inliers is None or inliers.sum() < 20:
        return None
    return m


def clasificar_movimiento(caja: Caja, vecinos: list[list[Caja]], afines: list[np.ndarray | None], radio: float = RADIO_VECINO_PX, otros_centro: list[Caja] = (), personas_vecinos: list[np.ndarray] | None = None, desfases: tuple[int, ...] = VECINOS) -> tuple[str, float, int]:
    """Si el candidato está quieto o se mueve respecto al césped: se lleva su posición a cada vecino con el movimiento
    de la cámara y se mira dónde apareció allí el balón más cercano, sin contar lo que corresponde a otro candidato del
    fotograma central que no se ha movido (otra marca) ni lo que está sobre una persona del vecino (la media de un
    jugador que pasa por ahí). `desfases` dice a cuántos fotogramas del central está cada vecino, y con ellos crece el
    radio de búsqueda. Devuelve (movimiento, desplazamiento máximo en px a media resolución, vecinos en los que se
    encontró)."""
    xy = _centro(caja) / 2
    lado = _lado(caja)
    # Por cada vecino donde se encuentra: (desplazamiento respecto al césped, velocidad en px por fotograma).
    hallazgos: list[tuple[float, np.ndarray]] = []
    for k, (cajas_vecino, afin, desfase) in enumerate(zip(vecinos, afines, desfases)):
        if afin is None:
            continue
        radio_k = radio_vecino(desfase, radio)
        esperado = afin[:, :2] @ xy + afin[:, 2]
        otros_esperados = [afin[:, :2] @ (_centro(o) / 2) + afin[:, 2] for o in otros_centro]
        personas = personas_vecinos[k] if personas_vecinos is not None and k < len(personas_vecinos) else None
        mejor = None
        for o in cajas_vecino:
            if not (0.5 <= _lado(o) / max(lado, 1) <= 2.0):
                continue
            if personas is not None and len(personas) and _en_persona(_centro(o), personas):
                continue
            p = _centro(o) / 2
            if any(np.linalg.norm(p - e) <= QUIETO_PX for e in otros_esperados):
                continue
            d = float(np.linalg.norm(p - esperado))
            if d <= radio_k and (mejor is None or d < mejor[0]):
                mejor = (d, (p - esperado) / desfase)
        if mejor is not None:
            hallazgos.append(mejor)
    encontrados = len(hallazgos)
    maximo = max((d for d, _ in hallazgos), default=0.0)
    if encontrados == 0:
        return "indeterminado", 0.0, 0
    if encontrados == 1:
        # Un balón rápido (en el aire, un pase largo) solo se encuentra en el vecino más cercano: con un desplazamiento
        # claro vale; si está donde lo dejó la cámara, es algo quieto.
        if maximo >= MOVIL_PX_UN_VECINO:
            return "movil", maximo, 1
        return ("quieto" if maximo <= QUIETO_PX else "indeterminado"), maximo, 1
    if maximo < MOVIL_PX:
        # Encontrado en varios vecinos sin moverse de verdad: una marca, un reflejo, un balón parado.
        return "quieto", maximo, encontrados
    # Para ser un balón que se mueve, la velocidad vista desde cada vecino tiene que ser la misma: si en un vecino
    # «se movió» 100 px y en otro nada, lo que se encontró en uno de los dos era otra cosa.
    velocidades = np.array([v for _, v in hallazgos])
    media = velocidades.mean(axis=0)
    coherente = all(np.linalg.norm(v - media) <= 0.5 * np.linalg.norm(media) + 1.0 for v in velocidades)
    return ("movil" if coherente else "indeterminado"), maximo, encontrados


def _en_persona(centro: np.ndarray, personas: np.ndarray, margen: float = 6.0) -> bool:
    for x1, y1, x2, y2 in personas:
        if x1 - margen <= centro[0] <= x2 + margen and y1 - margen <= centro[1] <= y2 + margen:
            return True
    return False


def puntuar_candidatos(centro: list[Caja], vecinos: list[list[Caja]], otros: list[Caja], personas: np.ndarray, afines: list[np.ndarray | None] | None = None, radio: float = RADIO_VECINO_PX, personas_vecinos: list[np.ndarray] | None = None, desfases: tuple[int, ...] = VECINOS) -> list[Candidato]:
    """Los candidatos a balón del fotograma central, de mejor a peor, con la regla rápida aplicada a cada uno.
    `centro` y `vecinos` son los candidatos del detector de balón (fotograma central y vecinos), `otros` los balones
    que ve el detector de jugadores en el central, `afines` el movimiento de la cámara hacia cada vecino (si se tiene)
    y `personas_vecinos` las personas de cada vecino (para no confundir una media que pasa con el balón que se mueve)."""
    salida = []
    plausibles = [c for c in centro if BALON_MIN_PX <= _lado(c) <= BALON_MAX_PX and not _en_cabeza(_centro(c), personas)]
    radios = [radio_vecino(d, radio) for d in desfases]
    for c in plausibles:
        xy = _centro(c)
        conf = c[4]
        n_vecinos = sum(1 for v, r in zip(vecinos, radios) if any(np.linalg.norm(_centro(o) - xy) <= r and 0.5 <= _lado(o) / max(_lado(c), 1) <= 2.0 for o in v))
        otro = any(np.linalg.norm(_centro(o) - xy) <= RADIO_OTRO_PX for o in otros)
        sobre_persona = _en_persona(xy, personas)
        if afines:
            movimiento, desplazamiento, encontrados = clasificar_movimiento(
                c, vecinos, afines, radio, otros_centro=[o for o in plausibles if o is not c], personas_vecinos=None if sobre_persona else personas_vecinos, desfases=desfases
            )
        else:
            movimiento, desplazamiento, encontrados = "indeterminado", 0.0, 0
        if sobre_persona:
            # Sobre una persona, el detector de balón confunde medias, botas y pantalones, y se mueven con ella: solo
            # vale si el detector de jugadores (otros datos) también ve ahí un balón; parado, además con confianza.
            confirmado = otro and (movimiento != "quieto" or conf >= CONF_QUIETO_A_LOS_PIES)
        elif movimiento == "quieto":
            # Quieto respecto al césped suele ser una marca; con mucha confianza es un balón parado (saque, falta).
            confirmado = conf >= CONF_QUIETO_SOLO
        elif movimiento == "movil":
            confirmado = encontrados >= 2 or conf >= CONF_BALON_CON_VECINO
        elif afines:
            # Con la cámara compensada y sin rastro coherente en los vecinos, solo vale la confianza alta.
            confirmado = conf >= CONF_BALON_SOLO
        else:
            confirmado = conf >= CONF_BALON_SOLO or (n_vecinos >= 1 and (conf >= CONF_BALON_CON_VECINO or otro)) or (n_vecinos >= 2 and conf >= CONF_BALON_DOS_VECINOS)
        puntuacion = conf + 0.3 * n_vecinos + 0.3 * otro + (0.5 if movimiento == "movil" else -1.0 if movimiento == "quieto" and not confirmado else 0.0)
        salida.append(Candidato(c, puntuacion, confirmado, movimiento, desplazamiento, max(n_vecinos, encontrados)))
    salida.sort(key=lambda c: -c.puntuacion)
    return salida[:CANDIDATOS_POR_FOTOGRAMA]


def elegir(candidatos: list[Candidato], aprobados: list[Candidato], radio: float = RADIO_VECINO_PX, parecido=None) -> ResultadoBalon:
    """El mejor de los aprobados (el primero), salvo que otro aprobado igual de bueno esté en otra parte de la imagen.
    `parecido(mejor, otro)` dice si el otro es igual de bueno; por defecto, por la puntuación de la regla rápida."""
    if not aprobados:
        return ResultadoBalon(None, len(candidatos))
    parecido = parecido or (lambda mejor, otro: otro.puntuacion >= mejor.puntuacion / 1.5)
    mejor = aprobados[0]
    for otro in aprobados[1:]:
        if np.linalg.norm(_centro(otro.caja) - _centro(mejor.caja)) > radio and parecido(mejor, otro):
            return ResultadoBalon(None, len(candidatos), ambiguo=True)
    return ResultadoBalon(mejor.caja, len(candidatos))


def confirmar_balon(centro: list[Caja], vecinos: list[list[Caja]], otros: list[Caja], personas: np.ndarray, radio: float = RADIO_VECINO_PX, desfases: tuple[int, ...] = VECINOS) -> ResultadoBalon:
    """Solo con la regla rápida (sin juez)."""
    candidatos = puntuar_candidatos(centro, vecinos, otros, personas, None, radio, desfases=desfases)
    return elegir(candidatos, [c for c in candidatos if c.heuristico], radio)


def resolver_con_juez(candidatos: list[Candidato], radio: float = RADIO_VECINO_PX) -> tuple[ResultadoBalon, str]:
    """Con los veredictos del juez: ('aprobado' | 'rechazado' | 'ambiguo' | 'sin_candidatos' | 'sin_juez')."""
    if not candidatos:
        return ResultadoBalon(None, 0), "sin_candidatos"
    juzgados = [c for c in candidatos if c.veredicto is not None and "error" not in c.veredicto]
    if not juzgados:
        resultado = elegir(candidatos, [c for c in candidatos if c.heuristico], radio)
        return resultado, "sin_juez"
    seguridad = lambda c: float(c.veredicto.get("confianza", 1.0))  # noqa: E731
    aprobados = [c for c in juzgados if c.veredicto.get("balon") and seguridad(c) >= 0.5]
    aprobados.sort(key=lambda c: -(seguridad(c) + 0.5 * c.puntuacion))
    # Dos balones aprobados en sitios distintos con una seguridad parecida: no se sabe cuál es el bueno.
    resultado = elegir(candidatos, aprobados, radio, parecido=lambda mejor, otro: seguridad(otro) >= seguridad(mejor) - 0.25)
    if resultado.caja is not None:
        return resultado, "aprobado"
    if resultado.ambiguo:
        return resultado, "ambiguo"
    return resultado, "rechazado" if len(juzgados) == len(candidatos) else "sin_juez"


@dataclass
class EtiquetaCampo:
    caja: tuple[float, float, float, float]
    puntos: np.ndarray  # (32, 3): x, y en píxeles y visibilidad (2 visible, 0 fuera de la imagen)
    homografia: Homografia
    error_px: float
    puntos_vistos: int


def etiqueta_campo(xy: np.ndarray, conf: np.ndarray, ancho: int, alto: int, reproyectar: bool = True) -> EtiquetaCampo | None:
    """De los puntos que ve el detector, los coherentes entre sí → homografía → los 32 vértices reproyectados.
    Sin `reproyectar` (medidas del campo desconocidas) se etiquetan solo los puntos vistos y coherentes; el resto queda
    sin etiqueta (visibilidad 0), que el entrenamiento ignora en vez de aprender que «no están»."""
    vistos = (conf >= CONF_PUNTO) & (xy[:, 0] > 1) & (xy[:, 1] > 1) & (xy[:, 0] < ancho - 1) & (xy[:, 1] < alto - 1)
    if vistos.sum() < MIN_PUNTOS_CAMPO:
        return None
    indices = np.flatnonzero(vistos)
    origen, destino = xy[indices].astype(np.float32), VERTICES[indices]
    try:
        h = Homografia(origen, destino)
    except (ValueError, np.linalg.LinAlgError):
        return None
    errores = np.linalg.norm(h.a_imagen(destino) - origen, axis=1)
    coherentes = errores <= ERROR_MAX_CAMPO_PX
    if coherentes.sum() < MIN_PUNTOS_CAMPO:
        return None
    origen, destino = origen[coherentes], destino[coherentes]
    extension = origen.max(axis=0) - origen.min(axis=0)
    if extension[0] < 0.15 * ancho or extension[1] < 0.08 * alto:
        return None
    try:
        h = Homografia(origen, destino)
    except (ValueError, np.linalg.LinAlgError):
        return None
    error = h.error_px(origen, destino)
    if not np.isfinite(error) or error > ERROR_MAX_CAMPO_PX:
        return None
    centro_campo = h.a_campo(np.array([[ancho / 2, alto / 2]], dtype=np.float32))[0]
    if not Campo().dentro((float(centro_campo[0]), float(centro_campo[1])), margen=2000):
        return None
    puntos = np.zeros((len(VERTICES), 3), dtype=np.float32)
    if reproyectar:
        proyectados = h.a_imagen(VERTICES)
        for i, (x, y) in enumerate(proyectados):
            if np.isfinite(x) and np.isfinite(y) and 0 <= x < ancho and 0 <= y < alto:
                puntos[i] = (x, y, 2)
    else:
        for i, (x, y) in zip(indices[coherentes], origen):
            puntos[i] = (x, y, 2)
    visibles = puntos[puntos[:, 2] > 0, :2]
    if len(visibles) < 4:
        return None
    x1, y1 = visibles.min(axis=0)
    x2, y2 = visibles.max(axis=0)
    return EtiquetaCampo((float(x1), float(y1), float(x2), float(y2)), puntos, h, float(error), int(coherentes.sum()))


def linea_yolo(clase: int, caja, ancho: int, alto: int, puntos: np.ndarray | None = None) -> str:
    """Una línea de etiqueta YOLO (normalizada), con los puntos clave si es un modelo de pose."""
    x1, y1, x2, y2 = (float(v) for v in caja[:4])
    x1, x2 = max(0.0, x1), min(float(ancho), x2)
    y1, y2 = max(0.0, y1), min(float(alto), y2)
    partes = [str(clase), f"{(x1 + x2) / 2 / ancho:.6f}", f"{(y1 + y2) / 2 / alto:.6f}", f"{(x2 - x1) / ancho:.6f}", f"{(y2 - y1) / alto:.6f}"]
    if puntos is not None:
        for x, y, v in puntos:
            partes += [f"{x / ancho:.6f}" if v else "0", f"{y / alto:.6f}" if v else "0", str(int(v))]
    return " ".join(partes)


def teselas(ancho: int, alto: int, lado: int = TESELA, solape: int = SOLAPE) -> list[tuple[int, int]]:
    """Esquinas superiores izquierdas de las teselas que cubren la imagen (la última se pega al borde)."""
    paso = lado - solape

    def ejes(total: int) -> list[int]:
        if total <= lado:
            return [0]
        posiciones = list(range(0, total - lado, paso))
        posiciones.append(total - lado)
        return sorted(set(posiciones))

    return [(x, y) for y in ejes(alto) for x in ejes(ancho)]


def recorte_juez(imagen: np.ndarray, caja: Caja, lado: int = RECORTE_JUEZ_PX) -> bytes:
    """Recorte cuadrado alrededor del candidato, ampliado al doble y con un círculo amarillo que lo señala."""
    cx, cy = (int(v) for v in _centro(caja))
    medio = lado // 2
    alto, ancho = imagen.shape[:2]
    con_borde = cv2.copyMakeBorder(imagen, medio, medio, medio, medio, cv2.BORDER_CONSTANT, value=(0, 0, 0))
    recorte = con_borde[cy:cy + lado, cx:cx + lado]
    recorte = cv2.resize(recorte, (lado * 2, lado * 2), interpolation=cv2.INTER_CUBIC)
    radio = int(max(_lado(caja), 10) + 12)
    cv2.circle(recorte, (lado, lado), radio, (0, 255, 255), 2)
    return a_jpeg(recorte, calidad=90)


# ---- Con los modelos ----


@dataclass
class Muestra:
    indice: int
    t: float
    imagen: np.ndarray
    vecinos: list[np.ndarray]


def _muestras(info: InfoVideo, config: ConfiguracionEtiquetado) -> Iterator[Muestra]:
    """Lectura secuencial (más rápida que ir saltando): cada `cada_s` segundos el fotograma y sus vecinos."""
    paso = max(1, round(config.cada_s * info.fps))
    # Los vecinos están definidos a 50 fps; a otra cadencia se escalan para que sigan siendo ±0,2 y ±0,4 s.
    desplazamientos = tuple(max(1, round(abs(v) * info.fps / 50)) * (1 if v > 0 else -1) for v in VECINOS)
    margen = max(abs(d) for d in desplazamientos)
    inicio = int(config.segmento.inicio * info.fps)
    fin = info.fotogramas if config.segmento.fin is None else min(info.fotogramas, int(config.segmento.fin * info.fps))
    centros = list(range(inicio + margen, fin - margen, paso))
    if config.max_muestras is not None:
        centros = centros[: config.max_muestras]
    if not centros:
        return
    necesarios: dict[int, list[int]] = {}
    for c in centros:
        for d in (0, *desplazamientos):
            necesarios.setdefault(c + d, []).append(c)
    pendientes: dict[int, dict[int, np.ndarray]] = {}
    cap = cv2.VideoCapture(str(info.ruta))
    if not cap.isOpened():
        raise FileNotFoundError(f"No se puede abrir el vídeo {info.ruta}")
    try:
        primero = centros[0] - margen
        if primero > 0:
            cap.set(cv2.CAP_PROP_POS_FRAMES, primero)
        indice = primero
        ultimo = centros[-1] + margen
        while indice <= ultimo:
            if not cap.grab():
                break
            duenos = necesarios.get(indice)
            if duenos:
                ok, imagen = cap.retrieve()
                if not ok:
                    break
                for centro in duenos:
                    grupo = pendientes.setdefault(centro, {})
                    grupo[indice] = imagen
                    if len(grupo) == 1 + len(desplazamientos):
                        del pendientes[centro]
                        yield Muestra(centro, centro / info.fps, grupo[centro], [grupo[centro + d] for d in desplazamientos])
            indice += 1
    finally:
        cap.release()


def _gris_medio(imagen: np.ndarray) -> np.ndarray:
    return cv2.cvtColor(cv2.resize(imagen, (imagen.shape[1] // 2, imagen.shape[0] // 2), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2GRAY)


def _a_cajas(detecciones: sv.Detections) -> list[Caja]:
    return [(float(x1), float(y1), float(x2), float(y2), float(c)) for (x1, y1, x2, y2), c in zip(detecciones.xyxy, detecciones.confidence)]


class Maestro:
    """Los tres detectores usados con calma: por teselas y en lote. `detectores_balon` permite proponer candidatos
    con más de un detector de balón (el público y el afinado): la unión de los dos, sin duplicados."""

    def __init__(self, modelos: Modelos, imgsz: int, detectores_balon: list | None = None) -> None:
        self.modelos = modelos
        self.imgsz = imgsz
        self.detectores_balon = detectores_balon or [modelos.balon]

    def _predecir(self, modelo, imagenes: list[np.ndarray], imgsz: int, conf: float) -> list[sv.Detections]:
        resultados = modelo(imagenes, imgsz=imgsz, conf=conf, device=self.modelos.device, **self.modelos.precision, verbose=False)
        return [sv.Detections.from_ultralytics(r) for r in resultados]

    def candidatos_balon(self, imagenes: list[np.ndarray]) -> list[list[Caja]]:
        """Por cada imagen, los candidatos a balón (teselas + fotograma entero, de cada detector, sin duplicados)."""
        alto, ancho = imagenes[0].shape[:2]
        posiciones = teselas(ancho, alto)
        trozos = [img[y:y + TESELA, x:x + TESELA] for img in imagenes for (x, y) in posiciones]
        cajas_por_imagen: list[list[np.ndarray]] = [[] for _ in imagenes]
        confianzas_por_imagen: list[list[np.ndarray]] = [[] for _ in imagenes]
        for detector in self.detectores_balon:
            por_tesela = []
            for i in range(0, len(trozos), 16):
                por_tesela += self._predecir(detector, trozos[i:i + 16], TESELA, CONF_CANDIDATO)
            enteros = self._predecir(detector, imagenes, self.imgsz, CONF_CANDIDATO)
            for i in range(len(imagenes)):
                if len(enteros[i]):
                    cajas_por_imagen[i].append(enteros[i].xyxy.reshape(-1, 4))
                    confianzas_por_imagen[i].append(enteros[i].confidence)
                for j, (x, y) in enumerate(posiciones):
                    d = por_tesela[i * len(posiciones) + j]
                    if len(d):
                        cajas_por_imagen[i].append(d.xyxy + np.array([x, y, x, y], dtype=np.float32))
                        confianzas_por_imagen[i].append(d.confidence)
        salida = []
        for cajas, confianzas in zip(cajas_por_imagen, confianzas_por_imagen):
            if not cajas:
                salida.append([])
                continue
            xyxy = np.concatenate(cajas).astype(np.float32)
            todas = sv.Detections(xyxy=xyxy, confidence=np.concatenate(confianzas).astype(np.float32), class_id=np.zeros(len(xyxy), dtype=int))
            salida.append(_a_cajas(todas.with_nms(threshold=0.3)))
        return salida

    def personas(self, imagen: np.ndarray) -> sv.Detections:
        return self._predecir(self.modelos.jugadores, [imagen], self.imgsz, CONF_PERSONA)[0]

    def personas_en(self, imagenes: list[np.ndarray]) -> list[np.ndarray]:
        """Solo las cajas de las personas (sin balón) de cada imagen."""
        salida = []
        for d in self._predecir(self.modelos.jugadores, imagenes, self.imgsz, CONF_PERSONA):
            salida.append(d.xyxy[d.class_id != CLASE_BALON] if len(d) else np.zeros((0, 4)))
        return salida

    def puntos_campo(self, imagen: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """Los 32 vértices con su confianza, a la escala nativa del detector (a 1280 se inventa puntos)."""
        xy = np.zeros((len(VERTICES), 2), dtype=np.float32)
        conf = np.zeros(len(VERTICES), dtype=np.float32)
        resultado = self.modelos.campo(imagen, imgsz=IMGSZ_CAMPO, device=self.modelos.device, **self.modelos.precision, verbose=False)[0]
        puntos = sv.KeyPoints.from_ultralytics(resultado)
        if len(puntos) and puntos.confidence is not None:
            xy[:] = puntos.xy[0]
            conf[:] = puntos.confidence[0]
        return xy, conf


def medir_campo_video(maestro: Maestro, info: InfoVideo, segmento: Segmento, fotogramas: int = 300, informar=print, guardar_puntos: Path | None = None) -> dict:
    """Mide el campo con los puntos detectados en fotogramas repartidos por todo el tramo. Con `guardar_puntos` deja
    los puntos detectados en un .npz para volver a medir sin GPU."""
    from tqdm import tqdm

    inicio = segmento.inicio
    fin = min(segmento.fin, info.duracion_s) if segmento.fin is not None else info.duracion_s
    tiempos = list(np.linspace(inicio + 1, max(inicio + 1, fin - 1), min(fotogramas, max(2, int(fin - inicio)))))
    observaciones = []
    for _, imagen in tqdm(fotogramas_en(info.ruta, tiempos), total=len(tiempos), desc="Midiendo el campo", unit="fot"):
        observaciones.append(maestro.puntos_campo(imagen))
    if guardar_puntos is not None:
        guardar_puntos.parent.mkdir(parents=True, exist_ok=True)
        np.savez_compressed(guardar_puntos, xy=np.stack([o[0] for o in observaciones]), conf=np.stack([o[1] for o in observaciones]), t=np.array(tiempos[: len(observaciones)]))
    largo, ancho, error, usados = medir_dimensiones(observaciones)
    informar(f"Campo medido: {largo / 100:.1f} × {ancho / 100:.1f} m (error medio {error:.1f} px con {usados} fotogramas de {len(observaciones)}).")
    return {"largo_m": round(largo / 100, 2), "ancho_m": round(ancho / 100, 2), "error_px": round(error, 2), "fotogramas_usados": usados, "fotogramas": len(observaciones)}


class EscritorYolo:
    def __init__(self, raiz: Path, nombres: list[str], kpt_shape: list[int] | None = None, flip_idx: list[int] | None = None, lado_max: int | None = None) -> None:
        self.raiz = raiz
        self.nombres = nombres
        self.kpt_shape = kpt_shape
        self.flip_idx = flip_idx
        self.lado_max = lado_max
        self.recuento = {"train": 0, "val": 0}
        for particion in ("train", "val"):
            (raiz / "images" / particion).mkdir(parents=True, exist_ok=True)
            (raiz / "labels" / particion).mkdir(parents=True, exist_ok=True)

    def escribir(self, nombre: str, particion: str, imagen: np.ndarray | Path, lineas: list[str]) -> None:
        destino = self.raiz / "images" / particion / f"{nombre}.jpg"
        if isinstance(imagen, Path):
            shutil.copy2(imagen, destino)
        else:
            if self.lado_max and max(imagen.shape[:2]) > self.lado_max:
                escala = self.lado_max / max(imagen.shape[:2])
                imagen = cv2.resize(imagen, (round(imagen.shape[1] * escala), round(imagen.shape[0] * escala)), interpolation=cv2.INTER_AREA)
            cv2.imwrite(str(destino), imagen, [int(cv2.IMWRITE_JPEG_QUALITY), 92])
        (self.raiz / "labels" / particion / f"{nombre}.txt").write_text("\n".join(lineas) + ("\n" if lineas else ""), encoding="utf-8")
        self.recuento[particion] += 1

    def cerrar(self) -> Path:
        lineas = [f"path: {self.raiz.resolve().as_posix()}", "train: images/train", "val: images/val", f"nc: {len(self.nombres)}", "names: [" + ", ".join(self.nombres) + "]"]
        if self.kpt_shape:
            lineas.append(f"kpt_shape: [{self.kpt_shape[0]}, {self.kpt_shape[1]}]")
        if self.flip_idx:
            lineas.append("flip_idx: [" + ", ".join(str(i) for i in self.flip_idx) + "]")
        destino = self.raiz / "data.yaml"
        destino.write_text("\n".join(lineas) + "\n", encoding="utf-8")
        return destino


def _vista_previa(imagen: np.ndarray, balon: Caja | None, candidatos: list[Candidato], personas: sv.Detections, campo: EtiquetaCampo | None, destino: Path) -> None:
    lienzo = imagen.copy()
    colores = {0: (0, 255, 255), 1: (255, 128, 0), 2: (0, 200, 0), 3: (0, 0, 255)}
    for (x1, y1, x2, y2), clase in zip(personas.xyxy, personas.class_id):
        cv2.rectangle(lienzo, (int(x1), int(y1)), (int(x2), int(y2)), colores.get(int(clase), (200, 200, 200)), 2)
    if campo is not None:
        for i, (x, y, v) in enumerate(campo.puntos):
            if v:
                cv2.circle(lienzo, (int(x), int(y)), 6, (255, 0, 255), -1)
                cv2.putText(lienzo, str(i), (int(x) + 6, int(y) - 6), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 0, 255), 1, cv2.LINE_AA)
    for c in candidatos:
        xy = _centro(c.caja)
        rechazado = c.movimiento == "quieto" or (c.veredicto is not None and not c.veredicto.get("balon"))
        cv2.circle(lienzo, (int(xy[0]), int(xy[1])), int(_lado(c.caja)) + 10, (0, 0, 255) if rechazado else (200, 200, 200), 1)
        cv2.putText(lienzo, f"{c.caja[4]:.2f} {c.movimiento[:3]} {c.desplazamiento_px:.0f}", (int(xy[0]) + 14, int(xy[1]) - 14), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 0, 255) if rechazado else (200, 200, 200), 1, cv2.LINE_AA)
    if balon is not None:
        c = _centro(balon)
        cv2.circle(lienzo, (int(c[0]), int(c[1])), int(_lado(balon)) + 10, (0, 255, 255), 3)
    escala = 1280 / lienzo.shape[1]
    if escala < 1:
        lienzo = cv2.resize(lienzo, (1280, round(lienzo.shape[0] * escala)), interpolation=cv2.INTER_AREA)
    destino.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(destino), lienzo, [int(cv2.IMWRITE_JPEG_QUALITY), 80])


@dataclass
class Pendiente:
    nombre: str
    t: float
    particion: str
    candidatos: list[Candidato]
    personas_lineas: list[str]
    fotograma: Path
    # Para la vista previa, que se hace al final (cuando el juez ya ha hablado).
    vista_previa: bool = False
    personas: sv.Detections | None = None
    campo: EtiquetaCampo | None = None


def juzgar(pendientes: list[Pendiente], juez, hilos: int, avance=None) -> int:
    """Pregunta al juez por cada candidato (en paralelo si el juez es remoto); devuelve cuántos ha juzgado."""
    candidatos = [c for p in pendientes for c in p.candidatos if c.recorte is not None]

    def preguntar(c: Candidato) -> None:
        c.veredicto = juez.preguntar_json([c.recorte], INSTRUCCIONES_JUEZ, ESQUEMA_JUEZ)

    hechos = 0
    with ThreadPoolExecutor(max_workers=max(1, hilos)) as pool:
        for _ in pool.map(preguntar, candidatos):
            hechos += 1
            if avance:
                avance(hechos)
    return hechos


def _detectores_balon(modelos: Modelos, config: ConfiguracionEtiquetado) -> list:
    """El detector (o los dos) que proponen candidatos a balón según `maestro_balon`. Si no hay pesos afinados,
    «afinado» y «ambos» se quedan con el público."""
    from ultralytics import YOLO

    from .modelos import ruta_pesos

    cargado = ruta_pesos(config.modelos, "balon", afinados=config.afinados)
    publico, afinado = ruta_pesos(config.modelos, "balon", afinados=False), ruta_pesos(config.modelos, "balon", afinados=True)
    rutas = {"publico": [publico], "afinado": [afinado], "ambos": [publico, afinado]}[config.maestro_balon]
    detectores = []
    for ruta in dict.fromkeys(rutas):
        if ruta == cargado:
            detectores.append(modelos.balon)
        else:
            otro = YOLO(str(ruta))
            otro.to(modelos.device)
            detectores.append(otro)
    return detectores


def etiquetar(config: ConfiguracionEtiquetado, informar=print) -> dict:
    from tqdm import tqdm

    from .llm import crear_modelo
    from .modelos import cargar_modelos

    info = info_video(config.video)
    config.salida.mkdir(parents=True, exist_ok=True)
    modelos = cargar_modelos(config.modelos, config.device, afinados=config.afinados)
    maestro = Maestro(modelos, config.imgsz, _detectores_balon(modelos, config))
    juez = crear_modelo(config.juez, config.modelo_juez)
    fin_s = min(config.segmento.fin, info.duracion_s) if config.segmento.fin is not None else info.duracion_s
    informar(f"Vídeo: {info.ancho}×{info.alto} a {info.fps:.0f} fps, {info.duracion_s / 60:.1f} min. Maestros: {', '.join(f'{n} ({r})' for n, r in modelos.rutas.items())} (balón: {config.maestro_balon}). Juez: {juez.nombre if juez else 'ninguno (regla de movimiento)'}.")

    # 0. Las medidas del campo. Si se conocen, los puntos del campo se etiquetan reproyectando los 32 vértices; si la
    # medida automática no es fiable (el detector público ve pocos puntos coherentes de noche), se etiquetan solo los
    # puntos vistos y coherentes, que no dependen de las medidas.
    if config.campo:
        medida = {"largo_m": config.campo[0], "ancho_m": config.campo[1], "origen": "parametro"}
        reproyectar = True
    else:
        medida = dict(medir_campo_video(maestro, info, config.segmento, informar=informar, guardar_puntos=config.salida / "campo-puntos.npz"), origen="medido")
        reproyectar = medida["error_px"] <= ERROR_MEDIDA_FIABLE_PX
        if not reproyectar:
            informar(f"La medida del campo no es fiable (error {medida['error_px']:.1f} px > {ERROR_MEDIDA_FIABLE_PX:g}): se mantienen 105 × 68 y se etiquetan solo los puntos vistos. Con las medidas reales (--campo) salen etiquetas completas.")
            medida = dict(medida, largo_m=105.0, ancho_m=68.0, fiable=False)
    medida["reproyectar"] = reproyectar
    configurar_campo(medida["largo_m"] * 100, medida["ancho_m"] * 100)
    (config.salida / "campo-medida.json").write_text(json.dumps(medida, ensure_ascii=False, indent=2), encoding="utf-8")

    # 1. La pasada por el vídeo: el campo se escribe ya; balón y personas esperan al juez.
    campo_geom = Campo()
    balon_w = EscritorYolo(config.salida / "balon", ["ball"])
    personas_w = EscritorYolo(config.salida / "personas", NOMBRES_PERSONAS)
    campo_w = EscritorYolo(config.salida / "campo", ["pitch"], kpt_shape=[len(VERTICES), 3], flip_idx=indices_espejo(), lado_max=1280)
    carpeta_fotogramas = config.salida / "fotogramas"
    carpeta_fotogramas.mkdir(exist_ok=True)
    pendientes: list[Pendiente] = []
    recuento = {"muestras": 0, "campo": 0, "campo_puntos_vistos": 0, "camara_sin_estimar": 0}
    total = int((fin_s - config.segmento.inicio) / config.cada_s)
    if config.max_muestras is not None:
        total = min(total, config.max_muestras)
    informar(f"Una muestra cada {config.cada_s:g} s: {total} muestras.")

    for muestra in tqdm(_muestras(info, config), total=total, desc="Etiquetando", unit="muestra"):
        recuento["muestras"] += 1
        candidatos_brutos = maestro.candidatos_balon([muestra.imagen] + muestra.vecinos)
        personas = maestro.personas(muestra.imagen)
        no_balon = personas[personas.class_id != CLASE_BALON] if len(personas) else personas
        otros = _a_cajas(personas[personas.class_id == CLASE_BALON]) if len(personas) else []
        afines = None
        personas_vecinos = None
        if candidatos_brutos[0]:
            gris = _gris_medio(muestra.imagen)
            afines = [movimiento_camara(gris, _gris_medio(v)) for v in muestra.vecinos]
            recuento["camara_sin_estimar"] += sum(1 for a in afines if a is None)
            personas_vecinos = maestro.personas_en(muestra.vecinos)
        candidatos = puntuar_candidatos(candidatos_brutos[0], candidatos_brutos[1:], otros, no_balon.xyxy if len(no_balon) else np.zeros((0, 4)), afines, personas_vecinos=personas_vecinos)
        xy, conf = maestro.puntos_campo(muestra.imagen)
        campo = etiqueta_campo(xy, conf, info.ancho, info.alto, reproyectar=reproyectar)
        if campo is not None:
            en_campo = campo.homografia.a_campo(np.array([_centro(c.caja) for c in candidatos], dtype=np.float32)) if candidatos else np.zeros((0, 2))
            candidatos = [c for c, p in zip(candidatos, en_campo) if campo_geom.dentro((float(p[0]), float(p[1])), margen=MARGEN_CAMPO_CM)]
        if juez is not None:
            for c in candidatos:
                c.recorte = recorte_juez(muestra.imagen, c.caja)
        particion = particion_de(muestra.t)
        nombre = f"{config.video.stem}-{muestra.indice:07d}"
        ruta_fotograma = carpeta_fotogramas / f"{nombre}.jpg"
        cv2.imwrite(str(ruta_fotograma), muestra.imagen, [int(cv2.IMWRITE_JPEG_QUALITY), 92])
        lineas_personas = [linea_yolo(int(clase), caja, info.ancho, info.alto) for caja, clase in zip(no_balon.xyxy, no_balon.class_id)] if len(no_balon) else []
        vista = bool(config.vista_previa_cada) and recuento["muestras"] % config.vista_previa_cada == 0
        pendientes.append(Pendiente(nombre, muestra.t, particion, candidatos, lineas_personas, ruta_fotograma, vista, no_balon if vista else None, campo if vista else None))
        if campo is not None:
            recuento["campo"] += 1
            recuento["campo_puntos_vistos"] += campo.puntos_vistos
            campo_w.escribir(nombre, particion, muestra.imagen, [linea_yolo(0, campo.caja, info.ancho, info.alto, campo.puntos)])

    # 2. El juez.
    if juez is not None:
        n = sum(len(p.candidatos) for p in pendientes)
        with tqdm(total=n, desc=f"Juzgando balones con {juez.nombre}", unit="candidato") as barra:
            juzgar(pendientes, juez, hilos=8 if config.juez == "claude" else 1, avance=lambda k: barra.update(k - barra.n))

    # 3. Balón y personas, ya resueltos.
    estados: dict[str, int] = {}
    negativos: list[tuple[float, Pendiente]] = []
    positivos = 0
    registro = (config.salida / "muestras.jsonl").open("w", encoding="utf-8")
    try:
        for p in pendientes:
            if juez is not None:
                resultado, estado = resolver_con_juez(p.candidatos)
            else:
                resultado = elegir(p.candidatos, [c for c in p.candidatos if c.heuristico])
                if not p.candidatos:
                    estado = "sin_candidatos"
                elif resultado.caja is not None:
                    estado = "aprobado"
                elif resultado.ambiguo:
                    estado = "ambiguo"
                elif all(c.movimiento == "quieto" and not c.heuristico and c.caja[4] < CONF_QUIETO_NEGATIVO for c in p.candidatos):
                    # Todo lo que parecía balón está quieto respecto al césped y con poca confianza: marcas, reflejos.
                    estado = "rechazado"
                else:
                    estado = "sin_confirmar"
            estados[estado] = estados.get(estado, 0) + 1
            if resultado.caja is not None:
                positivos += 1
                balon_w.escribir(p.nombre, p.particion, p.fotograma, [linea_yolo(0, resultado.caja, info.ancho, info.alto)])
            elif estado == "rechazado":
                negativos.append((max(c.caja[4] for c in p.candidatos), p))
            if estado in ("aprobado", "sin_candidatos", "rechazado"):
                lineas = list(p.personas_lineas)
                if resultado.caja is not None:
                    lineas.append(linea_yolo(CLASE_BALON, resultado.caja, info.ancho, info.alto))
                personas_w.escribir(p.nombre, p.particion, p.fotograma, lineas)
            registro.write(json.dumps({
                "nombre": p.nombre, "t": round(p.t, 2), "particion": p.particion, "estado": estado,
                "balon": None if resultado.caja is None else [round(v, 2) for v in resultado.caja],
                "candidatos": [{"caja": [round(v, 2) for v in c.caja], "puntuacion": round(c.puntuacion, 2), "heuristico": c.heuristico, "movimiento": c.movimiento, "desplazamiento_px": round(c.desplazamiento_px, 1), "vecinos": c.vecinos, "veredicto": c.veredicto} for c in p.candidatos],
                "personas": len(p.personas_lineas),
            }, ensure_ascii=False) + "\n")
            if p.vista_previa and p.personas is not None:
                _vista_previa(cv2.imread(str(p.fotograma)), resultado.caja, p.candidatos, p.personas, p.campo, config.salida / "revision" / f"{p.nombre}.jpg")
        negativos.sort(key=lambda par: -par[0])
        elegidos = negativos[: int(positivos * FRACCION_NEGATIVOS)]
        for _, p in elegidos:
            balon_w.escribir(p.nombre, p.particion, p.fotograma, [])
    finally:
        registro.close()
    shutil.rmtree(carpeta_fotogramas, ignore_errors=True)

    yamls = {"balon": balon_w.cerrar(), "personas": personas_w.cerrar(), "campo": campo_w.cerrar()}
    recuento.update({"balon": positivos, "balon_negativos": len(elegidos), "estados": estados, "personas": personas_w.recuento["train"] + personas_w.recuento["val"]})
    resumen = {
        "video": str(config.video), "cada_s": config.cada_s, "imgsz": config.imgsz, "maestros": modelos.rutas, "juez": juez.nombre if juez else None, "campo": medida,
        "recuento": recuento,
        "conjuntos": {k: {"train": w.recuento["train"], "val": w.recuento["val"], "data": str(y)} for k, w, y in (("balon", balon_w, yamls["balon"]), ("personas", personas_w, yamls["personas"]), ("campo", campo_w, yamls["campo"]))},
    }
    (config.salida / "resumen.json").write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
    m = recuento["muestras"] or 1
    informar(
        f"{recuento['muestras']} muestras: balón en {positivos} ({100 * positivos / m:.0f} %) y {len(elegidos)} negativos difíciles; estados {estados}; "
        f"campo en {recuento['campo']} ({100 * recuento['campo'] / m:.0f} %, {recuento['campo_puntos_vistos'] / max(1, recuento['campo']):.1f} puntos vistos de media)."
    )
    for k, c in resumen["conjuntos"].items():
        informar(f"  {k}: {c['train']} de entrenamiento, {c['val']} de validación → {c['data']}")
    return resumen
