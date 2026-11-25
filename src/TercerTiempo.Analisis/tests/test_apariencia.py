import numpy as np
import pytest

from tt_analisis import apariencia as ap
from tt_analisis.identidades import Cadena, Tramo, _apariencia

DIM = 16


def _direccion(rng, base, k):
    v = base + 0.8 * np.eye(DIM)[k]
    return v / np.linalg.norm(v)


def _partido(rng):
    """Dos equipos de cuatro jugadores (dorsales 1-4), ocho cadenas leídas de cada uno, cada una de 5 s y 6 recortes."""
    bases = {0: np.eye(DIM)[0] * 2.0, 1: np.eye(DIM)[1] * 2.0}
    aspecto = {(e, d): _direccion(rng, bases[e], 2 + 4 * e + d) for e in (0, 1) for d in (1, 2, 3, 4)}
    cadenas, asignada, apariencias = [], {}, {}
    for e in (0, 1):
        for d in (1, 2, 3, 4):
            for k in range(8):
                t = 100.0 * k + 10.0 * d
                tid = len(cadenas)
                cadenas.append(Cadena([Tramo(tid, e, t, t + 5, (0.0, 0.0), (0.0, 0.0), 100.0)]))
                asignada[tid] = (d, 2.0, 0.9)
                v = aspecto[(e, d)] + rng.normal(0, 0.02, DIM)
                # Recortes que se parecen entre sí, pero no son iguales (coherencia 0,77).
                apariencias[tid] = (0.9 * 6 * v / np.linalg.norm(v), 6)
    return cadenas, asignada, apariencias, aspecto


def _sin_dorsal(cadenas, apariencias, equipo, vector, t_ini, t_fin, n=6, coherente=True):
    tid = len(cadenas)
    cadenas.append(Cadena([Tramo(tid, equipo, t_ini, t_fin, (0.0, 0.0), (0.0, 0.0), 100.0)]))
    v = vector / np.linalg.norm(vector)
    # Una cadena con dos personas: sus recortes no se parecen entre sí y la suma sale más corta.
    apariencias[tid] = ((n if coherente else 0.3 * n) * v, n)
    return tid


def test_la_coherencia_sale_de_la_suma_y_baja_si_se_mezclan_dos():
    v = np.eye(DIM)[0]
    assert ap.coherencia(6 * v, 6) == pytest.approx(1.0)
    w = np.eye(DIM)[1]
    assert ap.coherencia(3 * v + 3 * w, 6) == pytest.approx((18 - 6) / 30)
    assert ap.coherencia(v, 1) is None


def test_el_recorte_se_hace_cuadrado_sin_deformarlo():
    alto = np.zeros((200, 80, 3), dtype=np.uint8)
    alto[:, :, 2] = 255
    cuadrado = ap.preparar(alto)
    assert cuadrado.shape == (ap.LADO, ap.LADO, 3)
    # En medio, la persona (roja en BGR, en RGB el primer canal); a los lados, el relleno gris.
    assert cuadrado[112, 112, 0] == 255 and cuadrado[112, 2, 0] == 128


def test_la_prueba_solo_da_umbral_si_acierta_en_el_95_por_ciento():
    rng = np.random.default_rng(1)
    centros = rng.normal(size=(4, DIM))
    dorsales = [d for d in range(4) for _ in range(10)]
    claros = np.stack([centros[d] + rng.normal(0, 0.05, DIM) for d in dorsales])
    prueba = ap.probar(claros, dorsales, list(range(40)), lambda k, apartadas: {0, 1, 2, 3})
    assert prueba.umbral == 0.5 and prueba.precision == 1.0 and set(prueba.a_dict()["por_umbral"]) == {str(u) for u in ap.UMBRALES}
    ruido = rng.normal(size=(40, DIM))
    assert ap.probar(ruido, dorsales, list(range(40)), lambda k, apartadas: {0, 1, 2, 3}).umbral is None


def test_una_cadena_sin_dorsal_con_el_aspecto_de_uno_es_ese_jugador():
    rng = np.random.default_rng(2)
    cadenas, asignada, apariencias, aspecto = _partido(rng)
    nueva = _sin_dorsal(cadenas, apariencias, 0, aspecto[(0, 2)], 60.0, 64.0)
    dadas, resumen = _apariencia(cadenas, asignada, apariencias, {0: None, 1: None}, {}, len(cadenas))
    assert dadas == {nueva} and asignada[nueva] == (2, 0.0, 0.0)
    assert resumen[0]["asignadas"] == 1 and resumen[0]["segundos"] == 4.0 and resumen[0]["umbral"] is not None


def test_no_se_da_a_quien_se_ve_a_la_vez_en_otro_sitio_ni_a_quien_el_acta_veta():
    rng = np.random.default_rng(3)
    cadenas, asignada, apariencias, aspecto = _partido(rng)
    # El 2 se ve de 120 a 125: esta cadena, a la vez, no puede ser él.
    a_la_vez = _sin_dorsal(cadenas, apariencias, 0, aspecto[(0, 2)], 121.0, 124.0)
    vetada = _sin_dorsal(cadenas, apariencias, 0, aspecto[(0, 3)], 60.0, 64.0)
    dadas, _ = _apariencia(cadenas, asignada, apariencias, {0: None, 1: None}, {0: {3}}, len(cadenas))
    assert asignada.get(a_la_vez, (None,))[0] != 2 and asignada.get(vetada, (None,))[0] != 3


def test_ni_a_la_cadena_con_la_camiseta_del_otro_equipo_ni_a_la_de_dos_personas():
    rng = np.random.default_rng(4)
    cadenas, asignada, apariencias, aspecto = _partido(rng)
    # Marcada del equipo 0 por el color medio, pero por el aspecto es del 1.
    otra_camiseta = _sin_dorsal(cadenas, apariencias, 0, aspecto[(1, 2)], 60.0, 64.0)
    mezclada = _sin_dorsal(cadenas, apariencias, 0, aspecto[(0, 1)], 70.0, 74.0, coherente=False)
    dadas, _ = _apariencia(cadenas, asignada, apariencias, {0: None, 1: None}, {}, len(cadenas))
    assert otra_camiseta not in dadas and mezclada not in dadas


def test_sin_vectores_o_con_pocas_cadenas_leidas_no_se_da_nada():
    rng = np.random.default_rng(5)
    cadenas, asignada, apariencias, aspecto = _partido(rng)
    assert _apariencia(cadenas, asignada, None, {0: None, 1: None}, {}, len(cadenas)) == (set(), {})
    pocas = {i: a for i, a in asignada.items() if i % 8 < 3}
    _sin_dorsal(cadenas, apariencias, 0, aspecto[(0, 2)], 60.0, 64.0)
    dadas, resumen = _apariencia(cadenas, pocas, apariencias, {0: None, 1: None}, {}, len(cadenas))
    assert not dadas and resumen[0]["umbral"] is None
