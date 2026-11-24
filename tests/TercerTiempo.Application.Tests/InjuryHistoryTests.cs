using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Medical;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// El historial de lesiones de cada jugador: lo que tardó de verdad del parte al alta, también en otras temporadas del
// club, con las recaídas aparte y sin mezclar a dos personas.
public class InjuryHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = SpainTime.Today(Now);

    [Fact]
    public void The_same_person_is_found_in_other_seasons_and_teams_of_the_club_without_mixing_people()
    {
        var team = Guid.NewGuid();
        var otherTeam = Guid.NewGuid();
        var account = Guid.NewGuid();
        var squad = Squad.Open(team, Guid.NewGuid(), Now);
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now);
        squad.LinkPlayer(pelayo.Id, account);

        var lastSeason = Squad.Open(team, Guid.NewGuid(), Now);
        var then = lastSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now);
        var bTeam = Squad.Open(otherTeam, Guid.NewGuid(), Now);
        var linked = bTeam.AddPlayer("Pelayo F.", Position.Forward, null, null, Now);
        bTeam.LinkPlayer(linked.Id, account);
        // Dos fichas que encajan con su nombre: no se sabe cuál es, así que ninguna.
        var doubtful = Squad.Open(team, Guid.NewGuid(), Now);
        doubtful.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now);
        doubtful.AddPlayer("Pelayo Fernández García", Position.Defender, null, null, Now);
        // Mismo nombre en otro equipo y sin cuenta, o con la cuenta de otro: otra persona.
        var otherTeamSameName = Squad.Open(otherTeam, Guid.NewGuid(), Now);
        otherTeamSameName.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now);
        var someoneElse = Squad.Open(team, Guid.NewGuid(), Now);
        var other = someoneElse.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now);
        someoneElse.LinkPlayer(other.Id, Guid.NewGuid());

        var found = InjuryHistory.SamePersonIn(squad, pelayo, [squad, lastSeason, bTeam, doubtful, otherTeamSameName, someoneElse]);

        Assert.Equal(new[] { then.Id, linked.Id }.Order(), found.Select(f => f.Player.Id).Order());
    }

    [Fact]
    public async Task The_profile_shows_the_injuries_of_every_season_with_the_real_days_and_only_to_who_may_see_them()
    {
        var f = new Fixture();
        var old = f.Injure(f.LastSeason, f.PelayoThen, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 30), returnedOn: new DateOnly(2026, 4, 6));
        var current = f.Injure(f.Squad, f.Pelayo, Today.AddDays(-5), Today.AddDays(55));
        f.Injure(f.Squad, f.Nel, Today.AddDays(-2), null);

        var history = await f.History(f.ClubUser).Handle(new GetPlayerInjuryHistoryQuery(f.Squad.Id, f.Pelayo.Id), default);

        Assert.Equal([current.Id, old.Id], history.Injuries.Select(i => i.Injury.Id).ToList());
        var now = history.Injuries[0];
        Assert.Equal((false, 5, 60, 55), (now.OtherSquad, now.Injury.DaysOut, now.Injury.ExpectedDays, now.Injury.DaysLeft));
        var before = history.Injuries[1];
        Assert.Equal((true, "2025/2026", 35, 28), (before.OtherSquad, before.SeasonLabel, before.Injury.DaysOut, before.Injury.InitialExpectedDays));
        Assert.Equal((2, 1, 40, 35.0, 0), (history.Totals.Injuries, history.Totals.Recovered, history.Totals.DaysOut, history.Totals.AverageRecoveryDays, history.Totals.Relapses));

        // Él mismo también lo ve; un compañero, no.
        Assert.Equal(2, (await f.History(f.PelayoUser).Handle(new GetPlayerInjuryHistoryQuery(f.Squad.Id, f.Pelayo.Id), default)).Injuries.Count);
        var denied = await Assert.ThrowsAsync<ForbiddenException>(() => f.History(f.NelUser).Handle(new GetPlayerInjuryHistoryQuery(f.Squad.Id, f.Pelayo.Id), default));
        Assert.Equal(ErrorCode.InjuryPrivate, denied.Code);
    }

    [Fact]
    public async Task The_return_is_a_day_already_lived_and_a_relapse_keeps_it()
    {
        var f = new Fixture();
        var injury = f.Injure(f.Squad, f.Pelayo, Today.AddDays(-30), Today.AddDays(-9));

        var future = await Assert.ThrowsAsync<DomainException>(() => f.Close().Handle(new CloseInjuryCommand(f.Squad.Id, injury.Id, Today.AddDays(1)), default));
        Assert.Equal(ErrorCode.InjuryReturnInFuture, future.Code);

        await f.Close().Handle(new CloseInjuryCommand(f.Squad.Id, injury.Id, Today.AddDays(-10)), default);
        var list = await f.Relapse().Handle(new RecordRelapseCommand(f.Squad.Id, injury.Id, Today.AddDays(-2), null, "Otra vez"), default);

        var relapse = list.Single(i => i.RelapseOfInjuryId == injury.Id);
        Assert.Equal((true, 2, Today.AddDays(19)), (relapse.Active, relapse.DaysOut, relapse.ExpectedReturnOn));
        var first = list.Single(i => i.Id == injury.Id);
        Assert.Equal((Today.AddDays(-10), 20), (first.ReturnedOn, first.DaysOut));

        // Con la recaída detrás, aquel alta fue real: no se deshace.
        var reopen = await Assert.ThrowsAsync<ConflictException>(() => f.Reopen().Handle(new ReopenInjuryCommand(f.Squad.Id, injury.Id), default));
        Assert.Equal(ErrorCode.InjuryAlreadyActive, reopen.Code);
        await f.Close().Handle(new CloseInjuryCommand(f.Squad.Id, relapse.Id, Today), default);
        var blocked = await Assert.ThrowsAsync<ConflictException>(() => f.Reopen().Handle(new ReopenInjuryCommand(f.Squad.Id, injury.Id), default));
        Assert.Equal(ErrorCode.InjuryHasRelapse, blocked.Code);

        // Borrar la primera (registrada por error) deja la recaída como una lesión sin más.
        var after = await f.Delete().Handle(new DeleteInjuryCommand(f.Squad.Id, injury.Id), default);
        Assert.Null(Assert.Single(after).RelapseOfInjuryId);
    }

    [Fact]
    public async Task The_report_cannot_be_dated_after_today()
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            f.Record().Handle(new RecordInjuryCommand(f.Squad.Id, f.Pelayo.Id, InjuryTypeCodes.Fracture, null, Today.AddDays(1), null), default));

        Assert.Equal(ErrorCode.InjuryStartInFuture, error.Code);
    }

    private sealed record Account(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null, AccountType? Type = null, bool Admin = false) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Type;
        public bool IsAdmin => Admin;
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Coach Coach { get; } = Coach.Register(Guid.NewGuid(), "Abel Pablo", Now);
        public Competition Competition { get; } = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        public Player PelayoAccount { get; } = Player.Register(Guid.NewGuid(), "Pelayo Fernández", Position.Forward, null, Now);
        public Player NelAccount { get; } = Player.Register(Guid.NewGuid(), "Nel Suárez", Position.Goalkeeper, null, Now);
        public Squad Squad { get; }
        public Squad LastSeason { get; }
        public SquadPlayer Pelayo { get; }
        public SquadPlayer PelayoThen { get; }
        public SquadPlayer Nel { get; }
        public List<PlayerInjury> Injuries { get; } = [];

        public ICurrentUser ClubUser => new Account(ClubId: Club.Id, UserId: Club.UserId, Type: AccountType.Club);
        public ICurrentUser CoachUser => new Account(CoachId: Coach.Id, UserId: Coach.UserId, Type: AccountType.Coach);
        public ICurrentUser PelayoUser => new Account(PlayerId: PelayoAccount.Id, UserId: PelayoAccount.UserId, Type: AccountType.Player);
        public ICurrentUser NelUser => new Account(PlayerId: NelAccount.Id, UserId: NelAccount.UserId, Type: AccountType.Player);

        public Fixture()
        {
            var current = Competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
            var previous = Competition.AddSeason("2025/2026", new(2025, 8, 1), new(2026, 6, 30), null);
            Club.AddTeam(Team.Id, Now);
            Squad = Squad.Create(Team.Id, current.Id, Coach.Id, Now);
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now);
            Nel = Squad.AddPlayer("Nel Suárez", Position.Goalkeeper, null, null, Now);
            Squad.LinkPlayer(Pelayo.Id, PelayoAccount.Id);
            Squad.LinkPlayer(Nel.Id, NelAccount.Id);
            // La temporada pasada no tenía cuenta: se le encuentra por el nombre en el mismo equipo.
            LastSeason = Squad.Open(Team.Id, previous.Id, Now.AddYears(-1));
            PelayoThen = LastSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now.AddYears(-1));
        }

        public PlayerInjury Injure(Squad squad, SquadPlayer player, DateOnly startedOn, DateOnly? expected, DateOnly? returnedOn = null)
        {
            var reportedAt = new DateTimeOffset(startedOn.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
            var injury = PlayerInjury.Create(squad, player, SquadEditor.Club(Club), TestInjuryTypes.Of(InjuryTypeCodes.AnkleSprain), null, startedOn, expected, reportedAt);
            if (returnedOn is { } day) injury.Close(day, reportedAt.AddDays(day.DayNumber - startedOn.DayNumber));
            Injuries.Add(injury);
            return injury;
        }

        public GetPlayerInjuryHistoryHandler History(ICurrentUser user) => new(user, Squads, Clubs, InjuryStore, DocumentStore, Teams, Competitions, Clock);
        public CloseInjuryHandler Close() => new(CoachUser, Squads, Clubs, InjuryStore, DocumentStore, Unit, Clock);
        public ReopenInjuryHandler Reopen() => new(CoachUser, Squads, Clubs, InjuryStore, DocumentStore, Unit, Clock);
        public RecordRelapseHandler Relapse() => new(CoachUser, Squads, Coaches, Clubs, InjuryStore, DocumentStore, Unit, Clock);
        public DeleteInjuryHandler Delete() => new(CoachUser, Squads, Clubs, InjuryStore, DocumentStore, Storage, Unit, Clock);
        public RecordInjuryHandler Record() => new(CoachUser, Squads, Coaches, Clubs, InjuryStore, Types, DocumentStore, Unit, Clock);

        private static TimeProvider Clock => new FixedClock();

        private ISquadStore Squads => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult(new[] { Squad, LastSeason }.FirstOrDefault(s => s.Id == (Guid)args![0]!)),
            nameof(ISquadStore.GetByTeamIdsAsync) when args!.Length == 2 => Task.FromResult<IReadOnlyList<Squad>>(
                new[] { Squad, LastSeason }.Where(s => ((IEnumerable<Guid>)args[0]!).Contains(s.TeamId)).ToList()),
            _ => throw new NotSupportedException(method),
        });

        private IClubStore Clubs => Stub<IClubStore>((method, args) => method switch
        {
            nameof(IClubStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Club.Id ? Club : null),
            nameof(IClubStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Club.UserId ? Club : null),
            nameof(IClubStore.GetByTeamAsync) => Task.FromResult(Club.Manages((Guid)args![0]!) ? Club : null),
            _ => throw new NotSupportedException(method),
        });

        private ICoachStore Coaches => Stub<ICoachStore>((method, args) => method switch
        {
            nameof(ICoachStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Coach.UserId ? Coach : null),
            _ => throw new NotSupportedException(method),
        });

        private ITeamStore Teams => Stub<ITeamStore>((method, args) => method switch
        {
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team]),
            _ => throw new NotSupportedException(method),
        });

        private ICompetitionStore Competitions => Stub<ICompetitionStore>((method, args) => method switch
        {
            nameof(ICompetitionStore.GetBySeasonIdsAsync) => Task.FromResult<IReadOnlyList<Competition>>([Competition]),
            _ => throw new NotSupportedException(method),
        });

        private IInjuryTypeStore Types => Stub<IInjuryTypeStore>((method, args) => method switch
        {
            nameof(IInjuryTypeStore.GetByCodeAsync) => Task.FromResult<InjuryTypeDefinition?>(TestInjuryTypes.Of((string)args![0]!)),
            _ => throw new NotSupportedException(method),
        });

        private IInjuryStore InjuryStore => Stub<IInjuryStore>((method, args) => method switch
        {
            nameof(IInjuryStore.GetByIdAsync) => Task.FromResult(Injuries.FirstOrDefault(i => i.Id == (Guid)args![0]!)),
            nameof(IInjuryStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<PlayerInjury>>(Injuries.Where(i => i.SquadId == (Guid)args![0]!).ToList()),
            nameof(IInjuryStore.GetBySquadPlayersAsync) => Task.FromResult<IReadOnlyList<PlayerInjury>>(
                Injuries.Where(i => ((IReadOnlyCollection<Guid>)args![0]!).Contains(i.SquadPlayerId)).ToList()),
            nameof(IInjuryStore.Add) => Add((PlayerInjury)args![0]!),
            nameof(IInjuryStore.Remove) => Injuries.Remove((PlayerInjury)args![0]!),
            _ => throw new NotSupportedException(method),
        });

        private object? Add(PlayerInjury injury)
        {
            Injuries.Add(injury);
            return null;
        }

        private static IInjuryDocumentStore DocumentStore => Stub<IInjuryDocumentStore>((method, _) => method switch
        {
            nameof(IInjuryDocumentStore.GetByInjuryIdsAsync) => Task.FromResult<IReadOnlyList<InjuryDocument>>([]),
            _ => throw new NotSupportedException(method),
        });

        private static IInjuryDocumentStorage Storage => Stub<IInjuryDocumentStorage>((_, _) => Task.CompletedTask);

        private static IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(1));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}
