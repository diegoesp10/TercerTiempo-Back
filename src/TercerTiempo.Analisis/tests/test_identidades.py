import numpy as np

from tt_analisis import identidades

from tt_analisis.identidades import (
    JugadorActa,
    Plantilla,
    Tramo,
    dorsal_de,
    enlazar,
    es_id_jugador,
    id_jugador,
    leer_plantillas,
    minuto_a_segundos,
    resolver,
)


def votos(dorsal: int | None, peso: float = 3.0) -> np.ndarray:
    v = np.zeros(99)
    if dorsal is not None:
        v[dorsal - 1] = peso
    return v


def tramo(tid: int, equipo: int, t_ini: float, t_fin: float, x_ini: float, x_fin: float, dorsal: int | None = None, peso: float = 3.0) -> Tramo:
    return Tramo(tid, equipo, t_ini, t_fin, (x_ini, 500.0), (x_fin, 500.0), 100.0, votos(dorsal, peso))


def test_los_tramos_que_se_siguen_donde_y_cuando_acaba_otro_forman_una_cadena():
    tramos = [tramo(1, 0, 0, 10, 100, 400), tramo(2, 0, 10.6, 20, 420, 600), tramo(3, 0, 10.6, 20, 1500, 1600), tramo(4, 1, 10.4, 20, 405, 500)]
    cadenas = enlazar(tramos)
    juntos = [sorted(t.tracklet for t in c.tramos) for c in cadenas]
    # El 3 empieza lejísimos y el 4 es del otro equipo: solo el 2 sigue al 1.
    assert [1, 2] in juntos
    assert [3] in juntos and [4] in juntos


def test_dos_tramos_que_dicen_dorsales_distintos_no_se_enlazan():
    cadenas = enlazar([tramo(1, 0, 0, 10, 100, 400, dorsal=8), tramo(2, 0, 10.4, 20, 410, 500, dorsal=6)])
    assert len(cadenas) == 2


def test_cada_cadena_es_el_jugador_de_su_dorsal_y_las_lecturas_se_suman():
    tramos = [tramo(1, 0, 0, 10, 100, 400, dorsal=8, peso=0.5), tramo(2, 0, 10.5, 30, 410, 700, dorsal=8, peso=0.5), tramo(3, 1, 0, 30, 1000, 1200, dorsal=9)]
    r = resolver(tramos)
    # Cada tramo por separado no llega a la evidencia mínima; la cadena sí.
    assert r.asignacion[1] == r.asignacion[2] == id_jugador(0, 8)
    assert r.asignacion[3] == id_jugador(1, 9)
    assert [(x.equipo, x.dorsal, sorted(x.tracklets)) for x in r.identidades] == [(0, 8, [1, 2]), (1, 9, [3])]


def test_con_el_acta_solo_cuentan_los_dorsales_de_los_que_jugaron_y_en_sus_minutos():
    v = votos(8, 2.0)
    v[17] = 1.0  # el 18 es del rival: en este equipo es una mala lectura
    tramos = [Tramo(1, 0, 0, 30, (0, 0), (10, 0), 100, v), tramo(2, 0, 3000, 3030, 0, 10, dorsal=3), tramo(4, 1, 0, 30, 2000, 2100, dorsal=18)]
    plantillas = [
        Plantilla("local", "Vallobín", [JugadorActa(8, "OCHO"), JugadorActa(3, "TRES", hasta_s=600.0)]),
        Plantilla("visitante", "Rival", [JugadorActa(18, "DIECIOCHO")]),
    ]
    r = resolver(tramos, plantillas)
    assert r.lado_de_equipo[0] == "local"
    assert r.asignacion[1] == id_jugador(0, 8)
    # El 3 salió en el minuto 10: a los 50 no puede ser él.
    assert 2 not in r.asignacion
    assert next(x for x in r.identidades if x.dorsal == 8).nombre == "OCHO"
    assert r.asignacion[4] == id_jugador(1, 18)


def test_un_dorsal_no_puede_estar_dos_veces_a_la_vez_y_la_cadena_mas_debil_prueba_con_su_segundo_numero():
    fuerte = tramo(1, 0, 0, 60, 0, 100, dorsal=6, peso=5.0)
    debil_v = votos(6, 1.5)
    debil_v[8] = 1.2  # también parece un 9
    debil = Tramo(2, 0, 10, 50, (5000, 0), (5100, 0), 100, debil_v)
    r = resolver([fuerte, debil])
    assert r.asignacion[1] == id_jugador(0, 6)
    assert r.asignacion[2] == id_jugador(0, 9)


