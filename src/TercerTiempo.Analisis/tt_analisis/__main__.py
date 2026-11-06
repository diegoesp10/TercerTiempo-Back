"""Línea de órdenes: `tt-analisis analizar partido.mp4 --salida salidas/partido`."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .config import Configuracion, Segmento, a_medidas, a_segundos


def construir_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="tt-analisis", description="Tercer Tiempo: análisis del vídeo de un partido para proponer el acta interna.")
    sub = parser.add_subparsers(dest="orden", required=True)

    a = sub.add_parser("analizar", help="Analiza un vídeo (o un tramo) y escribe los informes en la carpeta de salida.")
    a.add_argument("video", type=Path)
    a.add_argument("--salida", type=Path, default=None, help="Carpeta de salida (por defecto salidas/<nombre del vídeo>).")
    a.add_argument("--modelos", type=Path, default=Path("modelos"))
    a.add_argument("--sin-afinados", action="store_true", help="Usar los pesos públicos aunque haya pesos afinados en modelos/afinados.")
    a.add_argument("--campo", default=None, help="Medidas del campo en metros, p. ej. 100x62 (ver `medir-campo`); por defecto 105x68.")
    a.add_argument("--device", default="cuda", help="cuda (por defecto), cuda:0 o cpu.")
    a.add_argument("--fps", type=float, default=5.0, help="Fotogramas por segundo que se analizan (5 por defecto).")
    a.add_argument("--segmento", default=None, help="Tramo a analizar, p. ej. 10:00-15:00 o 0:45:00-1:35:00.")
    a.add_argument("--descanso", default=None, help="Minuto del vídeo en que empieza la segunda parte (p. ej. 52:30); si no, se detecta.")
    a.add_argument("--imgsz", type=int, default=1280)
    a.add_argument("--imgsz-campo", type=int, default=640, help="Tamaño de inferencia del detector de puntos del campo (640, el de su entrenamiento).")
    a.add_argument("--confianza", type=float, default=0.3)
    a.add_argument("--camara", choices=["movil", "fija"], default="movil", help="movil: alguien graba con paneo y zoom (por defecto); fija: trípode o cámara tipo Veo (una sola calibración).")
    a.add_argument("--tracker", default=None, help="Seguidor de Ultralytics: bytetrack.yaml, botsort.yaml o un yaml propio (por defecto el del paquete según la cámara).")
    a.add_argument("--llm", choices=["none", "claude", "ollama"], default="none", help="Modelo de lenguaje para confirmar eventos, leer dorsales y redactar.")
    a.add_argument("--modelo-llm", default=None, help="Modelo concreto (claude-opus-5-5, gemma4:12b, qwen3-vl:8b…).")
    a.add_argument("--sin-clips", action="store_true")
    a.add_argument("--sin-balon-fino", action="store_true", help="No buscar el balón por teselas cuando se pierde (más rápido).")
    a.add_argument("--video-anotado", action="store_true", help="Escribe anotado.mp4 con cajas, tracklets, balón y radar (lento).")
    a.add_argument("--max-fotogramas", type=int, default=None, help="Para probar: solo los primeros N fotogramas muestreados.")
    a.add_argument("--equipos", default=None, help="Nombres de los equipos separados por coma, en el orden que salgan (p. ej. 'Vallobín,Pumarín').")
    a.add_argument("--muestras-equipos", type=int, default=600)

    d = sub.add_parser("descargar-modelos", help="Descarga los tres detectores (y, si se pide, dos vídeos de muestra).")
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
    e.add_argument("--imgsz", type=int, default=1280)
    e.add_argument("--campo", default=None, help="Medidas del campo en metros, p. ej. 100x62; si no, se miden desde el vídeo.")
    e.add_argument("--afinados", action="store_true", help="Etiquetar con los pesos ya afinados como maestro (segunda ronda de autoentrenamiento).")
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
    mc.add_argument("--imgsz", type=int, default=1280)

    t = sub.add_parser("entrenar", help="Afina un detector con el dataset etiquetado y deja los pesos en modelos/afinados.")
    t.add_argument("que", choices=["balon", "jugadores", "campo", "todos"])
    t.add_argument("--dataset", type=Path, required=True, help="Carpeta que dejó `etiquetar`.")
    t.add_argument("--modelos", type=Path, default=Path("modelos"))
    t.add_argument("--salida", type=Path, default=Path("entrenamientos"), help="Carpeta de los entrenamientos de Ultralytics.")
    t.add_argument("--device", default="cuda")
    t.add_argument("--epocas", type=int, default=None, help="Por defecto 20 (balón y jugadores) o 40 (campo).")
    t.add_argument("--batch", type=int, default=None, help="Tamaño del lote (por defecto 4 a 1280 px y 12 a 640 px, lo que cabe en 16 GB; -1 = que lo calcule Ultralytics).")
    t.add_argument("--imgsz", type=int, default=None)
    t.add_argument("--congelar", type=int, default=10, help="Capas congeladas desde el principio (10 = el tronco de YOLOv8; 0 = ninguna).")
    t.add_argument("--paciencia", type=int, default=10)
    t.add_argument("--mezclar", type=Path, nargs="*", default=None, help="Otras carpetas de imágenes YOLO con las que entrenar a la vez (p. ej. el dataset público).")

    v = sub.add_parser("evaluar", help="Compara los pesos públicos con los afinados sobre la validación del dataset.")
    v.add_argument("que", choices=["balon", "jugadores", "campo", "todos"])
    v.add_argument("--dataset", type=Path, required=True)
    v.add_argument("--modelos", type=Path, default=Path("modelos"))
    v.add_argument("--salida", type=Path, default=None, help="Por defecto <dataset>/evaluacion.")
    v.add_argument("--device", default="cuda")
    v.add_argument("--imgsz", type=int, default=None)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = construir_parser().parse_args(argv)
    if args.orden == "etiquetar":
        from .etiquetado import ConfiguracionEtiquetado, etiquetar

        etiquetar(ConfiguracionEtiquetado(
            video=args.video, salida=args.salida or Path("datasets") / args.video.stem, modelos=args.modelos, device=args.device, cada_s=args.cada,
            segmento=Segmento.parse(args.segmento), imgsz=args.imgsz, campo=a_medidas(args.campo), afinados=args.afinados, juez=args.juez, modelo_juez=args.modelo_juez,
            vista_previa_cada=args.vista_previa_cada, max_muestras=args.max_muestras,
        ))
        return 0
    if args.orden == "medir-campo":
        from .etiquetado import Maestro, medir_campo_video
        from .modelos import cargar_modelos
        from .video import info_video

        info = info_video(args.video)
        puntos = Path("salidas") / f"{args.video.stem}-campo-puntos.npz"
        medida = medir_campo_video(Maestro(cargar_modelos(args.modelos, args.device, afinados=False), args.imgsz), info, Segmento.parse(args.segmento), fotogramas=args.fotogramas, guardar_puntos=puntos)
        print(f"Puntos detectados guardados en {puntos}. Para analizar: --campo {medida['largo_m']:g}x{medida['ancho_m']:g}")
        return 0
    if args.orden in ("entrenar", "evaluar"):
        from .entrenamiento import TAREAS, entrenar, evaluar

        tareas = list(TAREAS) if args.que == "todos" else [args.que]
        for que in tareas:
            if args.orden == "entrenar":
                entrenar(que, args.dataset, args.modelos, args.salida, epocas=args.epocas, batch=args.batch, imgsz=args.imgsz, device=args.device, congelar=args.congelar, paciencia=args.paciencia, mezclar=args.mezclar)
            else:
                evaluar(que, args.dataset, args.modelos, args.salida or args.dataset / "evaluacion", device=args.device, imgsz=args.imgsz)
        return 0
    if args.orden == "descargar-modelos":
        from .modelos import descargar_modelos

        for ruta in descargar_modelos(args.modelos, con_videos=args.con_videos):
            print(ruta)
        return 0
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
        segmento=Segmento.parse(args.segmento),
        descanso=a_segundos(args.descanso) if args.descanso else None,
        imgsz=args.imgsz,
        imgsz_campo=args.imgsz_campo,
        confianza=args.confianza,
        balon_fino=not args.sin_balon_fino,
        camara=args.camara,
        tracker=args.tracker,
        llm=args.llm,
        modelo_llm=args.modelo_llm,
        clips=not args.sin_clips,
        video_anotado=args.video_anotado,
        max_fotogramas=args.max_fotogramas,
        nombres_equipos=nombres,  # type: ignore[arg-type]
        muestras_equipos=args.muestras_equipos,
    )
    from .analizador import analizar

    analizar(config)
    return 0


if __name__ == "__main__":
    sys.exit(main())
