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
