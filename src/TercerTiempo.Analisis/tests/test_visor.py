import http.client
import json
import threading

import pytest

from tt_analisis.repeticion import CARPETA, FINAL, FOTOGRAMAS
from tt_analisis.visor.servidor import Biblioteca, Servidor, identificador, absoluta

VIDEO = bytes(range(256)) * 40


def _analisis(carpeta, video):
    (carpeta / CARPETA).mkdir(parents=True)
    (carpeta / "recortes").mkdir()
    datos = {
        "analizador": {"nombre": "Ojeador", "version": "2.3.0"},
        "video": {"ruta": str(video), "tramo_inicio_s": 0.0, "tramo_fin_s": 1.0, "duracion_s": 1.0},
        "equipos": [{"nombre": "CD Vallobín"}, {"nombre": "Real Titánico"}],
        "recuento": {"pct_con_balon": 50.0},
        "identificacion": {"jugadores": [{"id": 900007}]},
    }
    (carpeta / "analisis.json").write_text(json.dumps(datos), encoding="utf-8")
    (carpeta / CARPETA / FINAL).write_text(json.dumps({"formato": 1}), encoding="utf-8")
    with (carpeta / CARPETA / FOTOGRAMAS).open("w", encoding="utf-8", newline="\n") as f:
        for i in range(6):
            f.write(json.dumps({"t": round(i * 0.2, 3), "p": []}, separators=(",", ":")) + "\n")
    (carpeta / "recortes" / "900007.jpg").write_bytes(b"\xff\xd8foto")
    return carpeta


@pytest.fixture
def visor(tmp_path):
    video = tmp_path / "partido.mp4"
    video.write_bytes(VIDEO)
    _analisis(tmp_path / "salidas" / "j8" / "pasada", video)
    servidor = Servidor(("127.0.0.1", 0), Biblioteca([tmp_path / "salidas"]))
    hilo = threading.Thread(target=servidor.serve_forever, daemon=True)
    hilo.start()
    yield servidor, tmp_path
    servidor.shutdown()
    servidor.server_close()


def _pedir(servidor, metodo, ruta, cuerpo=None, cabeceras=None):
    conexion = http.client.HTTPConnection("127.0.0.1", servidor.server_port, timeout=5)
    todas = {"Host": f"127.0.0.1:{servidor.server_port}", **(cabeceras or {})}
    datos = None if cuerpo is None else json.dumps(cuerpo).encode("utf-8")
    if datos is not None:
        todas["Content-Type"] = "application/json"
    conexion.request(metodo, ruta, body=datos, headers=todas)
    respuesta = conexion.getresponse()
    contenido = respuesta.read()
    conexion.close()
    return respuesta, contenido


def _id(tmp_path):
    return identificador(absoluta(tmp_path / "salidas" / "j8" / "pasada"))


def test_solo_atiende_a_la_pagina_del_propio_visor(visor):
    servidor, _ = visor
    assert _pedir(servidor, "GET", "/api/analisis")[0].status == 200
    assert _pedir(servidor, "GET", "/api/analisis", cabeceras={"Host": "malo.example:80"})[0].status == 403
    assert _pedir(servidor, "GET", "/api/analisis", cabeceras={"Sec-Fetch-Site": "cross-site"})[0].status == 403
    assert _pedir(servidor, "POST", "/api/latido", {}, {"Origin": "http://malo.example"})[0].status == 403
    assert _pedir(servidor, "POST", "/api/latido", {}, {"Origin": "null"})[0].status == 403
    propio = {"Origin": f"http://127.0.0.1:{servidor.server_port}", "Sec-Fetch-Site": "same-origin"}
    assert _pedir(servidor, "POST", "/api/latido", {}, propio)[0].status == 200


def test_encuentra_los_analisis_de_la_carpeta_y_da_su_ficha(visor):
    servidor, tmp_path = visor
    respuesta, contenido = _pedir(servidor, "GET", "/api/analisis")
    fichas = json.loads(contenido)
    assert [f["id"] for f in fichas] == [_id(tmp_path)]
    ficha = fichas[0]
    assert ficha["equipos"] == ["CD Vallobín", "Real Titánico"]
    assert ficha["analizador"] == "Ojeador 2.3.0"
    assert ficha["repeticion"] is True and ficha["video_existe"] is True
    assert ficha["balon_pct"] == 50.0 and ficha["jugadores"] == 1
    assert json.loads(_pedir(servidor, "GET", f"/api/a/{_id(tmp_path)}/final.json")[1]) == {"formato": 1}
    assert _pedir(servidor, "GET", f"/api/a/{_id(tmp_path)}/acta-propuesta.json")[0].status == 404
    assert _pedir(servidor, "GET", "/api/a/000000000000/ficha")[0].status == 404


