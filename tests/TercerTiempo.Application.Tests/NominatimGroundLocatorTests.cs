using System.Text.Json;
using TercerTiempo.Infrastructure.Grounds;

namespace TercerTiempo.Application.Tests;

// OpenStreetMap devuelve de todo para un nombre («La Pixarra» también es una escultura): solo vale un campo o recinto
// deportivo cuyo nombre lleve el que se busca.
public class NominatimGroundLocatorTests
{
    private static IReadOnlyList<JsonElement> Results(string json) => JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

    [Fact]
    public void Takes_the_sports_pitch_and_builds_its_address()
    {
        var results = Results("""
            [
              { "category": "tourism", "type": "artwork", "name": "La Pixarra", "lat": "43.1", "lon": "-5.1" },
              { "category": "highway", "type": "residential", "name": "Calle La Pixarra", "lat": "43.2", "lon": "-5.2" },
              { "category": "leisure", "type": "pitch", "name": "Campo de fútbol de La Pixarra", "lat": "43.3585", "lon": "-5.8724",
                "address": { "road": "Calle Pixarra", "house_number": "3", "suburb": "Ventanielles", "postcode": "33010", "city": "Oviedo" } }
            ]
            """);

        var found = NominatimGroundLocator.Pick("La Pixarra", results);

        Assert.NotNull(found);
        Assert.Equal((43.3585m, -5.8724m), (found.Latitude, found.Longitude));
        Assert.Equal("Calle Pixarra 3, Ventanielles, 33010 Oviedo", found.Address);
        Assert.Equal("Oviedo", found.Locality);
    }

    [Fact]
    public void Rejects_anything_that_is_not_a_ground_with_that_name()
    {
        var results = Results("""
            [
              { "category": "tourism", "type": "artwork", "name": "La Pixarra", "lat": "43.1", "lon": "-5.1" },
              { "category": "leisure", "type": "pitch", "name": "Pista de pádel", "lat": "43.3", "lon": "-5.8" },
              { "category": "leisure", "type": "park", "name": "Parque de La Pixarra", "lat": "43.3", "lon": "-5.8" }
            ]
            """);

        Assert.Null(NominatimGroundLocator.Pick("La Pixarra", results));
    }

    [Fact]
    public void Matches_by_words_without_accents_when_the_order_changes()
    {
        var results = Results("""
            [ { "category": "leisure", "type": "stadium", "name": "Estadio Municipal Hermanos Llana", "lat": "43.5", "lon": "-5.7",
                "address": { "town": "Llanera" } } ]
            """);

        var found = NominatimGroundLocator.Pick("Llana Hermanos", results);

        Assert.Equal("Llanera", found?.Address);
    }
}
