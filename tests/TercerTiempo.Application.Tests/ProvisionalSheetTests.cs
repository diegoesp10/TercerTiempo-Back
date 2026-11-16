using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Referees;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

// La federación publica las alineaciones antes de que el árbitro cierre el acta. Esa página (sin goles, cambios ni
// tarjetas) no puede guardarse como definitiva: se guarda como provisional y se vuelve a pedir hasta que se cierre.
public class ProvisionalSheetTests
{
    private static readonly DateTimeOffset Kickoff = new(2026, 11, 15, 11, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDownload = Kickoff.AddHours(3);

    private static OfficialSheetData Parse(string fixture)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures." + fixture, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream);
        return AsturfutbolPages.ParseSheet(reader.ReadToEnd(), 90)!;
    }

    private static OfficialSheetData NotClosed => Parse("acta-25744818-sin-cerrar.html");
    private static OfficialSheetData Closed => Parse("acta-25744818.html");

    private static (Competition, Match) VallobinFresneda(int? home = 1, int? away = 0)
    {
        var competition = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var match = Match.Schedule(season.Id, 10, Guid.NewGuid(), Guid.NewGuid(), Kickoff, null, null);
        if (home is { } h && away is { } a) match.RecordResult(h, a, Kickoff.AddHours(2));
        return (competition, match);
    }

    // ------------------------------------------------------------------ cuándo está cerrada

    [Fact]
    public void A_sheet_without_events_is_not_closed_when_the_match_had_goals()
    {
        var (_, match) = VallobinFresneda();
        Assert.False(OfficialSheetRefresh.IsClosed(NotClosed, match, FirstDownload));
    }

    [Fact]
    public void A_sheet_without_any_event_is_not_closed_even_in_a_goalless_draw_or_without_result()
    {
        Assert.False(OfficialSheetRefresh.IsClosed(NotClosed, VallobinFresneda(0, 0).Item2, FirstDownload));
        Assert.False(OfficialSheetRefresh.IsClosed(NotClosed, VallobinFresneda(null, null).Item2, FirstDownload));
    }

    [Fact]
    public void The_closed_sheet_of_the_same_match_is_closed()
    {
        var (_, match) = VallobinFresneda();
        Assert.True(OfficialSheetRefresh.IsClosed(Closed, match, Kickoff.AddDays(1)));
    }

    [Fact]
    public void An_awarded_result_that_differs_from_the_played_goals_does_not_keep_the_sheet_open()
    {
        // 25744780 se jugó 0-5; aunque la liga lo diera 3-0 por despacho, el acta está cerrada.
        var (_, match) = VallobinFresneda(3, 0);
        Assert.True(OfficialSheetRefresh.IsClosed(Parse("acta-25744780.html"), match, FirstDownload));
    }

    [Fact]
    public void A_sheet_still_open_a_week_later_is_taken_as_it_is()
    {
        var (_, match) = VallobinFresneda();
        Assert.False(OfficialSheetRefresh.IsClosed(NotClosed, match, Kickoff + OfficialSheetRefresh.ProvisionalLimit - TimeSpan.FromMinutes(1)));
        Assert.True(OfficialSheetRefresh.IsClosed(NotClosed, match, Kickoff + OfficialSheetRefresh.ProvisionalLimit));
    }

    // ------------------------------------------------------------------ guardado y refresco

    [Fact]
    public void A_provisional_sheet_keeps_the_lineups_without_score_and_does_not_count_as_available()
    {
        var (_, match) = VallobinFresneda();
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", "25744818", null, FirstDownload);
        sheet.Apply(NotClosed, FirstDownload, closed: false);

        Assert.Equal(OfficialSheetStatus.Provisional, sheet.Status);
        Assert.False(sheet.IsAvailable);
        Assert.True(sheet.HasLineups);
        Assert.Null(sheet.HomeGoals);
        Assert.Null(sheet.AwayGoals);
        Assert.Equal(FirstDownload, sheet.FetchedAt);
    }

    [Fact]
    public void A_provisional_sheet_is_checked_again_every_few_hours_and_a_closed_one_never()
    {
        var (_, match) = VallobinFresneda();
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", "25744818", null, FirstDownload);
        sheet.Apply(NotClosed, FirstDownload, closed: false);

        Assert.False(OfficialSheetRefresh.IsDue(sheet, match, FirstDownload + OfficialSheetRefresh.ProvisionalRecheck - TimeSpan.FromMinutes(1)));
        Assert.True(OfficialSheetRefresh.IsDue(sheet, match, FirstDownload + OfficialSheetRefresh.ProvisionalRecheck));

        sheet.Apply(Closed, FirstDownload.AddHours(6));
        Assert.False(OfficialSheetRefresh.IsDue(sheet, match, FirstDownload.AddYears(1)));
    }

