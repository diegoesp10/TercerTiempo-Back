using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

public class CoachAppointmentTests
{
    private static readonly DateOnly SeasonEnd = new(2027, 6, 30);

    private static CoachAppointment NewAppointment(DateOnly? validUntil = null) =>
        CoachAppointment.Create(Guid.NewGuid(), TestData.HomeTeamId, TestData.SeasonId, "Abel Pablo Fernández", "abel@example.com",
            StaffRole.HeadCoach, TestData.Today, validUntil ?? SeasonEnd, TestData.Now);

    [Fact]
    public void Create_issues_an_access_code_and_starts_pending()
    {
        var appointment = NewAppointment();

        Assert.Equal(AccessCodes.Length, appointment.AccessCode.Length);
        Assert.Equal("abel pablo fernandez", appointment.NormalizedCoachName);
        Assert.Equal("abel@example.com", appointment.Email);
        Assert.False(appointment.IsRedeemed);
        Assert.False(appointment.IsActiveOn(TestData.Today));
        Assert.Equal(AppointmentStatus.Pending, appointment.StatusOn(TestData.Today));
    }

    [Fact]
    public void Validity_cannot_end_before_it_starts()
    {
        Assert.Throws<DomainException>(() => NewAppointment(TestData.Today.AddDays(-1)));
        Assert.Throws<DomainException>(() => NewAppointment().SetValidity(TestData.Today.AddDays(-1)));
    }

    [Fact]
    public void Redeem_binds_the_coach_and_activates_it()
    {
        var appointment = NewAppointment();
        var coachId = Guid.NewGuid();

        appointment.Redeem(coachId, TestData.Now.AddHours(1));

        Assert.Equal(coachId, appointment.CoachId);
        Assert.Equal(TestData.Now.AddHours(1), appointment.RedeemedAt);
        Assert.True(appointment.IsActiveOn(TestData.Today));
        Assert.Equal(AppointmentStatus.Active, appointment.StatusOn(TestData.Today));
        Assert.False(appointment.IsActiveOn(SeasonEnd.AddDays(1)));
        Assert.Equal(AppointmentStatus.Expired, appointment.StatusOn(SeasonEnd.AddDays(1)));
    }

    [Fact]
    public void Code_can_only_be_redeemed_once()
    {
        var appointment = NewAppointment();
        appointment.Redeem(Guid.NewGuid(), TestData.Now);

        Assert.Throws<DomainException>(() => appointment.Redeem(Guid.NewGuid(), TestData.Now));
        Assert.Throws<DomainException>(() => appointment.RegenerateAccessCode());
    }

    [Fact]
    public void Expired_or_revoked_codes_cannot_be_redeemed()
    {
        var expired = NewAppointment(TestData.Today);
        Assert.Throws<DomainException>(() => expired.Redeem(Guid.NewGuid(), TestData.Now.AddDays(2)));

        var revoked = NewAppointment();
        revoked.Revoke(TestData.Now);
        Assert.Throws<DomainException>(() => revoked.Redeem(Guid.NewGuid(), TestData.Now));
        Assert.Equal(AppointmentStatus.Revoked, revoked.StatusOn(TestData.Today));
        Assert.Throws<DomainException>(() => revoked.Revoke(TestData.Now));
        Assert.Throws<DomainException>(() => revoked.SetValidity(SeasonEnd));
    }

    [Fact]
    public void Regenerating_the_code_replaces_it_while_pending()
    {
        var appointment = NewAppointment();
        var before = appointment.AccessCode;

        appointment.RegenerateAccessCode();

        Assert.NotEqual(before, appointment.AccessCode);
    }

    [Fact]
    public void Club_manages_its_teams_without_duplicates()
    {
        var club = Club.Register(Guid.NewGuid(), " C.D. Vallobín ", TestData.Now);

        club.AddTeam(TestData.HomeTeamId, TestData.Now);

        Assert.Equal("C.D. Vallobín", club.Name);
        Assert.Equal("c d vallobin", club.NormalizedName);
        Assert.True(club.Manages(TestData.HomeTeamId));
        Assert.Throws<DomainException>(() => club.AddTeam(TestData.HomeTeamId, TestData.Now));

        // El club conserva al menos un equipo: se quita un filial, no el último.
        Assert.Throws<DomainException>(() => club.RemoveTeam(TestData.HomeTeamId));
        club.AddTeam(TestData.AwayTeamId, TestData.Now);
        club.RemoveTeam(TestData.HomeTeamId);
        Assert.False(club.Manages(TestData.HomeTeamId));
        Assert.Throws<DomainException>(() => club.RemoveTeam(TestData.HomeTeamId));
    }
}
