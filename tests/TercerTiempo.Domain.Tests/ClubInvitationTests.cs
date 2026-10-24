using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Domain.Tests;

public class ClubInvitationTests
{
    [Fact]
    public void A_new_invitation_has_a_code_and_can_be_used_once()
    {
        var invitation = ClubInvitation.Create("CD Vallobín", null, TestData.Now);

        Assert.Equal(ClubInvitation.CodeLength, invitation.Code.Length);
        Assert.True(invitation.IsUsable(TestData.Now));

        var clubId = Guid.NewGuid();
        invitation.Redeem(clubId, TestData.Now);

        Assert.Equal(clubId, invitation.ClubId);
        Assert.False(invitation.IsUsable(TestData.Now));
        var error = Assert.Throws<DomainException>(() => invitation.Redeem(Guid.NewGuid(), TestData.Now));
        Assert.Equal(ErrorCode.AuthClubInviteInvalid, error.Code);
    }

    [Fact]
    public void Expired_or_revoked_invitations_cannot_be_used()
    {
        var expiring = ClubInvitation.Create(null, TestData.Now.AddDays(7), TestData.Now);
        Assert.True(expiring.IsUsable(TestData.Now.AddDays(6)));
        Assert.False(expiring.IsUsable(TestData.Now.AddDays(7)));

        var revoked = ClubInvitation.Create(null, null, TestData.Now);
        revoked.Revoke(TestData.Now);
        Assert.False(revoked.IsUsable(TestData.Now));
    }

    [Fact]
    public void A_used_invitation_cannot_be_revoked()
    {
        var invitation = ClubInvitation.Create(null, null, TestData.Now);
        invitation.Redeem(Guid.NewGuid(), TestData.Now);

        var error = Assert.Throws<DomainException>(() => invitation.Revoke(TestData.Now));
        Assert.Equal(ErrorCode.ClubInvitationAlreadyUsed, error.Code);
    }

    [Fact]
    public void Only_crests_uploaded_by_the_club_are_marked_as_theirs()
    {
        var team = Team.Create("CD Vallobín", "VAL", null);

        team.SetCrest("vallobin-1.png");
        Assert.False(team.CrestFromClub);

        team.SetCrest("vallobin-2.png", fromClub: true);
        Assert.True(team.CrestFromClub);
    }
}
