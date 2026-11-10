using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Passes;

namespace TercerTiempo.Domain.Tests;

public class PassTests
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private static MatchPass Pass(int price = 0, bool sheet = true, bool video = true, bool download = false, Guid? forUser = null) =>
        MatchPass.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sheet, video, download,
            price, "EUR", null, null, Guid.NewGuid(), TestData.Now, Window, forUser);

    [Fact]
    public void Pass_must_open_something_and_download_needs_the_video()
    {
        Assert.Equal(ErrorCode.PassEmpty, Assert.Throws<DomainException>(() => Pass(sheet: false, video: false)).Code);
        Assert.Equal(ErrorCode.PassDownloadNeedsVideo, Assert.Throws<DomainException>(() => Pass(video: false, download: true)).Code);
        Assert.Throws<DomainException>(() => MatchPass.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            true, false, false, 0, "EUR", null, Guid.NewGuid(), Guid.NewGuid(), TestData.Now, Window));
        Assert.Throws<DomainException>(() => Pass(price: MatchPass.MaxPriceCents + 1));
    }

    [Fact]
    public void Code_is_redeemed_only_once_and_stays_with_that_account()
    {
        var pass = Pass();
        Assert.Equal(MatchPass.CodeLength, pass.Code.Length);
        var buyer = Guid.NewGuid();

        pass.Redeem(buyer, "Analista", TestData.Now.AddDays(1));
        Assert.True(pass.Grants(buyer));
        Assert.Equal(MatchPassStatus.Redeemed, pass.StatusOn(TestData.Now.AddYears(1)));

        Assert.Equal(ErrorCode.PassAlreadyYours, Assert.Throws<DomainException>(() => pass.Redeem(buyer, "Analista", TestData.Now.AddDays(2))).Code);
        var other = Guid.NewGuid();
        Assert.Equal(ErrorCode.PassAlreadyRedeemed, Assert.Throws<DomainException>(() => pass.Redeem(other, "Otro", TestData.Now.AddDays(2))).Code);
        Assert.False(pass.Grants(other));
    }

    [Fact]
    public void Unredeemed_code_expires_and_a_requested_one_only_works_for_the_requester()
    {
        var expired = Pass();
        Assert.Equal(MatchPassStatus.Expired, expired.StatusOn(TestData.Now + Window + TimeSpan.FromSeconds(1)));
        Assert.Equal(ErrorCode.PassExpired,
            Assert.Throws<DomainException>(() => expired.Redeem(Guid.NewGuid(), "Tarde", TestData.Now + Window + TimeSpan.FromSeconds(1))).Code);

        var requester = Guid.NewGuid();
        var personal = Pass(forUser: requester);
        Assert.Equal(ErrorCode.PassNotForYou, Assert.Throws<DomainException>(() => personal.Redeem(Guid.NewGuid(), "Otro", TestData.Now)).Code);
        personal.Redeem(requester, "Quien lo pidió", TestData.Now);
        Assert.True(personal.Grants(requester));
    }

    [Fact]
    public void Free_pass_can_be_revoked_but_a_paid_and_redeemed_one_cannot()
    {
        var buyer = Guid.NewGuid();
        var free = Pass();
        free.Redeem(buyer, "Invitado", TestData.Now);
        free.Revoke(TestData.Now.AddDays(1));
        Assert.False(free.Grants(buyer));

        var revoked = Pass();
        revoked.Revoke(TestData.Now);
        Assert.Equal(ErrorCode.PassRevoked, Assert.Throws<DomainException>(() => revoked.Redeem(buyer, "Invitado", TestData.Now)).Code);

        var paid = Pass(price: 500);
        paid.Redeem(buyer, "Comprador", TestData.Now);
        Assert.Equal(ErrorCode.PassPaidCannotRevoke, Assert.Throws<DomainException>(() => paid.Revoke(TestData.Now)).Code);
        Assert.True(paid.Grants(buyer));
    }

    [Fact]
    public void Failed_payment_releases_the_code()
    {
        var pass = Pass(price: 300);
        var buyer = Guid.NewGuid();
        pass.Redeem(buyer, "Comprador", TestData.Now);
        pass.ReleaseRedemption();
        Assert.False(pass.Grants(buyer));
        Assert.Equal(MatchPassStatus.Pending, pass.StatusOn(TestData.Now));
        pass.Redeem(buyer, "Comprador", TestData.Now);
    }

    [Theory]
    [InlineData(1000, 60, 600, 400)]
    [InlineData(999, 60, 599, 400)]
    [InlineData(5, 60, 3, 2)]
    [InlineData(250, 100, 250, 0)]
    public void Sale_splits_the_price_between_club_and_platform(int price, int percent, int club, int platform)
    {
        var pass = Pass(price: price);
        var sale = PassSale.Record(pass, Guid.NewGuid(), percent, "fake", "ref-1", TestData.Now);
        Assert.Equal(price, sale.AmountCents);
        Assert.Equal(club, sale.ClubShareCents);
        Assert.Equal(platform, sale.PlatformShareCents);
        Assert.Equal(price, sale.ClubShareCents + sale.PlatformShareCents);
        Assert.Equal(percent, sale.ClubSharePercent);
    }

    [Fact]
    public void Request_is_decided_once()
    {
        var request = MatchPassRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Analista",
            true, false, "Para preparar el partido de vuelta", TestData.Now);
        Assert.True(request.IsPending);
        var passId = Guid.NewGuid();
        request.Approve(passId, TestData.Now.AddHours(1));
        Assert.Equal(MatchPassRequestStatus.Approved, request.Status);
        Assert.Equal(passId, request.PassId);
        Assert.Equal(ErrorCode.PassRequestClosed, Assert.Throws<DomainException>(() => request.Reject(TestData.Now.AddHours(2))).Code);
        Assert.Equal(ErrorCode.PassRequestClosed, Assert.Throws<DomainException>(() => request.Cancel(TestData.Now.AddHours(2))).Code);
        Assert.Throws<DomainException>(() => MatchPassRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Analista", false, false, null, TestData.Now));
    }

    [Fact]
    public void Match_price_falls_back_to_the_club_when_both_are_cleared()
    {
        var price = MatchPassPrice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TestData.Now);
        price.Set(300, null, TestData.Now);
        Assert.False(price.IsEmpty);
        Assert.Null(price.VideoPriceCents);
        price.Set(null, null, TestData.Now);
        Assert.True(price.IsEmpty);
        Assert.Throws<DomainException>(() => price.Set(-1, null, TestData.Now));
    }
}
