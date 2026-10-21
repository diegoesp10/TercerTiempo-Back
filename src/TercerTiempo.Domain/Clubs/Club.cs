using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Clubs;

/// <summary>
/// Club con cuenta en la plataforma. Es quien da de alta a los entrenadores de sus equipos: por cada equipo y temporada
/// emite un nombramiento (<see cref="CoachAppointment"/>) con un código de acceso, y es el único que puede renovarlo o
/// revocarlo. Solo puede haber una cuenta por club (nombre normalizado único): al darse de alta elige su club entre los
/// que publica el proveedor de datos y quedan vinculados todos sus equipos (primero, filiales...).
/// </summary>
public sealed class Club : AggregateRoot
{
    private readonly List<ClubTeam> _teams = [];

    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; private set; }

    public IReadOnlyCollection<ClubTeam> Teams => _teams.AsReadOnly();

    private Club() { }

    public static Club Register(Guid userId, string name, DateTimeOffset now)
    {
        var trimmed = Guard.NotEmpty(name, "nombre", 120);

        return new Club
        {
            Id = NewId(),
            UserId = Guard.NotEmpty(userId, nameof(userId)),
            Name = trimmed,
            NormalizedName = NameNormalizer.Normalize(trimmed),
            RegisteredAt = now,
        };
    }

    public void UpdateProfile(string name)
    {
        var trimmed = Guard.NotEmpty(name, "nombre", 120);
        Name = trimmed;
        NormalizedName = NameNormalizer.Normalize(trimmed);
    }

    public bool Manages(Guid teamId) => _teams.Any(t => t.TeamId == teamId);

    public ClubTeam AddTeam(Guid teamId, DateTimeOffset now)
    {
        if (Manages(teamId))
            throw new DomainException("Ese equipo ya forma parte del club.");

        var team = ClubTeam.Create(Id, teamId, now);
        _teams.Add(team);
        return team;
    }

    public void RemoveTeam(Guid teamId)
    {
        var team = _teams.FirstOrDefault(t => t.TeamId == teamId)
            ?? throw new DomainException("Ese equipo no forma parte del club.");

        _teams.Remove(team);
    }
}

/// <summary>Equipo (del proveedor de datos) que gestiona un club.</summary>
public sealed class ClubTeam : Entity
{
    public Guid ClubId { get; private set; }
    public Guid TeamId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private ClubTeam() { }

    internal static ClubTeam Create(Guid clubId, Guid teamId, DateTimeOffset now) =>
        new()
        {
            Id = NewId(),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            AddedAt = now,
        };
}
