using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Grounds;

namespace TercerTiempo.Domain.Tests;

public class GroundTests
{
    [Fact]
    public void A_new_ground_is_searchable_without_accents_and_waits_to_be_located()
    {
        var ground = Ground.Create("  Hermanos Llana  ", TestData.Now);

        Assert.Equal("Hermanos Llana", ground.Name);
        Assert.Equal(Ground.KeyOf("HERMANOS LLANA"), ground.Key);
        Assert.Equal(Ground.KeyOf("Pixárra"), Ground.KeyOf("pixarra"));
        Assert.False(ground.IsLocated);
        Assert.True(ground.NeedsLocating(TestData.Now));
        Assert.Throws<DomainException>(() => Ground.Create("   ", TestData.Now));
    }

    [Fact]
    public void Not_finding_it_waits_a_month_between_tries_and_gives_up_after_three()
    {
        var ground = Ground.Create("La Pixarra", TestData.Now);
        var when = TestData.Now;

        for (var attempt = 1; attempt <= Ground.MaxLocateAttempts; attempt++)
        {
            Assert.True(ground.NeedsLocating(when));
            ground.Locate(null, when);
            Assert.Equal(attempt, ground.LocateAttempts);
            Assert.False(ground.NeedsLocating(when.AddDays(29)));
            when += Ground.RetryLocateAfter;
        }

        Assert.False(ground.NeedsLocating(when.AddYears(1)));
        Assert.StartsWith("https://www.google.com/maps/search/?api=1&query=Campo%20de%20f%C3%BAtbol%20La%20Pixarra%2C%20Asturias", ground.MapsUrl);
    }

    [Fact]
    public void Once_located_the_map_points_to_the_exact_spot()
    {
        var ground = Ground.Create("La Florida", TestData.Now);

        ground.Locate(new GroundLocation("Calle Fray Ceferino 12, 33013 Oviedo", "Oviedo", 43.3612345678m, -5.8498765432m), TestData.Now);

        Assert.True(ground.IsLocated);
        Assert.False(ground.NeedsLocating(TestData.Now.AddYears(1)));
        Assert.Equal((43.361235m, -5.849877m), (ground.Latitude!.Value, ground.Longitude!.Value));
        Assert.Equal("Oviedo", ground.Locality);
        Assert.Equal("https://www.google.com/maps/search/?api=1&query=43.361235%2C-5.849877", ground.MapsUrl);
        Assert.Throws<DomainException>(() => ground.Locate(new GroundLocation(null, null, 91m, 0m), TestData.Now));
    }
}
