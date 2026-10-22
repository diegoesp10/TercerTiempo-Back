using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.FootballData.BeSoccer;
using TercerTiempo.Infrastructure.FootballData.Fake;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;
using TercerTiempo.Infrastructure.FootballData.JsonFiles;
using TercerTiempo.Infrastructure.Identity;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Security;
using TercerTiempo.Infrastructure.Teams;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Stores;
using TercerTiempo.Infrastructure.Videos;

namespace TercerTiempo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        AddPersistence(services, configuration);
        AddIdentity(services, configuration);
        AddFootballData(services, configuration);
        AddOfficialSheets(services, configuration);
        AddVideos(services, configuration);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta la cadena de conexión ConnectionStrings:Default (SQL Server).");

        // SQL Server. Varios agregados cargan dos colecciones a la vez (plantilla: staff y fichas; acta oficial: líneas y
        // sucesos): en consultas separadas se evita el producto cartesiano de una sola JOIN. Reintentos ante errores
        // transitorios (arranque del servidor, cortes breves de red).
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)
                .CommandTimeout(60)));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IPlayerStore, PlayerStore>();
        services.AddScoped<ICoachStore, CoachStore>();
        services.AddScoped<ISquadStore, SquadStore>();
        services.AddScoped<IInjuryStore, InjuryStore>();
        services.AddScoped<ITeamStore, TeamStore>();
        services.AddScoped<ICompetitionStore, CompetitionStore>();
        services.AddScoped<IMatchStore, MatchStore>();
        services.AddScoped<IStandingStore, StandingStore>();
        services.AddScoped<IPerformanceStore, PerformanceStore>();
        services.AddScoped<IClubStore, ClubStore>();
        services.AddScoped<ICoachAppointmentStore, CoachAppointmentStore>();
        services.AddScoped<IOfficialSheetStore, OfficialSheetStore>();
        services.AddScoped<IMatchVideoStore, MatchVideoStore>();
        services.AddScoped<IVideoPurchaseStore, VideoPurchaseStore>();
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

        var provider = configuration[$"{FootballDataOptions.SectionName}:Provider"] ?? FootballDataOptions.FutbolAsturianoProvider;

        switch (provider.Trim().ToLowerInvariant())
        {
            case FootballDataOptions.FakeProvider:
                services.AddSingleton<IFootballDataProvider, FakeFootballDataProvider>();
                break;

            case FootballDataOptions.JsonFilesProvider:
                services.AddSingleton<IFootballDataProvider, JsonFileFootballDataProvider>();
                break;

            case FootballDataOptions.BeSoccerProvider:
                services.AddHttpClient<BeSoccerFootballDataProvider>();
                services.AddScoped<IFootballDataProvider>(sp => sp.GetRequiredService<BeSoccerFootballDataProvider>());
                break;

            default:
                // Una instancia por ámbito (sincronización): cachea catálogos de temporada, estadios, posiciones y jugadores.
                services.AddHttpClient<FutbolAsturianoFootballDataProvider>((sp, client) =>
                {
                    var settings = sp.GetRequiredService<IOptions<FootballDataOptions>>().Value.FutbolAsturiano;
                    client.BaseAddress = new Uri(settings.BaseUrl.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                });
                services.AddScoped<IFootballDataProvider>(sp => sp.GetRequiredService<FutbolAsturianoFootballDataProvider>());
                break;
        }

        services.AddHostedService<FootballDataSyncWorker>();
    }

    private static void AddOfficialSheets(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OfficialSheetOptions>()
            .Bind(configuration.GetSection(OfficialSheetOptions.SectionName));

        // La web exige cookie de sesión y redirige a NLogin en la primera petición: el handler las gestiona solo.
        services.AddHttpClient<AsturfutbolClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<OfficialSheetOptions>>().Value.Asturfutbol;
                client.BaseAddress = new Uri(settings.BaseUrl.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(40);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer(),
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            });

        services.AddScoped<IOfficialSheetProvider, AsturfutbolOfficialSheetProvider>();
        services.AddScoped<ITeamProfileProvider, AsturfutbolTeamProfileProvider>();

        services.AddOptions<CrestOptions>()
            .Bind(configuration.GetSection(CrestOptions.SectionName));
        services.AddSingleton<ICrestStorage, FileSystemCrestStorage>();
        services.AddSingleton<IPlayerPhotoStorage, FileSystemPlayerPhotoStorage>();
        services.AddScoped<IErrorMessageStore, ErrorMessageStore>();

        // Límite de llamadas por IP y por endpoint con bloqueos crecientes (el middleware vive en la API).
        services.AddOptions<ThrottlingOptions>().Bind(configuration.GetSection(ThrottlingOptions.SectionName));
        services.AddSingleton<ClientThrottle>();
        services.AddHttpClient<ICrestDownloader, HttpCrestDownloader>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TercerTiempo/1.0 (+https://github.com/diegoesp10/TercerTiempo-Back)");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/*");
        });
    }

    private static void AddVideos(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VideoOptions>()
            .Bind(configuration.GetSection(VideoOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<IVideoStorage, FileSystemVideoStorage>();
        services.AddSingleton<IVideoTickets, MemoryVideoTickets>();
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
