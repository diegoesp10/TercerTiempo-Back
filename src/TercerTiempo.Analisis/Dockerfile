# Analizador de vídeo con GPU. En Windows (Docker Desktop + WSL2) se arranca con `--gpus all`; en Linux, con el
# NVIDIA Container Toolkit. Los pesos no van en la imagen: se montan desde fuera (volumen `modelos`).
FROM nvidia/cuda:12.8.1-cudnn-runtime-ubuntu24.04

ENV DEBIAN_FRONTEND=noninteractive PYTHONUNBUFFERED=1 UV_LINK_MODE=copy
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libgl1 libglib2.0-0 git ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

# uv fijado (el mismo con el que se generó uv.lock) y la instalación tal cual dice uv.lock (--frozen): la imagen no
# resuelve versiones nuevas al construirse.
COPY --from=ghcr.io/astral-sh/uv:0.13.7 /uv /usr/local/bin/uv

WORKDIR /app
COPY pyproject.toml uv.lock .python-version README.md ./
COPY tt_analisis ./tt_analisis
RUN uv sync --frozen --no-dev

ENV PATH="/app/.venv/bin:${PATH}"
VOLUME ["/app/modelos", "/app/salidas", "/videos"]
ENTRYPOINT ["tt-analisis"]
CMD ["--help"]
