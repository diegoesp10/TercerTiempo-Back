using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Clubs;

public sealed class CoachAppointment : AggregateRoot
{
    public Guid ClubId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid SeasonId { get; private set; }
    public string CoachName { get; private set; } = string.Empty;
    public string NormalizedCoachName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public StaffRole Role { get; private set; }
    public string AccessCode { get; private set; } = string.Empty;
    public Guid? CoachId { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidUntil { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsRedeemed => CoachId is not null;
    public bool IsRevoked => RevokedAt is not null;

    private CoachAppointment() { }

    public static CoachAppointment Create(
        Guid clubId, Guid teamId, Guid seasonId, string coachName, string? email, StaffRole role,
        DateOnly validFrom, DateOnly validUntil, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException(ErrorCode.StaffInvalidRole);

        if (validUntil < validFrom)
            throw new DomainException(ErrorCode.AppointmentValidityOrder);

        var name = Guard.NotEmpty(coachName, "nombre del técnico", 100);

        return new CoachAppointment
        {
            Id = NewId(),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            CoachName = name,
            NormalizedCoachName = NameNormalizer.Normalize(name),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Role = role,
            AccessCode = AccessCodes.Generate(),
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            CreatedAt = now,
        };
    }

    public bool IsExpiredOn(DateOnly today) => today > ValidUntil;

    public bool IsActiveOn(DateOnly today) =>
        IsRedeemed && !IsRevoked && today >= ValidFrom && today <= ValidUntil;

    public AppointmentStatus StatusOn(DateOnly today) =>
        IsRevoked ? AppointmentStatus.Revoked
        : IsExpiredOn(today) ? AppointmentStatus.Expired
        : IsRedeemed ? AppointmentStatus.Active
        : AppointmentStatus.Pending;

    public void Redeem(Guid coachId, DateTimeOffset now)
    {
        if (IsRevoked)
            throw new DomainException(ErrorCode.AppointmentRevoked);

        if (IsRedeemed)
            throw new DomainException(ErrorCode.AppointmentAlreadyRedeemed);

        if (IsExpiredOn(Today(now)))
            throw new DomainException(ErrorCode.AppointmentExpired);

        CoachId = Guard.NotEmpty(coachId, nameof(coachId));
        RedeemedAt = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (IsRevoked)
            throw new DomainException(ErrorCode.AppointmentAlreadyRevoked);

        RevokedAt = now;
    }

    public void SetValidity(DateOnly validUntil)
    {
        if (IsRevoked)
            throw new DomainException(ErrorCode.AppointmentRevokedValidity);

        if (validUntil < ValidFrom)
            throw new DomainException(ErrorCode.AppointmentValidityOrder);

        ValidUntil = validUntil;
    }

    public void RegenerateAccessCode()
    {
        if (IsRedeemed)
            throw new DomainException(ErrorCode.AppointmentCodeRegenerate);

        AccessCode = AccessCodes.Generate();
    }

    internal static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);
}

public enum AppointmentStatus
{
    Pending = 1,
    Active = 2,
    Expired = 3,
    Revoked = 4,
}

public static class AccessCodes
{
    public const int Length = 10;

    public static string Generate() => InviteCodes.Generate(Length);

    public static string Normalize(string? code) => InviteCodes.Normalize(code);
}
