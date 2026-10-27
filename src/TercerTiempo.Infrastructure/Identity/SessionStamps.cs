using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Identity;

// La huella de sesión de cada usuario es un resumen de su SecurityStamp de Identity, que cambia con la contraseña: un
// token con otra huella es de antes del cambio y deja de valer. Se guarda dos minutos en memoria para no ir a la base en
// cada petición; el cambio de contraseña la olvida al momento en esta instancia.
public sealed class SessionStamps(IMemoryCache cache, IServiceScopeFactory scopes)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    public static string Fingerprint(string? securityStamp) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty)))[..16];

    public async Task<bool> IsCurrentAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var stamp = principal?.FindFirst(AuthClaims.SessionStamp)?.Value;
        if (string.IsNullOrEmpty(stamp) || !Guid.TryParse(principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
            return false;
        return await CurrentAsync(userId, cancellationToken) == stamp;
    }

    public void Forget(Guid userId) => cache.Remove(Key(userId));

    private async Task<string?> CurrentAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(userId), out string? known)) return known;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.SecurityStamp }).FirstOrDefaultAsync(cancellationToken);
        var current = user is null ? null : Fingerprint(user.SecurityStamp);
        cache.Set(Key(userId), current, CacheFor);
        return current;
    }

    private static string Key(Guid userId) => $"session-stamp:{userId}";
}
