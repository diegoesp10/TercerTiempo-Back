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
