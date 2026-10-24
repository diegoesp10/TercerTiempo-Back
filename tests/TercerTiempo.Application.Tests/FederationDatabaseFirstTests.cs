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
            AllSources, Options.Create(new PublicSheetOptions()), new OfficialSheetSyncMemory(), TimeProvider.System);
        Assert.Equal([new PlannedSheet(withSquad.Id, true), new PlannedSheet(others.Id, false)], await handler.Handle(new(20), default));
    }

    [Fact]
    public void Sheets_from_a_source_removed_from_the_configuration_are_hidden()
    {
        var closedBeta = new PublicSheetOptions { Sources = ["asturfutbol"] };
        Assert.True(closedBeta.Allows("asturfutbol"));
        Assert.False(closedBeta.Allows("besoccer"));
        Assert.True(new PublicSheetOptions().Allows("besoccer"));
    }

    [Fact]
    public async Task A_complete_sheet_from_a_disabled_source_is_not_served_and_is_searched_again()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        var match = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-3), null, null);
        match.RecordResult(2, 1, Now.AddDays(-3).AddHours(2));
        var hidden = OfficialMatchSheet.Create(match.Id, "besoccer", "202794664", null, Now.AddDays(-2));
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
    public async Task Without_besoccer_only_squad_matches_are_planned_including_hidden_sheets()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var ours = Guid.NewGuid();
        var withSquad = Match.Schedule(season.Id, 1, Guid.NewGuid(), ours, Now.AddDays(-3), null, null);
        var others = Match.Schedule(season.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        var fromBeSoccer = OfficialMatchSheet.Create(withSquad.Id, "besoccer", "1", null, Now.AddDays(-2));
        fromBeSoccer.Apply(ParsedFixture(), Now.AddDays(-2));
        var handler = new PlanOfficialSheetSyncHandler(
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition])),
            Stub<IMatchStore>((_, _) => Task.FromResult<IReadOnlyList<Match>>([withSquad, others])),
            Stub<ISquadStore>((_, _) => Task.FromResult<IReadOnlyList<Squad>>([Squad.Create(ours, season.Id, Guid.NewGuid(), Now)])),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([fromBeSoccer])),
            AllSources, Options.Create(new PublicSheetOptions { Sources = ["asturfutbol"] }), new OfficialSheetSyncMemory(), TimeProvider.System);

        Assert.Equal([new PlannedSheet(withSquad.Id, true)], await handler.Handle(new(20), default));
    }

    private static readonly IPublicSheetSource[] AllSources = [new FakeSource("besoccer", isFederation: false), new FakeSource("asturfutbol", isFederation: true)];

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
