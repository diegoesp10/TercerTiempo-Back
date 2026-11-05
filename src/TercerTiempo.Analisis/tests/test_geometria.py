from tt_analisis.geometria import Campo, corte_con_linea_de_fondo, hacia


def test_fuera_y_boca_de_porteria():
    campo = Campo()
    assert campo.fuera_por((6000, 3500)) is None
    assert campo.fuera_por((-200, 3500)) == "fondo_izq"
    assert campo.fuera_por((12300, 1000)) == "fondo_der"
    assert campo.fuera_por((6000, -200)) == "banda"
    assert campo.en_boca_porteria(3500)
    assert campo.en_boca_porteria(3500 + 366)
    assert not campo.en_boca_porteria(3500 + 600)


def test_tercios_relativos_dependen_de_la_direccion():
    campo = Campo()
    assert campo.tercio_relativo(1000, +1) == "propio"
    assert campo.tercio_relativo(1000, -1) == "rival"
    assert campo.tercio_relativo(6000, +1) == "medio"
    assert campo.tercio_relativo(11000, +1) == "rival"


def test_hacia_y_corte_con_la_linea_de_fondo():
    porteria = (12000.0, 3500.0)
    assert hacia((9000, 3500), (1500, 0), porteria)
    assert not hacia((9000, 3500), (-1500, 0), porteria)
    assert not hacia((9000, 3500), (0, 1500), porteria)
    assert corte_con_linea_de_fondo((9000, 3000), (1500, 250), 12000) == 3500
    assert corte_con_linea_de_fondo((9000, 3000), (-1500, 250), 12000) is None
