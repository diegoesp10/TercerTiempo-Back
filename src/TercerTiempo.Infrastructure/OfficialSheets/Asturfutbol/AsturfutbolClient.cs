using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    AsturfutbolSession session,
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
        var retries = 0;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = clock.GetUtcNow();
                var reservation = await store.ReserveAsync(key, original.AbsoluteUri, lease, redirect, now, _options.AsturfutbolLimits, ct);
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
                session.Use(reservation.SessionEpoch);

                HttpResponseMessage response;
                try
                {
                    response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    var retryAt = clock.GetUtcNow().AddMinutes(Math.Max(1, _options.Asturfutbol.FailureRetryMinutes));
                    await store.FailAsync(AsturfutbolOfficialSheetProvider.Name, key, null, retryAt, null, ct);
                    throw new HttpRequestException("No se pudo consultar la federación; el intento queda aplazado en la base de datos.", ex);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    if (status is 301 or 302 or 303 or 307 or 308)
                    {
                        if (response.Headers.Location is not { } location || ++redirects > 5)
                        {
                            await store.FailAsync(AsturfutbolOfficialSheetProvider.Name, key, status, now.AddHours(1), null, ct);
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
                        await store.CompleteAsync(AsturfutbolOfficialSheetProvider.Name, key, absent, clock.GetUtcNow().AddHours(Math.Max(1, _options.Asturfutbol.CacheHours)), ct);
                        return absent;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var blocked = status is 403 or 429;
                        var pauseProvider = blocked || status >= 500 || response.Headers.RetryAfter is not null;
                        var retryAt = RetryAt(response, blocked);
                        var pause = pauseProvider
                            ? new ProviderPause(retryAt, blocked ? PauseKinds.Blocked : PauseKinds.Error,
                                blocked ? $"La federación respondió {status} (acceso denegado o demasiadas peticiones)." : $"La federación respondió {status}.")
                            : null;
                        await store.FailAsync(AsturfutbolOfficialSheetProvider.Name, key, status, retryAt, pause, ct);
                        logger.LogWarning("La federación respondió {Status}; sin reintentos inmediatos, próximo intento a partir de {RetryAt}", status, retryAt);
                        if (pauseProvider) throw new ProviderThrottledException(AsturfutbolOfficialSheetProvider.Name,
                            $"La federación respondió {status}; todas las descargas quedan en pausa hasta {retryAt:O}.", retryAt);
                        throw new HttpRequestException($"La federación respondió {status}; el intento queda aplazado.", null, response.StatusCode);
                    }

                    byte[] content;
                    try { content = await response.Content.ReadAsByteArrayAsync(ct); }
                    catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
                    {
                        await store.FailAsync(AsturfutbolOfficialSheetProvider.Name, key, status, clock.GetUtcNow().AddMinutes(Math.Max(1, _options.Asturfutbol.FailureRetryMinutes)), null, ct);
                        throw;
                    }
                    var contentType = response.Content.Headers.ContentType?.ToString();
                    var text = html ? Decode(content, contentType) : null;
                    if (Rejection(content, text) is { } rejection)
                    {
                        var settings = _options.Asturfutbol;
                        // Una página vacía suelta no es un bloqueo: se repite la misma con una sesión nueva, como haría
                        // quien recarga en el navegador. Las páginas de bloqueo explícitas no se repiten.
                        if (rejection.Kind == RejectionKind.SessionLost && retries < Math.Max(0, settings.RejectionRetries))
                        {
                            retries++;
                            logger.LogInformation("La federación devolvió {What} en {Url} tras {Pages} páginas; se repite con una sesión nueva (intento {Retry}). Respuesta: {Snippet}",
                                rejection.What, uri, session.PagesServed, retries, Snippet(text));
                            session.Reset();
                            uri = original;
                            redirect = false;
                            redirects = 0;
                            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.RejectionRetrySeconds) * retries), clock, ct);
                            continue;
                        }

                        var pause = rejection.Kind == RejectionKind.SessionLost
                            ? await store.RejectAsync(AsturfutbolOfficialSheetProvider.Name, key, status, rejection.What, clock.GetUtcNow(),
                                TimeSpan.FromMinutes(Math.Max(1, settings.RetryPauseMinutes)), TimeSpan.FromHours(Math.Max(1, settings.MaxRetryPauseHours)), ct)
                            : new ProviderPause(clock.GetUtcNow().AddHours(Math.Max(1, settings.BlockedPauseHours)), PauseKinds.Blocked,
                                $"La federación devolvió {rejection.What}.", NewSession: true);
                        if (pause.Kind == PauseKinds.Blocked) await store.FailAsync(AsturfutbolOfficialSheetProvider.Name, key, status, pause.Until, pause, ct);
                        logger.LogWarning("La federación rechazó {Url} ({Kind}) tras {Retries} reintentos: {Reason}. Respuesta: {Snippet}",
                            uri, pause.Kind, retries, pause.Reason, Snippet(text));
                        throw new ProviderThrottledException(AsturfutbolOfficialSheetProvider.Name, $"{pause.Reason} Se retoma a partir de {pause.Until:O}.", pause.Until);
                    }
                    session.Served();
                    var result = new FederationResponse(content, contentType);
                    await store.CompleteAsync(AsturfutbolOfficialSheetProvider.Name, key, result, clock.GetUtcNow().AddHours(Math.Max(1, _options.Asturfutbol.CacheHours)), ct);
                    return result;
                }
            }
        }
        finally { gate.Semaphore.Release(); }
    }

    private enum RejectionKind { SessionLost, Blocked }

    private sealed record PageRejection(RejectionKind Kind, string What);

    // Respuestas 200 que no son la página: sin sesión ("No se ha aceptado el cookie" o vacía) o una página de bloqueo.
    private static PageRejection? Rejection(byte[] content, string? text)
    {
        if (content.Length == 0) return new(RejectionKind.SessionLost, "una página vacía");
        if (text is null) return null;
        if (string.IsNullOrWhiteSpace(text)) return new(RejectionKind.SessionLost, "una página vacía");
        if (text.Contains("No se ha aceptado el cookie", StringComparison.OrdinalIgnoreCase)) return new(RejectionKind.SessionLost, "«No se ha aceptado el cookie»");
        if (text.Contains("<title>Access Denied", StringComparison.OrdinalIgnoreCase)) return new(RejectionKind.Blocked, "«Access Denied»");
        if (text.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)) return new(RejectionKind.Blocked, "«Too Many Requests»");
        if (text.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)) return new(RejectionKind.Blocked, "una comprobación anti-bots");
        return null;
    }

    private static string Snippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(vacía)";
        var plain = Regex.Replace(Regex.Replace(text, "<[^>]+>", " "), @"\s+", " ").Trim();
        return plain.Length > 300 ? plain[..300] : plain;
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
