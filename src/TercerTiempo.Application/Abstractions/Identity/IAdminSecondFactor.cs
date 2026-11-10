using TercerTiempo.Application.Admin;

namespace TercerTiempo.Application.Abstractions.Identity;

public sealed record VerifiedAdmin(Guid UserId, string UserName);

// Segundo paso del login del administrador: un código TOTP de una app de autenticación (Microsoft Authenticator).
// Solo se pide donde la configuración lo exige (Admin:RequireMfa, apagado en Development).
public interface IAdminSecondFactor
{
    bool Required { get; }

    // Tras una contraseña buena: el billete para mandar el código y, si la app aún no está dada de alta, su clave.
    Task<AdminMfaChallenge> ChallengeAsync(Guid userId, string userName, CancellationToken cancellationToken);

    // El primer código bueno deja la app dada de alta; cada código falso cuenta para el bloqueo de la cuenta.
    Task<VerifiedAdmin> VerifyAsync(string ticket, string code, CancellationToken cancellationToken);
}
