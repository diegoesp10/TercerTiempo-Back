"""Línea de órdenes: `tt-analisis analizar partido.mp4 --salida salidas/partido`."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .config import Configuracion, Segmento, a_medidas, a_segundos

# Código de salida cuando se pidió --exigir-gpu y no hay GPU (lo distingue quien lanza el analizador).
CODIGO_SIN_GPU = 3


def construir_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="tt-analisis", description="Tercer Tiempo: análisis del vídeo de un partido para proponer el acta interna.")
    sub = parser.add_subparsers(dest="orden", required=True)

    a = sub.add_parser("analizar", help="Analiza un vídeo (o un tramo) y escribe los informes en la carpeta de salida.")
    a.add_argument("video", type=Path)
    a.add_argument("--salida", type=Path, default=None, help="Carpeta de salida (por defecto salidas/<nombre del vídeo>).")
    a.add_argument("--modelos", type=Path, default=Path("modelos"))
    a.add_argument("--sin-afinados", action="store_true", help="Usar el lector de dorsales publicado aunque haya uno afinado en modelos/afinados.")
    a.add_argument("--campo", default=None, help="Medidas del campo en metros, p. ej. 100x62 (ver `medir-campo`); por defecto 120x70.")
    a.add_argument("--device", default="cuda", help="cuda (por defecto), cuda:0 o cpu.")
    a.add_argument("--fps", type=float, default=5.0, help="Fotogramas por segundo que se analizan (5 por defecto).")
    a.add_argument("--vecinos-balon", type=float, default=0.04, help="Si no se ve el balón en un fotograma analizado, se busca en los de estos segundos a cada lado (0,04 por defecto; 0: solo en los analizados).")
    a.add_argument("--vecinos-enteros", type=int, default=1, help="Sin balón cerca, fotogramas vecinos en los que se busca en toda la imagen por cada tramo entre dos analizados (1 por defecto).")
    a.add_argument("--segmento", default=None, help="Tramo a analizar, p. ej. 10:00-15:00 o 0:45:00-1:35:00.")
    a.add_argument("--descanso", default=None, help="Minuto del vídeo en que empieza la segunda parte (p. ej. 52:30); si no, se detecta.")
    a.add_argument("--confianza", type=float, default=0.3, help="Puntuación mínima de una persona detectada (las flojas solo continúan a alguien ya seguido).")
    a.add_argument("--camara", choices=["movil", "fija"], default="movil", help="movil: alguien graba con paneo y zoom (por defecto); fija: trípode o cámara tipo Veo (una sola calibración).")
    a.add_argument("--seguidor", choices=["botsort", "bytetrack"], default=None, help="Seguidor de las personas (por defecto BoT-SORT con cámara móvil y ByteTrack con cámara fija).")
    a.add_argument("--llm", choices=["none", "claude", "ollama"], default="none", help="Modelo de lenguaje para confirmar eventos, leer dorsales y redactar.")
    a.add_argument("--modelo-llm", default=None, help="Modelo concreto (claude-opus-5-5, gemma4:12b, qwen3-vl:8b…).")
    a.add_argument("--sin-clips", action="store_true")
    a.add_argument("--video-anotado", action="store_true", help="Escribe anotado.mp4 con cajas, tracklets, balón y radar (lento).")
    a.add_argument("--max-fotogramas", type=int, default=None, help="Para probar: solo los primeros N fotogramas muestreados.")
    a.add_argument("--equipos", default=None, help="Nombres de los equipos separados por coma, en el orden que salgan (p. ej. 'Vallobín,Pumarín').")
    a.add_argument("--muestras-equipos", type=int, default=600)
    a.add_argument("--progreso", action="store_true", help="Escribe el avance como líneas «##progreso N» (0-100) en la salida estándar, sin barras: para quien lo lanza como proceso.")
    a.add_argument("--exigir-gpu", action="store_true", help="Sin GPU CUDA, termina con el código de salida 3 en vez de analizar en CPU.")
    a.add_argument("--semilla", type=int, default=None, help="Para medir la estabilidad: fija los generadores y muestrea otros fotogramas del mismo tramo.")
    a.add_argument("--plantillas", type=Path, default=None, help="JSON con las plantillas del acta oficial (dorsales y minutos de cada uno): el dorsal leído se cruza con ellas.")
    a.add_argument("--saque", default="0", help="Minuto del vídeo en que empieza el partido (p. ej. 1:30), para pasar los minutos del acta al vídeo.")
    a.add_argument("--sin-dorsales", action="store_true", help="No leer los dorsales: cada tracklet queda suelto, como antes.")
    a.add_argument("--sin-apariencia", action="store_true", help="No reconocer por el aspecto a quien no se le lee el dorsal.")
    a.add_argument("--guardar-dorsales", type=Path, default=None, help="Carpeta donde dejar las zonas del dorsal y sus etiquetas (los jugadores identificados) para `entrenar-dorsales`.")
    a.add_argument("--sin-repeticion", action="store_true", help="No escribir repeticion/ (lo que se vio en cada fotograma, para `visor`).")
    a.add_argument("--sin-solapar", action="store_true", help="Sin leer el vídeo ni usar la GPU por adelantado: cada paso espera al anterior (mismo resultado, más lento; para medir).")
    a.add_argument("--sin-grafos", action="store_true", help="Sin grafos CUDA: cada detector lanza su paso operación a operación (mismo resultado, más lento; para medir). Si falla la grabación de un grafo, el análisis se repite solo así, en un proceso limpio.")
    a.add_argument("--guardar-pasada", action="store_true", help="Guarda al acabar la pasada lo que entra en la capa de jugadas (pasada.pkl.gz), para repetirla sin la GPU con `rehacer-jugadas`.")

    rj = sub.add_parser(
        "rehacer-jugadas",
        help="Repite sin la GPU lo que va tras la pasada (balón, porteros, quién es quién, jugadas y salidas) con el código de ahora, desde un análisis hecho con --guardar-pasada, y lo mide con las etiquetas del visor si las hay.",
    )
    rj.add_argument("carpeta", type=Path, help="Carpeta del análisis (con pasada.pkl.gz).")
    rj.add_argument("--salida", type=Path, default=None, help="Dónde dejar lo rehecho (por defecto salidas/<carpeta>-rehecho). Si ahí hay algo rehecho de antes, se sustituye.")
    rj.add_argument("--revision", type=Path, default=None, help="JSON de etiquetas exportado del visor (por defecto, las del visor para ese vídeo).")
    rj.add_argument("--margen", type=float, default=None, help="Al medir, diferencia máxima en segundos para todos los tipos (por defecto, 2 en las jugadas y 1 en las acciones).")
    rj.add_argument("--sin-medir", action="store_true", help="No medir con las etiquetas del visor.")

    v = sub.add_parser("visor", help="Abre el visor en una ventana propia: el vídeo con lo que vio y sacó el analizador encima, fotograma a fotograma.")
    v.add_argument("carpetas", type=Path, nargs="*", help="Carpetas de análisis (con analisis.json) o carpetas donde buscarlos; por defecto, salidas/.")
    v.add_argument("--video", type=Path, default=None, help="El vídeo, si no está donde dice analisis.json (solo con una carpeta de análisis).")
    v.add_argument("--puerto", type=int, default=0, help="Puerto local (por defecto, uno libre).")
    v.add_argument("--sin-ventana", action="store_true", help="No abrir la ventana: solo escribe la dirección para abrirla en el navegador.")

    ed = sub.add_parser("entrenar-dorsales", help="Afina el lector de dorsales con las zonas guardadas (--guardar-dorsales) y deja los pesos en modelos/afinados/dorsales.pt.")
    ed.add_argument("carpetas", type=Path, nargs="+", help="Carpetas de --guardar-dorsales (de uno o varios partidos).")
    ed.add_argument("--modelos", type=Path, default=Path("modelos"))
    ed.add_argument("--epocas", type=int, default=4)
    ed.add_argument("--lote", type=int, default=128)
    ed.add_argument("--lr", type=float, default=3e-5)
    ed.add_argument("--maximo-por-dorsal", type=int, default=2500, help="Como mucho tantas zonas de cada dorsal (para que no manden los que más se ven).")
    ed.add_argument("--minimo-por-dorsal", type=int, default=300, help="Al menos tantas, repitiendo las de los dorsales que casi nunca se ven de espaldas (el del portero).")
    ed.add_argument("--device", default="cuda")

    c = sub.add_parser("comparar", help="Compara varias pasadas (carpetas con analisis.json): balón, calibración, posesión y su estabilidad.")
    c.add_argument("salidas", type=Path, nargs="+")

    x = sub.add_parser("exportar-revision", help="Empaqueta imágenes y etiquetas de un dataset en un zip YOLO 1.1 para corregirlas a mano en CVAT (o Label Studio).")
    x.add_argument("que", choices=["balon", "jugadores", "campo"])
    x.add_argument("--dataset", type=Path, required=True, help="Carpeta que dejó `etiquetar`.")
    x.add_argument("--salida", type=Path, default=None, help="Zip de salida (por defecto <dataset>/revision-<que>-<particion>.zip).")
    x.add_argument("--particion", choices=["train", "val"], default="val")
    x.add_argument("--max", type=int, default=None, help="Como mucho estas imágenes (las primeras en orden de tiempo).")
    x.add_argument("--solo-con-etiqueta", action="store_true", help="Solo las imágenes que ya tienen alguna etiqueta.")

    r = sub.add_parser("importar-revision", help="Vuelve a escribir en el dataset las etiquetas corregidas (zip YOLO 1.1 exportado de CVAT).")
    r.add_argument("que", choices=["balon", "jugadores", "campo"])
    r.add_argument("zip", type=Path)
    r.add_argument("--dataset", type=Path, required=True)

    d = sub.add_parser("descargar-modelos", help="Descarga el lector de dorsales (y, si se pide, dos vídeos de muestra); los tres detectores son RF-DETR propios, en modelos/rfdetr.")
    d.add_argument("--modelos", type=Path, default=Path("modelos"))
    d.add_argument("--con-videos", action="store_true")

    i = sub.add_parser("info", help="Datos del vídeo y de la GPU.")
    i.add_argument("video", type=Path, nargs="?")

    e = sub.add_parser("etiquetar", help="Etiqueta automáticamente un partido propio (balón, personas y campo) para afinar los detectores.")
    e.add_argument("video", type=Path)
    e.add_argument("--salida", type=Path, default=None, help="Carpeta del dataset (por defecto datasets/<nombre del vídeo>).")
    e.add_argument("--modelos", type=Path, default=Path("modelos"))
    e.add_argument("--device", default="cuda")
    e.add_argument("--cada", type=float, default=2.0, help="Segundos entre fotogramas etiquetados (2 por defecto).")
    e.add_argument("--segmento", default=None, help="Tramo del vídeo, p. ej. 0:00-45:00.")
    e.add_argument("--campo", default=None, help="Medidas del campo en metros, p. ej. 100x62; si no, se miden desde el vídeo.")
    e.add_argument("--juez", choices=["none", "claude", "ollama"], default="none", help="Quién confirma cada candidato a balón mirando un recorte: claude (API), ollama (local) o nadie.")
    e.add_argument("--modelo-juez", default=None, help="Modelo del juez (claude-opus-5-5, gemma4:12b…).")
    e.add_argument("--vista-previa-cada", type=int, default=10, help="Una imagen anotada en revision/ de cada N muestras (0 = ninguna).")
    e.add_argument("--max-muestras", type=int, default=None, help="Para probar: solo las primeras N muestras.")

    mc = sub.add_parser("medir-campo", help="Mide el largo y el ancho del campo con los puntos que detecta en el vídeo.")
    mc.add_argument("video", type=Path)
    mc.add_argument("--modelos", type=Path, default=Path("modelos"))
    mc.add_argument("--device", default="cuda")
    mc.add_argument("--segmento", default=None)
    mc.add_argument("--fotogramas", type=int, default=300)

    pc = sub.add_parser("preparar-campo", help="Etiqueta los 32 puntos del campo en fotogramas de uno o varios vídeos con el detector del campo actual como maestro (para la siguiente ronda).")
    pc.add_argument("videos", type=Path, nargs="+")
    pc.add_argument("--salida", type=Path, default=Path("datasets") / "campo")
    pc.add_argument("--modelos", type=Path, default=Path("modelos"))
    pc.add_argument("--device", default="cuda")
    pc.add_argument("--cada", type=float, default=1.0, help="Segundos entre fotogramas (1 por defecto).")
    pc.add_argument("--cada-cortos", type=float, default=0.25, help="Segundos entre fotogramas en los vídeos de menos de 10 min.")

    ec = sub.add_parser("entrenar-campo", help="Entrena el detector de los puntos del campo (RF-DETR Keypoint) con el dataset de `preparar-campo` y deja modelos/rfdetr/campo.pth.")
    ec.add_argument("--dataset", type=Path, default=Path("datasets") / "campo")
    ec.add_argument("--modelos", type=Path, default=Path("modelos"))
    ec.add_argument("--salida", type=Path, default=None, help="Carpeta del entrenamiento (por defecto entrenamientos/rfdetr-campo-<fecha>).")
    ec.add_argument("--resolucion", type=int, default=768, help="Lado del cuadrado al que se reescala el fotograma (múltiplo de 24).")
    ec.add_argument("--epocas", type=int, default=40)
    ec.add_argument("--lote", type=int, default=4)
    ec.add_argument("--acumular", type=int, default=4, help="Pasos de acumulación de gradiente (lote efectivo = lote × acumular).")
    ec.add_argument("--lr", type=float, default=1e-4)
    ec.add_argument("--paciencia", type=int, default=8)
    ec.add_argument("--device", default="cuda")

    vc = sub.add_parser("evaluar-campo", help="Mide el detector del campo (fotogramas calibrables, puntos y error de la homografía) sobre la validación de uno o varios datasets.")
    vc.add_argument("datasets", type=Path, nargs="+", help="Carpetas de `etiquetar` (se usa su campo/) o la de `preparar-campo`.")
    vc.add_argument("--modelos", type=Path, default=Path("modelos"))
    vc.add_argument("--device", default="cuda")

    pb = sub.add_parser("preparar-balon", help="Saca las teselas del balón (RF-DETR) de uno o varios datasets que dejó `etiquetar`.")
    pb.add_argument("datasets", type=Path, nargs="+", help="Carpetas de `etiquetar` (del mismo partido o de varios).")
    pb.add_argument("--salida", type=Path, default=Path("datasets") / "balon-teselas")
    pb.add_argument("--por-balon", type=int, default=2, help="Teselas con el balón por cada fotograma en que está confirmado.")
    pb.add_argument("--al-azar", type=int, default=2, help="Teselas sin balón al azar por fotograma.")
    pb.add_argument("--semilla", type=int, default=0)

    eb = sub.add_parser("entrenar-balon", help="Entrena el detector del balón (RF-DETR Medium) con las teselas de `preparar-balon` y deja modelos/rfdetr/balon.pth.")
    eb.add_argument("--dataset", type=Path, default=Path("datasets") / "balon-teselas")
    eb.add_argument("--modelos", type=Path, default=Path("modelos"))
    eb.add_argument("--salida", type=Path, default=None, help="Carpeta del entrenamiento (por defecto entrenamientos/rfdetr-balon-<fecha>).")
    eb.add_argument("--epocas", type=int, default=40)
    eb.add_argument("--lote", type=int, default=8)
    eb.add_argument("--acumular", type=int, default=2, help="Pasos de acumulación de gradiente (lote efectivo = lote × acumular).")
    eb.add_argument("--lr", type=float, default=1e-4)
    eb.add_argument("--paciencia", type=int, default=8)
    eb.add_argument("--device", default="cuda")

    vb = sub.add_parser("evaluar-balon", help="Mide el detector del balón sobre los fotogramas enteros de validación de uno o varios datasets.")
    vb.add_argument("datasets", type=Path, nargs="+", help="Carpetas de `etiquetar`; se usa su balon/images/val.")
    vb.add_argument("--modelos", type=Path, default=Path("modelos"))
    vb.add_argument("--device", default="cuda")

    pj = sub.add_parser("preparar-jugadores", help="Junta las personas (porteros, jugadores, árbitros) de uno o varios datasets que dejó `etiquetar` para RF-DETR.")
    pj.add_argument("datasets", type=Path, nargs="+", help="Carpetas de `etiquetar` (del mismo partido o de varios).")
    pj.add_argument("--salida", type=Path, default=Path("datasets") / "jugadores")

    ej = sub.add_parser("entrenar-jugadores", help="Entrena el detector de personas (RF-DETR Medium) con el dataset de `preparar-jugadores` y deja modelos/rfdetr/jugadores.pth.")
    ej.add_argument("--dataset", type=Path, default=Path("datasets") / "jugadores")
    ej.add_argument("--modelos", type=Path, default=Path("modelos"))
    ej.add_argument("--salida", type=Path, default=None, help="Carpeta del entrenamiento (por defecto entrenamientos/rfdetr-jugadores-<fecha>).")
    ej.add_argument("--resolucion", type=int, default=1280, help="Lado del cuadrado al que se reescala el fotograma (múltiplo de 32).")
    ej.add_argument("--epocas", type=int, default=30)
    ej.add_argument("--lote", type=int, default=2)
    ej.add_argument("--acumular", type=int, default=8, help="Pasos de acumulación de gradiente (lote efectivo = lote × acumular).")
    ej.add_argument("--lr", type=float, default=1e-4)
    ej.add_argument("--paciencia", type=int, default=6)
    ej.add_argument("--device", default="cuda")

    vj = sub.add_parser("evaluar-jugadores", help="Compara el detector de personas con las etiquetas de validación de uno o varios datasets.")
    vj.add_argument("datasets", type=Path, nargs="+", help="Carpetas de `etiquetar` (se usa su personas/images/val) o la de `preparar-jugadores`.")
    vj.add_argument("--modelos", type=Path, default=Path("modelos"))
    vj.add_argument("--device", default="cuda")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = construir_parser().parse_args(argv)
    if args.orden == "etiquetar":
        from .etiquetado import ConfiguracionEtiquetado, etiquetar

        etiquetar(ConfiguracionEtiquetado(
            video=args.video, salida=args.salida or Path("datasets") / args.video.stem, modelos=args.modelos, device=args.device, cada_s=args.cada,
            segmento=Segmento.parse(args.segmento), campo=a_medidas(args.campo), juez=args.juez, modelo_juez=args.modelo_juez,
            vista_previa_cada=args.vista_previa_cada, max_muestras=args.max_muestras,
        ))
        return 0
    if args.orden == "medir-campo":
        from .etiquetado import Maestro, medir_campo_video
        from .modelos import cargar_modelos
        from .video import info_video

        info = info_video(args.video)
        puntos = Path("salidas") / f"{args.video.stem}-campo-puntos.npz"
        medida = medir_campo_video(Maestro(cargar_modelos(args.modelos, args.device)), info, Segmento.parse(args.segmento), fotogramas=args.fotogramas, guardar_puntos=puntos)
        print(f"Puntos detectados guardados en {puntos}. Para analizar: --campo {medida['largo_m']:g}x{medida['ancho_m']:g}")
        return 0
    if args.orden == "preparar-campo":
        from .dataset_campo import preparar as preparar_campo
        from .detector_campo import DetectorCampo
        from .modelos import resolver_device

        maestro = DetectorCampo.cargar(args.modelos, resolver_device(args.device))
        preparar_campo(args.videos, args.salida, maestro.detectar, cada_s=args.cada, cada_s_cortos=args.cada_cortos)
        return 0
    if args.orden == "entrenar-campo":
        from datetime import datetime

        from .detector_campo import entrenar as entrenar_campo

        salida = args.salida or Path("entrenamientos") / f"rfdetr-campo-{datetime.now():%Y%m%d-%H%M}"
        entrenar_campo(args.dataset, args.modelos, salida, resolucion=args.resolucion, epocas=args.epocas, lote=args.lote, acumular=args.acumular, lr=args.lr, paciencia=args.paciencia, device=args.device)
        return 0
    if args.orden == "evaluar-campo":
        import json

        from .detector_campo import DetectorCampo
        from .detector_campo import evaluar as evaluar_campo

        detector = DetectorCampo.cargar(args.modelos, args.device)
        carpetas = [d if (d / "data.yaml").exists() else d / "campo" for d in args.datasets]
        resumen = evaluar_campo(detector.puntos, carpetas, f"RF-DETR ({detector.motor})")
        destino = args.modelos / "rfdetr" / f"evaluacion-campo-{detector.motor}.json"
        destino.write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Resultado en {destino}")
        return 0
    if args.orden == "preparar-balon":
        from .dataset_balon import preparar

        preparar(args.datasets, args.salida, por_balon=args.por_balon, al_azar=args.al_azar, semilla=args.semilla)
        return 0
    if args.orden == "entrenar-balon":
        from datetime import datetime

        from .detector_balon import entrenar as entrenar_balon

        salida = args.salida or Path("entrenamientos") / f"rfdetr-balon-{datetime.now():%Y%m%d-%H%M}"
        entrenar_balon(args.dataset, args.modelos, salida, epocas=args.epocas, lote=args.lote, acumular=args.acumular, lr=args.lr, paciencia=args.paciencia, device=args.device)
        return 0
    if args.orden == "evaluar-balon":
        import json

        from .detector_balon import DetectorBalon
        from .detector_balon import evaluar as evaluar_balon

        detector = DetectorBalon.cargar(args.modelos, args.device)
        resumen = evaluar_balon(detector, [d / "balon" for d in args.datasets])
        destino = args.modelos / "rfdetr" / f"evaluacion-balon-{detector.motor}.json"
        destino.write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Resultado en {destino}")
        return 0
    if args.orden == "preparar-jugadores":
        from .dataset_jugadores import preparar as preparar_jugadores

        preparar_jugadores(args.datasets, args.salida)
        return 0
    if args.orden == "entrenar-jugadores":
        from datetime import datetime

        from .detector_jugadores import entrenar as entrenar_jugadores

        salida = args.salida or Path("entrenamientos") / f"rfdetr-jugadores-{datetime.now():%Y%m%d-%H%M}"
        entrenar_jugadores(args.dataset, args.modelos, salida, resolucion=args.resolucion, epocas=args.epocas, lote=args.lote, acumular=args.acumular, lr=args.lr, paciencia=args.paciencia, device=args.device)
        return 0
    if args.orden == "evaluar-jugadores":
        import json

        from .detector_jugadores import DetectorJugadores
        from .detector_jugadores import evaluar as evaluar_jugadores

        detector = DetectorJugadores.cargar(args.modelos, args.device)
        carpetas = [d if (d / "data.yaml").exists() and not (d / "personas").exists() else d / "personas" for d in args.datasets]
        resumen = evaluar_jugadores(detector, carpetas)
        destino = args.modelos / "rfdetr" / f"evaluacion-jugadores-{detector.motor}.json"
        destino.write_text(json.dumps(resumen, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Resultado en {destino}")
        return 0
    if args.orden in ("exportar-revision", "importar-revision"):
        from .revision import CARPETAS, exportar, importar

        carpeta = args.dataset / CARPETAS[args.que]
        if args.orden == "exportar-revision":
            destino = args.salida or args.dataset / f"revision-{args.que}-{args.particion}.zip"
            n = exportar(carpeta, destino, args.particion, args.max, args.solo_con_etiqueta)
            print(f"{n} imágenes con sus etiquetas en {destino}. En CVAT: crear la tarea con las imágenes del zip y, en «Upload annotations», el mismo zip con el formato YOLO 1.1; al terminar, exportar en YOLO 1.1 e `importar-revision`.")
        else:
            resultado = importar(args.zip, carpeta)
            print(f"Etiquetas cambiadas: {resultado['cambiadas']}; iguales: {resultado['iguales']}" + (f"; imágenes que no están en el dataset: {', '.join(resultado['desconocidas'][:10])}" if resultado["desconocidas"] else "") + ".")
        return 0
    if args.orden == "comparar":
        import json

        from .comparacion import comparar

        pasadas = [(ruta, json.loads((ruta / "analisis.json").read_text(encoding="utf-8"))) for ruta in args.salidas]
        print(comparar(pasadas))
        return 0
    if args.orden == "entrenar-dorsales":
        from .entrenar_dorsales import entrenar_dorsales

        return entrenar_dorsales(args.carpetas, args.modelos, epocas=args.epocas, lote=args.lote, lr=args.lr, maximo_por_dorsal=args.maximo_por_dorsal, device=args.device,
                                 minimo_por_dorsal=args.minimo_por_dorsal)

    if args.orden == "descargar-modelos":
        from .modelos import descargar_modelos

        for ruta in descargar_modelos(args.modelos, con_videos=args.con_videos):
            print(ruta)
        return 0
    if args.orden == "rehacer-jugadas":
        from .analizador import rehacer_jugadas
        from .rehacer import leer, medir, preparar_salida, salida_por_defecto

        salida = args.salida or salida_por_defecto(args.carpeta)
        # Solo los fallos de lo guardado y de la salida, con su mensaje; uno de la capa de jugadas sale entero, con dónde.
        try:
            estado = leer(args.carpeta)
            preparar_salida(args.carpeta, salida)
        except (OSError, ValueError) as error:
            print(f"No se puede rehacer: {error}", file=sys.stderr)
            return 2
        rehacer_jugadas(args.carpeta, salida, estado=estado)
        if not args.sin_medir:
            try:
                print(medir(salida, args.revision, args.margen))
            except (OSError, ValueError, KeyError, TypeError) as error:
                print(f"Sin medir: {error}", file=sys.stderr)
        return 0

    if args.orden == "visor":
        from .visor import abrir_visor

        return abrir_visor(args.carpetas or [Path("salidas")], video=args.video, puerto=args.puerto, ventana=not args.sin_ventana)

    if args.orden == "info":
        from .modelos import describir_gpu
        from .video import info_video

        print(f"GPU: {describir_gpu()}")
        if args.video:
            info = info_video(args.video)
            print(f"{info.ruta}: {info.ancho}×{info.alto}, {info.fps:.2f} fps, {info.fotogramas} fotogramas, {info.duracion_s / 60:.1f} min")
        return 0

    nombres = tuple(n.strip() for n in args.equipos.split(",")) if args.equipos else ("Equipo A", "Equipo B")
    if len(nombres) != 2:
        print("--equipos necesita dos nombres separados por coma.", file=sys.stderr)
        return 2
    config = Configuracion(
        video=args.video,
        salida=args.salida or Path("salidas") / args.video.stem,
        modelos=args.modelos,
        afinados=not args.sin_afinados,
        campo=a_medidas(args.campo),
        device=args.device,
        fps_muestreo=args.fps,
        vecinos_balon_s=args.vecinos_balon,
        vecinos_enteros=args.vecinos_enteros,
        segmento=Segmento.parse(args.segmento),
        descanso=a_segundos(args.descanso) if args.descanso else None,
        confianza=args.confianza,
        camara=args.camara,
        seguidor=args.seguidor,
        llm=args.llm,
        modelo_llm=args.modelo_llm,
        clips=not args.sin_clips,
        video_anotado=args.video_anotado,
        max_fotogramas=args.max_fotogramas,
        nombres_equipos=nombres,  # type: ignore[arg-type]
        muestras_equipos=args.muestras_equipos,
        progreso=args.progreso,
        exigir_gpu=args.exigir_gpu,
        semilla=args.semilla,
        dorsales=not args.sin_dorsales,
        apariencia=not args.sin_apariencia,
        plantillas=args.plantillas,
        saque=a_segundos(args.saque) if args.saque else 0.0,
        guardar_dorsales=args.guardar_dorsales,
        repeticion=not args.sin_repeticion,
        solapar=not args.sin_solapar,
        grafos=not args.sin_grafos,
        guardar_pasada=args.guardar_pasada,
    )
    from .analizador import analizar
    from .modelos import SinGpu
    from .repetir import CapturaFallida, repetir_sin_grafos

    try:
        analizar(config)
    except SinGpu as sin_gpu:
        print(sin_gpu, file=sys.stderr)
        return CODIGO_SIN_GPU
    except CapturaFallida as fallo:
        motivo, ordenes = fallo.motivo, fallo.ordenes
    else:
        return 0
    # Este proceso puede haber quedado tocado para la GPU: el análisis se repite en uno limpio (`repetir`). Fuera del
    # `except`, para que el error ya no retenga lo que tenía en la GPU.
    return repetir_sin_grafos(sys.argv[1:] if argv is None else list(argv), motivo, ordenes)


if __name__ == "__main__":
    sys.exit(main())
