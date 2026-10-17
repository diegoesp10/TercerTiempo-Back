using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.FootballData.BeSoccer;
using TercerTiempo.Infrastructure.FootballData.Fake;
using TercerTiempo.Infrastructure.FootballData.JsonFiles;
using TercerTiempo.Infrastructure.Identity;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Stores;

namespace TercerTiempo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        AddPersistence(services, configuration);
        AddIdentity(services, configuration);
        AddFootballData(services, configuration);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=tercertiempo.db";

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IPlayerStore, PlayerStore>();
        services.AddScoped<ITeamStore, TeamStore>();
        services.AddScoped<ICompetitionStore, CompetitionStore>();
        services.AddScoped<IMatchStore, MatchStore>();
        services.AddScoped<IStandingStore, StandingStore>();
        services.AddScoped<IPerformanceStore, PerformanceStore>();
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Key) && o.Key.Length >= 32,
                "Jwt:Key debe configurarse con al menos 32 caracteres (user-secrets o variable de entorno).")
            .ValidateOnStart();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
    }

    private static void AddFootballData(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FootballDataOptions>()
            .Bind(configuration.GetSection(FootballDataOptions.SectionName));

        var provider = configuration[$"{FootballDataOptions.SectionName}:Provider"] ?? FootballDataOptions.FakeProvider;

        switch (provider.ToLowerInvariant())
        {
            case FootballDataOptions.BeSoccerProvider:
                services.AddHttpClient<BeSoccerFootballDataProvider>();
                services.AddScoped<IFootballDataProvider>(sp => sp.GetRequiredService<BeSoccerFootballDataProvider>());
                break;

            case FootballDataOptions.JsonFilesProvider:
                services.AddSingleton<IFootballDataProvider, JsonFileFootballDataProvider>();
                break;

            default:
                services.AddSingleton<IFootballDataProvider, FakeFootballDataProvider>();
                break;
        }

        services.AddHostedService<FootballDataSyncWorker>();
    }

    /// <summary>Aplica las migraciones pendientes. Se invoca desde la API al arrancar.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
