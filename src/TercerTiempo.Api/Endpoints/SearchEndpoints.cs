using TercerTiempo.Repository.Search;

namespace TercerTiempo.Api.Endpoints;

internal static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", (string? q, string? type, int? limit, ISearchRepository search, CancellationToken ct) => search.SearchAsync(q, type, limit, ct))
            .WithTags("Buscador")
            .RequireAuthorization()
            .WithName("Search")
            .WithSummary("Buscar en la app")
            .WithDescription("Para cuentas de club, técnico o jugador (no para el administrador). Busca ligas, equipos, clubes con cuenta, jugadores de las plantillas publicadas, partidos (escribiendo dos equipos: «nalón roces») y las plantillas internas que la cuenta puede ver. Sin tildes ni mayúsculas, por el comienzo de cada palabra y con erratas; los equipos de la cuenta suben. Devuelve grupos por tipo (limit por tipo, 1-50) con el enlace del frontal de cada resultado. type limita a un tipo: competition, team, club, player, match o squad.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
