import http.client
import json
import os
from shutil import copyfile

import pytest

from tt_analisis.evaluar_eventos import evaluar, leer_pasada, main, tabla
from tt_analisis.repeticion import CARPETA, FINAL
from tt_analisis.revision_eventos import ACCIONES, ConflictoRevision, Revisiones, catalogo, fuente_revision, huella_video
from tt_analisis.visor import servidor as servidor_mod
from test_visor import _id, _pedir, visor

SIETE = {"equipo": "CD Vallobín", "dorsal": 7}
DIEZ = {"equipo": "CD Vallobín", "dorsal": 10}


def _pasada(version, eventos):
    return {
        "analizador": {"version": version},
        "video": {"tramo_inicio_s": 0, "tramo_fin_s": 60},
        "eventos": [{"tipo": tipo, "t": t} for tipo, t in eventos],
    }


def _final_con_acciones():
    """Como `repeticion/final.json`: las acciones van por jugador identificado (900…) o por tracklet sin identificar."""
    return {
        **_pasada("2.4", [("tiro", 20.0)]),
        "equipos": [{"id": 0, "nombre": "CD Vallobín"}, {"id": 1, "nombre": "Real Titánico"}],
        "jugadores": [
            {"id": 900007, "equipo": 0, "dorsal": 7, "nombre": "PÉREZ GIL, ANA"},
            {"id": 900010, "equipo": 0, "dorsal": 10, "nombre": "GIL SOTO, LUIS"},
            {"id": 900104, "equipo": 1, "dorsal": 4, "nombre": ""},
            {"id": 900200, "equipo": 1, "dorsal": None, "nombre": ""},
        ],
        "acciones": {
            "900007": {"equipo": 0, "toques": 9, "pases": [5.0, 7.5], "perdidas": [12.0], "paradas": []},
            "900010": {"equipo": 0, "pases": [44.0], "pases_fallados": [50.0]},
            "33": {"equipo": 1, "recuperaciones": [12.4], "pases": [40.0, 80.0]},
        },
    }


def _orden(fuente, revision, **kwargs):
    return {"pasada": fuente["pasada"], "video": revision["video"], "rev": revision["rev"], **kwargs}


def _aplicar(almacen, fuente, revision, *ordenes):
    for orden in ordenes:
        revision = almacen.editar(revision["video"], fuente, _orden(fuente, revision, **orden))
    return revision


def test_cada_tipo_dice_en_que_segundo_se_anota_y_con_que_margen():
    datos = catalogo()
    tipos = set(datos["jugadas"]) | set(datos["acciones"])
    assert set(datos["tipos"]) == set(datos["pistas"]) == set(datos["margenes"]) == tipos
    assert all(datos["margenes"][t] == 1.0 for t in datos["acciones"])


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


def test_dos_versiones_que_proponen_lo_mismo_comparten_los_veredictos():
    misma = [("tiro", 12), ("parada", 27)]
    assert fuente_revision(_pasada("2.3.2", misma))["pasada"] == fuente_revision(_pasada("2.4.0", misma))["pasada"]
    assert fuente_revision(_pasada("2.4.0", misma))["pasada"] != fuente_revision(_pasada("2.4.0", [("tiro", 12.1), ("parada", 27)]))["pasada"]