def test_el_video_se_sirve_por_trozos(visor):
    servidor, tmp_path = visor
    ruta = f"/api/a/{_id(tmp_path)}/video"
    respuesta, contenido = _pedir(servidor, "GET", ruta, cabeceras={"Range": "bytes=10-19"})
    assert respuesta.status == 206 and contenido == VIDEO[10:20]
    assert respuesta.getheader("Content-Range") == f"bytes 10-19/{len(VIDEO)}"
    respuesta, contenido = _pedir(servidor, "GET", ruta, cabeceras={"Range": "bytes=-5"})
    assert respuesta.status == 206 and contenido == VIDEO[-5:]
    respuesta, contenido = _pedir(servidor, "GET", ruta, cabeceras={"Range": f"bytes={len(VIDEO)}-"})
    assert respuesta.status == 416 and respuesta.getheader("Content-Range") == f"bytes */{len(VIDEO)}"
    respuesta, contenido = _pedir(servidor, "GET", ruta)
    assert respuesta.status == 200 and contenido == VIDEO and respuesta.getheader("Accept-Ranges") == "bytes"


def test_los_fotogramas_van_por_tramos(visor):
    servidor, tmp_path = visor
    respuesta, contenido = _pedir(servidor, "GET", f"/api/a/{_id(tmp_path)}/fotogramas?desde=0.2&hasta=0.6")
    assert respuesta.status == 200
    assert [x["t"] for x in json.loads(contenido)] == [0.2, 0.4, 0.6]
    assert _pedir(servidor, "GET", f"/api/a/{_id(tmp_path)}/fotogramas?desde=uno")[0].status == 400


def test_abrir_otra_carpeta_y_decir_donde_esta_el_video(visor):
    servidor, tmp_path = visor
    otro_video = tmp_path / "otro.mp4"
    otro_video.write_bytes(b"0123456789")
    otra = _analisis(tmp_path / "suelta", tmp_path / "no-esta.mp4")
    respuesta, contenido = _pedir(servidor, "POST", "/api/abrir", {"carpeta": f'"{otra}"'})
    ficha = json.loads(contenido)
    assert respuesta.status == 200 and ficha["video_existe"] is False
    assert _pedir(servidor, "GET", f"/api/a/{ficha['id']}/video")[0].status == 404
    assert _pedir(servidor, "POST", "/api/abrir", {"carpeta": str(tmp_path / "nada")})[0].status == 404
    assert _pedir(servidor, "POST", f"/api/a/{ficha['id']}/video", {"ruta": str(tmp_path / "nada.mp4")})[0].status == 404
    respuesta, contenido = _pedir(servidor, "POST", f"/api/a/{ficha['id']}/video", {"ruta": str(otro_video)})
    assert respuesta.status == 200 and json.loads(contenido)["video_existe"] is True
    assert _pedir(servidor, "GET", f"/api/a/{ficha['id']}/video")[1] == b"0123456789"


def test_ni_estaticos_ni_recortes_salen_de_su_carpeta(visor):
    servidor, tmp_path = visor
    respuesta, contenido = _pedir(servidor, "GET", "/estatico/visor.js")
    assert respuesta.status == 200 and respuesta.getheader("Content-Type").startswith("text/javascript")
    assert _pedir(servidor, "GET", "/estatico/..%5Cservidor.py")[0].status == 404
    assert _pedir(servidor, "GET", "/estatico/%2E%2E%2Fservidor.py")[0].status == 404
    ident = _id(tmp_path)
    assert _pedir(servidor, "GET", f"/api/a/{ident}/recortes/900007.jpg")[1] == b"\xff\xd8foto"
    assert _pedir(servidor, "GET", f"/api/a/{ident}/recortes/..%2Fanalisis.json")[0].status == 404
    respuesta, contenido = _pedir(servidor, "GET", "/")
    assert respuesta.status == 200 and b"/estatico/visor.js" in contenido
