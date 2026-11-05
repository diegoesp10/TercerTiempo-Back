using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class ExerciseStore(AppDbContext db) : IExerciseStore
{
    public Task<Exercise?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Exercises.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Exercise>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.Exercises.Where(e => e.SquadId == squadId).OrderBy(e => e.Type).ThenBy(e => e.Name).ToListAsync(cancellationToken);

    public void Add(Exercise exercise) => db.Exercises.Add(exercise);

    public async Task RemoveAsync(Exercise exercise, CancellationToken cancellationToken = default)
    {
        await db.Set<TrainingItem>().Where(i => i.ExerciseId == exercise.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.ExerciseId, (Guid?)null), cancellationToken);
        db.Exercises.Remove(exercise);
    }
}

internal sealed class TrainingStore(AppDbContext db) : ITrainingStore
{
    public Task<TrainingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TrainingSession>> GetBySquadAsync(Guid squadId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await db.TrainingSessions
            .Where(s => s.SquadId == squadId && s.StartsAt >= from && s.StartsAt < to)
            .OrderBy(s => s.StartsAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrainingSession>> GetUpcomingAsync(Guid squadId, DateTimeOffset from, int take, CancellationToken cancellationToken = default) =>
        await db.TrainingSessions
            .Where(s => s.SquadId == squadId && s.StartsAt >= from)
            .OrderBy(s => s.StartsAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrainingSession>> GetSeriesAsync(Guid seriesId, DateTimeOffset from, CancellationToken cancellationToken = default) =>
        await db.TrainingSessions
            .Where(s => s.SeriesId == seriesId && s.StartsAt >= from)
            .OrderBy(s => s.StartsAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrainingSession>> GetByTrialPlayerAsync(Guid trialPlayerId, CancellationToken cancellationToken = default) =>
        await db.TrainingSessions
            .Where(s => s.Attendance.Any(a => a.TrialPlayerId == trialPlayerId))
            .OrderBy(s => s.StartsAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, TrialAttendanceCounts>> CountTrialAttendanceAsync(Guid squadId, CancellationToken cancellationToken = default)
    {
        var rows = await db.TrainingSessions
            .Where(s => s.SquadId == squadId)
            .SelectMany(s => s.Attendance)
            .Where(a => a.TrialPlayerId != null)
            .GroupBy(a => a.TrialPlayerId!.Value)
            .Select(g => new { TrialPlayerId = g.Key, Sessions = g.Count(), Attended = g.Count(a => a.Status == AttendanceStatus.Attended) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.TrialPlayerId, r => new TrialAttendanceCounts(r.Sessions, r.Attended));
    }

    public void Add(TrainingSession session) => db.TrainingSessions.Add(session);

    public void Remove(TrainingSession session) => db.TrainingSessions.Remove(session);
}

internal sealed class TrialPlayerStore(AppDbContext db) : ITrialPlayerStore
{
    public Task<TrialPlayer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.TrialPlayers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TrialPlayer>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.TrialPlayers.Where(t => t.SquadId == squadId).OrderBy(t => t.PromotedAt != null).ThenBy(t => t.FullName).ToListAsync(cancellationToken);

    public void Add(TrialPlayer trialPlayer) => db.TrialPlayers.Add(trialPlayer);

    public void Remove(TrialPlayer trialPlayer) => db.TrialPlayers.Remove(trialPlayer);
}

internal sealed class CallUpStore(AppDbContext db) : ICallUpStore
{
    public Task<MatchCallUp?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.MatchCallUps.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<MatchCallUp?> GetByMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        db.MatchCallUps.FirstOrDefaultAsync(c => c.SquadId == squadId && c.MatchId == matchId, cancellationToken);

    public async Task<IReadOnlyList<MatchCallUp>> GetPublishedBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.MatchCallUps.Where(c => c.SquadId == squadId && c.Status == CallUpStatus.Published).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchCallUp>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.MatchCallUps.Where(c => c.SquadId == squadId).ToListAsync(cancellationToken);

    public void Add(MatchCallUp callUp) => db.MatchCallUps.Add(callUp);
}
