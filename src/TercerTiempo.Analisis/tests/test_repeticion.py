import copy
import functools
import itertools
import json
import random
import time
from types import SimpleNamespace

import numpy as np
import pytest
import supervision as sv

from tt_analisis import analizador
from tt_analisis.campo import Calibracion, Homografia
from tt_analisis.config import Configuracion, Segmento
from tt_analisis.deteccion import Detecciones
from tt_analisis.geometria import Campo
from tt_analisis.porteros import TRACKLET_RESTO_BASE
from tt_analisis.posesion import resumir_posesion, toques_controlados
from tt_analisis.repeticion import (
    CARPETA,
    DIRECTO,
    ESTADO_DIRECTO,
    FINAL,
    FOTOGRAMAS,
    MAX_HITOS,
    MAX_VENTANA_S,
    Directo,
    IndiceFotogramas,
    escribir_repeticion,
    linea_fotograma,
    origenes_balon,
    repartir_vecinos,
    seguidor_de,
)
from tt_analisis.tipos import Evento, Instante, Persona, Toque
from tt_analisis.video import Fotograma, InfoVideo

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


def test_el_indice_resume_balon_y_calibrado_y_empieza_de_nuevo_si_el_archivo_encoge(tmp_path):
    ruta = tmp_path / DIRECTO
    h = [1, 0, 0, 0, 1, 0, 0, 0, 1]
    lineas = [
        {"t": 0.0, "i": 0, "h": h, "p": [[3, 3, 2, 0, 0, 0, 10, 20]], "b": [1.0, 2.0, "v", 0.9]},
        {"t": 0.2, "i": 5, "h": None, "p": [], "v": [[0.16, 1.0, 2.0, 0.5]]},
        {"t": 0.4, "i": 10, "h": None, "p": [], "bd": [1.0, 2.0, 0.3]},
        {"t": 0.6, "i": 15, "h": h, "p": []},
        {"t": 0.8, "i": 20, "h": h, "p": [], "b": [1.0, 2.0, "i", None]},
    ]
    ruta.write_text("".join(json.dumps(x, separators=(",", ":")) + "\n" for x in lineas), encoding="utf-8")
    indice = IndiceFotogramas(ruta)
    assert indice.resumen() == {"desde": 0, "t": [0.0, 0.2, 0.4, 0.6, 0.8], "balon": "vnx-i", "calibrado": "10011"}
    assert indice.resumen(3) == {"desde": 3, "t": [0.6, 0.8], "balon": "-i", "calibrado": "11"}
    assert indice.resumen(9) == {"desde": 9, "t": [], "balon": "", "calibrado": ""}
    # Otro análisis en la misma carpeta empieza su archivo de cero.
    ruta.write_text(json.dumps({"t": 5.0, "i": 125, "h": None, "p": []}, separators=(",", ":")) + "\n", encoding="utf-8")
    indice.actualizar()
    assert indice.resumen() == {"desde": 0, "t": [5.0], "balon": "-", "calibrado": "0"}


# ---------------------------------------------------------------- en directo

H_IDENTIDAD = SimpleNamespace(m=[[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]])
VIDEO_DIRECTO = {"ruta": "partido.mp4", "ancho": 1280, "alto": 720, "fps": 25.0, "duracion_s": 10.0, "tramo_inicio_s": 0.0, "tramo_fin_s": 10.0}
EQUIPOS_DIRECTO = [{"id": 0, "nombre": "CD Vallobín", "color": "#c81e1e"}, {"id": 1, "nombre": "Real Titánico", "color": "#1e1ec8"}]


class _Avance:
    def __init__(self, etapa="pasada"):
        self.etapa_actual = etapa
        self.pausado = False


def _estado(salida):
    for _ in range(100):
        try:
            return json.loads((salida / CARPETA / ESTADO_DIRECTO).read_text(encoding="utf-8"))
        except PermissionError:
            # En Windows, justo mientras el latido lo reemplaza.
            time.sleep(0.01)
    raise AssertionError("No se puede leer directo.json")


