import gzip
import json
import os
import pickle
import sqlite3
from pathlib import Path

import numpy as np
import pytest

from tt_analisis import __version__, analizador
from tt_analisis.__main__ import main
from tt_analisis.apariencia import LADO
from tt_analisis.config import Configuracion, Segmento
from tt_analisis.dorsales import Lectura
from tt_analisis.rehacer import ARCHIVO, SinPasada, guardar, leer, medir, preparar_salida
from tt_analisis.repeticion import CARPETA, FINAL, FOTOGRAMAS
from tt_analisis.revision_eventos import huella_video
from tt_analisis.video import Fotograma
from tt_analisis.visor.servidor import CONTENIDO_DE_UN_ANALISIS

from test_repeticion import ALTO, ANCHO, FPS, MUESTREADOS, _caja, _falsear_analizador, _leer

# El dorsal del jugador j de `_caja` (tracklets 1 a 6; los tres primeros, del equipo 0). Cada uno va pintado de un gris
# (GRIS + PASO_GRIS·j) y el lector falso lee el dorsal por ese gris; el aspecto falso, lo mismo.
DORSALES = (4, 5, 6, 7, 8, 9)
GRIS, PASO_GRIS = 20, 30


def _gris_a_jugador(gris: float) -> int | None:
    j = round((gris - GRIS) / PASO_GRIS)
    return j if 0 <= j < len(DORSALES) and abs(gris - (GRIS + PASO_GRIS * j)) < 3 else None


def _imagen(k):
    imagen = np.full((ALTO, ANCHO, 3), (40, 140, 40), dtype=np.uint8)
    for j in range(len(DORSALES)):
        x1, y1, x2, y2 = (int(round(v)) for v in _caja(k, j))
        imagen[max(0, y1):y2, max(0, x1):x2] = GRIS + PASO_GRIS * j
    return imagen


