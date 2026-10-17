namespace TercerTiempo.Application.Abstractions.Identity;

/// <summary>Gestión de usuarios/credenciales. La infraestructura lo implementa con ASP.NET Core Identity.</summary>
public interface IIdentityService
{
    /// <summary>Crea el usuario y devuelve su Id. Lanza ConflictException si el email ya existe.</summary>
    Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>Devuelve el Id del usuario si las credenciales son válidas; null en caso contrario.</summary>
    Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);
}