def _esperar(condicion, segundos=5.0):
    limite = time.monotonic() + segundos
    while time.monotonic() < limite:
        valor = condicion()
        if valor:
            return valor
        time.sleep(0.01)
    raise AssertionError("No ha pasado a tiempo")


def _lineas_directo(salida):
    ruta = salida / CARPETA / DIRECTO
    return [json.loads(x) for x in ruta.read_text(encoding="utf-8").splitlines()] if ruta.exists() else []


def _instante_directo(k, balon=True):
    personas = [_persona(1 + k % 3, k % 2, (10.0 + k, 20.0, 30.0 + k, 80.0)), _persona(9, None, (500.0, 20.0, 520.0, 80.0), clase=3)]
    calibrado = k % 4 != 1
    return Instante(
        5 * k, round(0.2 * k, 3), personas, balon_imagen=(100.0 + k, 50.0) if balon else None, balon_confianza=0.8 if balon else None,
        calibrado=calibrado, homografia=H_IDENTIDAD if calibrado else None,
    )


def _empezado(salida, **opciones):
    directo = Directo(salida, latido_s=opciones.pop("latido_s", 3600.0), **opciones)
    directo.empezar(analizador={"nombre": "Ojeador", "version": "2.3.0"}, video=VIDEO_DIRECTO, avance=_Avance())
    directo.empezar_pasada(video=VIDEO_DIRECTO, dt=0.2, equipos=EQUIPOS_DIRECTO, campo=Campo(), total=8)
    return directo


def test_en_directo_cada_instante_sale_al_llegar_el_siguiente_con_los_balones_vecinos_de_la_definitiva(tmp_path):
    tiempos = [round(0.2 * k, 3) for k in range(8)]
    # Antes del primero, más cerca de uno o del otro, justo en medio (al anterior, como en la definitiva) y junto al último.
    pista = [(-0.04, (1, 1), 0.5), (0.04, (2, 2), 0.6), (0.16, (3, 3), 0.7), (0.3, (4, 4), 0.8), (0.5, (5, 5), 0.9), (0.56, (6, 6), 0.4), (1.36, (7, 7), 0.3)]
    instantes = [_instante_directo(k, balon=k % 3 != 1) for k in range(8)]
    copia = copy.deepcopy(instantes)
    azar, azar_np = random.getstate(), np.random.get_state()
    directo = _empezado(tmp_path)
    for k, inst in enumerate(instantes):
        # Cada balón vecino llega con el primer instante analizado que va detrás de él, como en la pasada.
        llegan = [v for v in pista if v[0] <= inst.t and (k == 0 or v[0] > instantes[k - 1].t)]
        directo.fotograma(inst, llegan, tracklets=3 + k)
        assert len(_lineas_directo(tmp_path)) == k
    directo.fin_pasada()

    lineas = _lineas_directo(tmp_path)
    assert [x["t"] for x in lineas] == tiempos
    vecinos = repartir_vecinos(pista, tiempos)
    assert [x.get("v", []) for x in lineas] == [vecinos.get(k, []) for k in range(8)]
    # Lo mismo que la línea de la definitiva, con el tracklet del seguidor (aún no se sabe quién es nadie) y el balón visto.
    for inst, linea in zip(instantes, lineas):
        assert linea == json.loads(linea_fotograma(inst, None, "v" if inst.balon_imagen is not None else "-", None, linea.get("v", [])))
    assert IndiceFotogramas(tmp_path / CARPETA / DIRECTO).resumen() == {"desde": 0, "t": tiempos, "balon": "vnvv-vvn", "calibrado": "10111011"}
    # Sin tocar lo que analiza ni el azar.
    assert instantes == copia
    assert random.getstate() == azar
    ahora_np = np.random.get_state()
    assert ahora_np[0] == azar_np[0] and np.array_equal(ahora_np[1], azar_np[1]) and ahora_np[2:] == azar_np[2:]
    directo.terminar()


