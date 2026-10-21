using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

/// <summary>Agrupación de los equipos del proveedor en clubes (primer equipo + filiales).</summary>
public class ClubCatalogTests
{
    private static CatalogTeam Team(string name, string? competition = null) =>
        new(Domain.Teams.Team.Create(name, name[..Math.Min(3, name.Length)], null), competition);

    [Fact]
    public void Reserve_teams_join_their_club_and_the_first_team_names_it()
    {
        var catalog = ClubCatalog.Build(
        [
            Team("Caudal Deportivo 'B'", "3ª AsturFútbol · Grupo 4"),
            Team("Caudal Deportivo", "3ª RFEF"),
            Team("CD Mosconia B", "2ª AsturFútbol · Grupo 1"),
            Team("CD Mosconia", "3ª RFEF"),
            Team("Barcia CF", "2ª AsturFútbol · Grupo 1"),
        ]);

        Assert.Equal(["Barcia CF", "Caudal Deportivo", "CD Mosconia"], catalog.Select(c => c.Name));

        var caudal = catalog.Single(c => c.Name == "Caudal Deportivo");
        Assert.Equal("caudal deportivo", caudal.NormalizedName);
        Assert.Equal(["Caudal Deportivo", "Caudal Deportivo 'B'"], caudal.Teams.Select(t => t.Team.Name));
        Assert.Equal("3ª RFEF", caudal.Teams[0].CompetitionName);
    }

    [Fact]
    public void A_reserve_with_the_short_name_joins_the_only_club_that_contains_it()
    {
        var catalog = ClubCatalog.Build(
        [
            Team("CD Lealtad 'B'"),
            Team("CD Lealtad de Villaviciosa"),
            Team("CD Llanes"),
        ]);

        var lealtad = Assert.Single(catalog, c => c.Name == "CD Lealtad de Villaviciosa");
        Assert.Equal(2, lealtad.Teams.Count);
        Assert.DoesNotContain(catalog, c => c.Name.Contains("'B'"));
    }

    [Fact]
    public void A_club_with_only_reserve_teams_is_named_without_the_letter()
    {
        var catalog = ClubCatalog.Build([Team("Real Sporting 'C'"), Team("Real Sporting 'B'")]);

        var sporting = Assert.Single(catalog);
        Assert.Equal("Real Sporting", sporting.Name);
        Assert.Equal(["Real Sporting 'B'", "Real Sporting 'C'"], sporting.Teams.Select(t => t.Team.Name));
    }

    [Fact]
    public void Clubs_with_different_names_stay_apart()
    {
        var catalog = ClubCatalog.Build([Team("Atlético de Lugones"), Team("Atlético La Florida"), Team("UD San Claudio")]);

        Assert.Equal(3, catalog.Count);
    }
}
