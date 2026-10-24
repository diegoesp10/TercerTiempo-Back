using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sync;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

namespace TercerTiempo.Application.Tests;

public class FootballSyncScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 24);

    // ------------------------------------------------------------------ pasada automática

    [Fact]
    public void Scheduled_runs_do_the_whole_season_first_and_then_only_the_current_round_until_it_is_due_again()
    {
        var settings = FootballSyncSettings.CreateDefault(Now);

        Assert.Equal(SyncScopeKind.FullSeason, FootballSyncPlanner.Scheduled(settings, null, Now).Kind);

        var round = FootballSyncPlanner.Scheduled(settings, Now.AddDays(-2), Now);
        Assert.Equal(SyncScopeKind.CurrentRound, round.Kind);
        Assert.Equal(new MatchWindow(Today.AddDays(-3), Today.AddDays(4)), round.Window);
        Assert.False(round.IncludeRosters);
        Assert.Empty(round.CompetitionIds);

        var full = FootballSyncPlanner.Scheduled(settings, Now.AddDays(-7), Now);
        Assert.Equal(SyncScopeKind.FullSeason, full.Kind);
        Assert.True(full.IncludeRosters);
        Assert.Null(full.Window);
        Assert.Equal(Now.AddDays(5), FootballSyncPlanner.NextFullSyncAt(settings, Now.AddDays(-2)));
    }

    [Fact]
    public void Without_automatic_full_runs_the_schedule_always_takes_the_round_and_full_season_settings_always_the_season()
    {
        var settings = FootballSyncSettings.CreateDefault(Now);
        settings.Update(SyncScopeKind.CurrentRound, 1, 2, true, 0, Now);

        var round = FootballSyncPlanner.Scheduled(settings, null, Now);
        Assert.Equal(new MatchWindow(Today.AddDays(-1), Today.AddDays(2)), round.Window);
        Assert.True(round.IncludeRosters);
        Assert.Null(FootballSyncPlanner.NextFullSyncAt(settings, Now));

        settings.Update(SyncScopeKind.FullSeason, 1, 2, false, 0, Now);
        Assert.Equal(SyncScopeKind.FullSeason, FootballSyncPlanner.Scheduled(settings, Now, Now).Kind);
    }

    [Fact]
    public void Each_scope_describes_itself_for_the_run_history()
    {
        var settings = FootballSyncSettings.CreateDefault(Now);

        Assert.Equal("Jornada en curso (21 oct – 28 oct)", SyncScope.Round(settings, Today).Describe());
        Assert.Equal("Del 1 oct al 15 oct · 2 ligas · con plantillas",
            new SyncScope(SyncScopeKind.DateRange, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15), [Guid.NewGuid(), Guid.NewGuid()], true).Describe());
        Assert.Equal("Temporada completa · sin plantillas", SyncScope.FullSeason(includeRosters: false).Describe());
    }

    // ------------------------------------------------------------------ lanzar a mano

    [Fact]
    public async Task Launching_by_hand_passes_the_chosen_scope_to_the_job()
    {
        var league = Competition.Create("1ª AsturFútbol", "Senior", CompetitionLevel.Regional, 5, null, "Asturias", ExternalRef.Create("futbolasturiano", "164"));
        var monitor = new CapturingMonitor();
        var handler = Handler(monitor, league);

        await handler.Handle(new RunFootballSyncCommand(SyncScopeKind.CurrentRound, null, null, [league.Id], null), default);
        var round = Assert.IsType<SyncScope>(monitor.Parameters);
        Assert.Equal(new MatchWindow(Today.AddDays(-3), Today.AddDays(4)), round.Window);
        Assert.Equal([league.Id], round.CompetitionIds);
        Assert.False(round.IncludeRosters);

        await handler.Handle(new RunFootballSyncCommand(SyncScopeKind.DateRange, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null), default);
        var range = Assert.IsType<SyncScope>(monitor.Parameters);
        Assert.Equal(new MatchWindow(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), range.Window);
        Assert.False(range.IncludeRosters);

        await handler.Handle(new RunFootballSyncCommand(SyncScopeKind.FullSeason, null, null, [], null), default);
        Assert.True(Assert.IsType<SyncScope>(monitor.Parameters).IncludeRosters);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RunFootballSyncCommand(SyncScopeKind.FullSeason, null, null, [Guid.NewGuid()], null), default));
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-01")]
    [InlineData("2025-10-01", "2026-10-15")]
    [InlineData(null, "2026-10-15")]
    public void Date_ranges_need_both_dates_in_order_and_at_most_a_year(string? from, string? to)
    {
        var command = new RunFootballSyncCommand(
            SyncScopeKind.DateRange, from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), null, null);

        Assert.False(new RunFootballSyncValidator().Validate(command).IsValid);
    }

    // ------------------------------------------------------------------ futbolasturiano.es con ventana de fechas

    [Fact]
    public async Task With_a_window_only_those_days_are_requested_and_only_known_hidden_matches_about_to_start_without_result_are_reread()
    {
        var http = new SiteStub();
        var provider = new FutbolAsturianoFootballDataProvider(
            new HttpClient(http) { BaseAddress = new Uri("https://www.futbolasturiano.es/wp-json/") },
            Options.Create(new FootballDataOptions { FutbolAsturiano = new FutbolAsturianoOptions { RequestDelayMs = 0 } }),
            new FixedClock(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<FutbolAsturianoFootballDataProvider>.Instance);

        MatchSnapshot Known(string id, DateTimeOffset kickoff, MatchStatus status = MatchStatus.Scheduled) =>
            new(id, 6, "10", "11", kickoff, status, null, null, null);
        var known = new[]
        {
            Known("500", new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
            Known("600", new DateTimeOffset(2026, 10, 17, 14, 0, 0, TimeSpan.Zero)),
            Known("601", new DateTimeOffset(2026, 11, 1, 16, 0, 0, TimeSpan.Zero)),
            Known("602", new DateTimeOffset(2026, 10, 18, 16, 0, 0, TimeSpan.Zero)),
            Known("603", new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.Zero), MatchStatus.Played),
        };

        var matches = await provider.GetMatchesAsync("164", known, new MatchWindow(new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 19)), default);

        Assert.Equal(["500", "600"], matches.Select(m => m.ExternalId).Order());
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero), matches.Single(m => m.ExternalId == "600").KickoffAt);
        Assert.DoesNotContain(http.Requested, u => u.Contains("p=602") || u.Contains("p=603"));

        var events = Assert.Single(http.Requested, u => u.Contains("sp_event"));
        Assert.Contains("after=2026-10-15T00:00:00&before=2026-10-21T00:00:00", events);
        Assert.Contains(http.Requested, u => u.EndsWith("/?p=600"));
        Assert.DoesNotContain(http.Requested, u => u.Contains("p=601"));
    }

    private static RunFootballSyncHandler Handler(CapturingMonitor monitor, params Competition[] leagues)
    {
        var competitions = Stub<ICompetitionStore>((method, _) => method == nameof(ICompetitionStore.GetAllAsync)
            ? Task.FromResult<IReadOnlyList<Competition>>(leagues)
            : throw new Exception($"Unexpected call: {method}"));
        var store = Stub<IFootballSyncSettingsStore>((method, _) => method == nameof(IFootballSyncSettingsStore.GetAsync)
            ? Task.FromResult<FootballSyncSettings?>(null)
            : throw new Exception($"Unexpected call: {method}"));
        var clock = new FixedClock(Now);

        return new RunFootballSyncHandler(new FootballSyncSettingsReader(store, competitions, monitor, clock), competitions, monitor, clock);
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingMonitor : IJobsMonitor
    {
        public object? Parameters { get; private set; }

        public JobLaunchResult Launch(string job, object? parameters = null)
        {
            Parameters = parameters;
            return JobLaunchResult.Accepted;
        }

        public Task<DateTimeOffset?> LastFullFootballSyncAsync(CancellationToken cancellationToken) => Task.FromResult<DateTimeOffset?>(null);

        public Task<JobsOverviewDto> GetOverviewAsync(string? provider, int requests, CancellationToken cancellationToken) =>
            Task.FromResult(new JobsOverviewDto(
                [new JobDto(JobNames.FootballData, "Datos", "", 60, true, true, null, null, null)], [], [], [], DateTimeOffset.UtcNow));
    }

    // La web de futbolasturiano.es para una liga: dos partidos visibles en la API (uno fuera de la ventana pedida, dentro
    // del día de margen) y la página del partido oculto 600.
    private sealed class SiteStub : HttpMessageHandler
    {
        private const string HiddenPage = """
            <h1>SD Lenense vs Real Sporting &#8216;C&#8217;</h1>
            <div class="sp-template sp-template-event-logos sp-template-event-logos-inline"><div class="sp-event-logos sp-event-logos-2">
              <span class="sp-team-logo"><a href="https://www.futbolasturiano.es/equipo/sd-lenense/"><strong class="sp-team-name">SD Lenense</strong></a></span>
              vs
              <span class="sp-team-logo"><a href="https://www.futbolasturiano.es/equipo/sporting-c/"><strong class="sp-team-name">Real Sporting &#8216;C&#8217;</strong></a></span>
            </div></div>
            <table class="sp-event-details sp-data-table sp-scrollable-table">
              <thead><tr><th>Fecha</th><th>Hora</th><th>Liga</th><th>Temporada</th><th>Jornada</th><th>Tiempo completo</th></tr></thead>
              <tbody><tr class="odd"><td>17/10/2026</td><td>18:00</td><td>1ª AsturFútbol</td><td>2026/2027</td><td>6</td><td>90'</td></tr></tbody>
            </table>
            """;

        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            var body = url switch
            {
                _ when url.Contains("v2/sp_season?") => """[{"id":403,"name":"2026/2027"}]""",
                _ when url.Contains("v2/sp_venue?") => "[]",
                _ when url.Contains("v2/sp_event?") => """
                    [{"id":500,"date":"2026-10-18T12:00:00","day":6,"teams":[10,11]},
                     {"id":501,"date":"2026-10-20T17:00:00","day":6,"teams":[12,10]}]
                    """,
                _ when url.Contains("sp_league/164") => """{"id":164,"name":"1ª AsturFútbol"}""",
                _ when url.Contains("v2/sp_team?") => """
                    [{"id":10,"slug":"sd-lenense","title":{"rendered":"SD Lenense"}},
                     {"id":11,"slug":"sporting-c","title":{"rendered":"Real Sporting C"}},
                     {"id":12,"slug":"ud-llanera","title":{"rendered":"UD Llanera"}}]
                    """,
                _ when url.EndsWith("/?p=600") => HiddenPage,
                _ => null,
            };

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }
}
