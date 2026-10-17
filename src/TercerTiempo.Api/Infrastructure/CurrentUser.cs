using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using TercerTiempo.Application.Abstractions.Identity;

namespace TercerTiempo.Api.Infrastructure;

/// <summary>Resuelve el usuario/jugador autenticado a partir de los claims del JWT de la petición.</summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => ParseGuid(
        Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? PlayerId => ParseGuid(Principal?.FindFirstValue(AuthClaims.PlayerId));

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
