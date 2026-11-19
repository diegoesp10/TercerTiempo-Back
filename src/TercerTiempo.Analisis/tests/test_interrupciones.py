from tt_analisis.balon import permanencias
from tt_analisis.interrupciones import detectar_interrupciones, en_fuera_de_juego
from tt_analisis.tipos import Instante, Toque

from conftest import instante, persona, portero

DIRECCION = {0: 1, 1: -1}  # el equipo 0 ataca hacia x = 12000


def _direccion(equipo, mitad):
    return DIRECCION[equipo]


def _jugada(balon_en, personas_en, fin_s: float) -> list[Instante]:
    """Un instante cada 0,2 s; `balon_en(t)` y `personas_en(t)` dicen dónde está cada cosa."""
    return [instante(k, round(k * 0.2, 1), personas_en(round(k * 0.2, 1)), balon_en(round(k * 0.2, 1))) for k in range(int(fin_s / 0.2) + 1)]


def _detectar(instantes, toques, cortes=()):
    return detectar_interrupciones(instantes, toques, permanencias(instantes), list(cortes), [1] * len(instantes), _direccion)


def test_la_falta_la_recibe_el_que_tenia_el_balon_y_la_comete_el_rival_mas_cercano():
    # El 1 (equipo 0) conduce hasta (7000, 3000); el 2 (equipo 1) le entra; el balón se queda quieto 6 s y saca el 3.
    def balon(t):
        if t < 10:
            return (6000 + 100 * t, 3000)
        if t <= 16:
            return (7000, 3000)
        return (7000 + 800 * (t - 16), 3000)

    def personas(t):
        x = 6000 + 100 * min(t, 10)
        return [persona(1, 0, (x - 50, 3050)), persona(2, 1, (x + 150, 3100)), persona(3, 0, (6900, 2900)), persona(4, 1, (9000, 5000))]

    instantes = _jugada(balon, personas, 20)
    toques = [Toque(1, 0, 5.0, 10.0, (6500, 3000), (7000, 3000)), Toque(3, 0, 16.0, 16.4, (7000, 3000), (7300, 3000))]
    r = _detectar(instantes, toques)
    assert [(e.tipo, e.equipo, e.tracklet, e.tracklet_rival) for e in r.eventos] == [("falta", 1, 2, 1)]
    # Saca su equipo, el rival estaba encima y los jugadores frenan al pitar: 0,45 + 0,1 + 0,05 + 0,1.
    assert r.eventos[0].confianza == 0.7 and "saca su equipo" in r.eventos[0].detalle
    assert len(r.cortes) == 1 and 9.0 <= r.cortes[0] <= 10.0 and r.paradas and 9.0 <= r.paradas[0][0] <= 10.4


def test_tras_un_fuera_o_sin_juego_antes_no_hay_falta():
    def balon(t):
        return (7000, 3000) if 10 <= t <= 16 else (6000 + 100 * min(t, 10), 3000)

    def personas(t):
        return [persona(1, 0, (6900, 3000)), persona(2, 1, (7200, 3000))]

    instantes = _jugada(balon, personas, 18)
    toques = [Toque(1, 0, 5.0, 10.0, (6500, 3000), (7000, 3000))]
    assert _detectar(instantes, toques, cortes=[2.0]).eventos == []
    assert _detectar(instantes, []).eventos == []


def test_si_saca_el_rival_y_habia_un_atacante_mas_alla_del_penultimo_defensor_es_fuera_de_juego():
    defensa = [portero(20, 1, (11800, 3500)), persona(21, 1, (9800, 3000)), persona(22, 1, (9700, 4000)), persona(23, 1, (9500, 2000))]

    def balon(t):
        if t <= 5:
            return (8000, 3000)
        if t <= 7:
            return (8000 + 1200 * (t - 5), 3000 + 250 * (t - 5))
        if t < 8:
            return (10400, 3500)
        if t <= 12:
            return (9400, 3000)
        return (9400 - 800 * (t - 12), 3000)

    def personas(t):
        return [persona(1, 0, (7950, 3000)), persona(5, 0, (10500, 3500)), persona(6, 1, (9450, 3050))] + defensa

    instantes = _jugada(balon, personas, 15)
    toques = [Toque(1, 0, 3.0, 5.0, (8000, 3000), (8000, 3000)), Toque(5, 0, 7.0, 7.6, (10400, 3500), (10400, 3500)), Toque(6, 1, 12.0, 12.4, (9400, 3000), (9100, 3000))]
    r = _detectar(instantes, toques)
    assert [(e.tipo, e.equipo, e.tracklet) for e in r.eventos] == [("fuera_de_juego", 0, 5)]
    # El 5 estaba por delante del penúltimo defensor (el 21, a 98 m) al salir el pase.
    assert [p.tracklet for p in en_fuera_de_juego(instantes[25], 0, 1, __import__("tt_analisis.geometria", fromlist=["Campo"]).Campo())] == [5]


