"""Quién es quién por el aspecto (Ojeador 2.3.0). A un tramo al que no se le lee el dorsal a veces se le reconoce por lo
que no es la camiseta, igual para todo el equipo: el pelo, la piel, las medias y las botas, las mangas, la complexión.

SigLIP (google/siglip-base-patch16-224, Apache-2.0; solo la parte de la imagen, en `modelos/siglip`) da de cada recorte
un vector de 768 números; cada tracklet guarda la suma de los suyos (`ColaApariencia`). En cada partido y para cada
equipo, una regresión logística aprende a distinguir a los jugadores que ya identificó el dorsal (`Sonda`), y antes de
usarla se prueba con cadenas identificadas que se apartan del aprendizaje por bloques de tiempo (`probar`): solo se usa
con un umbral con el que acierta en el 95 % o más de las que se atreve a dar (`identidades._apariencia` pone el resto de
condiciones: que el jugador estuviera en el campo, que no se le vea a la vez en otro sitio y que llegue a tiempo)."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

import cv2
import numpy as np

CARPETA = "siglip"
LADO = 224
# Por debajo de este alto (px) el recorte no dice nada del aspecto.
ALTO_MIN_PX = 40
LOTE = 256
# Cada tracklet, un recorte de cada RECORTE_CADA fotogramas analizados (a 5 por segundo, dos por segundo y medio).
RECORTE_CADA = 2

# Prueba: bloques de tiempo, umbrales que se prueban (probabilidad del primero; la ventaja sobre el segundo es aparte) y
# lo que tiene que salir para usarla.
BLOQUES = 5
UMBRALES = (0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.97)
VENTAJA = 0.2
PRECISION_MIN = 0.95
ACIERTOS_MIN = 10


class Apariencia:
    def __init__(self, modelo, device: str, dtype, ruta: Path) -> None:
        self.modelo = modelo
        self.device = device
        self.dtype = dtype
        self.ruta = ruta

    def embeber(self, cuadrados: list[np.ndarray]) -> np.ndarray:
        """Vectores (N × 768, de norma 1) de recortes ya preparados (`preparar`)."""
        import torch

        x = torch.from_numpy(np.stack(cuadrados)).to(self.device).permute(0, 3, 1, 2).to(self.dtype)
        x = (x / 255.0 - 0.5) / 0.5
        with torch.inference_mode():
            salida = self.modelo(pixel_values=x).pooler_output.float()
        return torch.nn.functional.normalize(salida, dim=1).cpu().numpy()


def preparar(recorte: np.ndarray) -> np.ndarray | None:
    """Recorte BGR de OpenCV → cuadrado RGB de 224 px (uint8) sin deformarlo: lo que falta, gris."""
    alto, ancho = recorte.shape[:2]
    if alto < 2 or ancho < 2:
        return None
    lado = max(alto, ancho)
    lienzo = np.full((lado, lado, 3), 128, dtype=np.uint8)
    y, x = (lado - alto) // 2, (lado - ancho) // 2
    lienzo[y:y + alto, x:x + ancho] = recorte
    cuadrado = cv2.resize(lienzo, (LADO, LADO), interpolation=cv2.INTER_AREA if lado > LADO else cv2.INTER_LINEAR)
    return cv2.cvtColor(cuadrado, cv2.COLOR_BGR2RGB)


def cargar_apariencia(carpeta_modelos: Path, device: str) -> Apariencia | None:
    ruta = carpeta_modelos / CARPETA
    if not (ruta / "model.safetensors").exists():
        return None
    import torch
    from transformers import SiglipVisionModel

    dtype = torch.float16 if str(device).startswith("cuda") else torch.float32
    modelo = SiglipVisionModel.from_pretrained(ruta, local_files_only=True).to(device, dtype=dtype).eval()
    return Apariencia(modelo, device, dtype, ruta / "model.safetensors")


class ColaApariencia:
    """Junta los recortes de varios fotogramas, los pasa por SigLIP por lotes y suma los vectores de cada tracklet."""

    def __init__(self, apariencia: Apariencia, lote: int = LOTE) -> None:
        self.apariencia = apariencia
        self.lote = lote
        self.pendientes: list[tuple[int, np.ndarray]] = []
        self.sumas: dict[int, np.ndarray] = {}
        self.cuentas: dict[int, int] = {}
        self.recortes = 0

    def poner(self, tracklet: int, recorte: np.ndarray) -> None:
        cuadrado = preparar(recorte)
        if cuadrado is None:
            return
        self.pendientes.append((tracklet, cuadrado))
        if len(self.pendientes) >= self.lote:
            self.vaciar()

    def vaciar(self) -> None:
        if not self.pendientes:
            return
        vectores = self.apariencia.embeber([c for _, c in self.pendientes])
        for (tid, _), v in zip(self.pendientes, vectores):
            if tid in self.sumas:
                self.sumas[tid] += v
                self.cuentas[tid] += 1
            else:
                self.sumas[tid] = v.astype(np.float32)
                self.cuentas[tid] = 1
        self.recortes += len(self.pendientes)
        self.pendientes = []

    def vectores(self) -> dict[int, tuple[np.ndarray, int]]:
        """Por tracklet, la suma de sus vectores y cuántos recortes."""
        self.vaciar()
        return {tid: (s, self.cuentas[tid]) for tid, s in self.sumas.items()}


def juntar(sumas: list[tuple[np.ndarray, int]]) -> tuple[np.ndarray, int] | None:
    """La suma de los vectores de varios tracklets y cuántos recortes lleva."""
    n = sum(c for _, c in sumas)
    return (np.sum([s for s, _ in sumas], axis=0), n) if n else None


def media(sumas: list[tuple[np.ndarray, int]]) -> tuple[np.ndarray, int] | None:
    """El vector de varios tracklets juntos (de norma 1) y cuántos recortes lleva."""
    junto = juntar(sumas)
    if junto is None:
        return None
    norma = float(np.linalg.norm(junto[0]))
    return (junto[0] / norma, junto[1]) if norma > 0 else None


def coherencia(suma: np.ndarray, n: int) -> float | None:
    """Parecido medio (coseno) entre cada dos recortes, sacado de la suma de sus vectores (‖Σv‖² = n + Σ vi·vj con i ≠ j):
    si el seguidor saltó de una persona a otra, baja."""
    if n < 2:
        return None
    return float((suma @ suma - n) / (n * (n - 1)))


def probabilidad_equipo(conocidas: np.ndarray, equipos: list[int], otras: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Por el aspecto, la probabilidad de ser del equipo 1: para las `conocidas`, de una regresión que no las vio (por
    bloques); para las `otras`, de una con todas. La camiseta se ve tanto que un tramo con la de otro equipo (o la del
    árbitro) que el color medio dejó en este se nota."""
    from sklearn.linear_model import LogisticRegression
    from sklearn.model_selection import cross_val_predict
    from sklearn.pipeline import make_pipeline
    from sklearn.preprocessing import StandardScaler

    def modelo():
        return make_pipeline(StandardScaler(), LogisticRegression(C=1.0, max_iter=3000, class_weight="balanced"))

    y = np.asarray(equipos)
    pliegues = min(BLOQUES, int(min((y == 0).sum(), (y == 1).sum())))
    if pliegues < 2:
        return np.full(len(y), np.nan), np.full(len(otras), np.nan)
    de_conocidas = cross_val_predict(modelo(), conocidas, y, cv=pliegues, method="predict_proba")[:, 1]
    de_otras = modelo().fit(conocidas, y).predict_proba(otras)[:, 1] if len(otras) else np.zeros(0)
    return de_conocidas, de_otras


