from tt_analisis.posesion import MaquinaPosesion, acciones_por_jugador, resumir_posesion, secuencias

from conftest import instante, persona


def _toques_de(escenas):
    maquina = MaquinaPosesion(confirmacion=2)
    instantes = []
    for i, (personas, balon) in enumerate(escenas):
        inst = instante(i, i * 0.2, personas, balon)
        instantes.append(inst)
        maquina.actualizar(inst)
    maquina.cerrar(instantes[-1].t)
    return maquina.toques, instantes


def test_el_mas_cercano_tiene_el_balon_y_el_cambio_se_confirma():
    a = persona(1, 0, (1000, 1000))
    b = persona(2, 1, (2000, 1000))
    escenas = [([a, b], (1050, 1000))] * 5 + [([a, b], (1950, 1000))] * 5
    toques, _ = _toques_de(escenas)
    assert [(t.tracklet, t.equipo) for t in toques] == [(1, 0), (2, 1)]
    # El toque de 1 acaba cuando el balón deja de estar a su alcance; el de 2 empieza cuando llega a él.
    assert abs(toques[0].t_fin - 0.8) < 1e-9 and abs(toques[1].t_inicio - 1.0) < 1e-9


def test_un_balon_en_vuelo_no_cambia_la_posesion():
    a = persona(1, 0, (1000, 1000))
    b = persona(2, 1, (5000, 1000))
    escenas = [([a, b], (1050, 1000))] * 3 + [([a, b], (3000, 1000))] * 3 + [([a, b], (1050, 1000))] * 3
    toques, _ = _toques_de(escenas)
    assert [t.tracklet for t in toques] == [1]


def test_pases_perdidas_y_recuperaciones():
    a1, a2 = persona(1, 0, (1000, 1000)), persona(2, 0, (3000, 1000))
    b1 = persona(3, 1, (6000, 1000))
    escenas = (
        [([a1, a2, b1], (1050, 1000))] * 4
        + [([a1, a2, b1], (2950, 1000))] * 4  # pase de 1 a 2 (el balón se mueve 19 m)
        + [([a1, a2, b1], (5950, 1000))] * 4  # el rival 3 se hace con el balón
    )
    toques, _ = _toques_de(escenas)
    secs = secuencias(toques, cortes=[])
    assert [(s.equipo, len(s.toques)) for s in secs] == [(0, 2), (1, 1)]
    acciones = acciones_por_jugador(secs)
    assert acciones[1].pases == 1
    assert acciones[2].perdidas == 1 and acciones[2].pases_fallados == 1
    assert acciones[3].recuperaciones == 1


def test_un_corte_por_fuera_separa_las_secuencias_sin_perdida():
    a1, b1 = persona(1, 0, (1000, 1000)), persona(3, 1, (6000, 1000))
    escenas = [([a1, b1], (1050, 1000))] * 4 + [([a1, b1], (5950, 1000))] * 4
    toques, _ = _toques_de(escenas)
    secs = secuencias(toques, cortes=[0.7])
    assert abs(toques[0].t_fin - 0.6) < 1e-9 and abs(toques[1].t_inicio - 0.8) < 1e-9
    assert secs[0].cortada_por_fuera
    acciones = acciones_por_jugador(secs)
    assert acciones[1].perdidas == 0 and acciones[3].recuperaciones == 0


def test_la_posesion_reparte_el_tiempo_activo_y_el_balon_en_vuelo_es_del_ultimo(dt):
    a, b = persona(1, 0, (1000, 1000)), persona(2, 1, (2000, 1000))
    escenas = [([a, b], (1050, 1000))] * 5 + [([a, b], (1950, 1000))] * 5
    toques, instantes = _toques_de(escenas)
    activo = [True] * 10
    activo[9] = False
    resumen = resumir_posesion(toques, instantes, activo, [1] * 10, lambda e, m: 1, dt)
    assert resumen.segundos[0] == 5 * dt
    assert abs(resumen.segundos[1] - 4 * dt) < 1e-9
    assert resumen.porcentaje(0) == 55.6
    assert resumen.por_tercio[0]["propio"] > 0