def test_sin_continuidad_una_cadena_sin_dorsal_queda_sin_identificar():
    tramos = [tramo(1, 0, 0, 10, 100, 400, dorsal=8), tramo(2, 0, 13, 20, 450, 600)]
    assert 2 not in resolver(tramos).asignacion


def test_con_continuidad_una_cadena_sin_dorsal_en_el_hueco_de_un_solo_jugador_es_ese_jugador(monkeypatch):
    monkeypatch.setattr(identidades, "HUECO_CONTINUIDAD_S", 4.0)
    tramos = [
        tramo(1, 0, 0, 10, 100, 400, dorsal=8),
        # Sin dorsal, empieza 3 s después donde acabó el 8 (demasiado tarde para enlazarlo, a tiempo para la continuidad).
        tramo(2, 0, 13, 20, 450, 600),
        tramo(3, 0, 0, 25, 3000, 3100, dorsal=4),
    ]
    r = resolver(tramos)
    assert r.asignacion[2] == id_jugador(0, 8)
    assert next(x for x in r.identidades if x.dorsal == 8).por_continuidad == 1


def test_sin_acta_como_mucho_dieciseis_jugadores_por_equipo():
    tramos = [tramo(i, 0, i * 100, i * 100 + 30, 0, 10, dorsal=i, peso=1.0 + i / 10) for i in range(1, 21)]
    r = resolver(tramos)
    assert len(r.identidades) == 16
    # Se quedan los de más evidencia.
    assert min(x.dorsal for x in r.identidades) == 5


def test_los_minutos_del_acta_pasan_al_video_con_el_saque_y_el_descanso():
    assert minuto_a_segundos(24, saque_s=90, descanso_s=3300) == 90 + 24 * 60
    assert minuto_a_segundos(60, saque_s=90, descanso_s=3300) == 3300 + 15 * 60
    datos = {"equipos": [{"lado": "local", "nombre": "V", "jugadores": [
        {"dorsal": 18, "nombre": "A", "titular": True, "entra": None, "sale": 24},
        {"dorsal": 15, "nombre": "B", "titular": False, "entra": 24, "sale": None},
        {"dorsal": 19, "nombre": "C", "titular": False, "entra": None, "sale": None},
    ]}]}
    p = leer_plantillas(datos, saque_s=0, descanso_s=3000)[0]
    assert [(j.dorsal, j.desde_s, j.hasta_s) for j in p.jugadores] == [(18, None, 1440.0), (15, 1440.0, None)]


def test_el_identificador_de_un_jugador_lleva_su_equipo_y_su_dorsal():
    assert es_id_jugador(id_jugador(1, 14)) and dorsal_de(id_jugador(1, 14)) == 14
    assert not es_id_jugador(21700)


def test_con_un_video_de_una_sola_parte_solo_cuentan_los_que_estuvieron_en_ella():
    jugadores = [
        {"dorsal": 18, "nombre": "A", "titular": True, "entra": None, "sale": 24},
        {"dorsal": 15, "nombre": "B", "titular": False, "entra": 24, "sale": None},
        {"dorsal": 7, "nombre": "C", "titular": False, "entra": 60, "sale": None},
    ]
    primera = leer_plantillas({"parte": "primera", "minutos_parte": 40, "equipos": [{"lado": "local", "nombre": "V", "jugadores": jugadores}]}, saque_s=30)[0]
    assert [(j.dorsal, j.desde_s, j.hasta_s) for j in primera.jugadores] == [(18, None, 30 + 24 * 60.0), (15, 30 + 24 * 60.0, None)]
    segunda = leer_plantillas({"parte": "segunda", "minutos_parte": 40, "equipos": [{"lado": "local", "nombre": "V", "jugadores": jugadores}]}, saque_s=0)[0]
    # El 18 ya no estaba; el 15 está desde el principio y el 7 entra a los 20 minutos de la segunda parte.
    assert [(j.dorsal, j.desde_s, j.hasta_s) for j in segunda.jugadores] == [(15, None, None), (7, 20 * 60.0, None)]


def tramo_portero(tid: int, equipo: int, t_ini: float, t_fin: float, x: float, dorsal: int | None = None, peso: float = 0.5) -> Tramo:
    return Tramo(tid, equipo, t_ini, t_fin, (x, 3500.0), (x, 3500.0), 100.0, votos(dorsal, peso), None, None, True)


