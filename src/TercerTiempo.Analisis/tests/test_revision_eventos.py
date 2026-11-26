import json
from shutil import copyfile

import pytest

from tt_analisis.evaluar_eventos import evaluar, main
from tt_analisis.repeticion import CARPETA, FINAL
from tt_analisis.revision_eventos import ConflictoRevision, Revisiones, fuente_revision, huella_video
from test_visor import _id, _pedir, visor


def _pasada(version, eventos):
    return {
        "analizador": {"version": version},
        "video": {"tramo_inicio_s": 0, "tramo_fin_s": 60},
        "eventos": [{"tipo": tipo, "t": t} for tipo, t in eventos],
    }


def _orden(fuente, revision, **kwargs):
    return {"pasada": fuente["pasada"], "video": revision["video"], "rev": revision["rev"], **kwargs}


def test_las_anotaciones_sobreviven_a_una_nueva_pasada_y_protegen_la_edicion(tmp_path):
    video = tmp_path / "partido.mp4"
    video.write_bytes(b"el mismo video")
    copia = copyfile(video, tmp_path / "segunda-ruta.mp4")
    huella = huella_video(video)
    assert huella_video(copia) == huella
    almacen = Revisiones(tmp_path / "etiquetas" / "revision.sqlite")
    uno = fuente_revision(_pasada("2.3", [("tiro", 12), ("parada", 27)]))
    dos = fuente_revision(_pasada("2.4", [("tiro", 13), ("parada", 28)]))
    inicial = almacen.leer(huella)

    real = almacen.editar(huella, uno, _orden(uno, inicial, accion="decidir", id=0, estado="real"))
    con_parada = almacen.editar(huella, uno, _orden(uno, real, accion="anotar", tipo="parada", t=27.25, nota="En el área"))
    confirmado = almacen.editar(huella, dos, _orden(dos, con_parada, accion="decidir", id=0, estado="real"))
    assert [(h["tipo"], h["t"]) for h in confirmado["hechos"]] == [("tiro", 12), ("parada", 27.25)]
    with pytest.raises(ConflictoRevision, match="otra ventana"):
        almacen.editar(huella, uno, _orden(uno, real, accion="decidir", id=0, estado="falsa"))
    with pytest.raises(ConflictoRevision, match="Cambió"):
        almacen.editar(huella, dos, _orden(uno, confirmado, accion="decidir", id=0, estado="falsa"))

    sin_primera_marca = almacen.editar(huella, uno, _orden(uno, confirmado, accion="decidir", id=0, estado="falsa", nota="Centro"))
    assert len(sin_primera_marca["hechos"]) == 2
    corregido = almacen.editar(huella, dos, _orden(dos, sin_primera_marca, accion="decidir", id=0, estado="pendiente"))
    assert corregido["hechos"] == [con_parada["hechos"][1]]
    assert Revisiones(almacen.ruta).leer(huella) == corregido
    assert almacen.leer("otro-video")["hechos"] == []


