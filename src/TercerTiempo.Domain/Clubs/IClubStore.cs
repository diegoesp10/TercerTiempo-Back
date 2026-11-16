namespace TercerTiempo.Domain.Clubs;

public interface IClubStore
{
    Task<Club?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Club>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    // Los clubes que tienen vinculado alguno de esos equipos.
    Task<IReadOnlyList<Club>> GetByTeamsAsync(IEnumerable<Guid> teamIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Club>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Club?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Club?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRegisteredNamesAsync(CancellationToken cancellationToken = default);

    Task<Club?> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);

    void Add(Club club);
}

public interface ICoachAppointmentStore
{
    Task<CoachAppointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CoachAppointment?> GetByAccessCodeAsync(string accessCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachAppointment>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachAppointment>> GetByCoachAsync(Guid coachId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachAppointment>> GetByTeamAndSeasonAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachAppointment>> GetByTeamsAndSeasonsAsync(IEnumerable<Guid> teamIds, IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default);
    void Add(CoachAppointment appointment);
}
