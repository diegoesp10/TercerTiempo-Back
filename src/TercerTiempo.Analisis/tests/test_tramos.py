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
