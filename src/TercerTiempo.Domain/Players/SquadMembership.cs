using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Players;

/// <summary>Pertenencia de un jugador a la plantilla de un equipo durante una temporada.</summary>
public sealed class SquadMembership : Entity
{
    public Guid PlayerId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid SeasonId { get; private set; }
    public int? ShirtNumber { get; private set; }
    public MembershipStatus Status { get; private set; }

    /// <summary>true si el jugador coincide con una ficha de la plantilla publicada por el proveedor.</summary>
    public bool VerifiedInRoster { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    private SquadMembership() { }

    internal static SquadMembership Create(
        Guid playerId, Guid teamId, Guid seasonId, int? shirtNumber, bool verifiedInRoster, MembershipStatus status, DateTimeOffset now)
    {
        if (shirtNumber is < 1 or > 99)
            throw new DomainException("El dorsal debe estar entre 1 y 99.");

        if (status == MembershipStatus.Inactive)
            throw new DomainException("Una pertenencia no puede crearse ya finalizada.");

        return new SquadMembership
        {
            Id = NewId(),
            PlayerId = Guard.NotEmpty(playerId, nameof(playerId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            ShirtNumber = shirtNumber,
            VerifiedInRoster = verifiedInRoster,
            Status = status,
            JoinedAt = now,
        };
    }

    public bool IsActive => Status == MembershipStatus.Active;

    /// <summary>Pendiente o activa: sigue vinculado al equipo.</summary>
    public bool IsCurrent => Status != MembershipStatus.Inactive;

    public void Approve()
    {
        if (Status == MembershipStatus.Inactive)
            throw new DomainException("No se puede aprobar una pertenencia que ya ha finalizado.");

        Status = MembershipStatus.Active;
    }

    public void Leave(DateTimeOffset now)
    {
        Status = MembershipStatus.Inactive;
        LeftAt = now;
    }

    public void ChangeShirtNumber(int? shirtNumber)
    {
        if (shirtNumber is < 1 or > 99)
            throw new DomainException("El dorsal debe estar entre 1 y 99.");

        ShirtNumber = shirtNumber;
    }
}
