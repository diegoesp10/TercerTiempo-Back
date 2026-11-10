using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Domain.Tests;

public class CompetitionTests
{
    [Fact]
    public void Sort_order_follows_level_tier_and_group()
    {
        var tercera = Competition.Create("3ª RFEF", "3ª RFEF", CompetitionLevel.National, 3, null, "Asturias", null);
        var primeraAf = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var segundaG1 = Competition.Create("2ª AsturFútbol · Grupo 1", "2ª AsturFútbol", CompetitionLevel.Regional, 2, "1", "Asturias", null);
        var segundaG2 = Competition.Create("2ª AsturFútbol · Grupo 2", "2ª AsturFútbol", CompetitionLevel.Regional, 2, "2", "Asturias", null);

        Assert.True(tercera.SortOrder < primeraAf.SortOrder);
        Assert.True(primeraAf.SortOrder < segundaG1.SortOrder);
        Assert.True(segundaG1.SortOrder < segundaG2.SortOrder);
    }

    [Fact]
    public void Only_one_season_is_current()
    {
        var competition = Competition.Create("3ª RFEF", "3ª RFEF", CompetitionLevel.National, 3, null, "Asturias", null);
        var previous = competition.AddSeason("2025/2026", new DateOnly(2025, 8, 1), new DateOnly(2026, 6, 30), null);
        var current = competition.AddSeason("2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null);

        competition.MarkCurrent(previous.Id);
        competition.MarkCurrent(current.Id);

        Assert.Same(current, competition.CurrentSeason);
        Assert.False(previous.IsCurrent);
    }

    [Fact]
    public void Season_labels_are_unique_and_dates_consistent()
    {
        var competition = Competition.Create("3ª RFEF", "3ª RFEF", CompetitionLevel.National, 3, null, "Asturias", null);
        competition.AddSeason("2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null);

        Assert.Throws<DomainException>(() => competition.AddSeason("2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null));
        Assert.Throws<DomainException>(() => competition.AddSeason("2027/2028", new DateOnly(2027, 8, 1), new DateOnly(2027, 7, 1), null));
    }
}
