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


import json
import threading

from tt_analisis.avance import ORDEN_PAUSA, ORDEN_SIGUE, PREFIJO_ESTADO, PREFIJO_HITO


class Reloj:
    def __init__(self):
        self.t = 0.0

    def __call__(self):
        return self.t


def estados(lineas):
    return [json.loads(linea[len(PREFIJO_ESTADO):]) for linea in lineas if linea.startswith(PREFIJO_ESTADO)]


def test_el_estado_se_escribe_al_cambiar_de_etapa_y_luego_como_mucho_cada_dos_segundos():
    lineas, reloj = [], Reloj()
    avance = Avance(True, lineas.append, reloj=reloj)

    avance.etapa("equipos", objetivo=600)
    avance.estado(muestras=10)
    reloj.t = 2.5
    avance.estado(muestras=300, colores=["#ffffff", "#112233"])
    avance.etapa("pasada", fotogramas_total=1500)

    vistos = estados(lineas)
    assert [e["etapa"] for e in vistos] == ["equipos", "equipos", "pasada"]
    assert vistos[1]["muestras"] == 300
    # Al cambiar de etapa se olvida lo de la anterior, salvo lo que sigue valiendo (los colores de los equipos).
    assert "muestras" not in vistos[2] and vistos[2]["colores"] == ["#ffffff", "#112233"] and vistos[2]["fotogramas_total"] == 1500
    assert all(e["pausado"] is False for e in vistos)


def test_los_hitos_van_en_una_sola_linea():
    lineas = []
    avance = Avance(True, lineas.append)

    avance.hito("Equipos separados\npor el color   de la camiseta")
    avance.hito("   ")

    assert lineas == [f"{PREFIJO_HITO}Equipos separados por el color de la camiseta"]


def test_en_pausa_espera_dando_senales_y_no_cuenta_el_tiempo_parado():
    lineas, reloj = [], Reloj()
    avance = Avance(True, lineas.append, reloj=reloj, latido_pausa_s=5)
    dormidas = []

    def dormir(s):
        dormidas.append(s)
        reloj.t += 1
        if reloj.t >= 12:
            avance.seguir()

    avance._dormir = dormir
    avance.etapa("pasada")
    assert avance.punto_de_control() == 0.0

    avance.pausar()
    parado = avance.punto_de_control()

    assert parado == 12 and avance.pausa_total_s == 12
    pausados = [e for e in estados(lineas) if e["pausado"]]
    assert len(pausados) >= 3  # al parar y un latido cada 5 s
    assert any(linea == f"{PREFIJO_HITO}Reanudado." for linea in lineas)
    assert estados(lineas)[-1]["pausado"] is False and estados(lineas)[-1]["pausa_s"] == 12


def test_las_ordenes_llegan_por_la_entrada_y_al_cerrarse_nunca_se_queda_en_pausa():
    lineas = []
    puede_seguir = threading.Event()

    def entrada():
        yield ORDEN_PAUSA + "\n"
        puede_seguir.wait(2)
        yield ORDEN_SIGUE + "\n"
        yield ORDEN_PAUSA + "\n"

    avance = Avance(True, lineas.append, entrada=entrada())
    for _ in range(100):
        if avance.pausado:
            break
        threading.Event().wait(0.01)
    assert avance.pausado
    puede_seguir.set()
    for _ in range(200):
        if not avance.pausado:
            break
        threading.Event().wait(0.01)
    # La última orden era «pausa», pero la entrada se cerró: sin nadie que la quite, se sigue.
    assert not avance.pausado
