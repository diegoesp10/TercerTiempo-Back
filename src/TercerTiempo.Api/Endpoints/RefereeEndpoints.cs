using TercerTiempo.Application.Referees;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Repository.Referees;

namespace TercerTiempo.Api.Endpoints;

internal static class RefereeEndpoints
{
    public static IEndpointRouteBuilder MapRefereeEndpoints(this IEndpointRouteBuilder app)
    {
        // Los árbitros y sus notas son públicos, como la liga y los equipos; puntuar es de los clubes y sus cuerpos técnicos.
        var group = app.MapGroup("/api/referees").WithTags("Árbitros");

        group.MapGet("/categories", (IRefereesRepository referees, CancellationToken ct) => referees.GetCategoriesAsync(ct))
            .AllowAnonymous()
            .WithName("ListRefereeCategories")
            .WithSummary("Categorías con árbitros")
            .WithDescription("Primer paso para buscar árbitros: las categorías de los partidos con equipo arbitral (sus grupos van juntos), de la más alta a la más baja, con cuántos árbitros principales, asistentes, partidos y notas tiene cada una y sus ligas (competitionIds). Pública: sin token, con el de una cuenta o con el de administrador da lo mismo.");

        group.MapGet("/categories/{category}", (
                string category, OfficialDuty? duty, string? search, RefereeSort? sort, SortOrder? order, IRefereesRepository referees, CancellationToken ct) =>
                referees.ListAsync(category, duty, search, sort, order, ct))
            .AllowAnonymous()
            .WithName("ListReferees")
            .WithSummary("Árbitros de una categoría")
            .WithDescription("Los árbitros se buscan siempre dentro de una categoría (su clave, de GET /api/referees/categories, p. ej. 3a-asturfutbol). Una fila por árbitro y función con sus partidos pitados, cuántas notas tiene y su media (0-10) en la categoría. duty: Main (principal) o Assistant (asistente o cuarto árbitro); sin él, las dos. search busca por nombre sin distinguir tildes. sort: Average (por defecto), Ratings, Matches, Name o LastMatch; order: Desc o Asc (por defecto Desc, y Asc por nombre). Pública, sin token.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/pending", (IRefereesRepository referees, CancellationToken ct) => referees.GetPendingAsync(ct))
            .RequireAuthorization()
            .WithName("ListPendingRefereeRatings")
            .WithSummary("Equipos arbitrales por puntuar")
            .WithDescription("Partidos de los últimos 30 días de los equipos de la cuenta (club: todos los suyos; técnico: los de sus plantillas) con equipo arbitral al que su equipo aún no ha puesto nota.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{refereeId:guid}", (Guid refereeId, IRefereesRepository referees, CancellationToken ct) => referees.GetAsync(refereeId, ct))
            .AllowAnonymous()
            .WithName("GetReferee")
            .WithSummary("Ficha de un árbitro")
            .WithDescription("Sus estadísticas (partidos, notas, media y reparto de 0 a 10), por categoría y función, las mejores notas con comentario, todas sus notas con el equipo y quién las puso, y sus partidos con la nota de cada uno de los dos equipos. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var matches = app.MapGroup("/api/matches/{matchId:guid}/officials").WithTags("Árbitros");

        matches.MapGet("/", (Guid matchId, IRefereesRepository referees, CancellationToken ct) => referees.GetMatchOfficialsAsync(matchId, ct))
            .AllowAnonymous()
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
