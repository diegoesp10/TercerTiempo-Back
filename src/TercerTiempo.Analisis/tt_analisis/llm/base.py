from __future__ import annotations

from typing import Protocol


class ModeloLenguaje(Protocol):
    nombre: str

    def preguntar_json(self, imagenes: list[bytes], instrucciones: str, esquema: dict) -> dict:
        """Manda imágenes JPEG y una pregunta; devuelve el JSON que pide `esquema` (o {"error": ...})."""

    def redactar(self, instrucciones: str) -> str:
        """Texto libre (Markdown) a partir de unas instrucciones."""


def crear_modelo(tipo: str, modelo: str | None = None) -> ModeloLenguaje | None:
    tipo = (tipo or "none").lower()
    if tipo == "none":
        return None
    if tipo == "claude":
        from .claude import ClienteClaude

        return ClienteClaude(modelo or "claude-opus-5-5")
    if tipo == "ollama":
        from .ollama import ClienteOllama

        return ClienteOllama(modelo or "gemma4:12b")
    raise ValueError(f"Modelo de lenguaje desconocido: {tipo} (claude, ollama o none)")
