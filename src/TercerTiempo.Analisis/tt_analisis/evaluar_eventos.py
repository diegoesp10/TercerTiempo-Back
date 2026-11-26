"""Mide una pasada con lo revisado a mano en el visor (`revision_eventos.py`). Por cada tipo, en los tramos revisados de
principio a fin: cuántas propuestas aciertan, cuántas son falsas y cuánto de lo que pasó se le escapa. En las acciones
de cada jugador, también si acierta con quién la hizo; y, de las falsas, por qué lo son, si se marcaron con su motivo.

Lee la repetición de la pasada (`repeticion/final.json`), que trae las jugadas y las acciones, como el visor; sin ella,
`analisis.json`, que solo trae las jugadas."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from .repeticion import CARPETA, FINAL
from .revision_eventos import ACCIONES, FORMATO, MARGENES, MOTIVOS, TIPOS, clave_jugador, fuente_revision, huella_video

VEREDICTOS_JUGADOR = ("bien", "mal", "sin_identificar", "sin_saber")


def _unir(tramos):
    unidos = []
    for inicio, fin in sorted((a, b) for a, b in tramos if a < b):
        if unidos and inicio <= unidos[-1][1]:
            unidos[-1] = (unidos[-1][0], max(fin, unidos[-1][1]))
        else:
            unidos.append((inicio, fin))
    return unidos


def _en(t, tramos):
    return any(a <= t < b for a, b in tramos)


def _dentro(t, zonas):
    return any(a <= t <= b for a, b in zonas)


def _pct(parte, total):
    return round(100 * parte / total, 1) if total else None


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
            parejas.append((real, candidato))
            i += 1
            j += 1
    return parejas, falsas + candidatos[j:], omitidas + reales[i:]


def _jugador(real, candidato):
    """Si la propuesta acierta con quién hizo la acción: bien, mal, sin identificar (no dice quién) o sin saber (no se
    sabe quién fue)."""
    suyo = clave_jugador(candidato.get("jugador"))
    if suyo is None:
        return "sin_identificar"
    if real.get("jugador") is not None:
        return "bien" if real["jugador"] == suyo else "mal"
    return "mal" if suyo in real.get("no_es", []) else "sin_saber"


def evaluar(revision: dict, fuente: dict, margen: float | None = None) -> dict:
    """Sin `margen`, el de cada tipo (`MARGENES`). Lo que queda cerca de una propuesta marcada como «No se ve», en
    cualquier pasada, no cuenta ni como falsa ni como omitida, para que todas las versiones se midan igual."""
    if revision.get("formato") != FORMATO or (margen is not None and (not math.isfinite(margen) or not 0 <= margen <= 30)):
        raise ValueError("Formato de revisión o margen no válido (de 0 a 30 segundos).")
    dudas = [d["evento"] for p in revision["decisiones"].values() for d in p["marcas"].values() if d["estado"] == "dudosa"]
    marcas = revision["decisiones"].get(fuente["pasada"], {}).get("marcas", {})
    rechazadas = {d["evento"]["id"]: d.get("motivo") for d in marcas.values() if d["estado"] == "falsa"}
    dudosas = {d["evento"]["id"] for d in marcas.values() if d["estado"] == "dudosa"}
    salida = {}
    for tipo in TIPOS:
        m = MARGENES[tipo] if margen is None else margen
        tramos = _unir(
            (max(t["desde"], fuente["desde"]), min(t["hasta"], fuente["hasta"]))
            for t in revision["tramos"] if t["tipo"] == tipo
        )
        radio = max(0.001, m)
        exclusiones = _unir((e["t"] - radio, e["t"] + radio) for e in dudas if e["tipo"] == tipo)
        excluidos = sum(max(0.0, min(b, hasta) - max(a, desde)) for desde, hasta in tramos for a, b in exclusiones)
        segundos = sum(hasta - desde for desde, hasta in tramos) - excluidos

        def juzgable(t, tramos=tramos, exclusiones=exclusiones):
            return _en(t, tramos) and not _dentro(t, exclusiones)

        reales = sorted((h for h in revision["hechos"] if h["tipo"] == tipo and _en(h["t"], tramos)), key=lambda h: h["t"])
        # Una propuesta hasta un margen fuera del tramo puede ser lo que pasó dentro; como falsa solo cuenta la de dentro.
        propuestas = sorted(
            (e for e in fuente["candidatos"] if e["tipo"] == tipo and e["id"] not in dudosas and any(a - m <= e["t"] < b + m for a, b in tramos)),
            key=lambda e: e["t"],
        )
        parejas, sobrantes, omitidas = _emparejar(reales, [e for e in propuestas if e["id"] not in rechazadas], m)
        falsas = sorted(
            [e for e in propuestas if e["id"] in rechazadas and juzgable(e["t"])] + [e for e in sobrantes if juzgable(e["t"])],
            key=lambda e: e["t"],
        )
        omitidas = [h for h in omitidas if not _dentro(h["t"], exclusiones)]
        aciertos = len(parejas)
        motivos = {}
        for e in falsas:
            motivo = rechazadas.get(e["id"], "sin_revisar") or "sin_motivo"
            motivos[motivo] = motivos.get(motivo, 0) + 1
        resultado = {
            "margen_s": m,
            "segundos_revisados": round(segundos, 3), "segundos_excluidos_por_duda": round(excluidos, 3),
            "aciertos": aciertos, "falsas": len(falsas), "omitidas": len(omitidas),
            "precision_pct": _pct(aciertos, aciertos + len(falsas)),
            "cobertura_pct": _pct(aciertos, aciertos + len(omitidas)),
            "parejas": [{"real": r["t"], "propuesto": c["t"]} for r, c in parejas],
            "propuestas_sin_hecho": falsas, "hechos_sin_propuesta": omitidas, "motivos": motivos,
        }
        if tipo in ACCIONES:
            cuenta = dict.fromkeys(VEREDICTOS_JUGADOR, 0)
            for pareja, (real, candidato) in zip(resultado["parejas"], parejas):
                pareja["jugador"] = _jugador(real, candidato)
                cuenta[pareja["jugador"]] += 1
            resultado["jugador"] = {**cuenta, "acierto_pct": _pct(cuenta["bien"], cuenta["bien"] + cuenta["mal"])}
        salida[tipo] = resultado
    return salida


def tabla(resultados):
    lineas = [
        "| Tipo | Segundos revisados | Aciertos | Falsas | Omisiones | Precisión | Cobertura | Jugador bien |",
        "|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    sin_revisar, motivos = [], []
    for tipo, datos in resultados.items():
        if not datos["segundos_revisados"]:
            sin_revisar.append(TIPOS[tipo])
            continue
        pct = ["—" if datos[k] is None else f"{datos[k]:g} %" for k in ("precision_pct", "cobertura_pct")]
        jugador = datos.get("jugador")
        quien = "—" if not jugador or jugador["acierto_pct"] is None else f"{jugador['acierto_pct']:g} % ({jugador['bien']} de {jugador['bien'] + jugador['mal']})"
        lineas.append(f"| {TIPOS[tipo]} | {datos['segundos_revisados']:g} | {datos['aciertos']} | {datos['falsas']} | {datos['omitidas']} | {pct[0]} | {pct[1]} | {quien} |")
        if datos["motivos"]:
            nombres = {**MOTIVOS, "sin_motivo": "sin motivo", "sin_revisar": "sin veredicto en esta pasada"}
            motivos.append(f"- {TIPOS[tipo]}: " + ", ".join(f"{n} {nombres[k].lower()}" for k, n in sorted(datos["motivos"].items(), key=lambda x: -x[1])))
    if motivos:
        lineas += ["", "Por qué son falsas:", *motivos]
    if sin_revisar:
        lineas += ["", f"Sin tramos revisados: {', '.join(sin_revisar)}."]
    return "\n".join(lineas)


def leer_pasada(ruta: Path) -> dict:
    """La repetición de la pasada, como la ve el visor; si no la tiene, su analisis.json."""
    carpeta = ruta if ruta.is_dir() else ruta.parent
    repeticion = carpeta / CARPETA / FINAL
    elegida = repeticion if repeticion.is_file() else (ruta if ruta.is_file() else carpeta / "analisis.json")
    return json.loads(elegida.read_text(encoding="utf-8"))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("revision", type=Path, help="JSON exportado desde el visor (o su copia en datasets/revisiones)")
    parser.add_argument("analisis", type=Path, help="Carpeta de salida de la pasada, o su analisis.json")
    parser.add_argument("--video", type=Path, help="El archivo original si ha cambiado de ruta")
    parser.add_argument("--margen", type=float, help="Diferencia máxima en segundos para todos los tipos (por defecto, 2 en las jugadas y 1 en las acciones)")
    parser.add_argument("--json", action="store_true", help="Incluye los segundos de cada diferencia")
    args = parser.parse_args(argv)
    try:
        pasada = leer_pasada(args.analisis)
        revision = json.loads(args.revision.read_text(encoding="utf-8"))
        if huella_video(args.video or Path(pasada["video"]["ruta"])) != revision["video"]:
            raise ValueError("Las anotaciones pertenecen a otro archivo de vídeo.")
        resultados = evaluar(revision, fuente_revision(pasada), args.margen)
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        parser.exit(2, f"No se puede evaluar: {error}\n")
    print(json.dumps(resultados, indent=2, ensure_ascii=False) if args.json else tabla(resultados))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
