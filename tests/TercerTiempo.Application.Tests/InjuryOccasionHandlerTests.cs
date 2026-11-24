using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.MatchReports;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Tests;

// Dónde se lesionó: el partido o el entrenamiento se guarda con la lesión. Enlazada a un partido, la posible lesión de su
// crónica queda registrada sola y los sucesos del partido la dan por registrada; llevada a otro sitio, aquella queda
// descartada.
public class InjuryOccasionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Linking_it_to_the_match_registers_the_proposal_of_its_report()
    {
        var f = new Fixture();
        var proposal = f.Propose(f.Pelayo);

        var list = await f.Record().Handle(
            new RecordInjuryCommand(f.Squad.Id, f.Pelayo.Id, InjuryTypeCodes.Contusion, "Luxación de codo", f.MatchDay, null, MatchId: f.Match.Id), default);

        var injury = Assert.Single(list);
        Assert.Equal((InjuryOccasionKind.Match, f.Match.Id, f.MatchDay), (injury.Occasion!.Kind, injury.Occasion.Id, injury.Occasion.Day));
        Assert.Equal("J11 · Europa de Nava – CD Vallobín", injury.Occasion.Label);
        Assert.Equal((InjuryProposalStatus.Registered, (Guid?)injury.Id), (proposal.Status, proposal.InjuryId));
        Assert.NotNull(f.Alerts.Single().ResolvedAt);

        var events = await f.MatchInjuries().Handle(new ListMatchInjuriesQuery(f.Squad.Id, f.Match.Id), default);
        var registered = Assert.Single(events);
        Assert.Equal((true, (Guid?)injury.Id, (int?)9, "Luxación de codo"), (registered.Confirmed, registered.InjuryId, registered.Minute, registered.Phrase));
    }

    [Fact]
    public async Task One_recorded_by_hand_shows_as_registered_once_it_is_linked_to_the_match()
    {
        var f = new Fixture();
        var proposal = f.Propose(f.Pelayo);
        var recorded = Assert.Single(await f.Record().Handle(
            new RecordInjuryCommand(f.Squad.Id, f.Pelayo.Id, InjuryTypeCodes.Contusion, null, f.MatchDay.AddDays(1), null), default));

        var before = Assert.Single(await f.MatchInjuries().Handle(new ListMatchInjuriesQuery(f.Squad.Id, f.Match.Id), default));
        Assert.False(before.Confirmed);
        Assert.True(proposal.IsPending);

        await f.Update().Handle(
            new UpdateInjuryCommand(f.Squad.Id, recorded.Id, InjuryTypeCodes.Contusion, null, f.MatchDay.AddDays(1), null, MatchId: f.Match.Id), default);

        var after = Assert.Single(await f.MatchInjuries().Handle(new ListMatchInjuriesQuery(f.Squad.Id, f.Match.Id), default));
        Assert.Equal((true, (Guid?)recorded.Id, (int?)9), (after.Confirmed, after.InjuryId, after.Minute));
        Assert.Equal(InjuryProposalStatus.Registered, proposal.Status);
    }

    [Fact]
    public async Task Saying_it_was_in_a_training_dismisses_the_proposal_registered_with_it()
    {
        var f = new Fixture();
        var proposal = f.Propose(f.Pelayo);
        var injury = Assert.Single(await f.Record().Handle(
            new RecordInjuryCommand(f.Squad.Id, f.Pelayo.Id, InjuryTypeCodes.Contusion, null, f.MatchDay, null, ProposalId: proposal.Id), default));
        Assert.Equal(f.Match.Id, injury.Occasion!.Id);

        var training = f.Train(f.Squad, Now.AddDays(-1));
        var moved = Assert.Single(await f.Update().Handle(
            new UpdateInjuryCommand(f.Squad.Id, injury.Id, InjuryTypeCodes.Contusion, null, SpainTime.Today(training.StartsAt), null, TrainingSessionId: training.Id),
            default));

        Assert.Equal((InjuryOccasionKind.Training, training.Id, training.Title), (moved.Occasion!.Kind, moved.Occasion.Id, moved.Occasion.Label));
        Assert.Equal(InjuryProposalStatus.Dismissed, proposal.Status);
        Assert.Empty(await f.MatchInjuries().Handle(new ListMatchInjuriesQuery(f.Squad.Id, f.Match.Id), default));
    }

    [Fact]
    public async Task The_match_or_training_has_to_be_of_the_squad_already_played_and_not_after_the_report()
    {
        var f = new Fixture();
        var training = f.Train(f.Squad, Now.AddDays(-1));
        var later = f.Train(f.Squad, Now.AddDays(1));
        var elsewhere = f.Train(f.OtherSquad, Now.AddDays(-1));
        var proposal = f.Propose(f.Pelayo);

        Task<IReadOnlyList<InjuryDto>> Record(DateOnly startedOn, Guid? match = null, Guid? session = null, Guid? from = null) =>
            f.Record().Handle(new RecordInjuryCommand(f.Squad.Id, f.Pelayo.Id, InjuryTypeCodes.Contusion, null, startedOn, null, from, match, session), default);

        var today = SpainTime.Today(Now);
        Assert.Equal(ErrorCode.InjuryOccasionInvalid, (await Assert.ThrowsAsync<DomainException>(() => Record(today, f.Match.Id, training.Id))).Code);
        Assert.Equal(ErrorCode.InjuryOccasionNotFound, (await Assert.ThrowsAsync<DomainException>(() => Record(today, session: later.Id))).Code);
        Assert.Equal(ErrorCode.InjuryOccasionNotFound, (await Assert.ThrowsAsync<DomainException>(() => Record(today, session: elsewhere.Id))).Code);
        Assert.Equal(ErrorCode.InjuryBeforeOccasion, (await Assert.ThrowsAsync<DomainException>(() => Record(f.MatchDay.AddDays(-1), f.Match.Id))).Code);
        Assert.Equal(ErrorCode.InjuryProposalOccasion,
            (await Assert.ThrowsAsync<DomainException>(() => Record(today, session: training.Id, from: proposal.Id))).Code);
        Assert.Empty(f.Injuries);
    }

    [Fact]
    public async Task The_list_to_choose_from_has_the_played_matches_and_started_trainings_most_recent_first()
    {
        var f = new Fixture();
        var training = f.Train(f.Squad, Now.AddDays(-1));
        f.Train(f.Squad, Now.AddDays(1));

        var options = await f.Occasions().Handle(new ListInjuryOccasionsQuery(f.Squad.Id), default);

        Assert.Equal([training.Id, f.Match.Id], options.Select(o => o.Id));
        Assert.Equal([InjuryOccasionKind.Training, InjuryOccasionKind.Match], options.Select(o => o.Kind));
    }

    private sealed record Account(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null, AccountType? Type = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Type;
        public bool IsAdmin => false;
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Rival { get; } = Team.Create("Europa de Nava", "EUR", null);
        public Team OtherTeam { get; } = Team.Create("CD Vallobín B", "VAB", null);
        public Coach Coach { get; } = Coach.Register(Guid.NewGuid(), "Abel Pablo", Now);
        public Squad Squad { get; }
        public Squad OtherSquad { get; }
        public SquadPlayer Pelayo { get; }
        public Match Match { get; }
        public DateOnly MatchDay => SpainTime.Today(Match.KickoffAt);
        public List<PlayerInjury> Injuries { get; } = [];
        public List<InjuryProposal> Proposals { get; } = [];
        public List<TrainingSession> Sessions { get; } = [];
        public List<Alert> Alerts { get; } = [];

        private ICurrentUser CoachUser => new Account(CoachId: Coach.Id, UserId: Coach.UserId, Type: AccountType.Coach);

        public Fixture()
        {
            var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
            var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
            Club.AddTeam(Team.Id, Now);
            Club.AddTeam(OtherTeam.Id, Now);
            Squad = Squad.Create(Team.Id, season.Id, Coach.Id, Now.AddDays(-60));
            OtherSquad = Squad.Create(OtherTeam.Id, season.Id, Coach.Id, Now.AddDays(-60));
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now.AddDays(-60));
            Match = Match.Schedule(season.Id, 11, Rival.Id, Team.Id, Now.AddDays(-2), null, null);
        }

        // La crónica del partido cuenta que se lesionó y salió en el minuto 9; el aviso le llega al club.
        public InjuryProposal Propose(SquadPlayer player)
        {
            var mention = MatchInjuryMention.Create(
                Match.Id, MatchSide.Away, "FERNÁNDEZ, PELAYO", 9, 9, "Pelayo sufrió una luxación en el codo.", "luxación en el codo", "futbolasturiano",
                "https://example.com/cronica", Now.AddDays(-1));
            var proposal = InjuryProposal.Create(Squad.Id, player.Id, mention, Now.AddDays(-1));
            Proposals.Add(proposal);
            Alerts.Add(Alert.Raise(Club.Id, AlertKind.InjuryProposal, InjuryProposalAccess.KeyFor(proposal.Id), "Posible lesión", "…", null, null, Now.AddDays(-1)));
            return proposal;
        }

        public TrainingSession Train(Squad squad, DateTimeOffset startsAt)
        {
            var session = TrainingSession.Plan(squad, SquadEditor.Coach(Coach.Id), TrainingKind.Season, null, startsAt, 90, null, null, Now.AddDays(-7));
            Sessions.Add(session);
            return session;
        }

        public RecordInjuryHandler Record() =>
            new(CoachUser, Squads, Coaches, Clubs, InjuryStore, Types, DocumentStore, ProposalStore, AlertStore, Matches, Teams, Trainings, Unit, Clock, new MemoryBodySites());

        public UpdateInjuryHandler Update() =>
            new(CoachUser, Squads, Coaches, Clubs, InjuryStore, Types, DocumentStore, ProposalStore, AlertStore, Matches, Teams, Trainings, Unit, Clock, new MemoryBodySites());

        public ListMatchInjuriesHandler MatchInjuries() => new(CoachUser, Squads, Clubs, ProposalStore, InjuryStore, Matches, Sheets, Clock);

        public ListInjuryOccasionsHandler Occasions() => new(CoachUser, Squads, Clubs, Matches, Teams, Trainings, Clock);

        private static TimeProvider Clock => new FixedClock();

        private ISquadStore Squads => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult(new[] { Squad, OtherSquad }.FirstOrDefault(s => s.Id == (Guid)args![0]!)),
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
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>(
                new[] { Team, Rival, OtherTeam }.Where(t => ((IEnumerable<Guid>)args![0]!).Contains(t.Id)).ToList()),
            _ => throw new NotSupportedException(method),
        });

        private IMatchStore Matches => Stub<IMatchStore>((method, args) => method switch
        {
            nameof(IMatchStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Match.Id ? Match : null),
            nameof(IMatchStore.GetTeamMatchesAsync) => Task.FromResult<IReadOnlyList<Match>>(
                (Guid)args![0]! == Match.SeasonId && (Guid)args[1]! == Team.Id ? [Match] : []),
            _ => throw new NotSupportedException(method),
        });

        private ITrainingStore Trainings => Stub<ITrainingStore>((method, args) => method switch
        {
            nameof(ITrainingStore.GetByIdAsync) => Task.FromResult(Sessions.FirstOrDefault(s => s.Id == (Guid)args![0]!)),
            nameof(ITrainingStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<TrainingSession>>(Sessions
                .Where(s => s.SquadId == (Guid)args![0]! && s.StartsAt >= (DateTimeOffset)args[1]! && s.StartsAt < (DateTimeOffset)args[2]!)
                .ToList()),
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
            nameof(IInjuryStore.Add) => Add((PlayerInjury)args![0]!),
            _ => throw new NotSupportedException(method),
        });

        private object? Add(PlayerInjury injury)
        {
            Injuries.Add(injury);
            return null;
        }

        private IInjuryProposalStore ProposalStore => Stub<IInjuryProposalStore>((method, args) => method switch
        {
            nameof(IInjuryProposalStore.GetByIdAsync) => Task.FromResult(Proposals.FirstOrDefault(p => p.Id == (Guid)args![0]!)),
            nameof(IInjuryProposalStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<InjuryProposal>>(Proposals.Where(p => p.SquadId == (Guid)args![0]!).ToList()),
            nameof(IInjuryProposalStore.GetByMatchesAsync) => Task.FromResult<IReadOnlyList<InjuryProposal>>(
                Proposals.Where(p => ((IReadOnlyCollection<Guid>)args![0]!).Contains(p.MatchId)).ToList()),
            _ => throw new NotSupportedException(method),
        });

        private IAlertStore AlertStore => Stub<IAlertStore>((method, args) => method switch
        {
            nameof(IAlertStore.GetOpenByKeyPrefixAsync) => Task.FromResult<IReadOnlyList<Alert>>(
                Alerts.Where(a => a.ResolvedAt is null && a.Key.StartsWith((string)args![0]!, StringComparison.Ordinal)).ToList()),
            _ => throw new NotSupportedException(method),
        });

        // Sin acta oficial: el minuto sale de la crónica.
        private static IOfficialSheetStore Sheets => Stub<IOfficialSheetStore>((method, _) => method switch
        {
            nameof(IOfficialSheetStore.GetAvailableLinesByMatchIdsAsync) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([]),
            _ => throw new NotSupportedException(method),
        });

        private static IInjuryDocumentStore DocumentStore => Stub<IInjuryDocumentStore>((method, _) => method switch
        {
            nameof(IInjuryDocumentStore.GetByInjuryIdsAsync) => Task.FromResult<IReadOnlyList<InjuryDocument>>([]),
            _ => throw new NotSupportedException(method),
        });

        private static IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(1));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = System.Reflection.DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}
