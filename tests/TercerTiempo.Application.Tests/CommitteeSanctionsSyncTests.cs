using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sanctions;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Sanctions;

namespace TercerTiempo.Application.Tests;

// La descarga de las sanciones del comité: la página de un grupo solo se pide cuando puede haber algo nuevo (para no
// gastar peticiones a la federación) y lo publicado se guarda tal cual, también lo que cambia o se retira.
public class CommitteeSanctionsSyncTests
{
    // Viernes; la jornada 10 se jugó el domingo y la 11 es este domingo.
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Sunday10 = new(2026, 11, 15, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Sunday11 = new(2026, 11, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_group_is_asked_for_after_the_committee_meets_and_otherwise_once_a_week()
    {
        var played = (Matchday: 11, LastKickoff: Sunday11);
        Assert.True(SyncCommitteeSanctionsHandler.IsDue(null, played, Now));

        // Publicado hasta la 10: la 11 no se busca hasta 40 horas después de su último partido…
        var check = CommitteeSanctionCheck.First(Guid.NewGuid(), 10, Now);
        Assert.False(SyncCommitteeSanctionsHandler.IsDue(check, played, Sunday11.AddHours(39)));
        Assert.True(SyncCommitteeSanctionsHandler.IsDue(check, played, Sunday11.AddHours(40)));

        // …y luego como mucho cada 12 horas mientras no sale.
        check.Checked(10, Sunday11.AddHours(40));
        Assert.False(SyncCommitteeSanctionsHandler.IsDue(check, played, Sunday11.AddHours(51)));
        Assert.True(SyncCommitteeSanctionsHandler.IsDue(check, played, Sunday11.AddHours(52)));

        // Ya publicada: una vez por semana, por si corrige algo.
        check.Checked(11, Sunday11.AddHours(52));
        Assert.False(SyncCommitteeSanctionsHandler.IsDue(check, played, Sunday11.AddHours(52).AddDays(6)));
        Assert.True(SyncCommitteeSanctionsHandler.IsDue(check, played, Sunday11.AddHours(52).AddDays(7)));
    }

    [Fact]
    public void A_match_brought_forward_does_not_make_its_matchday_played()
    {
        var season = Guid.NewGuid();
        List<Match> matches =
        [
            .. Enumerable.Range(0, 4).Select(_ => Game(season, 10, Sunday10)),
            .. Enumerable.Range(0, 3).Select(_ => Game(season, 11, Sunday11)),
            Game(season, 11, Now.AddDays(-1)),
        ];

        Assert.Equal((10, Sunday10), SyncCommitteeSanctionsHandler.LatestPlayed(matches, Now));
        Assert.Null(SyncCommitteeSanctionsHandler.LatestPlayed([Game(season, 1, Now.AddHours(1))], Now));
    }

    [Fact]
    public async Task What_the_page_publishes_is_stored_and_followed_when_it_changes()
    {
        var f = new Fixture();
        f.Pages.Enqueue(
        [
            Meeting(new DateOnly(2026, 9, 15), 1, Sanction("GARCIA, IYAN", 1)),
            Meeting(new DateOnly(2026, 11, 17), 10, Sanction("SUAREZ, NEL", 2), Sanction("MENENDEZ, XUAN", 1)),
        ]);

        var first = await f.Run();

        Assert.Equal((1, 1, 2, 0, 0), (first.Groups, first.Checked, first.Added, first.Changed, first.Removed));
        Assert.Equal(3, f.Store.Resolutions.Sum(r => r.Sanctions.Count));
        Assert.Equal(10, Assert.Single(f.Store.Checks).PublishedMatchday);

        // Una hora después no toca: ni una petición.
        f.Clock.Now = Now.AddHours(1);
        Assert.Equal(0, (await f.Run()).Checked);
        Assert.Equal(1, f.Requests);

        // Jugada la 11, el martes ya puede estar: el comité rebaja la de la jornada 1, retira la de la 10 y publica la 11.
        f.Clock.Now = Sunday11.AddHours(41);
        f.Pages.Enqueue(
        [
            Meeting(new DateOnly(2026, 9, 15), 1, Sanction("GARCIA, IYAN", 2)),
            Meeting(new DateOnly(2026, 11, 24), 11, Sanction("FERNANDEZ, PELAYO", 3)),
        ]);

        var later = await f.Run();

        Assert.Equal((1, 1, 1, 1), (later.Checked, later.Added, later.Changed, later.Removed));
        Assert.Equal([(1, 2), (11, 3)], f.Store.Resolutions.OrderBy(r => r.Matchday).Select(r => (r.Matchday, r.Sanctions.Single().Matches)));
        Assert.Equal((11, f.Clock.Now), (f.Store.Checks.Single().PublishedMatchday, f.Store.Checks.Single().CheckedAt));
    }

    private static Match Game(Guid season, int matchday, DateTimeOffset kickoff) =>
        Match.Schedule(season, matchday, Guid.NewGuid(), Guid.NewGuid(), kickoff, null, null);

    private static CommitteeMeetingData Meeting(DateOnly on, int matchday, params CommitteeSanctionData[] sanctions) =>
        new("Comité Territorial de Competición", on, matchday, null, sanctions);

    private static CommitteeSanctionData Sanction(string name, int matches) => new("CD Vallobín", name, "105", "Agredir a otro", matches);

    private sealed class Fixture
    {
        private readonly Competition _competition = Competition.Create("Primera Asturfútbol", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        private readonly Season _season;
        private readonly List<Match> _matches;

        public ManualClock Clock { get; } = new() { Now = Now };
        public FakeCommitteeStore Store { get; } = new();
        public Queue<IReadOnlyList<CommitteeMeetingData>> Pages { get; } = new();
        public int Requests { get; private set; }

        public Fixture()
        {
            _season = _competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
            _competition.MarkCurrent(_season.Id);
            _season.LinkOfficial(ExternalRef.Create("asturfutbol", "1000/2000/21"));
            _matches = [Game(_season.Id, 10, Sunday10), Game(_season.Id, 10, Sunday10), Game(_season.Id, 11, Sunday11), Game(_season.Id, 11, Sunday11)];
        }

        public Task<CommitteeSanctionsSynced> Run() => new SyncCommitteeSanctionsHandler(
            TestStub.Of<ICompetitionStore>((method, _) => method == nameof(ICompetitionStore.GetAllAsync)
                ? Task.FromResult<IReadOnlyList<Competition>>([_competition])
                : throw new NotSupportedException(method)),
            TestStub.Of<IMatchStore>((method, args) => method == nameof(IMatchStore.GetBySeasonAsync) && (Guid)args![0]! == _season.Id
                ? Task.FromResult<IReadOnlyList<Match>>(_matches)
                : throw new NotSupportedException(method)),
            Store,
            TestStub.Of<IOfficialSheetProvider>((method, args) => method switch
            {
                "get_" + nameof(IOfficialSheetProvider.ProviderName) => "asturfutbol",
                nameof(IOfficialSheetProvider.GetSanctionsAsync) when ((OfficialCompetitionRef)args![0]!).GroupCode == "2000" => Request(),
                _ => throw new NotSupportedException(method),
            }),
            new JobProgress(TimeProvider.System),
            TestStub.Of<IUnitOfWork>((_, _) => Task.FromResult(1)),
            Clock).Handle(new(), default);

        private Task<IReadOnlyList<CommitteeMeetingData>?> Request()
        {
            Requests++;
            return Task.FromResult<IReadOnlyList<CommitteeMeetingData>?>(Pages.Dequeue());
        }
    }
}
