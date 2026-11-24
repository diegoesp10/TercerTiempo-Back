import numpy as np

from tt_analisis.config import CLASE_ARBITRO, CLASE_JUGADOR, CLASE_PORTERO
from tt_analisis.geometria import Campo
from tt_analisis.porteros import (
    TRACKLET_RESTO_BASE,
    aplicar_porteros,
    asignar_porteros,
    buscar_porteros,
    colocar_porteros,
    describir_porteros,
    equipo_que_defiende,
    lados_de_porteros,
)
from tt_analisis.seguimiento import MemoriaTracklet
from tt_analisis.tipos import Instante, Persona

from conftest import instante, portero

# El equipo 0 defiende la portería de la izquierda en la primera parte (ataca hacia la derecha) y la de la derecha en la
# segunda: en el descanso cambian de campo.
DIRECCIONES = {(0, 1): 1, (1, 1): -1, (0, 2): -1, (1, 2): 1}


def sin_calibrar(tracklet: int) -> Persona:
    return Persona(tracklet, CLASE_PORTERO, None, (60.0, 400.0), None, (0, 0, 10, 20), 0.9)


def partido(*porteros_por_parte):
    """Diez instantes por parte; en cada uno, los porteros de esa parte: (tracklet, x en cm) o una persona ya hecha."""
    instantes, mitad_de = [], []
    for mitad, gente in enumerate(porteros_por_parte, start=1):
        for k in range(10):
            personas = [g if isinstance(g, Persona) else portero(g[0], None, (g[1], 3500)) for g in gente]
            instantes.append(instante(len(instantes), float(len(instantes)), personas, None))
            mitad_de.append(mitad)
    return instantes, mitad_de


def equipos_de(instantes, tracklet):
    return {p.equipo for inst in instantes for p in inst.personas if p.tracklet == tracklet}


def test_cada_portero_es_del_equipo_que_defiende_su_porteria_en_cada_parte():
    # El titular del equipo 0 en la portería de la izquierda en la primera parte (7) y en la de la derecha en la segunda
    # (17); el del equipo 1, al revés (8 y 18).
    instantes, mitad_de = partido([(7, 400), (8, 11600)], [(17, 11500), (18, 300)])
    colocados = asignar_porteros(instantes, mitad_de, DIRECCIONES, {7, 8, 17, 18}, Campo())
    assert colocados == {(7, 1): 0, (8, 1): 1, (17, 2): 0, (18, 2): 1}
    assert equipos_de(instantes, 7) == equipos_de(instantes, 17) == {0}
    assert equipos_de(instantes, 8) == equipos_de(instantes, 18) == {1}


def test_el_lado_es_el_de_la_mitad_del_campo_en_la_que_esta():
    instantes, mitad_de = partido([(7, 2500), (8, 9000)])
    assert lados_de_porteros(instantes, mitad_de, {7, 8}, Campo()) == {(7, 1): -1, (8, 1): 1}
    assert equipo_que_defiende(-1, 1, DIRECCIONES) == 0
    assert equipo_que_defiende(-1, 2, DIRECCIONES) == 1
    assert equipo_que_defiende(1, 2, {}) is None


def test_en_el_centro_del_campo_o_sin_campo_calibrado_el_portero_se_queda_sin_equipo():
    # El 5 está en el círculo central (o no es un portero); al 6 no se le ve nunca con el campo calibrado. Al 7 le faltan
    # posiciones calibradas en algunos fotogramas, pero en esa parte no cambia de portería.
    instantes, mitad_de = partido([(5, 6400), sin_calibrar(6), (7, 500)])
    for inst in instantes[:4]:
        inst.personas[2] = sin_calibrar(7)
    colocados = asignar_porteros(instantes, mitad_de, DIRECCIONES, {5, 6, 7}, Campo())
    assert colocados == {(7, 1): 0}
    assert equipos_de(instantes, 5) == equipos_de(instantes, 6) == {None}
    assert equipos_de(instantes, 7) == {0}


