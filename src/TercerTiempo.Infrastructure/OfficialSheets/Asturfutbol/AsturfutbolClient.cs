using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

/// <summary>
/// Cliente HTTP de asturfutbol.es: mantiene la cookie de sesión (JSESSIONID) que la web exige, sigue la redirección
/// de bienvenida (<c>NLogin</c>), espacia las peticiones y decodifica las páginas (vienen en ISO-8859-1).
/// Una instancia por ámbito; el <see cref="HttpClient"/> se configura con cookies y redirecciones en el registro.
/// </summary>
internal sealed class AsturfutbolClient(HttpClient http, IOptions<OfficialSheetOptions> options, ILogger<AsturfutbolClient> logger)
{
    private const int MaxRetries = 2;

    private static readonly Encoding Latin1 = Encoding.Latin1;

    private readonly AsturfutbolOptions _settings = options.Value.Asturfutbol;
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    /// <summary>GET relativo a BaseUrl. Devuelve el HTML ya decodificado; null si la web responde 404.</summary>
    public async Task<string?> GetHtmlAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await ThrottleAsync(cancellationToken);

            try
            {
                using var response = await http.GetAsync(pathAndQuery, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt > MaxRetries || (int)response.StatusCode is >= 400 and < 500)
                        throw new HttpRequestException($"asturfutbol.es respondió {(int)response.StatusCode} a {pathAndQuery}");

                    logger.LogWarning("asturfutbol.es respondió {Status} a {Path}; reintento {Attempt}", (int)response.StatusCode, pathAndQuery, attempt);
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var html = Decode(bytes, response.Content.Headers.ContentType?.CharSet);

                // Sin cookie la web devuelve una página vacía que solo dice "No se ha aceptado el cookie".
                if (html.Contains("No se ha aceptado el cookie", StringComparison.OrdinalIgnoreCase))
                {
                    if (attempt > MaxRetries)
                        throw new HttpRequestException("asturfutbol.es no acepta la cookie de sesión.");

                    logger.LogWarning("asturfutbol.es rechazó la sesión en {Path}; reintento {Attempt}", pathAndQuery, attempt);
                    continue;
                }

                return html;
            }
            catch (HttpRequestException) when (attempt <= MaxRetries)
            {
                logger.LogWarning("Error de red con asturfutbol.es en {Path}; reintento {Attempt}", pathAndQuery, attempt);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt <= MaxRetries)
            {
                logger.LogWarning("Timeout con asturfutbol.es en {Path}; reintento {Attempt}", pathAndQuery, attempt);
            }

            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
        }
    }

    public string AbsoluteUrl(string pathAndQuery) => new Uri(http.BaseAddress!, pathAndQuery).ToString();

    private static string Decode(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrEmpty(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes); }
            catch (ArgumentException) { /* charset desconocido: se decide abajo */ }
        }

        // La web no declara charset; si el contenido no es UTF-8 válido es ISO-8859-1.
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Latin1.GetString(bytes);
        }
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        var wait = TimeSpan.FromMilliseconds(_settings.RequestDelayMs) - (DateTimeOffset.UtcNow - _lastRequestAt);
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);

        _lastRequestAt = DateTimeOffset.UtcNow;
    }
}
