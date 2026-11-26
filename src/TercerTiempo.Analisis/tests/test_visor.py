import http.client
import json
import os
import shutil
import threading
import time
from pathlib import Path

import pytest

from tt_analisis.repeticion import CARPETA, DIRECTO, ESTADO_DIRECTO, FINAL, FOTOGRAMAS
from tt_analisis.visor import servidor as servidor_mod
from tt_analisis.visor.servidor import Biblioteca, Servidor, identificador, absoluta, va_a_la_papelera

VIDEO = bytes(range(256)) * 40


def _analisis(carpeta, video):
    (carpeta / CARPETA).mkdir(parents=True, exist_ok=True)
    (carpeta / "recortes").mkdir(exist_ok=True)
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
    # La revisión, en la carpeta de la prueba: nunca en la de etiquetas del equipo ni en datasets/revisiones.
    revisiones = tmp_path / "revisiones"
    servidor = Servidor(("127.0.0.1", 0), Biblioteca([tmp_path / "salidas"]), revisiones / "revisiones.sqlite3", revisiones / "copias")
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


# ---------------------------------------------------------------- análisis que se están haciendo

def _en_marcha(carpeta, video, *, actualizado=None, error=None, lineas=3, inicio=1000.0):
    """Lo que deja un análisis a medias (`repeticion.Directo`): su estado y los fotogramas que lleva."""
    (carpeta / CARPETA).mkdir(parents=True, exist_ok=True)
    estado = {
        "formato": 1, "analizador": {"nombre": "Ojeador", "version": "2.3.0"},
        "video": {"ruta": str(video), "ancho": 1280, "alto": 720, "fps": 25.0, "duracion_s": 10.0, "tramo_inicio_s": 0.0, "tramo_fin_s": 10.0},
        "inicio": inicio, "actualizado": time.time() if actualizado is None else actualizado, "etapa": "pasada", "pausado": False,
        "hitos": ["Siguiendo a jugadores y balón: 50 fotogramas a 5.0 por segundo de vídeo."], "dt": 0.2,
        "equipos": [{"id": 0, "nombre": "CD Vallobín", "color": "#c81e1e"}, {"id": 1, "nombre": "Real Titánico", "color": "#1e1ec8"}],
        "campo": {"largo_cm": 10000, "ancho_cm": 6000, "vertices": [], "aristas": []}, "fotogramas_total": 50, "fotogramas": lineas,
    }
    if error:
        estado["error"] = error
    (carpeta / CARPETA / ESTADO_DIRECTO).write_text(json.dumps(estado), encoding="utf-8")
    with (carpeta / CARPETA / DIRECTO).open("w", encoding="utf-8", newline="\n") as f:
        for i in range(lineas):
            linea = {"t": round(i * 0.2, 3), "i": i * 5, "h": None if i == 1 else [1, 0, 0, 0, 1, 0, 0, 0, 1], "p": [[3, 3, 2, 0, 10, 10, 30, 60]]}
            if i == 0:
                linea["b"] = [100.0, 50.0, "v", 0.8]
            f.write(json.dumps(linea, separators=(",", ":")) + "\n")
    return carpeta


def _fechas(carpeta, *, final, directo):
    """Cuándo se escribió cada uno (lo que decide cuál manda)."""
    for nombre, cuando in ((FINAL, final), (ESTADO_DIRECTO, directo)):
        ruta = carpeta / CARPETA / nombre
        if cuando is not None and ruta.exists():
            os.utime(ruta, (cuando, cuando))


def _json(servidor, ruta):
    respuesta, contenido = _pedir(servidor, "GET", ruta)
    assert respuesta.status == 200, contenido
    return json.loads(contenido)


