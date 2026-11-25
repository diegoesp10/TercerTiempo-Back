import copy
import json
from types import SimpleNamespace

import pytest

from tt_analisis.geometria import Campo
from tt_analisis.porteros import TRACKLET_RESTO_BASE
from tt_analisis.posesion import resumir_posesion, toques_controlados
from tt_analisis.repeticion import (
    CARPETA,
    FINAL,
    FOTOGRAMAS,
    MAX_VENTANA_S,
    IndiceFotogramas,
    escribir_repeticion,
    linea_fotograma,
    origenes_balon,
    repartir_vecinos,
    seguidor_de,
)
from tt_analisis.tipos import Evento, Instante, Persona, Toque

from conftest import instante, persona


def _persona(tracklet, equipo, caja, clase=2):
    return Persona(tracklet, clase, equipo, ((caja[0] + caja[2]) / 2, caja[3]), None, caja, 0.9)


def test_origen_del_balon_de_cada_instante():
    instantes = [
        Instante(0, 0.0, balon_imagen=(10, 10)),
        Instante(1, 0.2, balon_imagen=(12, 10)),
        Instante(2, 0.4, balon_imagen=(14, 10), balon_origen="interpolado"),
        Instante(3, 0.6),
        Instante(4, 0.8),
    ]
    crudo = [((10, 10), 0.8), None, None, ((50, 50), 0.4), None]
    # Visto por el detector; puesto desde un fotograma vecino; interpolado; visto y descartado; sin balón.
    assert origenes_balon(instantes, crudo) == "vnix-"


def test_cada_balon_vecino_va_al_instante_mas_cercano():
    pista = [(0.05, (1, 1), 0.9), (0.11, (2, 2), 0.8), (0.1, (3, 3), 0.7), (0.55, (4, 4), 0.6)]
    vecinos = repartir_vecinos(pista, [0.0, 0.2, 0.4])
    assert [v[0] for v in vecinos[0]] == [0.05, 0.1]
    assert [v[0] for v in vecinos[1]] == [0.11]
    assert [v[0] for v in vecinos[2]] == [0.55]
    assert repartir_vecinos([], [0.0]) == {}


def test_el_tracklet_del_seguidor_sale_del_de_los_porteros():
    assert seguidor_de(TRACKLET_RESTO_BASE + 7) == 7
    assert seguidor_de(12) == 12
    assert seguidor_de(900007) == 900007


