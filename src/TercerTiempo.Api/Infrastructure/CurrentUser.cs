using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using TercerTiempo.Application.Abstractions.Identity;

namespace TercerTiempo.Api.Infrastructure;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => ParseGuid(
        Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public AccountType? AccountType =>
        Enum.TryParse<AccountType>(Principal?.FindFirstValue(AuthClaims.AccountType), out var type) ? type : null;

    public Guid? PlayerId => ParseGuid(Principal?.FindFirstValue(AuthClaims.PlayerId));

    public Guid? CoachId => ParseGuid(Principal?.FindFirstValue(AuthClaims.CoachId));

    public Guid? ClubId => ParseGuid(Principal?.FindFirstValue(AuthClaims.ClubId));

    public bool IsAdmin => Principal?.HasClaim(AuthClaims.Admin, "true") == true;

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public string? SessionId => Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
