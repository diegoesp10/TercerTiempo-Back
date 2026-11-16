using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Clubs;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class ClubStore(AppDbContext db) : IClubStore
{
    // El club de la cuenta se mira muchas veces en una misma petición (cada comprobación de acceso a una plantilla, cada
    // equipo de una ficha): en las lecturas sin seguimiento se guarda lo leído durante el ámbito; con seguimiento, el que ya
    // tiene el contexto (con lo que la petición haya cambiado) sirve sin volver a la base.
    private readonly Dictionary<Guid, Club?> _readById = [];
    private readonly Dictionary<Guid, Club?> _readByTeam = [];

    private bool Reading => db.ChangeTracker.QueryTrackingBehavior != QueryTrackingBehavior.TrackAll;

    public async Task<Club?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (db.Clubs.Local.FindEntry(id) is { State: not EntityState.Detached } tracked) return tracked.Entity;
        if (!Reading) return await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (_readById.TryGetValue(id, out var cached)) return cached;
        return _readById[id] = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Club>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Clubs.Where(c => list.Contains(c.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Club>> GetByTeamsAsync(IEnumerable<Guid> teamIds, CancellationToken cancellationToken = default)
    {
        var list = teamIds.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Clubs.Where(c => c.Teams.Any(t => list.Contains(t.TeamId))).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Club>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Clubs.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<Club?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Clubs.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<Club?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default) =>
        db.Clubs.FirstOrDefaultAsync(c => c.NormalizedName == normalizedName, cancellationToken);

    public async Task<IReadOnlyList<string>> GetRegisteredNamesAsync(CancellationToken cancellationToken = default) =>
        await db.Clubs.Select(c => c.NormalizedName).ToListAsync(cancellationToken);

    public async Task<Club?> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        if (!Reading) return await db.Clubs.FirstOrDefaultAsync(c => c.Teams.Any(t => t.TeamId == teamId), cancellationToken);
        if (_readByTeam.TryGetValue(teamId, out var cached)) return cached;
        var club = await db.Clubs.FirstOrDefaultAsync(c => c.Teams.Any(t => t.TeamId == teamId), cancellationToken);
        if (club is not null) _readById.TryAdd(club.Id, club);
        return _readByTeam[teamId] = club;
    }

    public void Add(Club club) => db.Clubs.Add(club);
}

internal sealed class CoachAppointmentStore(AppDbContext db) : ICoachAppointmentStore
{
    public Task<CoachAppointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.CoachAppointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<CoachAppointment?> GetByAccessCodeAsync(string accessCode, CancellationToken cancellationToken = default) =>
        db.CoachAppointments.FirstOrDefaultAsync(a => a.AccessCode == accessCode, cancellationToken);

    public async Task<IReadOnlyList<CoachAppointment>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.CoachAppointments.Where(a => a.ClubId == clubId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CoachAppointment>> GetByCoachAsync(Guid coachId, CancellationToken cancellationToken = default) =>
        await db.CoachAppointments.Where(a => a.CoachId == coachId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CoachAppointment>> GetByTeamAndSeasonAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.CoachAppointments.Where(a => a.TeamId == teamId && a.SeasonId == seasonId).ToListAsync(cancellationToken);

    // De esos equipos en esas temporadas; quien llama empareja equipo y temporada.
    public async Task<IReadOnlyList<CoachAppointment>> GetByTeamsAndSeasonsAsync(IEnumerable<Guid> teamIds, IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default)
    {
        var teams = teamIds.Distinct().ToList();
        var seasons = seasonIds.Distinct().ToList();
        if (teams.Count == 0 || seasons.Count == 0) return [];
        return await db.CoachAppointments.Where(a => teams.Contains(a.TeamId) && seasons.Contains(a.SeasonId)).ToListAsync(cancellationToken);
    }

    public void Add(CoachAppointment appointment) => db.CoachAppointments.Add(appointment);
}

internal sealed class ClubInvitationStore(AppDbContext db) : IClubInvitationStore
{
    public Task<ClubInvitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ClubInvitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<ClubInvitation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.ClubInvitations.FirstOrDefaultAsync(i => i.Code == code, cancellationToken);

    public async Task<IReadOnlyList<ClubInvitation>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.ClubInvitations.OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);

    public void Add(ClubInvitation invitation) => db.ClubInvitations.Add(invitation);
}
