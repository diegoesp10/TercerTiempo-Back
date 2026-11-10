"""Parámetros del análisis: lo que se puede ajustar desde la línea de órdenes y los valores por defecto."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path


@dataclass(frozen=True)
class Segmento:
    """Tramo del vídeo a analizar, en segundos desde el principio del archivo."""

    inicio: float = 0.0
    fin: float | None = None

    @staticmethod
    def parse(texto: str | None) -> "Segmento":
        if not texto:
            return Segmento()
        inicio, _, fin = texto.partition("-")
        return Segmento(a_segundos(inicio), a_segundos(fin) if fin else None)


def a_medidas(texto: str | None) -> tuple[float, float] | None:
    """'100x62' o '100,62' (metros) → (largo, ancho)."""
    if not texto:
        return None
    partes = texto.lower().replace(",", "x").replace("×", "x").split("x")
    if len(partes) != 2:
        raise ValueError(f"Medidas del campo no válidas: {texto} (p. ej. 100x62)")
    largo, ancho = (float(p) for p in partes)
    if not (60 <= largo <= 130 and 35 <= ancho <= 90):
        raise ValueError(f"Medidas del campo fuera de rango: {texto}")
    return largo, ancho


def a_segundos(texto: str) -> float:
    """'1:23:45', '23:45' o '345' → segundos."""
    partes = [float(p) for p in texto.strip().split(":")]
    total = 0.0
    for parte in partes:
        total = total * 60 + parte
    return total


@dataclass
class Configuracion:
    video: Path
    salida: Path
    modelos: Path = Path("modelos")
    # Usar los pesos afinados con partidos propios (modelos/afinados/*.pt) si existen.
    afinados: bool = True
    # Medidas del campo en metros (largo, ancho); si no se dan, las del modelo del campo de Roboflow (120 × 70). Se miden
    # desde el vídeo con `tt-analisis medir-campo`.
    campo: tuple[float, float] | None = None
    device: str = "cuda"
    # Fotogramas analizados por segundo de vídeo: 5 es el equilibrio entre coste y seguir el balón.
    fps_muestreo: float = 5.0
    segmento: Segmento = field(default_factory=Segmento)
    # Minuto del vídeo en el que empieza la segunda parte; si no se da, se detecta por el parón más largo.
    descanso: float | None = None
    # Tamaño de inferencia de los detectores (los jugadores lejanos son pequeños: 1280 mejor que 640).
    imgsz: int = 1280
    # El detector de puntos del campo se entrenó a 640 px y a 1280 se inventa puntos (medido con el vídeo del
    # Vallobín: 78 % de fotogramas calibrables y 13 px de error a 640, frente a 46 % y 351 px a 1280).
    imgsz_campo: int = 640
    confianza: float = 0.3
    # "movil" (alguien graba con paneo y zoom: se calibra en cada fotograma y se sigue con BoT-SORT) o "fija" (trípode o
    # cámara tipo Veo: una sola homografía para todo el partido y ByteTrack).
    camara: str = "movil"
    # Seguidor de Ultralytics: si no se da, el del paquete según la cámara (botsort-tt.yaml o bytetrack-tt.yaml).
    tracker: str | None = None

    def tracker_yaml(self) -> str:
        if self.tracker:
            return self.tracker
        nombre = "botsort-tt.yaml" if self.camara == "movil" else "bytetrack-tt.yaml"
        return str(Path(__file__).with_name("trackers") / nombre)
    # Buscar el balón también por teselas cuando se pierde (más lento, más fiable).
    balon_fino: bool = True
    # Cada cuántos segundos se recalcula la homografía con cámara móvil (0 = en cada fotograma analizado).
    calibrar_cada_s: float = 0.0
    # Recortes de jugadores con los que se ajusta el clasificador de equipos.
    muestras_equipos: int = 600
    # Modelo de lenguaje para confirmar eventos y redactar: "none", "claude" u "ollama".
    llm: str = "none"
    modelo_llm: str | None = None
    # Clips de vídeo de los eventos (ffmpeg) y vídeo con el radar del tramo.
    clips: bool = True
    radar: bool = False
    # Vídeo anotado completo del tramo analizado (lento; para revisar a ojo).
    video_anotado: bool = False
    # Minutos de margen de cada clip de evento.
    clip_margen_s: float = 5.0
    # Para probar sin GPU: analiza solo los primeros N fotogramas muestreados.
    max_fotogramas: int | None = None
    # Cómo se llama cada equipo en los informes (si no, "Equipo A" y "Equipo B").
    nombres_equipos: tuple[str, str] = ("Equipo A", "Equipo B")
    # Para quien lo lanza como proceso (el servicio de vídeo): líneas «##progreso N» en la salida estándar en vez de
    # las barras de tqdm, y fallar (código de salida 3) si no hay GPU en vez de analizar en CPU durante horas.
    progreso: bool = False
    exigir_gpu: bool = False
    # Semilla para medir la estabilidad: fija los generadores y desplaza qué fotogramas se muestrean dentro del paso.
    semilla: int | None = None


# Clases del detector de jugadores de Roboflow (football-player-detection).
CLASE_BALON = 0
CLASE_PORTERO = 1
CLASE_JUGADOR = 2
CLASE_ARBITRO = 3

# Umbrales del juego, en centímetros sobre el campo (la configuración del campo trabaja en cm).
DISTANCIA_POSESION_CM = 250.0
DISTANCIA_LEJOS_CM = 600.0
# Altura de un jugador para estimar distancias en píxeles cuando el campo no está calibrado.
ALTURA_JUGADOR_CM = 180.0
# Más lejos de las líneas que esto, una persona o un balón no son del partido (gradas, banquillo, recogepelotas).
MARGEN_CAMPO_CM = 350.0
VELOCIDAD_MAX_JUGADOR_CM_S = 1200.0
VELOCIDAD_TIRO_CM_S = 1000.0
DISTANCIA_TIRO_CM = 3500.0
