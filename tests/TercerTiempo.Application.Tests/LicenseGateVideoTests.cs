using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

// Un vídeo público lo ve cualquiera que llegue al partido, con o sin cuenta, mientras el equipo que lo subió tenga
// licencia; uno privado, solo quien pertenece a un equipo con licencia.
public class LicenseGateVideoTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly Club _club = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Squad _squad;
    private readonly MatchVideo _video;

    public LicenseGateVideoTests()
    {
        _club.AddTeam(_teamId, Now);
        _squad = Squad.Open(_teamId, Guid.NewGuid(), Now);
        _video = MatchVideo.Upload(_squad.Id, _teamId, Guid.NewGuid(), _squad.SeasonId, null, VideoPart.Full, null, "partido.mp4", "video/mp4",
            50_000_000, "clave", 500, Now, _club.Id);
    }

    [Fact]
    public async Task A_public_video_only_needs_the_license_of_the_team_that_uploaded_it()
    {
        _video.SetVisibility(VideoVisibility.Public);

        await Gate(ClubLicense.Start(_club.Id, Today, 30, Now)).EnsureVideoAccessAsync(_video.Id, new Anonymous(), default);

        var expired = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Gate(ClubLicense.Start(_club.Id, Today.AddDays(-60), 30, Now)).EnsureVideoAccessAsync(_video.Id, new Anonymous(), default));
        Assert.Equal(ErrorCode.LicenseRequired, expired.Code);
    }

    [Fact]
    public async Task A_private_video_needs_a_viewer_from_a_licensed_team()
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
        Stub<IVideoShareStore>((method, _) => throw new NotSupportedException(method)),
        Stub<IPlayerStore>((method, _) => throw new NotSupportedException(method)),
        Stub<IUnitOfWork>((method, _) => throw new NotSupportedException(method)),
        Stub<ILicensingCatalog>((method, _) => throw new NotSupportedException(method)),
        TimeProvider.System);

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
