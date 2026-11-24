using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Coaches.Profile;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// El perfil de un técnico: su historial de equipos por los nombramientos (los vigentes primero; un nombramiento retirado
// solo sale si llegó a empezarlo, hasta el día que lo dejó), el balance de cada equipo mientras estuvo y si es el tuyo.
public class CoachProfileTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly Club _club = Club.Register(Guid.NewGuid(), "Hispano", Now);
    private readonly Coach _coach = Coach.Register(Guid.NewGuid(), "Xurde Menéndez Cueto", Now);
    private readonly Team _first = Team.Create("Hispano", "HIS", null);
    private readonly Team _filial = Team.Create("Hispano 'B'", "HIB", null);
    private readonly Competition _competition = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", null);
    private readonly List<CoachAppointment> _appointments = [];
    private readonly List<Match> _matches = [];
    private readonly List<Standing> _standings = [];
    private readonly List<OfficialMatchSheet> _sheets = [];

    public CoachProfileTests()
    {
        var current = _competition.AddSeason("2026/2027", Today.AddDays(-60), Today.AddDays(200), null);
        var past = _competition.AddSeason("2025/2026", Today.AddDays(-420), Today.AddDays(-70), null);

        var active = CoachAppointment.Create(_club.Id, _first.Id, current.Id, _coach.DisplayName, null, StaffRole.HeadCoach, Today.AddDays(-30), Today.AddDays(200), Now);
        active.Redeem(_coach.Id, Now);

        var expired = CoachAppointment.Create(_club.Id, _filial.Id, past.Id, _coach.DisplayName, null, StaffRole.AssistantCoach, Today.AddDays(-400), Today.AddDays(-70), Now.AddDays(-400));
        expired.Redeem(_coach.Id, Now.AddDays(-390));

        var revoked = CoachAppointment.Create(_club.Id, _filial.Id, current.Id, _coach.DisplayName, null, StaffRole.Analyst, Today.AddDays(-20), Today.AddDays(200), Now);
        revoked.Redeem(_coach.Id, Now);
        revoked.Revoke(Now);

        _appointments.AddRange([expired, revoked, active]);
    }

    [Fact]
    public async Task The_profile_lists_current_teams_first_and_hides_revoked_appointments()
    {
        var profile = await Handler(new Viewer(CoachId: Guid.NewGuid())).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.Equal("Xurde Menéndez Cueto", profile.DisplayName);
        Assert.False(profile.IsMe);
        Assert.Equal([("Hispano", true, StaffRole.HeadCoach), ("Hispano 'B'", false, StaffRole.AssistantCoach)],
            profile.Teams.Select(t => (t.TeamName, t.Current, t.Role)));
        Assert.All(profile.Teams, t => Assert.Equal("Hispano", t.ClubName));
    }

    [Fact]
    public async Task A_coach_who_left_mid_season_stays_in_the_history_until_the_day_he_left()
    {
        var season = _competition.Seasons.Single(s => s.Label == "2026/2027");
        var dismissed = CoachAppointment.Create(_club.Id, _filial.Id, season.Id, _coach.DisplayName, null, StaffRole.Physio, Today.AddDays(-50), Today.AddDays(200), Now.AddDays(-50));
        dismissed.Redeem(_coach.Id, Now.AddDays(-45));
        dismissed.Revoke(Now);
        _appointments.Add(dismissed);

        var profile = await Handler(new Viewer(CoachId: Guid.NewGuid())).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.Equal([("Hispano", true, StaffRole.HeadCoach, (DateOnly?)null), ("Hispano 'B'", false, StaffRole.Physio, SpainTime.Today(Now)), ("Hispano 'B'", false, StaffRole.AssistantCoach, null)],
            profile.Teams.Select(t => (t.TeamName, t.Current, t.Role, t.LeftOn)));
    }

    [Fact]
    public async Task Each_team_counts_only_the_matches_played_while_in_it_with_its_table_position_and_a_summary()
    {
        var season = _competition.Seasons.Single(s => s.Label == "2026/2027");
        var rival = Guid.NewGuid();
        // Uno antes del nombramiento (no cuenta), dos victorias y un empate mientras está, y uno sin jugar.
        _matches.AddRange([
            Played(season.Id, 1, _first.Id, rival, -40, 0, 3),
            Played(season.Id, 2, _first.Id, rival, -20, 2, 1),
            Played(season.Id, 3, rival, _first.Id, -10, 0, 1),
            Played(season.Id, 4, _first.Id, rival, -3, 1, 1),
            Match.Schedule(season.Id, 5, rival, _first.Id, Now.AddDays(5), null, null),
        ]);
        _standings.AddRange([
            Standing.Create(season.Id, rival, new StandingLine(1, 4, 3, 1, 0, 6, 3, 10), Now),
            Standing.Create(season.Id, _first.Id, new StandingLine(2, 4, 2, 1, 1, 4, 5, 7), Now),
            Standing.Create(season.Id, Guid.NewGuid(), new StandingLine(3, 4, 0, 0, 4, 1, 7, 0), Now),
        ]);

        var profile = await Handler(new Viewer(CoachId: Guid.NewGuid())).Handle(new GetCoachProfileQuery(_coach.Id), default);

        var head = profile.Teams.Single(t => t.Role == StaffRole.HeadCoach);
        Assert.Equal(new CoachTeamRecordDto(3, 2, 1, 0, 4, 2, 7), head.Record);
        Assert.Equal((2, 3), (head.Position!.Value, head.TeamsInTable!.Value));
        var past = profile.Teams.Single(t => t.Role == StaffRole.AssistantCoach);
        Assert.Equal(0, past.Record!.Played);
        Assert.Null(past.Position);

        var summary = profile.Summary!;
        Assert.Equal((3, 2, 1, 0, 7), (summary.Played, summary.Won, summary.Drawn, summary.Lost, summary.Points));
        Assert.Equal((66.7, 33.3, 0.0, 2.33), (summary.WinPct!.Value, summary.DrawPct!.Value, summary.LossPct!.Value, summary.PointsPerMatch!.Value));
        Assert.Equal((2.0, 1), (summary.AveragePosition!.Value, summary.RankedTeams));
    }

    [Fact]
    public async Task Each_team_says_in_how_many_official_sheets_the_coach_appears_by_name_and_with_which_role()
    {
        var season = _competition.Seasons.Single(s => s.Label == "2026/2027");
        var rival = Guid.NewGuid();
        var withHim = Played(season.Id, 2, _first.Id, rival, -20, 2, 1);
        var away = Played(season.Id, 3, rival, _first.Id, -10, 0, 1);
        var withoutStaff = Played(season.Id, 4, _first.Id, rival, -3, 1, 1);
        _matches.AddRange([withHim, away, withoutStaff]);
        _sheets.AddRange([
            Sheet(withHim, [new(MatchSide.Home, "Entrenador", "MENENDEZ CUETO, XURDE"), new(MatchSide.Away, "Entrenador", "OTRO PEREZ, JUAN")]),
            // Fuera de casa figura en el lado visitante; el local con el mismo apellido no es él.
            Sheet(away, [new(MatchSide.Home, "Entrenador", "MENENDEZ GARCIA, PABLO"), new(MatchSide.Away, "2ºEntrenador", "MENENDEZ CUETO, XURDE")]),
            Sheet(withoutStaff, []),
        ]);

        var profile = await Handler(new Viewer(CoachId: Guid.NewGuid())).Handle(new GetCoachProfileQuery(_coach.Id), default);

        var head = profile.Teams.Single(t => t.Role == StaffRole.HeadCoach);
        Assert.Equal((2, 2), (head.OfficialSheets, head.OfficialAppearances));
        Assert.Contains(head.OfficialRole, new[] { "Entrenador", "2ºEntrenador" });
    }

    [Fact]
    public async Task The_current_team_says_whether_the_coach_is_warned_or_suspended_for_the_next_match()
    {
        var season = _competition.Seasons.Single(s => s.Label == "2026/2027");
        var rival = Guid.NewGuid();
        var games = Enumerable.Range(0, 4).Select(i => Played(season.Id, 2 + i, _first.Id, rival, -25 + i * 5, 1, 0)).ToList();
        var next = Match.Schedule(season.Id, 6, rival, _first.Id, Now.AddDays(3), null, null);
        _matches.AddRange([.. games, next]);
        foreach (var game in games)
            _sheets.Add(ClosedSheet(game, [new(MatchSide.Home, "Entrenador", "MENENDEZ CUETO, XURDE", YellowCards: 1), new(MatchSide.Away, "Entrenador", "OTRO PEREZ, JUAN")]));

        var head = (await Handler(new Viewer()).Handle(new GetCoachProfileQuery(_coach.Id), default)).Teams.Single(t => t.Role == StaffRole.HeadCoach);
        Assert.Equal((4, 0, 4, true), (head.Discipline!.YellowCards, head.Discipline.RedCards, head.Discipline.Next.CycleYellows, head.Discipline.Next.Warned));
        Assert.Null(head.Discipline.Next.Suspension);

        // Expulsado por doble amarilla en el último: no puede estar en el próximo, y esas dos no entran en el ciclo.
        _sheets[^1] = ClosedSheet(games[^1], [new(MatchSide.Home, "Entrenador", "MENENDEZ CUETO, XURDE", YellowCards: 2, RedCard: true)]);
        head = (await Handler(new Viewer()).Handle(new GetCoachProfileQuery(_coach.Id), default)).Teams.Single(t => t.Role == StaffRole.HeadCoach);
        Assert.Equal((5, 1, 3), (head.Discipline!.YellowCards, head.Discipline.RedCards, head.Discipline.Next.CycleYellows));
        Assert.Equal(SuspensionCause.SecondYellow, head.Discipline.Next.Suspension!.Cause);

        // En los equipos que ya no lleva no se dice nada.
        Assert.All(profileTeamsNotCurrent(await Handler(new Viewer()).Handle(new GetCoachProfileQuery(_coach.Id), default)), t => Assert.Null(t.Discipline));
        static IEnumerable<CoachTeamDto> profileTeamsNotCurrent(CoachProfileDto profile) => profile.Teams.Where(t => !t.Current);
    }

    private static OfficialMatchSheet ClosedSheet(Match match, IReadOnlyList<OfficialStaffData> staff)
    {
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", match.Id.ToString("N"), null, Now);
        var line = new OfficialLineData(MatchSide.Home, 1, "PORTERO PEREZ, LUIS", true, 0, null, 90, 0, 0, 0, 0, false);
        sheet.Apply(new OfficialSheetData(null, "Primero", "Rival", 1, 0, [line], [], Staff: staff), Now);
        return sheet;
    }

    private static OfficialMatchSheet Sheet(Match match, IReadOnlyList<OfficialStaffData> staff)
    {
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", match.Id.ToString("N"), null, Now);
        sheet.ApplyStaff(staff, Now);
        return sheet;
    }

    [Fact]
    public async Task Without_matches_the_summary_has_no_averages()
    {
        var profile = await Handler(new Viewer(CoachId: Guid.NewGuid())).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.Equal(0, profile.Summary!.Played);
        Assert.Null(profile.Summary.WinPct);
        Assert.Null(profile.Summary.PointsPerMatch);
        Assert.Null(profile.Summary.AveragePosition);
    }

    private static Match Played(Guid seasonId, int matchday, Guid home, Guid away, int days, int homeGoals, int awayGoals)
    {
        var match = Match.Schedule(seasonId, matchday, home, away, Now.AddDays(days), null, null);
        match.RecordResult(homeGoals, awayGoals, Now.AddDays(days));
        return match;
    }

    [Fact]
    public async Task Only_the_coach_sees_the_profile_as_their_own()
    {
        var mine = await Handler(new Viewer(CoachId: _coach.Id)).Handle(new GetCoachProfileQuery(_coach.Id), default);
        var club = await Handler(new Viewer(ClubId: _club.Id)).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.True(mine.IsMe);
        Assert.False(club.IsMe);
        Assert.Null(mine.PhotoUrl);
    }

    [Fact]
    public async Task The_photo_link_carries_the_version_of_the_file()
    {
        _coach.SetPhoto($"{_coach.Id:N}-abc123.png");

        var profile = await Handler(new Viewer(CoachId: _coach.Id)).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.Equal($"/api/coaches/{_coach.Id}/photo?v=abc123", profile.PhotoUrl);
    }

    private GetCoachProfileHandler Handler(ICurrentUser viewer)
    {
        var stores = new AccountStores(
            Stub<TercerTiempo.Domain.Players.IPlayerStore>(),
            Stub<ICoachStore>((method, args) => method == nameof(ICoachStore.GetByIdAsync)
                ? Task.FromResult<Coach?>((Guid)args![0]! == _coach.Id ? _coach : null)
                : throw new NotSupportedException(method)),
            Stub<IClubStore>((method, args) => method == nameof(IClubStore.GetByIdAsync)
                ? Task.FromResult<Club?>((Guid)args![0]! == _club.Id ? _club : null)
                : throw new NotSupportedException(method)),
            Stub<ICoachAppointmentStore>((method, _) => method == nameof(ICoachAppointmentStore.GetByCoachAsync)
                ? Task.FromResult<IReadOnlyList<CoachAppointment>>(_appointments)
                : throw new NotSupportedException(method)),
            Stub<ISquadStore>((method, _) => method == nameof(ISquadStore.GetByTeamAndSeasonAsync)
                ? Task.FromResult<Squad?>(null)
                : throw new NotSupportedException(method)),
            Stub<ITeamStore>((method, args) => method == nameof(ITeamStore.GetByIdAsync)
                ? Task.FromResult(new[] { _first, _filial }.FirstOrDefault(t => t.Id == (Guid)args![0]!))
                : throw new NotSupportedException(method)),
            Stub<ICompetitionStore>((method, _) => method == nameof(ICompetitionStore.GetBySeasonAsync)
                ? Task.FromResult<Competition?>(_competition)
                : throw new NotSupportedException(method)),
            Stub<Licensing.ILicenseGate>(),
            TimeProvider.System,
            new NoSeasons());
        var matches = Stub<IMatchStore>((method, args) => method == nameof(IMatchStore.GetTeamMatchesAsync)
            ? Task.FromResult<IReadOnlyList<Match>>(_matches.Where(m => m.SeasonId == (Guid)args![0]! && m.Involves((Guid)args[1]!)).ToList())
            : throw new NotSupportedException(method));
        var standings = Stub<IStandingStore>((method, args) => method == nameof(IStandingStore.GetBySeasonAsync)
            ? Task.FromResult<IReadOnlyList<Standing>>(_standings.Where(s => s.SeasonId == (Guid)args![0]!).ToList())
            : throw new NotSupportedException(method));
        var sheets = Stub<IOfficialSheetStore>((method, args) => method == nameof(IOfficialSheetStore.GetStaffByMatchIdsAsync)
            ? Task.FromResult<IReadOnlyList<OfficialMatchSheet>>(_sheets.Where(s => ((IEnumerable<Guid>)args![0]!).Contains(s.MatchId)).ToList())
            : throw new NotSupportedException(method));
        var committee = Stub<TercerTiempo.Domain.Sanctions.ICommitteeSanctionStore>((method, _) => method == nameof(TercerTiempo.Domain.Sanctions.ICommitteeSanctionStore.GetBySeasonAsync)
            ? Task.FromResult<IReadOnlyList<TercerTiempo.Domain.Sanctions.CommitteeResolution>>([])
            : throw new NotSupportedException(method));
        var discipline = new TercerTiempo.Application.Squads.Suspensions.StaffDisciplineReader(matches, sheets, committee, Stub<ITeamStore>(), TimeProvider.System);
        return new GetCoachProfileHandler(viewer, stores, matches, standings, sheets, discipline);
    }

    private sealed record Viewer(Guid? CoachId = null, Guid? ClubId = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public Guid? PlayerId => null;
        public AccountType? AccountType => CoachId != null ? Abstractions.Identity.AccountType.Coach : Abstractions.Identity.AccountType.Club;
    }

    private static T Stub<T>(Func<string, object?[]?, object?>? call = null) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call ?? ((method, _) => throw new NotSupportedException(method));
        return stub;
    }
}