def test_dos_jugadas_reales_seguidas_son_dos_hechos(tmp_path):
    almacen = Revisiones(tmp_path / "revision.sqlite")
    uno = fuente_revision(_pasada("2.3", [("tiro", 10.0), ("tiro", 11.0)]))
    dos = fuente_revision(_pasada("2.4", [("tiro", 10.6)]))
    revision = _aplicar(almacen, uno, almacen.leer("video"),
                        {"accion": "decidir", "id": 0, "estado": "real"},
                        {"accion": "decidir", "id": 1, "estado": "real"},
                        {"accion": "cubrir", "tipo": "tiro", "desde": 0, "hasta": 60})
    assert sorted(h["t"] for h in revision["hechos"]) == [10.0, 11.0]
    assert evaluar(revision, uno)["tiro"]["aciertos"] == 2

    # La de otra versión se queda con el hecho más cercano y no crea otro.
    revision = _aplicar(almacen, dos, revision, {"accion": "decidir", "id": 0, "estado": "real"})
    marca = revision["decisiones"][dos["pasada"]]["marcas"]["0"]
    assert len(revision["hechos"]) == 2
    assert next(h for h in revision["hechos"] if h["id"] == marca["hecho"])["t"] == 11.0
    tiro = evaluar(revision, dos)["tiro"]
    assert (tiro["aciertos"], tiro["falsas"], tiro["omitidas"]) == (1, 0, 1)


def test_real_en_este_segundo_guarda_el_segundo_exacto(tmp_path):
    almacen = Revisiones(tmp_path / "revision.sqlite")
    fuente = fuente_revision(_pasada("2.4", [("tiro", 10.0), ("parada", 30.0), ("tiro", 59.5)]))
    revision = _aplicar(almacen, fuente, almacen.leer("video"), {"accion": "decidir", "id": 0, "estado": "real"})
    assert [(h["t"], h["origen"]) for h in revision["hechos"]] == [(10.0, "confirmada")]

    revision = _aplicar(almacen, fuente, revision, {"accion": "decidir", "id": 0, "estado": "real", "t": 10.4004})
    assert [(h["t"], h["origen"]) for h in revision["hechos"]] == [(10.4, "exacta")]
    for orden, error in [
        ({"id": 0, "estado": "real", "t": 12.5}, "más de 2 s"),
        ({"id": 0, "estado": "falsa", "t": 10.2}, "solo se da al marcarla como real"),
        ({"id": 2, "estado": "real", "t": 60.5}, "fuera del tramo"),
        ({"id": 0, "estado": "real", "t": float("nan")}, "no válido"),
    ]:
        with pytest.raises(ValueError, match=error):
            almacen.editar("video", fuente, _orden(fuente, revision, accion="decidir", **orden))

    # Lo anotado a mano no se mueve: la propuesta se enlaza a ello.
    revision = _aplicar(almacen, fuente, revision,
                        {"accion": "anotar", "tipo": "parada", "t": 29.0},
                        {"accion": "decidir", "id": 1, "estado": "real", "t": 29.5})
    paradas = [(h["t"], h["origen"]) for h in revision["hechos"] if h["tipo"] == "parada"]
    assert paradas == [(29.0, "manual")]


