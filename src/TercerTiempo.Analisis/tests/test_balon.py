from tt_analisis.balon import cobertura, distancia_a_jugadores_m, quitar_aislados, rellenar_huecos
from tt_analisis.tipos import Instante

from conftest import instante, persona


def _con_balon(i, balon, conf=0.6):
    inst = instante(i, i * 0.2, [persona(1, 0, (1000, 1000))], balon)
    inst.balon_confianza = None if balon is None else conf
    return inst


def test_un_hueco_corto_se_rellena_en_linea_recta_en_el_campo_y_en_la_imagen():
    instantes = [_con_balon(0, (1000, 1000)), _con_balon(1, None), _con_balon(2, None), _con_balon(3, (1600, 1300))]
    relleno = rellenar_huecos(instantes)
    assert (relleno.huecos, relleno.interpolados, relleno.huecos_largos, relleno.huecos_implausibles) == (1, 2, 0, 0)
    assert instantes[1].balon_campo == (1200.0, 1100.0) and instantes[2].balon_campo == (1400.0, 1200.0)
    assert instantes[1].balon_imagen == (120.0, 110.0)
    assert instantes[1].balon_origen == "interpolado" and instantes[0].balon_origen == "visto"
    assert instantes[1].balon_confianza is None
    c = cobertura(instantes)
    assert (c["vistos"], c["interpolados"], c["pct_vistos"], c["pct_con_balon"]) == (2, 2, 50.0, 100.0)


def test_un_hueco_largo_o_un_salto_imposible_no_se_rellena():
    largo = [_con_balon(0, (1000, 1000))] + [_con_balon(i, None) for i in range(1, 12)] + [_con_balon(12, (1200, 1000))]
    relleno = rellenar_huecos(largo, max_hueco_s=2.0)
    assert relleno.huecos_largos == 1 and relleno.interpolados == 0
    assert all(i.balon_imagen is None for i in largo[1:12])
    # 60 m en 0,4 s: no es el mismo balón.
    salto = [_con_balon(0, (1000, 1000)), _con_balon(1, None), _con_balon(2, (7000, 1000))]
    relleno = rellenar_huecos(salto)
    assert relleno.huecos_implausibles == 1 and salto[1].balon_imagen is None


def test_sin_campo_en_un_extremo_solo_se_interpola_la_imagen():
    a = _con_balon(0, (1000, 1000))
    b = Instante(2, 0.4, [persona(1, 0, (1000, 1000))], (160.0, 100.0), None, False)
    medio = _con_balon(1, None)
    rellenar_huecos([a, medio, b])
    assert medio.balon_imagen == (130.0, 100.0) and medio.balon_campo is None


def test_un_avistamiento_suelto_flojo_y_lejos_de_todos_se_quita():
    # El jugador está en (1000, 1000): un «balón» a 40 m, solo, con confianza 0,3, es una marca.
    instantes = [_con_balon(0, None)] * 0 + [_con_balon(i, None) for i in range(0, 10)]
    instantes[5] = _con_balon(5, (5000, 1000), conf=0.3)
    assert distancia_a_jugadores_m(instantes[5]) == 40.0
    assert quitar_aislados(instantes) == 1
    assert instantes[5].balon_imagen is None and instantes[5].balon_origen is None
    # Con otro avistamiento a menos de 1,5 s, con confianza alta o cerca de un jugador, se queda.
    acompañado = [_con_balon(i, None) for i in range(10)]
    acompañado[5] = _con_balon(5, (5000, 1000), conf=0.3)
    acompañado[9] = _con_balon(9, (5200, 1000), conf=0.3)
    assert quitar_aislados(acompañado) == 0
    seguro = [_con_balon(i, None) for i in range(10)]
    seguro[5] = _con_balon(5, (5000, 1000), conf=0.8)
    assert quitar_aislados(seguro) == 0
    cerca = [_con_balon(i, None) for i in range(10)]
    cerca[5] = _con_balon(5, (1300, 1000), conf=0.3)
    assert quitar_aislados(cerca) == 0


def test_la_distancia_a_los_jugadores_sin_campo_se_estima_por_su_altura():
    from tt_analisis.config import CLASE_JUGADOR
    from tt_analisis.tipos import Persona

    jugador = Persona(1, CLASE_JUGADOR, 0, (400.0, 500.0), None, (385.0, 440.0, 415.0, 500.0), 0.9)  # 60 px = 1,80 m
    inst = Instante(0, 0.0, [jugador], (520.0, 500.0), None, False)
    assert abs(distancia_a_jugadores_m(inst) - 3.6) < 1e-6
    assert distancia_a_jugadores_m(Instante(0, 0.0, [], (1.0, 1.0), None, False)) is None
