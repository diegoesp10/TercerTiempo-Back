using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Teams;

/// <summary>
/// Jugador de la plantilla según el proveedor externo (scrapeado).
/// Sirve para verificar que un usuario registrado pertenece realmente al equipo.
/// </summary>
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

    private RosterEntry() { }

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
        var name = Guard.NotEmpty(fullName, "nombre", 150);
        FullName = name;
        NormalizedName = NameNormalizer.Normalize(name);
        Position = position;
        ShirtNumber = shirtNumber;
        UpdatedAt = now;
    }

    public bool Matches(string name) => NormalizedName == NameNormalizer.Normalize(name);
}
