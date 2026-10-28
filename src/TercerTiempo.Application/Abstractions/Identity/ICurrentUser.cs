namespace TercerTiempo.Application.Abstractions.Identity;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    AccountType? AccountType { get; }

    Guid? PlayerId { get; }

    Guid? CoachId { get; }

    Guid? ClubId { get; }

    // El administrador lo ve todo para el mantenimiento de la web, pero no tiene perfil: nunca gestiona ni edita lo de un
    // club, un técnico o un jugador (las rutas de las cuentas solo le dejan leer).
    bool IsAdmin => false;

    string? Email => null;

    // Un inicio de sesión: el identificador de su token (jti). La misma cuenta en dos dispositivos son dos sesiones.
    string? SessionId => null;
}
