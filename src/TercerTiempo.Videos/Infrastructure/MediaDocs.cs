using TercerTiempo.Hosting;

namespace TercerTiempo.Videos.Infrastructure;

internal static class MediaDocs
{
    public static readonly OpenApiDocumentInfo Info = new(
        "Tercer Tiempo · servicio de vídeo",
        """
        Servicio de vídeo de **Tercer Tiempo**: lo pesado del vídeo fuera de la API, con el mismo token (`Authorization:
        Bearer <token>` de `POST /api/auth/login`) y las mismas reglas de acceso. Un vídeo nunca es público: lo ven la
        plantilla que lo subió (club, cuerpo técnico y jugadores vinculados), el administrador y quien tenga un pase del
        partido que lo incluya.

        **Subir** (club propietario o cuerpo técnico vigente, solo de un partido ya jugado)

        1. `POST /media/squads/{squadId}/matches/{matchId}/video/uploads` → abre una subida por trozos: devuelve `uploadId`,
           el tamaño de trozo y por dónde va (sirve para reanudar).
        2. `PUT /media/squads/{squadId}/video-uploads/{uploadId}/chunks/{index}` → cada trozo en orden (cuerpo binario).
        3. `POST /media/squads/{squadId}/video-uploads/{uploadId}/complete` → publica el vídeo. Queda **en cola para
           comprimir**: el servicio lo recomprime a H.264 sin bajar de 1080p (calidad constante) y, mientras tanto, se sirve
           el original. Su estado (`Uploaded`, `Processing` con `progress`, `Ready` o `Failed`) lo da
           `GET /api/matches/{matchId}/videos` en la API.

        **Ver y descargar** (el reproductor no puede mandar el token: pide un ticket y lo usa en la dirección del archivo)

        1. `POST /media/videos/{videoId}/ticket` → `GET /media/videos/{videoId}/stream?ticket=` (rangos, 206).
        2. `POST /media/videos/{videoId}/download-ticket` → `GET /media/videos/{videoId}/download?ticket=` (solo el club,
           el cuerpo técnico y quien tenga un pase con descarga).

        **Errores**: siempre `application/problem+json` (RFC 9457), con los mismos códigos que la API.
        """,
        [
            ("Subidas", "Subida del vídeo de un partido, de una vez (archivos pequeños) o por trozos reanudables (partidos enteros)."),
            ("Vídeos", "Reproducción y descarga con ticket, y borrado. Solo la plantilla y quien tenga un pase del partido."),
        ]);
}
