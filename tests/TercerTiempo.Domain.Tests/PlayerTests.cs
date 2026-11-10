using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Tests;

public class PlayerTests
{
    [Fact]
    public void Register_normalizes_name_and_raises_event()
    {
        var player = Player.Register(Guid.NewGuid(), "  José Ángel Muñiz ", Position.Midfielder, null, TestData.Now);

        Assert.Equal("José Ángel Muñiz", player.DisplayName);
        Assert.Equal("jose angel muniz", player.NormalizedName);
        Assert.Single(player.DomainEvents);
        Assert.IsType<PlayerRegisteredEvent>(player.DomainEvents.First());
    }

    [Fact]
    public void Register_rejects_players_under_14()
    {
        var birthDate = DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddYears(-10);

        Assert.Throws<DomainException>(() =>
            Player.Register(Guid.NewGuid(), "Niño", Position.Forward, birthDate, TestData.Now));
    }

    [Fact]
    public void Register_rejects_unknown_position()
    {
        Assert.Throws<DomainException>(() =>
            Player.Register(Guid.NewGuid(), "Pelayo", (Position)99, null, TestData.Now));
    }

    [Fact]
    public void Register_requires_a_user_and_a_name()
    {
        Assert.Throws<DomainException>(() => Player.Register(Guid.Empty, "Pelayo", Position.Forward, null, TestData.Now));
        Assert.Throws<DomainException>(() => Player.Register(Guid.NewGuid(), "   ", Position.Forward, null, TestData.Now));
    }

    [Fact]
    public void UpdateProfile_changes_name_position_and_birth_date()
    {
        var player = TestData.NewPlayer();

        player.UpdateProfile(" Pelayo F. ", Position.Midfielder, new DateOnly(1999, 1, 1));

        Assert.Equal("Pelayo F.", player.DisplayName);
        Assert.Equal("pelayo f", player.NormalizedName);
        Assert.Equal(Position.Midfielder, player.Position);
        Assert.Equal(new DateOnly(1999, 1, 1), player.BirthDate);
    }
}
