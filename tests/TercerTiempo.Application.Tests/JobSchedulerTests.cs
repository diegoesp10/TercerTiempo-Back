using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

public class JobSchedulerTests(JobSqlFixture fixture) : IClassFixture<JobSqlFixture>
{
    [LocalSqlFact]
    public async Task Restarting_waits_for_the_interval_since_the_last_saved_run()
    {
        await fixture.Reset();
        var lastStart = DateTimeOffset.UtcNow.AddMinutes(-10);
        await fixture.AddRun(JobNames.FootballData, lastStart, JobRunStatus.Succeeded);

        var due = await fixture.Scheduler().FirstDueAsync(JobNames.FootballData, default);

        Assert.Equal(lastStart.AddMinutes(60), due!.Value, TimeSpan.FromSeconds(1));
    }

    [LocalSqlFact]
    public async Task Without_history_the_first_run_comes_shortly_after_starting()
    {
        await fixture.Reset();

        var due = await fixture.Scheduler().FirstDueAsync(JobNames.FootballData, default);

        Assert.InRange(due!.Value, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(15));
    }

    [LocalSqlFact]
    public async Task Runs_left_running_by_a_stopped_api_are_marked_interrupted()
    {
        await fixture.Reset();
        await fixture.AddRun(JobNames.OfficialSheets, DateTimeOffset.UtcNow.AddMinutes(-5), JobRunStatus.Running);

        await fixture.Scheduler().FirstDueAsync(JobNames.OfficialSheets, default);

        await using var db = fixture.Db();
        Assert.Equal(JobRunStatus.Interrupted, (await db.Set<JobRun>().SingleAsync()).Status);
    }

    [LocalSqlFact]
    public async Task External_calls_are_logged_and_counted_for_the_run_that_made_them()
    {
        await fixture.Reset();
        var scheduler = fixture.Scheduler();
        var http = fixture.LoggedClient("futbolasturiano");

        await http.GetAsync("https://example.invalid/fuera-de-ejecucion");
        await scheduler.RunAsync(JobNames.FootballData, manual: true, async ct =>
        {
            await http.GetAsync("https://example.invalid/liga-1", ct);
            await http.GetAsync("https://example.invalid/liga-2", ct);
            return new JobOutcome("2 ligas", []);
        }, default);

        await using var db = fixture.Db();
        var run = await db.Set<JobRun>().SingleAsync();
        Assert.True(run.Manual);
        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal("2 ligas", run.Summary);
        Assert.Equal(2, run.Requests);
        var requests = await db.Set<ScrapeRequest>().OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(3, requests.Count);
        Assert.Null(requests[0].JobRunId);
        Assert.All(requests.Skip(1), r => Assert.Equal(run.Id, r.JobRunId));
        Assert.All(requests, r => Assert.Equal(200, r.StatusCode));
    }

    [LocalSqlFact]
    public async Task A_failed_run_is_saved_with_its_error()
    {
        await fixture.Reset();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Scheduler().RunAsync(
            JobNames.FootballData, manual: false, _ => throw new InvalidOperationException("sin red"), default));

        await using var db = fixture.Db();
        var run = await db.Set<JobRun>().SingleAsync();
        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Equal("sin red", run.Errors);
        Assert.NotNull(run.FinishedAt);
    }

    [LocalSqlFact]
    public async Task Launching_by_hand_wakes_the_job_before_its_time_but_not_twice_while_running()
    {
        await fixture.Reset();
        var scheduler = fixture.Scheduler();
        var waiting = scheduler.WaitAsync(JobNames.FootballData, DateTimeOffset.UtcNow.AddHours(1), default);

        Assert.Equal(JobLaunchResult.Accepted, scheduler.Launch(JobNames.FootballData));
        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));

        var release = new TaskCompletionSource();
        var running = scheduler.RunAsync(JobNames.FootballData, true, async _ => { await release.Task; return new JobOutcome("ok", []); }, default);
        Assert.Equal(JobLaunchResult.AlreadyRunning, scheduler.Launch(JobNames.FootballData));
        release.SetResult();
        await running;

        Assert.Equal(JobLaunchResult.NotFound, scheduler.Launch("no-existe"));
    }
}

public class RunJobHandlerTests
{
    [Theory]
    [InlineData(JobLaunchResult.NotFound, typeof(NotFoundException))]
    [InlineData(JobLaunchResult.AlreadyRunning, typeof(ConflictException))]
    [InlineData(JobLaunchResult.Disabled, typeof(ConflictException))]
    public async Task Launch_problems_become_coded_errors(JobLaunchResult result, Type expected)
    {
        var handler = new RunJobHandler(new FakeMonitor(result));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(new RunJobCommand(JobNames.FootballData), default));

        Assert.IsType(expected, error);
    }

    [Fact]
    public async Task Accepted_launch_returns_the_job_state()
    {
        var handler = new RunJobHandler(new FakeMonitor(JobLaunchResult.Accepted));

        var job = await handler.Handle(new RunJobCommand(JobNames.FootballData), default);

        Assert.Equal(JobNames.FootballData, job.Name);
    }

    private sealed class FakeMonitor(JobLaunchResult result) : IJobsMonitor
    {
        public JobLaunchResult Launch(string job) => result;

        public Task<JobsOverviewDto> GetOverviewAsync(string? provider, int requests, CancellationToken cancellationToken) =>
            Task.FromResult(new JobsOverviewDto(
                [new JobDto(JobNames.FootballData, "Datos", "", 60, true, true, null, null, null)], [], [], [], DateTimeOffset.UtcNow));
    }
}

public sealed class JobSqlFixture : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private DbContextOptions<AppDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("TT_RUN_LOCAL_SQL_TESTS") != "1") return;
        var name = $"TercerTiempo_JobsVerification_{Guid.NewGuid():N}";
        var connection = $"Server=localhost;Database={name};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10";
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        _services = new ServiceCollection().AddDbContext<AppDbContext>(o => o.UseSqlServer(connection)).BuildServiceProvider();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public AppDbContext Db() => new(_options);

    internal JobScheduler Scheduler() => new(
        Options.Create(new FootballDataOptions { SyncIntervalMinutes = 60 }),
        Options.Create(new OfficialSheetOptions { BackgroundScanMinutes = 60, BackgroundStartDelaySeconds = 5 }),
        Options.Create(new LicensingOptions()),
        _services.GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        NullLogger<JobScheduler>.Instance);

    internal HttpClient LoggedClient(string provider) => new(new ScrapeLogHandler(
        provider, _services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<ScrapeLogHandler>.Instance)
    {
        InnerHandler = new OkHandler(),
    });

    internal async Task AddRun(string job, DateTimeOffset startedAt, string status)
    {
        await using var db = Db();
        db.Add(new JobRun { Id = Guid.CreateVersion7(), Job = job, Status = status, StartedAt = startedAt });
        await db.SaveChangesAsync();
    }

    public async Task Reset()
    {
        await using var db = Db();
        await db.Set<ScrapeRequest>().ExecuteDeleteAsync();
        await db.Set<JobRun>().ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        if (_options is null) return;
        await using var db = Db();
        await db.Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
    }
}
