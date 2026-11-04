using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

public class ConcurrentDeleteSqlTests
{
    // Dos peticiones que borran lo mismo (dos cancelaciones de una subida de vídeo, por ejemplo): la segunda no puede
    // acabar en un 500 por «se esperaba afectar a 1 fila y afectó a 0».
    [LocalSqlFact]
    public async Task Deleting_what_another_request_already_deleted_is_not_an_error()
    {
        var database = $"TercerTiempo_DeleteTwice_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10").Options;
        await using var setup = new AppDbContext(options);
        try
        {
            await setup.Database.MigrateAsync();
            var coach = Coach.Register(Guid.NewGuid(), "Entrenador de prueba", DateTimeOffset.UtcNow);
            setup.Add(coach);
            await setup.SaveChangesAsync();

            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(options);
            first.Remove(await first.Coaches.SingleAsync(c => c.Id == coach.Id));
            second.Remove(await second.Coaches.SingleAsync(c => c.Id == coach.Id));
            await first.SaveChangesAsync();
            Assert.Equal(0, await second.SaveChangesAsync());

            await using var check = new AppDbContext(options);
            Assert.False(await check.Coaches.AnyAsync(c => c.Id == coach.Id));
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
