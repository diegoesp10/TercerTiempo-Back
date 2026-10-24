using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

public sealed class SquadPlayer : Entity
{
    public Guid SquadId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public Position? Position { get; private set; }
    public int? ShirtNumber { get; private set; }
    public Guid? RosterEntryId { get; private set; }
    public Guid? PlayerId { get; private set; }
    public string? InviteCode { get; private set; }
    public SquadPlayerStatus Status { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    public bool IsActive => Status == SquadPlayerStatus.Active;
    public bool IsLinked => PlayerId is not null;

    private SquadPlayer() { }

    internal static SquadPlayer Create(Guid squadId, string fullName, Position? position, int? shirtNumber, Guid? rosterEntryId, DateTimeOffset now)
    {
        var player = new SquadPlayer
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            RosterEntryId = rosterEntryId,
            Status = SquadPlayerStatus.Active,
            AddedAt = now,
        };
        player.Update(fullName, position, shirtNumber);
        return player;
    }

    internal void Update(string fullName, Position? position, int? shirtNumber)
    {
        if (position is { } p && !Enum.IsDefined(p))
            throw new DomainException(ErrorCode.PlayerInvalidPosition);

        if (shirtNumber is < 1 or > 99)
            throw new DomainException(ErrorCode.PlayerShirtNumberRange);

        var name = Guard.NotEmpty(fullName, "nombre", 150);
        if (NormalizedName != NameNormalizer.Normalize(name)) InviteCode = null;
        FullName = name;
        NormalizedName = NameNormalizer.Normalize(name);
        Position = position;
        ShirtNumber = shirtNumber;
    }

    internal void GenerateInviteCode()
    {
        if (!IsActive) throw new DomainException(ErrorCode.SquadEntryInactive);
        if (IsLinked) throw new DomainException(ErrorCode.SquadEntryLinked);
        InviteCode = InviteCodes.Generate(12);
    }

    internal void Link(Guid playerId)
    {
        PlayerId = Guard.NotEmpty(playerId, nameof(playerId));
        InviteCode = null;
    }

    internal void Unlink() => PlayerId = null;

    internal void Leave(DateTimeOffset now)
    {
        Status = SquadPlayerStatus.Left;
        InviteCode = null;
        LeftAt = now;
    }

    internal void Reactivate()
    {
        Status = SquadPlayerStatus.Active;
        LeftAt = null;
    }

    public bool Matches(string name) => NormalizedName == NameNormalizer.Normalize(name);
}