def test_un_analisis_que_se_esta_haciendo_sale_en_la_lista_y_se_sigue_segun_avanza(visor):
    servidor, tmp_path = visor
    carpeta = _en_marcha(tmp_path / "salidas" / "j9", tmp_path / "partido.mp4")
    ident = identificador(absoluta(carpeta))
    ficha = next(f for f in _json(servidor, "/api/analisis") if f["id"] == ident)
    assert ficha["repeticion"] is False and ficha["en_curso"]["etapa"] == "pasada" and ficha["en_curso"]["vivo"] is True
    assert ficha["en_curso"]["fotogramas"] == 3 and ficha["en_curso"]["fotogramas_total"] == 50
    assert ficha["equipos"] == ["CD Vallobín", "Real Titánico"] and ficha["analizador"] == "Ojeador 2.3.0" and ficha["video_existe"] is True

    directo = _json(servidor, f"/api/a/{ident}/directo?desde=0")
    assert directo["terminado"] is False and directo["vivo"] is True and directo["dt"] == 0.2 and directo["hitos"]
    assert directo["instantes"] == {"desde": 0, "t": [0.0, 0.2, 0.4], "balon": "v--", "calibrado": "101"}
    # Lo nuevo, desde donde se quedó la página.
    with (carpeta / CARPETA / DIRECTO).open("a", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps({"t": 0.6, "i": 15, "h": None, "p": [], "v": [[0.56, 1.0, 2.0, 0.5]]}, separators=(",", ":")) + "\n")
    assert _json(servidor, f"/api/a/{ident}/directo?desde=3")["instantes"] == {"desde": 3, "t": [0.6], "balon": "n", "calibrado": "0"}
    assert [x["t"] for x in _json(servidor, f"/api/a/{ident}/fotogramas?desde=0.2&hasta=0.6")] == [0.2, 0.4, 0.6]
    assert _pedir(servidor, "GET", f"/api/a/{ident}/directo?desde=uno")[0].status == 400
    assert _pedir(servidor, "GET", f"/api/a/{ident}/video", cabeceras={"Range": "bytes=0-9"})[1] == VIDEO[:10]
    # También abriendo su carpeta a mano, aunque aún no tenga analisis.json.
    otra = _en_marcha(tmp_path / "suelta", tmp_path / "partido.mp4")
    respuesta, contenido = _pedir(servidor, "POST", "/api/abrir", {"carpeta": str(otra)})
    assert respuesta.status == 200 and json.loads(contenido)["en_curso"]["vivo"] is True


def test_al_terminar_se_pasa_a_la_repeticion_definitiva(visor):
    servidor, tmp_path = visor
    carpeta = _en_marcha(tmp_path / "salidas" / "j9", tmp_path / "partido.mp4")
    ident = identificador(absoluta(carpeta))
    assert _json(servidor, f"/api/a/{ident}/directo?desde=0")["terminado"] is False
    # El análisis deja de escribir directo.json antes de escribir la definitiva (y luego lo borra).
    _analisis(carpeta, tmp_path / "partido.mp4")
    ahora = time.time()
    _fechas(carpeta, final=ahora, directo=ahora - 5)
    assert _json(servidor, f"/api/a/{ident}/directo?desde=3") == {"terminado": True}
    ficha = _json(servidor, f"/api/a/{ident}/ficha")
    assert ficha["repeticion"] is True and ficha["en_curso"] is None and ficha["jugadores"] == 1
    assert [x["t"] for x in _json(servidor, f"/api/a/{ident}/fotogramas?desde=0&hasta=2")] == [0.0, 0.2, 0.4, 0.6, 0.8, 1.0]
    (carpeta / CARPETA / ESTADO_DIRECTO).unlink()
    (carpeta / CARPETA / DIRECTO).unlink()
    assert _json(servidor, f"/api/a/{ident}/directo?desde=0") == {"terminado": True}


