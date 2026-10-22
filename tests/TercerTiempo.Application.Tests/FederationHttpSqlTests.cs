using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

public class FederationHttpSqlTests(FederationSqlFixture fixture) : IClassFixture<FederationSqlFixture>
{
    [LocalSqlFact]
    public async Task Successful_download_is_read_from_sql_after_restarting_the_client()
    {
        await fixture.Reset();
        var first = new FakeHttp(_ => Ok("<html>datos guardados</html>"));
        Assert.Contains("datos guardados", await fixture.Client(first).GetHtmlAsync("acta", default));
        var afterRestart = new FakeHttp(_ => throw new Exception("The provider must not be contacted twice"));
        Assert.Contains("datos guardados", await fixture.Client(afterRestart).GetHtmlAsync("acta", default));
        Assert.Equal(1, first.Count);
        Assert.Equal(0, afterRestart.Count);
        await using var db = fixture.Db();
        Assert.Equal(1, (await db.Set<FederationRequestState>().SingleAsync()).DayRequests);
    }

    [LocalSqlFact]
    public async Task Two_clients_requesting_the_same_page_only_make_one_http_call()
    {
        await fixture.Reset();
        var http = new FakeHttp(_ => Ok("<html>solo una descarga</html>"));
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            try { return await fixture.Client(http).GetHtmlAsync("misma-acta", default); }
            catch (ProviderThrottledException) { return null; } // Otro proceso ya tiene la descarga reservada.
        }));
        Assert.Contains(results, value => value?.Contains("solo una descarga") == true);
        Assert.Contains("solo una descarga", await fixture.Client(http).GetHtmlAsync("misma-acta", default));
        Assert.Equal(1, http.Count);
    }

    [LocalSqlFact]
    public async Task Provider_block_is_global_persistent_and_does_not_prevent_cached_reads()
    {
        await fixture.Reset();
        var saved = new FakeHttp(_ => Ok("<html>copia local</html>"));
        await fixture.Client(saved).GetHtmlAsync("guardada", default);
        await fixture.ClearSpacing();
        var blocked = new FakeHttp(_ => new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("rate limit"),
            Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(48)) },
        });
        var error = await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(blocked).GetHtmlAsync("nueva", default));
        Assert.Equal(1, blocked.Count);
        Assert.True(error.RetryAt > DateTimeOffset.UtcNow.AddHours(47));
        var restart = new FakeHttp(_ => throw new Exception("Global pause must survive restart"));
        await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(restart).GetBytesAsync("escudo.png", default));
        Assert.Contains("copia local", await fixture.Client(restart).GetHtmlAsync("guardada", default));
        Assert.Equal(0, restart.Count);
    }

    [LocalSqlFact]
    public async Task Forbidden_and_empty_pages_pause_without_immediate_retries()
    {
        foreach (var status in new[] { 403, 503, 200 })
        {
            await fixture.Reset();
            var http = new FakeHttp(_ => new((HttpStatusCode)status) { Content = new StringContent(status == 200 ? "  " : "denied") });
            await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(http).GetHtmlAsync("bloqueo", default));
            await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(http).GetHtmlAsync("otra", default));
            Assert.Equal(1, http.Count);
        }
    }

    [LocalSqlFact]
    public async Task Network_failures_and_404s_are_remembered_across_restarts()
    {
        await fixture.Reset();
        var failure = new FakeHttp(_ => throw new HttpRequestException("simulated network failure"));
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Client(failure).GetHtmlAsync("error", default));
        await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(failure).GetHtmlAsync("error", default));
        Assert.Equal(1, failure.Count);
        await fixture.ClearSpacing();
        var missing = new FakeHttp(_ => new(HttpStatusCode.NotFound));
        Assert.Null(await fixture.Client(missing).GetHtmlAsync("ausente", default));
        Assert.Null(await fixture.Client(missing).GetHtmlAsync("ausente", default));
        Assert.Equal(1, missing.Count);
    }

    [LocalSqlFact]
    public async Task Hourly_and_daily_budgets_and_leases_are_shared_by_processes()
    {
        await fixture.Reset();
        var options = FederationSqlFixture.Settings();
        options.Asturfutbol.MaxRequestsPerHour = 1;
        options.Asturfutbol.MaxRequestsPerDay = 2;
        var now = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);
        var store = fixture.Store();
        Assert.Null((await store.ReserveAsync("one", "https://federation.invalid/one", Guid.NewGuid(), false, now, options, default)).RetryAt);
        var samePage = await fixture.Store().ReserveAsync("one", "https://federation.invalid/one", Guid.NewGuid(), false, now, options, default);
        Assert.NotNull(samePage.RetryAt);
        var hourLimit = await fixture.Store().ReserveAsync("two", "https://federation.invalid/two", Guid.NewGuid(), false, now.AddMinutes(1), options, default);
        Assert.Equal(now.AddHours(1), hourLimit.RetryAt);
        Assert.Null((await fixture.Store().ReserveAsync("two", "https://federation.invalid/two", Guid.NewGuid(), false, now.AddHours(1), options, default)).RetryAt);
        var dayLimit = await fixture.Store().ReserveAsync("three", "https://federation.invalid/three", Guid.NewGuid(), false, now.AddHours(2), options, default);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), dayLimit.RetryAt);
    }

    [LocalSqlFact]
    public async Task Redirects_count_as_real_requests_and_external_hosts_are_never_followed()
    {
        await fixture.Reset();
        var http = new FakeHttp(request => request.RequestUri!.AbsolutePath == "/initial"
            ? new(HttpStatusCode.Found) { Headers = { Location = new Uri("/final", UriKind.Relative) } }
            : Ok("<html>final</html>"));
        Assert.Contains("final", await fixture.Client(http).GetHtmlAsync("initial", default));
        Assert.Equal(2, http.Count);
        await using var db = fixture.Db();
        Assert.Equal(2, (await db.Set<FederationRequestState>().SingleAsync()).DayRequests);
        await fixture.Reset();
        var external = new FakeHttp(_ => new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://other.invalid/") } });
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Client(external).GetHtmlAsync("redirect", default));
        Assert.Equal(1, external.Count);
    }

    [LocalSqlFact]
    public async Task Disabling_downloads_still_serves_saved_pages_without_http()
    {
        await fixture.Reset();
        var http = new FakeHttp(_ => Ok("<html>almacenada</html>"));
        await fixture.Client(http).GetHtmlAsync("saved", default);
        var settings = FederationSqlFixture.Settings();
        settings.Enabled = false;
        Assert.Contains("almacenada", await fixture.Client(http, settings).GetHtmlAsync("saved", default));
        await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(http, settings).GetHtmlAsync("missing", default));
        Assert.Equal(1, http.Count);
    }

    private static HttpResponseMessage Ok(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    internal sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Count++; return Task.FromResult(response(request)); }
        protected override void Dispose(bool disposing) { } // Cada cliente es efímero; se comparte el contador de la prueba.
    }
}

