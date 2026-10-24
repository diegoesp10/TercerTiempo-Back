using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Clubs;

// Invitación de un solo uso para dar de alta un club mientras la beta está cerrada. La genera el administrador.
public sealed class ClubInvitation : AggregateRoot
{
    public const int CodeLength = 10;

    public string Code { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public Guid? ClubId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private ClubInvitation() { }

    public static ClubInvitation Create(string? note, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        new()
        {
            Id = NewId(),
            Code = InviteCodes.Generate(CodeLength),
            Note = string.IsNullOrWhiteSpace(note) ? null : Guard.NotEmpty(note, "nota", 200),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public void Redeem(Guid clubId, DateTimeOffset now)
    {
        if (!IsUsable(now)) throw new DomainException(ErrorCode.AuthClubInviteInvalid);
        UsedAt = now;
        ClubId = clubId;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (UsedAt is not null) throw new DomainException(ErrorCode.ClubInvitationAlreadyUsed);
        RevokedAt ??= now;
    }
}

public interface IClubInvitationStore
{
    Task<ClubInvitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClubInvitation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubInvitation>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(ClubInvitation invitation);
}
