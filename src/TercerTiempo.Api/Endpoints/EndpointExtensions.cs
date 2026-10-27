namespace TercerTiempo.Api.Endpoints;

public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapTercerTiempoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapClubEndpoints();
        app.MapAlertEndpoints();
        app.MapCoachEndpoints();
        app.MapSquadEndpoints();
        app.MapPlayerEndpoints();
        app.MapCompetitionEndpoints();
        app.MapTeamEndpoints();
        app.MapMatchEndpoints();
        app.MapRefereeEndpoints();
        app.MapModerationEndpoints();
        app.MapVideoEndpoints();
        app.MapStatsEndpoints();
        app.MapAdminEndpoints();
        app.MapLicensingEndpoints();
        app.MapPrivacyEndpoints();
        app.MapSearchEndpoints();

        return app;
    }
}