def asignados(r, *tracklets) -> list[int | None]:
    return [r.asignacion.get(t) for t in tracklets]


def test_los_tramos_del_portero_de_las_dos_partes_son_uno_y_su_dorsal_sale_de_todos_juntos():
    # El portero del equipo 0 en la portería de la izquierda en la primera parte y en la de la derecha en la segunda: de
    # lejos se le lee poco el número (0,5 por tramo, ninguno llega solo) y en dos tramos no se le lee.
    tramos = [
        tramo_portero(10, 0, 0, 600, 400, dorsal=13),
        tramo_portero(11, 0, 620, 1500, 400),
        tramo_portero(12, 0, 1600, 2700, 400, dorsal=13),
        tramo_portero(13, 0, 3000, 4000, 11600, dorsal=13),
        tramo_portero(14, 0, 4100, 5400, 11600),
        tramo_portero(20, 1, 0, 1300, 11600, dorsal=1, peso=0.6),
        tramo_portero(21, 1, 1400, 2700, 11600, dorsal=1, peso=0.6),
        tramo_portero(22, 1, 3000, 5400, 400, dorsal=1, peso=0.6),
    ]
    r = resolver(tramos)
    assert asignados(r, 10, 11, 12, 13, 14) == [id_jugador(0, 13)] * 5
    assert asignados(r, 20, 21, 22) == [id_jugador(1, 1)] * 3
    assert [(x.equipo, x.dorsal, sorted(x.tracklets)) for x in r.identidades] == [(0, 13, [10, 11, 12, 13, 14]), (1, 1, [20, 21, 22])]


def test_con_el_acta_el_portero_solo_puede_ser_de_los_de_su_equipo():
    # Un tramo del portero en el que se lee con fuerza un 18, que es del rival: sin el acta gana esa mala lectura.
    tramos = [
        tramo(1, 0, 0, 30, 5000, 5100, dorsal=8),
        tramo(2, 1, 0, 30, 7000, 7100, dorsal=18),
        tramo_portero(10, 0, 0, 900, 400, dorsal=18, peso=2.0),
        tramo_portero(11, 0, 1000, 2000, 400, dorsal=13, peso=0.8),
        tramo_portero(12, 0, 3000, 4000, 11600, dorsal=13, peso=0.8),
    ]
    plantillas = [
        Plantilla("local", "Vallobín", [JugadorActa(13, "PORTERO"), JugadorActa(8, "OCHO")]),
        Plantilla("visitante", "Rival", [JugadorActa(18, "DIECIOCHO"), JugadorActa(1, "UNO")]),
    ]
    assert asignados(resolver(tramos), 10, 11, 12) == [id_jugador(0, 18)] * 3
    r = resolver(tramos, plantillas)
    assert asignados(r, 10, 11, 12) == [id_jugador(0, 13)] * 3
    assert next(x for x in r.identidades if x.id == id_jugador(0, 13)).nombre == "PORTERO"
    assert r.asignacion[2] == id_jugador(1, 18)


def test_dos_porteros_del_mismo_equipo_a_la_vez_no_puede_ser_y_el_mas_corto_se_resuelve_como_jugador_de_campo():
    # Mientras el portero está en su portería, el detector toma por portero a un jugador de campo: sin número leído (12)
    # o con el suyo, el 7 (13).
    tramos = [
        tramo_portero(10, 0, 0, 2000, 400, dorsal=13, peso=1.0),
        tramo_portero(11, 0, 2100, 2700, 400, dorsal=13, peso=0.6),
        tramo_portero(12, 0, 500, 560, 3000),
        tramo_portero(13, 0, 1200, 1260, 3000, dorsal=7, peso=1.2),
    ]
    r = resolver(tramos)
    assert asignados(r, 10, 11, 12, 13) == [id_jugador(0, 13), id_jugador(0, 13), None, id_jugador(0, 7)]
    # La línea del 7 no es de un portero aunque el detector marcase uno de sus tramos: su foto es la de un jugador de campo.
    assert {x.dorsal: x.portero for x in r.identidades} == {13: True, 7: False}


