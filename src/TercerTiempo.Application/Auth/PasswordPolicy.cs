namespace TercerTiempo.Application.Auth;

// Reglas de las contraseñas de todas las cuentas (club, cuerpo técnico, jugador y administrador). Identity las aplica
// al crearlas y al cambiarlas; el frontal las repite para avisar mientras se escribe.
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;
    public const int MinUniqueChars = 5;
    public const int MaxRepeatedChars = 3;

    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Como mucho un cambio de contraseña cada 24 horas.
    public static readonly TimeSpan ChangeInterval = TimeSpan.FromHours(24);
}
