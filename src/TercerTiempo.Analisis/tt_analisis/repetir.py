"""Si falla la grabación misma de un grafo CUDA, el análisis se repite solo en un proceso limpio y sin grafos.

RF-DETR avisa de que una grabación fallida puede dejar el proceso tocado para la GPU (el estado de los números aleatorios
de CUDA), así que el análisis no sigue en él. El proceso que falló se queda de puente: lanza el mismo análisis con
`--sin-grafos` en otro proceso, le pasa las órdenes de pausa que le lleguen por la entrada estándar
(`avance.Avance.reenviar`), lo deja escribir en su misma salida (el avance y los hitos le llegan igual a quien lanzó el
análisis) y termina con su mismo código de salida. Sin grafos el resultado es el mismo, solo más lento.

Para cancelar no hace falta nada: el servicio de vídeo mata el árbol de procesos entero y el nuevo cae con el puente. Si
el puente se para por otra cosa (Ctrl+C), mata al nuevo antes de salir."""

from __future__ import annotations

import gc
import os
import subprocess
import sys

# En el entorno del proceso que repite: por qué se repite (lo guarda analisis.json en `rendimiento.sin_grafos_por`).
VARIABLE = "TT_ANALISIS_SIN_GRAFOS_POR"
SIN_GRAFOS = "--sin-grafos"
LARGO_MAXIMO = 500

DE_QUE = {"jugadores": "de los jugadores", "balon": "del balón", "campo": "del campo"}


class CapturaFallida(RuntimeError):
    """Falló la grabación de un grafo CUDA (`rfdetr_comun.grabar`) y el proceso puede haber quedado tocado para la GPU.
    `detector` lo pone `Modelos.grabar`; `ordenes`, el análisis: quien lee las órdenes de la entrada estándar en este
    proceso (`avance.Avance`), para pasárselas al proceso que repite."""

    def __init__(self, detalle: str, detector: str | None = None):
        super().__init__(detalle)
        self.detalle = detalle
        self.detector = detector
        self.ordenes = None

    @property
    def motivo(self) -> str:
        """El porqué técnico en una línea, para el proceso que repite."""
        return " ".join(f"{self.detector or 'grafo'}: {self.detalle}".split())[:LARGO_MAXIMO]

    @property
    def aviso(self) -> str:
        de = f" {DE_QUE[self.detector]}" if self.detector in DE_QUE else ""
        return f"Aviso: falló la grabación del grafo CUDA{de}. El análisis se repite en un proceso limpio, sin grafos: el mismo resultado, más lento."

    def __str__(self) -> str:
        return self.motivo


def describir(error: BaseException) -> str:
    """El error y lo que lo causó, en una línea: RF-DETR envuelve el de CUDA en uno suyo."""
    partes: list[str] = []
    visto: BaseException | None = error
    while visto is not None and len(partes) < 3:
        partes.append(f"{type(visto).__name__}: {visto}")
        visto = visto.__cause__
    return " ← ".join(partes)


def motivo_de_la_repeticion() -> str | None:
    """Si este proceso repite un análisis porque falló la grabación de un grafo, por qué."""
    return os.environ.get(VARIABLE) or None


def orden_sin_grafos(argumentos: list[str]) -> list[str]:
    extra = [] if SIN_GRAFOS in argumentos else [SIN_GRAFOS]
    return [sys.executable, "-m", "tt_analisis", *argumentos, *extra]


def liberar_gpu() -> None:
    """Suelta lo que se pueda de la memoria de la GPU de este proceso, que la necesita el nuevo. Si CUDA quedó tocado y no
    se puede, se queda: el nuevo cabe igual."""
    gc.collect()
    try:
        import torch

        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except Exception:  # noqa: BLE001
        pass


def repetir_sin_grafos(argumentos: list[str], motivo: str, ordenes=None, lanzar=subprocess.Popen) -> int:
    """Lanza el mismo análisis (`argumentos`: los de la línea de órdenes) con `--sin-grafos` en un proceso nuevo, le pasa
    las órdenes si este proceso las estaba leyendo (`ordenes`, un `avance.Avance`) y devuelve su código de salida."""
    liberar_gpu()
    escucha = ordenes is not None and ordenes.escucha
    sys.stdout.flush()
    hijo = lanzar(
        orden_sin_grafos(argumentos),
        stdin=subprocess.PIPE if escucha else None,
        env={**os.environ, VARIABLE: motivo},
        text=True,
        encoding="utf-8",
    )
    print(f"Falló la grabación de un grafo CUDA ({motivo}). Se repite sin grafos en el proceso {hijo.pid}.", file=sys.stderr, flush=True)
    if escucha:
        ordenes.reenviar(hijo.stdin)
    try:
        return hijo.wait()
    except BaseException:
        hijo.kill()
        raise
