using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Referees;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

// Equipo arbitral de cada partido (de la federación y de futbolasturiano.es) y notas de los equipos.
public class RefereeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_federation_sheet_lists_the_referee_and_both_assistants()
    {
        var sheet = AsturfutbolPages.ParseSheet(Page("acta-25744748.html"), matchLength: 90)!;

        Assert.Equal("Ramos Cifuentes, Ángel", sheet.Referee);
        Assert.Equal(
            [(OfficialRole.Referee, "Ramos Cifuentes, Ángel"), (OfficialRole.AssistantReferee1, "Caravia González, Yeray"), (OfficialRole.AssistantReferee2, "Marcos García, Fernando")],
            sheet.Officials!.Select(o => (o.Role, o.Name)).ToList());
    }

    [Fact]
    public void Futbolasturiano_lists_its_officials_with_the_slug_of_their_page()
    {
        const string html = """
            <div class="sp-template sp-template-event-officials"><table class="sp-event-officials sp-data-table">
            <thead><tr><th class="data-name">Árbitro</th><th class="data-name">Asistente 1</th><th class="data-name">Asistente 2</th></tr></thead>
            <tbody><tr>
            <td class="data-name"><a href="https://www.futbolasturiano.es/official/sara-ovies-garcia/">Sara Ovies García</a></td>
            <td class="data-name"><a href="https://www.futbolasturiano.es/official/alberto-gil-martinez/">Alberto Gil Martínez</a></td>
            <td class="data-name"><a href="https://www.futbolasturiano.es/official/alvaro-rodriguez-rodriguez/">Álvaro Rodríguez Rodríguez</a></td>
            </tr></tbody></table></div>
            """;

        var officials = EventPageParser.ParseOfficials(html);

        Assert.Equal(
            [
                new MatchOfficialData(OfficialRole.Referee, "Sara Ovies García", "sara-ovies-garcia"),
                new MatchOfficialData(OfficialRole.AssistantReferee1, "Alberto Gil Martínez", "alberto-gil-martinez"),
                new MatchOfficialData(OfficialRole.AssistantReferee2, "Álvaro Rodríguez Rodríguez", "alvaro-rodriguez-rodriguez"),
            ],
            officials);
        Assert.Empty(EventPageParser.ParseOfficials("<html><body>Sin equipo arbitral</body></html>"));
    }

    [Fact]
    public void Both_websites_name_the_same_person_alike()
    {
        Assert.Equal("Sara Ovies García", Referee.DisplayName("Ovies García, Sara"));
        Assert.Equal("Ángel Ramos Cifuentes", Referee.DisplayName("RAMOS CIFUENTES, ÁNGEL"));
        Assert.Equal("David del Valle Prieto", Referee.DisplayName("Del Valle Prieto, David"));
        Assert.Equal("Juan de la Rosa Velasco", Referee.DisplayName("DE LA ROSA VELASCO, JUAN"));
        Assert.Equal(Referee.Normalize("Sara Ovies García"), Referee.Normalize("Ovies García, Sara"));
    }

    [Fact]
    public async Task The_federation_overrides_futbolasturiano_which_only_fills_gaps_and_adds_slugs()
    {
        var store = new InMemoryRefereeStore();
        var writer = new MatchOfficialsWriter(store, TimeProvider.System);
        var matchId = Guid.NewGuid();

        await writer.ApplyAsync(matchId, [new(OfficialRole.Referee, "Sara Ovies García", "sara-ovies-garcia"), new(OfficialRole.AssistantReferee1, "Pepe Pérez")], "futbolasturiano", authoritative: false, default);
        await writer.ApplyAsync(matchId, [new(OfficialRole.Referee, "Ovies García, Sara"), new(OfficialRole.AssistantReferee1, "Gil Martínez, Alberto")], "asturfutbol", authoritative: true, default);
        await writer.ApplyAsync(matchId, [new(OfficialRole.AssistantReferee1, "Otro Nombre")], "futbolasturiano", authoritative: false, default);

        var sara = Assert.Single(store.Referees, r => r.Name == "Sara Ovies García");
        Assert.Equal("sara-ovies-garcia", sara.Slug);
        Assert.Equal(2, store.Officials.Count);
        Assert.Equal("Alberto Gil Martínez", store.Referees.Single(r => r.Id == store.Officials.Single(o => o.Role == OfficialRole.AssistantReferee1).RefereeId).Name);
    }

    [Fact]
    public void A_rating_goes_from_0_to_10_and_is_given_by_the_club_or_a_coach()
    {
        var official = MatchOfficial.Assign(Guid.NewGuid(), Guid.NewGuid(), OfficialRole.Referee, "asturfutbol", Now);

        var rating = RefereeRating.Rate(official, Guid.NewGuid(), 8, "  Buen criterio  ", Guid.NewGuid(), null, Now);
        Assert.Equal((8, "Buen criterio"), (rating.Score, rating.Comment));

        Assert.Equal(ErrorCode.RefereeScoreRange, Assert.Throws<DomainException>(() => rating.Change(11, null, Guid.NewGuid(), null, Now)).Code);
        Assert.Equal(ErrorCode.RefereeRaterRequired, Assert.Throws<DomainException>(() => rating.Change(5, null, null, null, Now)).Code);
    }

    [Fact]
    public async Task The_club_of_a_team_that_played_rates_each_official_once_and_can_change_it()
    {
        var f = new Fixture();

        await f.Rate(f.ClubUser, 7, "Bien colocada");
        var updated = await f.Rate(f.ClubUser, 9, null);

        var rating = Assert.Single(f.Store.Ratings);
        Assert.Equal((9, (string?)null, f.Home.Id), (rating.Score, rating.Comment, rating.TeamId));
        var mine = Assert.Single(updated.Officials);
        Assert.Equal((9, 9.0, 1), (mine.MyScore, mine.Average, mine.Ratings));
        Assert.Equal((true, f.Home.Id), (updated.CanRate, updated.RaterTeamId));
    }

    [Fact]
    public async Task Only_the_teams_that_played_can_rate_and_only_after_the_match()
    {
        var f = new Fixture();
        var outsider = Club.Register(Guid.NewGuid(), "Otro club", Now);
        outsider.AddTeam(Guid.NewGuid(), Now);
        f.Clubs.Add(outsider);

        var foreign = await Assert.ThrowsAsync<ForbiddenException>(() => f.Rate(new User(ClubId: outsider.Id), 5, null));
        Assert.Equal(ErrorCode.RefereeTeamNotInMatch, foreign.Code);

        f.Match.UpdateFixture(f.Match.Matchday, Now.AddDays(2), null);
        var early = await Assert.ThrowsAsync<ConflictException>(() => f.Rate(f.ClubUser, 5, null));
        Assert.Equal(ErrorCode.RefereeMatchNotOver, early.Code);
        Assert.Empty(f.Store.Ratings);
    }

    [Fact]
    public async Task Players_cannot_see_or_rate_referees()
    {
        var f = new Fixture();
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Rate(new User(PlayerId: Guid.NewGuid()), 5, null));
        Assert.Equal(ErrorCode.RefereeStaffOnly, error.Code);
    }

    private static string Page(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record User(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.Empty;
        public AccountType? AccountType => PlayerId != null ? Abstractions.Identity.AccountType.Player
            : CoachId != null ? Abstractions.Identity.AccountType.Coach : Abstractions.Identity.AccountType.Club;
    }

    private sealed class Fixture
    {
        public Team Home { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Away { get; } = Team.Create("C. Hispano", "HIS", null);
        public Match Match { get; }
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public List<Club> Clubs { get; } = [];
        public InMemoryRefereeStore Store { get; } = new();
        public User ClubUser => new(ClubId: Club.Id);

        public Fixture()
        {
            Match = Match.Schedule(Guid.NewGuid(), 1, Home.Id, Away.Id, Now.AddDays(-2), null, null);
            Match.RecordResult(1, 0, Now.AddDays(-2).AddHours(2));
            Club.AddTeam(Home.Id, Now);
            Clubs.Add(Club);
            var sara = Referee.Create("Ovies García, Sara", null, Now);
            Store.Referees.Add(sara);
            Store.Officials.Add(MatchOfficial.Assign(Match.Id, sara.Id, OfficialRole.Referee, "asturfutbol", Now));
        }

        public Task<MatchOfficialsDto> Rate(ICurrentUser user, int score, string? comment) =>
            new RateRefereeHandler(
                user,
                Stub<ILicenseGate>((_, _) => Task.FromResult(true)),
                Stub<IMatchStore>((_, args) => Task.FromResult<Match?>((Guid)args![0]! == Match.Id ? Match : null)),
                Stub<ITeamStore>((_, args) => Task.FromResult(new[] { Home, Away }.FirstOrDefault(t => t.Id == (Guid)args![0]!))),
                Stub<IClubStore>((_, args) => Task.FromResult(Clubs.FirstOrDefault(c => c.Id == (Guid)args![0]!))),
                Stub<ISquadStore>((_, _) => Task.FromResult<Squad?>(null)),
                Store,
                Stub<IUnitOfWork>((_, _) => Task.FromResult(1)),
                new FixedClock(Now)).Handle(new RateRefereeCommand(Match.Id, Store.Referees[0].Id, score, comment), default);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}

// Almacén de árbitros en memoria para los tests.
internal sealed class InMemoryRefereeStore : IRefereeStore
{
    public List<Referee> Referees { get; } = [];
    public List<MatchOfficial> Officials { get; } = [];
    public List<MatchOfficialLookup> Lookups { get; } = [];
    public List<RefereeRating> Ratings { get; } = [];

    public Task<Referee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Referees.FirstOrDefault(r => r.Id == id));
    public Task<IReadOnlyList<Referee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Referee>>(Referees.Where(r => ids.Contains(r.Id)).ToList());
    public Task<Referee?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default) =>
        Task.FromResult(Referees.FirstOrDefault(r => r.NormalizedName == normalizedName));
    public Task<IReadOnlyList<Referee>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Referee>>(Referees.ToList());
    public void Add(Referee referee) => Referees.Add(referee);

    public Task<IReadOnlyList<MatchOfficial>> GetOfficialsAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MatchOfficial>>(Officials.Where(o => o.MatchId == matchId).ToList());
    public Task<IReadOnlyList<MatchOfficial>> GetOfficialsByMatchesAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MatchOfficial>>(Officials.Where(o => matchIds.Contains(o.MatchId)).ToList());
    public Task<IReadOnlyList<MatchOfficial>> GetOfficialsByRefereeAsync(Guid refereeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MatchOfficial>>(Officials.Where(o => o.RefereeId == refereeId).ToList());
    public Task<IReadOnlyList<MatchOfficial>> GetAllOfficialsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MatchOfficial>>(Officials.ToList());
    public void AddOfficial(MatchOfficial official) => Officials.Add(official);

    public Task<IReadOnlyList<Guid>> GetMatchesWithOfficialsAsync(IEnumerable<Guid> teamIds, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Officials.Select(o => o.MatchId).Distinct().ToList());
    public Task<IReadOnlyList<OfficialsSource>> GetSheetsWithoutOfficialsAsync(string sheetProvider, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OfficialsSource>>([]);
    public Task<IReadOnlyList<OfficialsSource>> GetMatchesToLookUpAsync(string provider, DateTimeOffset now, TimeSpan retryAfter, int maxAttempts, int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OfficialsSource>>([]);
    public Task<MatchOfficialLookup?> GetLookupAsync(Guid matchId, CancellationToken cancellationToken = default) => Task.FromResult(Lookups.FirstOrDefault(l => l.MatchId == matchId));
    public void AddLookup(MatchOfficialLookup lookup) => Lookups.Add(lookup);

    public Task<IReadOnlyList<RefereeRating>> GetRatingsByMatchesAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RefereeRating>>(Ratings.Where(r => matchIds.Contains(r.MatchId)).ToList());
    public Task<IReadOnlyList<RefereeRating>> GetRatingsByRefereeAsync(Guid refereeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RefereeRating>>(Ratings.Where(r => r.RefereeId == refereeId).ToList());
    public Task<IReadOnlyList<RefereeRating>> GetAllRatingsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RefereeRating>>(Ratings.ToList());
    public void AddRating(RefereeRating rating) => Ratings.Add(rating);
}