def test_mide_aciertos_duplicados_y_omisiones_por_tipo_solo_en_tramos_revisados(tmp_path):
    almacen = Revisiones(tmp_path / "revision.sqlite")
    fuente = fuente_revision(_pasada("2.4", [
        ("tiro", 3), ("tiro", 11.3), ("tiro", 11.7), ("tiro", 16),
        ("tiro", 34), ("tiro", 45), ("parada", 23), ("paron", 24),
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
    assert "paron" not in resultado
    assert 34 not in [e["t"] for e in evaluar(revision, fuente, margen=0)["tiro"]["propuestas_sin_hecho"]]

    revision = almacen.editar("video", fuente, _orden(fuente, revision, accion="decidir", id=1, estado="falsa"))
    assert evaluar(revision, fuente)["tiro"]["parejas"] == [{"real": 13.3, "propuesto": 11.7}]


def test_la_medicion_respeta_los_bordes_de_los_tramos_y_las_dudas(tmp_path):
    almacen = Revisiones(tmp_path / "revision.sqlite")
    # Lo que pasó al final del tramo puede proponerse un poco después; lo de fuera no cuenta como falso.
    fuente = fuente_revision(_pasada("2.4", [("tiro", 9.0), ("tiro", 20.8), ("tiro", 21.5)]))
    revision = _aplicar(almacen, fuente, almacen.leer("borde"),
                        {"accion": "anotar", "tipo": "tiro", "t": 19.5},
                        {"accion": "cubrir", "tipo": "tiro", "desde": 10, "hasta": 20})
    tiro = evaluar(revision, fuente)["tiro"]
    assert tiro["parejas"] == [{"real": 19.5, "propuesto": 20.8}]
    assert (tiro["falsas"], tiro["omitidas"], tiro["precision_pct"]) == (0, 0, 100.0)

    # Lo marcado «No se ve» en una versión aparta lo que hay cerca en todas, pero una propuesta de otra versión dentro de
    # esa zona sigue acertando lo que pasó justo antes.
    a = fuente_revision(_pasada("2.3", [("tiro", 30.0)]))
    b = fuente_revision(_pasada("2.4", [("tiro", 29.0), ("tiro", 31.5)]))
    revision = _aplicar(almacen, a, almacen.leer("dudas"),
                        {"accion": "decidir", "id": 0, "estado": "dudosa"},
                        {"accion": "anotar", "tipo": "tiro", "t": 27.5},
                        {"accion": "cubrir", "tipo": "tiro", "desde": 0, "hasta": 60})
    en_b = evaluar(revision, b)["tiro"]
    assert en_b["parejas"] == [{"real": 27.5, "propuesto": 29.0}]
    assert (en_b["falsas"], en_b["omitidas"], en_b["segundos_excluidos_por_duda"]) == (0, 0, 4)
    en_a = evaluar(revision, a)["tiro"]
    assert (en_a["aciertos"], en_a["falsas"], en_a["omitidas"]) == (0, 0, 1)


def test_las_acciones_del_acta_se_revisan_con_quien_las_hizo(tmp_path):
    fuente = fuente_revision(_final_con_acciones())
    acciones = {c["id"]: c for c in fuente["candidatos"] if c["tipo"] in ACCIONES}
    assert set(acciones) == {
        "pase:900007:0", "pase:900007:1", "perdida:900007:0", "pase:900010:0", "pase_fallado:900010:0",
        "recuperacion:33:0", "pase:33:0",
    }
    assert acciones["pase:900007:0"]["jugador"] == {**SIETE, "nombre": "PÉREZ GIL, ANA"}
    assert (acciones["recuperacion:33:0"]["jugador"], acciones["recuperacion:33:0"]["equipo"]) == (None, 1)
    assert [c["t"] for c in fuente["candidatos"]] == sorted(c["t"] for c in fuente["candidatos"])
    assert fuente["equipos"] == {0: "CD Vallobín", 1: "Real Titánico"}
    assert [(j["equipo"], j["dorsal"]) for j in fuente["jugadores"]] == [("CD Vallobín", 7), ("CD Vallobín", 10), ("Real Titánico", 4)]

    almacen = Revisiones(tmp_path / "revision.sqlite")
    revision = _aplicar(almacen, fuente, almacen.leer("video"),
                        {"accion": "decidir", "id": "pase:900007:0", "estado": "real", "quien": "bien"},
                        {"accion": "decidir", "id": "pase:900007:1", "estado": "real", "quien": "otro", "jugador": DIEZ},
                        {"accion": "decidir", "id": "perdida:900007:0", "estado": "falsa", "motivo": "balon_falso"},
                        {"accion": "decidir", "id": "recuperacion:33:0", "estado": "real", "quien": "otro", "jugador": {"equipo": "Real Titánico", "dorsal": 4}})
    hechos = {h["t"]: h for h in revision["hechos"]}
    assert hechos[5.0]["jugador"] == SIETE and "no_es" not in hechos[5.0]
    assert hechos[7.5]["jugador"] == DIEZ and hechos[7.5]["no_es"] == [SIETE]
    assert hechos[12.4]["jugador"] == {"equipo": "Real Titánico", "dorsal": 4}
    assert revision["decisiones"][fuente["pasada"]]["marcas"]["perdida:900007:0"]["motivo"] == "balon_falso"

    for orden, error in [
        ({"id": "recuperacion:33:0", "estado": "real", "quien": "bien"}, "Quién la hizo"),
        ({"id": 0, "estado": "real", "quien": "bien"}, "Quién la hizo"),
        ({"id": "pase:900007:0", "estado": "falsa", "quien": "otro"}, "Quién la hizo"),
        ({"id": "pase:900007:0", "estado": "real", "quien": "otro", "jugador": SIETE}, "márcalo como que fue él"),
        ({"id": "pase:900007:0", "estado": "real", "quien": "bien", "jugador": DIEZ}, "solo se da cuando fue otro"),
        ({"id": "pase:900007:0", "estado": "real", "quien": "otro", "jugador": {**DIEZ, "dorsal": 100}}, "Jugador no válido"),
        ({"id": "pase:900007:0", "estado": "real", "motivo": "balon_falso"}, "motivo"),
        ({"id": "pase:900007:0", "estado": "falsa", "motivo": "inventado"}, "motivo"),
        ({"id": "pase:900007:9", "estado": "real"}, "Candidato"),
        ({"id": "0", "estado": "real"}, "Candidato"),
    ]:
        with pytest.raises(ValueError, match=error):
            almacen.editar("video", fuente, _orden(fuente, revision, accion="decidir", **orden))

    # «No se ve quién» deja de dar al jugador por sabido, pero recuerda quién no fue.
    revision = _aplicar(almacen, fuente, revision, {"accion": "decidir", "id": "pase:900007:1", "estado": "real", "quien": "no_se_sabe"})
    hecho = next(h for h in revision["hechos"] if h["t"] == 7.5)
    assert hecho["jugador"] is None and hecho["no_es"] == [SIETE]

    revision = _aplicar(almacen, fuente, revision,
                        {"accion": "anotar", "tipo": "pase", "t": 30.0, "jugador": DIEZ},
                        {"accion": "anotar", "tipo": "tiro", "t": 31.0})
    anotados = {h["t"]: h for h in revision["hechos"]}
    assert anotados[30.0]["jugador"] == DIEZ and "jugador" not in anotados[31.0]
    with pytest.raises(ValueError, match="Solo las acciones"):
        almacen.editar("video", fuente, _orden(fuente, revision, accion="anotar", tipo="tiro", t=32.0, jugador=DIEZ))


def test_la_medicion_de_las_acciones_cuenta_quien_las_hizo_y_por_que_son_falsas(tmp_path):
    fuente = fuente_revision(_final_con_acciones())
    almacen = Revisiones(tmp_path / "revision.sqlite")
    revision = _aplicar(almacen, fuente, almacen.leer("video"),
                        {"accion": "decidir", "id": "pase:900007:0", "estado": "real", "quien": "bien"},
                        {"accion": "decidir", "id": "pase:900007:1", "estado": "real", "quien": "otro", "jugador": DIEZ},
                        {"accion": "decidir", "id": "pase:33:0", "estado": "real", "quien": "otro", "jugador": SIETE},
                        {"accion": "anotar", "tipo": "pase", "t": 44.6},
                        {"accion": "anotar", "tipo": "pase", "t": 20.0, "jugador": SIETE},
                        {"accion": "decidir", "id": "perdida:900007:0", "estado": "falsa", "motivo": "balon_falso"},
                        {"accion": "decidir", "id": "recuperacion:33:0", "estado": "falsa"},
                        *({"accion": "cubrir", "tipo": tipo, "desde": 0, "hasta": 60} for tipo in ("pase", "perdida", "pase_fallado", "recuperacion")))
    medicion = evaluar(revision, fuente)
    pase = medicion["pase"]
    assert pase["margen_s"] == 1.0
    assert (pase["aciertos"], pase["falsas"], pase["omitidas"]) == (4, 0, 1)
    assert [p["jugador"] for p in pase["parejas"]] == ["bien", "mal", "sin_identificar", "sin_saber"]
    assert pase["jugador"] == {"bien": 1, "mal": 1, "sin_identificar": 1, "sin_saber": 1, "acierto_pct": 50.0}
    assert medicion["perdida"]["motivos"] == {"balon_falso": 1}
    assert medicion["recuperacion"]["motivos"] == {"sin_motivo": 1}
    assert medicion["pase_fallado"]["motivos"] == {"sin_revisar": 1}
    assert "jugador" not in medicion["tiro"] and medicion["tiro"]["segundos_revisados"] == 0
    # Con `--margen`, el mismo para todos los tipos.
    assert evaluar(revision, fuente, margen=2)["pase"]["margen_s"] == 2

    texto = tabla(medicion)
    assert "| Pase | 60 | 4 | 0 | 1 | 100 % | 80 % | 50 % (1 de 2) |" in texto
    assert "- Pérdida: 1 balón falso" in texto
    assert "Tiro" in texto.split("Sin tramos revisados:")[1]


def test_la_copia_de_cada_video_recupera_la_revision(tmp_path):
    huella = "a" * 64
    copias = tmp_path / "copias"
    almacen = Revisiones(tmp_path / "uno.sqlite", copias)
    fuente = fuente_revision(_pasada("2.4", [("tiro", 10.0)]))
    revision = _aplicar(almacen, fuente, almacen.leer(huella), {"accion": "decidir", "id": 0, "estado": "real"})
    assert almacen.guardar_copia(revision) is None
    assert json.loads((copias / f"{huella}.json").read_text(encoding="utf-8")) == revision
    assert not list(copias.glob("*.tmp"))

    # Sin el SQLite, sale de la copia y se sigue desde su revisión de edición.
    otro = Revisiones(tmp_path / "nuevo.sqlite", copias)
    assert otro.leer(huella) == revision
    siguiente = otro.editar(huella, fuente, _orden(fuente, revision, accion="cubrir", tipo="tiro", desde=0, hasta=60))
    assert siguiente["rev"] == revision["rev"] + 1 and siguiente["hechos"] == revision["hechos"]

    # Ni copias de lo que no es una huella, ni copias rotas o de otro vídeo.
    assert almacen.guardar_copia({**revision, "video": "../fuera"}) is None
    assert not (tmp_path / "fuera.json").exists()
    (copias / f"{'b' * 64}.json").write_text("{roto", encoding="utf-8")
    (copias / f"{'c' * 64}.json").write_text(json.dumps({**revision, "video": "d" * 64}), encoding="utf-8")
    assert otro.leer("b" * 64)["rev"] == otro.leer("c" * 64)["rev"] == 0

    # Si no se puede escribir, se dice por qué; lo del SQLite sigue guardado.
    no_es_carpeta = tmp_path / "no-es-carpeta"
    no_es_carpeta.write_text("", encoding="utf-8")
    assert Revisiones(tmp_path / "dos.sqlite", no_es_carpeta).guardar_copia(revision)


def test_el_visor_guarda_las_etiquetas_sin_tocar_ninguna_salida_del_analisis(visor):
    servidor, tmp_path = visor
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
    assert datos["catalogo"]["tipos"]["saque_portero"] == "Saque del portero"
    orden = _orden(datos["fuente"], datos["revision"], accion="decidir", id=0, estado="falsa", nota="Era un centro", motivo="otra_accion")
    assert _pedir(servidor, "POST", ruta, orden, {"Origin": "http://otro.example"})[0].status == 403
    respuesta, contenido = _pedir(servidor, "POST", ruta, orden)
    assert respuesta.status == 200
    respuesta = json.loads(contenido)
    guardado = respuesta["revision"]
    assert "aviso" not in respuesta
    assert guardado["decisiones"][datos["fuente"]["pasada"]]["marcas"]["0"]["motivo"] == "otra_accion"
    copia = tmp_path / "revisiones" / "copias" / f"{guardado['video']}.json"
    assert json.loads(copia.read_text(encoding="utf-8")) == guardado
    assert _pedir(servidor, "POST", ruta, orden)[0].status == 409
    assert json.loads(_pedir(servidor, "GET", ruta)[1])["revision"] == guardado
    assert all(r.read_bytes() == contenido for r, contenido in salidas.items())

    # Sin poder escribir la copia, se guarda igual y se avisa.
    no_es_carpeta = tmp_path / "no-es-carpeta"
    no_es_carpeta.write_text("", encoding="utf-8")
    servidor.revisiones = Revisiones(servidor.revisiones.ruta, no_es_carpeta)
    orden = _orden(datos["fuente"], guardado, accion="decidir", id=0, estado="pendiente")
    respuesta = json.loads(_pedir(servidor, "POST", ruta, orden)[1])
    assert respuesta["revision"]["rev"] == guardado["rev"] + 1
    assert "sin la copia de seguridad" in respuesta["aviso"]


def test_el_visor_no_rehace_la_lista_ni_la_huella_en_cada_peticion(visor, monkeypatch):
    servidor, tmp_path = visor
    final = tmp_path / "salidas" / "j8" / "pasada" / CARPETA / FINAL
    final.write_text(json.dumps(_pasada("2.3", [("tiro", 0.6)])), encoding="utf-8")
    llamadas = {"fuente": 0, "huella": 0}

    def contar(nombre, funcion):
        def contada(*args):
            llamadas[nombre] += 1
            return funcion(*args)
        return contada

    monkeypatch.setattr(servidor_mod, "fuente_revision", contar("fuente", servidor_mod.fuente_revision))
    monkeypatch.setattr(servidor_mod, "huella_video", contar("huella", servidor_mod.huella_video))
    ruta = f"/api/a/{_id(tmp_path)}/revision-eventos"
    for _ in range(3):
        assert _pedir(servidor, "GET", ruta)[0].status == 200
    assert llamadas == {"fuente": 1, "huella": 1}

    final.write_text(json.dumps(_pasada("2.3", [("tiro", 0.6), ("parada", 0.8)])), encoding="utf-8")
    marca = final.stat().st_mtime_ns + 1_000_000_000
    os.utime(final, ns=(marca, marca))
    datos = json.loads(_pedir(servidor, "GET", ruta)[1])
    assert len(datos["fuente"]["candidatos"]) == 2
    assert llamadas == {"fuente": 2, "huella": 1}


def test_una_peticion_demasiado_grande_cierra_la_conexion(visor):
    servidor, tmp_path = visor
    for largo, estado in ((str(1 << 17), 413), ("mucho", 400)):
        conexion = http.client.HTTPConnection("127.0.0.1", servidor.server_port, timeout=5)
        conexion.putrequest("POST", f"/api/a/{_id(tmp_path)}/revision-eventos", skip_host=True)
        conexion.putheader("Host", f"127.0.0.1:{servidor.server_port}")
        conexion.putheader("Content-Length", largo)
        conexion.endheaders()
        respuesta = conexion.getresponse()
        assert (respuesta.status, respuesta.getheader("Connection")) == (estado, "close")
        conexion.close()


def test_la_evaluacion_lee_la_repeticion_antes_que_analisis_json(tmp_path):
    carpeta = tmp_path / "pasada"
    (carpeta / CARPETA).mkdir(parents=True)
    (carpeta / "analisis.json").write_text(json.dumps({"de": "analisis"}), encoding="utf-8")
    (carpeta / CARPETA / FINAL).write_text(json.dumps({"de": "repeticion"}), encoding="utf-8")
    assert leer_pasada(carpeta) == leer_pasada(carpeta / "analisis.json") == {"de": "repeticion"}
    (carpeta / CARPETA / FINAL).unlink()
    assert leer_pasada(carpeta) == {"de": "analisis"}


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
