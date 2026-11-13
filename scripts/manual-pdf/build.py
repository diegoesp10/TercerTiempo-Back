"""Genera el PDF compacto del manual de uso a partir de la exportación HTML del documento de Claude Docs.

Uso:  python build.py <manual.html> <salida.pdf> [--tamano 9.5]

El HTML es el que exporta Claude Docs (solo el cuerpo: h1, h2, h3, p, listas, tablas, figuras). El script lo envuelve
en una página con estilo compacto (letra de 9,5 pt, márgenes estrechos) y lo imprime con Google Chrome (o Edge) en
modo sin ventana. No hace falta instalar nada más.
"""

from __future__ import annotations

import argparse
import datetime as dt
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

# Chrome imprime bien en modo sin ventana; Edge (probado el 13-11-2026) devuelve 0 sin escribir el PDF.
NAVEGADORES = [
    Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
    Path(r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
]

MESES = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"]

ESTILO = """
@page { size: A4; margin: 13mm 14mm 14mm 14mm; }
html { font-size: %(tamano)spt; }
body { font-family: "Segoe UI", Calibri, Arial, sans-serif; line-height: 1.32; color: #111; margin: 0; }
h1 { font-size: 1.9em; margin: 0 0 0.15em; letter-spacing: -0.01em; }
h2 { font-size: 1.35em; margin: 1.1em 0 0.35em; padding-bottom: 0.15em; border-bottom: 1px solid #999; page-break-after: avoid; }
h3 { font-size: 1.12em; margin: 0.9em 0 0.3em; page-break-after: avoid; }
p { margin: 0.3em 0 0.45em; orphans: 3; widows: 3; }
p.byline { color: #555; margin-bottom: 0.9em; }
ul, ol { margin: 0.25em 0 0.5em; padding-left: 1.5em; }
li { margin: 0.12em 0; }
li > p { margin: 0.1em 0; }
table { border-collapse: collapse; width: 100%%; margin: 0.4em 0 0.7em; font-size: 0.94em; page-break-inside: auto; }
tr { page-break-inside: avoid; }
th, td { border: 1px solid #bbb; padding: 0.22em 0.45em; vertical-align: top; text-align: left; }
th { background: #eef0f3; }
td p, th p { margin: 0; }
figure { margin: 0.5em 0 0.8em; padding: 0.4em 0.7em; border-left: 3px solid #bbb; color: #444; font-style: italic; page-break-inside: avoid; }
figcaption { font-style: normal; }
a { color: inherit; text-decoration: none; }
code { font-family: Consolas, "Courier New", monospace; font-size: 0.95em; }
"""


def fecha_larga(iso: str) -> str:
    try:
        fecha = dt.date.fromisoformat(iso)
    except ValueError:
        return iso
    return f"{fecha.day} de {MESES[fecha.month - 1]} de {fecha.year}"


def preparar(cuerpo: str, tamano: float) -> str:
    """El cuerpo exportado, con la línea de autor legible y sin los restos de los chips."""
    # La línea de autor: la fecha como chip y la mención del autor como identificador interno.
    def byline(m: re.Match) -> str:
        fecha = fecha_larga(m.group(1))
        return f'<p class="byline">{fecha} · Diego Espina</p>'

    cuerpo = re.sub(r'<p><time[^>]*datetime="([^"]+)"[^>]*>[^<]*</time>[^<]*<span data-atom="mention"[^>]*>[^<]*</span></p>', byline, cuerpo, count=1)
    cuerpo = re.sub(r'<span data-atom="mention"[^>]*>@?([^<]*)</span>', r"\1", cuerpo)
    cuerpo = re.sub(r'<time[^>]*datetime="([^"]+)"[^>]*>[^<]*</time>', lambda m: fecha_larga(m.group(1)), cuerpo)
    # Un dibujo incrustado no se exporta: queda su pie como nota.
    cuerpo = re.sub(r"<figure>(?:(?!</figure>).)*?<figcaption>(.*?)</figcaption>(?:(?!</figure>).)*?</figure>", r"<figure><figcaption>\1</figcaption></figure>", cuerpo, flags=re.S)
    cuerpo = re.sub(r"<figure>(?:(?!<figcaption>)(?!</figure>).)*</figure>", "", cuerpo, flags=re.S)
    return f'<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Manual de uso de Tercer Tiempo</title><style>{ESTILO % {"tamano": tamano}}</style></head><body>{cuerpo}</body></html>'


def navegador() -> Path:
    for ruta in NAVEGADORES:
        if ruta.exists():
            return ruta
    raise FileNotFoundError("No se encuentra Microsoft Edge ni Google Chrome para imprimir el PDF.")


def imprimir(html: Path, pdf: Path) -> None:
    # Con un perfil propio el navegador no se engancha a la ventana que pueda tener abierta el usuario.
    perfil = Path(tempfile.mkdtemp(prefix="manual-pdf-"))
    orden = [
        str(navegador()), "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
        f"--user-data-dir={perfil}", "--no-pdf-header-footer", "--virtual-time-budget=5000",
        f"--print-to-pdf={pdf.resolve()}", html.resolve().as_uri(),
    ]
    try:
        resultado = subprocess.run(orden, capture_output=True, text=True, timeout=120)
    finally:
        shutil.rmtree(perfil, ignore_errors=True)
    if not pdf.exists():
        raise RuntimeError(f"El navegador no dejó el PDF ({resultado.returncode}): {resultado.stderr[-500:]}")


def paginas(pdf: Path) -> int:
    datos = pdf.read_bytes()
    m = re.search(rb"/Type\s*/Pages[^>]*?/Count\s+(\d+)", datos)
    return int(m.group(1)) if m else len(re.findall(rb"/Type\s*/Page[^s]", datos))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="PDF compacto del manual de uso desde la exportación HTML de Claude Docs.")
    parser.add_argument("html", type=Path)
    parser.add_argument("pdf", type=Path)
    parser.add_argument("--tamano", type=float, default=9.5, help="Tamaño de la letra en puntos (9,5 por defecto).")
    args = parser.parse_args(argv)

    cuerpo = args.html.read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory() as carpeta:
        pagina = Path(carpeta) / "manual.html"
        pagina.write_text(preparar(cuerpo, args.tamano), encoding="utf-8")
        args.pdf.parent.mkdir(parents=True, exist_ok=True)
        if args.pdf.exists():
            args.pdf.unlink()
        imprimir(pagina, args.pdf)
    print(f"{args.pdf}: {paginas(args.pdf)} páginas, {args.pdf.stat().st_size // 1024} KB, letra de {args.tamano:g} pt.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
