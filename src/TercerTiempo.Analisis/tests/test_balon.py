from tt_analisis.balon import cobertura, distancia_a_jugadores_m, objetos_fijos, permanencias, quitar_aislados, quitar_objetos_fijos, rellenar_huecos
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


def _partido_con_paradas(paradas: list[tuple[float, float, tuple[float, float]]]) -> list[Instante]:
    """Instantes cada 0,2 s con el balón quieto en cada (t_inicio, duración, punto) y sin balón el resto; un jugador
    en (1000, 1000)."""
    fin = max(t + d for t, d, _ in paradas) + 5
    instantes = []
    for k in range(int(fin / 0.2)):
        t = round(k * 0.2, 1)
        balon = next(((xy[0] + (k % 3) * 20, xy[1]) for t0, d, xy in paradas if t0 <= t <= t0 + d), None)
        instantes.append(instante(k, t, [persona(1, 0, (1000, 1000))], balon))
    return instantes


def test_las_permanencias_son_los_tramos_de_balon_quieto():
    instantes = _partido_con_paradas([(10.0, 3.0, (3000, 2000)), (30.0, 1.0, (5000, 2000))])
    perms = permanencias(instantes)
    assert len(perms) == 1
    assert perms[0].t_inicio == 10.0 and abs(perms[0].duracion - 3.0) < 1e-6
    assert abs(perms[0].centro[0] - 3020) <= 20 and perms[0].centro[1] == 2000


def test_un_objeto_que_se_para_una_y_otra_vez_en_el_mismo_sitio_se_quita():
    # Cinco «paradas» en (6900, 4100) entre los minutos 1 y 15, una falta de verdad en (3000, 2000) y cinco saques de
    # centro: solo lo primero es un objeto.
    objeto = [(60.0 + 200 * k, 3.0, (6900, 4100)) for k in range(5)]
    centro = [(80.0 + 200 * k, 3.0, (6000, 3500)) for k in range(5)]
    instantes = _partido_con_paradas(objeto + centro + [(500.0, 4.0, (3000, 2000))])
    objetos = objetos_fijos(permanencias(instantes))
    assert len(objetos) == 1
    o = objetos[0]
    assert abs(o.centro[0] - 6920) <= 20 and o.centro[1] == 4100 and o.permanencias == 5
    assert o.radio >= 250
    vistos = sum(1 for i in instantes if i.balon_imagen is not None)
    assert quitar_objetos_fijos(instantes, objetos) == 5 * 16
    assert sum(1 for i in instantes if i.balon_imagen is not None) == vistos - 5 * 16
    # La falta y los saques de centro siguen.
    assert any(i.balon_campo is not None and i.balon_campo[0] < 3100 for i in instantes)
    assert any(i.balon_campo is not None and abs(i.balon_campo[0] - 6000) <= 40 for i in instantes)


def test_pocas_paradas_o_muy_seguidas_no_son_un_objeto():
    tres = [(60.0 + 300 * k, 3.0, (6900, 4100)) for k in range(3)]
    assert objetos_fijos(permanencias(_partido_con_paradas(tres))) == []
    seguidas = [(60.0 + 60 * k, 3.0, (6900, 4100)) for k in range(6)]
    assert objetos_fijos(permanencias(_partido_con_paradas(seguidas))) == []


def test_junto_a_un_objeto_fijo_solo_se_queda_el_balon_que_va_de_paso():
    objeto = [(60.0 + 200 * k, 3.0, (6900, 4100)) for k in range(5)]
    objetos = objetos_fijos(permanencias(_partido_con_paradas(objeto)))
    jugador = persona(7, 1, (6950, 4150))
    # Mientras el objeto está en el césped, lo que se queda dentro es el objeto, aunque haya un jugador al lado...
    quieto = [instante(0, 500.0, [jugador], (6900, 4100)), instante(1, 500.2, [jugador], (6900, 4100))]
    assert quitar_objetos_fijos(quieto, objetos) == 2 and quieto[0].balon_campo is None
    # ...y el balón que entra y sale a una velocidad posible va de paso.
    de_paso = [instante(0, 500.0, [jugador], (6300, 4100)), instante(1, 500.2, [jugador], (6900, 4100)), instante(2, 500.4, [jugador], (7500, 4100))]
    assert quitar_objetos_fijos(de_paso, objetos) == 0
    # Más de un minuto después de su última parada ya no está: no se quita nada.
    despues = [instante(0, 2000.0, [jugador], (6900, 4100))]
    assert quitar_objetos_fijos(despues, objetos) == 0 and despues[0].balon_campo == (6900, 4100)
