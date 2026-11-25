import pytest

from tt_analisis.geometria import Campo
from tt_analisis.posesion import MaquinaControl, MaquinaPosesion, acciones_por_jugador, resumir_posesion, secuencias, toques_controlados

from conftest import instante, persona, portero


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


def _jugar(escenas, dt=0.2):
    """Instantes cada `dt` con (personas, balón) y los toques controlados que salen de ellos."""
    instantes = [instante(i, round(i * dt, 2), personas, balon) for i, (personas, balon) in enumerate(escenas)]
    return toques_controlados(instantes), instantes


def test_pase_perdida_y_recuperacion_con_el_balon_golpeado():
    a1, a2, b1 = persona(1, 0, (1000, 1000)), persona(2, 0, (2500, 1000)), persona(3, 1, (4000, 1000))
    todos = [a1, a2, b1]
    escenas = (
        [(todos, (1050, 1000))] * 5  # 0,0-0,8: lo tiene 1
        + [(todos, (x, 1000)) for x in (1400, 1750, 2100)]  # 1,0-1,4: pase raso a 17,5 m/s
        + [(todos, (2450, 1000))] * 5  # 1,6-2,4: lo controla 2
        + [(todos, (x, 1000)) for x in (2825, 3200, 3575)]  # 2,6-3,0: 2 se lo da al rival
        + [(todos, (3950, 1000))] * 10  # 3,2-5,0: el rival 3 se queda con él
    )
    toques, _ = _jugar(escenas)
    assert [(t.tracklet, t.equipo) for t in toques] == [(1, 0), (2, 0), (3, 1)]
    secs = secuencias(toques, cortes=[])
    assert [(s.equipo, len(s.toques)) for s in secs] == [(0, 2), (1, 1)]
    acciones = acciones_por_jugador(secs)
    assert acciones[1].pases == 1
    assert acciones[2].perdidas == 1 and acciones[2].pases_fallados == 1
    assert acciones[3].recuperaciones == 1
    # Cada una con su segundo del vídeo: el pase y la pérdida cuando el balón deja el pie (fin del toque), la
    # recuperación cuando llega al rival (principio del suyo).
    assert acciones[1].segundos_pases == pytest.approx([0.8])
    assert acciones[2].segundos_perdidas == pytest.approx([2.4]) and acciones[2].segundos_pases_fallados == pytest.approx([2.4])
    assert acciones[3].segundos_recuperaciones == pytest.approx([3.2])
    for a in acciones.values():
        assert (len(a.segundos_pases), len(a.segundos_pases_fallados), len(a.segundos_perdidas), len(a.segundos_recuperaciones)) == (a.pases, a.pases_fallados, a.perdidas, a.recuperaciones)


def test_sin_balon_golpeado_ni_rival_que_lo_conserve_no_hay_pase_ni_robo():
    a1, a2, b1 = persona(1, 0, (1000, 1000)), persona(2, 0, (1300, 1000)), persona(3, 1, (1600, 1000))
    todos = [a1, a2, b1]
    escenas = (
        [(todos, (1050, 1000))] * 5
        + [(todos, (1250, 1000))] * 5  # a 2 m: se la deja al compañero de al lado (o es el mismo, partido en dos)
        + [(todos, (1550, 1000))] * 4  # el rival la toca 0,6 s y...
        + [(todos, (1250, 1000))] * 5  # ...vuelve a 2: balón dividido
    )
    toques, _ = _jugar(escenas)
    assert [t.tracklet for t in toques] == [1, 2, 3, 2]
    acciones = acciones_por_jugador(secuencias(toques, cortes=[]))
    assert acciones[1].pases == 0 and acciones[2].perdidas == 0 and acciones[3].recuperaciones == 0
    assert acciones[3].perdidas == 0 and acciones[2].recuperaciones == 0


def test_un_balon_que_pasa_rapido_al_lado_de_un_jugador_no_es_un_toque_suyo():
    # A 10 m/s y a 1 m: dos muestras a menos de 2 m, pero en ellas el balón cruza de un lado a otro de él (2 m en 0,2 s).
    jugador = persona(1, 0, (5000, 3000))
    escenas = [([jugador], (x, 3100)) for x in range(4100, 6100, 200)]
    toques, instantes = _jugar(escenas)
    assert toques == []
    # El más cercano sí se lo da: para la posesión del equipo vale, para las acciones del jugador no.
    maquina = MaquinaPosesion(confirmacion=2)
    for inst in instantes:
        maquina.actualizar(inst)
    maquina.cerrar()
    assert [t.tracklet for t in maquina.toques] == [1]


def test_en_un_forcejeo_el_balon_sigue_siendo_de_quien_lo_tenia():
    a, b = persona(1, 0, (5000, 3000)), persona(2, 1, (5150, 3000))
    # Lo tiene 1 y llega 2: el balón va y viene entre los dos, casi siempre más cerca de 2, pero nunca claramente.
    escenas = [([a, b], (5000, 3050))] * 5 + [([a, b], (x, 3000)) for x in (5080, 5110) * 5]
    instantes = [instante(i, round(i * 0.2, 2), personas, balon) for i, (personas, balon) in enumerate(escenas)]
    assert [t.tracklet for t in toques_controlados(instantes)] == [1]
    sin_histeresis = MaquinaControl(histeresis_cm=0.0)
    for inst in instantes:
        sin_histeresis.actualizar(inst)
    sin_histeresis.cerrar()
    assert [t.tracklet for t in sin_histeresis.toques] == [1, 2]


