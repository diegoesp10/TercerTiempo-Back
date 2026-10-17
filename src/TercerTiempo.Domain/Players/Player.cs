using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Players;

/// <summary>
/// Jugador registrado en la plataforma. Está vinculado a un usuario de identidad (UserId)
/// y, opcionalmente, a su ficha en el proveedor externo (ExternalRef) cuando se verifica en la plantilla.
/// </summary>
public sealed class Player : AggregateRoot
{
    private readonly List<SquadMembership> _memberships = [];

    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public Position Position { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public ExternalRef? ExternalRef { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }

    public IReadOnlyCollection<SquadMembership> Memberships => _memberships.AsReadOnly();

    private Player() { }

    public static Player Register(Guid userId, string displayName, Position position, DateOnly? birthDate, DateTimeOffset now)
    {
        var name = Guard.NotEmpty(displayName, "nombre", 100);

        if (!Enum.IsDefined(position))
            throw new DomainException("Posición no válida.");

        if (birthDate is { } date && date > DateOnly.FromDateTime(now.UtcDateTime).AddYears(-14))
            throw new DomainException("El jugador debe tener al menos 14 años.");

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

    /// <summary>
    /// Se une a la plantilla de un equipo para una temporada. Si el nombre aparece en la plantilla
    /// del proveedor la pertenencia nace activa; si no, queda pendiente de validación.
    /// </summary>
    public SquadMembership JoinSquad(Guid teamId, Guid seasonId, int? shirtNumber, bool verifiedInRoster, DateTimeOffset now)
    {
        if (_memberships.Any(m => m.SeasonId == seasonId && m.IsCurrent))
            throw new DomainException("Ya perteneces a una plantilla en esta temporada. Abandónala antes de unirte a otra.");

        var membership = SquadMembership.Create(Id, teamId, seasonId, shirtNumber, verifiedInRoster, now);
        _memberships.Add(membership);
        return membership;
    }

    public void LeaveSquad(Guid seasonId, DateTimeOffset now)
    {
        var membership = MembershipFor(seasonId)
            ?? throw new DomainException("No perteneces a ninguna plantilla en esta temporada.");

        membership.Leave(now);
    }

    public SquadMembership? MembershipFor(Guid seasonId) =>
        _memberships.FirstOrDefault(m => m.SeasonId == seasonId && m.IsCurrent);

    public bool IsActiveIn(Guid teamId, Guid seasonId) =>
        _memberships.Any(m => m.TeamId == teamId && m.SeasonId == seasonId && m.IsActive);

    public void LinkExternalRef(ExternalRef externalRef) => ExternalRef = externalRef;

    public void UpdateProfile(string displayName, Position position, DateOnly? birthDate)
    {
        var name = Guard.NotEmpty(displayName, "nombre", 100);

        if (!Enum.IsDefined(position))
            throw new DomainException("Posición no válida.");

        DisplayName = name;
        NormalizedName = NameNormalizer.Normalize(name);
        Position = position;
        BirthDate = birthDate;
    }
}
