using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches.GetMatchPreview;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

public class MatchPreviewAccessTests
{
    [Theory]
    [InlineData(AccountType.Coach)]
    [InlineData(AccountType.Club)]
    public async Task Coaches_and_clubs_get_past_the_access_check(AccountType account)
    {
        // Sin partido en la base: si el permiso deja pasar, lo siguiente es «partido no encontrado».
        await Assert.ThrowsAsync<NotFoundException>(() => Handler(account).Handle(new(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task Players_cannot_see_the_preview()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Handler(AccountType.Player).Handle(new(Guid.NewGuid()), default));
    }

    private static GetMatchPreviewHandler Handler(AccountType account) => new(
        new User(account),
        Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(null)),
        Stub<ITeamStore>((method, _) => throw new Exception($"Unexpected call: {method}")),
        Stub<IStandingStore>((method, _) => throw new Exception($"Unexpected call: {method}")),
        Stub<ICompetitionStore>((method, _) => throw new Exception($"Unexpected call: {method}")),
        Stub<IOfficialSheetStore>((method, _) => throw new Exception($"Unexpected call: {method}")));

    private sealed record User(AccountType Account) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Account;
        public Guid? UserId => Guid.NewGuid();
        public Guid? PlayerId => Account == Abstractions.Identity.AccountType.Player ? Guid.NewGuid() : null;
        public Guid? CoachId => Account == Abstractions.Identity.AccountType.Coach ? Guid.NewGuid() : null;
        public Guid? ClubId => Account == Abstractions.Identity.AccountType.Club ? Guid.NewGuid() : null;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