def test_la_linea_empieza_por_el_segundo_y_lleva_cada_persona_y_el_balon():
    portero_rato_ajeno = _persona(900001, 0, (100, 200, 140, 300))
    jugador = _persona(901010, 1, (400.4, 210.6, 440.2, 320.5))
    inst = Instante(
        60000, 1200.0, [portero_rato_ajeno, jugador], balon_imagen=(512.34, 300.06), balon_campo=(5012.6, 3020.4), calibrado=True,
        balon_confianza=0.873, homografia=SimpleNamespace(m=[[1.0, 0.123456789, 3.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]),
    )
    tracklets = [TRACKLET_RESTO_BASE + 33, 41]
    texto = linea_fotograma(inst, tracklets, "v", ((512.34, 300.06), 0.873), [[1200.04, 515.0, 301.0, 0.5]])
    assert texto.startswith('{"t":1200.0,')
    linea = json.loads(texto)
    assert linea["i"] == 60000
    assert linea["h"][1] == pytest.approx(0.123457)
    # [id final, tracklet, clase, equipo, x1, y1, x2, y2] y, si el seguidor le dio otro tracklet, ese al final.
    assert linea["p"][0] == [900001, TRACKLET_RESTO_BASE + 33, 2, 0, 100, 200, 140, 300, 33]
    assert linea["p"][1] == [901010, 41, 2, 1, 400, 211, 440, 320]
    assert linea["b"] == [512.3, 300.1, "v", 0.87]
    assert linea["bc"] == [5013, 3020]
    assert linea["v"] == [[1200.04, 515.0, 301.0, 0.5]]


def test_sin_balon_queda_lo_que_se_descarto():
    inst = Instante(5, 0.1, [_persona(3, None, (0, 0, 10, 20), clase=3)])
    linea = json.loads(linea_fotograma(inst, None, "x", ((70.0, 80.0), 0.35), []))
    assert linea["h"] is None
    assert linea["p"] == [[3, 3, 3, -1, 0, 0, 10, 20]]
    assert "b" not in linea and linea["bd"] == [70.0, 80.0, 0.35]


def _jugadas():
    return SimpleNamespace(
        en_juego=None,
        juego_activo=[True, True, False],
        posesion=SimpleNamespace(por_instante=[0, None, None]),
        descanso=None,
        direcciones={(0, 1): 1, (1, 1): -1},
        mitad_de=[1, 1, 1],
        toques=[Toque(900007, 0, 0.0, 0.2, None, None)],
        acciones={900007: SimpleNamespace(
            toques=1, segundos_pases=[0.2], segundos_pases_fallados=[], segundos_perdidas=[], segundos_recuperaciones=[], segundos_paradas=[], segundos_saques=[],
        )},
        resumenes={900007: SimpleNamespace(equipo=0)},
        eventos=[Evento("saque_banda", 0.4, equipo=1)],
    )


def _resolucion():
    jugador = SimpleNamespace(
        id=900007, equipo=0, dorsal=7, nombre="Pablo", portero=False, confianza=0.9,
        por_continuidad=False, por_apariencia=True, por_descarte=False, tracklets=[3, 5],
    )
    return SimpleNamespace(identidades=[jugador], cadena={3: 0, 5: 0}, metodo={3: "aspecto", 5: "dorsal"})


def test_escribe_la_repeticion_sin_tocar_el_analisis(tmp_path):
    h = SimpleNamespace(m=[[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]])
    instantes = [
        Instante(0, 0.0, [_persona(900007, 0, (10, 10, 30, 60))], balon_imagen=(20, 60), balon_campo=(20, 60), calibrado=True, homografia=h),
        Instante(10, 0.2, [_persona(900007, 0, (12, 10, 32, 60))], balon_imagen=(24, 60), calibrado=True, homografia=h),
        Instante(20, 0.4, [_persona(900007, 0, (14, 10, 34, 60))]),
    ]
    tracklets = [[3], [3], [5]]
    crudo = [((20, 60), 0.9), None, None]
    copia = copy.deepcopy(instantes)
    ruta = escribir_repeticion(
        tmp_path, instantes, tracklets, crudo, [(0.1, (22, 60), 0.6)],
        analizador={"nombre": "Ojeador", "version": "2.3.0"}, video={"ancho": 1920, "alto": 1080, "fps": 50.0}, dt=0.2,
        equipos=[{"nombre": "A"}, {"nombre": "B"}], campo=Campo(), jugadas=_jugadas(), resolucion=_resolucion(),
        recortes={900007: "recortes/900007.jpg"},
    )
    assert ruta == f"{CARPETA}/{FINAL}"
    assert instantes == copia

    lineas = (tmp_path / CARPETA / FOTOGRAMAS).read_text(encoding="utf-8").splitlines()
    assert [json.loads(x)["t"] for x in lineas] == [0.0, 0.2, 0.4]
    assert json.loads(lineas[0])["v"] == [[0.1, 22.0, 60.0, 0.6]]

    final = json.loads((tmp_path / ruta).read_text(encoding="utf-8"))
    assert final["formato"] == 1 and final["analizador"] == {"nombre": "Ojeador", "version": "2.3.0"}
    assert final["instantes"] == {"t": [0.0, 0.2, 0.4], "balon": "vn-", "en_juego": "110", "posesion": "0--", "calibrado": "110", "mitad": "111"}
    assert final["toques"] == [[0.0, 0.2, 900007, 0, 0]]
    assert final["jugadores"][0]["recorte"] == "recortes/900007.jpg" and final["jugadores"][0]["tracklets"] == 2
    assert final["tracklets"] == {"3": {"cadena": 0, "metodo": "aspecto"}, "5": {"cadena": 0, "metodo": "dorsal"}}
    assert final["acciones"]["900007"]["pases"] == [0.2] and final["acciones"]["900007"]["equipo"] == 0
    assert final["eventos"][0]["tipo"] == "saque_banda"
    assert final["direcciones_ataque"] == {"equipo0_mitad1": 1, "equipo1_mitad1": -1}
    assert len(final["campo"]["vertices"]) == 32 and final["campo"]["largo_cm"] == Campo().largo


def test_la_posesion_de_cada_instante_cuadra_con_el_resumen():
    a1, a2, b1 = persona(1, 0, (1000, 1000)), persona(2, 0, (2500, 1000)), persona(3, 1, (4000, 1000))
    todos = [a1, a2, b1]
    escenas = (
        [(todos, (1050, 1000))] * 5
        + [(todos, (x, 1000)) for x in (1400, 1750, 2100)]
        + [(todos, (2450, 1000))] * 5
        + [(todos, None)] * 3
        + [(todos, (3950, 1000))] * 10
    )
    instantes = [instante(i, round(i * 0.2, 2), personas, balon) for i, (personas, balon) in enumerate(escenas)]
    toques = toques_controlados(instantes)
    activo = [True] * len(instantes)
    activo[2] = False
    resumen = resumir_posesion(toques, instantes, activo, [1] * len(instantes), lambda e, m: 1, 0.2)
    assert len(resumen.por_instante) == len(instantes)
    assert resumen.por_instante[2] is None
    for equipo in (0, 1):
        assert sum(0.2 for e in resumen.por_instante if e == equipo) == pytest.approx(resumen.segundos[equipo])


def _jsonl(ruta, tiempos):
    with ruta.open("w", encoding="utf-8", newline="\n") as f:
        for t in tiempos:
            f.write(json.dumps({"t": t, "p": []}, separators=(",", ":")) + "\n")


def test_el_indice_lee_solo_lo_nuevo_y_deja_la_linea_a_medias(tmp_path):
    ruta = tmp_path / FOTOGRAMAS
    _jsonl(ruta, [0.0, 0.2, 0.4])
    with ruta.open("a", encoding="utf-8", newline="\n") as f:
        f.write('{"t":0.6,"p":[')
    indice = IndiceFotogramas(ruta)
    assert len(indice) == 3
    with ruta.open("a", encoding="utf-8", newline="\n") as f:
        f.write("]}\n" + '{"t":0.8,"p":[]}\n')
    indice.actualizar()
    assert indice.tiempos == [0.0, 0.2, 0.4, 0.6, 0.8]
    assert [x["t"] for x in json.loads(indice.entre(0.2, 0.6))] == [0.2, 0.4, 0.6]
    assert json.loads(indice.entre(5.0, 6.0)) == []


def test_el_indice_no_da_mas_de_la_ventana_maxima(tmp_path):
    ruta = tmp_path / FOTOGRAMAS
    _jsonl(ruta, [float(t) for t in range(0, 301)])
    tramo = json.loads(IndiceFotogramas(ruta).entre(0.0, 1000.0))
    assert tramo[0]["t"] == 0.0 and tramo[-1]["t"] == MAX_VENTANA_S


def test_sin_archivo_el_indice_esta_vacio(tmp_path):
    indice = IndiceFotogramas(tmp_path / "no-existe.jsonl")
    assert len(indice) == 0 and indice.entre(0.0, 10.0) == b"[]"
