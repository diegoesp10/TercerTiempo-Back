"""Ningún paquete del analizador con licencia de pago o restrictiva (26-11-2026): todo tiene que poder publicarse y
ofrecerse como servicio sin pagar ni pedir permiso. Fuera TensorRT (propietario: su licencia limita ofrecerlo como
servicio por internet), Ultralytics (AGPL-3.0) y los modelos de `rfdetr[plus]` (PML 1.0, de pago en servidor propio)."""

import tomllib
from pathlib import Path

RAIZ = Path(__file__).resolve().parents[1]
PROHIBIDOS = ("tensorrt", "ultralytics", "rfdetr-plus")


def test_ningun_paquete_del_entorno_es_de_los_prohibidos():
    bloqueo = tomllib.loads((RAIZ / "uv.lock").read_text(encoding="utf-8"))
    nombres = sorted(paquete["name"] for paquete in bloqueo["package"])
    assert [nombre for nombre in nombres if nombre.startswith(PROHIBIDOS)] == []
