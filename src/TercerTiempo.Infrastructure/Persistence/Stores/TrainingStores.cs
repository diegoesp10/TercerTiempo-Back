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

    public void Add(TrainingSession session) => db.TrainingSessions.Add(session);

    public void Remove(TrainingSession session) => db.TrainingSessions.Remove(session);
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
