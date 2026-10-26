using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

public class FederationDatabaseFirstTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Completed_sheet_is_never_due_even_years_later()
    {
        var (match, sheet) = CompletedSheet();
        Assert.False(OfficialSheetRefresh.IsDue(sheet, match, Now.AddYears(5)));
    }

    [Fact]
    public void Pending_sheet_is_checked_at_most_daily()
    {
        var match = Match.Schedule(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", "123", null, Now);
        Assert.False(OfficialSheetRefresh.IsDue(sheet, match, Now.AddHours(23)));
        Assert.True(OfficialSheetRefresh.IsDue(sheet, match, Now.AddHours(24)));
    }

    [Fact]
    public async Task Reading_a_saved_sheet_never_queues_or_contacts_the_provider()
    {
        var (match, sheet) = CompletedSheet();
        var handler = new GetOfficialSheetHandler(
            Stub<IOfficialSheetProvider>((method, _) => throw new Exception($"Unexpected provider call: {method}")),
            Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(match)),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<OfficialMatchSheet?>(sheet)),
            Stub<IOfficialSheetQueue>((method, _) => throw new Exception($"Unexpected queue call: {method}")),
            new OfficialSheetSyncMemory(), TimeProvider.System);
        var result = await handler.Handle(new(match.Id), default);
        Assert.Equal(OfficialSheetAvailability.Available, result.Status);
        Assert.NotEmpty(result.Home!.Starters);
    }

    [Fact]
    public async Task Even_a_legacy_force_command_cannot_download_an_existing_complete_sheet()
    {
        var (match, sheet) = CompletedSheet();
        var handler = new SyncOfficialSheetHandler(null!, Options.Create(new PublicSheetOptions()),
            Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(match)), null!,
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<OfficialMatchSheet?>(sheet)),
            null!, new OfficialSheetSyncMemory(), TimeProvider.System, NullLogger<SyncOfficialSheetHandler>.Instance);
        var result = await handler.Handle(new(match.Id, Force: true), default);
        Assert.Equal(OfficialSheetAvailability.Available, result.Status);
    }

    [Fact]
    public async Task Background_scan_with_all_data_saved_does_not_resolve_federation_catalogs()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var (match, sheet) = CompletedSheet(season.Id);
        var handler = new PlanOfficialSheetSyncHandler(
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition])),
            Stub<IMatchStore>((_, _) => Task.FromResult<IReadOnlyList<Match>>([match])),
            Stub<ISquadStore>((_, _) => Task.FromResult<IReadOnlyList<Squad>>([Squad.Create(match.HomeTeamId, season.Id, Guid.NewGuid(), Now)])),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([sheet])),
            AllSources, Options.Create(new PublicSheetOptions()), new OfficialSheetSyncMemory(), TimeProvider.System);
        Assert.Empty(await handler.Handle(new(20), default));
    }

    [Fact]
    public async Task Background_scan_puts_squad_matches_first_and_keeps_the_federation_for_them()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var ours = Guid.NewGuid();
        var withSquad = Match.Schedule(season.Id, 1, Guid.NewGuid(), ours, Now.AddDays(-3), null, null);
        var others = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        var handler = new PlanOfficialSheetSyncHandler(
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition])),
            Stub<IMatchStore>((_, _) => Task.FromResult<IReadOnlyList<Match>>([withSquad, others])),
            Stub<ISquadStore>((_, _) => Task.FromResult<IReadOnlyList<Squad>>([Squad.Create(ours, season.Id, Guid.NewGuid(), Now)])),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([])),
            AllSources, Options.Create(new PublicSheetOptions { Sources = ["otra-web", "asturfutbol"] }), new OfficialSheetSyncMemory(), TimeProvider.System);
        Assert.Equal([new PlannedSheet(withSquad.Id, true), new PlannedSheet(others.Id, false)], await handler.Handle(new(20), default));
    }

    [Fact]
    public void Sheets_from_a_source_removed_from_the_configuration_are_hidden()
    {
        var closedBeta = new PublicSheetOptions { Sources = ["asturfutbol"] };
        Assert.True(closedBeta.Allows("asturfutbol"));
        Assert.False(closedBeta.Allows("otra-web"));
        Assert.False(new PublicSheetOptions().Allows("otra-web"));
        Assert.True(new PublicSheetOptions().Allows("asturfutbol"));
    }

    [Fact]
    public async Task A_complete_sheet_from_a_disabled_source_is_not_served_and_is_searched_again()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        var match = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-3), null, null);
        match.RecordResult(2, 1, Now.AddDays(-3).AddHours(2));
        var hidden = OfficialMatchSheet.Create(match.Id, "otra-web", "202794664", null, Now.AddDays(-2));
        hidden.Apply(ParsedFixture(), Now.AddDays(-2));
        var federation = new FakeSource("asturfutbol", isFederation: true);
        var handler = new SyncOfficialSheetHandler([federation], Options.Create(new PublicSheetOptions { Sources = ["asturfutbol"] }),
            Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(match)),
            Stub<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(competition)),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<OfficialMatchSheet?>(hidden)),
            Stub<TercerTiempo.Domain.Common.IUnitOfWork>((_, _) => Task.FromResult(0)),
            new OfficialSheetSyncMemory(), TimeProvider.System, NullLogger<SyncOfficialSheetHandler>.Instance);

        var result = await handler.Handle(new(match.Id, Background: true), default);

        Assert.Equal(1, federation.Calls);
        Assert.NotEqual(OfficialSheetAvailability.Available, result.Status);
        Assert.Null(result.Home);
    }

    [Fact]
    public async Task Without_other_public_sources_only_squad_matches_are_planned_including_hidden_sheets()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var ours = Guid.NewGuid();
        var withSquad = Match.Schedule(season.Id, 1, Guid.NewGuid(), ours, Now.AddDays(-3), null, null);
        var others = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        var fromRemovedSource = OfficialMatchSheet.Create(withSquad.Id, "otra-web", "1", null, Now.AddDays(-2));
        fromRemovedSource.Apply(ParsedFixture(), Now.AddDays(-2));
        var handler = new PlanOfficialSheetSyncHandler(
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition])),
            Stub<IMatchStore>((_, _) => Task.FromResult<IReadOnlyList<Match>>([withSquad, others])),
            Stub<ISquadStore>((_, _) => Task.FromResult<IReadOnlyList<Squad>>([Squad.Create(ours, season.Id, Guid.NewGuid(), Now)])),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([fromRemovedSource])),
            AllSources, Options.Create(new PublicSheetOptions()), new OfficialSheetSyncMemory(), TimeProvider.System);

        Assert.Equal([new PlannedSheet(withSquad.Id, true)], await handler.Handle(new(20), default));
    }

    [Fact]
    public async Task Requested_sheets_are_the_played_matches_of_the_scope_without_a_saved_sheet_in_round_order()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var other = Competition.Create("Otra", "Segunda", CompetitionLevel.Regional, 2, null, "Asturias", null);
        var otherSeason = other.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        other.MarkCurrent(otherSeason.Id);

        var (saved, sheet) = CompletedSheet(season.Id);
        var round2 = Match.Schedule(season.Id, 2, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-3), null, null);
        var round1 = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-10), null, null);
        var future = Match.Schedule(season.Id, 3, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(4), null, null);
        var cancelled = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-10), null, null);
        cancelled.Cancel();
        var otherLeague = Match.Schedule(otherSeason.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-3), null, null);

        List<Guid> requested = [];
        var signalled = false;
        var handler = new RequestSheetDownloadsHandler(
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition, other])),
            Stub<IOfficialSheetProvider>((_, _) => true),
            Stub<IMatchStore>((_, args) => Task.FromResult<IReadOnlyList<Match>>(
                (Guid)args![0]! == season.Id ? [saved, round2, round1, future, cancelled] : [otherLeague])),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([sheet])),
            Stub<ISheetDownloadStore>((method, args) =>
            {
                requested.AddRange((IReadOnlyList<Guid>)args![0]!);
                return Task.FromResult(requested.Count);
            }),
            new FakeSignal(() => signalled = true),
            TimeProvider.System);

        var scope = TercerTiempo.Application.Sync.SyncScope.FullSeason([competition.Id]) with { IncludeSheets = true };
        Assert.Equal(2, await handler.Handle(new(scope), default));
        Assert.Equal([round1.Id, round2.Id], requested);
        Assert.True(signalled);
    }

    [Fact]
    public async Task A_requested_sheet_waits_for_a_paused_federation_instead_of_giving_up()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        var match = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        SyncOfficialSheetHandler Handler() => new(
            [new ThrottledSource()], Options.Create(new PublicSheetOptions()),
            Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(match)),
            Stub<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(competition)),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<OfficialMatchSheet?>(null)),
            Stub<TercerTiempo.Domain.Common.IUnitOfWork>((_, _) => Task.FromResult(0)),
            new OfficialSheetSyncMemory(), TimeProvider.System, NullLogger<SyncOfficialSheetHandler>.Instance);

        // La pasada general sigue (y lo intentará otro día); la cola pedida se para hasta que acabe la pausa.
        Assert.Equal(OfficialSheetAvailability.Pending, (await Handler().Handle(new(match.Id, Background: true), default)).Status);
        var paused = await Assert.ThrowsAsync<ProviderThrottledException>(() => Handler().Handle(new(match.Id, Background: true, StopWhenThrottled: true), default));
        Assert.Equal(Now.AddHours(5), paused.RetryAt);
    }

    private sealed class ThrottledSource : IPublicSheetSource
    {
        public string Name => "asturfutbol";
        public bool IsFederation => true;

        public Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken) =>
            throw new ProviderThrottledException("asturfutbol", "En pausa", Now.AddHours(5));
    }

    [Fact]
    public void Queue_estimate_respects_the_hourly_and_daily_limits_of_the_federation()
    {
        var start = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

        // 20 por hora: 30 peticiones son una hora entera y diez más de la siguiente.
        var finish = TercerTiempo.Application.Admin.GetLiveDownloadsHandler.EstimateFinish(30, start, null, null, 0, 20, 0, 60);
        Assert.Equal(start.AddHours(1).AddMinutes(5), finish);

        // Con el tope del día gastado, se sigue al día siguiente; con pausa, desde su final.
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 2, 30, TimeSpan.Zero),
            TercerTiempo.Application.Admin.GetLiveDownloadsHandler.EstimateFinish(5, start, null, null, 0, 20, 60, 60));
        // Sin tope diario (0) solo cuenta el ritmo por hora: 45 peticiones a 20 por hora acaban en la tercera hora.
        Assert.Equal(start.AddHours(2).AddSeconds(5 * 30),
            TercerTiempo.Application.Admin.GetLiveDownloadsHandler.EstimateFinish(45, start, null, null, 0, 20, 500, 0));
        Assert.Equal(start.AddHours(5).AddSeconds(30),
            TercerTiempo.Application.Admin.GetLiveDownloadsHandler.EstimateFinish(1, start, start.AddHours(5), null, 18, 20, 59, 60));
    }

    [Fact]
    public async Task The_weekly_queue_asks_for_the_last_days_of_the_leagues_the_federation_publishes()
    {
        var regional = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var tercera = Competition.Create("3ª RFEF", "3ª RFEF", CompetitionLevel.National, 3, null, "Asturias", null);
        var terceraSeason = tercera.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        tercera.MarkCurrent(terceraSeason.Id);
        terceraSeason.LinkOfficial(TercerTiempo.Domain.Common.ExternalRef.Create("asturfutbol", "22880342/22922739/22"));
        var segunda = Competition.Create("2ª RFEF", "2ª RFEF", CompetitionLevel.National, 2, null, "Asturias", null);
        var segundaSeason = segunda.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        segunda.MarkCurrent(segundaSeason.Id);

        TercerTiempo.Application.Sync.SyncScope? asked = null;
        var handler = new QueueRecentSheetsHandler(
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([regional, tercera, segunda])),
            Stub<IOfficialSheetProvider>((method, args) => method == nameof(IOfficialSheetProvider.Publishes) ? (string)args![0]! != "2ª RFEF" : "asturfutbol"),
            Stub<TercerTiempo.Application.Abstractions.Messaging.ICommandHandler<RequestSheetDownloadsCommand, int>>((_, args) =>
            {
                asked = ((RequestSheetDownloadsCommand)args![0]!).Scope;
                return Task.FromResult(12);
            }),
            new FixedClock(new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(new(7), default);

        Assert.Equal(new RecentSheetsQueued(2, 12, new(2026, 10, 19), new(2026, 10, 26)), result);
        Assert.Equal([regional.Id, tercera.Id], asked!.CompetitionIds);
        Assert.True(asked.IncludeSheets);
        Assert.Equal(new TercerTiempo.Application.Abstractions.FootballData.MatchWindow(new(2026, 10, 19), new(2026, 10, 26)), asked.Window);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSignal(Action onSignal) : ISheetDownloadSignal
    {
        public void Signal() => onSignal();
    }

    private static readonly IPublicSheetSource[] AllSources = [new FakeSource("otra-web", isFederation: false), new FakeSource("asturfutbol", isFederation: true)];

    private static OfficialSheetData ParsedFixture()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.acta-25744780.html")))!;
        using var reader = new StreamReader(stream);
        return AsturfutbolPages.ParseSheet(reader.ReadToEnd(), 90)!;
    }

    private sealed class FakeSource(string name, bool isFederation) : IPublicSheetSource
    {
        public int Calls { get; private set; }
        public string Name => name;
        public bool IsFederation => isFederation;

        public Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<SheetFetch?>(null);
        }
    }

    private static (Match, OfficialMatchSheet) CompletedSheet(Guid? seasonId = null)
    {
        var match = Match.Schedule(seasonId ?? Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        match.RecordResult(2, 1, Now.AddDays(-2).AddHours(2));
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", "123", null, Now.AddDays(-1));
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.acta-25744780.html")))!;
        using var reader = new StreamReader(stream);
        sheet.Apply(AsturfutbolPages.ParseSheet(reader.ReadToEnd(), 90)!, Now.AddDays(-1));
        return (match, sheet);
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
