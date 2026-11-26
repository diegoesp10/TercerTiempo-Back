namespace TercerTiempo.Domain.Squads;

public interface ISquadStore
{
    Task<Squad?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Squad?> GetByTeamAndSeasonAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default);
    Task<Squad?> GetByInviteCodeAsync(string inviteCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Squad>> GetByCoachAsync(Guid coachId, DateOnly today, CancellationToken cancellationToken = default);

    // Solo los datos de la plantilla, sin jugadores ni cuerpo técnico: para saber cuáles son y de qué equipo.
    Task<IReadOnlyList<SquadRef>> GetRefsByCoachAsync(Guid coachId, DateOnly today, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SquadRef>> GetRefsBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    // De esos equipos en esas temporadas; quien llama empareja equipo y temporada.
    Task<IReadOnlyList<SquadRef>> GetRefsAsync(IEnumerable<Guid> teamIds, IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Squad>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<Squad?> GetByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);

    // Todas las plantillas en las que ha estado el jugador (también las de temporadas pasadas o de las que se fue).
    Task<IReadOnlyList<Squad>> GetAllByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Squad>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Squad>> GetByTeamIdsAsync(IEnumerable<Guid> teamIds, Guid seasonId, CancellationToken cancellationToken = default);

    // Todas las temporadas de esos equipos.
    Task<IReadOnlyList<Squad>> GetByTeamIdsAsync(IEnumerable<Guid> teamIds, CancellationToken cancellationToken = default);

    // Las que tienen algún jugador en activo sin posición (la federación no la publica de todos).
    Task<IReadOnlyList<Squad>> GetWithPlayersMissingPositionAsync(CancellationToken cancellationToken = default);

    // Los nombres de los jugadores en activo de todas las plantillas, sin cargarlas: para buscar fichas repetidas.
    Task<IReadOnlyList<SquadPlayerName>> GetActivePlayerNamesAsync(CancellationToken cancellationToken = default);

    // Qué es esa cuenta en la plantilla (cuerpo técnico vigente, jugador en activo o el club del equipo) sin cargarla:
    // para la puerta de licencia y las huellas que se preguntan cada pocos segundos. Mismas reglas que Squad.IsStaff,
    // Squad.IsMember y Club.Manages; null si la plantilla no existe.
    Task<SquadAccessRow?> GetAccessAsync(
        Guid squadId, Guid? coachId, Guid? playerId, Guid? clubId, DateOnly today, CancellationToken cancellationToken = default);

    void Add(Squad squad);
}

public sealed record SquadRef(Guid Id, Guid TeamId, Guid SeasonId, SquadVisibility Visibility);

public sealed record SquadAccessRow(Guid Id, Guid TeamId, Guid SeasonId, bool IsStaff, bool IsMember, bool IsClub)
{
    public bool IsInsider => IsStaff || IsMember || IsClub;
    public bool IsManager => IsStaff || IsClub;
}

public sealed record SquadPlayerName(Guid SquadId, Guid SquadPlayerId, string NormalizedName);