def test_quien_coge_un_balon_parado_no_se_lo_ha_quitado_a_nadie():
    a_cerca, a_lejos = persona(1, 0, (5800, 3500)), persona(1, 0, (5500, 3500))
    b_lejos, b_cerca = persona(2, 1, (6400, 3500)), persona(2, 1, (6050, 3500))
    escenas = (
        [([a_cerca, b_lejos], (5850, 3500))] * 5
        + [([a_cerca, b_lejos], (6000, 3500))]
        # El balón se queda quieto casi 2 s sin nadie al lado (una falta, un saque) y luego lo coge el rival.
        + [([a_lejos, b_lejos], (6000, 3500))] * 9
        + [([a_lejos, b_cerca], (6000, 3500))] * 11
    )
    toques, _ = _jugar(escenas)
    assert [(t.tracklet, t.parado) for t in toques] == [(1, False), (2, True)]
    acciones = acciones_por_jugador(secuencias(toques, cortes=[]))
    assert acciones[1].perdidas == 0 and acciones[2].recuperaciones == 0


def test_un_avistamiento_imposible_no_es_el_balon():
    a, b = persona(1, 0, (3000, 3000)), persona(2, 1, (9000, 3000))
    # 0,2 s después del balón de 1, un «balón» junto a 2, a 60 m: una cabeza o una media, no el balón.
    escenas = [([a, b], (3050, 3000))] * 5 + [([a, b], (9050, 3000))] + [([a, b], (3050, 3000))] * 5
    maquina = MaquinaControl()
    for i, (personas, balon) in enumerate(escenas):
        maquina.actualizar(instante(i, round(i * 0.2, 2), personas, balon))
    maquina.cerrar()
    assert maquina.saltos == 1
    assert [(t.tracklet, t.t_inicio, t.t_fin) for t in maquina.toques] == [(1, 0.0, 2.0)]


def test_el_portero_que_se_queda_con_el_balon_del_rival_hace_una_parada():
    campo = Campo(12000, 7000)
    delantero, guardameta = persona(1, 0, (10000, 3500)), portero(9, 1, (11500, 3500))
    todos = [delantero, guardameta]
    tiro = [(todos, (10050, 3500))] * 5 + [(todos, (x, 3500)) for x in (10400, 10750, 11100)]  # lo tiene 1 y tira
    toques, instantes = _jugar(tiro + [(todos, (11450, 3500))] * 11)  # 1,6-3,6: el portero lo bloca y lo tiene
    assert [t.tracklet for t in toques] == [1, 9]
    acciones = acciones_por_jugador(secuencias(toques, cortes=[]), porteros={9}, tiros=[(1, 1.0)], instantes=instantes, campo=campo)
    assert acciones[9].paradas == 1 and acciones[9].segundos_paradas == pytest.approx([1.6])
    # Ni recupera el portero ni pierde el balón el que tiró.
    assert acciones[9].recuperaciones == 0 and acciones[1].perdidas == 0
    # El balón que solo le pasa rozando (lo tiene 0,4 s y se va) no es una parada.
    roce = tiro + [(todos, (11450, 3500))] * 3 + [(todos, (11450 + 250 * k, 3500 + 400 * k)) for k in range(1, 5)]
    toques, instantes = _jugar(roce)
    acciones = acciones_por_jugador(secuencias(toques, cortes=[]), porteros={9}, instantes=instantes, campo=campo)
    assert acciones[9].paradas == 0 and acciones[9].recuperaciones == 0


def test_el_balon_que_el_portero_pone_en_juego_desde_su_area_es_un_saque_no_un_pase():
    campo = Campo(12000, 7000)
    guardameta, companero = portero(9, 1, (11500, 3500)), persona(4, 1, (9000, 3500))
    todos = [guardameta, companero]
    escenas = (
        [(todos, (11450, 3500))] * 6  # 0,0-1,0: lo tiene el portero
        + [(todos, (x, 3500)) for x in (11000, 10500, 10000, 9500)]  # 1,2-1,8: saca en largo
        + [(todos, (9050, 3500))] * 6  # 2,0-3,0: lo controla un compañero
    )
    toques, instantes = _jugar(escenas)
    assert [t.tracklet for t in toques] == [9, 4]
    acciones = acciones_por_jugador(secuencias(toques, cortes=[]), porteros={9}, instantes=instantes, campo=campo)
    assert acciones[9].saques == 1 and acciones[9].segundos_saques == pytest.approx([1.0])
    assert acciones[9].pases == 0
    # Sin el campo calibrado no se sabe si sacó desde su área: no cuenta.
    sin_campo = acciones_por_jugador(secuencias(toques, cortes=[]), porteros={9}, campo=campo)
    assert sin_campo[9].saques == 0


def test_un_corte_por_fuera_separa_las_secuencias_sin_perdida():
    a1, b1 = persona(1, 0, (1000, 1000)), persona(3, 1, (6000, 1000))
    escenas = [([a1, b1], (1050, 1000))] * 4 + [([a1, b1], (5950, 1000))] * 4
    toques, _ = _toques_de(escenas)
    secs = secuencias(toques, cortes=[0.7])
    assert abs(toques[0].t_fin - 0.6) < 1e-9 and abs(toques[1].t_inicio - 0.8) < 1e-9
    assert secs[0].cortada_por_fuera
    acciones = acciones_por_jugador(secs)
    assert acciones[1].perdidas == 0 and acciones[3].recuperaciones == 0
    assert acciones[1].segundos_perdidas == [] and acciones[3].segundos_recuperaciones == []


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