def test_sin_saber_hacia_donde_ataca_cada_equipo_en_esa_parte_no_se_le_pone_equipo():
    instantes, mitad_de = partido([(7, 400)], [(17, 11500)])
    colocados = asignar_porteros(instantes, mitad_de, {(0, 1): 1, (1, 1): -1}, {7, 17}, Campo())
    assert colocados == {(7, 1): 0}
    assert equipos_de(instantes, 17) == {None}


def test_un_jugador_de_campo_no_se_toca():
    instantes, mitad_de = partido([(7, 400)])
    for inst in instantes:
        inst.personas.append(Persona(30, 2, 1, (500.0, 300.0), (6000.0, 3000.0), (0, 0, 10, 20), 0.9))
    asignar_porteros(instantes, mitad_de, DIRECCIONES, {7}, Campo())
    assert equipos_de(instantes, 30) == {1}


# Equipaciones como las del J8 (claridad del torso respecto al césped, a, b; lo mismo del pantalón).
RAYAS = (52.0, 131.0, 131.0, -10.0, 142.0, 135.0)  # equipo 0: rayas rojas y blancas, pantalón rojo
MARINO = (-39.0, 130.0, 127.0, 43.0, 127.0, 132.0)  # equipo 1: azul marino, pantalón blanco
MENTA = (51.0, 107.0, 139.0, -69.0, 124.0, 135.0)  # árbitro: verde menta, pantalón negro
MORADO = (24.0, 159.0, 106.0, 16.0, 142.0, 123.0)  # portero del equipo 0
LIMA = (83.0, 102.0, 149.0, 85.0, 102.0, 148.0)  # portero del equipo 1


class Partido:
    """Dos partes de 100 fotogramas (uno por segundo). Con DIRECCIONES, en la primera el equipo 0 defiende la portería de
    la izquierda (x = 0) y el 1 la de la derecha; en la segunda, al revés."""

    def __init__(self, semilla: int = 3):
        self.rng = np.random.default_rng(semilla)
        self.instantes = [Instante(k, float(k), [], None, None, True) for k in range(200)]
        self.mitad_de = [1 if k < 100 else 2 for k in range(200)]
        self.memorias: dict[int, MemoriaTracklet] = {}

    def tracklet(self, tid, equipacion, x, desde, hasta, clase=CLASE_JUGADOR, camiseta=None, muestras=20, calibrado=True, serie=None):
        """`serie`: la equipación de cada muestra, en orden, si no son todas `equipacion` (el seguidor saltó de una
        persona a otra). Las muestras se reparten por los segundos del tracklet."""
        memoria = self.memorias[tid] = MemoriaTracklet(tid)
        memoria.votos_clase[clase] += hasta - desde
        if camiseta is not None:
            memoria.votos_equipo[camiseta] += 5.0
        serie = serie or [equipacion] * muestras
        memoria.equipaciones = [np.array(e) + self.rng.normal(0, [3, 1, 1, 3, 1, 1]) for e in serie]
        memoria.tiempos_equipacion = [float(t) for t in np.linspace(desde, hasta - 1, len(serie))]
        for k in range(desde, hasta):
            xy = (x + self.rng.normal(0, 100), 3500 + self.rng.normal(0, 300)) if calibrado else None
            equipo = camiseta if clase == CLASE_JUGADOR else None
            self.instantes[k].personas.append(Persona(tid, clase, equipo, (0.0, 0.0), xy, (0, 0, 10, 20), 0.9))

    def con_equipos(self, arbitro: bool = True, equipo_1: bool = True):
        """Seis jugadores de cada equipo en cada parte, en su mitad del campo, y el árbitro por el centro."""
        for i in range(12):
            mitad = 1 if i < 6 else 2
            desde = (0 if mitad == 1 else 100) + (i % 6) * 5
            x = 2500 + (i % 6) * 600
            self.tracklet(100 + i, RAYAS, x if mitad == 1 else 12000 - x, desde, desde + 70, camiseta=0)
            if equipo_1:
                self.tracklet(200 + i, MARINO, 12000 - x if mitad == 1 else x, desde, desde + 70, camiseta=1)
            if arbitro:
                self.tracklet(300 + i, MENTA, 6000 + (i % 6) * 200, desde, desde + 70, clase=CLASE_ARBITRO)
        return self

    def con_porteros(self):
        """El del equipo 0, de morado, que el detector toma por jugador (y la camiseta, del equipo 1); el del equipo 1, de
        verde lima, unas veces portero y otras jugador del equipo 0. Cada uno en su portería y en el descanso cambian."""
        self.tracklet(10, MORADO, 400, 0, 40, camiseta=1)
        self.tracklet(11, MORADO, 450, 50, 90, camiseta=1)
        self.tracklet(12, MORADO, 11600, 100, 140, camiseta=1)
        self.tracklet(13, MORADO, 11550, 150, 190, camiseta=1)
        self.tracklet(20, LIMA, 11600, 0, 45, clase=CLASE_PORTERO)
        self.tracklet(21, LIMA, 11500, 50, 95, camiseta=0)
        self.tracklet(22, LIMA, 500, 100, 190, camiseta=0)
        return self

    def buscar(self, con_tiempos: bool = True):
        m = self.memorias
        equipaciones = {t: np.asarray(x.equipaciones) for t, x in m.items()}
        tiempos = {t: np.asarray(x.tiempos_equipacion) for t, x in m.items()} if con_tiempos else None
        return buscar_porteros(self.instantes, self.mitad_de, DIRECCIONES, equipaciones, {t: x.equipo for t, x in m.items()}, {t: x.clase for t, x in m.items()}, Campo(), tiempos)


