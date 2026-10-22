namespace TercerTiempo.Application.Abstractions.Identity;

public interface IIdentityService
{
    Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken);

    Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);
}
