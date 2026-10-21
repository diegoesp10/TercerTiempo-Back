using System.Reflection;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

/// <summary>Lectura de la ficha del equipo y de la página del campo de asturfutbol.es (HTML guardado, sin datos personales).</summary>
public class AsturfutbolTeamPagesTests
{
    private static string Fixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Team_page_gives_crest_seat_kit_and_ground()
    {
        var (profile, groundCode) = AsturfutbolPages.ParseTeamProfile(Fixture("equipo-3597.html"))!.Value;

        Assert.Contains("/pnfg/pimg/Clubes/", profile.CrestUrl);
        Assert.Equal("6662", groundCode);

        var club = profile.Profile;
        Assert.Equal("C.D. Vallobin", club.OfficialName);
        Assert.Equal("Vazquez de Mella 59 Bajo", club.Address);
        Assert.Equal("Oviedo", club.Locality);
        Assert.Equal("Asturias", club.Province);
        Assert.Equal("33012", club.PostalCode);
        Assert.Equal("www.cdvallobin.es", club.Website);
        Assert.Equal("Vallobin", club.Ground);
        Assert.Equal("Hierba artificial", club.GroundSurface);
        Assert.Equal("Azul", club.ShirtColor);
        Assert.Equal("Blanco", club.ShortsColor);
        Assert.Equal("Azules", club.SocksColor);
        Assert.Null(club.GroundAddress);
    }

    [Fact]
    public void Ground_page_gives_address_locality_and_surface()
    {
        var ground = AsturfutbolPages.ParseGround(Fixture("campo-6662.html"))!;

        Assert.Equal("Calle Av/ Monumentos Nº 61", ground.Address);
        Assert.Equal("Oviedo", ground.Locality);
        Assert.Equal("Hierba Artificial", ground.Surface);
    }

    [Theory]
    [InlineData("VALLOBIN(H.SINTETICA)", "Vallobin", "Hierba artificial")]
    [InlineData("LA TOBA (H.NATURAL)", "La Toba", "Hierba natural")]
    [InlineData("EL BAYU", "El Bayu", null)]
    [InlineData("", null, null)]
    public void Ground_name_and_surface_are_split(string raw, string? ground, string? surface)
    {
        Assert.Equal((ground, surface), AsturfutbolPages.SplitGround(raw));
    }

    [Fact]
    public void Pages_without_club_data_give_null()
    {
        Assert.Null(AsturfutbolPages.ParseTeamProfile("<html><body><p>No se ha aceptado el cookie</p></body></html>"));
        Assert.Null(AsturfutbolPages.ParseGround("<html><body></body></html>"));
    }
}
