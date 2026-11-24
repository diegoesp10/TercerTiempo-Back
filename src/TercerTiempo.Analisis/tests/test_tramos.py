import numpy as np

from tt_analisis.analizador import tramos_de
from tt_analisis.config import CLASE_PORTERO
from tt_analisis.porteros import Porteros
from tt_analisis.seguimiento import MemoriaTracklet, Seguidor
from tt_analisis.tipos import Instante, Persona


def test_del_portero_cuentan_las_lecturas_con_las_dos_equipaciones_salvo_si_el_seguidor_salto_a_otra_persona():
    seguidor = Seguidor()
    for tid in (1, 2):
        memoria = seguidor.memorias[tid] = MemoriaTracklet(tid)
        memoria.votos_clase[CLASE_PORTERO] += 10
        # El color de la camiseta del portero cae unas veces en un equipo y otras en el otro.
        memoria.sumar_dorsal(np.eye(99)[0], 1.0, None, 0)
        memoria.sumar_dorsal(np.eye(99)[0], 1.0, None, 1)
    personas = [Persona(tid, CLASE_PORTERO, 1, (0.0, 0.0), (500.0, 3500.0), (0, 0, 10, 20), 0.9) for tid in (1, 2)]
    instantes = [Instante(k, float(k), list(personas), None, None, True) for k in range(10)]
    porteros = Porteros(equipo_de={1: 1, 2: 1}, mezclados={2})
    tramos = {t.tracklet: t for t in tramos_de(instantes, seguidor, porteros)}
    assert tramos[1].portero and tramos[1].por_equipacion and tramos[1].equipo == 1
    assert tramos[1].votos[0] == 2.0
    assert not tramos[2].votos.any()


def test_del_portero_se_cuenta_en_cada_parte_cuanto_esta_junto_a_la_porteria_que_defiende_su_equipo():
    # 10 instantes (5 por segundo) de la primera parte, en la que el equipo 0 defiende la izquierda: el portero está 6 en
    # su portería (x = 5 m), 2 a 35 m de su línea y 2 sin el campo calibrado. El jugador de campo no lleva nada de eso.
    seguidor = Seguidor()
    for tid in (1, 2):
        seguidor.memorias[tid] = MemoriaTracklet(tid)
    seguidor.memorias[1].votos_clase[CLASE_PORTERO] += 10
    xs = [500.0] * 6 + [3500.0] * 2 + [None] * 2
    instantes = [
        Instante(
            k, k * 0.2,
            [Persona(1, CLASE_PORTERO, 0, (0.0, 0.0), None if x is None else (x, 3500.0), (0, 0, 10, 20), 0.9), Persona(2, 2, 0, (0.0, 0.0), (500.0, 3500.0), (0, 0, 10, 20), 0.9)],
            None, None, True,
        )
        for k, x in enumerate(xs)
    ]
    porteros = Porteros(equipo_de={1: 0}, direcciones={(0, 1): 1, (1, 1): -1})
    tramos = {t.tracklet: t for t in tramos_de(instantes, seguidor, porteros, [1] * len(instantes))}
    assert tramos[1].mitad == 1
    segundos, cerca = tramos[1].porteria[1]
    assert abs(segundos - 8 * 0.2) < 1e-9 and abs(cerca - 6 * 0.2) < 1e-9
    assert not tramos[2].porteria
    # Sin las partes no se sabe qué portería defiende.
    assert not next(t for t in tramos_de(instantes, seguidor, porteros) if t.tracklet == 1).porteria
