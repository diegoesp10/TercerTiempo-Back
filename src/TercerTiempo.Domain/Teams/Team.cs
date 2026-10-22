using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Teams;

public sealed class Team : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string ShortName { get; private set; } = string.Empty;
    public ExternalRef? ExternalRef { get; private set; }

    public ExternalRef? OfficialRef { get; private set; }

    public TeamProfile Profile { get; private set; } = new();

    public string? CrestKey { get; private set; }

    public DateTimeOffset? ProfileSyncedAt { get; private set; }

    private Team() { }

    public void UpdateProfile(TeamProfile profile, DateTimeOffset now)
    {
        Profile = profile;
        ProfileSyncedAt = now;
    }

    public void MarkProfileChecked(DateTimeOffset now) => ProfileSyncedAt = now;

    public void SetCrest(string crestKey) => CrestKey = Guard.NotEmpty(crestKey, "escudo", 80);

    public static Team Create(string name, string shortName, ExternalRef? externalRef) =>
        new()
        {
            Id = NewId(),
            Name = Guard.NotEmpty(name, "nombre", 120),
            ShortName = Guard.NotEmpty(shortName, "nombre corto", 30),
            ExternalRef = externalRef,
        };

    public void LinkOfficial(ExternalRef officialRef) => OfficialRef = officialRef;

    public void UpdateDetails(string name, string shortName)
    {
        Name = Guard.NotEmpty(name, "nombre", 120);
        ShortName = Guard.NotEmpty(shortName, "nombre corto", 30);
    }
}
