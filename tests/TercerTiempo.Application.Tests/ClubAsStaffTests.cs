using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

// El club es la entidad de la que depende el cuerpo técnico: hace lo mismo que él en las plantillas de sus equipos.
public class ClubAsStaffTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task The_club_opens_the_squad_of_its_team_without_coaches_and_with_the_published_roster()
    {
        var f = new Fixture(withSquad: false);

        var first = await f.Open().Handle(new(f.Team.Id, f.Played.Id), default);
        var again = await f.Open().Handle(new(f.Team.Id, null), default);

        var squad = Assert.Single(f.Squads);
        Assert.Equal(squad.Id, first.SquadId);
        Assert.Equal(first.SquadId, again.SquadId);
        Assert.Empty(squad.Staff);
        Assert.Equal(["Pelayo Fernández"], squad.ActivePlayers.Select(p => p.FullName));
    }

    [Fact]
    public async Task A_club_cannot_open_the_squad_of_a_team_it_does_not_manage()
    {
        var f = new Fixture(withSquad: false);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Open().Handle(new(f.Rival.Id, null), default));

        Assert.Equal(ErrorCode.ClubTeamNotLinked, error.Code);
        Assert.Empty(f.Squads);
    }

    [Fact]
    public async Task The_club_records_the_internal_sheet_of_its_team_in_its_own_name()
    {
        var f = new Fixture();
        var player = f.Squad.ActivePlayers.Single();

        var sheet = await f.Record(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, [Line(player.Id)]), default);

        Assert.True(sheet.CanEdit);
        var line = Assert.Single(f.Performances);
        Assert.Equal(f.Club.Id, line.RecordedByClubId);
        Assert.Null(line.RecordedByCoachId);
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public async Task Another_club_cannot_record_the_internal_sheet()
    {
        var f = new Fixture();
        var other = Club.Register(Guid.NewGuid(), "Real Titánico", Now);
        other.AddTeam(f.Rival.Id, Now);
        f.OtherClubs.Add(other);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Record(new User(ClubId: other.Id, UserId: other.UserId)).Handle(new(f.Squad.Id, f.Played.Id, [Line(f.Squad.ActivePlayers.Single().Id)]), default));

        Assert.Equal(ErrorCode.SquadManagersOnly, error.Code);
        Assert.Empty(f.Performances);
    }

    [Fact]
    public async Task The_video_of_a_match_not_played_yet_cannot_be_uploaded()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            f.BeginUpload().Handle(new(f.Squad.Id, f.Upcoming.Id, VideoPart.Full, null, "partido.mp4", "video/mp4", 1_000_000), default));

        Assert.Equal(ErrorCode.VideoMatchNotPlayed, error.Code);
        Assert.Equal(0, f.Saves);
    }

    private static MatchSheetEntryInput Line(Guid squadPlayerId) =>
        new(squadPlayerId, Played: true, Started: true, MinutesPlayed: 90, Goals: 1, Assists: 0, Shots: 2, ShotsOnTarget: 1,
            KeyPasses: 0, Tackles: 1, Interceptions: 0, Saves: 0, YellowCards: 0, RedCard: false, Notes: null);

    private sealed record User(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => CoachId != null ? Abstractions.Identity.AccountType.Coach : Abstractions.Identity.AccountType.Club;
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public List<Club> OtherClubs { get; } = [];
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Rival { get; } = Team.Create("Real Titánico", "TIT", null);
        public Competition Competition { get; } = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        public Match Played { get; }
        public Match Upcoming { get; }
        public List<Squad> Squads { get; } = [];
        public Squad Squad => Squads.Single();
        public List<PlayerMatchPerformance> Performances { get; } = [];
        public User ClubUser => new(ClubId: Club.Id, UserId: Club.UserId);
        public int Saves { get; private set; }
        private readonly Season _season;
        private readonly RosterEntry _rosterEntry;

        public Fixture(bool withSquad = true)
        {
            _season = Competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
            Competition.MarkCurrent(_season.Id);
            Club.AddTeam(Team.Id, Now);
            _rosterEntry = RosterEntry.Create(Team.Id, _season.Id, "Pelayo Fernández", Position.Forward, 9, null, Now);
            Played = Match.Schedule(_season.Id, 1, Team.Id, Rival.Id, Now.AddDays(-2), null, null);
            Played.RecordResult(2, 1, Now.AddDays(-2).AddHours(2));
            Upcoming = Match.Schedule(_season.Id, 2, Team.Id, Rival.Id, Now.AddDays(3), null, null);

            if (withSquad)
            {
                var squad = Squad.Open(Team.Id, _season.Id, Now);
                squad.AddPlayer(_rosterEntry.FullName, _rosterEntry.Position, _rosterEntry.ShirtNumber, _rosterEntry.Id, Now);
                Squads.Add(squad);
            }
        }

        private ISquadStore SquadStore => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult(Squads.FirstOrDefault(s => s.Id == (Guid)args![0]!)),
            nameof(ISquadStore.GetByTeamAndSeasonAsync) => Task.FromResult(Squads.FirstOrDefault(s => s.TeamId == (Guid)args![0]! && s.SeasonId == (Guid)args[1]!)),
            nameof(ISquadStore.Add) => Run(() => Squads.Add((Squad)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IClubStore Clubs => Stub<IClubStore>((_, args) =>
            Task.FromResult(OtherClubs.Append(Club).FirstOrDefault(c => c.Id == (Guid)args![0]! || c.UserId == (Guid)args[0]!)));
        private ICoachStore Coaches => Stub<ICoachStore>((method, _) => throw new NotSupportedException(method));
        private IMatchStore Matches => Stub<IMatchStore>((method, args) => method switch
        {
            nameof(IMatchStore.GetByIdAsync) => Task.FromResult(new[] { Played, Upcoming }.FirstOrDefault(m => m.Id == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ITeamStore Teams => Stub<ITeamStore>((method, args) => method switch
        {
            nameof(ITeamStore.GetRosterAsync) => Task.FromResult<IReadOnlyList<RosterEntry>>([_rosterEntry]),
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team, Rival]),
            nameof(ITeamStore.GetByIdAsync) => Task.FromResult(new[] { Team, Rival }.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ICompetitionStore Competitions => Stub<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(Competition));
        private IPerformanceStore PerformanceStore => Stub<IPerformanceStore>((method, args) => method switch
        {
            nameof(IPerformanceStore.GetBySquadAndMatchAsync) => Task.FromResult<IReadOnlyList<PlayerMatchPerformance>>(
                Performances.Where(p => p.SquadId == (Guid)args![0]! && p.MatchId == (Guid)args[1]!).ToList()),
            nameof(IPerformanceStore.Add) => Run(() => Performances.Add((PlayerMatchPerformance)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IPlayerStore Players => Stub<IPlayerStore>((_, _) => Task.FromResult<IReadOnlyList<Player>>([]));
        private ITeamMatchStatsStore TeamStats => Stub<ITeamMatchStatsStore>((_, _) => Task.FromResult<TeamMatchStats?>(null));
        private IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(++Saves));
        private static object? Run(Action action) { action(); return null; }

        public OpenClubSquadHandler Open()
        {
            var licenses = Stub<Licensing.ILicenseGate>((method, _) => throw new NotSupportedException(method));
            var appointments = Stub<ICoachAppointmentStore>((method, _) => throw new NotSupportedException(method));
            var stores = new AccountStores(Players, Coaches, Clubs, appointments, SquadStore, Teams, Competitions, licenses, TimeProvider.System, new NoSeasons());
            return new OpenClubSquadHandler(ClubUser, stores, Matches, Unit);
        }

        public RecordMatchSheetHandler Record(ICurrentUser user) =>
            new(user, Coaches, Clubs, SquadStore, Matches, Teams, Competitions, Players, PerformanceStore, TeamStats, Unit, TimeProvider.System);

        public BeginVideoUploadHandler BeginUpload() =>
            new(ClubUser, Coaches, Clubs, SquadStore, Matches,
                Stub<IMatchVideoStore>((method, _) => throw new NotSupportedException(method)),
                Stub<IVideoUploadStore>((method, _) => throw new NotSupportedException(method)),
                Stub<IVideoStorage>((method, _) => throw new NotSupportedException(method)),
                Microsoft.Extensions.Options.Options.Create(new VideoOptions()), Unit, TimeProvider.System);
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, StoreProxy>();
        ((StoreProxy)(object)stub).Call = call;
        return stub;
    }

    public class StoreProxy : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