    [Fact]
    public async Task The_federation_is_asked_again_for_a_provisional_sheet_without_the_saved_copy()
    {
        var (competition, match) = VallobinFresneda();
        var season = competition.Seasons.Single();
        List<bool> refreshes = [];
        var provider = Stub<IOfficialSheetProvider>((method, args) => method switch
        {
            nameof(IOfficialSheetProvider.ProviderName) => "asturfutbol",
            nameof(IOfficialSheetProvider.SheetUrl) => $"https://federacion.invalid/{args![0]}",
            nameof(IOfficialSheetProvider.GetSheetAsync) => Record(refreshes, (bool)args![1]!),
            _ => throw new InvalidOperationException(method),
        });
        var source = new FederationSheetSource(provider, null!, new OfficialSheetSyncMemory(), new FixedClock(FirstDownload));

        var provisional = OfficialMatchSheet.Create(match.Id, "asturfutbol", "25744818", null, FirstDownload);
        provisional.Apply(NotClosed, FirstDownload, closed: false);
        var closed = OfficialMatchSheet.Create(match.Id, "asturfutbol", "25744818", null, FirstDownload);
        closed.Apply(Closed, FirstDownload);
        var pending = OfficialMatchSheet.Create(match.Id, "asturfutbol", "25744818", null, FirstDownload);

        await source.FetchAsync(match, competition, season, provisional, default);
        await source.FetchAsync(match, competition, season, pending, default);
        await source.FetchAsync(match, competition, season, closed, default);

        Assert.Equal([true, false, false], refreshes);

        static Task<OfficialSheetData?> Record(List<bool> into, bool refresh)
        {
            into.Add(refresh);
            return Task.FromResult<OfficialSheetData?>(null);
        }
    }

    [Fact]
    public async Task The_J10_sheet_is_saved_as_provisional_first_and_completed_when_the_referee_closes_it()
    {
        var (competition, match) = VallobinFresneda();
        OfficialMatchSheet? stored = null;
        var store = Stub<IOfficialSheetStore>((method, args) =>
        {
            if (method == nameof(IOfficialSheetStore.Add)) { stored = (OfficialMatchSheet)args![0]!; return null; }
            return Task.FromResult(stored);
        });
        var source = new ScriptedSource(NotClosed, Closed);

        SyncOfficialSheetHandler Handler(DateTimeOffset now) => new(
            [source], Options.Create(new PublicSheetOptions()),
            Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(match)),
            Stub<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(competition)),
            store,
            Stub<IUnitOfWork>((_, _) => Task.FromResult(0)),
            new OfficialSheetSyncMemory(), new MatchOfficialsWriter(new InMemoryRefereeStore(), TimeProvider.System),
            new FixedClock(now), NullLogger<SyncOfficialSheetHandler>.Instance);

        // 15:26: alineaciones sin goles, cambios ni tarjetas.
        var first = await Handler(FirstDownload).Handle(new(match.Id, Background: true), default);
        Assert.Equal(OfficialSheetAvailability.Provisional, first.Status);
        Assert.Equal(OfficialSheetStatus.Provisional, stored!.Status);
        Assert.Equal(11, first.Home!.Starters.Count);
        Assert.Null(first.Home.Goals);
        Assert.Null(first.Away!.Goals);
        Assert.Empty(first.Events);

        // Antes de que toque refrescar no se vuelve a preguntar, pero se sigue enseñando como provisional.
        var early = await Handler(FirstDownload.AddHours(1)).Handle(new(match.Id, Background: true), default);
        Assert.Equal(OfficialSheetAvailability.Provisional, early.Status);
        Assert.Equal(1, source.Calls);

        // Unas horas después el árbitro ya la ha cerrado.
        var second = await Handler(FirstDownload + OfficialSheetRefresh.ProvisionalRecheck).Handle(new(match.Id, Background: true), default);
        Assert.Equal(2, source.Calls);
        Assert.True(source.LastCurrentWasProvisional);
        Assert.Equal(OfficialSheetAvailability.Available, second.Status);
        Assert.Equal(OfficialSheetStatus.Available, stored.Status);
        Assert.Equal(1, second.Home!.Goals);
        Assert.Equal(0, second.Away!.Goals);
        Assert.Contains(second.Events, e => e.Type == OfficialEventType.Goal && e.Side == MatchSide.Home);
        Assert.Contains(second.Events, e => e.Type == OfficialEventType.Substitution);
        Assert.Contains(second.Home.Bench, l => l.Played);
        Assert.Contains(second.Home.Starters, l => l.MinuteOut is not null);
    }

    private sealed class ScriptedSource(params OfficialSheetData[] pages) : IPublicSheetSource
    {
        public int Calls { get; private set; }
        public bool LastCurrentWasProvisional { get; private set; }
        public string Name => "asturfutbol";
        public bool IsFederation => true;

        public Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken)
        {
            LastCurrentWasProvisional = current is { IsProvisional: true };
            var page = pages[Math.Min(Calls++, pages.Length - 1)];
            return Task.FromResult<SheetFetch?>(new SheetFetch(Name, "25744818", null, page));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = (method, args) => call(method.StartsWith("get_", StringComparison.Ordinal) ? method[4..] : method, args);
        return result;
    }
}