def test_mide_aciertos_duplicados_y_omisiones_por_tipo_solo_en_tramos_revisados(tmp_path):
    almacen = Revisiones(tmp_path / "revision.sqlite")
    fuente = fuente_revision(_pasada("2.4", [
        ("tiro", 3), ("tiro", 11.3), ("tiro", 11.7), ("tiro", 16),
        ("tiro", 34), ("tiro", 45), ("parada", 23), ("gol", 24),
    ]))
    revision = almacen.leer("video")
    for accion in [
        {"accion": "anotar", "tipo": "tiro", "t": 13.3},
        {"accion": "anotar", "tipo": "tiro", "t": 19},
        {"accion": "anotar", "tipo": "tiro", "t": 41},
        {"accion": "decidir", "id": 4, "estado": "dudosa"},
        {"accion": "cubrir", "tipo": "tiro", "desde": 10, "hasta": 35},
        {"accion": "cubrir", "tipo": "tiro", "desde": 30, "hasta": 40},
    ]:
        revision = almacen.editar("video", fuente, _orden(fuente, revision, **accion))
    resultado = evaluar(revision, fuente)
    tiro = resultado["tiro"]
    assert tiro["segundos_revisados"] == 26
    assert tiro["segundos_excluidos_por_duda"] == 4
    assert tiro["parejas"] == [{"real": 13.3, "propuesto": 11.3}]
    assert [e["t"] for e in tiro["propuestas_sin_hecho"]] == [11.7, 16]
    assert [h["t"] for h in tiro["hechos_sin_propuesta"]] == [19]
    assert (tiro["precision_pct"], tiro["cobertura_pct"]) == (33.3, 50.0)
    assert resultado["parada"]["precision_pct"] is None
    assert resultado["saque_puerta"]["cobertura_pct"] is None
    assert 34 not in [e["t"] for e in evaluar(revision, fuente, margen=0)["tiro"]["propuestas_sin_hecho"]]

    revision = almacen.editar("video", fuente, _orden(fuente, revision, accion="decidir", id=1, estado="falsa"))
    assert evaluar(revision, fuente)["tiro"]["parejas"] == [{"real": 13.3, "propuesto": 11.7}]


def test_el_visor_guarda_las_etiquetas_sin_tocar_ninguna_salida_del_analisis(visor):
    servidor, tmp_path = visor
    servidor.revisiones = Revisiones(tmp_path / "etiquetas.sqlite")
    carpeta = tmp_path / "salidas" / "j8" / "pasada"
    final = carpeta / CARPETA / FINAL
    final.write_text(json.dumps(_pasada("2.3", [("tiro", 0.6)])), encoding="utf-8")
    propuesta = carpeta / "acta-propuesta.json"
    propuesta.write_bytes(b'{"tiros":1}')
    salidas = {r: r.read_bytes() for r in (propuesta, final, carpeta / "analisis.json")}
    ruta = f"/api/a/{_id(tmp_path)}/revision-eventos"
    respuesta, contenido = _pedir(servidor, "GET", ruta)
    assert respuesta.status == 200
    datos = json.loads(contenido)
    orden = _orden(datos["fuente"], datos["revision"], accion="decidir", id=0, estado="falsa", nota="Era un centro")
    assert _pedir(servidor, "POST", ruta, orden, {"Origin": "http://otro.example"})[0].status == 403
    respuesta, contenido = _pedir(servidor, "POST", ruta, orden)
    assert respuesta.status == 200
    guardado = json.loads(contenido)["revision"]
    assert guardado["decisiones"][datos["fuente"]["pasada"]]["marcas"]["0"]["estado"] == "falsa"
    assert _pedir(servidor, "POST", ruta, orden)[0].status == 409
    assert json.loads(_pedir(servidor, "GET", ruta)[1])["revision"] == guardado
    assert all(r.read_bytes() == contenido for r, contenido in salidas.items())


def test_la_evaluacion_comprueba_que_es_el_mismo_video(tmp_path, capsys):
    video = tmp_path / "partido.mp4"
    video.write_bytes(b"video de referencia")
    datos = _pasada("2.4", [("saque_puerta", 12)])
    datos["video"]["ruta"] = str(video)
    analisis = tmp_path / "analisis.json"
    analisis.write_text(json.dumps(datos), encoding="utf-8")
    almacen = Revisiones(tmp_path / "revision.sqlite")
    revision = almacen.leer(huella_video(video))
    ruta = tmp_path / "revision.json"
    ruta.write_text(json.dumps(revision), encoding="utf-8")
    assert main([str(ruta), str(tmp_path), "--json"]) == 0
    assert json.loads(capsys.readouterr().out)["saque_puerta"]["precision_pct"] is None
    video.write_bytes(b"otro")
    with pytest.raises(SystemExit) as error:
        main([str(ruta), str(analisis)])
    assert error.value.code == 2
    assert "otro archivo de vídeo" in capsys.readouterr().err
