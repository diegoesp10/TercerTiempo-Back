using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Squads;

/// <summary>
/// Plantilla de un equipo en una temporada, gestionada por su cuerpo técnico: quién forma el staff, qué jugadores
/// la componen (y cuáles tienen cuenta vinculada), el código de invitación y la visibilidad de sus datos.
/// Es el límite de privacidad: las actas y estadísticas pertenecen a la plantilla.
/// </summary>
public sealed class Squad : AggregateRoot
{
    private readonly List<SquadStaff> _staff = [];
    private readonly List<SquadPlayer> _players = [];

    public Guid TeamId { get; private set; }
    public Guid SeasonId { get; private set; }
    public SquadVisibility Visibility { get; private set; }
    public string InviteCode { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<SquadStaff> Staff => _staff.AsReadOnly();
    public IReadOnlyCollection<SquadPlayer> Players => _players.AsReadOnly();

    public IEnumerable<SquadStaff> ActiveStaff => _staff.Where(s => s.IsActive);
    public IEnumerable<SquadPlayer> ActivePlayers => _players.Where(p => p.IsActive);

    private Squad() { }

    /// <summary>Crea la plantilla con su primer entrenador. Nace privada y con un código de invitación nuevo.</summary>
    public static Squad Create(Guid teamId, Guid seasonId, Guid headCoachId, DateTimeOffset now)
    {
        var squad = new Squad
        {
            Id = NewId(),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            Visibility = SquadVisibility.Private,
            InviteCode = InviteCodes.Generate(),
            CreatedAt = now,
        };

        squad.AddStaff(headCoachId, StaffRole.HeadCoach, now);
        return squad;
    }

    // ---------------------------------------------------------------- Cuerpo técnico

    public SquadStaff AddStaff(Guid coachId, StaffRole role, DateTimeOffset now)
    {
        if (IsStaff(coachId))
            throw new DomainException("Ya formas parte del cuerpo técnico de esta plantilla.");

        var member = SquadStaff.Create(Id, coachId, role, now);
        _staff.Add(member);
        return member;
    }

    public void RemoveStaff(Guid coachId, DateTimeOffset now)
    {
        var member = _staff.FirstOrDefault(s => s.CoachId == coachId && s.IsActive)
            ?? throw new DomainException("Ese técnico no forma parte de la plantilla.");

        if (member.Role == StaffRole.HeadCoach && ActiveStaff.Count(s => s.Role == StaffRole.HeadCoach) == 1)
            throw new DomainException("La plantilla debe conservar al menos un primer entrenador.");

        member.Leave(now);
    }

    public bool IsStaff(Guid coachId) => _staff.Any(s => s.CoachId == coachId && s.IsActive);

    public bool IsHeadCoach(Guid coachId) => _staff.Any(s => s.CoachId == coachId && s.IsActive && s.Role == StaffRole.HeadCoach);

    // ---------------------------------------------------------------- Jugadores

    public SquadPlayer AddPlayer(string fullName, Position? position, int? shirtNumber, Guid? rosterEntryId, DateTimeOffset now)
    {
        var normalized = NameNormalizer.Normalize(fullName);

        if (rosterEntryId is { } rosterId && _players.FirstOrDefault(p => p.RosterEntryId == rosterId) is { } fromRoster)
        {
            // Ya se importó esta ficha: si había causado baja, se reactiva.
            if (!fromRoster.IsActive) fromRoster.Reactivate();
            fromRoster.Update(fullName, position ?? fromRoster.Position, shirtNumber ?? fromRoster.ShirtNumber);
            return fromRoster;
        }

        if (ActivePlayers.Any(p => p.NormalizedName == normalized))
            throw new DomainException($"'{fullName.Trim()}' ya está en la plantilla.");

        var player = SquadPlayer.Create(Id, fullName, position, shirtNumber, rosterEntryId, now);
        _players.Add(player);
        return player;
    }

    public SquadPlayer GetPlayer(Guid squadPlayerId) =>
        _players.FirstOrDefault(p => p.Id == squadPlayerId)
            ?? throw new DomainException("El jugador no pertenece a esta plantilla.");

    public void UpdatePlayer(Guid squadPlayerId, string fullName, Position? position, int? shirtNumber)
    {
        var player = GetPlayer(squadPlayerId);
        var normalized = NameNormalizer.Normalize(fullName);

        if (ActivePlayers.Any(p => p.Id != squadPlayerId && p.NormalizedName == normalized))
            throw new DomainException($"'{fullName.Trim()}' ya está en la plantilla.");

        player.Update(fullName, position, shirtNumber);
    }

    public void RemovePlayer(Guid squadPlayerId, DateTimeOffset now)
    {
        var player = GetPlayer(squadPlayerId);
        if (!player.IsActive)
            throw new DomainException("El jugador ya había causado baja.");

        player.Leave(now);
    }

    public SquadPlayer LinkPlayer(Guid squadPlayerId, Guid playerId)
    {
        var entry = GetPlayer(squadPlayerId);

        if (!entry.IsActive)
            throw new DomainException("Esa ficha ya no está activa en la plantilla.");

        if (entry.PlayerId is { } current && current != playerId)
            throw new DomainException("Esa ficha ya está vinculada a otro jugador registrado.");

        if (_players.Any(p => p.Id != squadPlayerId && p.IsActive && p.PlayerId == playerId))
            throw new DomainException("Ya estás vinculado a otra ficha de esta plantilla.");

        entry.Link(playerId);
        return entry;
    }

    public void UnlinkPlayer(Guid playerId)
    {
        var entry = PlayerEntryOf(playerId)
            ?? throw new DomainException("No estás vinculado a esta plantilla.");

        entry.Unlink();
    }

    public SquadPlayer? PlayerEntryOf(Guid playerId) => ActivePlayers.FirstOrDefault(p => p.PlayerId == playerId);

    public bool IsMember(Guid playerId) => PlayerEntryOf(playerId) is not null;

    // ---------------------------------------------------------------- Visibilidad e invitaciones

    public void SetVisibility(SquadVisibility visibility)
    {
        if (!Enum.IsDefined(visibility))
            throw new DomainException("Visibilidad no válida.");

        Visibility = visibility;
    }

    public void RotateInviteCode() => InviteCode = InviteCodes.Generate();

    /// <summary>Un técnico de la plantilla o un jugador vinculado siempre pueden verla; el resto, solo si es pública.</summary>
    public bool CanBeViewedBy(Guid? coachId, Guid? playerId) =>
        Visibility == SquadVisibility.Public
        || (coachId is { } c && IsStaff(c))
        || (playerId is { } p && IsMember(p));
}
