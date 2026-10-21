using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

/// <summary>
/// Plantilla de un equipo en una temporada, gestionada por su cuerpo técnico: quién forma el staff (nombrado por el
/// club y con validez limitada a la temporada), qué jugadores la componen (y cuáles tienen cuenta vinculada), el
/// código de invitación para los jugadores y la visibilidad de sus datos.
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

    public IEnumerable<SquadPlayer> ActivePlayers => _players.Where(p => p.IsActive);

    private Squad() { }

    /// <summary>
    /// Crea la plantilla con su primer técnico (nombrado por el club hasta <paramref name="validUntil"/>).
    /// Nace privada y con un código de invitación nuevo para los jugadores.
    /// </summary>
    public static Squad Create(Guid teamId, Guid seasonId, Guid headCoachId, DateTimeOffset now, StaffRole role = StaffRole.HeadCoach, DateOnly? validUntil = null, Guid? appointmentId = null)
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

        squad.AddStaff(headCoachId, role, now, validUntil, appointmentId);
        return squad;
    }

    // ---------------------------------------------------------------- Cuerpo técnico

    /// <summary>Miembros del staff vigentes en la fecha indicada (sin baja y sin caducar).</summary>
    public IEnumerable<SquadStaff> StaffOn(DateOnly today) => _staff.Where(s => s.IsActiveOn(today));

    /// <summary>
    /// Incorpora a un técnico. Si ya figuraba y su pertenencia había caducado, se renueva (nueva validez y
    /// nombramiento); si sigue vigente, es un error.
    /// </summary>
    public SquadStaff AddStaff(Guid coachId, StaffRole role, DateTimeOffset now, DateOnly? validUntil = null, Guid? appointmentId = null)
    {
        var today = Today(now);

        if (_staff.FirstOrDefault(s => s.CoachId == coachId && s.IsActive) is { } current)
        {
            if (current.IsActiveOn(today))
                throw new DomainException(ErrorCode.SquadAlreadyStaff);

            current.Renew(role, validUntil, appointmentId);
            return current;
        }

        var member = SquadStaff.Create(Id, coachId, role, now, validUntil, appointmentId);
        _staff.Add(member);
        return member;
    }

    public void RemoveStaff(Guid coachId, DateTimeOffset now)
    {
        var member = _staff.FirstOrDefault(s => s.CoachId == coachId && s.IsActive)
            ?? throw new DomainException(ErrorCode.SquadStaffNotMember);

        member.Leave(now);
    }

    /// <summary>Cambia la fecha de validez de un técnico (cuando el club acorta o prorroga su nombramiento).</summary>
    public void SetStaffValidity(Guid coachId, DateOnly? validUntil)
    {
        var member = _staff.FirstOrDefault(s => s.CoachId == coachId && s.IsActive)
            ?? throw new DomainException(ErrorCode.SquadStaffNotMember);

        member.SetValidity(validUntil);
    }

    public SquadStaff? StaffMemberOf(Guid coachId, DateOnly today) =>
        _staff.FirstOrDefault(s => s.CoachId == coachId && s.IsActiveOn(today));

    public bool IsStaff(Guid coachId, DateOnly today) => StaffMemberOf(coachId, today) is not null;

    public bool IsHeadCoach(Guid coachId, DateOnly today) => StaffMemberOf(coachId, today)?.Role == StaffRole.HeadCoach;

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
            throw new DomainException(ErrorCode.SquadPlayerDuplicate, fullName.Trim());

        var player = SquadPlayer.Create(Id, fullName, position, shirtNumber, rosterEntryId, now);
        _players.Add(player);
        return player;
    }

    public SquadPlayer GetPlayer(Guid squadPlayerId) =>
        _players.FirstOrDefault(p => p.Id == squadPlayerId)
            ?? throw new DomainException(ErrorCode.SquadPlayerNotMember);

    public void UpdatePlayer(Guid squadPlayerId, string fullName, Position? position, int? shirtNumber)
    {
        var player = GetPlayer(squadPlayerId);
        var normalized = NameNormalizer.Normalize(fullName);

        if (ActivePlayers.Any(p => p.Id != squadPlayerId && p.NormalizedName == normalized))
            throw new DomainException(ErrorCode.SquadPlayerDuplicate, fullName.Trim());

        player.Update(fullName, position, shirtNumber);
    }

    public void RemovePlayer(Guid squadPlayerId, DateTimeOffset now)
    {
        var player = GetPlayer(squadPlayerId);
        if (!player.IsActive)
            throw new DomainException(ErrorCode.SquadPlayerAlreadyLeft);

        player.Leave(now);
    }

    public SquadPlayer LinkPlayer(Guid squadPlayerId, Guid playerId)
    {
        var entry = GetPlayer(squadPlayerId);

        if (!entry.IsActive)
            throw new DomainException(ErrorCode.SquadEntryInactive);

        if (entry.PlayerId is { } current && current != playerId)
            throw new DomainException(ErrorCode.SquadEntryLinked);

        if (_players.Any(p => p.Id != squadPlayerId && p.IsActive && p.PlayerId == playerId))
            throw new DomainException(ErrorCode.SquadAlreadyLinkedHere);

        entry.Link(playerId);
        return entry;
    }

    public void UnlinkPlayer(Guid playerId)
    {
        var entry = PlayerEntryOf(playerId)
            ?? throw new DomainException(ErrorCode.SquadNotLinked);

        entry.Unlink();
    }

    public SquadPlayer? PlayerEntryOf(Guid playerId) => ActivePlayers.FirstOrDefault(p => p.PlayerId == playerId);

    public bool IsMember(Guid playerId) => PlayerEntryOf(playerId) is not null;

    // ---------------------------------------------------------------- Visibilidad e invitaciones

    public void SetVisibility(SquadVisibility visibility)
    {
        if (!Enum.IsDefined(visibility))
            throw new DomainException(ErrorCode.SquadInvalidVisibility);

        Visibility = visibility;
    }

    public void RotateInviteCode() => InviteCode = InviteCodes.Generate();

    /// <summary>Un técnico vigente de la plantilla o un jugador vinculado siempre pueden verla; el resto, solo si es pública.</summary>
    public bool CanBeViewedBy(Guid? coachId, Guid? playerId, DateOnly today) =>
        Visibility == SquadVisibility.Public
        || (coachId is { } c && IsStaff(c, today))
        || (playerId is { } p && IsMember(p));

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);
}
