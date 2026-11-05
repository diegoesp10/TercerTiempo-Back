using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

// Cada entorno es appsettings.json con su appsettings.{Entorno}.json encima, como lo lee la API.
public class EnvironmentSettingsTests
{
    private static readonly string[] JobsThatReadTheWeb =
        [JobNames.FootballData, JobNames.MatchOfficials, JobNames.LeagueZones, JobNames.OfficialSheets, JobNames.WeeklySheets];

    [Theory]
    [InlineData("Development", false)]
    [InlineData("FakeDatos", false)]
    [InlineData("Pilot", true)]
    [InlineData("Production", true)]
    public void Only_publication_asks_the_administrator_for_the_authenticator_code(string environment, bool mfa) =>
        Assert.Equal(mfa, Load(environment).GetValue<bool>("Admin:RequireMfa"));

    // Probar el login en local no deja la cuenta 15 minutos fuera; en publicación, cinco fallos la bloquean.
    [Theory]
    [InlineData("Development", false)]
    [InlineData("FakeDatos", false)]
    [InlineData("Pilot", true)]
    [InlineData("Production", true)]
    public void Only_publication_locks_accounts_after_failed_logins(string environment, bool lockout) =>
        Assert.Equal(lockout, Load(environment).GetValue<bool>("Lockout:Enabled"));

    // Sea cual sea el entorno que esté en marcha, descarga los datos reales a la base compartida (de uno en uno, por el
    // bloqueo) y los demás los copian al arrancar.
    [Theory]
    [InlineData("Development")]
    [InlineData("FakeDatos")]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void Every_environment_downloads_the_real_data_on_its_own(string environment)
    {
        var configuration = Load(environment);
        var scheduler = Scheduler(configuration);

        Assert.True(configuration.GetValue<bool>("RealData:Ingest"));
        Assert.All(JobsThatReadTheWeb, job => Assert.True(scheduler.Get(job).Enabled && scheduler.Get(job).Interval is not null, job));
    }

    [Fact]
    public void Fake_data_uses_its_own_database_and_the_shared_real_data()
    {
        var configuration = Load("FakeDatos");

        Assert.Contains("Database=tercertiempo_DatosFake;", configuration.GetConnectionString("Default"));
        Assert.Contains("Database=tercertiempo_DatosReales;", configuration.GetConnectionString("RealData"));
    }

    [Fact]
    public void The_pilot_limits_requests_by_the_ip_the_tunnel_sends() =>
        Assert.True(Load("Pilot").GetValue<bool>("Throttling:TrustForwardedHeaders"));

    // El servicio de vídeo valida el JWT que firma la API: misma clave, emisor y audiencia en cada entorno (en Pilot y
    // Production las dos claves llegan por TT_JWT_KEY, así que aquí las dos están vacías).
    [Theory]
    [InlineData("Development")]
    [InlineData("FakeDatos")]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void The_video_service_validates_the_tokens_the_api_signs(string environment)
    {
        var api = Load(environment);
        var videos = Load(environment, VideoService);

        foreach (var key in new[] { "Jwt:Key", "Jwt:Issuer", "Jwt:Audience" })
            Assert.Equal(api[key], videos[key]);
        if (environment is "Development" or "FakeDatos")
            Assert.True(api["Jwt:Key"]?.Length >= 32);
    }

    // Los dos procesos leen la misma base, y la subida por trozos del frontal cuenta con los mismos límites en los dos.
    [Theory]
    [InlineData("Development")]
    [InlineData("FakeDatos")]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void The_video_service_shares_the_database_and_the_video_limits_of_the_api(string environment)
    {
        var api = Load(environment);
        var videos = Load(environment, VideoService);

        Assert.Equal(api.GetConnectionString("Default"), videos.GetConnectionString("Default"));
        Assert.Equal(api.GetConnectionString("RealData"), videos.GetConnectionString("RealData"));
        foreach (var key in new[] { "Videos:Directory", "Videos:ChunkSizeMb", "Videos:MaxSizeMb", "Videos:AllowedContentTypes:0", "Videos:AllowedContentTypes:1" })
            Assert.Equal(api[key], videos[key]);
        Assert.True(videos.GetValue<bool>("VideoProcessing:Enabled"));
        Assert.Equal(1080, videos.GetValue<int>("VideoProcessing:MaxHeight"));
    }

    private const string Api = "TercerTiempo.Api";
    private const string VideoService = "TercerTiempo.Videos";

    private static IConfiguration Load(string environment, string project = Api)
    {
        var folder = ProjectFolder(project);
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(folder, "appsettings.json"))
            .AddJsonFile(Path.Combine(folder, $"appsettings.{environment}.json"))
            .Build();
    }

    private static string ProjectFolder(string project)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "src", project);
            if (File.Exists(Path.Combine(candidate, "appsettings.json"))) return candidate;
        }
        throw new DirectoryNotFoundException($"No se encuentra src/{project} subiendo desde la carpeta de los tests.");
    }

    private static JobScheduler Scheduler(IConfiguration configuration) => TestJobs.Scheduler(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        configuration.GetSection(FootballDataOptions.SectionName).Get<FootballDataOptions>()!,
        configuration.GetSection(OfficialSheetOptions.SectionName).Get<OfficialSheetOptions>()!,
        configuration.GetValue<bool>("RealData:Ingest"),
        configuration.GetSection(LicensingOptions.SectionName).Get<LicensingOptions>()!);
}
