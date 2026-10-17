using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Squads;

/// <summary>Miembro del cuerpo técnico de una plantilla.</summary>
public sealed class SquadStaff : Entity
{
    public Guid SquadId { get; private set; }
    public Guid CoachId { get; private set; }
    public StaffRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    public bool IsActive => LeftAt is null;

    private SquadStaff() { }

    internal static SquadStaff Create(Guid squadId, Guid coachId, StaffRole role, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException("Rol del cuerpo técnico no válido.");

        return new SquadStaff
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            CoachId = Guard.NotEmpty(coachId, nameof(coachId)),
            Role = role,
            JoinedAt = now,
        };
    }

    internal void ChangeRole(StaffRole role)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException("Rol del cuerpo técnico no válido.");

        Role = role;
    }

    internal void Leave(DateTimeOffset now) => LeftAt = now;
}
