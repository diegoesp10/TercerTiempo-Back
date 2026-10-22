using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

public sealed class SquadStaff : Entity
{
    public Guid SquadId { get; private set; }
    public Guid CoachId { get; private set; }
    public StaffRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    public DateOnly? ValidUntil { get; private set; }

    public Guid? AppointmentId { get; private set; }

    public bool IsActive => LeftAt is null;

    public bool IsExpiredOn(DateOnly today) => ValidUntil is { } until && today > until;

    public bool IsActiveOn(DateOnly today) => IsActive && !IsExpiredOn(today);

    private SquadStaff() { }

    internal static SquadStaff Create(Guid squadId, Guid coachId, StaffRole role, DateTimeOffset now, DateOnly? validUntil, Guid? appointmentId)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException(ErrorCode.StaffInvalidRole);

        return new SquadStaff
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            CoachId = Guard.NotEmpty(coachId, nameof(coachId)),
            Role = role,
            JoinedAt = now,
            ValidUntil = validUntil,
            AppointmentId = appointmentId,
        };
    }

    internal void ChangeRole(StaffRole role)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException(ErrorCode.StaffInvalidRole);

        Role = role;
    }

    internal void Renew(StaffRole role, DateOnly? validUntil, Guid? appointmentId)
    {
        ChangeRole(role);
        ValidUntil = validUntil;
        AppointmentId = appointmentId;
        LeftAt = null;
    }

    internal void SetValidity(DateOnly? validUntil) => ValidUntil = validUntil;

    internal void Leave(DateTimeOffset now) => LeftAt = now;
}
