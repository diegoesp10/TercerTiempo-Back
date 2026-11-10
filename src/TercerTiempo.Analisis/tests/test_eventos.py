from tt_analisis.eventos import Parametros, detectar_descanso, detectar_eventos
from tt_analisis.tipos import Toque

from conftest import instante, persona, portero


def _instantes(balones, personas=(), dt=0.2, t0=0.0):
    return [instante(i, t0 + i * dt, personas, b) for i, b in enumerate(balones)]


def _tipos(resultado, tipo):
    return [e for e in resultado.eventos if e.tipo == tipo]


DIRECCION = {0: 1, 1: -1}  # el equipo 0 ataca hacia x = 12000


def test_saque_de_banda_para_el_rival_del_ultimo_que_toco():
    balones = [(6000, 3000)] * 3 + [(6000, 6800)] + [(6000, 7200)] * 3 + [(6000, 6800)] * 3
    instantes = _instantes(balones)
    toques = [Toque(1, 0, 0.0, 0.5, (6000, 3000), (6000, 3000))]
    r = detectar_eventos(instantes, toques, [1] * len(instantes), lambda e, m: DIRECCION[e], lambda e: set())
    fuera = _tipos(r, "saque_banda")
    assert len(fuera) == 1 and fuera[0].equipo == 1
    assert r.juego_activo[4] is False and r.juego_activo[0] is True
    assert r.cortes == [fuera[0].t]


def test_corner_o_saque_de_puerta_segun_quien_toco():
    balones = [(11500, 500)] * 3 + [(12300, 500)] * 3
    instantes = _instantes(balones)
    atacante = [Toque(1, 0, 0.0, 0.5, (11500, 500), (11500, 500))]
    defensor = [Toque(2, 1, 0.0, 0.5, (11500, 500), (11500, 500))]
    r1 = detectar_eventos(instantes, atacante, [1] * 6, lambda e, m: DIRECCION[e], lambda e: set())
    r2 = detectar_eventos(instantes, defensor, [1] * 6, lambda e, m: DIRECCION[e], lambda e: set())
    assert [(e.tipo, e.equipo) for e in r1.eventos] == [("saque_puerta", 1)]
    assert [(e.tipo, e.equipo) for e in r2.eventos] == [("corner", 0)]


def test_gol_candidato_confirmado_por_el_saque_de_centro():
    balones = [(11000, 3500), (11600, 3500), (12200, 3500), (12300, 3500)] + [None] * 10 + [(6000, 3500)] * 6
    instantes = _instantes(balones)
    toques = [Toque(7, 0, 0.0, 0.2, (11000, 3500), (11600, 3500))]
    r = detectar_eventos(instantes, toques, [1] * len(instantes), lambda e, m: DIRECCION[e], lambda e: set())
    goles = _tipos(r, "gol")
    assert len(goles) == 1 and goles[0].equipo == 0 and goles[0].tracklet == 7
    assert goles[0].confianza >= 0.8
    assert _tipos(r, "saque_centro")
    tiros = _tipos(r, "tiro")
    assert tiros and tiros[0].tracklet == 7


def test_tiro_a_puerta_y_parada_del_portero():
    # Balón lanzado desde 9 m de la portería derecha a 15 m/s hacia el centro de la portería.
    balones = [(9000, 3500), (9300, 3500), (9600, 3500), (9900, 3500), (10200, 3500), (10500, 3500), (10520, 3500)]
    guardameta = portero(9, 1, (11800, 3500))
    instantes = _instantes(balones, personas=[guardameta])
    toques = [Toque(4, 0, 0.0, 0.2, (9000, 3500), (9300, 3500)), Toque(9, 1, 1.1, 1.3, (10500, 3500), (10520, 3500))]
    r = detectar_eventos(instantes, toques, [1] * len(instantes), lambda e, m: DIRECCION[e], lambda e: {9} if e == 1 else set())
    tiros = _tipos(r, "tiro")
    assert len(tiros) == 1 and tiros[0].detalle == "A puerta" and tiros[0].tracklet == 4
    paradas = _tipos(r, "parada")
    assert len(paradas) == 1 and paradas[0].tracklet == 9


def test_un_despeje_del_defensor_no_es_tiro():
    balones = [(9000, 3500), (9300, 3500), (9600, 3500), (9900, 3500)]
    instantes = _instantes(balones)
    toques = [Toque(5, 1, 0.0, 0.2, (9000, 3500), (9300, 3500))]  # el equipo 1 ataca hacia x = 0
    r = detectar_eventos(instantes, toques, [1] * 4, lambda e, m: DIRECCION[e], lambda e: set())
    assert not _tipos(r, "tiro")


def test_paron_largo_sin_balon_y_descanso():
    balones = [(6000, 3500)] * 5 + [None] * 150 + [(6000, 3500)] * 5
    instantes = _instantes(balones)
    r = detectar_eventos(instantes, [], [1] * len(instantes), lambda e, m: None, lambda e: set(), parametros=Parametros(paron_s=20))
    parones = _tipos(r, "paron")
    assert len(parones) == 1 and parones[0].t_fin is not None
    assert r.juego_activo[120] is False and r.juego_activo[2] is True
    # Con un tramo corto no hay descanso; se da a mano y se respeta.
    assert detectar_descanso(instantes, r.eventos, 32.0, None) is None
    assert detectar_descanso(instantes, r.eventos, 32.0, 15.0) == 15.0


def test_el_descanso_es_el_paron_mas_largo_del_medio():
    largo = [(6000, 3500)] * 100 + [None] * 400 + [(6000, 3500)] * 100
    instantes = _instantes(largo, dt=5.0)  # 50 min de tramo, parón de 33 min en medio
    r = detectar_eventos(instantes, [], [1] * len(instantes), lambda e, m: None, lambda e: set())
    assert detectar_descanso(instantes, r.eventos, 3000.0, None) == instantes[500].t
