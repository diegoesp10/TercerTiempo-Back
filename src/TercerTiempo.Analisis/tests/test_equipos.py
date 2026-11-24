from types import SimpleNamespace

import cv2
import numpy as np
import pytest
import supervision as sv

from tt_analisis import equipos
from tt_analisis.config import CLASE_ARBITRO, CLASE_JUGADOR, CLASE_PORTERO, Configuracion, Segmento
from tt_analisis.equipos import ClasificadorEquipos, instantes_muestra
from tt_analisis.video import info_video


def video_numerado(ruta, segundos, fps=1):
    """Un vídeo en el que cada fotograma lleva su número en binario, en 8 franjas blancas o negras (la compresión no
    lo estropea): así se sabe qué fotograma se ha leído."""
    escritor = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"mp4v"), fps, (96, 64))
    for k in range(int(segundos * fps)):
        imagen = np.zeros((64, 96, 3), dtype=np.uint8)
        for bit in range(8):
            if k >> bit & 1:
                imagen[:, bit * 12:(bit + 1) * 12] = 255
        escritor.write(imagen)
    escritor.release()
    return info_video(ruta)


def numero(imagen):
    franjas = imagen[:, :, 0].reshape(64, 8, 12).mean(axis=(0, 2))
    return sum(1 << bit for bit, media in enumerate(franjas) if media > 128)


class JugadoresFalsos:
    """Tres jugadores por fotograma; apunta qué fotograma se le ha pasado."""

    def __init__(self):
        self.vistos = []

    def detectar(self, imagen, confianza):
        self.vistos.append(numero(imagen))
        cajas = np.array([[10, 10, 30, 50], [40, 10, 60, 50], [70, 10, 90, 50]], dtype=float)
        return sv.Detections(xyxy=cajas, class_id=np.full(3, CLASE_JUGADOR), confidence=np.full(3, 0.9))


@pytest.fixture
def dos_camisetas(monkeypatch):
    # Rasgos de dos equipaciones alternas, para que el clasificador tenga dos grupos claros.
    cuenta = iter(range(10_000))
    rng = np.random.default_rng(3)

    def rasgos(_recorte):
        base = np.array([200, 120, 120, 30, 30, 30, 0, 0], dtype=np.float32) if next(cuenta) % 2 else np.array([60, 140, 100, 30, 30, 30, 0, 0], dtype=np.float32)
        return base + rng.normal(0, 2, 8).astype(np.float32)

    monkeypatch.setattr(equipos, "rasgos_camiseta", rasgos)


def test_los_instantes_caen_en_el_centro_de_cada_trozo():
    assert instantes_muestra(100.0, 80.0, 4) == [110.0, 130.0, 150.0, 170.0]


def test_en_un_tramo_largo_salta_a_cada_muestra_sin_leer_el_video_entero(tmp_path, monkeypatch, dos_camisetas):
    info = video_numerado(tmp_path / "largo.mp4", 200)
    monkeypatch.setattr(equipos, "fotogramas", lambda *a, **k: pytest.fail("no debe descomprimir el tramo entero"))
    jugadores = JugadoresFalsos()
    config = Configuracion(video=info.ruta, salida=tmp_path, muestras_equipos=96)  # 8 fotogramas, uno cada 25 s

    muestras = ClasificadorEquipos(SimpleNamespace(jugadores=jugadores), config).ajustar(info)

    assert muestras == 24
    esperados = instantes_muestra(0.0, 200.0, 8)
    assert len(jugadores.vistos) == 8
    assert all(abs(visto - t) <= 1 for visto, t in zip(jugadores.vistos, esperados))


def test_en_un_tramo_corto_lee_seguido(tmp_path, monkeypatch, dos_camisetas):
    info = video_numerado(tmp_path / "corto.mp4", 200)
    monkeypatch.setattr(equipos, "fotogramas_en", lambda *a, **k: pytest.fail("en un tramo corto no compensa saltar"))
    jugadores = JugadoresFalsos()
    config = Configuracion(video=info.ruta, salida=tmp_path, muestras_equipos=96, segmento=Segmento(100.0, 160.0))

    ClasificadorEquipos(SimpleNamespace(jugadores=jugadores), config).ajustar(info)

    # Uno de cada int(60 / 8) = 7 fotogramas desde el principio del tramo.
    assert jugadores.vistos == list(range(100, 160, 7))


def test_ni_los_porteros_ni_los_arbitros_votan_equipo_por_la_camiseta(monkeypatch):
    # El portero viste distinto que los suyos: su equipo sale de la portería que defiende, no del color.
    monkeypatch.setattr(equipos, "rasgos_camiseta", lambda recorte: np.zeros(8, dtype=np.float32))
    puntuados = []

    def puntuar(rasgos):
        puntuados.append(len(rasgos))
        return np.array([0.8, -0.6])

    clasificador = ClasificadorEquipos(SimpleNamespace(), SimpleNamespace(), clasificador=SimpleNamespace(puntuar=puntuar))
    gente = sv.Detections(
        xyxy=np.array([[10, 10, 30, 60], [40, 10, 60, 60], [70, 10, 90, 60], [100, 10, 120, 60]], dtype=float),
        class_id=np.array([CLASE_JUGADOR, CLASE_PORTERO, CLASE_JUGADOR, CLASE_ARBITRO]),
        confidence=np.full(4, 0.9),
    )
    equipo, peso = clasificador.clasificar(np.zeros((80, 140, 3), dtype=np.uint8), gente)
    assert puntuados == [2]
    assert equipo.tolist() == [1, -1, 0, -1]
    assert peso.tolist() == pytest.approx([0.8, 0.0, 0.6, 0.0])
