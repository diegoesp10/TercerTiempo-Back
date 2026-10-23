using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Jobs;

// Apunta en ScrapeRequests cada llamada que sale hacia una web externa, con la ejecución que la ha hecho (si la hay).
// Si no se puede apuntar, la llamada sigue su curso.
internal sealed class ScrapeLogHandler(string provider, IServiceScopeFactory scopes, TimeProvider clock, ILogger<ScrapeLogHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();
        var started = clock.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            await LogAsync(request, at, started, (int)response.StatusCode, null);
            return response;
        }
        catch (Exception ex)
        {
            await LogAsync(request, at, started, null, $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private async Task LogAsync(HttpRequestMessage request, DateTimeOffset at, long started, int? status, string? error)
    {
        try
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(new ScrapeRequest
            {
                At = at,
                Provider = provider,
                Method = request.Method.Method,
                Url = url.Length > 2048 ? url[..2048] : url,
                StatusCode = status,
                DurationMs = (int)Math.Min(int.MaxValue, clock.GetElapsedTime(started).TotalMilliseconds),
                Error = error is { Length: > 500 } ? error[..500] : error,
                JobRunId = JobContext.RunId,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo apuntar la llamada a {Provider}", provider);
        }
    }
}

internal static class ScrapeLogging
{
    public static IHttpClientBuilder LogScrapes(this IHttpClientBuilder builder, string provider) =>
        builder.AddHttpMessageHandler(sp => new ScrapeLogHandler(
            provider,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ScrapeLogHandler>>()));
}
