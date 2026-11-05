"""La API de Claude (SDK oficial `anthropic`): imágenes en base64 + salida estructurada en JSON.

La clave se lee de ANTHROPIC_API_KEY (o del perfil de `ant auth login`); nunca va en código ni en archivos del
proyecto. Se activa el respaldo del servidor ante un rechazo de seguridad (`fallbacks: "default"`): si el modelo
declina una petición, la API la reintenta con otro modelo de la familia en la misma llamada."""

from __future__ import annotations

import base64
import json


class ClienteClaude:
    def __init__(self, modelo: str = "claude-opus-5-5") -> None:
        import anthropic

        self.nombre = f"claude:{modelo}"
        self.modelo = modelo
        self.client = anthropic.Anthropic()

    def preguntar_json(self, imagenes: list[bytes], instrucciones: str, esquema: dict) -> dict:
        contenido = [
            {"type": "image", "source": {"type": "base64", "media_type": "image/jpeg", "data": base64.standard_b64encode(img).decode("ascii")}}
            for img in imagenes
        ]
        contenido.append({"type": "text", "text": instrucciones})
        respuesta = self._crear(
            max_tokens=4096,
            messages=[{"role": "user", "content": contenido}],
            output_config={"effort": "low", "format": {"type": "json_schema", "schema": esquema}},
        )
        if respuesta.stop_reason == "refusal":
            return {"error": "refusal"}
        texto = next((b.text for b in respuesta.content if b.type == "text"), "")
        try:
            return json.loads(texto)
        except json.JSONDecodeError:
            return {"error": "json", "texto": texto}

    def redactar(self, instrucciones: str) -> str:
        respuesta = self._crear(max_tokens=8000, messages=[{"role": "user", "content": instrucciones}], output_config={"effort": "medium"})
        if respuesta.stop_reason == "refusal":
            return ""
        return "".join(b.text for b in respuesta.content if b.type == "text")

    def _crear(self, **kwargs):
        try:
            return self.client.beta.messages.create(model=self.modelo, betas=["server-side-fallback-2026-07-01"], fallbacks="default", **kwargs)
        except TypeError:
            # SDK anterior sin el parámetro de respaldo: la misma petición sin él.
            return self.client.messages.create(model=self.modelo, **kwargs)
