using System.Reflection;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

public class AsturfutbolPagesTests
{
    private static string Fixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ catálogos

    [Fact]
    public void Reads_seasons_and_competitions_from_the_calendar_selects()
    {
        var html = Fixture("calendario.html");

        var seasons = AsturfutbolPages.ParseSelect(html, "temporada");
        var competitions = AsturfutbolPages.ParseSelect(html, "competicion");

        Assert.Equal(new CatalogEntry("22", "2026-2027"), seasons[0]);
        Assert.Contains(new CatalogEntry("22880345", "1ª Asturfútbol"), competitions);
        Assert.Contains(new CatalogEntry("22880347", "3ª Asturfútbol"), competitions);
        Assert.DoesNotContain(competitions, c => c.Code == "0");
    }

    [Fact]
    public void Reads_groups_from_the_exec_script()
    {
        var groups = AsturfutbolPages.ParseJsArray(Fixture("grupos.js"));

        Assert.Equal([new CatalogEntry("22932421", "1")], groups);
    }

    // ------------------------------------------------------------------ jornada

    [Fact]
    public void Reads_the_fixtures_of_a_matchday_with_their_sheet_codes()
    {
        var fixtures = AsturfutbolPages.ParseMatchday(Fixture("jornada-6.html"));

        Assert.Equal(10, fixtures.Count);

        var vallobin = Assert.Single(fixtures, f => f.HomeCode == "3597");
        Assert.Equal("C.D. Vallobin \"A\"", vallobin.HomeName);
        Assert.Equal("Navarro C.F. Alusin Solar \"A\"", vallobin.AwayName);
        Assert.Equal("25744780", vallobin.SheetCode);
        Assert.Equal(new DateOnly(2026, 10, 18), vallobin.Date);
        Assert.Equal(new TimeOnly(11, 30), vallobin.Time);

        var lenense = Assert.Single(fixtures, f => f.HomeCode == "4210");
        Assert.Equal("9602883", lenense.AwayCode);
        Assert.Equal(new DateOnly(2026, 10, 17), lenense.Date);
        Assert.All(fixtures, f => Assert.False(string.IsNullOrEmpty(f.SheetCode)));
    }

    // ------------------------------------------------------------------ acta

