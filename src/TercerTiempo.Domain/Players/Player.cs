using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Players;

/// <summary>
/// Jugador registrado en la plataforma. Está vinculado a un usuario de identidad (UserId)
/// y, opcionalmente, a su ficha en el proveedor externo (ExternalRef) cuando se verifica en la plantilla.
/// Solo puede pertenecer a una plantilla a la vez.
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

    /// <summary>Pertenencia vigente (pendiente o activa), si la hay.</summary>
    public SquadMembership? CurrentMembership => _memberships.FirstOrDefault(m => m.IsCurrent);

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
    /// Se une a la plantilla de un equipo en una temporada. La pertenencia nace activa si el jugador
    /// coincide con una ficha de la plantilla publicada o si el proveedor no publica plantilla para ese equipo
    /// (no hay nada contra lo que verificar); si hay plantilla y no aparece, queda pendiente de validación.
    /// </summary>
    public SquadMembership JoinSquad(
        Guid teamId, Guid seasonId, int? shirtNumber, bool verifiedInRoster, bool rosterAvailable, DateTimeOffset now)
    {
        if (CurrentMembership is not null)
            throw new DomainException("Ya perteneces a una plantilla. Abandónala antes de unirte a otra.");

        var status = verifiedInRoster || !rosterAvailable ? MembershipStatus.Active : MembershipStatus.Pending;

        var membership = SquadMembership.Create(Id, teamId, seasonId, shirtNumber, verifiedInRoster, status, now);
        _memberships.Add(membership);
        return membership;
    }

    public void LeaveSquad(DateTimeOffset now)
    {
        var membership = CurrentMembership
            ?? throw new DomainException("No perteneces a ninguna plantilla.");

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
