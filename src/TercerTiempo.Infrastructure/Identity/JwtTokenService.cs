using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TercerTiempo.Application.Abstractions.Identity;

namespace TercerTiempo.Infrastructure.Identity;

internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AuthToken CreateToken(Guid userId, string email, AccountType accountType, Guid profileId)
    {
        return Create(new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [JwtRegisteredClaimNames.Email] = email,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [AuthClaims.AccountType] = accountType.ToString(),
            [AuthClaims.ProfileClaimFor(accountType)] = profileId.ToString(),
        });
    }

    public AuthToken CreateAdminToken(Guid userId, string userName) =>
        Create(new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [JwtRegisteredClaimNames.UniqueName] = userName,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [AuthClaims.Admin] = "true",
        });

    private AuthToken Create(Dictionary<string, object> claims)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(settings.ExpirationMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        };

        return new AuthToken(_handler.CreateToken(descriptor), expires);
    }
}
