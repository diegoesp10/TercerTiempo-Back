using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Infrastructure.OfficialSheets.BeSoccer;

internal sealed class BeSoccerRequestGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

// Lee páginas de es.besoccer.com con la misma caché SQL y el mismo control de peticiones que la federación, pero con
// sus propios contadores y pausa. Cada página se guarda el tiempo que pide quien la lee.
internal sealed class BeSoccerClient(
    HttpClient http,
    IOptions<OfficialSheetOptions> options,
    ILogger<BeSoccerClient> logger,
    IAsturfutbolRequestStore store,
    BeSoccerRequestGate gate,
    TimeProvider clock)
{
    private readonly OfficialSheetOptions _options = options.Value;

    public string AbsoluteUrl(string path) => new Uri(http.BaseAddress!, path).ToString();

    public async Task<string?> GetHtmlAsync(string path, TimeSpan cacheFor, CancellationToken ct)
    {
        var uri = new Uri(http.BaseAddress!, path);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        var limits = _options.BeSoccerLimits;
        var settings = _options.BeSoccer;
        var lease = Guid.NewGuid();

        await gate.Semaphore.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = clock.GetUtcNow();
                var reservation = await store.ReserveAsync(key, uri.AbsoluteUri, lease, false, now, limits, ct);
                if (reservation.Cached is { } cached) return cached.Content is null ? null : Encoding.UTF8.GetString(cached.Content);
                if (reservation.RetryAt is { } retry)
                {
                    if (reservation.Spacing)
                    {
                        await Task.Delay(retry > now ? retry - now : TimeSpan.Zero, clock, ct);
                        continue;
                    }
                    throw new ProviderThrottledException(BeSoccerDirectory.Name, $"Descarga de BeSoccer aplazada hasta {retry:O}.", retry);
                }

                HttpResponseMessage response;
                try
                {
                    response = await http.GetAsync(uri, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    await store.FailAsync(BeSoccerDirectory.Name, key, null, clock.GetUtcNow().AddMinutes(Math.Max(1, settings.FailureRetryMinutes)), false, ct);
                    throw new HttpRequestException("No se pudo consultar BeSoccer; el intento queda aplazado en la base de datos.", ex);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;

                    // Las páginas que no existen (competición o temporada sin datos) redirigen a otra sección.
                    var landed = response.RequestMessage?.RequestUri;
                    if (response.StatusCode == HttpStatusCode.NotFound || landed is not null && landed.AbsolutePath.TrimEnd('/') != uri.AbsolutePath.TrimEnd('/'))
                    {
                        var absent = new FederationResponse(null, null);
                        await store.CompleteAsync(key, absent, clock.GetUtcNow() + cacheFor, ct);
                        return null;
                    }

                    if (status is 403 or 406 or 429)
                    {
                        var pausedUntil = clock.GetUtcNow().AddHours(Math.Max(1, settings.BlockedPauseHours));
                        await store.FailAsync(BeSoccerDirectory.Name, key, status, pausedUntil, true, ct);
                        logger.LogWarning("BeSoccer respondió {Status}; se pausan sus descargas hasta {Until}", status, pausedUntil);
                        throw new ProviderThrottledException(BeSoccerDirectory.Name, $"BeSoccer respondió {status}.", pausedUntil);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        await store.FailAsync(BeSoccerDirectory.Name, key, status, clock.GetUtcNow().AddMinutes(Math.Max(1, settings.FailureRetryMinutes)), false, ct);
                        throw new HttpRequestException($"BeSoccer respondió {status}; el intento queda aplazado.", null, response.StatusCode);
                    }

                    var content = await response.Content.ReadAsByteArrayAsync(ct);
                    if (content.Length == 0)
                    {
                        await store.FailAsync(BeSoccerDirectory.Name, key, status, clock.GetUtcNow().AddMinutes(Math.Max(1, settings.FailureRetryMinutes)), false, ct);
                        throw new HttpRequestException("BeSoccer devolvió una página vacía; el intento queda aplazado.");
                    }

                    await store.CompleteAsync(key, new FederationResponse(content, response.Content.Headers.ContentType?.ToString()), clock.GetUtcNow() + cacheFor, ct);
                    return Encoding.UTF8.GetString(content);
                }
            }
        }
        finally { gate.Semaphore.Release(); }
    }
}
