using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Clubs;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class ClubStore(AppDbContext db) : IClubStore
{
    public Task<Club?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Clubs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Club?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Clubs.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<Club?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default) =>
        db.Clubs.FirstOrDefaultAsync(c => c.NormalizedName == normalizedName, cancellationToken);

    public async Task<IReadOnlyList<string>> GetRegisteredNamesAsync(CancellationToken cancellationToken = default) =>
        await db.Clubs.Select(c => c.NormalizedName).ToListAsync(cancellationToken);

    public Task<Club?> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        db.Clubs.FirstOrDefaultAsync(c => c.Teams.Any(t => t.TeamId == teamId), cancellationToken);

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

    public void Add(CoachAppointment appointment) => db.CoachAppointments.Add(appointment);
}
