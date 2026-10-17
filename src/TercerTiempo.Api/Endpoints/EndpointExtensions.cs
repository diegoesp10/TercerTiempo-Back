namespace TercerTiempo.Api.Endpoints;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapTercerTiempoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapSquadEndpoints();
        app.MapPlayerEndpoints();
        app.MapCompetitionEndpoints();
        app.MapTeamEndpoints();
        app.MapMatchEndpoints();
        app.MapStatsEndpoints();
        app.MapAdminEndpoints();

        return app;
    }
}
