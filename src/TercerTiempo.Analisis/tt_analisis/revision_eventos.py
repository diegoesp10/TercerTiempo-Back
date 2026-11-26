"""Etiquetas humanas del vídeo, independientes de las propuestas de Ojeador."""

from __future__ import annotations

import hashlib
import json
import math
import sqlite3
from contextlib import closing
from pathlib import Path
from uuid import uuid4

TIPOS = {"tiro": "Tiro", "saque_puerta": "Saque de puerta", "parada": "Parada"}
FORMATO = 1
MARGEN_S = 2.0


class ConflictoRevision(ValueError):
    pass


def huella_video(ruta: Path) -> str:
    """Identifica el archivo por su tamaño y muestras de bytes, aunque se mueva de carpeta."""
    with ruta.open("rb") as archivo:
        tamano = archivo.seek(0, 2)
        huella = hashlib.sha256(str(tamano).encode("ascii"))
        for offset in (0, max(0, tamano // 2 - 32768), max(0, tamano - 65536)):
            archivo.seek(offset)
            huella.update(archivo.read(65536))
    return huella.hexdigest()


def _json(valor) -> str:
    return json.dumps(valor, ensure_ascii=False, allow_nan=False, sort_keys=True, separators=(",", ":"))


def _segundo(valor) -> float:
    if type(valor) not in (int, float) or not 0 <= valor <= 86400 or not math.isfinite(valor):
        raise ValueError("Segundo del vídeo no válido.")
    return round(valor, 3)


def fuente_revision(final: dict) -> dict:
    video = final["video"]
    desde, hasta = _segundo(video["tramo_inicio_s"]), _segundo(video["tramo_fin_s"])
    candidatos = [
        {"id": i, "tipo": e["tipo"], "t": _segundo(e["t"]), "equipo": e.get("equipo"),
         "detalle": e.get("detalle") or "", "confianza": e.get("confianza"), "tracklet": e.get("tracklet")}
        for i, e in enumerate(final["eventos"])
        if e.get("tipo") in TIPOS and desde <= e["t"] <= hasta
    ]
    candidatos.sort(key=lambda e: e["t"])
    analizador = (final.get("analizador") or {}).get("version")
    clave = hashlib.sha256(_json([desde, hasta, analizador, candidatos]).encode("utf-8")).hexdigest()
    return {"pasada": clave, "version": analizador, "desde": desde, "hasta": hasta, "candidatos": candidatos}


class Revisiones:
    """Un documento por vídeo. La revisión de edición impide pisar cambios de otra ventana."""

    def __init__(self, ruta: Path) -> None:
        self.ruta = ruta

    def _conectar(self):
        self.ruta.parent.mkdir(parents=True, exist_ok=True)
        conexion = sqlite3.connect(self.ruta)
        try:
            conexion.execute("CREATE TABLE IF NOT EXISTS revisiones (video TEXT PRIMARY KEY, datos TEXT NOT NULL)")
        except BaseException:
            conexion.close()
            raise
        return conexion

    @staticmethod
    def _leer(conexion, video: str) -> dict:
        fila = conexion.execute("SELECT datos FROM revisiones WHERE video = ?", (video,)).fetchone()
        if fila:
            return json.loads(fila[0])
        return {"formato": FORMATO, "video": video, "rev": 0, "hechos": [], "tramos": [], "decisiones": {}}

    def leer(self, video: str) -> dict:
        with closing(self._conectar()) as conexion:
            return self._leer(conexion, video)

    def editar(self, video: str, fuente: dict, orden: dict) -> dict:
        if not isinstance(orden, dict):
            raise ValueError("La revisión no es válida.")
        if orden.get("pasada") != fuente["pasada"] or orden.get("video") != video:
            raise ConflictoRevision("Cambió el vídeo o el análisis. Vuelve a abrirlo.")
        with closing(self._conectar()) as conexion, conexion:
            conexion.execute("BEGIN IMMEDIATE")
            datos = self._leer(conexion, video)
            if type(orden.get("rev")) is not int or orden["rev"] != datos["rev"]:
                raise ConflictoRevision("Hay cambios en otra ventana. Actualiza la revisión.")
            _editar(datos, fuente, orden)
            datos["rev"] += 1
            conexion.execute("INSERT INTO revisiones VALUES (?, ?) ON CONFLICT(video) DO UPDATE SET datos=excluded.datos", (video, _json(datos)))
            return datos


def _tipo(orden):
    tipo = orden.get("tipo")
    if not isinstance(tipo, str) or tipo not in TIPOS:
        raise ValueError("Tipo de acción no válido.")
    return tipo


def _en_tramo(fuente, valor):
    segundo = _segundo(valor)
    if not fuente["desde"] <= segundo <= fuente["hasta"]:
        raise ValueError("Ese segundo queda fuera del tramo analizado.")
    return segundo


def _hecho(datos, tipo, t, nota, origen):
    hecho = {"id": uuid4().hex, "tipo": tipo, "t": t, "nota": nota, "origen": origen}
    datos["hechos"].append(hecho)
    return hecho["id"]


def _decisiones(datos):
    return (d for pasada in datos["decisiones"].values() for d in pasada["marcas"].values())


def _editar(datos, fuente, orden):
    accion = orden.get("accion")
    nota = orden.get("nota", "")
    if not isinstance(nota, str) or len(nota) > 500:
        raise ValueError("La nota puede tener hasta 500 caracteres.")
    nota = nota.strip()
    if accion == "decidir":
        indice, estado = orden.get("id"), orden.get("estado")
        evento = next((e for e in fuente["candidatos"] if type(indice) is int and e["id"] == indice), None)
        if evento is None or estado not in ("real", "falsa", "dudosa", "pendiente"):
            raise ValueError("Candidato o veredicto no válido.")
        marcas = datos["decisiones"].setdefault(fuente["pasada"], {"version": fuente["version"], "marcas": {}})["marcas"]
        anterior = marcas.pop(str(indice), {}).get("hecho")
        if estado != "pendiente":
            hecho = None
            if estado == "real":
                cercanos = [h for h in datos["hechos"] if h["tipo"] == evento["tipo"] and abs(h["t"] - evento["t"]) <= MARGEN_S]
                hecho = min(cercanos, key=lambda h: abs(h["t"] - evento["t"]))["id"] if cercanos else _hecho(datos, evento["tipo"], evento["t"], nota, "confirmada")
            marcas[str(indice)] = {"estado": estado, "evento": evento, "nota": nota, "hecho": hecho}
        if anterior and not any(d["hecho"] == anterior for d in _decisiones(datos)):
            datos["hechos"] = [h for h in datos["hechos"] if h["id"] != anterior or h["origen"] == "manual"]
    elif accion == "anotar":
        tipo, t = _tipo(orden), _en_tramo(fuente, orden.get("t"))
        if any(h["tipo"] == tipo and h["t"] == t for h in datos["hechos"]):
            raise ValueError("Esa acción ya está anotada en ese segundo.")
        _hecho(datos, tipo, t, nota, "manual")
    elif accion == "cubrir":
        tipo = _tipo(orden)
        desde, hasta = (_en_tramo(fuente, orden.get(k)) for k in ("desde", "hasta"))
        if desde >= hasta:
            raise ValueError("El final del tramo debe ir después del inicio.")
        datos["tramos"].append({"id": uuid4().hex, "tipo": tipo, "desde": desde, "hasta": hasta})
    elif accion in ("quitar_hecho", "quitar_tramo"):
        clave = "hechos" if accion == "quitar_hecho" else "tramos"
        ident = orden.get("id")
        if not any(fila["id"] == ident for fila in datos[clave]):
            raise ValueError("Esa anotación ya no existe.")
        datos[clave] = [fila for fila in datos[clave] if fila["id"] != ident]
        if clave == "hechos":
            for pasada in datos["decisiones"].values():
                pasada["marcas"] = {i: d for i, d in pasada["marcas"].items() if d["hecho"] != ident}
    else:
        raise ValueError("Operación de revisión no válida.")
