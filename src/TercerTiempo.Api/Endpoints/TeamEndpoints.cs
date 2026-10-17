using TercerTiempo.Repository.Teams;

namespace TercerTiempo.Api.Endpoints;

internal static class TeamEndpoints
{
    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/teams").WithTags("Equipos");

        group.MapGet("/", (ITeamsRepository teams, CancellationToken ct) => teams.ListAsync(ct))
            .WithSummary("Equipos de la temporada actual ordenados por clasificación");

        group.MapGet("/{teamId:guid}", (Guid teamId, ITeamsRepository teams, CancellationToken ct) => teams.GetAsync(teamId, ct))
            .WithSummary("Ficha del equipo: clasificación, plantilla del proveedor y jugadores registrados")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{teamId:guid}/matches", (Guid teamId, ITeamsRepository teams, CancellationToken ct) => teams.GetMatchesAsync(teamId, ct))
            .WithSummary("Calendario y resultados del equipo en la temporada actual");

        return app;
    }
}