def clases_de(instantes, tracklet):
    return {p.clase for inst in instantes for p in inst.personas if p.tracklet == tracklet}


def test_el_portero_es_el_que_viste_distinto_de_los_dos_equipos_y_del_arbitro_junto_a_su_porteria():
    partido = Partido().con_equipos().con_porteros()
    p = partido.buscar()
    assert sorted(p.modelos) == [0, 1]
    # Aunque el detector los tome por jugadores y la camiseta los ponga en el otro equipo: en las dos partes, cada uno
    # del equipo de la portería que defiende.
    assert p.equipo_de == {10: 0, 11: 0, 12: 0, 13: 0, 20: 1, 21: 1, 22: 1}
    assert not p.sin_equipo and not p.de_campo
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    assert equipos_de(partido.instantes, 10) == equipos_de(partido.instantes, 12) == {0}
    assert equipos_de(partido.instantes, 21) == equipos_de(partido.instantes, 22) == {1}
    assert clases_de(partido.instantes, 11) == clases_de(partido.instantes, 21) == {CLASE_PORTERO}
    # Los jugadores de campo y el árbitro, como estaban.
    assert equipos_de(partido.instantes, 100) == {0} and equipos_de(partido.instantes, 300) == {None}


def test_el_que_el_detector_marca_de_portero_pero_viste_como_un_equipo_es_un_jugador_de_ese_equipo():
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(30, RAYAS, 1500, 10, 40, clase=CLASE_PORTERO)
    p = partido.buscar()
    assert p.de_campo == {30: 0}
    assert not p.es_portero(30)
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    assert clases_de(partido.instantes, 30) == {CLASE_JUGADOR} and equipos_de(partido.instantes, 30) == {0}


def test_lo_que_parece_un_portero_pero_no_se_puede_colocar_se_queda_sin_equipo():
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(31, MORADO, 11600, 60, 70, camiseta=1)  # de morado, pero en la portería del equipo 1
    partido.tracklet(32, MORADO, 400, 92, 99, muestras=2)  # muy pocas muestras de la equipación
    partido.tracklet(33, MORADO, 400, 0, 20, calibrado=False)  # sin campo, pero solo se parece a un portero
    partido.tracklet(34, MORADO, 400, 20, 30, muestras=4, calibrado=False)  # sin campo y pocas muestras
    p = partido.buscar()
    assert {31, 32, 34} <= p.sin_equipo
    assert p.equipo_de[33] == 0
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    assert equipos_de(partido.instantes, 31) == {None}


