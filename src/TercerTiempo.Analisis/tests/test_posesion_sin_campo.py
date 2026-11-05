from tt_analisis.config import CLASE_JUGADOR
from tt_analisis.posesion import MaquinaPosesion, acciones_por_jugador, secuencias
from tt_analisis.tipos import Instante, Persona


def _persona_px(tracklet, equipo, x, y, alto=60.0):
    return Persona(tracklet, CLASE_JUGADOR, equipo, (x, y), None, (x - 15, y - alto, x + 15, y), 0.9)


def test_sin_campo_calibrado_la_posesion_se_mide_en_la_imagen():
    # Dos jugadores de 60 px de alto (1,80 m): el balón a 40 px del 1 es 1,2 m → suyo; a 300 px del 2 → en vuelo.
    a = _persona_px(1, 0, 400, 500)
    b = _persona_px(2, 1, 900, 500)
    maquina = MaquinaPosesion(confirmacion=2)
    instantes = [Instante(i, i * 0.2, [a, b], (440, 480), None, False) for i in range(4)]
    instantes += [Instante(4 + i, (4 + i) * 0.2, [a, b], (920, 490), None, False) for i in range(4)]
    for inst in instantes:
        maquina.actualizar(inst)
    maquina.cerrar()
    assert [(t.tracklet, t.equipo) for t in maquina.toques] == [(1, 0), (2, 1)]
    assert maquina.toques[0].xy_inicio is None

    secs = secuencias(maquina.toques, [])
    acciones = acciones_por_jugador(secs)
    assert acciones[1].perdidas == 1 and acciones[2].recuperaciones == 1
    assert acciones[1].pases_fallados == 0  # sin distancia no se afirma que fuera un pase
