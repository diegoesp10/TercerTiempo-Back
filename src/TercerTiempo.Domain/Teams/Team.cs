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

    // El escudo lo ha subido el propio club desde su cuenta (no descargado de otra web ni importado por el admin).
    public bool CrestFromClub { get; private set; }

    public DateTimeOffset? ProfileSyncedAt { get; private set; }

    private Team() { }

    // La ficha de la federación sustituye a la anterior, salvo las medidas del campo, que son del club.
    public void UpdateProfile(TeamProfile profile, DateTimeOffset now)
    {
        Profile = profile.WithPitch(Profile.PitchLengthM, Profile.PitchWidthM);
        ProfileSyncedAt = now;
    }

    public void SetPitch(decimal? lengthM, decimal? widthM) => Profile = Profile.WithPitch(lengthM, widthM);

    public void MarkProfileChecked(DateTimeOffset now) => ProfileSyncedAt = now;

    public void SetCrest(string crestKey, bool fromClub = false)
    {
        CrestKey = Guard.NotEmpty(crestKey, "escudo", 80);
        CrestFromClub = fromClub;
    }

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
