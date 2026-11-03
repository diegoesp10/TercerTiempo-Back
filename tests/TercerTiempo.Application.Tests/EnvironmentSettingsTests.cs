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

    [Theory]
    [InlineData("Development")]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void Outside_fake_data_the_jobs_that_read_the_web_run_on_their_own(string environment)
    {
        var scheduler = Scheduler(Load(environment));

        Assert.All(JobsThatReadTheWeb, job => Assert.True(scheduler.Get(job).Enabled && scheduler.Get(job).Interval is not null, job));
    }

    [Fact]
    public void Fake_data_uses_its_own_database_and_never_reads_the_web_on_its_own()
    {
        var configuration = Load("FakeDatos");
        var scheduler = Scheduler(configuration);

        Assert.Contains("Database=tercertiempo_DatosFake;", configuration.GetConnectionString("Default"));
        Assert.False(configuration.GetValue<bool>("OfficialSheets:Enabled"));
        Assert.All(JobsThatReadTheWeb, job => Assert.True(!scheduler.Get(job).Enabled || scheduler.Get(job).Interval is null, job));
    }

    [Fact]
    public void The_pilot_limits_requests_by_the_ip_the_tunnel_sends() =>
        Assert.True(Load("Pilot").GetValue<bool>("Throttling:TrustForwardedHeaders"));

    private static IConfiguration Load(string environment)
    {
        var api = ApiFolder();
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(api, "appsettings.json"))
            .AddJsonFile(Path.Combine(api, $"appsettings.{environment}.json"))
            .Build();
    }

    private static string ApiFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var api = Path.Combine(folder.FullName, "src", "TercerTiempo.Api");
            if (File.Exists(Path.Combine(api, "appsettings.json"))) return api;
        }
        throw new DirectoryNotFoundException("No se encuentra src/TercerTiempo.Api subiendo desde la carpeta de los tests.");
    }

    private static JobScheduler Scheduler(IConfiguration configuration) => new(
        Options.Create(configuration.GetSection(FootballDataOptions.SectionName).Get<FootballDataOptions>()!),
        Options.Create(configuration.GetSection(OfficialSheetOptions.SectionName).Get<OfficialSheetOptions>()!),
        Options.Create(configuration.GetSection(LicensingOptions.SectionName).Get<LicensingOptions>()!),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        new JobProgress(TimeProvider.System),
        TimeProvider.System,
        NullLogger<JobScheduler>.Instance);
}
