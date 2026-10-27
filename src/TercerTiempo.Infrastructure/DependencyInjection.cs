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
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Licensing;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.FootballData.Fake;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;
using TercerTiempo.Infrastructure.FootballData.JsonFiles;
using TercerTiempo.Infrastructure.Identity;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Application.Jobs;
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
        services.AddSingleton<Search.SearchIndexInvalidator>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(connectionString, sql => sql
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)
                .CommandTimeout(60))
            .AddInterceptors(sp.GetRequiredService<Search.SearchIndexInvalidator>()));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IPlayerStore, PlayerStore>();
        services.AddScoped<ICoachStore, CoachStore>();
        services.AddScoped<ISquadStore, SquadStore>();
        services.AddScoped<IInjuryStore, InjuryStore>();
        services.AddScoped<ITeamMatchStatsStore, TeamMatchStatsStore>();
        services.AddScoped<ITeamStore, TeamStore>();
        services.AddScoped<ICompetitionStore, CompetitionStore>();
        services.AddScoped<IMatchStore, MatchStore>();
        services.AddScoped<IStandingStore, StandingStore>();
        services.AddScoped<IPerformanceStore, PerformanceStore>();
        services.AddScoped<IPerformanceSuggestionStore, SuggestionStore>();
        services.AddScoped<IClubStore, ClubStore>();
        services.AddScoped<IClubInvitationStore, ClubInvitationStore>();
        services.AddScoped<ICoachAppointmentStore, CoachAppointmentStore>();
        services.AddScoped<IOfficialSheetStore, OfficialSheetStore>();
        services.AddScoped<IMatchVideoStore, MatchVideoStore>();
        services.AddScoped<IVideoPurchaseStore, VideoPurchaseStore>();
        services.AddScoped<IVideoUploadStore, VideoUploadStore>();
        services.AddScoped<IVideoShareStore, VideoShareStore>();
        services.AddScoped<ILicenseStore, LicenseStore>();
        services.AddScoped<TercerTiempo.Domain.Alerts.IAlertStore, TercerTiempo.Infrastructure.Alerts.AlertStore>();
        services.AddScoped<TercerTiempo.Domain.Referees.IRefereeStore, TercerTiempo.Infrastructure.Referees.RefereeStore>();
        services.AddScoped<ILicensingSettingsStore, TercerTiempo.Infrastructure.Licensing.LicensingSettingsStore>();
        services.AddScoped<TercerTiempo.Domain.Competitions.IFootballSyncSettingsStore, FootballData.FootballSyncSettingsStore>();
        services.AddScoped<TercerTiempo.Domain.Competitions.ISeasonYearStore, Persistence.Stores.SeasonYearStore>();
        services.AddScoped<TercerTiempo.Domain.Privacy.ICookieStore, Privacy.CookieStore>();
        services.AddScoped<TercerTiempo.Domain.Privacy.IAnalyticsStore, Privacy.AnalyticsStore>();
        services.AddScoped<TercerTiempo.Application.Privacy.IAnalyticsReports, Privacy.AnalyticsReports>();
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddIdentityCore<ApplicationUser>(IdentitySetup.Configure)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddPasswordValidator<PasswordRulesValidator>();

        services.Configure<PasswordHasherOptions>(IdentitySetup.ConfigureHasher);
        services.AddSingleton<SessionStamps>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Key) && o.Key.Length >= 32,
                "Jwt:Key debe configurarse con al menos 32 caracteres (user-secrets o variable de entorno).")
            .ValidateOnStart();

        services.AddOptions<AdminOptions>().Bind(configuration.GetSection(AdminOptions.SectionName));
        services.AddOptions<TercerTiempo.Application.Clubs.Invitations.RegistrationOptions>()
            .Bind(configuration.GetSection(TercerTiempo.Application.Clubs.Invitations.RegistrationOptions.SectionName));

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

            default:
                // Una instancia por ámbito (sincronización): cachea catálogos de temporada, estadios, posiciones y jugadores.
                services.AddHttpClient<FutbolAsturianoFootballDataProvider>((sp, client) =>
                {
                    var settings = sp.GetRequiredService<IOptions<FootballDataOptions>>().Value.FutbolAsturiano;
                    client.BaseAddress = new Uri(settings.BaseUrl.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/");
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                }).LogScrapes("futbolasturiano");
                services.AddScoped<IFootballDataProvider>(sp => sp.GetRequiredService<FutbolAsturianoFootballDataProvider>());
                break;
        }

        // Trabajos en segundo plano: cuándo tocan, su historial y el lanzamiento a mano desde /jobs.
        services.AddSingleton<JobScheduler>();
        services.AddScoped<IJobsMonitor, JobsMonitor>();
        services.AddScoped<TercerTiempo.Application.Search.ISearchCorpusSource, Search.SearchCorpusSource>();
        services.AddHostedService<FootballDataSyncWorker>();

        // Actas oficiales: cola en memoria y proceso en segundo plano que las descarga y guarda.
        services.AddSingleton<TercerTiempo.Infrastructure.OfficialSheets.OfficialSheetQueue>();
        services.AddSingleton<TercerTiempo.Application.OfficialSheets.IOfficialSheetQueue>(sp => sp.GetRequiredService<TercerTiempo.Infrastructure.OfficialSheets.OfficialSheetQueue>());
        services.AddSingleton<TercerTiempo.Application.OfficialSheets.ISheetDownloadSignal>(sp => sp.GetRequiredService<TercerTiempo.Infrastructure.OfficialSheets.OfficialSheetQueue>());
        services.AddScoped<TercerTiempo.Application.OfficialSheets.ISheetDownloadStore, TercerTiempo.Infrastructure.OfficialSheets.SheetDownloadStore>();
        services.AddHostedService<TercerTiempo.Infrastructure.OfficialSheets.OfficialSheetSyncWorker>();
        services.AddHostedService<TercerTiempo.Infrastructure.OfficialSheets.WeeklySheetQueueWorker>();
    }

    private static void AddOfficialSheets(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAsturfutbolRequestStore, SqlAsturfutbolRequestStore>();
        services.AddSingleton<AsturfutbolRequestGate>();
        services.AddSingleton<AsturfutbolSession>();
        services.AddOptions<OfficialSheetOptions>()
            .Bind(configuration.GetSection(OfficialSheetOptions.SectionName));

        // Cookie compartida; los saltos de sesión también pasan por el control persistente de peticiones.
        services.AddHttpClient<AsturfutbolClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<OfficialSheetOptions>>().Value.Asturfutbol;
                client.BaseAddress = new Uri(settings.BaseUrl.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(40);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = sp.GetRequiredService<AsturfutbolSession>().Cookies,
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .LogScrapes(AsturfutbolOfficialSheetProvider.Name);

        services.AddScoped<IOfficialSheetProvider, AsturfutbolOfficialSheetProvider>();
        services.AddScoped<ITeamProfileProvider, AsturfutbolTeamProfileProvider>();

        // Webs del acta pública (OfficialSheets:Sources): hoy solo la federación.
        services.AddOptions<TercerTiempo.Application.OfficialSheets.PublicSheetOptions>()
            .Bind(configuration.GetSection(TercerTiempo.Application.OfficialSheets.PublicSheetOptions.SectionName));

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
        }).LogScrapes("escudos");
    }

    private static void AddVideos(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VideoOptions>()
            .Bind(configuration.GetSection(VideoOptions.SectionName));

        // Licencias por equipo: planes, precios, prueba y pasarela («mock» = pago simulado, «stripe»).
        services.AddOptions<LicensingOptions>()
            .Bind(configuration.GetSection(LicensingOptions.SectionName));
        if (configuration.GetValue<string>($"{LicensingOptions.SectionName}:Provider") == "stripe")
        {
            services.AddHttpClient<ILicensePaymentGateway, StripeLicensePaymentGateway>((sp, client) =>
            {
                var stripe = sp.GetRequiredService<IOptions<LicensingOptions>>().Value.Stripe;
                client.BaseAddress = new Uri(stripe.ApiBaseUrl.EndsWith('/') ? stripe.ApiBaseUrl : stripe.ApiBaseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddScoped<IPaymentSimulator, DisabledPaymentSimulator>();
        }
        else
        {
            services.AddScoped<MockPaymentGateway>();
            services.AddScoped<ILicensePaymentGateway>(sp => sp.GetRequiredService<MockPaymentGateway>());
            services.AddScoped<IPaymentSimulator>(sp => sp.GetRequiredService<MockPaymentGateway>());
        }
        services.AddHostedService<LicenseRenewalWorker>();
        services.AddHostedService<TercerTiempo.Infrastructure.Alerts.LicenseAlertsWorker>();
        services.AddHostedService<TercerTiempo.Infrastructure.Referees.MatchOfficialsWorker>();

        // Cola de avisos de la pasarela (webhooks de Stripe o eventos de la simulada) y el proceso que los aplica.
        services.AddScoped<IPaymentEventInbox, PaymentEventInbox>();
        services.AddSingleton<PaymentEventSignal>();
        services.AddSingleton<IPaymentEventSignal>(sp => sp.GetRequiredService<PaymentEventSignal>());
        services.AddHostedService<PaymentEventWorker>();

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
