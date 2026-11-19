"""Lectura del dorsal en los recortes de los jugadores con PARSeq (un lector de texto de escenas, Apache-2.0).

De cada recorte se toma la zona del número (la parte alta del tronco, donde va el dorsal de la espalda) y el lector da,
para cada número del 1 al 99, la probabilidad de que sea ese: P(primera cifra) · P(segunda cifra o fin | primera). Con
eso cada tracklet suma las lecturas de muchos fotogramas (un jugador de frente no enseña el número y no vota) y, si se
conocen los dorsales del acta, solo cuentan los que de verdad jugaron."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np

PESOS_PARSEQ = "parseq-bb5792a6.pt"
URL_PARSEQ = "https://github.com/baudm/parseq/releases/download/v1.0.0/parseq-bb5792a6.pt"
# Lo que el modelo publicado sabe leer (94 caracteres); el orden fija los identificadores de cada carácter.
CHARSET_PARSEQ = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"
NUMEROS = list(range(1, 100))
ALTO, ANCHO = 32, 128

# Zona del dorsal dentro de la caja de la persona (proporciones del alto y del ancho): la espalda, de los hombros a
# media altura del tronco. Medido con el vídeo del Vallobín: más alta se cuela la cabeza y más baja, el pantalón.
ZONA_Y = (0.16, 0.42)
ZONA_X = (0.2, 0.8)
# Por debajo de este alto de la persona en píxeles el número no se puede leer.
ALTO_MIN_PX = 60


def zona_dorsal(imagen: np.ndarray, caja) -> np.ndarray | None:
    """El recorte de la zona del número de la persona de `caja` (x1, y1, x2, y2), o None si es pequeña o se sale."""
    x1, y1, x2, y2 = (float(v) for v in caja)
    alto, ancho = y2 - y1, x2 - x1
    if alto < ALTO_MIN_PX or ancho <= 4:
        return None
    ya, yb = int(round(y1 + ZONA_Y[0] * alto)), int(round(y1 + ZONA_Y[1] * alto))
    xa, xb = int(round(x1 + ZONA_X[0] * ancho)), int(round(x1 + ZONA_X[1] * ancho))
    h, w = imagen.shape[:2]
    ya, yb, xa, xb = max(0, ya), min(h, yb), max(0, xa), min(w, xb)
    if yb - ya < 8 or xb - xa < 6:
        return None
    return imagen[ya:yb, xa:xb]


def preparar(zona: np.ndarray) -> np.ndarray:
    """La zona estirada a 32 × 128, la entrada del lector. Estirar el número lee mejor que dejarlo en su proporción y
    rellenar los lados (con el vídeo del Vallobín, 5 de 7 dorsales difíciles frente a 4 sin afinar)."""
    lienzo = cv2.resize(zona, (ANCHO, ALTO), interpolation=cv2.INTER_CUBIC)
    rgb = cv2.cvtColor(lienzo, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    return ((rgb - 0.5) / 0.5).transpose(2, 0, 1)


@dataclass
class Lectura:
    # Probabilidad de cada número del 1 al 99 (índice 0 = el 1) y la de que no haya un número legible.
    probabilidades: np.ndarray
    sin_numero: float

    @property
    def mejor(self) -> tuple[int, float]:
        i = int(self.probabilidades.argmax())
        return NUMEROS[i], float(self.probabilidades[i])


def peso_lectura(lectura: "Lectura") -> float:
    """Cuánto cuenta una lectura: lo que el lector pone en números (de frente, sin número a la vista, se reparte entre
    letras y basura)."""
    return max(0.0, 1.0 - lectura.sin_numero)


class GuardaZonas:
    """Deja en disco, por bloques (zonas-NNNN.npz), las zonas del dorsal ya estiradas a 32 × 128 con su tracklet, su
    equipo y lo que leyó el lector: el material de `entrenar-dorsales`. Las que parecen tener un número y, de las que no
    (de frente, de lado), una de cada `NEGATIVAS_CADA`: sin ellas el afinado olvida que ahí no hay número y lee alguno en
    todo, lo que reparte los votos de cada jugador entre números que no lleva."""

    PESO_MIN = 0.25
    PESO_NEGATIVA = 0.08
    NEGATIVAS_CADA = 3
    BLOQUE = 4096

    def __init__(self, carpeta: Path) -> None:
        self.carpeta = carpeta
        carpeta.mkdir(parents=True, exist_ok=True)
        self.bloques = len(list(carpeta.glob("zonas-*.npz")))
        self.imagenes: list[np.ndarray] = []
        self.datos: list[tuple[int, int, int, float]] = []
        self.guardadas = 0
        self.vistas_sin_numero = 0

    def poner(self, tracklet: int, equipo: int, zona: np.ndarray, lectura: "Lectura") -> None:
        peso = peso_lectura(lectura)
        if peso < self.PESO_MIN:
            if peso >= self.PESO_NEGATIVA:
                return
            self.vistas_sin_numero += 1
            if self.vistas_sin_numero % self.NEGATIVAS_CADA:
                return
        # Igual que la estira `preparar` (bicúbica): con otra interpolación el lector publicado acierta la mitad y el
        # afinado aprende de imágenes que no verá al analizar.
        self.imagenes.append(cv2.resize(zona, (ANCHO, ALTO), interpolation=cv2.INTER_CUBIC))
        self.datos.append((tracklet, equipo, lectura.mejor[0] if peso >= self.PESO_MIN else 0, peso))
        if len(self.imagenes) >= self.BLOQUE:
            self.vaciar()

    def vaciar(self) -> None:
        if not self.imagenes:
            return
        d = np.array(self.datos, dtype=np.float64)
        np.savez_compressed(
            self.carpeta / f"zonas-{self.bloques:04d}.npz",
            imagenes=np.stack(self.imagenes), tracklet=d[:, 0].astype(np.int64), equipo=d[:, 1].astype(np.int64),
            leido=d[:, 2].astype(np.int64), peso=d[:, 3].astype(np.float32),
        )
        self.bloques += 1
        self.guardadas += len(self.imagenes)
        self.imagenes, self.datos = [], []


class ColaDorsales:
    """Junta las zonas de los dorsales de varios fotogramas y las lee de golpe (en la GPU, por lotes)."""

    def __init__(self, lector: "LectorDorsales", lote: int = 512, guardar: GuardaZonas | None = None) -> None:
        self.lector = lector
        self.lote = lote
        self.guardar = guardar
        self.pendientes: list[tuple[int, np.ndarray, np.ndarray | None, int]] = []
        self.leidas = 0

    def poner(self, tracklet: int, zona: np.ndarray, recorte: np.ndarray | None, memorias, equipo: int = -1) -> None:
        """`equipo`: el que dice la equipación en este fotograma (la lectura solo cuenta para ese equipo)."""
        self.pendientes.append((tracklet, zona, recorte, equipo))
        if len(self.pendientes) >= self.lote:
            self.vaciar(memorias)

    def vaciar(self, memorias) -> None:
        if not self.pendientes:
            return
        lecturas = self.lector.leer([z for _, z, _, _ in self.pendientes])
        for (tracklet, zona, recorte, equipo), lectura in zip(self.pendientes, lecturas):
            memoria = memorias.get(tracklet)
            if memoria is not None:
                memoria.sumar_dorsal(lectura.probabilidades, peso_lectura(lectura), recorte, equipo)
            if self.guardar is not None:
                self.guardar.poner(tracklet, equipo, zona, lectura)
        self.leidas += len(self.pendientes)
        self.pendientes = []


class LectorDorsales:
    def __init__(self, ruta_pesos: Path, device: str = "cuda") -> None:
        import torch

        from .terceros.parseq.model import PARSeq
        from .terceros.parseq.tokenizer import Tokenizer

        self.torch = torch
        self.device = device
        self.nombre = "PARSeq"
        self.tokenizer = Tokenizer(CHARSET_PARSEQ)
        self.modelo = PARSeq(
            num_tokens=len(self.tokenizer), max_label_length=25, img_size=(ALTO, ANCHO), patch_size=(4, 8), embed_dim=384,
            enc_num_heads=6, enc_mlp_ratio=4, enc_depth=12, dec_num_heads=12, dec_mlp_ratio=4, dec_depth=1,
            decode_ar=True, refine_iters=1, dropout=0.1,
        )
        estado = torch.load(ruta_pesos, map_location="cpu")
        if isinstance(estado, dict) and "state_dict" in estado:
            estado = {k.removeprefix("model."): v for k, v in estado["state_dict"].items() if k.startswith("model.")}
        self.modelo.load_state_dict(estado)
        self.modelo.eval().to(device)
        if device.startswith("cuda"):
            self.modelo.half()
        self.digitos = [self.tokenizer.id_of(str(d)) for d in range(10)]

    def leer(self, zonas: list[np.ndarray], lote: int = 256) -> list[Lectura]:
        salida: list[Lectura] = []
        for i in range(0, len(zonas), lote):
            salida.extend(self._leer_lote(zonas[i : i + lote]))
        return salida

    def _leer_lote(self, zonas: list[np.ndarray]) -> list[Lectura]:
        torch = self.torch
        if not zonas:
            return []
        # En el tipo del modelo: media precisión al analizar en la GPU, completa mientras se afina.
        tensor = torch.from_numpy(np.stack([preparar(z) for z in zonas])).to(self.device, dtype=next(self.modelo.parameters()).dtype)
        tok = self.tokenizer
        with torch.no_grad():
            memoria = self.modelo.encode(tensor)
            b = memoria.shape[0]
            # Diez secuencias por imagen, [BOS, d] con d = 0..9: la posición 0 da la primera cifra y la 1, lo que va
            # después de d (otra cifra o el fin).
            tgt = torch.full((b * 10, 2), tok.bos_id, dtype=torch.long, device=self.device)
            tgt[:, 1] = torch.tensor(self.digitos, device=self.device).repeat(b)
            mem = memoria.repeat_interleave(10, dim=0)
            mascara = torch.triu(torch.ones((2, 2), dtype=torch.bool, device=self.device), 1)
            salida = self.modelo.decode(tgt, mem, mascara, tgt_query_mask=mascara)
            logits = self.modelo.head(salida).float()
            p0 = logits[:, 0].softmax(-1).view(b, 10, -1)[:, 0]
            p1 = logits[:, 1].softmax(-1).view(b, 10, -1)
        p0 = p0.cpu().numpy()
        p1 = p1.cpu().numpy()
        # La cabeza no predice BOS ni PAD: sus índices son los del tokenizador sin esos dos (EOS es el 0).
        eos = tok.eos_id
        resultados = []
        for k in range(b):
            primera = p0[k, self.digitos]
            probs = np.zeros(len(NUMEROS), dtype=np.float64)
            for d1 in range(10):
                tras = p1[k, d1]
                for n in NUMEROS:
                    if n < 10 and n == d1:
                        probs[n - 1] += primera[d1] * tras[eos]
                    elif n >= 10 and n // 10 == d1:
                        probs[n - 1] += primera[d1] * tras[self.digitos[n % 10]]
            total = float(probs.sum())
            resultados.append(Lectura(probs / total if total > 0 else probs, sin_numero=max(0.0, 1.0 - total)))
        return resultados


def ruta_lector(carpeta: Path, afinados: bool = True) -> Path | None:
    """Los pesos del lector: los afinados con partidos propios si existen (`entrenar-dorsales`); si no, los publicados."""
    afinado = carpeta / "afinados" / "dorsales.pt"
    if afinados and afinado.exists():
        return afinado
    publico = carpeta / PESOS_PARSEQ
    return publico if publico.exists() else None


def cargar_lector(carpeta: Path, device: str, afinados: bool = True) -> "LectorDorsales | None":
    ruta = ruta_lector(carpeta, afinados)
    if ruta is None:
        return None
    lector = LectorDorsales(ruta, device)
    lector.nombre = f"PARSeq ({ruta.relative_to(carpeta).as_posix()})"
    return lector
