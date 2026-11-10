using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Tests;

public class CoachTests
{
    [Fact]
    public void Register_normalizes_name()
    {
        var coach = Coach.Register(Guid.NewGuid(), "  Óscar Cano ", TestData.Now);

        Assert.Equal("Óscar Cano", coach.DisplayName);
        Assert.Equal("oscar cano", coach.NormalizedName);
        Assert.Equal(TestData.Now, coach.RegisteredAt);
    }

    [Fact]
    public void Register_requires_a_user_and_a_name()
    {
        Assert.Throws<DomainException>(() => Coach.Register(Guid.Empty, "Míster", TestData.Now));
        Assert.Throws<DomainException>(() => Coach.Register(Guid.NewGuid(), "", TestData.Now));
    }

    [Fact]
    public void UpdateProfile_changes_name()
    {
        var coach = TestData.NewCoach();

        coach.UpdateProfile("Nuevo Míster");

        Assert.Equal("Nuevo Míster", coach.DisplayName);
        Assert.Equal("nuevo mister", coach.NormalizedName);
    }
}
