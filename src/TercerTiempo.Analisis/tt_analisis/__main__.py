"""Línea de órdenes: `tt-analisis analizar partido.mp4 --salida salidas/partido`."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .config import Configuracion, Segmento, a_segundos


def construir_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="tt-analisis", description="Tercer Tiempo: análisis del vídeo de un partido para proponer el acta interna.")
    sub = parser.add_subparsers(dest="orden", required=True)

    a = sub.add_parser("analizar", help="Analiza un vídeo (o un tramo) y escribe los informes en la carpeta de salida.")
    a.add_argument("video", type=Path)
    a.add_argument("--salida", type=Path, default=None, help="Carpeta de salida (por defecto salidas/<nombre del vídeo>).")
    a.add_argument("--modelos", type=Path, default=Path("modelos"))
    a.add_argument("--device", default="cuda", help="cuda (por defecto), cuda:0 o cpu.")
    a.add_argument("--fps", type=float, default=5.0, help="Fotogramas por segundo que se analizan (5 por defecto).")
    a.add_argument("--segmento", default=None, help="Tramo a analizar, p. ej. 10:00-15:00 o 0:45:00-1:35:00.")
    a.add_argument("--descanso", default=None, help="Minuto del vídeo en que empieza la segunda parte (p. ej. 52:30); si no, se detecta.")
    a.add_argument("--imgsz", type=int, default=1280)
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
    return parser


def main(argv: list[str] | None = None) -> int:
    args = construir_parser().parse_args(argv)
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
        device=args.device,
        fps_muestreo=args.fps,
        segmento=Segmento.parse(args.segmento),
        descanso=a_segundos(args.descanso) if args.descanso else None,
        imgsz=args.imgsz,
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