def test_el_estado_dice_como_va_lo_ultimo_que_dijo_y_late_aunque_este_en_pausa(tmp_path):
    reloj = itertools.count(1000.0, 0.5)
    avance = _Avance("equipos")
    directo = Directo(tmp_path, latido_s=0.01, reloj=lambda: next(reloj))
    nombres = [{"id": 0, "nombre": "CD Vallobín"}, {"id": 1, "nombre": "Real Titánico"}]
    directo.empezar(analizador={"nombre": "Ojeador", "version": "2.3.0"}, video=VIDEO_DIRECTO, equipos=nombres, avance=avance)
    estado = _estado(tmp_path)
    assert estado["formato"] == 1 and estado["analizador"]["version"] == "2.3.0" and estado["video"] == VIDEO_DIRECTO
    assert estado["inicio"] == 1000.0 and estado["etapa"] == "equipos" and estado["pausado"] is False and estado["hitos"] == []
    # Los nombres desde el principio, para la lista del visor; los colores, cuando se separan los equipos.
    assert estado["equipos"] == nombres and "campo" not in estado and "dt" not in estado

    dichos = []
    decir = directo.con_hitos(dichos.append)
    for i in range(MAX_HITOS + 3):
        decir(f"Hito  {i}\n  con espacios")
    assert len(dichos) == MAX_HITOS + 3 and dichos[0] == "Hito  0\n  con espacios"
    avance.pausado = True

    def en_pausa():
        e = _estado(tmp_path)
        return e if e["pausado"] else None

    estado = _esperar(en_pausa)
    assert estado["hitos"] == [f"Hito {i} con espacios" for i in range(3, MAX_HITOS + 3)]
    assert estado["actualizado"] > estado["inicio"]

    avance.pausado, avance.etapa_actual = False, "pasada"
    directo.empezar_pasada(video=VIDEO_DIRECTO, dt=0.2, equipos=EQUIPOS_DIRECTO, campo=Campo(), total=8)
    for k in range(3):
        directo.fotograma(_instante_directo(k), tracklets=10 + k)

    def con_dos():
        e = _estado(tmp_path)
        return e if e.get("fotogramas") == 2 and e.get("tracklets") == 12 and not e["pausado"] else None

    estado = _esperar(con_dos)
    assert estado["etapa"] == "pasada" and estado["t"] == 0.2 and estado["dt"] == 0.2 and estado["fotogramas_total"] == 8
    assert estado["equipos"] == EQUIPOS_DIRECTO and len(estado["campo"]["vertices"]) == 32 and estado["inicio_pasada"] > estado["inicio"]
    directo.terminar()
    assert not (tmp_path / CARPETA / ESTADO_DIRECTO).exists() and not (tmp_path / CARPETA / DIRECTO).exists()


def test_si_el_analisis_falla_lo_provisional_se_queda_con_el_porque(tmp_path):
    directo = _empezado(tmp_path)
    directo.fotograma(_instante_directo(0))
    directo.fotograma(_instante_directo(1))
    directo.fallo(RuntimeError("CUDA sin memoria\n   en la pasada"))
    estado = _estado(tmp_path)
    assert estado["error"] == "CUDA sin memoria en la pasada" and estado["etapa"] == "pasada" and estado["fotogramas"] == 1
    assert len(_lineas_directo(tmp_path)) == 1
    parado = _empezado(tmp_path / "otra")
    parado.fallo(KeyboardInterrupt())
    assert _estado(tmp_path / "otra")["error"] == "Parado a mano."


