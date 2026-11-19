using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Stores;

namespace TercerTiempo.Application.Tests;

// Las consultas del recuento de entrenamientos tienen que traducirse a SQL (la hora de fin se calcula con DATEADD) y
// devolver solo lo pedido.
public class TrainingHistorySqlTests
{
    [LocalSqlFact]
    public async Task The_history_and_the_trial_counts_run_on_sql_server()
    {
        var database = $"TercerTiempo_TrainingHistory_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10").Options;
        await using var db = new AppDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            // Sin equipo ni temporada de verdad: aquí solo importan las sesiones y su lista.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [TrainingSessions] NOCHECK CONSTRAINT ALL; ALTER TABLE [TrainingAttendances] NOCHECK CONSTRAINT ALL; ALTER TABLE [TrialPlayers] NOCHECK CONSTRAINT ALL");

            var now = new DateTimeOffset(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);
            var squad = Squad.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-30));
            var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, now.AddDays(-30));
            var nel = squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, now.AddDays(-30));
            var editor = SquadEditor.Coach(squad.Staff.First().CoachId);
            var done = TrainingSession.Plan(squad, editor, TrainingKind.Season, null, now.AddDays(-2), 90, null, null, now.AddDays(-10));
            var running = TrainingSession.Plan(squad, editor, TrainingKind.Season, null, now.AddMinutes(-30), 90, null, null, now.AddDays(-10));
            var trial = TrialPlayer.Create(squad, editor, "Iyán Prieto", null, now.AddDays(-10));
            done.AddTrialPlayer(trial, now.AddDays(-10));
            running.AddTrialPlayer(trial, now.AddDays(-10));
            db.Add(trial);
            db.AddRange(done, running);
            await db.SaveChangesAsync();

            var store = new TrainingStore(db);
            var history = await store.GetHistoryAsync([squad.Id], now.AddDays(-7), now, [pelayo.Id]);
            var trials = await store.CountTrialAttendanceAsync(squad.Id, now);

            Assert.Equal(2, history.Slots.Count);
            Assert.All(history.Marks, m => Assert.Equal(pelayo.Id, m.SquadPlayerId));
            Assert.Equal(2, history.Marks.Count);
            Assert.DoesNotContain(history.Marks, m => m.SquadPlayerId == nel.Id);
            Assert.Equal(new TrialAttendanceCounts(2, 1), trials[trial.Id]);
            Assert.Empty((await store.GetHistoryAsync([squad.Id], now.AddDays(-30), now.AddDays(-20), null)).Slots);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