    [Fact]
    public void Reads_lineups_minutes_goals_and_cards_from_a_sheet()
    {
        var sheet = AsturfutbolPages.ParseSheet(Fixture("acta-25744780.html"), matchLength: 90)!;

        Assert.Equal("C.D. Vallobin A", sheet.HomeTeamName);
        Assert.Equal("Navarro C.F. Alusin Solar A", sheet.AwayTeamName);
        Assert.Equal("Menéndez Rodríguez, Nicolás", sheet.Referee);
        Assert.Equal(0, sheet.HomeGoals);
        Assert.Equal(5, sheet.AwayGoals);

        var home = sheet.Lines.Where(l => l.Side == MatchSide.Home).ToList();
        var away = sheet.Lines.Where(l => l.Side == MatchSide.Away).ToList();
        Assert.Equal(18, home.Count);
        Assert.Equal(18, away.Count);
        Assert.Equal(11, home.Count(l => l.Started));
        Assert.Equal(11, away.Count(l => l.Started));

        // Portero titular: 90 minutos.
        var keeper = Assert.Single(home, l => l.ShirtNumber == 1);
        Assert.Equal("CEPEDAL FERNANDEZ, JUAN", keeper.FullName);
        Assert.True(keeper.Started);
        Assert.Equal(0, keeper.MinuteIn);
        Assert.Null(keeper.MinuteOut);
        Assert.Equal(90, keeper.MinutesPlayed);

        // Sustituido en el 16': el que sale jugó 16; el que entra, 74.
        var cavia = Assert.Single(home, l => l.ShirtNumber == 11);
        Assert.Equal(16, cavia.MinuteOut);
        Assert.Equal(16, cavia.MinutesPlayed);
        var martinez = Assert.Single(home, l => l.ShirtNumber == 5);
        Assert.False(martinez.Started);
        Assert.Equal(16, martinez.MinuteIn);
        Assert.Equal(74, martinez.MinutesPlayed);

        // Suplente que no entra: sin minutos.
        var bench = Assert.Single(home, l => l.ShirtNumber == 13);
        Assert.False(bench.Started);
        Assert.Null(bench.MinuteIn);
        Assert.Equal(0, bench.MinutesPlayed);

        // Expulsado en el 48' con roja directa.
        var toral = Assert.Single(home, l => l.ShirtNumber == 8);
        Assert.True(toral.RedCard);
        Assert.Equal(48, toral.MinuteOut);
        Assert.Equal(48, toral.MinutesPlayed);

        // Amarillas.
        Assert.Equal(1, Assert.Single(home, l => l.ShirtNumber == 14).YellowCards);
        Assert.Equal(2, Assert.Single(away, l => l.ShirtNumber == 25).YellowCards);
        Assert.True(Assert.Single(away, l => l.ShirtNumber == 25).RedCard);

        // Goles atribuidos al equipo del goleador (dos de Serrano Pantiga, que entró en el 62').
        var serrano = Assert.Single(away, l => l.ShirtNumber == 7);
        Assert.Equal(2, serrano.Goals);
        Assert.Equal(62, serrano.MinuteIn);
        Assert.Equal(28, serrano.MinutesPlayed);
        Assert.Equal(1, Assert.Single(away, l => l.ShirtNumber == 9).Goals);
        Assert.Equal(0, home.Sum(l => l.Goals + l.PenaltyGoals));

        // Sucesos en orden cronológico, con el "90'+1".
        Assert.Equal(5, sheet.Events.Count(e => e.Type == OfficialEventType.Goal));
        Assert.Equal(10, sheet.Events.Count(e => e.Type == OfficialEventType.Substitution));
        Assert.Equal(5, sheet.Events.Count(e => e.Type == OfficialEventType.YellowCard));
        Assert.Equal(1, sheet.Events.Count(e => e.Type == OfficialEventType.RedCard));
        var last = sheet.Events.Last(e => e.Type == OfficialEventType.Goal);
        Assert.Equal(90, last.Minute);
        Assert.Equal(1, last.ExtraMinute);
        Assert.Equal("MARQUES FERNANDEZ, ENRIQUE", last.PlayerName);
        Assert.True(sheet.Events.Select(e => e.Minute).SequenceEqual(sheet.Events.Select(e => e.Minute).OrderBy(m => m)));

        // Tarjeta a un delegado: queda como suceso aunque no haya línea.
        Assert.Contains(sheet.Events, e => e.Type == OfficialEventType.YellowCard && e.PlayerName == "TRELLES CAMPA, ADRIAN");
    }

    [Fact]
    public void A_page_without_teams_is_not_a_sheet()
    {
        Assert.Null(AsturfutbolPages.ParseSheet("<html><body><p>No se ha aceptado el cookie</p></body></html>", 90));
    }

    // ------------------------------------------------------------------ nombres

    [Theory]
    [InlineData("1ª Asturfútbol", "1ª AsturFútbol", true)]
    [InlineData("2ª Asturfútbol", "1ª AsturFútbol", false)]
    [InlineData("Tercera Federación", "3ª RFEF", true)]
    [InlineData("Tercera Federación de Fútbol Femenino", "3ª RFEF", true)]
    [InlineData("Liga Nacional Juvenil", "3ª RFEF", false)]
    public void Matches_federation_names_with_our_categories(string federation, string category, bool expected)
    {
        Assert.Equal(expected, AsturfutbolOfficialSheetProvider.Matches(federation, category));
    }

    [Theory]
    [InlineData("2026/2027", "20262027")]
    [InlineData("2026-2027", "20262027")]
    [InlineData("2026-27", "20262027")]
    public void Season_labels_compare_by_their_years(string label, string key)
    {
        Assert.Equal(key, AsturfutbolOfficialSheetProvider.SeasonKey(label));
    }
}
