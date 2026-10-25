using TercerTiempo.Application.Search;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Tests;

public class SearchIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid League = Guid.NewGuid();
    private static readonly Guid Vallobin = Guid.NewGuid();
    private static readonly Guid Roces = Guid.NewGuid();
    private static readonly Guid Nalon = Guid.NewGuid();
    private static readonly Guid Caudal = Guid.NewGuid();
    private static readonly Guid CaudalB = Guid.NewGuid();
    private static readonly Guid PepeEntry = Guid.NewGuid();
    private static readonly Guid NalonRoces = Guid.NewGuid();
    private static readonly Guid RocesSquad = Guid.NewGuid();
    private static readonly Guid PepeInSquad = Guid.NewGuid();
    private static readonly Guid ManualPlayer = Guid.NewGuid();

    private static SearchIndex Index() => SearchIndex.Build(new SearchCorpus(
        [new CorpusCompetition(League, "1ª AsturFútbol", "Primera", null, "Asturias", 5, 5, Guid.NewGuid(), "2026/2027", 20, 38, 60)],
        [
            new CorpusTeam(Vallobin, "CD Vallobín", "VAL", "/crest/v", League, 4, 10),
            new CorpusTeam(Roces, "TSK Roces", "ROC", "/crest/r", League, 2, 13),
            new CorpusTeam(Nalon, "Nalón CF", "NAL", null, League, 9, 7),
            new CorpusTeam(Caudal, "Caudal Deportivo", "CAU", null, null, null, null),
            new CorpusTeam(CaudalB, "Caudal Deportivo 'B'", "CAU", null, null, null, null),
        ],
        [new CorpusClub(Guid.NewGuid(), "TSK Roces", [Roces])],
        [
            new CorpusPlayer(PepeEntry, Roces, "José Fernández Álvarez", Position.Forward, 10, null),
            new CorpusPlayer(Guid.NewGuid(), Vallobin, "Pedro García", Position.Goalkeeper, 1, null),
        ],
        [new CorpusMatch(NalonRoces, League, 6, Nalon, Roces, Now.AddDays(-7), MatchStatus.Played, 0, 1)],
        [new CorpusSquad(RocesSquad, Roces, "2026/2027", [
            new CorpusSquadPlayer(PepeInSquad, "José Fernández Álvarez", Position.Forward, 10, PepeEntry, null),
            new CorpusSquadPlayer(ManualPlayer, "Marcos Juvenil Prueba", Position.Defender, 22, null, null),
        ])]), Now);

    private static readonly SearchViewer Stranger = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    private static readonly SearchViewer RocesStaff = new(new HashSet<Guid> { Roces }, new HashSet<Guid> { RocesSquad }, new HashSet<Guid>());

    [Theory]
    [InlineData("vallobin")]
    [InlineData("VALLOBÍN")]
    [InlineData("valobin")]
    [InlineData("vallo")]
    public void Teams_are_found_without_accents_by_prefix_and_with_typos(string query)
    {
        var first = Index().Search(query, Stranger, null).Hits[0];
        Assert.Equal((SearchTypes.Team, Vallobin, $"/equipos/{Vallobin}"), (first.Doc.Type, first.Doc.Id, first.Link));
    }

    [Fact]
    public void Every_word_has_to_match_and_generic_club_initials_do_not_count()
    {
        var hits = Index().Search("caudal b", Stranger, null).Hits;
        Assert.Equal(CaudalB, hits[0].Doc.Id);

        Assert.Equal(Vallobin, Index().Search("cd vallobin", Stranger, null).Hits[0].Doc.Id);
        // Ningún equipo tiene dos de esas tres palabras: ni siquiera hay coincidencias parciales.
        Assert.Empty(Index().Search("vallobin roces caudal", Stranger, SearchTypes.Team).Hits);
    }

    [Fact]
    public void A_match_needs_both_teams_in_the_query()
    {
        var hit = Assert.Single(Index().Search("nalon roces", Stranger, SearchTypes.Match).Hits);
        Assert.Equal(($"/partidos/{NalonRoces}", "0-1"), (hit.Link, hit.Doc.Meta));
        Assert.Empty(Index().Search("roces", Stranger, SearchTypes.Match).Hits);
    }

    [Fact]
    public void Players_are_found_by_any_name_and_shirt_number_and_lead_to_their_team()
    {
        var hit = Index().Search("alvarez 10", Stranger, null).Hits[0];
        Assert.Equal((SearchTypes.Player, $"/equipos/{Roces}/jugadores/{PepeEntry}"), (hit.Doc.Type, hit.Link));
        Assert.Contains("TSK Roces", hit.Doc.Subtitle);
    }

    [Fact]
    public void Internal_squads_and_their_players_are_only_for_who_can_see_them()
    {
        Assert.DoesNotContain(Index().Search("marcos juvenil", Stranger, null).Hits, h => h.Doc.Id == ManualPlayer);
        Assert.DoesNotContain(Index().Search("roces", Stranger, null).Hits, h => h.Doc.Type == SearchTypes.Squad);

        var mine = Index().Search("marcos juvenil", RocesStaff, null).Hits[0];
        Assert.Equal($"/plantillas/{RocesSquad}/jugadores/{ManualPlayer}", mine.Link);

        // La ficha publicada de un jugador de tu plantilla lleva a su perfil interno.
        var pepe = Index().Search("jose fernandez", RocesStaff, null).Hits[0];
        Assert.Equal($"/plantillas/{RocesSquad}/jugadores/{PepeInSquad}", pepe.Link);
        Assert.Contains("Tu plantilla", pepe.Badges);

        // Un club ve las plantillas de todos sus equipos.
        var club = new SearchViewer(new HashSet<Guid> { Roces }, new HashSet<Guid>(), new HashSet<Guid> { Roces });
        Assert.Contains(Index().Search("roces", club, null).Hits, h => h.Doc.Type == SearchTypes.Squad);
    }

    [Fact]
    public void Your_own_teams_come_first()
    {
        var hits = Index().Search("roces", RocesStaff, SearchTypes.Team).Hits;
        Assert.Equal(Roces, hits[0].Doc.Id);
        Assert.Contains("Tu equipo", hits[0].Badges);
    }

    [Theory]
    [InlineData("asturfutbol")]
    [InlineData("primera asturfutbol")]
    [InlineData("1a asturfutbol")]
    public void Leagues_are_found_by_their_name_with_the_category_in_letters_or_numbers(string query)
    {
        var hit = Index().Search(query, Stranger, null).Hits[0];
        Assert.Equal((SearchTypes.Competition, $"/liga?competition={League}"), (hit.Doc.Type, hit.Link));
    }

    [Fact]
    public void A_team_name_with_two_words_is_not_read_as_a_match()
    {
        // «tsk roces» nombra a un solo equipo aunque el rival se llame parecido; una letra suelta tampoco nombra a nadie.
        Assert.Empty(Index().Search("tsk roces", Stranger, SearchTypes.Match).Hits);
        Assert.Empty(Index().Search("nalon c", Stranger, SearchTypes.Match).Hits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public void Too_short_queries_return_nothing(string query) => Assert.Empty(Index().Search(query, Stranger, null).Hits);

    [Theory]
    [InlineData("vallobin", "valobin", 1)]
    [InlineData("fernandez", "frenandez", 1)]
    [InlineData("roces", "rocas", 1)]
    [InlineData("abc", "xyz", 3)]
    public void Edit_distance_counts_transpositions_as_one(string a, string b, int expected) =>
        Assert.Equal(expected, SearchText.Distance(a, b, 3));
}
