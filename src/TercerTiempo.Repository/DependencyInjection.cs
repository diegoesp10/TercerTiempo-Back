using Microsoft.Extensions.DependencyInjection;
using TercerTiempo.Repository.Admin;
using TercerTiempo.Repository.Auth;
using TercerTiempo.Repository.Competitions;
using TercerTiempo.Repository.Matches;
using TercerTiempo.Repository.Performances;
using TercerTiempo.Repository.Players;
using TercerTiempo.Repository.Stats;
using TercerTiempo.Repository.Teams;

namespace TercerTiempo.Repository;

public static class DependencyInjection
{
    /// <summary>Registra las fachadas que consumen los endpoints. Requiere <c>AddApplication()</c>.</summary>
    public static IServiceCollection AddRepositoryLayer(this IServiceCollection services)
    {
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IPlayersRepository, PlayersRepository>();
        services.AddScoped<ICompetitionsRepository, CompetitionsRepository>();
        services.AddScoped<ITeamsRepository, TeamsRepository>();
        services.AddScoped<IMatchesRepository, MatchesRepository>();
        services.AddScoped<IPerformancesRepository, PerformancesRepository>();
        services.AddScoped<IStatsRepository, StatsRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();

        return services;
    }
}
