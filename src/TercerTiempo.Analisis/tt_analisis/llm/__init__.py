"""Modelos de lenguaje con visión para lo que la cadena de visión no hace bien: confirmar eventos dudosos con unos
pocos fotogramas, leer dorsales en recortes y redactar el resumen. La API de Claude o un modelo local vía Ollama."""

from .base import ModeloLenguaje, crear_modelo

__all__ = ["ModeloLenguaje", "crear_modelo"]