class Sonda:
    """Regresión logística sobre los vectores estandarizados: de qué jugador (dorsal) es cada uno. Medido el 25-11-2026 en
    el J8 (minutos 20-30, cadenas identificadas apartadas por bloques de tiempo): acierta el jugador en el 65-81 % (al
    azar, el 9 %) y, con probabilidad de 0,9 o más, en el 94-97 %. Sin estandarizar, los vectores de norma 1 apenas se
    separan y la regresión no llega a estar segura de nada."""

    def __init__(self, vectores: np.ndarray, dorsales: list[int]) -> None:
        from sklearn.linear_model import LogisticRegression
        from sklearn.preprocessing import StandardScaler

        self.clases = sorted(set(dorsales))
        self.escala = StandardScaler().fit(vectores)
        self.modelo = LogisticRegression(C=1.0, max_iter=3000, class_weight="balanced")
        self.modelo.fit(self.escala.transform(vectores), [self.clases.index(d) for d in dorsales])

    def probabilidades(self, vectores: np.ndarray) -> np.ndarray:
        """Por vector, la probabilidad de cada dorsal de `clases` (las columnas, en ese orden)."""
        salida = np.zeros((len(vectores), len(self.clases)))
        salida[:, self.modelo.classes_] = self.modelo.predict_proba(self.escala.transform(vectores))
        return salida


