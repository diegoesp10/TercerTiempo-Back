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
    public async Task Errors_pause_the_provider_and_only_empty_pages_are_retried_first()
    {
        // 403 y 503 pausan a la primera; una página vacía se repite una vez con sesión nueva antes de pausar.
        foreach (var (status, calls) in new[] { (403, 1), (503, 1), (200, 2) })
        {
            await fixture.Reset();
            var http = new FakeHttp(_ => new((HttpStatusCode)status) { Content = new StringContent(status == 200 ? "  " : "denied") });
            await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(http).GetHtmlAsync("bloqueo", default));
            await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(http).GetHtmlAsync("otra", default));
            Assert.Equal(calls, http.Count);
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
        Assert.Null((await store.ReserveAsync("one", "https://federation.invalid/one", Guid.NewGuid(), false, now, options.AsturfutbolLimits, default)).RetryAt);
        var samePage = await fixture.Store().ReserveAsync("one", "https://federation.invalid/one", Guid.NewGuid(), false, now, options.AsturfutbolLimits, default);
        Assert.NotNull(samePage.RetryAt);
        var hourLimit = await fixture.Store().ReserveAsync("two", "https://federation.invalid/two", Guid.NewGuid(), false, now.AddMinutes(1), options.AsturfutbolLimits, default);
        Assert.Equal(now.AddHours(1), hourLimit.RetryAt);
        Assert.Null((await fixture.Store().ReserveAsync("two", "https://federation.invalid/two", Guid.NewGuid(), false, now.AddHours(1), options.AsturfutbolLimits, default)).RetryAt);
        var dayLimit = await fixture.Store().ReserveAsync("three", "https://federation.invalid/three", Guid.NewGuid(), false, now.AddHours(2), options.AsturfutbolLimits, default);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), dayLimit.RetryAt);
    }

    [LocalSqlFact]
    public async Task Requests_go_in_bursts_with_a_rest_and_a_new_session_between_them()
    {
        await fixture.Reset();
        var options = FederationSqlFixture.Settings();
        options.Asturfutbol.MaxRequestsPerBurst = 3;
        options.Asturfutbol.BurstRestMinutes = 40;
        var limits = options.AsturfutbolLimits;
        var now = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
        var store = fixture.Store();

        var epochs = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var page = await store.ReserveAsync($"p{i}", $"https://federation.invalid/p{i}", Guid.NewGuid(), false, now.AddSeconds(30 * i), limits, default);
            Assert.Null(page.RetryAt);
            epochs.Add(page.SessionEpoch);
        }
        Assert.Single(epochs.Distinct());

        var rest = await store.ReserveAsync("p3", "https://federation.invalid/p3", Guid.NewGuid(), false, now.AddSeconds(90), limits, default);
        Assert.Equal(now.AddSeconds(90).AddMinutes(40), rest.RetryAt);
        await using (var db = fixture.Db())
            Assert.Equal(PauseKinds.Rest, (await db.Set<FederationRequestState>().SingleAsync()).PauseKind);

        var next = await store.ReserveAsync("p3", "https://federation.invalid/p3", Guid.NewGuid(), false, now.AddMinutes(42), limits, default);
        Assert.Null(next.RetryAt);
        Assert.NotEqual(epochs[0], next.SessionEpoch);

        // Tanto tiempo sin pedir nada como un descanso también abre tanda y sesión nuevas.
        var later = await store.ReserveAsync("p4", "https://federation.invalid/p4", Guid.NewGuid(), false, now.AddHours(3), limits, default);
        Assert.Null(later.RetryAt);
        Assert.NotEqual(next.SessionEpoch, later.SessionEpoch);
    }

    [LocalSqlFact]
    public async Task A_stray_empty_page_is_retried_with_a_new_session_and_nothing_pauses()
    {
        await fixture.Reset();
        var calls = 0;
        var session = new AsturfutbolSession();
        var http = new FakeHttp(_ => ++calls == 1 ? Ok("  ") : Ok("<html>acta</html>"));
        session.Cookies.Add(new Uri("https://federation.invalid/"), new Cookie("JSESSIONID", "vieja"));

        Assert.Contains("acta", await fixture.Client(http, session: session).GetHtmlAsync("acta", default));
        Assert.Equal(2, calls);
        Assert.Empty(session.Cookies.GetCookies(new Uri("https://federation.invalid/")));
        await using var db = fixture.Db();
        var state = await db.Set<FederationRequestState>().SingleAsync();
        Assert.Null(state.PausedUntil);
        Assert.Equal(0, state.RejectionStreak);
    }

    [LocalSqlFact]
    public async Task Pages_that_stay_empty_pause_briefly_and_the_pause_grows_until_one_works()
    {
        await fixture.Reset();
        var empty = new FakeHttp(_ => Ok("<html><body>No se ha aceptado el cookie</body></html>"));

        var first = await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(empty).GetHtmlAsync("acta", default));
        Assert.Equal(2, empty.Count);
        Assert.InRange(first.RetryAt!.Value, DateTimeOffset.UtcNow.AddMinutes(29), DateTimeOffset.UtcNow.AddMinutes(31));
        await using (var db = fixture.Db())
        {
            var state = await db.Set<FederationRequestState>().SingleAsync();
            Assert.Equal(PauseKinds.Retry, state.PauseKind);
            Assert.Contains("sin insistir", state.PauseReason);
        }

        await fixture.EndPause();
        var second = await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(empty).GetHtmlAsync("acta", default));
        Assert.InRange(second.RetryAt!.Value, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));
        await using (var db = fixture.Db())
            Assert.Contains("2 intentos seguidos", (await db.Set<FederationRequestState>().SingleAsync()).PauseReason);

        await fixture.EndPause();
        Assert.Contains("acta", await fixture.Client(new FakeHttp(_ => Ok("<html>acta</html>"))).GetHtmlAsync("acta", default));
        await using (var db = fixture.Db())
            Assert.Equal(0, (await db.Set<FederationRequestState>().SingleAsync()).RejectionStreak);
    }

    [LocalSqlFact]
    public async Task An_explicit_block_page_is_a_block_without_retries()
    {
        await fixture.Reset();
        var http = new FakeHttp(_ => Ok("<html><head><title>Access Denied</title></head></html>"));
        var blocked = await Assert.ThrowsAsync<ProviderThrottledException>(() => fixture.Client(http).GetHtmlAsync("acta", default));
        Assert.Equal(1, http.Count);
        Assert.True(blocked.RetryAt > DateTimeOffset.UtcNow.AddHours(23));
        await using var db = fixture.Db();
        Assert.Equal(PauseKinds.Blocked, (await db.Set<FederationRequestState>().SingleAsync()).PauseKind);
    }

    [Theory]
    [InlineData("1ª RFEF", false)]
    [InlineData("2ª RFEF", false)]
    [InlineData("3ª RFEF", true)]
    [InlineData("2ª AsturFútbol", true)]
    public void The_federation_only_publishes_its_own_leagues(string category, bool expected)
    {
        var provider = new AsturfutbolOfficialSheetProvider(null!, Options.Create(new OfficialSheetOptions()), NullLogger<AsturfutbolOfficialSheetProvider>.Instance);
        Assert.Equal(expected, provider.Publishes(category));
    }

    [Fact]
    public void A_new_session_epoch_drops_the_cookies()
    {
        var session = new AsturfutbolSession();
        var site = new Uri("https://www.asturfutbol.es/pnfg/");
        session.Use(0);
        session.Cookies.Add(site, new Cookie("JSESSIONID", "abc"));
        session.Served();
        session.Use(0);
        Assert.Single(session.Cookies.GetCookies(site));
        session.Use(1);
        Assert.Empty(session.Cookies.GetCookies(site));
        Assert.Equal(0, session.PagesServed);
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
    internal static OfficialSheetOptions Settings() => new() { Asturfutbol = new() { RequestDelayMs = 1000, RejectionRetrySeconds = 0 } };
    internal AsturfutbolClient Client(HttpMessageHandler http, OfficialSheetOptions? settings = null, AsturfutbolSession? session = null)
    {
        session ??= new AsturfutbolSession();
        return new(
            new HttpClient(http, disposeHandler: false) { BaseAddress = new Uri("https://federation.invalid/") },
            Options.Create(settings ?? Settings()), NullLogger<AsturfutbolClient>.Instance, Store(), new AsturfutbolRequestGate(), session, TimeProvider.System);
    }
    public async Task Reset()
    {
        await using var db = Db();
        await db.Set<FederationHttpCache>().ExecuteDeleteAsync();
        await db.Set<FederationRequestState>().ExecuteDeleteAsync();
    }
    public async Task EndPause()
    {
        await using var db = Db();
        await db.Set<FederationRequestState>().ExecuteUpdateAsync(s => s
            .SetProperty(x => x.PausedUntil, (DateTimeOffset?)null)
            .SetProperty(x => x.NextRequestAt, DateTimeOffset.MinValue));
        await db.Set<FederationHttpCache>().ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, (DateTimeOffset?)null));
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