def test_la_posesion_solo_cuenta_el_tiempo_con_balon_y_olvida_tras_un_hueco_largo(dt):
    a, b = persona(1, 0, (1000, 1000)), persona(2, 1, (6000, 1000))
    # 1 tiene el balón; 3 s sin verlo; vuelve en vuelo (lejos de todos) y luego lo coge 2.
    escenas = [([a, b], (1050, 1000))] * 5 + [([a, b], None)] * 15 + [([a, b], (3500, 1000))] * 3 + [([a, b], (5950, 1000))] * 5
    toques, instantes = _toques_de(escenas)
    assert [(t.tracklet, t.equipo) for t in toques] == [(1, 0), (2, 1)]
    resumen = resumir_posesion(toques, instantes, [True] * len(instantes), [1] * len(instantes), lambda e, m: 1, dt)
    # Los 15 instantes sin balón no se reparten; los 3 en vuelo tras el hueco largo tampoco (no se sabe de quién es).
    assert abs(resumen.segundos[0] - 5 * dt) < 1e-9 and abs(resumen.segundos[1] - 5 * dt) < 1e-9
    assert abs(resumen.segundos_tramo - 28 * dt) < 1e-9
    assert abs(resumen.segundos_con_balon - 13 * dt) < 1e-9 and abs(resumen.segundos_vistos - 13 * dt) < 1e-9
    assert resumen.porcentaje(0) == 50.0
    # Con un hueco corto, el balón en vuelo sigue siendo de quien lo lanzó.
    corto = [([a, b], (1050, 1000))] * 5 + [([a, b], None)] * 5 + [([a, b], (3500, 1000))] * 3 + [([a, b], (5950, 1000))] * 5
    toques, instantes = _toques_de(corto)
    resumen = resumir_posesion(toques, instantes, [True] * len(instantes), [1] * len(instantes), lambda e, m: 1, dt)
    assert abs(resumen.segundos[0] - 8 * dt) < 1e-9


def test_el_balon_interpolado_cuenta_como_balon_y_el_descanso_no_cuenta(dt):
    from tt_analisis.balon import rellenar_huecos
    from tt_analisis.posesion import confianza_posesion

    a, b = persona(1, 0, (1000, 1000)), persona(2, 1, (6000, 1000))
    escenas = [([a, b], (1050, 1000))] * 3 + [([a, b], None)] * 4 + [([a, b], (1050, 1000))] * 3
    toques, instantes = _toques_de(escenas)
    rellenar_huecos(instantes)
    excluidos = [False] * 8 + [True, True]
    resumen = resumir_posesion(toques, instantes, [True] * 10, [1] * 10, lambda e, m: 1, dt, excluidos=excluidos)
    assert abs(resumen.segundos_tramo - 8 * dt) < 1e-9
    assert abs(resumen.segundos_con_balon - 8 * dt) < 1e-9 and abs(resumen.segundos_vistos - 4 * dt) < 1e-9
    assert abs(resumen.segundos[0] - 8 * dt) < 1e-9
    assert resumen.cobertura_pct == 100.0 and resumen.confianza == "alta"
    d = resumen.a_dict()
    assert d["confianza"] == "alta" and d["fiable"] and d["cobertura"]["pct_atribuido"] == 100.0
    assert confianza_posesion(34.9, 30.0) == "baja" and confianza_posesion(40.0, 30.0) == "media" and confianza_posesion(70.0, 40.0) == "media" and confianza_posesion(70.0, 55.0) == "alta"


def test_la_confirmacion_del_poseedor_se_mide_en_segundos_no_en_muestras():
    a, b = persona(1, 0, (1000, 1000)), persona(2, 1, (2000, 1000))
    # A 10 fotogramas/s dos muestras son 0,1 s: el balón pasa 0,2 s junto a 2 y vuelve a 1 sin que 2 llegue a tenerlo.
    escenas = [([a, b], (1050, 1000))] * 5 + [([a, b], (1950, 1000))] * 2 + [([a, b], (1050, 1000))] * 5
    maquina = MaquinaPosesion(confirmacion=2)
    for i, (personas, balon) in enumerate(escenas):
        maquina.actualizar(instante(i, i * 0.1, personas, balon))
    maquina.cerrar()
    assert [t.tracklet for t in maquina.toques] == [1]
    # Con tres muestras (0,2 s) sí.
    escenas = [([a, b], (1050, 1000))] * 5 + [([a, b], (1950, 1000))] * 3 + [([a, b], (1050, 1000))] * 5
    maquina = MaquinaPosesion(confirmacion=2)
    for i, (personas, balon) in enumerate(escenas):
        maquina.actualizar(instante(i, i * 0.1, personas, balon))
    maquina.cerrar()
    assert [t.tracklet for t in maquina.toques] == [1, 2, 1]