def mejor(p: np.ndarray, clases: list[int], permitidos: set[int] | None = None) -> tuple[int, float, float] | None:
    """El dorsal más probable entre los `permitidos` (todos si no se dice), su probabilidad y la del segundo."""
    orden = [k for k in np.argsort(-p) if permitidos is None or clases[k] in permitidos]
    if not orden:
        return None
    segunda = float(p[orden[1]]) if len(orden) > 1 else 0.0
    return clases[orden[0]], float(p[orden[0]]), segunda


def se_atreve(p1: float, p2: float, umbral: float) -> bool:
    return p1 >= umbral and p1 - p2 >= VENTAJA


@dataclass
class Prueba:
    """Lo que salió al probar la sonda con cadenas identificadas apartadas del aprendizaje."""

    umbral: float | None
    probadas: int
    # Con el umbral elegido (o, si no hay, con el más alto): las que se atrevió a dar y cuántas acertó.
    dadas: int = 0
    aciertos: int = 0
    # Con cada umbral: (dadas, aciertos).
    tabla: dict[float, tuple[int, int]] = field(default_factory=dict)

    @property
    def precision(self) -> float | None:
        return round(self.aciertos / self.dadas, 3) if self.dadas else None

    def a_dict(self) -> dict:
        return {
            "umbral": self.umbral, "probadas": self.probadas, "dadas": self.dadas, "aciertos": self.aciertos, "precision": self.precision,
            "por_umbral": {str(u): {"dadas": d, "aciertos": a} for u, (d, a) in self.tabla.items()},
        }


def probar(vectores: np.ndarray, dorsales: list[int], tiempos: list[float], posibles, bloques: int = BLOQUES) -> Prueba:
    """Aparta cada bloque de tiempo, aprende con el resto y mira qué diría de las del bloque. `posibles(k, apartadas)` da
    los dorsales que podía tener la k sin saber nada de las `apartadas` (las del bloque). Devuelve el umbral más bajo con
    el que acierta en al menos PRECISION_MIN de las que da (y da al menos ACIERTOS_MIN), o ninguno."""
    n = len(dorsales)
    orden = sorted(range(n), key=lambda k: tiempos[k])
    trozos = [orden[b * n // bloques:(b + 1) * n // bloques] for b in range(bloques)]
    dadas = {u: 0 for u in UMBRALES}
    aciertos = {u: 0 for u in UMBRALES}
    for trozo in trozos:
        if not trozo:
            continue
        dentro = set(trozo)
        fuera = [k for k in range(n) if k not in dentro]
        if len({dorsales[k] for k in fuera}) < 2:
            continue
        sonda = Sonda(vectores[fuera], [dorsales[k] for k in fuera])
        for fila, k in zip(sonda.probabilidades(vectores[trozo]), trozo):
            elegido = mejor(fila, sonda.clases, posibles(k, dentro))
            if elegido is None:
                continue
            for u in UMBRALES:
                if se_atreve(elegido[1], elegido[2], u):
                    dadas[u] += 1
                    aciertos[u] += elegido[0] == dorsales[k]
    tabla = {u: (dadas[u], aciertos[u]) for u in UMBRALES}
    for u in UMBRALES:
        if dadas[u] >= ACIERTOS_MIN and aciertos[u] >= PRECISION_MIN * dadas[u]:
            return Prueba(u, n, dadas[u], aciertos[u], tabla)
    return Prueba(None, n, dadas[UMBRALES[-1]], aciertos[UMBRALES[-1]], tabla)
