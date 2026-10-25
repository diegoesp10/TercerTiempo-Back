using Microsoft.Extensions.DependencyInjection;
using TercerTiempo.Repository.Admin;
using TercerTiempo.Repository.Auth;
using TercerTiempo.Repository.Clubs;
using TercerTiempo.Repository.Coaches;
using TercerTiempo.Repository.Competitions;
using TercerTiempo.Repository.Matches;
using TercerTiempo.Repository.Players;
using TercerTiempo.Repository.Squads;
using TercerTiempo.Repository.Stats;
using TercerTiempo.Repository.Teams;
using TercerTiempo.Repository.Videos;

namespace TercerTiempo.Repository;

public static class DependencyInjection
{
    public static IServiceCollection AddRepositoryLayer(this IServiceCollection services)
    {
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<Search.ISearchRepository, Search.SearchRepository>();
        services.AddScoped<IPlayersRepository, PlayersRepository>();
        services.AddScoped<ICoachesRepository, CoachesRepository>();
        services.AddScoped<IClubsRepository, ClubsRepository>();
        services.AddScoped<ISquadsRepository, SquadsRepository>();
        services.AddScoped<ICompetitionsRepository, CompetitionsRepository>();
        services.AddScoped<ITeamsRepository, TeamsRepository>();
        services.AddScoped<IMatchesRepository, MatchesRepository>();
        services.AddScoped<IVideosRepository, VideosRepository>();
        services.AddScoped<IStatsRepository, StatsRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<Privacy.IPrivacyRepository, Privacy.PrivacyRepository>();
        services.AddScoped<Licensing.ILicensingRepository, Licensing.LicensingRepository>();

        return services;
    }
}