def test_un_tramo_de_portero_que_dice_con_claridad_otro_numero_no_es_del_portero():
    tramos = [
        tramo_portero(10, 0, 0, 900, 400, dorsal=13, peso=1.0),
        tramo_portero(11, 0, 1000, 2000, 400, dorsal=13, peso=1.0),
        tramo_portero(12, 0, 2100, 2200, 400, dorsal=5, peso=1.5),
        tramo_portero(13, 0, 3000, 4000, 11600, dorsal=13, peso=1.0),
    ]
    r = resolver(tramos)
    assert asignados(r, 10, 11, 13) == [id_jugador(0, 13)] * 3
    assert r.asignacion[12] == id_jugador(0, 5)


def test_si_cambian_al_portero_cada_uno_se_queda_con_los_tramos_de_sus_minutos():
    # El 1 juega hasta el segundo 3000 y el 13 desde ahí (con el margen del reloj, hasta 5 min antes o después).
    tramos = [
        tramo(1, 0, 0, 30, 5000, 5100, dorsal=8),
        tramo(2, 1, 0, 30, 7000, 7100, dorsal=9),
        tramo_portero(10, 0, 0, 1000, 400, dorsal=1, peso=0.8),
        tramo_portero(11, 0, 1100, 2600, 400, dorsal=1, peso=0.8),
        tramo_portero(12, 0, 3400, 4400, 11600, dorsal=13, peso=0.8),
        tramo_portero(13, 0, 4500, 5400, 11600, dorsal=13, peso=0.8),
        tramo_portero(14, 0, 5500, 6000, 11600),
    ]
    plantillas = [
        Plantilla("local", "Vallobín", [JugadorActa(1, "UNO", hasta_s=3000.0), JugadorActa(13, "TRECE", desde_s=3000.0), JugadorActa(8, "OCHO")]),
        Plantilla("visitante", "Rival", [JugadorActa(9, "NUEVE")]),
    ]
    r = resolver(tramos, plantillas)
    assert asignados(r, 10, 11) == [id_jugador(0, 1)] * 2
    assert asignados(r, 12, 13, 14) == [id_jugador(0, 13)] * 3


def test_un_jugador_de_campo_con_el_dorsal_del_portero_a_la_vez_que_el_prueba_con_su_segundo_numero():
    # El 20 se lee como 13 (y algo como 4) mientras el portero 13 está en su portería; el 21, también tomado como jugador
    # de campo, cae en un hueco en el que al portero no se le ve: es el portero.
    otro = votos(13, 1.2)
    otro[3] = 0.9
    tramos = [
        tramo_portero(10, 0, 0, 1000, 400, dorsal=13, peso=0.8),
        tramo_portero(11, 0, 2000, 2700, 400, dorsal=13, peso=0.8),
        Tramo(20, 0, 500, 600, (5000.0, 3000.0), (5100.0, 3000.0), 100.0, otro),
        Tramo(21, 0, 1200, 1800, (400.0, 3500.0), (420.0, 3500.0), 100.0, votos(13, 1.0)),
    ]
    r = resolver(tramos)
    assert r.asignacion[20] == id_jugador(0, 4)
    assert asignados(r, 10, 11, 21) == [id_jugador(0, 13)] * 3


def tramo_equipacion(
    tid: int, equipo: int, t_ini: float, t_fin: float, x: float, lecturas: dict[int, float], junto: float | None = 0.95, calibrado: float = 0.5, por_equipacion: bool = True
) -> Tramo:
    """Un tramo del portero por la equipación, con sus lecturas del dorsal (dorsal → peso): con el campo calibrado la
    parte `calibrado` del tramo y, de ese tiempo, la parte `junto` al lado o debajo de su portería (None: sin campo). La
    primera parte acaba en el segundo 2900."""
    v = np.zeros(99)
    for dorsal, peso in lecturas.items():
        v[dorsal - 1] = peso
    mitad = 1 if t_ini < 2900 else 2
    segundos = (t_fin - t_ini) * calibrado
    porteria = {} if junto is None else {mitad: (segundos, segundos * junto)}
    return Tramo(tid, equipo, t_ini, t_fin, (x, 3500.0), (x, 3500.0), 100.0, v, None, None, True, por_equipacion, mitad, porteria)


