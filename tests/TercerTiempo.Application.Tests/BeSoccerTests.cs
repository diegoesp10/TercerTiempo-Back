using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.OfficialSheets.BeSoccer;

namespace TercerTiempo.Application.Tests;

public class BeSoccerTests
{
    private static string Fixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static OfficialSheetData Sheet() =>
        BeSoccerPages.ParseSheet(Fixture("besoccer-alineaciones.html"), Fixture("besoccer-eventos.html"), 90)!;

    [Fact]
    public void Sheet_has_teams_score_referee_and_both_lineups()
    {
        var sheet = Sheet();

        Assert.Equal("Vallobin CD", sheet.HomeTeamName);
        Assert.Equal("Navarro", sheet.AwayTeamName);
        Assert.Equal((0, 5), (sheet.HomeGoals, sheet.AwayGoals));
        Assert.Equal("Nicolas Menendez Rodriguez", sheet.Referee);
        Assert.Equal(11, sheet.Lines.Count(l => l.Side == MatchSide.Home && l.Started));
        Assert.Equal(11, sheet.Lines.Count(l => l.Side == MatchSide.Away && l.Started));
        Assert.Equal(7, sheet.Lines.Count(l => l.Side == MatchSide.Home && !l.Started));

        var keeper = sheet.Lines.Single(l => l.FullName == "Juan");
        Assert.Equal((1, MatchSide.Home, 0, 90), (keeper.ShirtNumber, keeper.Side, keeper.MinuteIn, keeper.MinutesPlayed));
    }

    [Fact]
    public void Events_give_substitutions_goals_and_cards_with_their_minutes()
    {
        var sheet = Sheet();

        // Mismo partido que el acta de la federación: Guille sale en el 16 por Aldo, Robi ve la roja en el 48,
        // L. Serrano entra en el 62 y marca dos goles; Ameijide ve dos amarillas.
        var guille = sheet.Lines.Single(l => l.FullName == "Guille");
        Assert.Equal((0, 16, 16), (guille.MinuteIn, guille.MinuteOut, guille.MinutesPlayed));
        var aldo = sheet.Lines.Single(l => l.FullName == "Aldo");
        Assert.Equal((16, 74), (aldo.MinuteIn, aldo.MinutesPlayed));
        Assert.True(sheet.Lines.Single(l => l.FullName == "Robi").RedCard);
        var serrano = sheet.Lines.Single(l => l.FullName == "L. Serrano");
        Assert.Equal((62, 2), (serrano.MinuteIn, serrano.Goals));
        var ameijide = sheet.Lines.Single(l => l.FullName == "M. Ameijide");
        Assert.Equal((2, true), (ameijide.YellowCards, ameijide.RedCard));

        Assert.Equal(5, sheet.Events.Count(e => e.Type is OfficialEventType.Goal or OfficialEventType.PenaltyGoal && e.Side == MatchSide.Away));
        Assert.Contains(sheet.Events, e => e is { Type: OfficialEventType.Substitution, Minute: 16, PlayerName: "Aldo", RelatedPlayerName: "Guille" });
        Assert.Equal(sheet.Events.OrderBy(e => e.Minute).Select(e => e.Minute), sheet.Events.Select(e => e.Minute));
    }

    [Fact]
    public void Lineups_without_events_page_still_give_the_players()
    {
        var sheet = BeSoccerPages.ParseSheet(Fixture("besoccer-alineaciones.html"), null, 90)!;
        Assert.Equal(36, sheet.Lines.Count);
        Assert.Empty(sheet.Events);
    }

