using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

internal sealed class AsturfutbolClient(
    HttpClient http,
    IOptions<OfficialSheetOptions> options,
    ILogger<AsturfutbolClient> logger,
    IAsturfutbolRequestStore store,
    AsturfutbolRequestGate gate,
    TimeProvider clock)
{
    private readonly OfficialSheetOptions _options = options.Value;

    public async Task<string?> GetHtmlAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var response = await FetchAsync(pathAndQuery, html: true, cancellationToken);
        return response.Content is null ? null : Decode(response.Content, response.ContentType);
    }

    public async Task<(byte[] Content, string ContentType)?> GetBytesAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var response = await FetchAsync(pathAndQuery, html: false, cancellationToken);
        return response.Content is null ? null : (response.Content, response.ContentType?.Split(';')[0] ?? "application/octet-stream");
    }

    public string AbsoluteUrl(string pathAndQuery) => new Uri(http.BaseAddress!, pathAndQuery).ToString();

    private async Task<FederationResponse> FetchAsync(string pathAndQuery, bool html, CancellationToken ct)
    {
        var original = new Uri(http.BaseAddress!, pathAndQuery);
        EnsureHost(original);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original.AbsoluteUri)));
        var lease = Guid.NewGuid();
        var uri = original;
        var redirect = false;
        var redirects = 0;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = clock.GetUtcNow();
                var reservation = await store.ReserveAsync(key, original.AbsoluteUri, lease, redirect, now, _options, ct);
                if (reservation.Cached is { } cached) return cached;
                if (reservation.RetryAt is { } retry)
                {
                    if (reservation.Spacing)
                    {
                        await Task.Delay(retry > now ? retry - now : TimeSpan.Zero, clock, ct);
                        continue;
                    }
                    throw new ProviderThrottledException(AsturfutbolOfficialSheetProvider.Name,
                        $"Descarga de la federación aplazada hasta {retry:O}; se conservan los datos guardados.", retry);
                }

                HttpResponseMessage response;
                try
                {
                    response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    var retryAt = clock.GetUtcNow().AddMinutes(Math.Max(1, _options.Asturfutbol.FailureRetryMinutes));
                    await store.FailAsync(key, null, retryAt, false, ct);
                    throw new HttpRequestException("No se pudo consultar la federación; el intento queda aplazado en la base de datos.", ex);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    if (status is 301 or 302 or 303 or 307 or 308)
                    {
                        if (response.Headers.Location is not { } location || ++redirects > 5)
                        {
                            await store.FailAsync(key, status, now.AddHours(1), false, ct);
                            throw new HttpRequestException("Redirección de la federación no válida.");
                        }
                        uri = new Uri(uri, location);
                        EnsureHost(uri);
                        redirect = true;
                        // Cada salto consume su propio turno y presupuesto, incluidas las redirecciones de sesión.
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        var absent = new FederationResponse(null, null);
                        await store.CompleteAsync(key, absent, clock.GetUtcNow().AddHours(Math.Max(1, _options.Asturfutbol.CacheHours)), ct);
                        return absent;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var blocked = status is 403 or 429;
                        var pauseProvider = blocked || status >= 500 || response.Headers.RetryAfter is not null;
                        var retryAt = RetryAt(response, blocked);
                        await store.FailAsync(key, status, retryAt, pauseProvider, ct);
                        logger.LogWarning("La federación respondió {Status}; sin reintentos inmediatos, próximo intento a partir de {RetryAt}", status, retryAt);
                        if (pauseProvider) throw new ProviderThrottledException(AsturfutbolOfficialSheetProvider.Name,
                            $"La federación respondió {status}; todas las descargas quedan en pausa hasta {retryAt:O}.", retryAt);
                        throw new HttpRequestException($"La federación respondió {status}; el intento queda aplazado.", null, response.StatusCode);
                    }

                    byte[] content;
                    try { content = await response.Content.ReadAsByteArrayAsync(ct); }
                    catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
                    {
                        await store.FailAsync(key, status, clock.GetUtcNow().AddMinutes(Math.Max(1, _options.Asturfutbol.FailureRetryMinutes)), false, ct);
                        throw;
                    }
                    var contentType = response.Content.Headers.ContentType?.ToString();
                    var text = html ? Decode(content, contentType) : null;
                    if (content.Length == 0 || html && (string.IsNullOrWhiteSpace(text)
                        || text!.Contains("No se ha aceptado el cookie", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("<title>Access Denied", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)))
                    {
                        var retryAt = clock.GetUtcNow().AddHours(Math.Max(1, _options.Asturfutbol.BlockedPauseHours));
                        await store.FailAsync(key, status, retryAt, true, ct);
                        throw new ProviderThrottledException(AsturfutbolOfficialSheetProvider.Name,
                            "La federación devolvió una página vacía o rechazó la sesión; se pausan todas las descargas.", retryAt);
                    }
                    var result = new FederationResponse(content, contentType);
                    await store.CompleteAsync(key, result, clock.GetUtcNow().AddHours(Math.Max(1, _options.Asturfutbol.CacheHours)), ct);
                    return result;
                }
            }
        }
        finally { gate.Semaphore.Release(); }
    }

    private DateTimeOffset RetryAt(HttpResponseMessage response, bool blocked)
    {
        var now = clock.GetUtcNow();
        var minimum = blocked
            ? now.AddHours(Math.Max(1, _options.Asturfutbol.BlockedPauseHours))
            : now.AddMinutes(Math.Max(1, _options.Asturfutbol.FailureRetryMinutes));
        var requested = response.Headers.RetryAfter?.Date
            ?? (response.Headers.RetryAfter?.Delta is { } delta ? now + delta : minimum);
        return requested > minimum ? requested : minimum;
    }

    private void EnsureHost(Uri uri)
    {
        static string Host(Uri value) => value.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? value.Host[4..] : value.Host;
        if (uri.Scheme is not ("http" or "https") || !Host(uri).Equals(Host(http.BaseAddress!), StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("La URL o redirección no pertenece al proveedor de la federación.");
    }

    private static string Decode(byte[] bytes, string? contentType)
    {
        var charset = contentType?.Split(';').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("charset=", StringComparison.OrdinalIgnoreCase))?[8..].Trim('"');
        if (!string.IsNullOrEmpty(charset))
        {
            try { return Encoding.GetEncoding(charset).GetString(bytes); }
            catch (ArgumentException) { }
        }
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