def test_el_dorsal_que_lleva_un_jugador_de_campo_mientras_se_ve_al_portero_no_es_el_del_portero():
    # Como el 13 del Real Titánico en el J8: se le lee sobre todo un 8, el de un defensa que se ve a la vez que él.
    tramos = [
        tramo(1, 0, 0, 600, 1500, 2500, dorsal=8),
        tramo_equipacion(10, 0, 0, 900, 400, {8: 2.0, 13: 0.5}),
        tramo_equipacion(11, 0, 1000, 2000, 400, {8: 1.0, 13: 0.6}),
        tramo_equipacion(12, 0, 3000, 4000, 11600, {13: 0.6, 6: 0.4}),
        # Un tramo que solo el detector marca de portero, a la vez que el portero: se resuelve como uno de campo.
        Tramo(13, 0, 100, 160, (3000.0, 3500.0), (3100.0, 3500.0), 100.0, votos(5, 1.5), None, None, True),
    ]
    r = resolver(tramos)
    # Aunque en el tramo 10 se lea con claridad el 8: es el portero por la equipación.
    assert asignados(r, 10, 11, 12) == [id_jugador(0, 13)] * 3
    assert asignados(r, 1, 13) == [id_jugador(0, 8), id_jugador(0, 5)]
    portero = next(x for x in r.identidades if x.dorsal == 13)
    assert not portero.por_descarte and portero.confianza < 0.5


def test_si_se_sabe_como_viste_el_portero_ningun_jugador_de_campo_lleva_su_dorsal():
    # Por la equipación, ningún tracklet de campo es el portero 13: ni el defensa al que se lee 13 (y algo de 4) cuando
    # al portero no se le ve, ni el que solo dice 13, ni el que sigue justo donde acaba el último tramo del portero.
    defensa = votos(13, 1.2)
    defensa[3] = 0.9
    tramos = [
        tramo_equipacion(10, 0, 0, 1000, 400, {13: 2.0}),
        tramo_equipacion(11, 0, 2000, 2700, 400, {13: 1.0}),
        Tramo(20, 0, 1200, 1800, (400.0, 3500.0), (420.0, 3500.0), 100.0, defensa),
        Tramo(22, 0, 1850, 1950, (5000.0, 3500.0), (5100.0, 3500.0), 100.0, votos(13, 1.5)),
        Tramo(21, 0, 2700, 2750, (400.0, 3500.0), (600.0, 3500.0), 100.0, np.zeros(99)),
    ]
    r = resolver(tramos)
    assert asignados(r, 10, 11) == [id_jugador(0, 13)] * 2
    assert asignados(r, 20, 22, 21) == [id_jugador(0, 4), None, None]

    # Si solo lo marca el detector, como hasta ahora: el que cae en un hueco del portero con su dorsal es él.
    solo_detector = [tramo_portero(10, 0, 0, 1000, 400, dorsal=13, peso=2.0), tramo_portero(11, 0, 2000, 2700, 400, dorsal=13, peso=1.0)]
    r = resolver(solo_detector + tramos[2:])
    assert asignados(r, 10, 11, 20, 22) == [id_jugador(0, 13)] * 4


def test_si_las_lecturas_no_bastan_el_portero_sale_del_acta_por_descarte():
    # Como el 1 del Vallobín en el J8: casi no se le lee el número y lo poco que se lee es de otros (el 9, que es un
    # jugador de campo, y el 6 y el 21, que no jugaron en su equipo).
    de_campo = [
        tramo(1, 1, 0, 600, 1500, 2500, dorsal=4),
        tramo(2, 1, 0, 600, 3000, 3500, dorsal=9),
        tramo(3, 1, 1000, 1500, 3000, 3500, dorsal=5),
        tramo(4, 1, 3500, 4000, 3000, 3500, dorsal=12),
        tramo(5, 0, 0, 600, 7000, 7100, dorsal=7),
    ]
    porteros = [
        tramo_equipacion(20, 1, 0, 1200, 11600, {9: 0.8, 6: 0.5}),
        tramo_equipacion(21, 1, 1300, 2700, 11600, {21: 0.7}),
        tramo_equipacion(22, 1, 3000, 5400, 400, {9: 0.4}),
    ]
    vallobin = Plantilla("local", "Vallobín", [JugadorActa(1, "UNO"), JugadorActa(4, "CUATRO"), JugadorActa(5, "CINCO", hasta_s=3000.0), JugadorActa(9, "NUEVE"), JugadorActa(12, "DOCE", desde_s=3000.0)])
    rival = Plantilla("visitante", "Rival", [JugadorActa(7, "SIETE")])
    r = resolver(de_campo + porteros, [vallobin, rival])
    assert asignados(r, 20, 21, 22) == [id_jugador(1, 1)] * 3
    portero = next(x for x in r.identidades if x.id == id_jugador(1, 1))
    assert portero.por_descarte and portero.nombre == "UNO"
    assert asignados(r, 1, 2, 3, 4) == [id_jugador(1, 4), id_jugador(1, 9), id_jugador(1, 5), id_jugador(1, 12)]

    # Sin el acta no hay descarte posible y, sin dorsal, sus tramos no se juntan con ningún jugador de campo (el 9).
    r = resolver(de_campo + porteros)
    assert asignados(r, 20, 21, 22) == [None] * 3
    assert r.segundos_sin_identificar[1] >= 5000

    # Si queda más de uno que jugó todo el partido sin identificar (aquí el 4), tampoco: mejor sin identificar.
    r = resolver(de_campo[1:] + porteros, [vallobin, rival])
    assert asignados(r, 20, 21, 22) == [None] * 3


