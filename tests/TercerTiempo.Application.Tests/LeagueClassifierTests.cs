using TercerTiempo.Domain.Competitions;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

namespace TercerTiempo.Application.Tests;

public class LeagueClassifierTests
{
    [Theory]
    [InlineData("3ª RFEF", CompetitionLevel.National, 3, null, "3ª RFEF", "3ª RFEF")]
    [InlineData("1ª RFEF", CompetitionLevel.National, 1, null, "1ª RFEF", "1ª RFEF")]
    [InlineData("1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "1ª AsturFútbol", "1ª AsturFútbol")]
    [InlineData("2ª AsturFútbol - Grupo 1", CompetitionLevel.Regional, 2, "1", "2ª AsturFútbol", "2ª AsturFútbol · Grupo 1")]
    [InlineData("3ª AsturFútbol &#8211; Grupo 4", CompetitionLevel.Regional, 3, "4", "3ª AsturFútbol", "3ª AsturFútbol · Grupo 4")]
    [InlineData("Primera División", CompetitionLevel.Professional, 1, null, "Primera División", "Primera División")]
    [InlineData("Segunda División", CompetitionLevel.Professional, 2, null, "Segunda División", "Segunda División")]
    [InlineData("Copa AsturFútbol 2026 · Grupo A", CompetitionLevel.Cup, 0, "A", "Copa AsturFútbol 2026", "Copa AsturFútbol 2026 · Grupo A")]
    [InlineData("División de Honor juvenil", CompetitionLevel.Youth, 0, null, "División de Honor juvenil", "División de Honor juvenil")]
    [InlineData("Torneos y Copas", CompetitionLevel.Cup, 0, null, "Torneos y Copas", "Torneos y Copas")]
    public void Classifies_league_names_from_futbolasturiano(
        string raw, CompetitionLevel level, int tier, string? group, string category, string displayName)
    {
        var info = LeagueClassifier.Classify(raw);

        Assert.Equal(level, info.Level);
        Assert.Equal(tier, info.Tier);
        Assert.Equal(group, info.Group);
        Assert.Equal(category, info.Category);
        Assert.Equal(displayName, info.DisplayName);
    }

    [Theory]
    [InlineData("CD Lealtad de Villaviciosa", null, "LEA")]
    [InlineData("U.C. Ceares", "UCC", "UCC")]
    [InlineData("Sporting Atlético", null, "SPO")]
    [InlineData("L’Entregu C.F.", null, "LEN")]
    [InlineData("Gijón Industrial", null, "GIJ")]
    [InlineData("Racing de Pravia", null, "PRA")]
    [InlineData("CP Cacereño", null, "CAC")]
    [InlineData("AD Mérida", null, "MER")]
    [InlineData("RC Deportivo Fabril", null, "FAB")]
    [InlineData("Athletic Club «B»", null, "ATH")]
    [InlineData("Real Avilés Industrial", null, "AVI")]
    public void Builds_short_names(string name, string? abbreviation, string expected)
    {
        Assert.Equal(expected, TeamNames.ShortName(name, abbreviation));
    }
}