def test_uno_vuelto_a_lanzar_en_la_carpeta_tapa_al_anterior_mientras_se_hace(visor):
    servidor, tmp_path = visor
    video = tmp_path / "partido.mp4"
    carpeta = tmp_path / "salidas" / "j10"
    _analisis(carpeta, video)
    _en_marcha(carpeta, video)
    ahora = time.time()
    _fechas(carpeta, final=ahora - 100, directo=ahora)
    ident = identificador(absoluta(carpeta))
    ficha = _json(servidor, f"/api/a/{ident}/ficha")
    assert ficha["repeticion"] is False and ficha["en_curso"]["vivo"] is True
    assert [x["t"] for x in _json(servidor, f"/api/a/{ident}/fotogramas?desde=0&hasta=2")] == [0.0, 0.2, 0.4]

    # Se para sin noticias: vuelve a verse el anterior, y la página sabe que el que seguía no terminó.
    _en_marcha(carpeta, video, actualizado=ahora - 120)
    _fechas(carpeta, final=ahora - 100, directo=ahora)
    ficha = _json(servidor, f"/api/a/{ident}/ficha")
    assert ficha["repeticion"] is True and ficha["en_curso"] is None
    assert _json(servidor, f"/api/a/{ident}/directo?desde=3") == {"terminado": True, "parado": "sin noticias del análisis"}
    assert [x["t"] for x in _json(servidor, f"/api/a/{ident}/fotogramas?desde=0&hasta=2")] == [0.0, 0.2, 0.4, 0.6, 0.8, 1.0]
    _en_marcha(carpeta, video, error="CUDA sin memoria")
    _fechas(carpeta, final=ahora - 100, directo=ahora)
    assert _json(servidor, f"/api/a/{ident}/directo?desde=0") == {"terminado": True, "parado": "CUDA sin memoria"}


def test_sin_uno_anterior_el_que_se_paro_se_sigue_viendo_con_el_porque(visor):
    servidor, tmp_path = visor
    video = tmp_path / "partido.mp4"
    parado = _en_marcha(tmp_path / "salidas" / "j11", video, actualizado=time.time() - 120)
    fallido = _en_marcha(tmp_path / "salidas" / "j12", video, error="No se puede abrir el vídeo")
    fichas = {f["id"]: f for f in _json(servidor, "/api/analisis")}
    sin_noticias = fichas[identificador(absoluta(parado))]["en_curso"]
    assert sin_noticias["vivo"] is False and sin_noticias["error"] is None
    con_error = fichas[identificador(absoluta(fallido))]["en_curso"]
    assert con_error["vivo"] is False and con_error["error"] == "No se puede abrir el vídeo"
    directo = _json(servidor, f"/api/a/{identificador(absoluta(fallido))}/directo?desde=0")
    assert directo["terminado"] is False and directo["vivo"] is False and directo["error"] == "No se puede abrir el vídeo"
    assert directo["instantes"]["t"] == [0.0, 0.2, 0.4]


def test_si_lo_provisional_desaparece_la_pagina_lo_sabe_y_sigue_mirando(visor):
    servidor, tmp_path = visor
    video = tmp_path / "partido.mp4"
    carpeta = _en_marcha(tmp_path / "salidas" / "j13", video)
    ident = identificador(absoluta(carpeta))
    assert _json(servidor, f"/api/a/{ident}/directo?desde=0")["instantes"]["t"] == [0.0, 0.2, 0.4]
    # El analizador dejó de escribirlo (o se vació la carpeta): ni lo provisional ni la definitiva.
    (carpeta / CARPETA / ESTADO_DIRECTO).unlink()
    (carpeta / CARPETA / DIRECTO).unlink()
    assert _json(servidor, f"/api/a/{ident}/directo?desde=3") == {"terminado": False, "sin_directo": True}
    assert ident not in [f["id"] for f in _json(servidor, "/api/analisis")]
    # Si en la carpeta empieza otro, es ese el que se sigue.
    _en_marcha(carpeta, video, inicio=2000.0, lineas=1)
    directo = _json(servidor, f"/api/a/{ident}/directo?desde=0")
    assert directo["inicio"] == 2000.0 and directo["vivo"] is True and directo["instantes"]["t"] == [0.0]
    assert ident in [f["id"] for f in _json(servidor, "/api/analisis")]


# ---------------------------------------------------------------- dónde están y borrarlos

@pytest.fixture
def papelera(monkeypatch):
    """Sin tocar la Papelera de verdad: lo que se le manda se apunta y se borra."""
    mandadas = []

    def a_la_papelera(carpeta):
        mandadas.append(carpeta)
        shutil.rmtree(carpeta)

    monkeypatch.setattr(servidor_mod, "va_a_la_papelera", lambda carpeta: True)
    monkeypatch.setattr(servidor_mod, "a_la_papelera", a_la_papelera)
    return mandadas