def test_sin_bastantes_defensores_a_la_vista_no_se_juzga_el_fuera_de_juego():
    inst = instante(0, 0.0, [persona(5, 0, (10500, 3500)), persona(21, 1, (9800, 3000)), portero(20, 1, (11800, 3500))], (8000, 3000))
    from tt_analisis.geometria import Campo

    assert en_fuera_de_juego(inst, 0, 1, Campo()) == []


def test_balon_quieto_en_el_punto_de_penalti_tras_juego_es_penalti():
    def balon(t):
        if t < 6:
            return (10000 + 100 * t, 3500)
        if t <= 14:
            return (10900, 3500)
        return (10900 + 1500 * (t - 14), 3500)

    def personas(t):
        return [persona(1, 0, (10000 + 100 * min(t, 6), 3550)), persona(2, 1, (10700, 3600)), portero(9, 1, (11900, 3500))]

    instantes = _jugada(balon, personas, 16)
    toques = [Toque(1, 0, 2.0, 6.0, (10200, 3500), (10600, 3500)), Toque(1, 0, 14.0, 14.2, (10900, 3500), (11100, 3500))]
    r = _detectar(instantes, toques)
    assert [(e.tipo, e.equipo, e.tracklet, e.tracklet_rival, e.detalle) for e in r.eventos] == [("falta", 1, 2, 1, "Penalti")]


def test_el_portero_con_el_balon_en_su_area_no_es_una_falta():
    def balon(t):
        return (1500, 3500) if t >= 6 else (3000 - 250 * t, 3500)

    def personas(t):
        return [portero(9, 1, (1550, 3500)), persona(1, 0, (2500, 3000)), persona(2, 1, (4000, 3000))]

    instantes = _jugada(balon, personas, 14)
    toques = [Toque(1, 0, 0.0, 2.0, (3000, 3500), (2500, 3500)), Toque(9, 1, 5.8, 14.0, (1500, 3500), (1500, 3500))]
    assert _detectar(instantes, toques).eventos == []


def test_un_frenazo_de_todos_tras_un_choque_es_falta_aunque_no_se_vea_el_balon():
    # Ocho jugadores corriendo a 4 m/s; en el segundo 20 el 1 (equipo 0) pierde el balón con el 2 (equipo 1) encima y
    # todos frenan; el balón no se ve quieto. Se reanuda en el 35.
    def personas(t):
        v = 400 if t < 20 else 0
        x = 4000 + v * min(t, 20) / 1 if t < 20 else 4000 + 400 * 20
        gente = [persona(1, 0, (x, 3000)), persona(2, 1, (x + 150, 3050))]
        gente += [persona(10 + k, k % 2, (x - 500 - 300 * k, 1000 + 500 * k)) for k in range(6)]
        return gente

    def balon(t):
        return (4000 + 400 * t, 3000) if t < 20 else None

    instantes = _jugada(balon, personas, 40)
    toques = [Toque(1, 0, 15.0, 19.8, (10000, 3000), (11900, 3000)), Toque(3, 1, 35.0, 35.4, (12000, 3000), (12100, 3000))]
    r = _detectar(instantes, toques)
    assert [(e.tipo, e.equipo, e.tracklet, e.tracklet_rival) for e in r.eventos] == [("falta", 1, 2, 1)]
    assert r.eventos[0].detalle.startswith("Los jugadores frenan") and r.eventos[0].confianza == 0.45
    # Con un fuera justo antes, el frenazo lo explica el fuera.
    assert _detectar(instantes, toques, cortes=[18.0]).eventos == []
