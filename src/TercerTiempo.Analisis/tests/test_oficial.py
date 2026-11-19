from tt_analisis.oficial import ajustar_goles, lados_completos, leer_acta
from tt_analisis.tipos import Evento

DATOS = {
    "parte": "partido",
    "minutos_parte": 45,
    "resultado": {"local": 0, "visitante": 1},
    "equipos": [
        {"lado": "local", "nombre": "Vallobín", "jugadores": [
            {"dorsal": 1, "nombre": "Uno", "titular": True, "entra": None, "sale": None},
            {"dorsal": 3, "nombre": "Tres", "titular": True, "entra": None, "sale": 60},
            {"dorsal": 7, "nombre": "Siete", "titular": False, "entra": 60, "sale": None},
            {"dorsal": 15, "nombre": "Quince", "titular": False, "entra": 24, "sale": 80},
            {"dorsal": 22, "nombre": "Banquillo", "titular": False, "entra": None, "sale": None},
        ]},
        {"lado": "visitante", "nombre": "Rival", "jugadores": [{"dorsal": 9, "nombre": "Nueve", "titular": True, "entra": None, "sale": None, "goles": 1}]},
    ],
}


def test_los_minutos_salen_por_descarte():
    acta = leer_acta(DATOS)
    minutos = {j.dorsal: acta.minutos(j) for j in acta.jugadores if j.lado == "local"}
    # Sin cambio, el partido entero; con cambio, hasta o desde ese minuto; el que no salió, 0.
    assert minutos == {1: 90, 3: 60, 7: 30, 15: 56, 22: 0}
    assert acta.jugador("visitante", 9).goles == 1 and acta.jugador("local", 9) is None
    assert acta.goles == {"local": 0, "visitante": 1}


def test_sin_resultado_o_sin_equipos():
    assert leer_acta(None) is None and leer_acta({"equipos": []}) is None
    sin_cerrar = leer_acta(dict(DATOS, resultado=None))
    assert sin_cerrar.goles is None
    assert ajustar_goles([Evento("gol", 1.0, equipo=0)], sin_cerrar, {0: "local"}) == 0


def test_el_acta_deja_como_mucho_sus_goles_y_los_de_mas_confianza():
    eventos = [
        Evento("tiro", 99.0, equipo=0, tracklet=4, detalle="A puerta · el balón entra en la portería"),
        Evento("gol", 100.0, equipo=0, tracklet=4, confianza=0.85),
        Evento("tiro", 300.0, equipo=1, tracklet=8, detalle="Balón que entra en la portería (a puerta)"),
        Evento("gol", 300.0, equipo=1, tracklet=8, confianza=0.45),
        Evento("gol", 500.0, equipo=1, tracklet=9, confianza=0.85),
    ]
    # El equipo 0 es el local (0 goles en el acta); el 1, el visitante (1 gol), deducido. Del tiro de un gol quitado,
    # el disparo que se vio queda como tiro fuera (por encima del larguero) y el que solo se dedujo del cruce se va.
    assert ajustar_goles(eventos, leer_acta(DATOS), {0: "local", 1: None}) == 2
    assert [(e.tipo, e.tracklet) for e in eventos] == [("tiro", 4), ("gol", 9)]
    assert eventos[0].detalle.startswith("Fuera") and "entra" not in eventos[0].detalle


def test_sin_minutos_solo_valen_los_goles_confirmados_por_el_saque_de_centro():
    eventos = [Evento("gol", 300.0, equipo=1, tracklet=8, confianza=0.45)]
    assert ajustar_goles(eventos, leer_acta(DATOS), {1: "visitante"}) == 1 and eventos == []


def test_lejos_del_minuto_solo_vale_el_gol_confirmado_por_el_saque_de_centro():
    acta = leer_acta(dict(DATOS, goles=[{"lado": "visitante", "minuto": 30}]))
    sin_confirmar = [Evento("gol", 2000.0, equipo=1, confianza=0.45)]
    assert ajustar_goles(sin_confirmar, acta, {1: "visitante"}, lambda minuto: minuto * 60.0) == 1 and sin_confirmar == []
    confirmado = [Evento("gol", 2000.0, equipo=1, confianza=0.85)]
    assert ajustar_goles(confirmado, acta, {1: "visitante"}, lambda minuto: minuto * 60.0) == 0 and len(confirmado) == 1


def test_lados_completos():
    assert lados_completos({0: None, 1: "local"}) == {0: "visitante", 1: "local"}
    assert lados_completos({0: None, 1: None}) == {0: None, 1: None}


def test_con_el_minuto_del_gol_se_queda_el_candidato_de_ese_minuto():
    datos = dict(DATOS, goles=[{"lado": "visitante", "minuto": 30}])
    acta = leer_acta(datos)
    assert acta.minutos_goles == (("visitante", 30, 0),)
    eventos = [
        Evento("gol", 600.0, equipo=1, tracklet=8, confianza=0.85, detalle="El balón cruza la línea"),
        Evento("gol", 1850.0, equipo=1, tracklet=9, confianza=0.45, detalle="El balón cruza la línea"),
    ]
    # El vídeo empieza con el saque: el minuto 30 es el segundo 1800; gana el candidato de 1850 aunque sea más flojo.
    assert ajustar_goles(eventos, acta, {1: "visitante"}, lambda minuto: minuto * 60.0) == 1
    assert [(e.t, e.confianza) for e in eventos] == [(1850.0, 0.85)] and eventos[0].detalle.endswith("en el acta, minuto 30")
    # Sin ningún candidato cerca del minuto, no queda ninguno.
    lejos = [Evento("gol", 600.0, equipo=1, confianza=0.85)]
    assert ajustar_goles(lejos, acta, {1: "visitante"}, lambda minuto: minuto * 60.0) == 1 and lejos == []


def test_el_gol_en_el_descuento_sigue_en_su_parte():
    datos = dict(DATOS, goles=[{"lado": "visitante", "minuto": 45, "descuento": 2}])
    acta = leer_acta(datos)
    assert acta.minutos_goles == (("visitante", 45, 2),)
    eventos = [Evento("gol", 2650.0, equipo=1, confianza=0.45), Evento("gol", 2830.0, equipo=1, confianza=0.45)]
    # El 45+2 es el segundo 2820 del vídeo (dos minutos después del 45), no el principio de la segunda parte.
    assert ajustar_goles(eventos, acta, {1: "visitante"}, lambda minuto: minuto * 60.0) == 1
    assert [e.t for e in eventos] == [2830.0] and eventos[0].detalle.endswith("minuto 45+2")
