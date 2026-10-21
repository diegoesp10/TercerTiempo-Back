using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Teams;

public sealed class Team : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string ShortName { get; private set; } = string.Empty;
    public string? CrestUrl { get; private set; }
    public ExternalRef? ExternalRef { get; private set; }

    /// <summary>Código del equipo en la federación (asturfutbol.es), cuando ya se ha localizado.</summary>
    public ExternalRef? OfficialRef { get; private set; }

    private Team() { }

    public static Team Create(string name, string shortName, string? crestUrl, ExternalRef? externalRef) =>
        new()
        {
            Id = NewId(),
            Name = Guard.NotEmpty(name, "nombre", 120),
            ShortName = Guard.NotEmpty(shortName, "nombre corto", 30),
            CrestUrl = crestUrl,
            ExternalRef = externalRef,
        };

    public void LinkOfficial(ExternalRef officialRef) => OfficialRef = officialRef;

    public void UpdateDetails(string name, string shortName, string? crestUrl)
    {
        Name = Guard.NotEmpty(name, "nombre", 120);
        ShortName = Guard.NotEmpty(shortName, "nombre corto", 30);
        CrestUrl = crestUrl;
    }
}
