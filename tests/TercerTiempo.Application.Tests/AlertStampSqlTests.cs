using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Players;
using TercerTiempo.Infrastructure.Alerts;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

public class AlertStampSqlTests
{
    [LocalSqlFact]
    public async Task The_stamp_counts_the_bell_of_one_account_in_a_single_query()
    {
        // Base creada solo para esta prueba; nunca usa ni borra la base de desarrollo.
        var database = $"TercerTiempo_AlertStamp_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10").Options;
        await using var db = new AppDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var player = Player.Register(Guid.NewGuid(), "Pelayo", Position.Forward, null, now);
            var other = Player.Register(Guid.NewGuid(), "Xuan", Position.Defender, null, now);
            db.AddRange(player, other);
            await db.SaveChangesAsync();
            var store = new AlertStore(db);

            Assert.Equal(new AlertStamp(0, 0, 0, 0), await store.GetStampForAsync(null, player.Id, null));
            Assert.Equal(new AlertStamp(0, 0, 0, 0), await store.GetStampForAsync(null, null, null));

            Alert Raise(Guid playerId, string key) => Alert.ForPlayer(playerId, AlertKind.MatchCallUp, key, "Convocado", "Te han convocado.", null, null, now);
            var seen = Raise(player.Id, "visto");
            var read = Raise(player.Id, "leido");
            var resolved = Raise(player.Id, "resuelto");
            var fresh = Raise(player.Id, "nuevo");
            seen.MarkSeen(now);
            read.MarkRead(now);
            resolved.Resolve(now);
            foreach (var alert in new[] { seen, read, resolved, fresh, Raise(other.Id, "de-otro") }) store.Add(alert);
            await db.SaveChangesAsync();

            // Nuevos: solo el que no se ha visto ni resuelto. Sin leer: todos menos el leído. Vigentes: todos menos el resuelto.
            Assert.Equal(new AlertStamp(4, 1, 3, 3), await store.GetStampForAsync(null, player.Id, null));

            fresh.MarkSeen(now);
            await db.SaveChangesAsync();
            Assert.Equal(new AlertStamp(4, 0, 3, 3), await store.GetStampForAsync(null, player.Id, null));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
