using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

public class SheetComparisonTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ equipos

    [Theory]
    [InlineData("CD Vallobín", "C.D. Vallobin \"A\"", 0.85)]
    [InlineData("Real Sporting 'C'", "Real Sporting de Gijón SAD \"C\"", 0.8)]
    [InlineData("Real Sporting 'B'", "Real Sporting de Gijón SAD \"C\"", 0.2)]
    [InlineData("Navarro CF", "Navarro C.F. Alusin Solar \"A\"", 0.5)]
    [InlineData("UD San Claudio", "C.D. Tineo \"A\"", 0.0)]
    public void Team_names_from_different_sources_score_as_expected(string ours, string theirs, double atLeast)
    {
        var score = TeamNameSimilarity.Score(ours, theirs);

        if (atLeast == 0) Assert.Equal(0, score);
        else Assert.True(score >= atLeast, $"{ours} / {theirs}: {score}");
    }

    [Fact]
    public void Suffix_letter_tells_reserve_teams_apart()
    {
        Assert.True(TeamNameSimilarity.Score("Real Sporting 'C'", "Real Sporting de Gijón SAD \"C\"")
                    > TeamNameSimilarity.Score("Real Sporting 'B'", "Real Sporting de Gijón SAD \"C\""));
    }

    [Fact]
    public void Matchday_fixtures_are_paired_with_our_matches_by_team_names()
    {
        var seasonId = Guid.NewGuid();
        var vallobin = Team.Create("CD Vallobín", "VAL", null);
        var navarro = Team.Create("Navarro CF", "NAV", null);
        var lenense = Team.Create("SD Lenense", "LEN", null);
        var sportingC = Team.Create("Real Sporting 'C'", "SPO", null);
        var teams = new[] { vallobin, navarro, lenense, sportingC }.ToDictionary(t => t.Id);

        var ours = new List<Match>
        {
            Match.Schedule(seasonId, 6, vallobin.Id, navarro.Id, Now, null, null),
            Match.Schedule(seasonId, 6, lenense.Id, sportingC.Id, Now, null, null),
        };
        var theirs = new List<OfficialFixture>
        {
            new("S.D. Lenense Proinastur \"A\"", "4210", "Real Sporting de Gijón SAD \"C\"", "9602883", null, null, "25744773"),
            new("C.D. Vallobin \"A\"", "3597", "Navarro C.F. Alusin Solar \"A\"", "1133", null, null, "25744780"),
            new("Valdesoto C.F. \"A\"", "4284", "Candas C.F.", "1095", null, null, "25744771"),
        };

        var pairs = OfficialSheetLocator.Pair(ours, theirs, teams).ToDictionary(p => p.Ours.Id, p => p.Theirs);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("25744780", pairs[ours[0].Id].SheetCode);
        Assert.Equal("25744773", pairs[ours[1].Id].SheetCode);
    }

    // ------------------------------------------------------------------ jugadores

    [Fact]
    public void Official_lines_are_matched_to_squad_players_by_name_tokens_and_shirt()
    {
        var squad = Squad.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var diego = squad.AddPlayer("Diego Espina Rodríguez", Position.Midfielder, null, null, Now);
        var mateo = squad.AddPlayer("Mateo García Mazo", Position.Forward, 10, null, Now);
        var other = squad.AddPlayer("Mateo José Álvarez Álvarez", Position.Defender, 15, null, Now);
        var miguel = squad.AddPlayer("Miguel Fernández-Peña Fernández", Position.Defender, 17, null, Now);

        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "1", null, Now);
        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", 0, 0,
            [
                Line("ESPINA RODRÍGUEZ, DIEGO", null),
                Line("Mateo", 10),
                Line("ALVAREZ ALVAREZ, MATEO JOSE", 15),
                Line("FERNANDEZ PEÑA FERNANDEZ, MIGUEL", 17),
                Line("CEPEDAL FERNANDEZ, JUAN", 1),
            ], []), Now);

        var matched = SheetMatcher.Match(squad.ActivePlayers, sheet.Lines);

        Assert.Equal("ESPINA RODRÍGUEZ, DIEGO", matched[diego.Id].FullName);
        Assert.Equal("Mateo", matched[mateo.Id].FullName);
        Assert.Equal("ALVAREZ ALVAREZ, MATEO JOSE", matched[other.Id].FullName);
        Assert.Equal("FERNANDEZ PEÑA FERNANDEZ, MIGUEL", matched[miguel.Id].FullName);
        Assert.Equal(4, matched.Count);
    }

    [Fact]
    public void Official_lines_are_matched_by_surnames_despite_nicknames_and_typos()
    {
        var squad = Squad.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var nacho = squad.AddPlayer("Nacho Cabal", Position.Forward, null, null, Now);
        var ivan = squad.AddPlayer("Ivanchu Roldán", Position.Forward, null, null, Now);
        var berto = squad.AddPlayer("Berto Arias", Position.Forward, null, null, Now);
        var david = squad.AddPlayer("David Frade Liñera", Position.Defender, null, null, Now);
        var pablo = squad.AddPlayer("Pablo Cabal", Position.Defender, null, null, Now);
        var mateo = squad.AddPlayer("Mateo García Martínez", Position.Forward, 10, null, Now);
        var mateoJose = squad.AddPlayer("Mateo José Álvarez Álvarez", Position.Defender, 15, null, Now);

        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "1", null, Now);
        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", 0, 0,
            [
                Line("CABAL GONZALEZ, IGNACIO", 9),
                Line("ROLDAN BRIÑAS, IVAN", 7),
                Line("ARIAS SANZ, ALBERTO JESUS", 11),
                Line("FRADE LIÑEIRA, DAVID", 2),
                Line("Mateo", 18),
                Line("ALVAREZ ALVAREZ, MATEO JOSE", 15),
                Line("GARCIA LOPEZ, PEDRO", 4),
            ], []), Now);

        var matched = SheetMatcher.Match(squad.ActivePlayers, sheet.Lines);

        Assert.Equal("CABAL GONZALEZ, IGNACIO", matched[nacho.Id].FullName);
        Assert.Equal("ROLDAN BRIÑAS, IVAN", matched[ivan.Id].FullName);
        Assert.Equal("ARIAS SANZ, ALBERTO JESUS", matched[berto.Id].FullName);
        Assert.Equal("FRADE LIÑEIRA, DAVID", matched[david.Id].FullName);
        Assert.Equal("ALVAREZ ALVAREZ, MATEO JOSE", matched[mateoJose.Id].FullName);
        // El otro Mateo ya tiene su línea completa: el "Mateo" suelto es este, aunque el dorsal no coincida.
        Assert.Equal("Mateo", matched[mateo.Id].FullName);
        // Mismo apellido con otro nombre de pila, o un apellido común sin nada más: no es el mismo jugador.
        Assert.False(matched.ContainsKey(pablo.Id));
        Assert.Equal(6, matched.Count);
    }

    [Theory]
    [InlineData("Pablo Suárez Suárez", "SUAREZ IGLESIAS, RUBEN")]
    [InlineData("Germán García González", "GARCIA LOPEZ, PEDRO")]
    [InlineData("Manu Fernández", "FERNANDEZ CASTIÑEIRA, IZAN")]
    [InlineData("Juan Carlos Pérez", "PEREZ GOMEZ, JUAN MANUEL")]
    [InlineData("Pablo Fernández Flórez", "FLORES RASTRERO, PABLO")]
    public void A_shared_surname_with_a_different_name_is_not_a_match(string player, string line)
    {
        var tokens = SheetMatcher.Tokens(NameNormalizer.Normalize(player));
        Assert.True(SheetMatcher.SurnameScore(tokens, line, null, null) < 0.6);
    }

    // Sin dorsal que lo confirme: el apodo puede estar en el acta y el segundo apellido, mal en la plantilla publicada.
    [Theory]
    [InlineData("Ignacio Puente Gutiérrez", "PUENTE GUTIERREZ, NACHO")]
    [InlineData("Roberto Bastida González", "BASTIDA CORRALES, ROBERTO")]
    [InlineData("Alejandro Iglesias Menéndez", "IGLESIAS MENDEZ, ALEJANDRO")]
    [InlineData("Pablo Flores Rastrillo", "FLORES RASTRERO, PABLO")]
    public void The_given_name_and_the_first_surname_are_enough_when_only_the_other_surname_differs(string player, string line)
    {
        var tokens = SheetMatcher.Tokens(NameNormalizer.Normalize(player));
        Assert.True(SheetMatcher.SurnameScore(tokens, line, null, null) >= 0.6);
    }

    [Fact]
    public void A_roster_with_one_word_per_player_is_matched_when_only_one_free_line_carries_that_word()
    {
        SheetMatcher.Candidate Entry(string name) => new(Guid.NewGuid(), NameNormalizer.Normalize(name), null);
        var guzman = Entry("Guzmán");
        var surname = Entry("Ogbogbodu");
        var sameByGivenName = Entry("Desmond");
        var dani = Entry("Dani");
        var daniel = Entry("Daniel Frechilla López");
        var pelayo = Entry("Pelayo");
        var martin = Entry("Martín");

        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "1", null, Now);
        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", 0, 0,
            [
                Line("DIAZ HUERTA, GUZMAN", 1),
                Line("OGBOGBODU, DESMOND TEGA", 19),
                Line("FRECHILLA LOPEZ, DANIEL", 23),
                Line("MARTINEZ RAMIREZ, DANIEL ANTONIO", 16),
                Line("GONZALEZ ALVAREZ, PELAYO", 3),
                Line("SECADES DIAZ, PELAYO", 8),
            ], []), Now);

        var matched = SheetMatcher.Match([guzman, sameByGivenName, surname, dani, daniel, pelayo, martin], sheet.Lines);

        Assert.Equal("DIAZ HUERTA, GUZMAN", matched[guzman.Id].FullName);
        // El mismo jugador publicado dos veces, por el nombre y por el apellido: la línea es de la ficha del apellido.
        Assert.Equal("OGBOGBODU, DESMOND TEGA", matched[surname.Id].FullName);
        Assert.False(matched.ContainsKey(sameByGivenName.Id));
        Assert.Equal("FRECHILLA LOPEZ, DANIEL", matched[daniel.Id].FullName);
        // El otro Daniel ya tiene su línea: «Dani» es el que queda.
        Assert.Equal("MARTINEZ RAMIREZ, DANIEL ANTONIO", matched[dani.Id].FullName);
        // Dos Pelayos en el acta y una sola ficha: por el nombre no se sabe cuál es. Y «Martín» no es «Martínez».
        Assert.False(matched.ContainsKey(pelayo.Id));
        Assert.False(matched.ContainsKey(martin.Id));
    }

    [Fact]
    public void Differences_cover_presence_minutes_goals_and_cards()
    {
        var (squad, coachId) = (Squad.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now), Guid.Empty);
        coachId = squad.Staff.First().CoachId;
        var player = squad.AddPlayer("Pelayo", Position.Forward, 9, null, Now);
        var match = Match.Schedule(squad.SeasonId, 1, squad.TeamId, Guid.NewGuid(), Now.AddDays(-1), null, null);
        match.RecordResult(2, 1, Now);

        var coach = PlayerMatchPerformance.Record(squad, player, match, SquadEditor.Coach(coachId),
            new PerformanceStats(true, 90, 1, 0, 2, 1, 0, 0, 0, 0, 1, false, null), Now);

        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", "1", null, Now);
        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", 2, 1,
            [new OfficialLineData(MatchSide.Home, 9, "PELAYO", false, 30, null, 60, 2, 0, 0, 0, false)], []), Now);
        var official = sheet.Lines.Single();

        var differences = SheetMatcher.Differences(coach, official);

        Assert.Equal(["started", "minutes", "goals", "yellowCards"], differences.Select(d => d.Field));
        Assert.Equal(("90", "60"), (differences[1].Coach, differences[1].Official));

        // Jugó según el técnico pero no aparece en el acta oficial: una sola discrepancia.
        var missing = SheetMatcher.Differences(coach, null);
        Assert.Equal("played", Assert.Single(missing).Field);

        // Convocado sin minutos en las dos: sin discrepancias.
        var benchLine = OfficialMatchSheet.Create(match.Id, "asturfutbol", "2", null, Now);
        benchLine.Apply(new OfficialSheetData(null, "Local", "Visitante", 2, 1,
            [new OfficialLineData(MatchSide.Home, 9, "PELAYO", false, null, null, 0, 0, 0, 0, 0, false)], []), Now);
        Assert.Empty(SheetMatcher.Differences(null, benchLine.Lines.Single()));
    }

    [Fact]
    public void Sheet_without_lines_stays_pending_and_with_lines_becomes_available()
    {
        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "25744780", "https://example", Now);
        Assert.Equal(OfficialSheetStatus.Pending, sheet.Status);

        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", null, null, [], []), Now.AddMinutes(10));
        Assert.Equal(OfficialSheetStatus.Pending, sheet.Status);
        Assert.Null(sheet.FetchedAt);

        sheet.Apply(new OfficialSheetData("Árbitro", "Local", "Visitante", 1, 0, [Line("UNO, JUGADOR", 1)], []), Now.AddHours(2));
        Assert.Equal(OfficialSheetStatus.Available, sheet.Status);
        Assert.Equal(Now.AddHours(2), sheet.FetchedAt);
        Assert.Equal("uno jugador", sheet.Lines.Single().NormalizedName);
    }

    private static OfficialLineData Line(string name, int? shirt) =>
        new(MatchSide.Home, shirt, name, true, 0, null, 90, 0, 0, 0, 0, false);
}
