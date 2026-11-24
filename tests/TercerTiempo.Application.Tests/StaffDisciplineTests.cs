using System.Reflection;
using TercerTiempo.Application.Squads.Suspensions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Sanctions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

public class StaffDisciplineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private const string Coach = "ENTRENADOR PEREZ, JUAN";

    private readonly Guid _season = Guid.NewGuid();
    private readonly Guid _team = Guid.NewGuid();
    private readonly Guid _rival = Guid.NewGuid();
    private readonly List<Match> _matches = [];
    private readonly List<OfficialMatchSheet> _sheets = [];
    private readonly List<CommitteeResolution> _resolutions = [];

    public StaffDisciplineTests()
    {
        for (var matchday = 1; matchday <= 3; matchday++)
        {
            var match = Match.Schedule(_season, matchday, _team, _rival, Now.AddDays(-28 + matchday * 7), null, null);
            match.RecordResult(1, 1, match.KickoffAt);
            _matches.Add(match);
            _sheets.Add(Sheet(match, matchday == 3 ? 2 : 0));
        }
        _matches.Add(Match.Schedule(_season, 4, _rival, _team, Now.AddDays(3), null, null));
    }

    [Fact]
    public async Task A_sending_off_costs_the_next_match_until_the_committee_says_otherwise()
    {
        var discipline = await Reader().LoadAsync(_team, _season, default);

        var coach = Assert.Single(discipline.People);
        Assert.Equal((2, 1), (coach.YellowCards, coach.RedCards));
        Assert.Equal(SuspensionCause.SecondYellow, coach.Next.Suspension!.Cause);
        Assert.Equal(4, discipline.NextMatch!.Matchday);

        // Otro equipo con un técnico del mismo nombre no le toca.
        _resolutions.Add(Resolution(3, new CommitteeSanctionData("Otro Club A", Coach, "71-c", "Menosprecio al árbitro", 3, CommitteeSanctionKind.Staff)));
        Assert.Equal(SuspensionCause.SecondYellow, Assert.Single((await Reader().LoadAsync(_team, _season, default)).People).Next.Suspension!.Cause);
    }

    [Fact]
    public async Task The_committee_decides_the_matches_or_the_days_of_ban()
    {
        _resolutions.Add(Resolution(3, new CommitteeSanctionData("Mi Equipo A", Coach, "60-b", "Insultar al árbitro", 2, CommitteeSanctionKind.Staff)));
        var suspension = Assert.Single((await Reader().LoadAsync(_team, _season, default)).People).Next.Suspension!;
        Assert.Equal((SuspensionCause.Committee, 2, 1), (suspension.Cause, suspension.Matches, suspension.Nth));

        _resolutions.Clear();
        _resolutions.Add(Resolution(3, new CommitteeSanctionData("Mi Equipo A", Coach, "71-c", "Menosprecio al árbitro", 0, CommitteeSanctionKind.Staff, Days: 21)));
        var ban = Assert.Single((await Reader().LoadAsync(_team, _season, default)).People).Next.Suspension!;
        Assert.Equal(SuspensionCause.Committee, ban.Cause);
        Assert.NotNull(ban.Until);
        Assert.Contains("21 días", ban.Reason, StringComparison.Ordinal);

        // Un ban de dos días ya cumplido no le deja fuera.
        _resolutions.Clear();
        _resolutions.Add(Resolution(3, new CommitteeSanctionData("Mi Equipo A", Coach, "71-c", "Menosprecio al árbitro", 0, CommitteeSanctionKind.Staff, Days: 2)));
        Assert.Null(Assert.Single((await Reader().LoadAsync(_team, _season, default)).People).Next.Suspension);
    }

    [Fact]
    public async Task Four_yellows_in_the_cycle_is_a_warning()
    {
        _sheets.Clear();
        foreach (var match in _matches.Where(m => m.Status == MatchStatus.Played)) _sheets.Add(Sheet(match, 1));
        var extra = Match.Schedule(_season, 5, _team, _rival, Now.AddDays(-1), null, null);
        extra.RecordResult(0, 0, extra.KickoffAt);
        _matches.Add(extra);
        _sheets.Add(Sheet(extra, 1));

        var coach = Assert.Single((await Reader().LoadAsync(_team, _season, default)).People);
        Assert.Equal((4, 4, true), (coach.YellowCards, coach.Next.CycleYellows, coach.Next.Warned));
        Assert.Equal(new[] { Coach }, coach.FullNames);
    }

    private CommitteeResolution Resolution(int matchday, CommitteeSanctionData sanction)
    {
        var played = _matches.Single(m => m.Matchday == matchday);
        return CommitteeResolution.Create(_season, "asturfutbol", "Comité Territorial de Competición", DateOnly.FromDateTime(played.KickoffAt.UtcDateTime).AddDays(2), matchday, null, [sanction], Now);
    }

    private static OfficialMatchSheet Sheet(Match match, int yellows)
    {
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", match.Id.ToString("N"), null, Now);
        var line = new OfficialLineData(MatchSide.Home, 1, "PORTERO GARCIA, LUIS", true, 0, null, 90, 0, 0, 0, 0, false);
        sheet.Apply(
            new OfficialSheetData(null, "Mi Equipo A", "Rival B", 1, 1, [line], [],
                Staff: [new OfficialStaffData(MatchSide.Home, "Entrenador", Coach, yellows, yellows >= 2), new OfficialStaffData(MatchSide.Away, "Entrenador", "RIVAL LOPEZ, PEDRO")]),
            Now);
        return sheet;
    }

    private StaffDisciplineReader Reader()
    {
        var matches = Stub<IMatchStore>((method, _) => method == nameof(IMatchStore.GetTeamMatchesAsync)
            ? Task.FromResult<IReadOnlyList<Match>>(_matches.ToList())
            : throw new NotSupportedException(method));
        var sheets = Stub<IOfficialSheetStore>((method, args) => method == nameof(IOfficialSheetStore.GetStaffByMatchIdsAsync)
            ? Task.FromResult<IReadOnlyList<OfficialMatchSheet>>(_sheets.Where(s => ((IEnumerable<Guid>)args![0]!).Contains(s.MatchId)).ToList())
            : throw new NotSupportedException(method));
        var committee = Stub<ICommitteeSanctionStore>((method, _) => method == nameof(ICommitteeSanctionStore.GetBySeasonAsync)
            ? Task.FromResult<IReadOnlyList<CommitteeResolution>>(_resolutions.ToList())
            : throw new NotSupportedException(method));
        return new StaffDisciplineReader(matches, sheets, committee, Stub<ITeamStore>(), new FixedClock(Now));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?>? call = null) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call ?? ((method, _) => throw new NotSupportedException(method));
        return stub;
    }
}
