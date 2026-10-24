using TercerTiempo.Repository.Competitions;

namespace TercerTiempo.Api.Endpoints;

internal static class CompetitionEndpoints
{
    public static IEndpointRouteBuilder MapCompetitionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/seasons", (ICompetitionsRepository competitions, CancellationToken ct) => competitions.ListSeasonsAsync(ct))
            .WithTags("Competiciones")
            .WithName("ListSeasons")
            .WithSummary("Temporadas")
            .WithDescription("Las temporadas de la app (2026/2027, 2025/2026...), de la más reciente a la más antigua, con la actual marcada. Todo lo demás (ligas, equipos, calendario, clasificación, plantillas) se lee de la temporada que llega en la cabecera X-Season (su id o su nombre, p. ej. 2025-2026); sin ella, de la actual. Las temporadas pasadas solo se consultan. Público.");

        var group = app.MapGroup("/api/competitions").WithTags("Competiciones");

        group.MapGet("/", (ICompetitionsRepository competitions, CancellationToken ct) => competitions.ListAsync(ct))
            .WithName("ListCompetitions")
            .WithSummary("Ligas disponibles")
            .WithDescription("Las ligas (categoría + grupo) de la temporada elegida (cabecera X-Season; sin ella, la actual), con nivel (Professional, National, Regional, Cup, Youth), escalón, grupo, su temporada y número de equipos. Ordenadas por nivel, escalón y grupo. Público.");

        group.MapGet("/{competitionId:guid}/matchdays/current", (Guid competitionId, ICompetitionsRepository competitions, CancellationToken ct) => competitions.GetMatchdayAsync(competitionId, null, ct))
            .WithName("GetCurrentMatchday")
            .WithSummary("Jornada en curso")
            .WithDescription("Partidos de la jornada en curso o, si no hay ninguna en juego, de la próxima: la primera cuya fecha central (mediana de sus partidos) no quedó atrás hace más de un día, así que cambia el lunes. Incluye todas las jornadas de la temporada con sus fechas para navegar entre ellas. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{competitionId:guid}/matchdays/{matchday:int}", (Guid competitionId, int matchday, ICompetitionsRepository competitions, CancellationToken ct) => competitions.GetMatchdayAsync(competitionId, matchday, ct))
            .WithName("GetMatchday")
            .WithSummary("Partidos de una jornada")
            .WithDescription("Calendario y resultados de una jornada de la temporada en curso, ordenados por fecha y hora. currentMatchday indica cuál es la jornada en curso. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
