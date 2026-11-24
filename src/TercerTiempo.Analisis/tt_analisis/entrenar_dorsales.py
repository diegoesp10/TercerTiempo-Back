"""Afinado del lector de dorsales con partidos propios.

El material son las zonas del dorsal que guarda `analizar --guardar-dorsales` y sus etiquetas: el dorsal de cada
tracklet identificado (las lecturas de todos sus fotogramas sumadas y cruzadas con el acta oficial). Así el lector aprende
de los fotogramas en los que leyó mal un número que se ve, porque la etiqueta sale del resto de lecturas del mismo
jugador y del acta, no de esa lectura suelta. Solo cuentan los tracklets en los que la identidad es clara (la mayoría
de sus lecturas dicen ese número) y las zonas en las que el lector veía un número: las que leía bien y, de las que leía
mal, solo las que veía claras (en una zona borrosa la etiqueta enseña a adivinar el número del jugador, no a leerlo).
Además entran zonas sin número (las que el lector publicado veía de frente) con la respuesta vacía, para que no
olvide que ahí no hay número. Y las de los porteros (`analizador.guardar_zonas_porteros`): su espalda con su número y
su delantera sin número, porque el escudo del pecho se leía como un número y a él casi nunca se le ve de espaldas;
los números con pocas zonas se repiten hasta `minimo_por_dorsal` para que cuenten.

Se separa una parte de los tracklets para validar (nunca se entrena con ellos) y se compara el acierto del lector
publicado con el del afinado sobre esa parte. Los pesos quedan en modelos/afinados/dorsales.pt, que el analizador usa
si existen (salvo con --sin-afinados)."""

from __future__ import annotations

import json
import random
import time
from pathlib import Path

import cv2
import numpy as np

from .dorsales import ALTO, ANCHO, PESOS_PARSEQ, LectorDorsales

# Qué zonas valen para entrenar: identidad con la mayoría de sus lecturas en su número, tracklet que lo dice al menos
# en esta parte y zona en la que el lector veía un número.
CONFIANZA_MIN = 0.45
PARTE_TRACKLET_MIN = 0.35
PESO_MIN = 0.3
PESO_LEGIBLE = 0.6
PESO_NEGATIVA = 0.08
PARTE_NEGATIVAS = 0.3
PARTE_VALIDACION = 0.12
# Un dorsal con una o dos zonas (que pueden estar mal etiquetadas) no se repite tanto como para pesar como uno que se ve
# cientos de veces: como mucho tantas veces cada zona (en el J8, el 1 del portero, el que más, ×42).
REPETICIONES_MAX = 50


def cargar(carpetas: list[Path]) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """Las zonas etiquetadas de todas las carpetas: imágenes, dorsal (0: sin número), grupo (tracklet, para separar la
    validación) y lo que leyó el lector publicado."""
    imagenes, dorsales, grupos, leidos = [], [], [], []
    for n, carpeta in enumerate(carpetas):
        etiquetas = json.loads((carpeta / "etiquetas.json").read_text(encoding="utf-8"))["tracklets"]
        for bloque in sorted(carpeta.glob("zonas-*.npz")):
            # Cada clave de un npz se descomprime entera al leerla: una vez por bloque, no por zona.
            with np.load(bloque) as z:
                zonas, tracklets, equipos, pesos, leido = z["imagenes"], z["tracklet"], z["equipo"], z["peso"], z["leido"]
            for i in range(len(tracklets)):
                peso = float(pesos[i])
                if peso < PESO_NEGATIVA:
                    imagenes.append(zonas[i])
                    dorsales.append(0)
                    grupos.append(n * 10_000_000 + int(tracklets[i]))
                    leidos.append(0)
                    continue
                e = etiquetas.get(str(int(tracklets[i])))
                if e is None or e["equipo"] != int(equipos[i]):
                    continue
                if e["confianza"] < CONFIANZA_MIN or e["parte_tracklet"] < PARTE_TRACKLET_MIN or peso < PESO_MIN:
                    continue
                if int(leido[i]) != int(e["dorsal"]) and peso < PESO_LEGIBLE:
                    continue
                imagenes.append(zonas[i])
                dorsales.append(int(e["dorsal"]))
                grupos.append(n * 10_000_000 + int(tracklets[i]))
                leidos.append(int(leido[i]))
    if not imagenes:
        return np.zeros((0, ALTO, ANCHO, 3), np.uint8), np.zeros(0, int), np.zeros(0, int), np.zeros(0, int)
    return np.stack(imagenes), np.array(dorsales), np.array(grupos), np.array(leidos)


