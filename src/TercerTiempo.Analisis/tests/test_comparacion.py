from pathlib import Path

from tt_analisis.comparacion import comparar, emparejar_equipos, posesion_por_minuto


def _analisis(colores, posesion, por_minuto, con_balon=30.0, total=45.0):
    return {
        "equipos": [{"id": 0, "nombre": "Vallobín", "color": colores[0]}, {"id": 1, "nombre": "Rival", "color": colores[1]}],
        "parametros": {"fps_muestreo": 5.0},
        "recuento": {"fotogramas": 1500, "pct_con_balon": con_balon, "pct_con_balon_total": total, "pct_calibrados": 67.0, "toques": 30},
        "posesion": {
            "confianza": "media",
            "cobertura": {"segundos_atribuidos": 120.0},
            "equipos": [{"equipo": 0, "porcentaje": posesion[0]}, {"equipo": 1, "porcentaje": posesion[1]}],
            "por_minuto": [{"minuto": m, "equipo_0": a, "equipo_1": b} for m, (a, b) in por_minuto.items()],
        },
        "eventos": [{"tipo": "corner"}, {"tipo": "paron"}],
        "tiempos": {"total_s": 270.0},
    }


def test_los_equipos_se_emparejan_por_color_aunque_cambie_el_indice():
    a = _analisis(["#5a5a57", "#9c8e8d"], (40.0, 60.0), {20: (10.0, 20.0)})
    b = _analisis(["#9d8f8e", "#5b5b58"], (60.0, 40.0), {20: (20.0, 10.0)})
    assert emparejar_equipos(a, b) == {0: 1, 1: 0}
    assert posesion_por_minuto(b, emparejar_equipos(a, b)) == {20: 1 / 3}


def test_la_comparacion_enseña_la_tabla_y_la_diferencia_con_la_primera():
    a = _analisis(["#5a5a57", "#9c8e8d"], (40.0, 60.0), {20: (10.0, 20.0), 21: (30.0, 10.0)})
    b = _analisis(["#9d8f8e", "#5b5b58"], (57.0, 43.0), {20: (20.0, 10.0), 21: (10.0, 30.0)}, con_balon=35.0)
    texto = comparar([(Path("salidas/a"), a), (Path("salidas/b"), b)])
    assert "| Posesión Vallobín (%) | 40.0 | 43.0 |" in texto
    assert "| Balón visto (%) | 30.0 | 35.0 |" in texto
    assert "| Eventos | 1 | 1 |" in texto
    # Minuto 20 igual (1/3 en las dos), minuto 21 igual (3/4): 0 puntos de media, 3 de diferencia total.
    assert "b: posesión total a 3.0 puntos" in texto
    assert "0 puntos de diferencia media en 2 minutos" in texto
    assert comparar([]) == "Nada que comparar."
