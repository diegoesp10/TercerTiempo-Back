namespace TercerTiempo.Application.Abstractions.Identity;

public interface IIdentityService
{
    Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken);

    Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    // Solo usuarios con el rol Admin; cada fallo cuenta para el bloqueo temporal de Identity.
    Task<Guid?> ValidateAdminAsync(string userName, string password, CancellationToken cancellationToken);
}
