from tt_analisis.config import CLASE_JUGADOR
from tt_analisis.en_juego import balon_en_juego, movimiento_relativo
from tt_analisis.tipos import Instante, Persona

DT = 0.2


def _jugadores(i: int, corren: bool) -> list[Persona]:
    """Seis jugadores de 100 px de alto; si corren, cada uno hacia un lado distinto a 2 altos por segundo."""
    salida = []
    for k in range(6):
        dx = (2.0 if k % 2 else -2.0) * 100 * DT * i if corren else 0.0
        x, y = 300.0 * k + dx, 500.0
        salida.append(Persona(k + 1, CLASE_JUGADOR, k % 2, (x, y), (1000.0 * k, 3000.0), (x - 20, y - 100, x + 20, y), 0.9))
    return salida


def _tramo(n: int, corren: bool, balon=lambda i: (500.0, 400.0)) -> list[Instante]:
    instantes = []
    for i in range(n):
        b = balon(i)
        instantes.append(Instante(i, i * DT, _jugadores(i, corren), None if b is None else (b[0] / 10, b[1] / 10), b, True))
    return instantes


def test_el_movimiento_no_cuenta_el_paneo_de_la_camara():
    # Todos se desplazan igual en la imagen (la cámara barre): nadie se mueve respecto a los demás.
    quietos = _tramo(10, corren=False)
    for i, inst in enumerate(quietos):
        for p in inst.personas:
            p.xy_imagen = (p.xy_imagen[0] + 50 * i, p.xy_imagen[1])
    assert max(v for v in movimiento_relativo(quietos) if v == v) < 0.01
    assert min(v for v in movimiento_relativo(_tramo(10, corren=True)) if v == v) > 1.5


def test_sin_balon_un_rato_con_los_jugadores_quietos_esta_parado_y_corriendo_no():
    sin_balon = lambda i: None if 5 <= i < 30 else (500.0, 400.0)  # noqa: E731
    parado = balon_en_juego(_tramo(40, corren=False, balon=sin_balon), DT)
    assert not any(parado.en_juego[5:30]) and all(parado.en_juego[:5])
    corriendo = balon_en_juego(_tramo(40, corren=True, balon=sin_balon), DT)
    assert all(corriendo.en_juego)


def test_un_hueco_corto_sin_balon_sigue_en_juego_aunque_esten_quietos():
    corto = balon_en_juego(_tramo(30, corren=False, balon=lambda i: None if 10 <= i < 20 else (500.0 + 100 * i, 400.0)), DT)
    assert all(corto.en_juego)


def test_el_balon_fuera_para_el_juego_hasta_que_se_vuelve_a_ver_dentro():
    def balon(i):
        if i < 5:
            return (500.0 + 100 * i, 400.0)
        if i < 8:
            return (500.0, -500.0)
        if i < 12:
            return None
        return (900.0 + 100 * i, 400.0)

    resultado = balon_en_juego(_tramo(20, corren=True, balon=balon), DT)
    assert resultado.en_juego[:5] == [True] * 5
    assert resultado.en_juego[5:12] == [False] * 7
    assert all(resultado.en_juego[12:])


def test_lo_visto_y_la_trayectoria_se_cuentan_sobre_el_tiempo_en_juego():
    instantes = _tramo(20, corren=True, balon=lambda i: (500.0 + 50 * i, 400.0) if i % 2 == 0 else None)
    for inst in instantes[1::4]:
        inst.balon_imagen, inst.balon_campo, inst.balon_origen = (1.0, 1.0), (10.0, 10.0), "interpolado"
    resumen = balon_en_juego(instantes, DT, juego_activo=[i < 10 for i in range(20)]).a_dict(DT)
    assert resumen["pct_en_juego"] == 50.0
    assert resumen["pct_visto"] == 50.0 and resumen["pct_con_trayectoria"] == 80.0
