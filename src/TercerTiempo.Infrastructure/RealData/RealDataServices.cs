using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.RealData;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Teams;

namespace TercerTiempo.Infrastructure.RealData;

internal sealed class RealDataGateway(IServiceScopeFactory scopes, JobScheduler scheduler) : IRealData
{
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = scopes.CreateRealDataScope();
        using var ambient = RealDataAmbient.Enter();
        return await work(scope.ServiceProvider);
    }

    public void RequestCopy() => scheduler.Launch(JobNames.RealDataCopy);
}

// Escudos que se descargan de las webs: en la base de datos reales, para que cada entorno los copie a sus ficheros. Se
// guardan con el resto de cambios del ámbito (el SaveChanges de quien lo llama).
internal sealed class RealDataCrestStorage(AppDbContext db, TimeProvider clock) : ICrestStorage
{
    public async Task<string> SaveAsync(Guid teamId, CrestImage image, CancellationToken cancellationToken)
    {
        var key = FileSystemImageStore.NewKey(teamId, image.ContentType);
        db.RemoveRange(await db.Set<TeamCrestImage>().Where(i => i.TeamId == teamId).ToListAsync(cancellationToken));
        db.Add(new TeamCrestImage { Key = key, TeamId = teamId, ContentType = image.ContentType, Content = image.Content, SavedAt = clock.GetUtcNow() });
        return key;
    }

    public async Task<CrestFile?> OpenReadAsync(string crestKey, CancellationToken cancellationToken) =>
        await db.Set<TeamCrestImage>().AsNoTracking().FirstOrDefaultAsync(i => i.Key == crestKey, cancellationToken) is { } image
            ? new CrestFile(new MemoryStream(image.Content, writable: false), image.ContentType)
            : null;
}

// Trae a la base de este entorno lo nuevo de la base de datos reales: al arrancar, cada RealData:CopyMinutes y en cuanto
// termina una descarga en este proceso. No hace ninguna petición a las webs.
internal sealed class RealDataCopyWorker(
    JobScheduler scheduler,
    RealDataCopier copier,
    TimeProvider clock,
    ILogger<RealDataCopyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.RealDataCopy);
        if (!job.Enabled) return;
        try
        {
            var due = await scheduler.FirstDueAsync(job.Name, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var manual = await scheduler.WaitAsync(job.Name, due, stoppingToken);
                try
                {
                    await scheduler.RunAsync(job.Name, manual, copier.CopyAsync, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error copiando la base de datos reales a la de este entorno");
                }

                due = job.Interval is { } interval ? clock.GetUtcNow() + interval : null;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado ordenado.
        }
    }
}
