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
    public void Verified_join_creates_active_membership()
    {
        var player = TestData.NewPlayer();

        var membership = player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, 7, verifiedInRoster: true, TestData.Now);

        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.True(player.IsActiveIn(TestData.HomeTeamId, TestData.SeasonId));
    }

    [Fact]
    public void Unverified_join_creates_pending_membership()
    {
        var player = TestData.NewPlayer();

        var membership = player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, null, verifiedInRoster: false, TestData.Now);

        Assert.Equal(MembershipStatus.Pending, membership.Status);
        Assert.False(player.IsActiveIn(TestData.HomeTeamId, TestData.SeasonId));
        Assert.NotNull(player.MembershipFor(TestData.SeasonId));
    }

    [Fact]
    public void Cannot_join_two_squads_in_same_season()
    {
        var player = TestData.NewPlayer();
        player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, null, true, TestData.Now);

        Assert.Throws<DomainException>(() =>
            player.JoinSquad(TestData.AwayTeamId, TestData.SeasonId, null, true, TestData.Now));
    }

    [Fact]
    public void Can_join_another_squad_after_leaving()
    {
        var player = TestData.NewPlayer();
        player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, null, true, TestData.Now);

        player.LeaveSquad(TestData.SeasonId, TestData.Now.AddDays(1));
        var membership = player.JoinSquad(TestData.AwayTeamId, TestData.SeasonId, null, true, TestData.Now.AddDays(2));

        Assert.Equal(TestData.AwayTeamId, membership.TeamId);
        Assert.Equal(2, player.Memberships.Count);
        Assert.False(player.IsActiveIn(TestData.HomeTeamId, TestData.SeasonId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Shirt_number_must_be_between_1_and_99(int shirtNumber)
    {
        var player = TestData.NewPlayer();

        Assert.Throws<DomainException>(() =>
            player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, shirtNumber, true, TestData.Now));
    }
}