def test_si_no_puede_escribir_se_apaga_y_el_analisis_sigue_igual(tmp_path, capsys):
    archivo = tmp_path / "no-es-una-carpeta"
    archivo.write_text("x", encoding="utf-8")
    directo = Directo(archivo, latido_s=0.01)
    directo.empezar(analizador={}, video={}, avance=_Avance())
    directo.empezar_pasada(video={}, dt=0.2, equipos=[], campo=Campo(), total=1)
    directo.fotograma(_instante_directo(0))
    directo.fin_pasada()
    directo.fallo(RuntimeError("otra cosa"))
    directo.terminar()
    assert directo.activo is False
    assert "la repetición en directo se ha parado" in capsys.readouterr().err

    class _DiscoLleno:
        def write(self, _texto):
            raise OSError(28, "No queda espacio en el disco")

        def flush(self):
            pass

        def close(self):
            pass

    roto = _empezado(tmp_path / "roto")
    roto.fotograma(_instante_directo(0))
    roto._archivo.close()
    roto._archivo = _DiscoLleno()
    roto.fotograma(_instante_directo(1))
    roto.fotograma(_instante_directo(2))
    roto.fin_pasada()
    assert roto.activo is False and roto._archivo is None and roto._hilo is None
    assert "No queda espacio" in capsys.readouterr().err
    # Sin latido, el visor lo daría por parado aunque el análisis siga: lo provisional se quita.
    assert not (tmp_path / "roto" / CARPETA / ESTADO_DIRECTO).exists() and not (tmp_path / "roto" / CARPETA / DIRECTO).exists()
    roto.fallo(RuntimeError("después"))
    roto.terminar()
    assert not (tmp_path / "roto" / CARPETA / ESTADO_DIRECTO).exists()


def test_sin_repeticion_no_escribe_nada_y_lo_cortado_de_antes_no_vale(tmp_path):
    apagado = Directo(tmp_path / "sin", activo=False)
    apagado.empezar(analizador={}, video={})
    assert apagado.con_hitos(print) is print
    apagado.empezar_pasada(video={}, dt=0.2, equipos=[], campo=Campo(), total=1)
    apagado.fotograma(_instante_directo(0))
    apagado.fin_pasada()
    apagado.terminar()
    assert not (tmp_path / "sin").exists()

    (tmp_path / CARPETA).mkdir()
    (tmp_path / CARPETA / DIRECTO).write_text('{"t":99.0,"p":[]}\n', encoding="utf-8")
    directo = Directo(tmp_path, latido_s=3600.0)
    directo.empezar(analizador={}, video={})
    assert not (tmp_path / CARPETA / DIRECTO).exists()
    directo.terminar()


# ---------------------------------------------------------------- la repetición no cambia lo que se propone

ANCHO, ALTO, FPS = 1280, 720, 25.0
# 10 s a 5 fotogramas analizados por segundo.
MUESTREADOS = 50
IMAGEN = np.full((ALTO, ANCHO, 3), (40, 140, 40), dtype=np.uint8)
# Lo que hace la prueba al detectar en cada fotograma analizado (para mirar a mitad de la pasada).
_AL_DETECTAR = {"mirar": None}


def _caja(k, j):
    x = 160.0 + 170.0 * j + 25.0 * np.sin(0.2 * k + j)
    y = 300.0 + 90.0 * (j % 3) + 15.0 * np.cos(0.15 * k + j)
    return (x - 14.0, y - 70.0, x + 14.0, y)


