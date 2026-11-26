"""Evalúa una pasada con las anotaciones exportadas desde el visor."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from .revision_eventos import FORMATO, MARGEN_S, TIPOS, fuente_revision, huella_video


def _unir(tramos):
    unidos = []
    for inicio, fin in sorted((a, b) for a, b in tramos if a < b):
        if unidos and inicio <= unidos[-1][1]:
            unidos[-1] = (unidos[-1][0], max(fin, unidos[-1][1]))
        else:
            unidos.append((inicio, fin))
    return unidos


def _emparejar(reales, candidatos, margen):
    """Usa cada anotación una vez. Con un margen fijo, emparejar los primeros compatibles maximiza los aciertos."""
    parejas, falsas, omitidas = [], [], []
    i = j = 0
    while i < len(reales) and j < len(candidatos):
        real, candidato = reales[i], candidatos[j]
        diferencia = round(candidato["t"] - real["t"], 3)
        if diferencia < -margen:
            falsas.append(candidato)
            j += 1
        elif diferencia > margen:
            omitidas.append(real)
            i += 1
        else:
            parejas.append({"real": real["t"], "propuesto": candidato["t"]})
            i += 1
            j += 1
    return parejas, falsas + candidatos[j:], omitidas + reales[i:]


def evaluar(revision: dict, fuente: dict, margen: float = MARGEN_S) -> dict:
    if revision.get("formato") != FORMATO or not math.isfinite(margen) or not 0 <= margen <= 30:
        raise ValueError("Formato de revisión o margen no válido (de 0 a 30 segundos).")
    dudas = [d["evento"] for p in revision["decisiones"].values() for d in p["marcas"].values() if d["estado"] == "dudosa"]
    marcas = revision["decisiones"].get(fuente["pasada"], {}).get("marcas", {})
    rechazados = {d["evento"]["id"] for d in marcas.values() if d["estado"] == "falsa"}
    salida = {}
    for tipo in TIPOS:
        tramos = _unir(
            (max(t["desde"], fuente["desde"]), min(t["hasta"], fuente["hasta"]))
            for t in revision["tramos"] if t["tipo"] == tipo
        )
        radio_duda = max(0.001, margen)
        exclusiones = _unir((e["t"] - radio_duda, e["t"] + radio_duda) for e in dudas if e["tipo"] == tipo)
        reales = sorted((e for e in revision["hechos"] if e["tipo"] == tipo), key=lambda e: e["t"])
        candidatos = sorted((e for e in fuente["candidatos"] if e["tipo"] == tipo), key=lambda e: e["t"])
        segundos = excluidos = 0.0
        parejas, falsas, omitidas = [], [], []
        for desde, hasta in tramos:
            segmentos = []
            inicio = desde
            for a, b in exclusiones:
                if b <= inicio or a >= hasta:
                    continue
                if inicio < a:
                    segmentos.append((inicio, a))
                excluidos += max(0, min(b, hasta) - max(a, inicio))
                inicio = max(inicio, b)
            if inicio < hasta:
                segmentos.append((inicio, hasta))
            for inicio, fin in segmentos:
                segundos += fin - inicio
                propuestas = [e for e in candidatos if inicio <= e["t"] < fin]
                falsas.extend(e for e in propuestas if e["id"] in rechazados)
                p, f, o = _emparejar(
                    [e for e in reales if inicio <= e["t"] < fin],
                    [e for e in propuestas if e["id"] not in rechazados],
                    margen,
                )
                parejas.extend(p)
                falsas.extend(f)
                omitidas.extend(o)
        aciertos = len(parejas)
        salida[tipo] = {
            "segundos_revisados": round(segundos, 3), "segundos_excluidos_por_duda": round(excluidos, 3),
            "aciertos": aciertos, "falsas": len(falsas), "omitidas": len(omitidas),
            "precision_pct": round(100 * aciertos / (aciertos + len(falsas)), 1) if aciertos or falsas else None,
            "cobertura_pct": round(100 * aciertos / (aciertos + len(omitidas)), 1) if aciertos or omitidas else None,
            "parejas": parejas, "propuestas_sin_hecho": sorted(falsas, key=lambda e: e["t"]), "hechos_sin_propuesta": omitidas,
        }
    return salida


def tabla(resultados):
    lineas = ["| Acción | Segundos revisados | Aciertos | Falsas | Omisiones | Precisión | Cobertura |", "|---|---:|---:|---:|---:|---:|---:|"]
    for tipo, datos in resultados.items():
        if not datos["segundos_revisados"]:
            lineas.append(f"| {TIPOS[tipo]} | 0 | — | — | — | — | — |")
            continue
        pct = ["—" if datos[k] is None else f"{datos[k]:g} %" for k in ("precision_pct", "cobertura_pct")]
        lineas.append(f"| {TIPOS[tipo]} | {datos['segundos_revisados']:g} | {datos['aciertos']} | {datos['falsas']} | {datos['omitidas']} | {pct[0]} | {pct[1]} |")
    return "\n".join(lineas)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("revision", type=Path, help="JSON exportado desde el visor")
    parser.add_argument("analisis", type=Path, help="Carpeta de salida o su analisis.json")
    parser.add_argument("--video", type=Path, help="El archivo original si ha cambiado de ruta")
    parser.add_argument("--margen", type=float, default=MARGEN_S, help="Diferencia máxima en segundos (por defecto, 2)")
    parser.add_argument("--json", action="store_true", help="Incluye los segundos de cada diferencia")
    args = parser.parse_args(argv)
    try:
        ruta = args.analisis / "analisis.json" if args.analisis.is_dir() else args.analisis
        analisis = json.loads(ruta.read_text(encoding="utf-8"))
        revision = json.loads(args.revision.read_text(encoding="utf-8"))
        if huella_video(args.video or Path(analisis["video"]["ruta"])) != revision["video"]:
            raise ValueError("Las anotaciones pertenecen a otro archivo de vídeo.")
        resultados = evaluar(revision, fuente_revision(analisis), args.margen)
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        parser.exit(2, f"No se puede evaluar: {error}\n")
    print(json.dumps(resultados, indent=2, ensure_ascii=False) if args.json else tabla(resultados))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
