namespace TercerTiempo.Application.Abstractions.Identity;

/// <summary>Usuario que ejecuta la petición actual (lo resuelve la API a partir del JWT).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    AccountType? AccountType { get; }

    /// <summary>Id del perfil de jugador, si la cuenta es de jugador.</summary>
    Guid? PlayerId { get; }

    /// <summary>Id del perfil de técnico, si la cuenta es del cuerpo técnico.</summary>
    Guid? CoachId { get; }
}