public sealed class FederationSqlFixture : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private DbContextOptions<AppDbContext> _options = null!;
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("TT_RUN_LOCAL_SQL_TESTS") != "1") return;
        var name = $"TercerTiempo_FederationVerification_{Guid.NewGuid():N}";
        var connection = $"Server=localhost;Database={name};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10";
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection, sql => sql.EnableRetryOnFailure()).Options;
        _services = new ServiceCollection().AddDbContext<AppDbContext>(o => o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()))
            .BuildServiceProvider();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }
    public AppDbContext Db() => new(_options);
    internal SqlAsturfutbolRequestStore Store() => new(_services.GetRequiredService<IServiceScopeFactory>());
    internal static OfficialSheetOptions Settings() => new() { Asturfutbol = new() { RequestDelayMs = 1000 } };
    internal AsturfutbolClient Client(HttpMessageHandler http, OfficialSheetOptions? settings = null) => new(
        new HttpClient(http, disposeHandler: false) { BaseAddress = new Uri("https://federation.invalid/") },
        Options.Create(settings ?? Settings()), NullLogger<AsturfutbolClient>.Instance, Store(), new AsturfutbolRequestGate(), TimeProvider.System);
    public async Task Reset()
    {
        await using var db = Db();
        await db.Set<FederationHttpCache>().ExecuteDeleteAsync();
        await db.Set<FederationRequestState>().ExecuteDeleteAsync();
    }
    public async Task ClearSpacing()
    {
        await using var db = Db();
        await db.Set<FederationRequestState>().ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRequestAt, DateTimeOffset.MinValue));
    }
    public async Task DisposeAsync()
    {
        if (_options is null) return;
        await using var db = Db();
        await db.Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }
}
