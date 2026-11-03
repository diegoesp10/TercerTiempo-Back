using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.RealData;

namespace TercerTiempo.Application.Tests;

internal static class TestJobs
{
    // ingest: el entorno puede descargar y además tiene el bloqueo (es el que descarga ahora).
    public static JobScheduler Scheduler(
        IServiceScopeFactory scopes,
        FootballDataOptions football,
        OfficialSheetOptions sheets,
        bool ingest = true,
        LicensingOptions? licensing = null) => new(
        Options.Create(football),
        Options.Create(sheets),
        Options.Create(licensing ?? new LicensingOptions()),
        Options.Create(new RealDataOptions { Ingest = ingest }),
        new Lease(ingest, ingest),
        new TestEnvironment(),
        scopes,
        new JobProgress(TimeProvider.System),
        TimeProvider.System,
        NullLogger<JobScheduler>.Instance);

    // Puede descargar, pero ahora descarga otro entorno.
    public static JobScheduler SchedulerWhileAnotherIngests(IServiceScopeFactory scopes) => new(
        Options.Create(new FootballDataOptions { SyncIntervalMinutes = 60 }),
        Options.Create(new OfficialSheetOptions()),
        Options.Create(new LicensingOptions()),
        Options.Create(new RealDataOptions { Ingest = true }),
        new Lease(true, false),
        new TestEnvironment(),
        scopes,
        new JobProgress(TimeProvider.System),
        TimeProvider.System,
        NullLogger<JobScheduler>.Instance);

    private sealed class Lease(bool canIngest, bool held) : IRealDataLease
    {
        public bool CanIngest => canIngest;
        public bool IsHeld => held;
        public Task WhenHeldAsync(CancellationToken cancellationToken) => held ? Task.CompletedTask : Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "TercerTiempo.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
