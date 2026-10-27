using TercerTiempo.Domain.Referees;
using TercerTiempo.Repository.Referees;

namespace TercerTiempo.Api.Endpoints;

internal static class RefereeEndpoints
{
    public static IEndpointRouteBuilder MapRefereeEndpoints(this IEndpointRouteBuilder app)
    {
        // Los árbitros y sus notas son públicos, como la liga y los equipos; puntuar es de los clubes y sus cuerpos técnicos.
        var group = app.MapGroup("/api/referees").WithTags("Árbitros");

        group.MapGet("/", (string? category, OfficialDuty? duty, string? search, IRefereesRepository referees, CancellationToken ct) =>
                referees.ListAsync(category, duty, search, ct))
            .WithName("ListReferees")
            .WithSummary("Árbitros de una categoría y su nota media")
            .WithDescription("Árbitros de los partidos guardados (de las actas de la federación y de futbolasturiano.es) con sus cifras en cada categoría y función: una fila por árbitro, categoría y función (Main: principal; Assistant: asistente o cuarto árbitro), con los partidos, cuántas notas tiene y su media (0-10), de mejor a peor. category (la clave de GET /api/referees/categories) y duty filtran; search busca por nombre. Público.");

        group.MapGet("/categories", (IRefereesRepository referees, CancellationToken ct) => referees.GetCategoriesAsync(ct))
            .WithName("ListRefereeCategories")
            .WithSummary("Categorías con árbitros")
            .WithDescription("Categorías de los partidos con equipo arbitral, de la más alta a la más baja, con cuántos árbitros principales, asistentes, partidos y notas tiene cada una, y las de los equipos de la cuenta (si hay sesión) para abrir la suya. Público.");

        group.MapGet("/pending", (IRefereesRepository referees, CancellationToken ct) => referees.GetPendingAsync(ct))
            .RequireAuthorization()
            .WithName("ListPendingRefereeRatings")
            .WithSummary("Equipos arbitrales por puntuar")
            .WithDescription("Partidos de los últimos 30 días de los equipos de la cuenta (club: todos los suyos; técnico: los de sus plantillas) con equipo arbitral al que su equipo aún no ha puesto nota.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{refereeId:guid}", (Guid refereeId, IRefereesRepository referees, CancellationToken ct) => referees.GetAsync(refereeId, ct))
            .WithName("GetReferee")
            .WithSummary("Ficha de un árbitro")
            .WithDescription("Sus estadísticas (partidos, notas, media y reparto de 0 a 10), por categoría y función, las mejores notas con comentario, todas sus notas con el equipo y quién las puso, y sus partidos con la nota de cada uno de los dos equipos. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var matches = app.MapGroup("/api/matches/{matchId:guid}/officials").WithTags("Árbitros");

        matches.MapGet("/", (Guid matchId, IRefereesRepository referees, CancellationToken ct) => referees.GetMatchOfficialsAsync(matchId, ct))
            .WithName("GetMatchOfficials")
            .WithSummary("Equipo arbitral de un partido")
            .WithDescription("Árbitro, asistentes y cuarto árbitro con su nota media y las notas de los dos equipos en este partido, con quién las puso y por qué. Público; si la cuenta es del club o del cuerpo técnico de uno de los dos equipos, dice además si puede puntuar (partido jugado y licencia vigente).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        matches.MapPut("/{refereeId:guid}/rating", (Guid matchId, Guid refereeId, RefereeRatingRequest request, IRefereesRepository referees, CancellationToken ct) =>
                referees.RateAsync(matchId, refereeId, request, ct))
            .RequireAuthorization()
            .WithName("RateReferee")
            .WithSummary("Puntuar a un miembro del equipo arbitral")
            .WithDescription("Nota de 0 a 10 con un comentario opcional, en nombre del equipo de la cuenta (su club o su cuerpo técnico vigente, con licencia) en un partido que ya ha jugado. Una por equipo, árbitro y partido, y definitiva: confirmed tiene que ser true y una segunda nota del mismo equipo da 409. Se guarda quién la puso (el club o el técnico, con su función).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