def elegir_entreno(dorsales: np.ndarray, indices: list[int], rnd: random.Random, maximo_por_dorsal: int, minimo_por_dorsal: int) -> tuple[list[int], dict[int, int]]:
    """Las zonas de cada época: como mucho `maximo_por_dorsal` de cada número, para que no manden los que más se ven, y al
    menos `minimo_por_dorsal`, repitiendo (cada vez con otra variación, y cada zona como mucho REPETICIONES_MAX veces)
    las de los que casi nunca se ven de espaldas, como el del portero: unas pocas zonas entre miles no se aprenden. Y sin
    número, al azar, PARTE_NEGATIVAS del total.
    Devuelve también cuántas veces va cada dorsal repetido."""
    por_dorsal: dict[int, list[int]] = {}
    for i in indices:
        por_dorsal.setdefault(int(dorsales[i]), []).append(i)
    positivos: list[int] = []
    repetidos: dict[int, int] = {}
    for d, lista in sorted(por_dorsal.items()):
        if d == 0:
            continue
        elegidos = rnd.sample(lista, min(len(lista), maximo_por_dorsal))
        veces = min(REPETICIONES_MAX, max(1, minimo_por_dorsal // len(elegidos)))
        if veces > 1:
            repetidos[d] = veces
        positivos += elegidos * veces
    tope = int(len(positivos) * PARTE_NEGATIVAS / (1 - PARTE_NEGATIVAS))
    negativas = por_dorsal.get(0, [])
    return positivos + rnd.sample(negativas, min(len(negativas), tope)), repetidos


def aumentar(img: np.ndarray, rnd: random.Random) -> np.ndarray:
    """Variaciones de lo que cambia de un fotograma a otro: luz, desenfoque, encuadre de la zona y un leve giro."""
    out = img.astype(np.float32)
    out = out * rnd.uniform(0.7, 1.3) + rnd.uniform(-25, 25)
    if rnd.random() < 0.3:
        out = 255 - out if rnd.random() < 0.15 else out
    out = np.clip(out, 0, 255).astype(np.uint8)
    if rnd.random() < 0.4:
        k = rnd.choice([3, 5])
        out = cv2.GaussianBlur(out, (k, k), 0)
    angulo = rnd.uniform(-6, 6)
    escala = rnd.uniform(0.85, 1.12)
    m = cv2.getRotationMatrix2D((ANCHO / 2, ALTO / 2), angulo, escala)
    m[0, 2] += rnd.uniform(-8, 8)
    m[1, 2] += rnd.uniform(-2.5, 2.5)
    return cv2.warpAffine(out, m, (ANCHO, ALTO), borderMode=cv2.BORDER_REPLICATE)


def a_tensor(imgs: list[np.ndarray], torch, device):
    rgb = np.stack([cv2.cvtColor(i, cv2.COLOR_BGR2RGB) for i in imgs]).astype(np.float32) / 255.0
    return torch.from_numpy(((rgb - 0.5) / 0.5).transpose(0, 3, 1, 2)).to(device)


def acierto(lector: LectorDorsales, imagenes: np.ndarray, dorsales: np.ndarray) -> tuple[float, float, float]:
    """Acierto en las zonas con número (lee ese número y cree que hay número), en las sin número (cree que no lo hay) y
    peso medio que da a las sin número (lo que meterían de ruido en los votos de un jugador)."""
    if len(imagenes) == 0:
        return 0.0, 0.0, 0.0
    lecturas = lector.leer(list(imagenes))
    con = [l.mejor[0] == d and l.sin_numero < 0.5 for l, d in zip(lecturas, dorsales) if d > 0]
    sin = [(l.sin_numero >= 0.5, 1.0 - l.sin_numero) for l, d in zip(lecturas, dorsales) if d == 0]
    return (float(np.mean(con)) if con else 0.0, float(np.mean([s[0] for s in sin])) if sin else 1.0,
            float(np.mean([s[1] for s in sin])) if sin else 0.0)


def nota(a: tuple[float, float, float]) -> float:
    # Las dos cosas a la vez: leer el número que hay y no inventarlo donde no lo hay.
    return min(a[0], a[1])


def texto(a: tuple[float, float, float]) -> str:
    return f"con número {100 * a[0]:.1f} % bien leídos, sin número {100 * a[1]:.1f} % (peso medio {a[2]:.2f})"


def entrenar_dorsales(carpetas: list[Path], modelos: Path, epocas: int = 4, lote: int = 128, lr: float = 3e-5, maximo_por_dorsal: int = 2500, device: str = "cuda", semilla: int = 7,
                      minimo_por_dorsal: int = 300) -> int:
    import torch

    rnd = random.Random(semilla)
    imagenes, dorsales, grupos, leidos = cargar(carpetas)
    if len(imagenes) < 200:
        print(f"Muy pocas zonas etiquetadas ({len(imagenes)}): analiza más vídeo con --guardar-dorsales y el acta oficial.")
        return 1

    unicos = sorted(set(grupos.tolist()))
    rnd.shuffle(unicos)
    validacion = set(unicos[: max(1, int(len(unicos) * PARTE_VALIDACION))])
    en_val = np.array([g in validacion for g in grupos])
    entreno, repetidos = elegir_entreno(dorsales, np.where(~en_val)[0].tolist(), rnd, maximo_por_dorsal, minimo_por_dorsal)
    sin_numero = sum(1 for i in entreno if dorsales[i] == 0)
    val_img, val_d = imagenes[en_val], dorsales[en_val]
    con = dorsales > 0
    print(f"Zonas etiquetadas: {int(con.sum())} con número de {len(set(dorsales[con].tolist()))} dorsales y {int((~con).sum())} sin número, "
          f"de {len(unicos)} tracklets; entreno {len(entreno)} ({sin_numero} sin número), validación {len(val_img)} ({len(validacion)} tracklets).")
    if repetidos:
        print("Repetidos por verse poco: " + ", ".join(f"el {d} ×{v}" for d, v in repetidos.items()) + ".")
    if not (~con).any():
        print("Aviso: no hay zonas sin número (guardadas con una versión anterior): el afinado tenderá a leer un número en todo.")
    print(f"El lector publicado ya coincidía con la etiqueta en el {100 * float(np.mean(leidos[con] == dorsales[con])):.1f} % de las zonas con número.")

    lector = LectorDorsales(modelos / PESOS_PARSEQ, device)
    modelo = lector.modelo.float()
    base = acierto(lector, val_img, val_d)
    print(f"Validación con el lector publicado: {texto(base)}.")

    tok = lector.tokenizer
    optimizador = torch.optim.AdamW(modelo.parameters(), lr=lr, weight_decay=0.01)
    pasos = epocas * max(1, len(entreno) // lote)
    planificador = torch.optim.lr_scheduler.OneCycleLR(optimizador, max_lr=lr, total_steps=pasos, pct_start=0.1)
    perdida_fn = torch.nn.CrossEntropyLoss(ignore_index=tok.pad_id)
    mejor, mejor_estado = nota(base), None
    mejor_acierto = base
    for epoca in range(epocas):
        modelo.train()
        rnd.shuffle(entreno)
        t0, total, n = time.time(), 0.0, 0
        for k in range(0, len(entreno) - lote + 1, lote):
            idx = entreno[k : k + lote]
            x = a_tensor([aumentar(imagenes[i], rnd) for i in idx], torch, device)
            tgt = tok.encode([str(int(dorsales[i])) if dorsales[i] > 0 else "" for i in idx], torch.device(device))
            tgt_in, tgt_out = tgt[:, :-1], tgt[:, 1:]
            largo = tgt_in.shape[1]
            mascara = torch.triu(torch.ones((largo, largo), dtype=torch.bool, device=device), 1)
            relleno = (tgt_in == tok.pad_id) | (tgt_in == tok.eos_id)
            memoria = modelo.encode(x)
            salida = modelo.decode(tgt_in, memoria, mascara, relleno, tgt_query_mask=mascara)
            logits = modelo.head(salida)
            perdida = perdida_fn(logits.flatten(end_dim=1), tgt_out.flatten())
            optimizador.zero_grad(set_to_none=True)
            perdida.backward()
            torch.nn.utils.clip_grad_norm_(modelo.parameters(), 1.0)
            optimizador.step()
            planificador.step()
            total += float(perdida)
            n += 1
        modelo.eval()
        val = acierto(lector, val_img, val_d)
        print(f"Época {epoca + 1}/{epocas}: pérdida {total / max(1, n):.3f}, validación {texto(val)} ({time.time() - t0:.0f} s).")
        if nota(val) > mejor:
            mejor, mejor_acierto, mejor_estado = nota(val), val, {k: v.detach().cpu().clone() for k, v in modelo.state_dict().items()}

    if mejor_estado is None:
        print("El afinado no mejora al lector publicado: no se guarda nada.")
        return 1
    destino = modelos / "afinados" / "dorsales.pt"
    destino.parent.mkdir(parents=True, exist_ok=True)
    torch.save(mejor_estado, destino)
    destino.with_suffix(".json").write_text(json.dumps({
        "carpetas": [str(c) for c in carpetas], "zonas": len(imagenes), "entreno": len(entreno), "validacion": len(val_img),
        "acierto_publicado": {"con_numero": round(base[0], 4), "sin_numero": round(base[1], 4), "peso_sin_numero": round(base[2], 4)},
        "acierto_afinado": {"con_numero": round(mejor_acierto[0], 4), "sin_numero": round(mejor_acierto[1], 4), "peso_sin_numero": round(mejor_acierto[2], 4)},
        "epocas": epocas, "lr": lr, "minimo_por_dorsal": minimo_por_dorsal,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Guardado {destino}: validación de «{texto(base)}» a «{texto(mejor_acierto)}».")
    return 0