def _balon(k):
    """Junto a los pies de uno u otro jugador; en uno de cada cinco fotogramas analizados no se ve."""
    if k % 5 == 3:
        return None
    x1, _, x2, y2 = _caja(k, (k // 6) % 6)
    return ((x1 + x2) / 2 + 12.0, y2 - 4.0)


class _Clasificador:
    colores = [(200, 30, 30), (30, 30, 200)]
    reparto = (300, 300)
    desequilibrado = False

    def __init__(self, modelos, config):
        pass

    def ajustar(self, info, avance=None, por_fotograma=None):
        return 600

    def clasificar(self, imagen, personas):
        equipos = np.array([-1 if t == 7 else (0 if t <= 3 else 1) for t in personas.tracker_id])
        return equipos, np.ones(len(equipos))


class _Calibrador:
    def __init__(self, modelo, device, modo="movil", cada_s=0.0):
        self.modo = modo
        campo = Campo()
        imagen = np.array([[0, 0], [ANCHO, 0], [ANCHO, ALTO], [0, ALTO]], dtype=np.float32)
        self.h = Homografia(imagen, np.array([[0, 0], [campo.largo, 0], [campo.largo, campo.ancho], [0, campo.ancho]], dtype=np.float32))
        self.actual = Calibracion()
        self.aciertos = 0

    def calibrar(self, imagen, t, forzar=False):
        self.actual = Calibracion(t=t) if round(t / 0.2) % 7 == 4 else Calibracion(self.h, 12, 2.0, t)
        self.aciertos += self.actual.valida
        return self.actual

    def invalidar(self, t):
        self.actual = Calibracion(t=t)

    def a_campo(self, puntos):
        return self.actual.homografia.a_campo(puntos) if self.actual.valida and len(puntos) else None

    def punto_a_campo(self, xy):
        salida = self.a_campo(np.array([xy], dtype=np.float32))
        return None if salida is None else (float(salida[0][0]), float(salida[0][1]))

    def resumen(self):
        return {"modo": self.modo, "aciertos": self.aciertos}


class _Detector:
    def __init__(self, modelos, config):
        self.rastreador = SimpleNamespace(nombre="prueba")
        self.intermedios_mirados = self.vecinos_enteros = self.descartados = self.flojos_descartados = 0
        self._ultimo = None

    @property
    def ultimo_balon(self):
        return self._ultimo

    def detectar(self, imagen, t):
        k = round(t / 0.2)
        if _AL_DETECTAR["mirar"] is not None:
            _AL_DETECTAR["mirar"](k)
        cajas = [_caja(k, j) for j in range(6)] + [(620.0, 120.0, 648.0, 190.0)]
        personas = sv.Detections(
            xyxy=np.array(cajas, dtype=np.float32), confidence=np.full(7, 0.9, dtype=np.float32),
            class_id=np.array([2] * 6 + [3]), tracker_id=np.arange(1, 8),
        )
        balon = _balon(k)
        if balon is None:
            return Detecciones(personas, sv.Detections.empty())
        self._ultimo = (t, np.array(balon, dtype=np.float32), None)
        caja = np.array([[balon[0] - 5, balon[1] - 5, balon[0] + 5, balon[1] + 5]], dtype=np.float32)
        return Detecciones(personas, sv.Detections(xyxy=caja, confidence=np.array([0.8], dtype=np.float32), class_id=np.array([0])))

    def balon_vecinos(self, intermedios, previo, actual, vecinos, dt, enteros):
        self.intermedios_mirados += len(intermedios)
        k = round(actual.t / 0.2)
        if actual.balon is not None or k % 10 != 3:
            return []
        x, y = _balon(k - 1)
        return [(intermedios[0].t, np.array([x + 3.0, y], dtype=np.float32), 0.6)]

    def recordar_balon(self, t, centro):
        if self._ultimo is None or t > self._ultimo[0]:
            self._ultimo = (t, np.asarray(centro, dtype=np.float32), None)


def _fotogramas(config, info, paso=1, lectura=None, vecinos=0, medio=False):
    for k in range(MUESTREADOS):
        indice = k * paso
        intermedios = [] if k == 0 else [Fotograma(i, round(i / FPS, 4), IMAGEN) for i in (indice - paso + 1, indice - 1)]
        yield Fotograma(indice, round(indice / FPS, 4), IMAGEN, intermedios)


def _leer(carpeta, nombre):
    return json.loads((carpeta / nombre).read_text(encoding="utf-8"))


def test_la_repeticion_no_cambia_nada_de_lo_que_se_propone(tmp_path, monkeypatch):
    modelos = SimpleNamespace(
        rutas={"jugadores": "prueba", "balon": "prueba", "campo": "prueba"}, device="cpu",
        jugadores=SimpleNamespace(motor="prueba"), balon=SimpleNamespace(motor="prueba"), campo=SimpleNamespace(motor="prueba"),
    )
    falsos = {
        "info_video": lambda ruta: InfoVideo(ruta, ANCHO, ALTO, FPS, 250),
        "describir_gpu": lambda: "sin GPU (prueba)",
        "cargar_modelos": lambda carpeta, device: modelos,
        "describir_modelos": lambda *args, **kwargs: [],
        "ClasificadorEquipos": _Clasificador,
        "Calibrador": _Calibrador,
        "Detector": _Detector,
        "fotogramas": _fotogramas,
        # Con un latido muy seguido, para que escriba a la vez que la pasada.
        "Directo": functools.partial(Directo, latido_s=0.01),
    }
    for nombre, falso in falsos.items():
        monkeypatch.setattr(analizador, nombre, falso)
    video = tmp_path / "partido.mp4"
    a_mitad = {}

    def mirar_en(salida):
        def mirar(k):
            if k == 30:
                hay = (salida / CARPETA / ESTADO_DIRECTO).exists()
                a_mitad[salida.name] = (_estado(salida) if hay else None, _lineas_directo(salida), (salida / CARPETA).exists())
        return mirar

    for nombre, repeticion in (("con", True), ("sin", False)):
        salida = tmp_path / nombre
        monkeypatch.setitem(_AL_DETECTAR, "mirar", mirar_en(salida))
        config = Configuracion(
            video=video, salida=salida, device="cpu", segmento=Segmento(0.0, 10.0), dorsales=False, apariencia=False, clips=False,
            repeticion=repeticion, nombres_equipos=("CD Vallobín", "Real Titánico"),
        )
        analizador.analizar(config, informar=lambda texto: None)
    con, sin = tmp_path / "con", tmp_path / "sin"

    # A mitad de la pasada ya se ve lo provisional: hasta el instante de antes del último analizado.
    estado, lineas, _ = a_mitad["con"]
    assert [x["t"] for x in lineas] == [round(0.2 * k, 3) for k in range(29)]
    assert estado["etapa"] == "pasada" and estado["fotogramas_total"] == MUESTREADOS and estado["dt"] == 0.2 and "error" not in estado
    assert [e["nombre"] for e in estado["equipos"]] == ["CD Vallobín", "Real Titánico"]
    assert all(p[0] == p[1] for x in lineas for p in x["p"])
    assert a_mitad["sin"] == (None, [], False)
    # Al terminar, la definitiva y nada de lo provisional; sin repetición, nada.
    assert (con / CARPETA / FINAL).exists() and (con / CARPETA / FOTOGRAMAS).exists()
    assert not (con / CARPETA / DIRECTO).exists() and not (con / CARPETA / ESTADO_DIRECTO).exists()
    assert not (sin / CARPETA).exists()

    # Y lo que se propone es lo mismo con y sin ella.
    assert _leer(con, "acta-propuesta.json") == _leer(sin, "acta-propuesta.json")
    analisis_con, analisis_sin = _leer(con, "analisis.json"), _leer(sin, "analisis.json")
    assert analisis_con["archivos"].pop("repeticion") == f"{CARPETA}/{FINAL}" and analisis_sin["archivos"].pop("repeticion") is None
    analisis_con.pop("tiempos")
    analisis_sin.pop("tiempos")
    assert analisis_con == analisis_sin
    recuento = analisis_con["recuento"]
    assert recuento["fotogramas"] == MUESTREADOS and recuento["con_balon"] > 0 and recuento["tracklets"] >= 6