def test_sin_leerle_el_dorsal_el_descarte_pide_que_la_equipacion_y_la_posicion_lo_confirmen():
    # Al portero del equipo 1 no se le lee nada; por el acta solo puede ser el 1 (el 4 y el 9 se ven en el campo).
    de_campo = [tramo(1, 1, 0, 600, 1500, 2500, dorsal=4), tramo(2, 1, 0, 600, 3000, 3500, dorsal=9), tramo(5, 0, 0, 600, 7000, 7100, dorsal=7)]
    vallobin = Plantilla("local", "Vallobín", [JugadorActa(1, "UNO"), JugadorActa(4, "CUATRO"), JugadorActa(9, "NUEVE")])
    rival = Plantilla("visitante", "Rival", [JugadorActa(7, "SIETE")])

    def portero(**como):
        return [tramo_equipacion(20, 1, 0, 1200, 11600, {}, **como), tramo_equipacion(22, 1, 3000, 5400, 400, {}, **como)]

    def lineas(porteros, *tracklets):
        return asignados(resolver(de_campo + porteros, [vallobin, rival]), *tracklets)

    # Vestido de portero y casi siempre junto a su portería con el campo calibrado: es el 1.
    assert lineas(portero(), 20, 22) == [id_jugador(1, 1)] * 2
    # Sin campo calibrado no se sabe dónde está; con 36 s calibrados entre las dos partes (34 junto a su portería), no
    # llega al minuto que hace falta; y si solo lo marca el detector, la equipación no lo confirma: mejor sin identificar.
    assert lineas(portero(junto=None), 20, 22) == [None] * 2
    assert lineas(portero(calibrado=0.01), 20, 22) == [None] * 2
    assert lineas(portero(por_equipacion=False), 20, 22) == [None] * 2


def test_un_rival_sin_dorsal_que_el_detector_toma_por_portero_no_entra_en_la_linea_del_portero_por_descarte():
    # Como en el J8: entre dos tramos del 1 del Vallobín (por la equipación, sin leerle el número), el detector marca de
    # portero a un jugador del Real Titánico que remata en su área y del que no se ve el dorsal.
    de_campo = [tramo(1, 1, 0, 600, 1500, 2500, dorsal=4), tramo(5, 0, 0, 600, 7000, 7100, dorsal=7)]
    vallobin = Plantilla("local", "Vallobín", [JugadorActa(1, "RUEDA"), JugadorActa(4, "CUATRO")])
    rival = Plantilla("visitante", "Real Titánico", [JugadorActa(7, "SIETE")])
    rematador = Tramo(30, 1, 1250, 1290, (11000.0, 3500.0), (11100.0, 3500.0), 100.0, np.zeros(99), None, None, True, False, 1, {1: (40.0, 40.0)})
    porteros = [tramo_equipacion(20, 1, 0, 1200, 11600, {}), tramo_equipacion(22, 1, 3000, 5400, 400, {}), rematador]
    r = resolver(de_campo + porteros, [vallobin, rival])
    assert asignados(r, 20, 22, 30) == [id_jugador(1, 1), id_jugador(1, 1), None]
    rueda = next(x for x in r.identidades if x.id == id_jugador(1, 1))
    assert rueda.por_descarte and sorted(rueda.tracklets) == [20, 22]


