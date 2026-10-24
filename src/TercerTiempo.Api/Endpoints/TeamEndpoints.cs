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

        group.MapGet("/{teamId:guid}/crest", async (Guid teamId, HttpContext context, ITeamsRepository teams, CancellationToken ct) =>
            {
                var crest = await teams.OpenCrestAsync(teamId, ct);
                if (crest is null) return Results.NotFound();

                context.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Stream(crest.Content, crest.ContentType);
            })
            .WithName("GetTeamCrest")
            .WithSummary("Escudo del equipo")
            .WithDescription("Escudo descargado de la federación y guardado en la plataforma (crestUrl de las respuestas apunta aquí cuando existe). 404 si el equipo no tiene escudo guardado. Público.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{teamId:guid}/roster/{entryId:guid}/photo", async (Guid teamId, Guid entryId, HttpContext context, ITeamsRepository teams, CancellationToken ct) =>
            {
                var photo = await teams.OpenRosterPhotoAsync(teamId, entryId, ct);
                if (photo is null) return Results.NotFound();

                context.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Stream(photo.Content, photo.ContentType);
            })
            .WithName("GetRosterPhoto")
            .WithSummary("Foto de un jugador de la plantilla")
            .WithDescription("Foto subida por el club (photoUrl de las fichas apunta aquí cuando existe). 404 si no hay foto. Público.")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{teamId:guid}/matches", (Guid teamId, ITeamsRepository teams, CancellationToken ct) => teams.GetMatchesAsync(teamId, ct))
            .WithName("ListTeamMatches")
            .WithSummary("Calendario y resultados del equipo")
            .WithDescription("Todos los partidos de la temporada en curso ordenados por jornada. Si se envía token, myPerformanceId indica en cuáles ya has registrado rendimiento. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
