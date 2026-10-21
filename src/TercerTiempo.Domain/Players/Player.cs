using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Players;

/// <summary>
/// Jugador con cuenta en la plataforma. Está vinculado a un usuario de identidad (UserId) y, cuando el cuerpo
/// técnico de su equipo le da acceso, a su ficha dentro de una plantilla (<see cref="Squads.SquadPlayer"/>).
/// Los datos de rendimiento los registra el cuerpo técnico; el jugador solo los consulta.
/// </summary>
public sealed class Player : AggregateRoot
{
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public Position Position { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }

    private Player() { }

    public static Player Register(Guid userId, string displayName, Position position, DateOnly? birthDate, DateTimeOffset now)
    {
        var name = Guard.NotEmpty(displayName, "nombre", 100);

        if (!Enum.IsDefined(position))
            throw new DomainException(ErrorCode.PlayerInvalidPosition);

        if (birthDate is { } date && date > DateOnly.FromDateTime(now.UtcDateTime).AddYears(-14))
            throw new DomainException(ErrorCode.PlayerMinAge);

        var player = new Player
        {
            Id = NewId(),
            UserId = Guard.NotEmpty(userId, nameof(userId)),
            DisplayName = name,
            NormalizedName = NameNormalizer.Normalize(name),
            Position = position,
            BirthDate = birthDate,
            RegisteredAt = now,
        };

        player.RaiseDomainEvent(new PlayerRegisteredEvent(player.Id, now));
        return player;
    }

    public void UpdateProfile(string displayName, Position position, DateOnly? birthDate)
    {
        var name = Guard.NotEmpty(displayName, "nombre", 100);

        if (!Enum.IsDefined(position))
            throw new DomainException(ErrorCode.PlayerInvalidPosition);

        DisplayName = name;
        NormalizedName = NameNormalizer.Normalize(name);
        Position = position;
        BirthDate = birthDate;
    }
}
