"""Un modelo local con visión servido por Ollama (http://localhost:11434): el vídeo no sale del equipo."""

from __future__ import annotations

import base64
import json
import os

import httpx


class ClienteOllama:
    def __init__(self, modelo: str = "gemma4:12b", base_url: str | None = None) -> None:
        self.nombre = f"ollama:{modelo}"
        self.modelo = modelo
        self.base_url = (base_url or os.environ.get("OLLAMA_HOST") or "http://localhost:11434").rstrip("/")
        self.http = httpx.Client(timeout=httpx.Timeout(600.0, connect=10.0))

    def preguntar_json(self, imagenes: list[bytes], instrucciones: str, esquema: dict) -> dict:
        mensaje = {"role": "user", "content": instrucciones, "images": [base64.standard_b64encode(img).decode("ascii") for img in imagenes]}
        cuerpo = {"model": self.modelo, "messages": [mensaje], "format": esquema, "stream": False, "options": {"temperature": 0}}
        try:
            respuesta = self.http.post(f"{self.base_url}/api/chat", json=cuerpo)
            respuesta.raise_for_status()
        except httpx.HTTPError as e:
            return {"error": f"ollama: {e}"}
        texto = respuesta.json().get("message", {}).get("content", "")
        try:
            return json.loads(texto)
        except json.JSONDecodeError:
            return {"error": "json", "texto": texto}

    def redactar(self, instrucciones: str) -> str:
        cuerpo = {"model": self.modelo, "messages": [{"role": "user", "content": instrucciones}], "stream": False}
        try:
            respuesta = self.http.post(f"{self.base_url}/api/chat", json=cuerpo)
            respuesta.raise_for_status()
        except httpx.HTTPError:
            return ""
        return respuesta.json().get("message", {}).get("content", "")
