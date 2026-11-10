from tt_analisis.config import CLASE_ARBITRO
from tt_analisis.estadisticas import direcciones_de_ataque, mapa_equipo, mitades, resumir_tracklets
from tt_analisis.tipos import Persona

from conftest import instante, persona, portero


def test_distancia_velocidad_y_mapa_de_calor(dt):
    # El jugador 1 recorre 5 m en la primera muestra de 0,2 s (25 m/s: imposible, se descarta) y luego 1 m por
    # muestra (5 m/s = 18 km/h) durante un segundo.
    pasos = [(1000, 1000), (1500, 1000)] + [(1500 + 100 * k, 1000) for k in range(1, 7)]
    instantes = [instante(i, i * dt, [persona(1, 0, xy)], None) for i, xy in enumerate(pasos)]
    resumen = resumir_tracklets(instantes, dt)[1]
    assert resumen.muestras == 8 and abs(resumen.segundos - 1.6) < 1e-9
    assert abs(resumen.distancia_m - 6.0) < 1e-9
    # La punta se mide sobre ~1 s: 5 muestras de 1 m en 1,0 s → 18 km/h (el salto imposible no cuenta).
    assert abs(resumen.velocidad_max_kmh - 18.0) < 1e-6
    assert resumen.mapa.sum() == 8 and resumen.mapa[1, 1] == 6 and resumen.mapa[1, 2] == 2
    assert mapa_equipo({1: resumen}, 0).sum() == 8


def test_el_arbitro_no_tiene_equipo_y_los_votos_deciden(dt):
    arbitro = Persona(5, CLASE_ARBITRO, None, (10, 10), (100, 100), (0, 0, 1, 1), 0.9)
    instantes = [instante(i, i * dt, [arbitro, persona(2, 1 if i < 2 else 0, (500, 500))], None) for i in range(5)]
    resumenes = resumir_tracklets(instantes, dt)
    assert resumenes[5].arbitro and resumenes[5].equipo is None
    assert resumenes[2].equipo == 0


def test_direccion_de_ataque_por_los_porteros_y_cambio_de_mitad(dt):
    p0 = portero(10, 0, (500, 3500))
    p1 = portero(11, 1, (11500, 3500))
    primera = [instante(i, i * dt, [p0, p1, persona(1, 0, (3000, 3000)), persona(2, 1, (9000, 3000))], None) for i in range(12)]
    p0b = portero(10, 0, (11500, 3500))
    p1b = portero(11, 1, (500, 3500))
    segunda = [instante(12 + i, 100 + i * dt, [p0b, p1b], None) for i in range(12)]
    instantes = primera + segunda
    mitad_de = mitades(instantes, 50.0)
    assert mitad_de[0] == 1 and mitad_de[-1] == 2
    resumenes = resumir_tracklets(instantes, dt)
    direcciones = direcciones_de_ataque(instantes, mitad_de, resumenes)
    assert direcciones[(0, 1)] == 1 and direcciones[(1, 1)] == -1
    assert direcciones[(0, 2)] == -1 and direcciones[(1, 2)] == 1
