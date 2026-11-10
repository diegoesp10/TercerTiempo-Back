import numpy as np

from tt_analisis.config import Segmento, a_segundos
from tt_analisis.llm.revision import confirmar_eventos, leer_dorsales
from tt_analisis.tipos import Evento, formato_minuto


def test_segmentos_y_minutos():
    assert a_segundos("1:02:03") == 3723
    assert a_segundos("45:30") == 2730
    assert a_segundos("90") == 90
    s = Segmento.parse("10:00-15:30")
    assert (s.inicio, s.fin) == (600, 930)
    assert Segmento.parse(None).fin is None
    assert formato_minuto(3723) == "1:02:03" and formato_minuto(75) == "01:15"


class ModeloFalso:
    nombre = "falso"

    def __init__(self, respuesta):
        self.respuesta = respuesta
        self.llamadas = []

    def preguntar_json(self, imagenes, instrucciones, esquema):
        self.llamadas.append((len(imagenes), instrucciones))
        return self.respuesta

    def redactar(self, instrucciones):
        return "resumen"


def _imagen(_t):
    return np.zeros((64, 96, 3), dtype=np.uint8)


def test_la_revision_sube_o_baja_la_confianza():
    gol = Evento("gol", 100.0, equipo=0, confianza=0.45)
    tiro = Evento("tiro", 200.0, equipo=1, confianza=0.55)
    banda = Evento("saque_banda", 300.0, equipo=1, confianza=0.6)
    modelo = ModeloFalso({"veredicto": "si", "tipo_real": "gol", "explicacion": "El balón entra"})
    revisados = confirmar_eventos([gol, tiro, banda], _imagen, modelo)
    assert revisados == 2  # el saque de banda no se revisa
    assert gol.confianza == 0.75 and tiro.confianza == 0.85
    assert gol.veredicto_llm.startswith("si")
    assert modelo.llamadas[0][0] == 3  # tres fotogramas por evento

    modelo_no = ModeloFalso({"veredicto": "no", "tipo_real": "despeje", "explicacion": "Se va fuera"})
    confirmar_eventos([tiro], _imagen, modelo_no)
    assert tiro.confianza == 0.15

    modelo_error = ModeloFalso({"error": "refusal"})
    confirmar_eventos([gol], _imagen, modelo_error)
    assert gol.veredicto_llm.startswith("error")


def test_lectura_de_dorsales():
    modelo = ModeloFalso({"dorsal": 7, "confianza": 0.8, "color_camiseta": "azul"})
    dorsales = leer_dorsales({3: [np.zeros((40, 20, 3), dtype=np.uint8)], 4: []}, modelo)
    assert dorsales == {3: {"dorsal": 7, "confianza": 0.8, "color_camiseta": "azul"}}
