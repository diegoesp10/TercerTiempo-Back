"""Segunda capa: con los candidatos ya en la mano, el modelo de lenguaje mira unos pocos fotogramas y confirma o
descarta, lee dorsales en los mejores recortes y redacta el resumen. Las funciones reciben cómo obtener fotogramas,
así se prueban sin vídeo."""

from __future__ import annotations

import json
from typing import Callable

import numpy as np

from ..tipos import Evento, formato_minuto
from ..video import a_jpeg
from .base import ModeloLenguaje

TIPOS_REVISABLES = ("gol", "tiro", "parada", "corner", "saque_puerta")

ESQUEMA_EVENTO = {
    "type": "object",
    "properties": {
        "veredicto": {"type": "string", "enum": ["si", "no", "dudoso"]},
        "tipo_real": {"type": "string"},
        "explicacion": {"type": "string"},
    },
    "required": ["veredicto", "tipo_real", "explicacion"],
    "additionalProperties": False,
}

ESQUEMA_DORSAL = {
    "type": "object",
    "properties": {
        "dorsal": {"type": ["integer", "null"]},
        "confianza": {"type": "number"},
        "color_camiseta": {"type": "string"},
    },
    "required": ["dorsal", "confianza", "color_camiseta"],
    "additionalProperties": False,
}

DESCRIPCION_TIPO = {
    "gol": "un gol (el balón entra en la portería)",
    "tiro": "un tiro a portería",
    "parada": "una parada del portero",
    "corner": "un saque de esquina",
    "saque_puerta": "un saque de puerta",
}


def confirmar_eventos(eventos: list[Evento], leer_fotograma: Callable[[float], np.ndarray | None], llm: ModeloLenguaje, max_eventos: int = 40, avance=None) -> int:
    """Pregunta por los eventos revisables (los de más confianza primero) y ajusta su confianza con el veredicto."""
    candidatos = [e for e in eventos if e.tipo in TIPOS_REVISABLES]
    candidatos.sort(key=lambda e: (e.tipo != "gol", -e.confianza))
    revisados = 0
    for evento in candidatos[:max_eventos]:
        imagenes = []
        for desfase in (-1.2, 0.0, 1.2):
            imagen = leer_fotograma(evento.t + desfase)
            if imagen is not None:
                imagenes.append(a_jpeg(imagen, calidad=85, lado_max=1280))
        if not imagenes:
            continue
        instrucciones = (
            f"Son {len(imagenes)} fotogramas seguidos (1,2 s entre ellos) de un partido de fútbol amateur grabado con una cámara fija, "
            f"en el minuto {formato_minuto(evento.t)}. Un programa de visión por computador cree que aquí hay {DESCRIPCION_TIPO.get(evento.tipo, evento.tipo)}"
            f"{' (' + evento.detalle + ')' if evento.detalle else ''}. "
            "Mira el balón, la portería, los jugadores y el árbitro y contesta en JSON: veredicto 'si' si se ve claramente, 'no' si se ve que no es eso, "
            "'dudoso' si no se puede saber con estos fotogramas; tipo_real con lo que de verdad parece pasar (gol, tiro, despeje, corner, saque de puerta, "
            "falta, nada…) y una explicación de una frase en español."
        )
        respuesta = llm.preguntar_json(imagenes, instrucciones, ESQUEMA_EVENTO)
        revisados += 1
        if avance:
            avance(revisados)
        if "error" in respuesta:
            evento.veredicto_llm = f"error: {respuesta['error']}"
            continue
        veredicto = respuesta.get("veredicto", "dudoso")
        evento.veredicto_llm = f"{veredicto} · {respuesta.get('tipo_real', '')} · {respuesta.get('explicacion', '')}".strip(" ·")
        if veredicto == "si":
            evento.confianza = round(min(0.95, evento.confianza + 0.3), 2)
        elif veredicto == "no":
            evento.confianza = round(min(evento.confianza, 0.15), 2)
    return revisados


def leer_dorsales(recortes_por_tracklet: dict[int, list[np.ndarray]], llm: ModeloLenguaje, max_tracklets: int = 40, avance=None) -> dict[int, dict]:
    """Para cada jugador seguido, los mejores recortes → dorsal (o null) y color de camiseta."""
    salida: dict[int, dict] = {}
    for n, (tracklet, recortes) in enumerate(list(recortes_por_tracklet.items())[:max_tracklets]):
        imagenes = [a_jpeg(r, calidad=92, lado_max=512) for r in recortes[:3] if r is not None and r.size]
        if not imagenes:
            continue
        instrucciones = (
            f"Son {len(imagenes)} recortes del mismo jugador de fútbol tomados de un vídeo. Si se le ve el número del dorsal en la camiseta, "
            "devuélvelo en 'dorsal' (entero) con una confianza de 0 a 1; si no se ve ningún número, dorsal null y confianza 0. "
            "En 'color_camiseta' el color principal de la camiseta, en español."
        )
        respuesta = llm.preguntar_json(imagenes, instrucciones, ESQUEMA_DORSAL)
        if avance:
            avance(n + 1)
        if "error" in respuesta:
            continue
        salida[tracklet] = {"dorsal": respuesta.get("dorsal"), "confianza": float(respuesta.get("confianza", 0) or 0), "color_camiseta": respuesta.get("color_camiseta", "")}
    return salida


def redactar_resumen(analisis: dict, llm: ModeloLenguaje) -> str:
    """Un resumen en Markdown para el cuerpo técnico, a partir del JSON del análisis (sin los mapas ni las posiciones)."""
    compacto = {k: v for k, v in analisis.items() if k not in ("tracklets", "parametros")}
    compacto["jugadores_destacados"] = [
        {k: v for k, v in t.items() if k != "mapa_calor"}
        for t in sorted(analisis.get("tracklets", []), key=lambda t: -t.get("segundos_a_la_vista", 0))[:16]
    ]
    instrucciones = (
        "Eres el analista de un equipo de fútbol amateur asturiano. A partir de este análisis automático del vídeo del partido (JSON), escribe en español "
        "un resumen en Markdown para el cuerpo técnico: cómo fue la posesión por mitades y por zonas, las ocasiones y goles detectados (con su minuto y "
        "avisando de que son candidatos que hay que confirmar), qué jugadores seguidos (tracklets) destacan por distancia, recuperaciones o pérdidas, y "
        "tres o cuatro ideas para el siguiente entrenamiento. Sé concreto y breve (menos de 500 palabras), sin inventar nada que no esté en los datos. "
        "Los jugadores aparecen como tracklets numerados porque el programa no sabe quién es quién.\n\n"
        + json.dumps(compacto, ensure_ascii=False)
    )
    return llm.redactar(instrucciones)