def test_el_tramo_del_detector_que_viste_como_su_portero_no_acaba_en_la_linea_de_un_jugador_de_campo():
    # Como en el 20:00-30:00 del J8: al 13 no se le identifica, y 5 s suyos que solo marca el detector, pero en los que
    # viste de morado como él, siguen justo donde acaba un tramo del 17.
    diecisiete = tramo(1, 0, 0, 100, 1000, 1400, dorsal=17)

    def portero(vestido: bool) -> Tramo:
        return Tramo(2, 0, 100.4, 105.4, (1400.0, 500.0), (1450.0, 500.0), 100.0, np.zeros(99), None, None, True, vestido_de_portero=vestido)

    r = resolver([diecisiete, portero(True)])
    assert asignados(r, 1, 2) == [id_jugador(0, 17), None]
    assert r.segundos_sin_identificar[0] == 5.0
    # El que no viste como el portero (o no se sabe) se resuelve como uno de campo: un jugador que el detector tomó por
    # portero.
    assert asignados(resolver([diecisiete, portero(False)]), 1, 2) == [id_jugador(0, 17)] * 2


def test_en_la_parte_en_que_no_esta_junto_a_su_porteria_no_es_su_portero():
    # El 13 del equipo 0 pasa la primera parte junto a su portería; en la segunda, lo que se tomó por él solo está ahí el
    # 30 % del tiempo con el campo calibrado: esa parte no es suya (en el J8 los porteros pasan ahí del 84 al 98 %).
    tramos = [
        tramo_equipacion(10, 0, 0, 1300, 400, {13: 1.0}),
        tramo_equipacion(11, 0, 1400, 2700, 400, {13: 1.0}),
        tramo_equipacion(12, 0, 3000, 4000, 11600, {13: 0.4}, junto=0.3),
        tramo_equipacion(13, 0, 4100, 5400, 11600, {}, junto=0.3),
    ]
    r = resolver(tramos)
    assert asignados(r, 10, 11, 12, 13) == [id_jugador(0, 13)] * 2 + [None] * 2
    assert r.segundos_sin_identificar[0] >= 2300
    # Con el 75 %, o con muy poco campo calibrado en esa parte (10 s), sí es él.
    tramos[2:] = [tramo_equipacion(12, 0, 3000, 4000, 11600, {13: 0.4}, junto=0.75), tramo_equipacion(13, 0, 4100, 5400, 11600, {}, junto=0.75)]
    assert asignados(resolver(tramos), 10, 11, 12, 13) == [id_jugador(0, 13)] * 4
    tramos[2:] = [tramo_equipacion(12, 0, 3000, 4000, 11600, {13: 0.4}, junto=0.3, calibrado=5 / 1000), tramo_equipacion(13, 0, 4100, 5400, 11600, {}, junto=0.3, calibrado=5 / 1300)]
    assert asignados(resolver(tramos), 10, 11, 12, 13) == [id_jugador(0, 13)] * 4


def test_si_el_acta_dice_quien_es_portero_su_dorsal_solo_puede_ser_uno_de_ellos():
    # Al portero del equipo 1 se le lee más el 9 (un jugador de campo al que no se ve a la vez que él) que su 1.
    tramos = [
        tramo(5, 0, 0, 600, 7000, 7100, dorsal=7),
        tramo_equipacion(20, 1, 0, 1200, 11600, {9: 1.2, 1: 0.6}),
        tramo_equipacion(22, 1, 3000, 5400, 400, {9: 0.8, 1: 1.0}),
    ]
    jugadores = [
        {"dorsal": 1, "nombre": "UNO", "titular": True, "entra": None, "sale": None, "portero": True},
        {"dorsal": 9, "nombre": "NUEVE", "titular": True, "entra": None, "sale": None},
    ]
    rival = {"lado": "visitante", "nombre": "Rival", "jugadores": [{"dorsal": 7, "nombre": "SIETE", "titular": True, "entra": None, "sale": None}]}
    plantillas = leer_plantillas({"equipos": [{"lado": "local", "nombre": "Vallobín", "jugadores": jugadores}, rival]})
    assert plantillas[0].porteros() == {1} and plantillas[1].porteros() == set()
    assert asignados(resolver(tramos, plantillas), 20, 22) == [id_jugador(1, 1)] * 2
    # Si el acta no lo dice, gana la lectura: el 9.
    for j in jugadores:
        j.pop("portero", None)
    assert asignados(resolver(tramos, leer_plantillas({"equipos": [{"lado": "local", "nombre": "Vallobín", "jugadores": jugadores}, rival]})), 20, 22) == [id_jugador(1, 9)] * 2
