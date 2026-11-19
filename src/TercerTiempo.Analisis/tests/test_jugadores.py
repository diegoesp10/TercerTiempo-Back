import numpy as np
import supervision as sv

from tt_analisis.config import CLASE_ARBITRO, CLASE_JUGADOR, CLASE_PORTERO
from tt_analisis.dataset_jugadores import convertir_etiquetas, preparar
from tt_analisis.detector_jugadores import clases_del_modelo
from tt_analisis.metricas import emparejar, emparejar_pares
from tt_analisis.rastreador import Rastreador


def test_las_etiquetas_pierden_el_balon_y_cambian_de_clase():
    texto = "0 0.5 0.5 0.01 0.01\n1 0.1 0.2 0.02 0.08\n2 0.3 0.4 0.02 0.08\n2 0.6 0.4 0.02 0.08\n3 0.7 0.7 0.02 0.08\nbasura\n"
    lineas, recuento = convertir_etiquetas(texto)
    assert lineas == ["0 0.1 0.2 0.02 0.08", "1 0.3 0.4 0.02 0.08", "1 0.6 0.4 0.02 0.08", "2 0.7 0.7 0.02 0.08"]
    assert recuento == {0: 1, 1: 2, 2: 1}


def test_preparar_junta_los_datasets_sin_repetir_instantes(tmp_path):
    for nombre, particion, fotogramas in (("a", "train", ["v-1", "v-2"]), ("b", "train", ["v-2", "v-3"]), ("b", "val", ["v-4"])):
        carpeta = tmp_path / nombre / "personas"
        (carpeta / "images" / particion).mkdir(parents=True, exist_ok=True)
        (carpeta / "labels" / particion).mkdir(parents=True, exist_ok=True)
        for f in fotogramas:
            (carpeta / "images" / particion / f"{f}.jpg").write_bytes(b"jpg")
            (carpeta / "labels" / particion / f"{f}.txt").write_text(f"2 0.5 0.5 0.1 0.2\n0 0.1 0.1 0.01 0.01\n# {nombre}\n", encoding="utf-8")
    salida = tmp_path / "jugadores"
    resumen = preparar([tmp_path / "a", tmp_path / "b"], salida, informar=lambda *_: None)
    assert resumen["fotogramas"] == {"train": 3, "val": 1}
    assert sorted(p.stem for p in (salida / "images" / "train").glob("*.jpg")) == ["v-1", "v-2", "v-3"]
    # El instante repetido se queda con las etiquetas del primer dataset, ya sin balón y con las clases nuevas.
    assert (salida / "labels" / "train" / "v-2.txt").read_text(encoding="utf-8") == "1 0.5 0.5 0.1 0.2\n"
    assert "names: [goalkeeper, player, referee]" in (salida / "data.yaml").read_text(encoding="utf-8")


def test_las_clases_del_modelo_vuelven_a_las_del_analisis():
    assert clases_del_modelo(["goalkeeper", "player", "referee"]).tolist() == [CLASE_PORTERO, CLASE_JUGADOR, CLASE_ARBITRO]
    assert clases_del_modelo({"1": "player", "0": "referee", "2": "ball"}).tolist() == [CLASE_ARBITRO, CLASE_JUGADOR, -1]


def test_emparejar_pares_por_iou():
    verdad = np.array([[0, 0, 10, 20], [100, 0, 110, 20]], dtype=float)
    predichas = np.array([[101, 1, 111, 21], [50, 50, 60, 70], [0, 0, 10, 19]], dtype=float)
    assert emparejar_pares(predichas, verdad, 0.5, por_centro=False) == [(0, 1), (2, 0)]
    assert emparejar(predichas, verdad, 0.5, por_centro=False) == (2, 1, 0)


def _personas(xs: list[float], conf: list[float], clases: list[int]) -> sv.Detections:
    xyxy = np.array([[x, 100, x + 30, 190] for x in xs], dtype=np.float32)
    return sv.Detections(xyxy=xyxy, confidence=np.array(conf, dtype=np.float32), class_id=np.array(clases))


def test_el_rastreador_mantiene_el_identificador_y_deja_fuera_lo_flojo_suelto():
    for nombre in ("botsort", "bytetrack"):
        rastreador = Rastreador(nombre, fps=5.0)
        imagen = np.zeros((360, 640, 3), np.uint8)
        ids = []
        for k in range(5):
            # Dos jugadores que avanzan y una detección floja suelta que no llega a nacer. ByteTrack confirma a cada uno
            # en su segunda detección; BoT-SORT da identificador ya en el primer fotograma.
            seguidas = rastreador.actualizar(_personas([20 + 3 * k, 300 + 3 * k, 500], [0.9, 0.8, 0.2], [CLASE_JUGADOR, CLASE_ARBITRO, CLASE_JUGADOR]), imagen, k * 0.2)
            if k == 0 and nombre == "bytetrack":
                assert len(seguidas) == 0
                continue
            assert len(seguidas) == 2 and (seguidas.tracker_id >= 0).all()
            por_x = np.argsort(seguidas.xyxy[:, 0])
            ids.append(tuple(seguidas.tracker_id[por_x]))
            assert seguidas.class_id[por_x].tolist() == [CLASE_JUGADOR, CLASE_ARBITRO]
        assert len(set(ids)) == 1, nombre
        # Un fotograma sin nadie no rompe nada.
        assert len(rastreador.actualizar(sv.Detections.empty(), imagen, 1.0)) == 0
