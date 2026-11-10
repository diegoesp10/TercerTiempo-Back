namespace TercerTiempo.Application.Abstractions.Identity;

public sealed record AuthToken(string AccessToken, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AuthToken CreateToken(Guid userId, string email, AccountType accountType, Guid profileId, string sessionStamp);

    AuthToken CreateAdminToken(Guid userId, string userName, string sessionStamp);
}
