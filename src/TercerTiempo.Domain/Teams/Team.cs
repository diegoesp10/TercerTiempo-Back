using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Teams;

public sealed class Team : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string ShortName { get; private set; } = string.Empty;
    public ExternalRef? ExternalRef { get; private set; }

    /// <summary>Código del equipo en la federación (asturfutbol.es), cuando ya se ha localizado.</summary>
    public ExternalRef? OfficialRef { get; private set; }

    /// <summary>Ficha del club según la federación (sede, campo, equipación), cuando se ha descargado.</summary>
    public TeamProfile? Profile { get; private set; }

    /// <summary>Clave del escudo en nuestro almacenamiento ("{id}-{versión}.{ext}"); la API lo sirve por id. Nunca se guarda una URL de terceros.</summary>
    public string? CrestKey { get; private set; }

    /// <summary>Última vez que se consultó la ficha en la federación (aunque no hubiera cambios).</summary>
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