def _fotogramas(config, info, paso=1, lectura=None, vecinos=0, medio=False):
    for k in range(MUESTREADOS):
        indice = k * paso
        imagen = _imagen(k)
        intermedios = [] if k == 0 else [Fotograma(i, round(i / FPS, 4), imagen) for i in (indice - paso + 1, indice - 1)]
        de_la_mitad = Fotograma(indice - paso // 2, round((indice - paso // 2) / FPS, 4), imagen) if medio and k else None
        yield Fotograma(indice, round(indice / FPS, 4), imagen, intermedios, de_la_mitad)


class _Lector:
    nombre = "prueba"

    def leer(self, zonas):
        lecturas = []
        for zona in zonas:
            j = _gris_a_jugador(float(np.median(zona)))
            probabilidades = np.full(99, 0.001, dtype=np.float32)
            if j is not None:
                probabilidades[DORSALES[j] - 1] = 0.9
            lecturas.append(Lectura(probabilidades, 0.05 if j is not None else 0.95))
        return lecturas


class _Apariencia:
    def embeber(self, cuadrados):
        vectores = np.zeros((len(cuadrados), 8), dtype=np.float32)
        for i, cuadrado in enumerate(cuadrados):
            j = _gris_a_jugador(float(cuadrado[LADO // 2, LADO // 2, 0]))
            vectores[i, 7 if j is None else j] = 1.0
        return vectores


def _plantillas(tmp_path: Path) -> Path:
    def jugadores(dorsales):
        return [{"dorsal": d, "nombre": f"Jugador {d}", "titular": True, "entra": None, "sale": None, "portero": d == 1} for d in dorsales]

    ruta = tmp_path / "plantillas.json"
    ruta.write_text(json.dumps({"parte": "partido", "minutos_parte": 45, "equipos": [
        {"lado": "local", "nombre": "CD Vallobín", "jugadores": jugadores((1, 4, 5, 6))},
        {"lado": "visitante", "nombre": "Real Titánico", "jugadores": jugadores((1, 7, 8, 9))},
    ]}), encoding="utf-8")
    return ruta


def _analizar(tmp_path: Path, monkeypatch, nombre: str = "analisis", **opciones) -> Path:
    """Un análisis de prueba con dorsales, aspecto y acta (`test_repeticion._falsear_analizador`, con un lector y un aspecto
    falsos): en `tmp_path`/`nombre`."""
    _falsear_analizador(monkeypatch)
    monkeypatch.setattr(analizador, "fotogramas", _fotogramas)
    monkeypatch.setattr(analizador, "cargar_lector", lambda carpeta, device, afinados=True: _Lector())
    monkeypatch.setattr(analizador.ap, "cargar_apariencia", lambda carpeta, device: _Apariencia())
    video = tmp_path / "partido.mp4"
    if not video.exists():
        video.write_bytes(b"no es un video de verdad, pero tiene huella" * 100)
    config = Configuracion(
        video=video, salida=tmp_path / nombre, device="cpu", segmento=Segmento(0.0, 10.0), clips=False,
        nombres_equipos=("CD Vallobín", "Real Titánico"), plantillas=_plantillas(tmp_path), **({"guardar_pasada": True} | opciones),
    )
    analizador.analizar(config, informar=lambda texto: None)
    return config.salida


def _archivos(carpeta: Path) -> dict[str, bytes]:
    return {str(p.relative_to(carpeta)).replace("\\", "/"): p.read_bytes() for p in sorted(carpeta.rglob("*")) if p.is_file()}


def test_rehacer_da_lo_mismo_que_el_analisis(tmp_path, monkeypatch):
    origen = _analizar(tmp_path, monkeypatch)
    assert (origen / ARCHIVO).is_file()
    # El visor lo reconoce como parte de un análisis: deja borrar la carpeta.
    assert {p.name.lower() for p in origen.iterdir()} <= CONTENIDO_DE_UN_ANALISIS

    salida = tmp_path / "rehecho"
    analizador.rehacer_jugadas(origen, salida, informar=lambda texto: None)

    analisis, rehecho = _leer(origen, "analisis.json"), _leer(salida, "analisis.json")
    # La prueba solo vale si pasa por todo: quién es quién con el acta y el aspecto, y el balón de los vecinos.
    assert len(analisis["identificacion"]["jugadores"]) >= 4 and analisis["identificacion"]["lecturas"] > 0
    assert analisis["identificacion"]["recortes_apariencia"] > 0 and analisis["recuento"]["balon_vecinos"]["mirados"] > 0
    assert rehecho.pop("rehecho") == {"de": str(origen.resolve()), "version_de_la_pasada": __version__}
    analisis.pop("tiempos")
    rehecho.pop("tiempos")
    assert rehecho == analisis
    # Todo lo demás, byte a byte, menos lo que lleva los tiempos y lo guardado de la pasada.
    antes, despues = _archivos(origen), _archivos(salida)
    for nombre in ("analisis.json", "resumen.md", ARCHIVO, "plantillas.json"):
        antes.pop(nombre, None)
        despues.pop(nombre, None)
    assert {f"{CARPETA}/{FINAL}", f"{CARPETA}/{FOTOGRAMAS}", "posiciones.csv.gz", "acta-propuesta.json"} <= set(antes)
    assert any(nombre.startswith("recortes/jugador-") for nombre in antes)
    assert despues.keys() == antes.keys()
    assert [n for n in antes if antes[n] != despues[n]] == []


def test_lo_rehecho_sale_del_codigo_de_ahora(tmp_path, monkeypatch):
    origen = _analizar(tmp_path, monkeypatch)
    monkeypatch.setattr(analizador, "cobertura", lambda instantes: {"vistos": 1, "interpolados": 0, "pct_con_balon": 2.0})
    analizador.rehacer_jugadas(origen, tmp_path / "rehecho", informar=lambda texto: None)
    assert _leer(origen, "analisis.json")["recuento"]["con_balon"] > 1
    assert _leer(tmp_path / "rehecho", "analisis.json")["recuento"]["con_balon"] == 1


def test_solo_se_guarda_si_se_pide(tmp_path, monkeypatch):
    sin = _analizar(tmp_path, monkeypatch, "sin", guardar_pasada=False)
    assert not (sin / ARCHIVO).exists()
    with pytest.raises(SinPasada, match="--guardar-pasada"):
        leer(sin)
    monkeypatch.setenv("TT_GUARDAR_PASADA", "1")
    assert (_analizar(tmp_path, monkeypatch, "con", guardar_pasada=False) / ARCHIVO).is_file()


def test_lo_guardado_solo_puede_traer_lo_de_un_analisis(tmp_path):
    hecho = tmp_path / "hecho.txt"

    class _Trampa:
        def __init__(self, funcion, argumentos):
            self.funcion, self.argumentos = funcion, argumentos

        def __reduce__(self):
            return self.funcion, self.argumentos

    for trampa in (_Trampa(Path.write_text, (hecho, "ejecutado")), _Trampa(os.system, (f'echo ejecutado > "{hecho}"',))):
        with gzip.open(tmp_path / ARCHIVO, "wb") as archivo:
            pickle.dump(trampa, archivo)
        with pytest.raises(ValueError, match="no es de un análisis"):
            leer(tmp_path)
        assert not hecho.exists()

    with gzip.open(tmp_path / ARCHIVO, "wb") as archivo:
        pickle.dump({"instantes": []}, archivo)
    with pytest.raises(ValueError, match="no es lo que guarda una pasada"):
        leer(tmp_path)
    (tmp_path / ARCHIVO).write_bytes(b"esto no es un gzip")
    with pytest.raises(ValueError, match="incompleto o dañado"):
        leer(tmp_path)


def test_otro_formato_no_se_lee(tmp_path, monkeypatch):
    estado = leer(_analizar(tmp_path, monkeypatch))
    estado.formato = 99
    viejo = tmp_path / "viejo"
    viejo.mkdir()
    guardar(estado, viejo)
    with pytest.raises(ValueError, match="formato 99"):
        leer(viejo)


def test_la_salida_solo_sustituye_lo_rehecho(tmp_path):
    origen = tmp_path / "salidas" / "j8"
    origen.mkdir(parents=True)
    (origen / "analisis.json").write_text("{}", encoding="utf-8")
    for mala in (origen, origen / "rehecho", tmp_path / "salidas"):
        with pytest.raises(ValueError, match="aparte"):
            preparar_salida(origen, mala)
    assert not (origen / "rehecho").exists()

    otra = tmp_path / "otra"
    otra.mkdir()
    (otra / "notas.txt").write_text("mías", encoding="utf-8")
    normal = tmp_path / "normal"
    normal.mkdir()
    (normal / "analisis.json").write_text("{}", encoding="utf-8")
    rehecho_con_mas = tmp_path / "mezclada"
    rehecho_con_mas.mkdir()
    (rehecho_con_mas / "analisis.json").write_text('{"rehecho": {}}', encoding="utf-8")
    (rehecho_con_mas / "partido.mp4").write_bytes(b"")
    for llena in (otra, normal, rehecho_con_mas):
        with pytest.raises(ValueError, match="otras cosas"):
            preparar_salida(origen, llena)
        assert any(llena.iterdir())

    anterior = tmp_path / "j8-rehecho"
    (anterior / "recortes").mkdir(parents=True)
    (anterior / "analisis.json").write_text('{"rehecho": {"de": "j8"}}', encoding="utf-8")
    (anterior / "recortes" / "tracklet-3.jpg").write_bytes(b"")
    preparar_salida(origen, anterior)
    assert anterior.is_dir() and not any(anterior.iterdir())
    nueva = tmp_path / "nueva" / "j8-rehecho"
    preparar_salida(origen, nueva)
    assert nueva.is_dir()


def _etiquetas(video: Path, tipo: str = "tiro") -> dict:
    return {"formato": 1, "video": huella_video(video), "rev": 1, "hechos": [], "tramos": [{"id": "t1", "tipo": tipo, "desde": 0.0, "hasta": 10.0}], "decisiones": {}}


def test_mide_con_las_etiquetas_del_visor_sin_crear_nada(tmp_path, monkeypatch):
    salida = tmp_path / "rehecho"
    analizador.rehacer_jugadas(_analizar(tmp_path, monkeypatch), salida, informar=lambda texto: None)
    video = tmp_path / "partido.mp4"
    sqlite, copias = tmp_path / "visor" / "revisiones.sqlite3", tmp_path / "visor" / "copias"

    assert medir(salida, ruta_revisiones=sqlite, copias=copias) == "Sin medir: no hay etiquetas del visor para este vídeo."
    assert not sqlite.exists() and not copias.exists()

    # De la copia, si no hay SQLite; del SQLite, abierto solo para leer, si lo hay.
    copias.mkdir(parents=True)
    (copias / f"{huella_video(video)}.json").write_text(json.dumps(_etiquetas(video, "tiro")), encoding="utf-8")
    assert "| Tiro | 10 |" in medir(salida, ruta_revisiones=sqlite, copias=copias)
    with sqlite3.connect(sqlite) as conexion:
        conexion.execute("CREATE TABLE revisiones (video TEXT PRIMARY KEY, datos TEXT NOT NULL)")
        conexion.execute("INSERT INTO revisiones VALUES (?, ?)", (huella_video(video), json.dumps(_etiquetas(video, "corner"))))
    conexion.close()
    tabla = medir(salida, ruta_revisiones=sqlite, copias=copias)
    assert "| Córner | 10 |" in tabla and "| Tiro |" not in tabla

    vacias = dict(_etiquetas(video), tramos=[])
    (tmp_path / "vacias.json").write_text(json.dumps(vacias), encoding="utf-8")
    assert "ningún tramo revisado" in medir(salida, tmp_path / "vacias.json")
    otro = dict(_etiquetas(video), video="0" * 64)
    (tmp_path / "otro.json").write_text(json.dumps(otro), encoding="utf-8")
    assert "otro archivo de vídeo" in medir(salida, tmp_path / "otro.json")


def test_la_orden_rehace_y_mide(tmp_path, monkeypatch, capsys):
    origen = _analizar(tmp_path, monkeypatch)
    etiquetas = tmp_path / "etiquetas.json"
    etiquetas.write_text(json.dumps(_etiquetas(tmp_path / "partido.mp4")), encoding="utf-8")
    capsys.readouterr()

    assert main(["rehacer-jugadas", str(origen), "--salida", str(tmp_path / "rehecho"), "--revision", str(etiquetas)]) == 0
    dicho = capsys.readouterr().out
    assert "Rehaciendo con Ojeador" in dicho and "Rehecho en" in dicho and "| Tiro | 10 |" in dicho
    # Otra vez a la misma salida: sustituye lo rehecho de antes.
    assert main(["rehacer-jugadas", str(origen), "--salida", str(tmp_path / "rehecho"), "--sin-medir"]) == 0
    assert "| Tiro |" not in capsys.readouterr().out

    assert main(["rehacer-jugadas", str(tmp_path / "sin-pasada")]) == 2
    assert "--guardar-pasada" in capsys.readouterr().err
    (tmp_path / "otra").mkdir()
    (tmp_path / "otra" / "notas.txt").write_text("mías", encoding="utf-8")
    assert main(["rehacer-jugadas", str(origen), "--salida", str(tmp_path / "otra")]) == 2
    assert "otras cosas" in capsys.readouterr().err and (tmp_path / "otra" / "notas.txt").exists()
