namespace TercerTiempo.Application.Abstractions.Identity;

// Cuándo se cambió la contraseña por última vez y desde cuándo se puede volver a cambiar (null: ya se puede).
public sealed record PasswordState(DateTimeOffset? ChangedAt, DateTimeOffset? NextChangeAt);

public interface IIdentityService
{
    Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken);

    // Cada fallo cuenta para el bloqueo temporal; con la cuenta bloqueada lanza auth.locked_out.
    Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    // Solo usuarios con el rol Admin; cada fallo cuenta para el bloqueo temporal de Identity. Con el segundo paso
    // pendiente (MFA), una contraseña buena no borra los fallos: así no se puede probar códigos sin fin.
    Task<Guid?> ValidateAdminAsync(string userName, string password, bool secondFactorPending, CancellationToken cancellationToken);

    // Huella de la sesión que viaja en el token: cambia con la contraseña y deja sin valor los tokens anteriores.
    Task<string> GetSessionStampAsync(Guid userId, CancellationToken cancellationToken);

    Task<PasswordState> GetPasswordStateAsync(Guid userId, CancellationToken cancellationToken);

    // Devuelve el email de la cuenta, para emitir el token de la sesión que hace el cambio.
    Task<string> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);
}
