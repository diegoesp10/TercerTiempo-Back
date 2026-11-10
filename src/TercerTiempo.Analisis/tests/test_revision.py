import zipfile

import cv2
import numpy as np

from tt_analisis.revision import exportar, importar


def _dataset(raiz):
    for particion in ("train", "val"):
        (raiz / "images" / particion).mkdir(parents=True)
        (raiz / "labels" / particion).mkdir(parents=True)
    (raiz / "data.yaml").write_text(f"path: {raiz.as_posix()}\ntrain: images/train\nval: images/val\nnc: 1\nnames: [ball]\n", encoding="utf-8")
    imagen = np.zeros((48, 64, 3), dtype=np.uint8)
    for nombre, particion, etiqueta in (("a-0001", "val", "0 0.5 0.5 0.1 0.1\n"), ("a-0002", "val", ""), ("a-0003", "train", "0 0.2 0.2 0.1 0.1\n")):
        cv2.imwrite(str(raiz / "images" / particion / f"{nombre}.jpg"), imagen)
        (raiz / "labels" / particion / f"{nombre}.txt").write_text(etiqueta, encoding="utf-8")


def test_exportar_empaqueta_la_particion_en_formato_yolo_de_cvat(tmp_path):
    raiz = tmp_path / "balon"
    _dataset(raiz)
    destino = tmp_path / "revision.zip"
    assert exportar(raiz, destino, "val") == 2
    with zipfile.ZipFile(destino) as zf:
        nombres = zf.namelist()
        assert "obj.names" in nombres and "obj.data" in nombres and "train.txt" in nombres
        assert zf.read("obj.names").decode("utf-8") == "ball\n"
        assert zf.read("train.txt").decode("utf-8") == "obj_train_data/a-0001.jpg\nobj_train_data/a-0002.jpg\n"
        assert zf.read("obj_train_data/a-0001.txt").decode("utf-8") == "0 0.5 0.5 0.1 0.1\n"
        assert zf.read("obj_train_data/a-0002.txt").decode("utf-8") == ""
        assert "obj_train_data/a-0002.jpg" in nombres
    assert exportar(raiz, tmp_path / "solo.zip", "val", solo_con_etiqueta=True) == 1
    assert exportar(raiz, tmp_path / "uno.zip", "train", maximo=5) == 1


def test_importar_vuelve_a_escribir_solo_las_etiquetas_del_zip(tmp_path):
    raiz = tmp_path / "balon"
    _dataset(raiz)
    corregido = tmp_path / "corregido.zip"
    with zipfile.ZipFile(corregido, "w") as zf:
        zf.writestr("obj.names", "ball\n")
        zf.writestr("train.txt", "obj_train_data/a-0001.jpg\nobj_train_data/a-0002.jpg\nobj_train_data/otra.jpg\n")
        # La 1 se corrige, la 2 (sin etiqueta en el zip) se queda sin balón, «otra» no es del dataset.
        zf.writestr("obj_train_data/a-0001.txt", "0 0.600000 0.400000 0.100000 0.100000\n")
    resultado = importar(corregido, raiz)
    assert resultado == {"cambiadas": 1, "iguales": 1, "desconocidas": ["otra"]}
    assert (raiz / "labels" / "val" / "a-0001.txt").read_text(encoding="utf-8") == "0 0.600000 0.400000 0.100000 0.100000\n"
    assert (raiz / "labels" / "val" / "a-0002.txt").read_text(encoding="utf-8") == ""
    assert (raiz / "labels" / "train" / "a-0003.txt").read_text(encoding="utf-8") == "0 0.2 0.2 0.1 0.1\n"
    # Volver a importar lo mismo no cambia nada.
    assert importar(corregido, raiz)["cambiadas"] == 0