def _borrar(servidor, carpeta, cabeceras=None):
    respuesta, contenido = _pedir(servidor, "POST", f"/api/a/{identificador(absoluta(carpeta))}/borrar", {}, cabeceras)
    return respuesta.status, json.loads(contenido)


def test_dice_en_que_carpetas_busca(visor):
    servidor, tmp_path = visor
    assert _json(servidor, "/api/carpetas") == [{"ruta": str(absoluta(tmp_path / "salidas")), "existe": True}]


def test_borrar_lo_manda_a_la_papelera_y_lo_quita_de_la_lista(visor, papelera):
    servidor, tmp_path = visor
    carpeta = tmp_path / "salidas" / "j8" / "pasada"
    assert _json(servidor, f"/api/a/{_id(tmp_path)}/ficha")["papelera"] is True
    # Desde otra web, nunca.
    assert _borrar(servidor, carpeta, {"Origin": "http://malo.example"})[0] == 403
    assert _borrar(servidor, carpeta, {"Sec-Fetch-Site": "cross-site"})[0] == 403
    assert carpeta.exists() and papelera == []

    assert _borrar(servidor, carpeta) == (200, {"ok": True, "papelera": True})
    assert papelera == [absoluta(carpeta)] and not carpeta.exists()
    # El vídeo del partido no se toca.
    assert (tmp_path / "partido.mp4").read_bytes() == VIDEO
    assert _json(servidor, "/api/analisis") == []
    assert _pedir(servidor, "GET", f"/api/a/{_id(tmp_path)}/ficha")[0].status == 404
    estado, respuesta = _borrar(servidor, carpeta)
    assert estado == 404 and respuesta["error"] == "Ese análisis ya no está."


def test_sin_papelera_se_borra_del_todo(visor, monkeypatch):
    servidor, tmp_path = visor
    mandadas = []
    monkeypatch.setattr(servidor_mod, "va_a_la_papelera", lambda carpeta: False)
    monkeypatch.setattr(servidor_mod, "a_la_papelera", mandadas.append)
    carpeta = tmp_path / "salidas" / "j8" / "pasada"
    assert _json(servidor, f"/api/a/{_id(tmp_path)}/ficha")["papelera"] is False
    assert _borrar(servidor, carpeta) == (200, {"ok": True, "papelera": False})
    assert not carpeta.exists() and mandadas == []


def test_no_borra_uno_que_se_esta_haciendo_y_si_uno_que_se_paro(visor, papelera):
    servidor, tmp_path = visor
    video = tmp_path / "partido.mp4"
    vivo = _en_marcha(tmp_path / "salidas" / "j9", video)
    estado, respuesta = _borrar(servidor, vivo)
    assert estado == 409 and respuesta["error"].startswith("Se está analizando")
    assert (vivo / CARPETA / ESTADO_DIRECTO).exists()
    # Tampoco uno vuelto a lanzar encima de otro terminado: mientras se hace, es el que manda.
    encima = _analisis(tmp_path / "salidas" / "j10", video)
    _en_marcha(encima, video)
    assert _borrar(servidor, encima)[0] == 409 and (encima / "analisis.json").exists()

    # Uno que se paró (sin noticias) o que falló, sí.
    parado = _en_marcha(tmp_path / "salidas" / "j11", video, actualizado=time.time() - 120)
    fallido = _en_marcha(tmp_path / "salidas" / "j12", video, error="CUDA sin memoria")
    assert _borrar(servidor, parado) == (200, {"ok": True, "papelera": True})
    assert _borrar(servidor, fallido) == (200, {"ok": True, "papelera": True})
    assert not parado.exists() and not fallido.exists()
    assert papelera == [absoluta(parado), absoluta(fallido)]
    assert sorted(f["id"] for f in _json(servidor, "/api/analisis")) == sorted([_id(tmp_path), *(identificador(absoluta(c)) for c in (vivo, encima))])