def test_sin_saber_como_visten_los_dos_equipos_solo_cuentan_los_que_marca_el_detector():
    partido = Partido().con_equipos(equipo_1=False).con_porteros()
    p = partido.buscar()
    assert not p.modelos and not p.equipo_de
    # Como hasta la 1.1.0: el único que el detector marca de portero, por la portería que defiende.
    assert p.del_detector == {20}
    assert p.por_parte == {(20, 1): 1}


def test_colocar_porteros_saca_hacia_donde_ataca_cada_equipo_y_lo_deja_en_los_fotogramas(dt):
    partido = Partido().con_equipos().con_porteros()
    p = colocar_porteros(partido.instantes, partido.mitad_de, partido.memorias, Campo())
    assert p.equipo_de == {10: 0, 11: 0, 12: 0, 13: 0, 20: 1, 21: 1, 22: 1}
    assert equipos_de(partido.instantes, 22) == {1}
    texto = describir_porteros(p, partido.instantes, dt)
    assert texto.startswith("Porteros por la equipación: el del equipo 0 viste de #")
    assert "el del equipo 1 viste de #" in texto
    assert "solo por el detector" not in texto


def de(instantes, tracklet):
    return [(inst.t, p.clase, p.equipo) for inst in instantes for p in inst.personas if p.tracklet == tracklet]


def test_del_tracklet_del_portero_solo_queda_el_rato_en_que_viste_como_el(dt):
    # El 14 es el portero del equipo 0 hasta que el seguidor salta a un jugador del equipo 0 que pasaba cerca; el 23
    # empieza en uno del equipo 1 y sigue en el portero del equipo 1.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(14, MORADO, 400, 0, 40, serie=[MORADO] * 15 + [RAYAS] * 5)
    partido.tracklet(23, LIMA, 11600, 50, 90, serie=[MARINO] * 4 + [LIMA] * 16)
    p = partido.buscar()
    assert p.equipo_de[14] == 0 and p.equipo_de[23] == 1
    hasta = partido.memorias[14].tiempos_equipacion[14]
    desde = partido.memorias[23].tiempos_equipacion[4]
    assert p.ratos == {14: (-np.inf, hasta), 23: (desde, np.inf)}
    assert p.mezclados == {14, 23}
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    # Dentro del rato, el portero de su equipo; fuera, otra persona: otro tracklet, sin equipo.
    assert de(partido.instantes, 14) and all(t <= hasta and c == CLASE_PORTERO and e == 0 for t, c, e in de(partido.instantes, 14))
    resto = de(partido.instantes, TRACKLET_RESTO_BASE + 14)
    assert len(resto) == 40 - len(de(partido.instantes, 14)) >= 10
    assert all(t > hasta and c == CLASE_JUGADOR and e is None for t, c, e in resto)
    assert all(t >= desde and e == 1 for t, _, e in de(partido.instantes, 23))
    assert {e for _, _, e in de(partido.instantes, TRACKLET_RESTO_BASE + 23)} == {None}
    assert "2 tracklets de portero recortados al rato en que visten como él (en 2 el seguidor saltó a otra persona" in describir_porteros(p, partido.instantes, dt)


def test_una_muestra_suelta_que_no_viste_como_el_en_medio_no_corta_el_tracklet():
    # El portero agachado, borroso o tapado un momento.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(14, MORADO, 400, 0, 40, serie=[MORADO] * 8 + [RAYAS] + [MORADO] * 8 + [RAYAS])
    p = partido.buscar()
    assert p.equipo_de[14] == 0
    assert p.ratos[14] == (-np.inf, partido.memorias[14].tiempos_equipacion[16])
    assert 14 not in p.mezclados


