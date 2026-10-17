namespace TercerTiempo.Application.Abstractions.Identity;

public sealed record AuthToken(string AccessToken, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AuthToken CreateToken(Guid userId, string email, AccountType accountType, Guid profileId);
}
