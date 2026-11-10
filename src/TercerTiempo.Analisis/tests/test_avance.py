from tt_analisis.avance import PREFIJO, Avance


def test_el_avance_solo_escribe_cuando_sube_el_porcentaje():
    lineas = []
    avance = Avance(True, lineas.append)

    avance.fase("equipos", 0, 600)
    avance.fase("equipos", 300, 600)
    avance.fase("equipos", 301, 600)
    avance.fase("equipos", 600, 600)
    avance.fase("pasada", 0, 1500)
    avance.fase("pasada", 750, 1500)
    avance.fase("pasada", 1500, 1500)
    avance.fase("salidas", 1, 2)
    avance.fin()
    avance.fin()

    assert lineas == [f"{PREFIJO}{n}" for n in (0, 4, 8, 51, 95, 97, 100)]


def test_el_avance_no_se_pasa_de_su_fase_ni_retrocede():
    lineas = []
    avance = Avance(True, lineas.append)

    avance.fase("pasada", 3000, 1500)
    avance.fase("equipos", 10, 600)
    avance.fase("pasada", 10, 0)

    assert lineas == [f"{PREFIJO}95"]


def test_sin_progreso_no_se_escribe_nada():
    lineas = []
    avance = Avance(False, lineas.append)

    avance.fase("pasada", 10, 100)
    avance.fin()

    assert lineas == []
