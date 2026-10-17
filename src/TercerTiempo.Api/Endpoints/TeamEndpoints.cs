using TercerTiempo.Repository.Teams;

namespace TercerTiempo.Api.Endpoints;

internal static class TeamEndpoints
{
    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/teams").WithTags("Equipos");

        group.MapGet("/", (Guid competitionId, ITeamsRepository teams, CancellationToken ct) => teams.ListAsync(competitionId, ct))
            .WithName("ListTeams")
            .WithSummary("Equipos de una competición")
            .WithDescription("Equipos de la temporada en curso de la competición indicada (competitionId, de GET /api/competitions), ordenados por clasificación. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{teamId:guid}", (Guid teamId, ITeamsRepository teams, CancellationToken ct) => teams.GetAsync(teamId, ct))
            .WithName("GetTeam")
            .WithSummary("Ficha del equipo")
            .WithDescription("Competición y temporada, clasificación, plantilla según el proveedor (roster, con el id que se usa para reclamar la ficha; rosterPublished indica si el proveedor la publica) y jugadores registrados en la plataforma. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{teamId:guid}/matches", (Guid teamId, ITeamsRepository teams, CancellationToken ct) => teams.GetMatchesAsync(teamId, ct))
            .WithName("ListTeamMatches")
            .WithSummary("Calendario y resultados del equipo")
            .WithDescription("Todos los partidos de la temporada en curso ordenados por jornada. Si se envía token, myPerformanceId indica en cuáles ya has registrado rendimiento. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