def test_dos_muestras_seguidas_que_no_visten_como_el_en_medio_se_quitan_del_portero(dt):
    # El seguidor salta del portero a un jugador que pasa pegado y vuelve al portero (o es el portero agachado o borroso,
    # que por el color no se distingue): ese rato es de otra persona y las lecturas del dorsal del tracklet no cuentan.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(14, MORADO, 400, 0, 40, serie=[MORADO] * 8 + [RAYAS] * 3 + [MORADO] * 9)
    p = partido.buscar()
    tiempos = partido.memorias[14].tiempos_equipacion
    assert p.equipo_de[14] == 0 and 14 not in p.ratos
    assert p.huecos[14] == [(tiempos[7], tiempos[11])] and 14 in p.mezclados
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    resto = de(partido.instantes, TRACKLET_RESTO_BASE + 14)
    assert resto and all(tiempos[7] < t < tiempos[11] and e is None for t, _, e in resto)
    assert all(not tiempos[7] < t < tiempos[11] and e == 0 for t, _, e in de(partido.instantes, 14))
    assert "1 tracklets de portero recortados al rato en que visten como él" in describir_porteros(p, partido.instantes, dt)


def test_el_que_no_viste_como_el_portero_ni_dos_muestras_seguidas_se_queda_sin_equipo():
    # Muestras sueltas de morado entre otras del equipo 0: la mediana es la del portero, pero no hay un rato en que sea él.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(15, MORADO, 400, 0, 40, serie=[MORADO, RAYAS] * 4 + [MORADO])
    p = partido.buscar()
    assert 15 not in p.equipo_de and 15 in p.sin_equipo


# Rayas con el pantalón claro: se parece al equipo 0, pero no tanto como para darlo por un jugador suyo.
RAYAS_PANTALON_CLARO = (52.0, 131.0, 131.0, 40.0, 142.0, 135.0)
# Verde lima con el pantalón oscuro: no viste como nadie del todo (otra luz), pero lo que más se le parece es el portero
# del equipo 1.
LIMA_PANTALON_OSCURO = (83.0, 102.0, 149.0, 8.0, 102.0, 148.0)


def test_el_que_el_detector_marca_de_portero_solo_cuenta_el_rato_en_que_viste_como_el_portero_de_esa_porteria():
    partido = Partido().con_equipos().con_porteros()
    # Un jugador del equipo 0 que remata un córner en la portería del equipo 1 y el detector toma por portero: no es
    # el portero del equipo 1 en ningún momento.
    partido.tracklet(41, RAYAS_PANTALON_CLARO, 11600, 60, 80, clase=CLASE_PORTERO)
    # El portero del equipo 1 hasta que el seguidor salta a un jugador del equipo 0.
    partido.tracklet(42, LIMA_PANTALON_OSCURO, 11600, 60, 90, clase=CLASE_PORTERO, serie=[LIMA_PANTALON_OSCURO] * 12 + [RAYAS] * 6)
    p = partido.buscar()
    assert 41 in p.sin_equipo and not p.es_portero(41) and not any(k[0] == 41 for k in p.por_parte)
    assert 42 in p.del_detector and p.por_parte[(42, 1)] == 1
    assert p.ratos[42] == (-np.inf, partido.memorias[42].tiempos_equipacion[11]) and 42 in p.mezclados
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    assert {e for _, _, e in de(partido.instantes, 41)} == {None}
    assert {e for _, _, e in de(partido.instantes, 42)} == {1}
    assert {e for _, _, e in de(partido.instantes, TRACKLET_RESTO_BASE + 42)} == {None}


def test_sin_saber_cuando_se_tomo_cada_muestra_no_se_recorta_pero_se_sabe_que_esta_mezclado():
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(14, MORADO, 400, 0, 40, serie=[MORADO] * 15 + [RAYAS] * 5)
    p = partido.buscar(con_tiempos=False)
    assert p.equipo_de[14] == 0 and not p.ratos and p.mezclados == {14}


