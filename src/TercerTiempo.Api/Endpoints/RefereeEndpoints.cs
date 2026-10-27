using TercerTiempo.Domain.Referees;
using TercerTiempo.Repository.Referees;

namespace TercerTiempo.Api.Endpoints;

internal static class RefereeEndpoints
{
    public static IEndpointRouteBuilder MapRefereeEndpoints(this IEndpointRouteBuilder app)
    {
        // Para clubes y cuerpos técnicos con licencia vigente: ni el público ni los jugadores ven árbitros ni notas.
        var group = app.MapGroup("/api/referees").WithTags("Árbitros").RequireAuthorization();

        group.MapGet("/", (string? category, OfficialDuty? duty, string? search, IRefereesRepository referees, CancellationToken ct) =>
                referees.ListAsync(category, duty, search, ct))
            .WithName("ListReferees")
            .WithSummary("Árbitros de una categoría y su nota media")
            .WithDescription("Árbitros de los partidos guardados (de las actas de la federación y de futbolasturiano.es) con sus cifras en cada categoría y función: una fila por árbitro, categoría y función (Main: principal; Assistant: asistente o cuarto árbitro), con los partidos, cuántas notas tiene y su media (0-10), de mejor a peor. category (la clave de GET /api/referees/categories) y duty filtran; search busca por nombre. Solo clubes y cuerpos técnicos con licencia vigente.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/categories", (IRefereesRepository referees, CancellationToken ct) => referees.GetCategoriesAsync(ct))
            .WithName("ListRefereeCategories")
            .WithSummary("Categorías con árbitros")
            .WithDescription("Categorías de los partidos con equipo arbitral, de la más alta a la más baja, con cuántos árbitros principales, asistentes, partidos y notas tiene cada una, y las de los equipos de la cuenta para abrir la suya.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/pending", (IRefereesRepository referees, CancellationToken ct) => referees.GetPendingAsync(ct))
            .WithName("ListPendingRefereeRatings")
            .WithSummary("Equipos arbitrales por puntuar")
            .WithDescription("Partidos de los últimos 30 días de los equipos de la cuenta (club: todos los suyos; técnico: los de sus plantillas) con equipo arbitral al que su equipo aún no ha puesto nota.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{refereeId:guid}", (Guid refereeId, IRefereesRepository referees, CancellationToken ct) => referees.GetAsync(refereeId, ct))
            .WithName("GetReferee")
            .WithSummary("Ficha de un árbitro")
            .WithDescription("Sus estadísticas (partidos, notas, media y reparto de 0 a 10), por categoría y función, las mejores notas con comentario, todas sus notas con el equipo que las puso y sus partidos.")
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