    [Fact]
    public void Team_page_lists_its_matches_with_codes_names_and_kickoff()
    {
        var fixtures = BeSoccerPages.ParseFixtures(Fixture("besoccer-equipo-partidos.html"));

        var match = fixtures.Single(f => f.Code == "vallobin-cd/navarro/202794664");
        Assert.Equal(("vallobin-cd", "Vallobin CD", "navarro", "Navarro"), (match.HomeCode, match.HomeName, match.AwayCode, match.AwayName));
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 11, 30, 0, TimeSpan.FromHours(2)), match.StartsAt);
    }

    [Fact]
    public void Competition_page_lists_groups_and_teams()
    {
        var html = Fixture("besoccer-equipos.html");

        Assert.Equal(["grupo1"], BeSoccerPages.ParseGroups(html));
        var teams = BeSoccerPages.ParseTeams(html);
        Assert.Equal(20, teams.Count);
        Assert.Contains(new DirectoryTeam("vallobin-cd", "Vallobin CD"), teams);
    }

    [Theory]
    [InlineData("Guille", 11, "CAVIA CASTRO, GUILLERMO", 11, true)]
    [InlineData("M. Huelga", 14, "HUELGA ONIS, MARCO", 14, true)]
    [InlineData("Miguel Peña", null, "FERNANDEZ PEÑA FERNANDEZ, MIGUEL", null, true)]
    [InlineData("Juan", null, "CEPEDAL FERNANDEZ, JUAN", null, false)]
    [InlineData("M. Huelga", null, "HUELGA ONIS, MARCO", null, true)]
    [InlineData("M.", null, "MARTINEZ RODRIGUEZ, PABLO", null, false)]
    public void Short_names_from_other_webs_match_the_full_name(string shortName, int? shortShirt, string fullName, int? fullShirt, bool matches)
    {
        var score = SheetMatcher.Score(
            SheetMatcher.Tokens(TercerTiempo.Domain.Common.NameNormalizer.Normalize(shortName)),
            SheetMatcher.Tokens(TercerTiempo.Domain.Common.NameNormalizer.Normalize(fullName.Replace(',', ' '))),
            shortShirt, fullShirt);
        Assert.Equal(matches, score >= 0.6);
    }

    [Fact]
    public void A_single_word_matches_only_when_one_player_has_it()
    {
        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "besoccer", "a/b/1", null, DateTimeOffset.UtcNow);
        OfficialLineData Line(string name) => new(MatchSide.Home, null, name, true, 0, null, 90, 0, 0, 0, 0, false);
        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", 0, 0, [Line("Mazo"), Line("Juan")], []), DateTimeOffset.UtcNow);

        SheetMatcher.Candidate Player(string name) => new(Guid.NewGuid(), TercerTiempo.Domain.Common.NameNormalizer.Normalize(name), null);
        var mazo = Player("Pablo García Mazo");
        var matched = SheetMatcher.Match([mazo, Player("Juan Cepedal Fernández"), Player("Juan Cirisuelo García")], sheet.Lines);

        Assert.Equal("Mazo", Assert.Single(matched).Value.FullName);
        Assert.True(matched.ContainsKey(mazo.Id));
    }

    [Fact]
    public async Task Directory_source_finds_the_match_by_team_codes_and_date()
    {
        var competition = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        var home = Team.Create("CD Vallobín", "VAL", null);
        var away = Team.Create("Navarro CF", "NAV", null);
        var match = Match.Schedule(season.Id, 6, home.Id, away.Id, new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero), null, null);

        var directory = new FakeDirectory(BeSoccerPages.ParseTeams(Fixture("besoccer-equipos.html")), BeSoccerPages.ParseFixtures(Fixture("besoccer-equipo-partidos.html")),
            BeSoccerPages.ParseSheet(Fixture("besoccer-alineaciones.html"), Fixture("besoccer-eventos.html"), 90));
        var teams = DispatchProxy.Create<ITeamStore, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)teams).Call = (method, args) => method switch
        {
            "GetBySeasonAsync" => Task.FromResult<IReadOnlyList<Team>>([home, away]),
            "GetByIdsAsync" => Task.FromResult<IReadOnlyList<Team>>([home, away]),
            _ => throw new Exception($"Unexpected team store call: {method}"),
        };
        var source = new DirectorySheetSource(directory, teams, new DirectoryTeamIndex(), new OfficialSheetSyncMemory(),
            TimeProvider.System, NullLogger<DirectorySheetSource>.Instance);

        var fetch = await source.FetchAsync(match, competition, season, null, default);

        Assert.NotNull(fetch);
        Assert.Equal(("besoccer", "vallobin-cd/navarro/202794664"), (fetch.Provider, fetch.ExternalId));
        Assert.True(fetch.Complete);
    }

    [Fact]
    public void Configured_sources_replace_the_default_order()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OfficialSheets:Sources:0"] = "asturfutbol", ["OfficialSheets:Sources:1"] = "besoccer" })
            .Build();
        var options = new PublicSheetOptions();
        configuration.GetSection(PublicSheetOptions.SectionName).Bind(options);

        Assert.Equal(["asturfutbol", "besoccer"], options.Sources);
        Assert.Equal(["besoccer", "asturfutbol"], PublicSheetOptions.DefaultSources);
    }

    private sealed class FakeDirectory(IReadOnlyList<DirectoryTeam> teams, IReadOnlyList<DirectoryFixture> fixtures, OfficialSheetData? sheet) : IMatchSheetDirectory
    {
        public string ProviderName => "besoccer";
        public Task<IReadOnlyList<string>> GetGroupsAsync(string category, string seasonLabel, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(["grupo1"]);
        public Task<IReadOnlyList<DirectoryTeam>> GetTeamsAsync(string category, string seasonLabel, string group, CancellationToken cancellationToken) => Task.FromResult(teams);
        public Task<IReadOnlyList<DirectoryFixture>> GetFixturesAsync(string teamCode, CancellationToken cancellationToken) => Task.FromResult(teamCode == "vallobin-cd" ? fixtures : []);
        public Task<OfficialSheetData?> GetSheetAsync(string matchCode, CancellationToken cancellationToken) => Task.FromResult(sheet);
        public string SheetUrl(string matchCode) => "https://es.besoccer.com/partido/" + matchCode;
    }
}
