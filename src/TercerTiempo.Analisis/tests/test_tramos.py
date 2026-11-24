import numpy as np

from tt_analisis.analizador import tramos_de
from tt_analisis.config import CLASE_PORTERO
from tt_analisis.dorsales import HistorialLecturas
from tt_analisis.porteros import MARGEN_CORTE_S, Porteros
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


def _portero_mezclado(porteros: Porteros) -> np.ndarray:
    """Los votos del tracklet 2, un portero cuyo tracklet saltó a otra persona, con siete lecturas: del 13 si es él y del
    8 si es el otro. Tres del 13 caen a medio margen de un corte."""
    seguidor = Seguidor()
    seguidor.memorias[2] = MemoriaTracklet(2)
    seguidor.memorias[2].votos_clase[CLASE_PORTERO] += 10
    trece, ocho = np.eye(99)[12], np.eye(99)[7]
    medio = MARGEN_CORTE_S / 2
    lecturas = [(5.0, ocho), (10.0 + medio, trece), (12.0, trece), (19.0, ocho), (22.0 + medio, trece), (25.0, trece), (30.0 - medio, trece)]
    historial = HistorialLecturas()
    historial.poner([2] * len(lecturas), [t for t, _ in lecturas], [0] * len(lecturas), [1.0] * len(lecturas), np.stack([p for _, p in lecturas]))
    personas = [Persona(2, CLASE_PORTERO, 0, (0.0, 0.0), (500.0, 3500.0), (0, 0, 10, 20), 0.9)]
    instantes = [Instante(k, float(k), list(personas), None, None, True) for k in range(10, 31)]
    return next(t for t in tramos_de(instantes, seguidor, porteros, lecturas=historial) if t.tracklet == 2).votos


def test_del_portero_cuyo_tracklet_salto_cuentan_las_lecturas_de_su_rato_lejos_de_los_cortes():
    # Es el portero de los 10 a los 30 s salvo de los 18 a los 22, en que el seguidor se fue a otro jugador y volvió.
    votos = _portero_mezclado(Porteros(equipo_de={2: 0}, mezclados={2}, ratos={2: (10.0, 30.0)}, huecos={2: [(18.0, 22.0)]}))
    # Las de los 12 y los 25 s; no la de antes del rato (5 s), la del hueco (19 s) ni las de junto a un corte (justo
    # después del principio, del final del hueco y antes del final).
    assert votos[12] == 2.0 and votos.sum() == 2.0
    # Sin saber dónde se cortó (sin el segundo de cada muestra de la equipación), ninguna.
    assert not _portero_mezclado(Porteros(equipo_de={2: 0}, mezclados={2})).any()
