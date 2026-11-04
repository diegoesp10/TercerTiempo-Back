using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

// Un vídeo nunca es público: lo ve quien pertenece a un equipo con licencia o quien tenga un pase del partido, y en ese
// caso basta la licencia del equipo que lo subió.
public class LicenseGateVideoTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly Club _club = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Squad _squad;
    private readonly MatchVideo _video;
    private readonly List<MatchPass> _redeemed = [];

    public LicenseGateVideoTests()
    {
        _club.AddTeam(_teamId, Now);
        _squad = Squad.Open(_teamId, Guid.NewGuid(), Now);
        _video = MatchVideo.Upload(_squad.Id, _teamId, Guid.NewGuid(), _squad.SeasonId, null, VideoPart.Full, null, "partido.mp4", "video/mp4",
            50_000_000, "clave", Now, _club.Id);
    }

    [Fact]
    public async Task A_pass_holder_only_needs_the_license_of_the_team_that_uploaded_it()
    {
        var viewer = new SignedIn(Guid.NewGuid());
        var pass = MatchPass.Create(_squad.Id, _teamId, _video.MatchId, _club.Id, false, true, false, 0, "EUR", null, null, _club.Id, Now, TimeSpan.FromDays(30));
        pass.Redeem(viewer.UserId!.Value, "Analista", Now);
        _redeemed.Add(pass);

        await Gate(ClubLicense.Start(_club.Id, Today, 30, Now)).EnsureVideoAccessAsync(_video.Id, viewer, default);

        var expired = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Gate(ClubLicense.Start(_club.Id, Today.AddDays(-60), 30, Now)).EnsureVideoAccessAsync(_video.Id, viewer, default));
        Assert.Equal(ErrorCode.LicenseRequired, expired.Code);
    }

    [Fact]
    public async Task Without_a_pass_the_viewer_must_belong_to_a_licensed_team()
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Gate(ClubLicense.Start(_club.Id, Today, 30, Now)).EnsureVideoAccessAsync(_video.Id, new Anonymous(), default));

        Assert.Equal(ErrorCode.LicenseViewerRequired, error.Code);
    }

    private LicenseGate Gate(ClubLicense license) => new(
        Stub<ILicenseStore>((method, _) => method == nameof(ILicenseStore.GetByClubAsync) ? Task.FromResult<ClubLicense?>(license) : throw new NotSupportedException(method)),
        Stub<IClubStore>((method, _) => method switch
        {
            nameof(IClubStore.GetByTeamAsync) or nameof(IClubStore.GetByIdAsync) => Task.FromResult<Club?>(_club),
            _ => throw new NotSupportedException(method),
        }),
        Stub<ISquadStore>((method, _) => method == nameof(ISquadStore.GetByIdAsync) ? Task.FromResult<Squad?>(_squad) : throw new NotSupportedException(method)),
        Stub<IMatchVideoStore>((method, _) => method == nameof(IMatchVideoStore.GetByIdAsync) ? Task.FromResult<MatchVideo?>(_video) : throw new NotSupportedException(method)),
        Stub<IMatchPassStore>((method, _) => method == nameof(IMatchPassStore.GetRedeemedByAsync)
            ? Task.FromResult<IReadOnlyList<MatchPass>>(_redeemed)
            : throw new NotSupportedException(method)),
        Stub<IPlayerStore>((method, _) => throw new NotSupportedException(method)),
        Stub<IUnitOfWork>((method, _) => throw new NotSupportedException(method)),
        Stub<ILicensingCatalog>((method, _) => throw new NotSupportedException(method)),
        TimeProvider.System);

    private sealed record SignedIn(Guid Id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Id;
        public AccountType? AccountType => Abstractions.Identity.AccountType.Coach;
        public Guid? PlayerId => null;
        public Guid? CoachId => Guid.NewGuid();
        public Guid? ClubId => null;
    }

    private sealed record Anonymous : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public AccountType? AccountType => null;
        public Guid? PlayerId => null;
        public Guid? CoachId => null;
        public Guid? ClubId => null;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, ClubAsStaffTests.StoreProxy>();
        ((ClubAsStaffTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}
