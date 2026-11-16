using System.Reflection;
using TercerTiempo.Application.Teams.Roster;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// La ficha publicada no tiene dorsal: los que lleva cada jugador salen de la línea del acta oficial de cada jornada.
public class RosterShirtNumbersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _rivalId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();
    private readonly List<Match> _matches = [];
    private readonly List<OfficialMatchSheet> _sheets = [];

    [Fact]
    public async Task Two_players_can_wear_the_same_number_in_different_matches()
    {
        var diego = Entry("Diego Espina", Position.Forward);
        var roberto = Entry("Roberto Toral Gómez", Position.Defender);
        var aldo = Entry("Aldo Fabián", Position.Defender);

        Sheet(1, home: true, Line(8, "ESPINA RODRIGUEZ, DIEGO"), Line(4, "TORAL GOMEZ, ROBERTO"));
        Sheet(2, home: false, Line(8, "ESPINA RODRIGUEZ, DIEGO"), Line(4, "TORAL GOMEZ, ROBERTO"));
        Sheet(3, home: true, Line(8, "ESPINA RODRIGUEZ, DIEGO"), Line(4, "TORAL GOMEZ, ROBERTO"));
        // Diego se queda en el banquillo con el 14 y el 8 lo lleva Roberto.
        Sheet(4, home: false, Line(14, "ESPINA RODRIGUEZ, DIEGO", played: false), Line(8, "TORAL GOMEZ, ROBERTO"));

        var appearances = await RosterSummary.AppearancesAsync(_teamId, _seasonId, [diego, roberto, aldo], Matches, Sheets, default);

        Assert.Equal([(8, 3, 75), (14, 1, 25)], Usage(appearances[diego.Id]));
        Assert.Equal([(4, 3, 75), (8, 1, 25)], Usage(appearances[roberto.Id]));
        Assert.Empty(RosterSummary.ShirtNumbers(appearances[aldo.Id]));
        // El partido del banquillo cuenta para el dorsal, no para los partidos jugados.
        Assert.Equal(3, RosterSummary.Stats(appearances[diego.Id])!.Appearances);
    }

    [Fact]
    public async Task Percentages_add_up_to_100_and_a_tie_goes_to_the_latest_number()
    {
        var mateo = Entry("Mateo García Martínez", Position.Midfielder);

        Sheet(1, home: true, Line(6, "GARCIA MARTINEZ, MATEO"));
        Sheet(2, home: true, Line(10, "GARCIA MARTINEZ, MATEO"));
        Sheet(3, home: true, Line(16, "GARCIA MARTINEZ, MATEO"));
        // Un acta sin dorsal no entra en el reparto.
        Sheet(4, home: true, Line(null, "GARCIA MARTINEZ, MATEO"));

        var appearances = await RosterSummary.AppearancesAsync(_teamId, _seasonId, [mateo], Matches, Sheets, default);

        Assert.Equal([(16, 1, 34), (10, 1, 33), (6, 1, 33)], Usage(appearances[mateo.Id]));
    }

    [Fact]
    public async Task The_number_a_player_usually_wears_settles_what_the_name_leaves_open()
    {
        var pelayo = Entry("Pelayo", Position.Defender);

        Sheet(1, home: true, Line(3, "GONZALEZ ALVAREZ, PELAYO"));
        Sheet(2, home: false, Line(3, "GONZALEZ ALVAREZ, PELAYO"));
        // Dos Pelayos en el acta: por el nombre no se sabe cuál es el de la ficha; por el dorsal que suele llevar, sí.
        Sheet(3, home: true, Line(8, "SECADES DIAZ, PELAYO"), Line(3, "GONZALEZ ALVAREZ, PELAYO"));

        var appearances = await RosterSummary.AppearancesAsync(_teamId, _seasonId, [pelayo], Matches, Sheets, default);

        Assert.Equal([1, 2, 3], appearances[pelayo.Id].Select(a => a.Match.Matchday));
        Assert.All(appearances[pelayo.Id], a => Assert.Equal("GONZALEZ ALVAREZ, PELAYO", a.Line.FullName));
        Assert.Equal([(3, 3, 100)], Usage(appearances[pelayo.Id]));
    }

    [Fact]
    public async Task A_player_published_twice_is_one_person_with_the_sheets_of_both_cards()
    {
        var shortCard = Entry("Lucas Paredes", Position.Forward);
        var fullCard = Entry("Lucas Paredes Rivera", Position.Forward);

        Sheet(1, home: true, Line(19, "PAREDES RIVERA, LUCAS"));
        Sheet(2, home: false, Line(12, "PAREDES RIVERA, LUCAS"));

        var lucas = Assert.Single(await RosterSummary.PeopleAsync(_teamId, _seasonId, [shortCard, fullCard], Matches, Sheets, default));

        Assert.Equal(fullCard.Id, lucas.Person.Main.Id);
        Assert.Equal([1, 2], lucas.Appearances.Select(a => a.Match.Matchday));
    }

    [Fact]
    public async Task Two_cards_with_a_line_each_in_the_same_sheet_are_two_players()
    {
        var shortCard = Entry("Lucas Paredes", Position.Forward);
        var fullCard = Entry("Lucas Paredes Rivera", Position.Forward);

        Sheet(1, home: true, Line(19, "PAREDES RIVERA, LUCAS"), Line(12, "PAREDES GOMEZ, LUCAS"));

        var people = await RosterSummary.PeopleAsync(_teamId, _seasonId, [shortCard, fullCard], Matches, Sheets, default);

        Assert.Equal(2, people.Count);
        Assert.All(people, p => Assert.Single(p.Appearances));
    }

    [Fact]
    public void A_player_published_twice_is_imported_into_the_squad_once()
    {
        var squad = TercerTiempo.Domain.Squads.Squad.Open(_teamId, _seasonId, Now);
        var roster = new[] { Entry("Lucas Paredes", Position.Forward), Entry("Lucas Paredes Rivera", Position.Forward), Entry("Marco Huelga Onís", Position.Midfielder) };

        TercerTiempo.Application.Squads.SquadOpening.ImportRoster(squad, roster, Now);
        TercerTiempo.Application.Squads.SquadOpening.ImportRoster(squad, roster, Now);

        Assert.Equal(["Lucas Paredes Rivera", "Marco Huelga Onís"], squad.ActivePlayers.Select(p => p.FullName).Order());
    }

    private static List<(int, int, int)> Usage(IReadOnlyList<RosterAppearance> appearances) =>
        RosterSummary.ShirtNumbers(appearances).Select(u => (u.Number, u.Matches, u.Percentage)).ToList();

    private RosterEntry Entry(string name, Position position) => RosterEntry.Create(_teamId, _seasonId, name, position, null, Now);

    private static OfficialLineData Line(int? number, string name, bool played = true) =>
        new(MatchSide.Home, number, name, played, played ? 0 : null, null, played ? 90 : 0, 0, 0, 0, 0, false);

    private void Sheet(int matchday, bool home, params OfficialLineData[] lines)
    {
        var match = Match.Schedule(_seasonId, matchday, home ? _teamId : _rivalId, home ? _rivalId : _teamId, Now.AddDays(7 * matchday), null, null);
        var side = home ? MatchSide.Home : MatchSide.Away;
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", $"acta-{matchday}", null, Now);
        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", 1, 0, lines.Select(l => l with { Side = side }).ToList(), []), Now);
        _matches.Add(match);
        _sheets.Add(sheet);
    }

    private IMatchStore Matches => Stub<IMatchStore>((method, _) => method == nameof(IMatchStore.GetTeamMatchesAsync)
        ? Task.FromResult<IReadOnlyList<Match>>(_matches)
        : throw new NotSupportedException(method));

    private IOfficialSheetStore Sheets => Stub<IOfficialSheetStore>((method, _) => method == nameof(IOfficialSheetStore.GetAvailableLinesByMatchIdsAsync)
        ? Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([.. _sheets.Where(s => s.IsAvailable)])
        : throw new NotSupportedException(method));

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