def test_no_borra_una_carpeta_con_mas_cosas_que_las_de_un_analisis(visor, papelera):
    servidor, tmp_path = visor
    video = tmp_path / "partido.mp4"
    # Una --salida puesta en una carpeta con el vídeo y otras cosas.
    mezclada = _analisis(tmp_path / "salidas" / "j14", video)
    (mezclada / "partido-entero.mp4").write_bytes(b"video")
    (mezclada / "notas.txt").write_text("ojo", encoding="utf-8")
    estado, respuesta = _borrar(servidor, mezclada)
    assert estado == 409 and "(«notas.txt», «partido-entero.mp4»)" in respuesta["error"]
    (mezclada / "tactica").mkdir()
    (mezclada / "fotos").mkdir()
    (mezclada / "otro.mp4").write_bytes(b"")
    assert "(«fotos», «notas.txt», «otro.mp4» y más)" in _borrar(servidor, mezclada)[1]["error"]
    assert (mezclada / "partido-entero.mp4").exists() and (mezclada / "analisis.json").exists() and papelera == []

    # Con todo lo que puede dejar un análisis (también lo opcional y lo de la app), sí.
    completa = _analisis(tmp_path / "salidas" / "j15", video)
    for nombre in ("acta-propuesta.json", "resumen.md", "posiciones.csv.gz", "anotado.mp4", "porteros.pkl", "tramos.pkl",
                   "apariencias.pkl", "pasada.pkl.gz", "plantillas.json", "stderr.txt"):
        (completa / nombre).write_bytes(b"")
    for nombre in ("mapas", "Clips"):
        (completa / nombre).mkdir()
    assert _borrar(servidor, completa)[0] == 200 and not completa.exists()


def test_la_ficha_dice_si_es_de_la_app(visor):
    servidor, tmp_path = visor
    de_la_app = _analisis(tmp_path / "salidas" / "videos" / "analysis" / ("ab" * 16), tmp_path / "partido.mp4")
    suelta = _analisis(tmp_path / "salidas" / "analysis" / "j8", tmp_path / "partido.mp4")
    fichas = {f["id"]: f for f in _json(servidor, "/api/analisis")}
    assert fichas[identificador(absoluta(de_la_app))]["de_la_app"] is True
    assert fichas[identificador(absoluta(suelta))]["de_la_app"] is False
    assert fichas[_id(tmp_path)]["de_la_app"] is False


def test_a_la_papelera_solo_va_lo_de_un_disco_del_equipo(tmp_path):
    docker = Path(r"\\wsl.localhost\docker-desktop\mnt\docker-desktop-disk\data\docker\volumes\tt\_data\videos\analysis\x")
    assert va_a_la_papelera(docker) is False
    assert va_a_la_papelera(tmp_path) is (os.name == "nt")


def test_solo_abre_en_el_explorador_carpetas_del_visor(visor, monkeypatch):
    servidor, tmp_path = visor
    abiertas = []
    monkeypatch.setattr(servidor_mod, "abrir_en_el_explorador", abiertas.append)
    carpeta = tmp_path / "salidas" / "j8" / "pasada"
    # La de un análisis, aunque la lista aún no se haya pedido, y la carpeta en que busca.
    assert _pedir(servidor, "POST", "/api/explorar", {"carpeta": str(carpeta)})[0].status == 200
    assert _pedir(servidor, "POST", "/api/explorar", {"carpeta": f'"{tmp_path / "salidas"}"'})[0].status == 200
    # Ni otra cualquiera (aunque exista), ni una que ya no está, ni desde otra web.
    assert _pedir(servidor, "POST", "/api/explorar", {"carpeta": str(tmp_path)})[0].status == 404
    assert _pedir(servidor, "POST", "/api/explorar", {"carpeta": str(tmp_path / "salidas" / "j8")})[0].status == 404
    assert _pedir(servidor, "POST", "/api/explorar", {"carpeta": ""})[0].status == 404
    assert _pedir(servidor, "POST", "/api/explorar", {"carpeta": str(carpeta)}, {"Origin": "http://malo.example"})[0].status == 403
    assert abiertas == [absoluta(carpeta), absoluta(tmp_path / "salidas")]
    shutil.rmtree(carpeta)
    assert _pedir(servidor, "POST", "/api/explorar", {"carpeta": str(carpeta)})[0].status == 404