def test_el_que_no_viste_como_nadie_mas_es_del_color_del_portero_hasta_el_mismo_limite_que_los_equipos():
    # El portero del equipo 0 con otra luz (el torso más claro): a más de 3,5 de su color, pero lejos de todos los demás.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(16, (79.0, 159.0, 106.0, 16.0, 142.0, 123.0), 400, 0, 40)
    p = partido.buscar()
    distancia = float(p.modelos[0].distancia(np.median(np.asarray(partido.memorias[16].equipaciones), axis=0))[0])
    assert 3.5 < distancia <= 5
    assert p.equipo_de[16] == 0 and 16 not in p.ratos


def test_del_jugador_de_campo_que_empieza_acaba_o_pasa_por_el_portero_se_quita_ese_rato(dt):
    # El seguidor salta de un jugador del equipo 0 al portero del equipo 1 (lima), otro tracklet del equipo 1 empieza en
    # el portero del equipo 0 (morado) y otro del equipo 0 pasa un momento al portero del equipo 1 y vuelve. Una sola
    # muestra suelta que se parece a un portero no corta nada.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(50, RAYAS, 10500, 10, 50, camiseta=0, serie=[RAYAS] * 15 + [LIMA] * 5)
    partido.tracklet(51, MARINO, 1500, 10, 50, camiseta=1, serie=[MORADO] * 3 + [MARINO] * 17)
    partido.tracklet(52, RAYAS, 3000, 10, 50, camiseta=0, serie=[RAYAS] * 10 + [MORADO] + [RAYAS] * 9)
    partido.tracklet(54, RAYAS, 10500, 10, 50, camiseta=0, serie=[RAYAS] * 8 + [LIMA] * 3 + [RAYAS] * 9)
    p = partido.buscar()
    assert p.ratos[50] == (-np.inf, partido.memorias[50].tiempos_equipacion[14])
    assert p.ratos[51] == (partido.memorias[51].tiempos_equipacion[3], np.inf)
    tiempos = partido.memorias[54].tiempos_equipacion
    assert 54 not in p.ratos and p.huecos[54] == [(tiempos[7], tiempos[11])]
    assert 52 not in p.ratos and 52 not in p.huecos and not p.es_portero(50) and not p.mezclados & {50, 51, 52, 54}
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    assert de(partido.instantes, 50) and {e for _, _, e in de(partido.instantes, 50)} == {0}
    assert {e for _, _, e in de(partido.instantes, TRACKLET_RESTO_BASE + 50)} == {None}
    assert {e for _, _, e in de(partido.instantes, 52)} == {0}
    resto = de(partido.instantes, TRACKLET_RESTO_BASE + 54)
    assert resto and all(tiempos[7] < t < tiempos[11] and e is None for t, _, e in resto)
    assert "3 de jugadores de campo que empezaban, acababan o pasaban por un portero" in describir_porteros(p, partido.instantes, dt)


def test_el_jugador_de_campo_que_viste_a_ratos_como_un_portero_de_principio_a_fin_no_es_nadie():
    # Ni del todo como el portero ni del todo como su equipo, pero nunca dos muestras seguidas que no sean del portero.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(53, RAYAS, 3000, 10, 50, camiseta=0, serie=[MORADO, RAYAS] * 3)
    p = partido.buscar()
    assert 53 not in p.equipo_de and 53 in p.sin_equipo


def test_el_portero_que_el_detector_toma_por_el_arbitro_es_el_portero():
    # El verde lima del portero del equipo 1 y el verde menta del árbitro se le confunden al detector.
    partido = Partido().con_equipos().con_porteros()
    partido.tracklet(24, LIMA, 11600, 50, 95, clase=CLASE_ARBITRO)
    p = partido.buscar()
    assert p.equipo_de[24] == 1
    assert not any(t in p.equipo_de or t in p.sin_equipo for t in range(300, 312))
    aplicar_porteros(partido.instantes, partido.mitad_de, p)
    assert clases_de(partido.instantes, 24) == {CLASE_PORTERO} and equipos_de(partido.instantes, 24) == {1}
    assert clases_de(partido.instantes, 300) == {CLASE_ARBITRO} and equipos_de(partido.instantes, 300) == {None}
