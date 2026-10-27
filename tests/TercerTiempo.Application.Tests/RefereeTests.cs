using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Referees;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
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
        var club = new RefereeRater(Guid.NewGuid(), null, Guid.NewGuid(), "CD Vallobín");

        var rating = RefereeRating.Rate(official, Guid.NewGuid(), 8, "  Buen criterio  ", club, Now);
        Assert.Equal((8, "Buen criterio", "CD Vallobín", (StaffRole?)null), (rating.Score, rating.Comment, rating.RaterName, rating.RaterStaffRole));

        Assert.Equal(ErrorCode.RefereeScoreRange, Assert.Throws<DomainException>(() => RefereeRating.Rate(official, Guid.NewGuid(), 11, null, club, Now)).Code);
        Assert.Equal(ErrorCode.RefereeRaterRequired,
            Assert.Throws<DomainException>(() => RefereeRating.Rate(official, Guid.NewGuid(), 5, null, new RefereeRater(null, null, null, "Nadie"), Now)).Code);
    }

    [Fact]
    public async Task The_club_rates_each_official_once_and_the_rating_is_final()
    {
        var f = new Fixture();

        var saved = await f.Rate(f.ClubUser, 7, "Bien colocada");
        var again = await Assert.ThrowsAsync<ConflictException>(() => f.Rate(f.ClubUser, 9, null));
        Assert.Equal(ErrorCode.RefereeAlreadyRated, again.Code);

        var rating = Assert.Single(f.Store.Ratings);
        Assert.Equal((7, "Bien colocada", f.Home.Id, "CD Vallobín", f.Club.Id), (rating.Score, rating.Comment, rating.TeamId, rating.RaterName, rating.RatedByClubId));
        var official = Assert.Single(saved.Officials);
        Assert.Equal((7, 7.0, 1), (official.MyScore, official.Average, official.Ratings));
        var shown = Assert.Single(official.TeamRatings);
        Assert.Equal(("CD Vallobín", "CD Vallobín", true, "Bien colocada"), (shown.TeamName, shown.RatedBy.Name, shown.RatedBy.Club, shown.Comment));
        Assert.Equal((true, f.Home.Id), (saved.CanRate, saved.RaterTeamId));
    }

    [Fact]
    public async Task A_coach_of_the_other_team_rates_too_and_both_ratings_are_public()
    {
        var f = new Fixture();
        var coach = Coach.Register(Guid.NewGuid(), "Pepe Rivero", Now);
        f.Coaches.Add(coach);
        f.Squads.Add(Squad.Create(f.Away.Id, f.Match.SeasonId, coach.Id, Now.AddDays(-30), StaffRole.AssistantCoach));

        await f.Rate(f.ClubUser, 8, "Dejó jugar");
        await f.Rate(new User(CoachId: coach.Id), 4, "Se comió un penalti claro");

        var away = Assert.Single(f.Store.Ratings, r => r.TeamId == f.Away.Id);
        Assert.Equal(("Pepe Rivero", (StaffRole?)StaffRole.AssistantCoach, coach.Id), (away.RaterName, away.RaterStaffRole, away.RatedByCoachId));

        var anonymous = await f.View(new Anonymous());
        Assert.Equal((false, false, (Guid?)null), (anonymous.CanRate, anonymous.NeedsLicense, anonymous.RaterTeamId));
        var official = Assert.Single(anonymous.Officials);
        Assert.Null(official.MyScore);
        Assert.Equal(
            [(f.Home.Id, 8, "CD Vallobín", (StaffRole?)null), (f.Away.Id, 4, "Pepe Rivero", StaffRole.AssistantCoach)],
            official.TeamRatings.Select(r => (r.TeamId, r.Score, r.RatedBy.Name, r.RatedBy.StaffRole)));
        Assert.Equal(6.0, official.Average);
    }

    [Fact]
    public void The_rating_has_to_be_confirmed()
    {
        var validator = new RateRefereeValidator();
        var unconfirmed = validator.Validate(new RateRefereeCommand(Guid.NewGuid(), Guid.NewGuid(), 7, null));
        Assert.Contains(unconfirmed.Errors, e => e.ErrorCode == ErrorCode.RefereeRatingNotConfirmed);
        Assert.True(validator.Validate(new RateRefereeCommand(Guid.NewGuid(), Guid.NewGuid(), 7, null, Confirmed: true)).IsValid);
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
    public async Task Players_and_visitors_see_referees_but_cannot_rate_them()
    {
        var f = new Fixture();
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Rate(new User(PlayerId: Guid.NewGuid()), 5, null));
        Assert.Equal(ErrorCode.RefereeStaffOnly, error.Code);
        await Assert.ThrowsAsync<UnauthenticatedException>(() => f.Rate(new Anonymous(), 5, null));

        var seen = await f.View(new User(PlayerId: Guid.NewGuid()));
        Assert.Equal((false, "Sara Ovies García"), (seen.CanRate, Assert.Single(seen.Officials).Name));
    }

    [Fact]
    public async Task Referees_are_listed_by_category_with_groups_together_and_split_into_main_and_assistant()
    {
        var third = Competition.Create("3ª AsturFútbol – Grupo 1", "3ª AsturFútbol", CompetitionLevel.Regional, 3, "1", "Asturias", null);
        var thirdTwo = Competition.Create("3ª AsturFútbol – Grupo 2", "3ª AsturFútbol", CompetitionLevel.Regional, 3, "2", "Asturias", null);
        var second = Competition.Create("2ª AsturFútbol – Grupo 1", "2ª AsturFútbol", CompetitionLevel.Regional, 2, "1", "Asturias", null);
        var seasons = new[] { third, thirdTwo, second }.Select(c => c.AddSeason("2026-27", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null)).ToArray();
        var games = seasons.Select(s =>
        {
            var match = Match.Schedule(s.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-3), null, null);
            match.RecordResult(2, 1, Now);
            return match;
        }).ToArray();

        var store = new InMemoryRefereeStore();
        var sara = Referee.Create("Ovies García, Sara", null, Now);
        var pepe = Referee.Create("Pepe Pérez", null, Now);
        store.Referees.AddRange([sara, pepe]);
        store.Officials.Add(MatchOfficial.Assign(games[0].Id, sara.Id, OfficialRole.Referee, "asturfutbol", Now));
        store.Officials.Add(MatchOfficial.Assign(games[1].Id, sara.Id, OfficialRole.Referee, "asturfutbol", Now));
        store.Officials.Add(MatchOfficial.Assign(games[2].Id, sara.Id, OfficialRole.AssistantReferee1, "asturfutbol", Now));
        store.Officials.Add(MatchOfficial.Assign(games[0].Id, pepe.Id, OfficialRole.AssistantReferee2, "asturfutbol", Now));
        var rater = new RefereeRater(Guid.NewGuid(), null, null, "Club");
        store.Ratings.Add(RefereeRating.Rate(store.Officials[0], games[0].HomeTeamId, 8, null, rater, Now));
        store.Ratings.Add(RefereeRating.Rate(store.Officials[2], games[2].HomeTeamId, 4, null, rater, Now));

        var matches = Stub<IMatchStore>((_, args) => Task.FromResult<IReadOnlyList<Match>>(games.Where(m => ((IEnumerable<Guid>)args![0]!).Contains(m.Id)).ToList()));
        var competitions = Stub<ICompetitionStore>((method, _) => method == nameof(ICompetitionStore.GetAllAsync)
            ? Task.FromResult<IReadOnlyList<Competition>>([third, thirdTwo, second])
            : Task.FromResult<Competition?>(third));
        Assert.Equal("3a-asturfutbol", RefereeCategories.Key("3ª AsturFútbol"));
        var list = new ListRefereesHandler(store, matches, competitions);

        var thirdMain = await list.Handle(new ListRefereesQuery("3a-asturfutbol", OfficialDuty.Main), default);
        var main = Assert.Single(thirdMain.Referees);
        Assert.Equal((sara.Id, "3ª AsturFútbol", 2, 1, 8.0), (main.Id, main.Category, main.Matches, main.Ratings, main.Average));
        Assert.Equal((2, 1, 1, 2), (thirdMain.Category.CompetitionIds.Count, thirdMain.Category.Referees, thirdMain.Category.Assistants, thirdMain.Category.Matches));

        var thirdAll = await list.Handle(new ListRefereesQuery("3ª AsturFútbol"), default);
        Assert.Equal([(sara.Id, OfficialDuty.Main), (pepe.Id, OfficialDuty.Assistant)], thirdAll.Referees.Select(r => (r.Id, r.Duty)));
        var worstFirst = await list.Handle(new ListRefereesQuery("3a-asturfutbol", Sort: RefereeSort.Average, Order: SortOrder.Asc), default);
        Assert.Equal([sara.Id, pepe.Id], worstFirst.Referees.Select(r => r.Id));
        var byName = await list.Handle(new ListRefereesQuery("3a-asturfutbol", Sort: RefereeSort.Name), default);
        Assert.Equal((SortOrder.Asc, pepe.Id), (byName.Order, byName.Referees[0].Id));

        var asAssistant = Assert.Single((await list.Handle(new ListRefereesQuery("2a-asturfutbol", OfficialDuty.Assistant), default)).Referees);
        Assert.Equal((sara.Id, 1, 4.0), (asAssistant.Id, asAssistant.Ratings, asAssistant.Average));
        Assert.Empty((await list.Handle(new ListRefereesQuery("2a-asturfutbol", Search: "pepe"), default)).Referees);
        Assert.Single((await list.Handle(new ListRefereesQuery("3a-asturfutbol", Search: "PEREZ"), default)).Referees);

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => list.Handle(new ListRefereesQuery("juveniles"), default));
        Assert.Equal(ErrorCode.RefereeCategoryNotFound, unknown.Code);

        var categories = await new ListRefereeCategoriesHandler(store, matches, competitions).Handle(new ListRefereeCategoriesQuery(), default);
        Assert.Equal(["2a-asturfutbol", "3a-asturfutbol"], categories.Select(c => c.Key));
        var thirdCategory = categories[1];
        Assert.Equal((1, 1, 2, 1), (thirdCategory.Referees, thirdCategory.Assistants, thirdCategory.Matches, thirdCategory.Ratings));
        Assert.Equal([third.Id, thirdTwo.Id], thirdCategory.CompetitionIds);
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

    private sealed record Anonymous : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public AccountType? AccountType => null;
        public Guid? PlayerId => null;
        public Guid? CoachId => null;
        public Guid? ClubId => null;
    }

    private sealed class Fixture
    {
        public Team Home { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Away { get; } = Team.Create("C. Hispano", "HIS", null);
        public Match Match { get; }
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public List<Club> Clubs { get; } = [];
        public List<Coach> Coaches { get; } = [];
        public List<Squad> Squads { get; } = [];
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

        private ILicenseGate Licenses => Stub<ILicenseGate>((_, _) => Task.FromResult(true));
        private IMatchStore Matches => Stub<IMatchStore>((_, args) => Task.FromResult<Match?>((Guid)args![0]! == Match.Id ? Match : null));
        private ITeamStore Teams => Stub<ITeamStore>((method, args) => method == nameof(ITeamStore.GetByIdsAsync)
            ? Task.FromResult<IReadOnlyList<Team>>(new[] { Home, Away }.Where(t => ((IEnumerable<Guid>)args![0]!).Contains(t.Id)).ToList())
            : Task.FromResult(new[] { Home, Away }.FirstOrDefault(t => t.Id == (Guid)args![0]!)));
        private IClubStore ClubStore => Stub<IClubStore>((_, args) => Task.FromResult(Clubs.FirstOrDefault(c => c.Id == (Guid)args![0]!)));
        private ISquadStore SquadStore => Stub<ISquadStore>((_, args) =>
            Task.FromResult(Squads.FirstOrDefault(s => s.TeamId == (Guid)args![0]! && s.SeasonId == (Guid)args[1]!)));
        private ICoachStore CoachStore => Stub<ICoachStore>((_, args) => Task.FromResult(Coaches.FirstOrDefault(c => c.Id == (Guid)args![0]!)));
        private ICompetitionStore Competitions => Stub<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(null));

        public Task<MatchOfficialsDto> Rate(ICurrentUser user, int score, string? comment) =>
            new RateRefereeHandler(
                user, Licenses, Matches, Teams, ClubStore, SquadStore, CoachStore, Store, Competitions,
                Stub<IUnitOfWork>((_, _) => Task.FromResult(1)),
                new FixedClock(Now)).Handle(new RateRefereeCommand(Match.Id, Store.Referees[0].Id, score, comment, Confirmed: true), default);

        public Task<MatchOfficialsDto> View(ICurrentUser user) =>
            new GetMatchOfficialsHandler(user, Licenses, Matches, Teams, ClubStore, SquadStore, Store, Competitions, new FixedClock(Now))
                .Handle(new GetMatchOfficialsQuery(Match.Id), default);
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
