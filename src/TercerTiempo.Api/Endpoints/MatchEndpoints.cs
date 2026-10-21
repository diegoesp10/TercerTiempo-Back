using TercerTiempo.Repository.Matches;

namespace TercerTiempo.Api.Endpoints;

internal static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/matches").WithTags("Partidos");

        group.MapGet("/{matchId:guid}", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.GetAsync(matchId, ct))
            .WithName("GetMatch")
            .WithSummary("Detalle de un partido")
            .WithDescription("Datos del partido y las actas de las plantillas de ambos equipos. Cada acta solo se incluye si el usuario puede verla (plantilla pública o propia); si no, llega vacía con visible = false. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{matchId:guid}/official-sheet", (Guid matchId, bool? refresh, IMatchesRepository matches, CancellationToken ct) => matches.GetOfficialSheetAsync(matchId, refresh ?? false, ct))
            .WithName("GetOfficialSheet")
            .WithSummary("Acta oficial del partido (federación)")
            .WithDescription("Copia del acta que publica asturfutbol.es: alineaciones con dorsales, minutos jugados, goles, tarjetas y sustituciones de los dos equipos. status indica si el partido aún no se ha jugado (NotPlayed), si la federación todavía no la ha publicado (Pending: se vuelve a consultar cada 10 minutos), si está disponible (Available: se refresca una vez al día durante la semana siguiente) o si no se ha localizado el partido (Unavailable). Con refresh=true se descarga de nuevo. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{matchId:guid}/videos", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.ListVideosAsync(matchId, ct))
            .WithName("ListMatchVideos")
            .WithSummary("Vídeos del partido")
            .WithDescription("Vídeos subidos por el cuerpo técnico de cualquiera de los dos equipos, con el acceso del usuario a cada uno: Owner (su plantilla lo subió), Free, Purchased, Locked (técnico de otra plantilla: puede comprarlo) o None. Público, pero sin sesión nunca hay acceso.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
