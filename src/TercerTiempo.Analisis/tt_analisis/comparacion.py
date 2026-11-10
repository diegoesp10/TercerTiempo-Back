"""Comparar varias pasadas del analizador sobre el mismo tramo (`tt-analisis comparar salidas/a salidas/b …`): cuánto
balón se ve, cuánto se calibra, qué posesión sale y cuánto baila de una pasada a otra. Los equipos se emparejan por el
color de la camiseta, no por el índice (el 0 es el que más recortes tiene, y eso puede cambiar entre pasadas)."""

from __future__ import annotations

from pathlib import Path


def _rgb(hexadecimal: str | None) -> tuple[int, int, int]:
    if not hexadecimal or len(hexadecimal) != 7:
        return (0, 0, 0)
    return (int(hexadecimal[1:3], 16), int(hexadecimal[3:5], 16), int(hexadecimal[5:7], 16))


def emparejar_equipos(referencia: dict, otra: dict) -> dict[int, int]:
    """Índice de equipo de `otra` → índice en `referencia`, por el color más parecido."""
    colores_ref = [_rgb(e.get("color")) for e in referencia.get("equipos", [])]
    colores = [_rgb(e.get("color")) for e in otra.get("equipos", [])]
    if len(colores_ref) != 2 or len(colores) != 2:
        return {0: 0, 1: 1}

    def d(a, b) -> float:
        return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5

    directo = d(colores[0], colores_ref[0]) + d(colores[1], colores_ref[1])
    cruzado = d(colores[0], colores_ref[1]) + d(colores[1], colores_ref[0])
    return {0: 0, 1: 1} if directo <= cruzado else {0: 1, 1: 0}


def posesion_por_minuto(analisis: dict, mapa: dict[int, int]) -> dict[int, float | None]:
    """Parte del minuto (0-1) que tiene el equipo de referencia 0; None si ese minuto no se repartió."""
    salida: dict[int, float | None] = {}
    for fila in analisis.get("posesion", {}).get("por_minuto", []):
        valores = {mapa[0]: float(fila.get("equipo_0", 0.0)), mapa[1]: float(fila.get("equipo_1", 0.0))}
        total = valores[0] + valores[1]
        salida[int(fila["minuto"])] = None if total <= 0 else valores[0] / total
    return salida


def posesion_de(analisis: dict, mapa: dict[int, int], equipo_ref: int) -> float | None:
    """Porcentaje de posesión del equipo `equipo_ref` (índice de la referencia) en esta pasada."""
    equipos = {e["equipo"]: e for e in analisis.get("posesion", {}).get("equipos", [])}
    propio = next((i for i, j in mapa.items() if j == equipo_ref), equipo_ref)
    return equipos.get(propio, {}).get("porcentaje")


def comparar(pasadas: list[tuple[Path, dict]]) -> str:
    """Una tabla Markdown con una columna por pasada y, debajo, cuánto difiere cada pasada de la primera."""
    if not pasadas:
        return "Nada que comparar."
    ref = pasadas[0][1]
    mapas = [emparejar_equipos(ref, a) for _, a in pasadas]
    nombres = [e.get("nombre", f"Equipo {e.get('id')}") for e in ref.get("equipos", [])] or ["Equipo A", "Equipo B"]

    def fila(etiqueta: str, valor) -> str:
        celdas = []
        for (_, a), m in zip(pasadas, mapas):
            v = valor(a, m)
            celdas.append("—" if v is None else str(v))
        return f"| {etiqueta} | " + " | ".join(celdas) + " |"

    def recuento(clave: str):
        return lambda a, m: a.get("recuento", {}).get(clave)

    lineas = [
        "| | " + " | ".join(ruta.name for ruta, _ in pasadas) + " |",
        "|---|" + "---|" * len(pasadas),
        fila("Fotogramas", recuento("fotogramas")),
        fila("Fotogramas/s", lambda a, m: a.get("parametros", {}).get("fps_muestreo")),
        fila("Balón visto (%)", recuento("pct_con_balon")),
        fila("Balón visto o interpolado (%)", recuento("pct_con_balon_total")),
        fila("Campo calibrado (%)", recuento("pct_calibrados")),
        fila("Toques", recuento("toques")),
        fila("Segundos atribuidos", lambda a, m: a.get("posesion", {}).get("cobertura", {}).get("segundos_atribuidos", a.get("posesion", {}).get("segundos_con_posesion"))),
        fila(f"Posesión {nombres[0]} (%)", lambda a, m: posesion_de(a, m, 0)),
        fila(f"Posesión {nombres[1]} (%)", lambda a, m: posesion_de(a, m, 1)),
        fila("Confianza", lambda a, m: a.get("posesion", {}).get("confianza")),
        fila("Eventos", lambda a, m: len([e for e in a.get("eventos", []) if e.get("tipo") != "paron"])),
        fila("Minutos de cálculo", lambda a, m: round(a.get("tiempos", {}).get("total_s", 0) / 60, 1)),
    ]
    if len(pasadas) > 1:
        base = posesion_por_minuto(ref, mapas[0])
        referencia = posesion_de(ref, mapas[0], 0) or 0.0
        lineas += ["", f"Frente a la primera pasada ({pasadas[0][0].name}):", ""]
        for (ruta, a), m in list(zip(pasadas, mapas))[1:]:
            otra = posesion_por_minuto(a, m)
            comunes = [k for k in base if k in otra and base[k] is not None and otra[k] is not None]
            diferencia = abs((posesion_de(a, m, 0) or 0.0) - referencia)
            if comunes:
                media = sum(abs(base[k] - otra[k]) for k in comunes) / len(comunes) * 100  # type: ignore[operator]
                lineas.append(f"- {ruta.name}: posesión total a {diferencia:.1f} puntos; por minuto, {media:.0f} puntos de diferencia media en {len(comunes)} minutos repartidos en las dos.")
            else:
                lineas.append(f"- {ruta.name}: posesión total a {diferencia:.1f} puntos; sin minutos repartidos en común.")
    return "\n".join(lineas)
