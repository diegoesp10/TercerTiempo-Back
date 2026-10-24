using Microsoft.Extensions.Diagnostics.HealthChecks;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Api.Infrastructure;

// GET /api/health: Docker solo da el contenedor por sano si la API responde y llega a SQL Server.
internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Sin conexión con la base de datos");
}
