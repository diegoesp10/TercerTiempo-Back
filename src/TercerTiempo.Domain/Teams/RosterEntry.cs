using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Teams;

public sealed class RosterEntry : AggregateRoot
{
    public Guid TeamId { get; private set; }
    public Guid SeasonId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public Position? Position { get; private set; }
    public int? ShirtNumber { get; private set; }
    public ExternalRef? ExternalRef { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool ManagedByClub { get; private set; }

    public bool Hidden { get; private set; }

    public string? PhotoKey { get; private set; }

    private RosterEntry() { }

    public static RosterEntry CreateByClub(Guid teamId, Guid seasonId, string fullName, Position? position, int? shirtNumber, DateTimeOffset now)
    {
        var entry = Create(teamId, seasonId, fullName, position, shirtNumber, null, now);
        entry.ManagedByClub = true;
        return entry;
    }

    public void UpdateByClub(string fullName, Position? position, int? shirtNumber, DateTimeOffset now)
    {
        Update(fullName, position, shirtNumber, now);
        ManagedByClub = true;
        Hidden = false;
    }

    public void Hide() => Hidden = true;

    public void SetPhoto(string photoKey) => PhotoKey = Guard.NotEmpty(photoKey, nameof(photoKey), 100);

    public static RosterEntry Create(
        Guid teamId, Guid seasonId, string fullName, Position? position, int? shirtNumber, ExternalRef? externalRef, DateTimeOffset now)
    {
        var name = Guard.NotEmpty(fullName, "nombre", 150);

        return new RosterEntry
        {
            Id = NewId(),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            FullName = name,
            NormalizedName = NameNormalizer.Normalize(name),
            Position = position,
            ShirtNumber = shirtNumber,
            ExternalRef = externalRef,
            UpdatedAt = now,
        };
    }

    public void Update(string fullName, Position? position, int? shirtNumber, DateTimeOffset now)
    {
        if (shirtNumber is < 1 or > 99)
            throw new DomainException(ErrorCode.PlayerShirtNumberRange);

        var name = Guard.NotEmpty(fullName, "nombre", 150);
        FullName = name;
        NormalizedName = NameNormalizer.Normalize(name);
        Position = position;
        ShirtNumber = shirtNumber;
        UpdatedAt = now;
    }

    public bool Matches(string name) => NormalizedName == NameNormalizer.Normalize(name);
}
