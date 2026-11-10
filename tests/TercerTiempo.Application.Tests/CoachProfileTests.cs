using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Coaches.Profile;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// El perfil de un técnico: los equipos de sus nombramientos (los vigentes primero, sin los revocados) y si es el tuyo.
public class CoachProfileTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly Club _club = Club.Register(Guid.NewGuid(), "Hispano", Now);
    private readonly Coach _coach = Coach.Register(Guid.NewGuid(), "Xurde Menéndez Cueto", Now);
    private readonly Team _first = Team.Create("Hispano", "HIS", null);
    private readonly Team _filial = Team.Create("Hispano 'B'", "HIB", null);
    private readonly Competition _competition = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", null);
    private readonly List<CoachAppointment> _appointments = [];

    public CoachProfileTests()
    {
        var current = _competition.AddSeason("2026/2027", Today.AddDays(-60), Today.AddDays(200), null);
        var past = _competition.AddSeason("2025/2026", Today.AddDays(-420), Today.AddDays(-70), null);

        var active = CoachAppointment.Create(_club.Id, _first.Id, current.Id, _coach.DisplayName, null, StaffRole.HeadCoach, Today.AddDays(-30), Today.AddDays(200), Now);
        active.Redeem(_coach.Id, Now);

        var expired = CoachAppointment.Create(_club.Id, _filial.Id, past.Id, _coach.DisplayName, null, StaffRole.AssistantCoach, Today.AddDays(-400), Today.AddDays(-70), Now.AddDays(-400));
        expired.Redeem(_coach.Id, Now.AddDays(-390));

        var revoked = CoachAppointment.Create(_club.Id, _filial.Id, current.Id, _coach.DisplayName, null, StaffRole.Analyst, Today.AddDays(-20), Today.AddDays(200), Now);
        revoked.Redeem(_coach.Id, Now);
        revoked.Revoke(Now);

        _appointments.AddRange([expired, revoked, active]);
    }

    [Fact]
    public async Task The_profile_lists_current_teams_first_and_hides_revoked_appointments()
    {
        var profile = await Handler(new Viewer(CoachId: Guid.NewGuid())).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.Equal("Xurde Menéndez Cueto", profile.DisplayName);
        Assert.False(profile.IsMe);
        Assert.Equal([("Hispano", true, StaffRole.HeadCoach), ("Hispano 'B'", false, StaffRole.AssistantCoach)],
            profile.Teams.Select(t => (t.TeamName, t.Current, t.Role)));
        Assert.All(profile.Teams, t => Assert.Equal("Hispano", t.ClubName));
    }

    [Fact]
    public async Task Only_the_coach_sees_the_profile_as_their_own()
    {
        var mine = await Handler(new Viewer(CoachId: _coach.Id)).Handle(new GetCoachProfileQuery(_coach.Id), default);
        var club = await Handler(new Viewer(ClubId: _club.Id)).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.True(mine.IsMe);
        Assert.False(club.IsMe);
        Assert.Null(mine.PhotoUrl);
    }

    [Fact]
    public async Task The_photo_link_carries_the_version_of_the_file()
    {
        _coach.SetPhoto($"{_coach.Id:N}-abc123.png");

        var profile = await Handler(new Viewer(CoachId: _coach.Id)).Handle(new GetCoachProfileQuery(_coach.Id), default);

        Assert.Equal($"/api/coaches/{_coach.Id}/photo?v=abc123", profile.PhotoUrl);
    }

    private GetCoachProfileHandler Handler(ICurrentUser viewer)
    {
        var stores = new AccountStores(
            Stub<TercerTiempo.Domain.Players.IPlayerStore>(),
            Stub<ICoachStore>((method, args) => method == nameof(ICoachStore.GetByIdAsync)
                ? Task.FromResult<Coach?>((Guid)args![0]! == _coach.Id ? _coach : null)
                : throw new NotSupportedException(method)),
            Stub<IClubStore>((method, args) => method == nameof(IClubStore.GetByIdAsync)
                ? Task.FromResult<Club?>((Guid)args![0]! == _club.Id ? _club : null)
                : throw new NotSupportedException(method)),
            Stub<ICoachAppointmentStore>((method, _) => method == nameof(ICoachAppointmentStore.GetByCoachAsync)
                ? Task.FromResult<IReadOnlyList<CoachAppointment>>(_appointments)
                : throw new NotSupportedException(method)),
            Stub<ISquadStore>((method, _) => method == nameof(ISquadStore.GetByTeamAndSeasonAsync)
                ? Task.FromResult<Squad?>(null)
                : throw new NotSupportedException(method)),
            Stub<ITeamStore>((method, args) => method == nameof(ITeamStore.GetByIdAsync)
                ? Task.FromResult(new[] { _first, _filial }.FirstOrDefault(t => t.Id == (Guid)args![0]!))
                : throw new NotSupportedException(method)),
            Stub<ICompetitionStore>((method, _) => method == nameof(ICompetitionStore.GetBySeasonAsync)
                ? Task.FromResult<Competition?>(_competition)
                : throw new NotSupportedException(method)),
            Stub<Licensing.ILicenseGate>(),
            TimeProvider.System,
            new NoSeasons());
        return new GetCoachProfileHandler(viewer, stores);
    }

    private sealed record Viewer(Guid? CoachId = null, Guid? ClubId = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public Guid? PlayerId => null;
        public AccountType? AccountType => CoachId != null ? Abstractions.Identity.AccountType.Coach : Abstractions.Identity.AccountType.Club;
    }

    private static T Stub<T>(Func<string, object?[]?, object?>? call = null) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call ?? ((method, _) => throw new NotSupportedException(method));
        return stub;
    }
}
