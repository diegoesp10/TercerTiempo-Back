using TercerTiempo.Repository.Referees;

namespace TercerTiempo.Api.Endpoints;

internal static class RefereeEndpoints
{
    public static IEndpointRouteBuilder MapRefereeEndpoints(this IEndpointRouteBuilder app)
    {
        // Para clubes y cuerpos técnicos con licencia vigente: ni el público ni los jugadores ven árbitros ni notas.
        var group = app.MapGroup("/api/referees").WithTags("Árbitros").RequireAuthorization();

        group.MapGet("/", (string? search, IRefereesRepository referees, CancellationToken ct) => referees.ListAsync(search, ct))
            .WithName("ListReferees")
            .WithSummary("Árbitros y su nota media")
            .WithDescription("Árbitros, asistentes y cuartos árbitros de los partidos guardados (de las actas de la federación y de futbolasturiano.es), con los partidos en que figuran, cuántas notas tienen y su media (0-10), de mejor a peor. search filtra por nombre. Solo clubes y cuerpos técnicos con licencia vigente.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/pending", (IRefereesRepository referees, CancellationToken ct) => referees.GetPendingAsync(ct))
            .WithName("ListPendingRefereeRatings")
            .WithSummary("Equipos arbitrales por puntuar")
            .WithDescription("Partidos de los últimos 30 días de los equipos de la cuenta (club: todos los suyos; técnico: los de sus plantillas) con equipo arbitral al que su equipo aún no ha puesto nota.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{refereeId:guid}", (Guid refereeId, IRefereesRepository referees, CancellationToken ct) => referees.GetAsync(refereeId, ct))
            .WithName("GetReferee")
            .WithSummary("Ficha de un árbitro")
            .WithDescription("Sus estadísticas (partidos, notas, media y reparto de 0 a 10), por función, las mejores notas con comentario, todas sus notas con el equipo que las puso y sus partidos.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var matches = app.MapGroup("/api/matches/{matchId:guid}/officials").WithTags("Árbitros").RequireAuthorization();

        matches.MapGet("/", (Guid matchId, IRefereesRepository referees, CancellationToken ct) => referees.GetMatchOfficialsAsync(matchId, ct))
            .WithName("GetMatchOfficials")
            .WithSummary("Equipo arbitral de un partido")
            .WithDescription("Árbitro, asistentes y cuarto árbitro con su nota media y, si la cuenta es del club o del cuerpo técnico de uno de los dos equipos, la nota que le ha puesto su equipo y si puede puntuar (solo con el partido ya jugado).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        matches.MapPut("/{refereeId:guid}/rating", (Guid matchId, Guid refereeId, RefereeRatingRequest request, IRefereesRepository referees, CancellationToken ct) =>
                referees.RateAsync(matchId, refereeId, request, ct))
            .WithName("RateReferee")
            .WithSummary("Puntuar a un miembro del equipo arbitral")
            .WithDescription("Nota de 0 a 10 con un comentario opcional, en nombre del equipo de la cuenta (su club o su cuerpo técnico vigente) en un partido que ya ha jugado. Una por equipo, árbitro y partido: repetirla la cambia.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
