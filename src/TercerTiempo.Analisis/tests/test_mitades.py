from tt_analisis.mitades import cambio_de_campo, detectar_descanso

from conftest import instante, persona


def _partido(corte_min: int | None, minutos: int = 90, ruido: tuple[int, ...] = ()):
    """Un instante cada 2 s con cuatro jugadores por equipo; el equipo 0 a la derecha hasta `corte_min` y a la izquierda
    después (en los minutos de `ruido`, al revés: la calibración falla)."""
    instantes = []
    for k in range(minutos * 30):
        t = k * 2.0
        minuto = int(t // 60)
        derecha = corte_min is None or minuto < corte_min
        if minuto in ruido:
            derecha = not derecha
        x0, x1 = (8000, 4000) if derecha else (4000, 8000)
        gente = [persona(10 + j, 0, (x0 + 200 * j, 3000)) for j in range(4)] + [persona(20 + j, 1, (x1 + 200 * j, 3000)) for j in range(4)]
        instantes.append(instante(k, t, gente, None))
    return instantes


def test_sin_pausa_el_descanso_es_el_cambio_de_campo():
    instantes = _partido(46, ruido=(10, 52, 53))
    assert cambio_de_campo(instantes) == 46 * 60
    assert detectar_descanso(instantes, [], 90 * 60, None) == 46 * 60


def test_sin_cambio_de_campo_no_hay_descanso():
    assert cambio_de_campo(_partido(None)) is None
    # Un cambio fuera del medio del tramo (minuto 80) no es el descanso.
    assert cambio_de_campo(_partido(80)) is None
    # Con un tramo corto no se busca.
    assert cambio_de_campo(_partido(10, minutos=15)) is None
