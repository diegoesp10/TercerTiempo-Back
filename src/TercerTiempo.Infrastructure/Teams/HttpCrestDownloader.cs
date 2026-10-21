using System.Net;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Teams;

namespace TercerTiempo.Infrastructure.Teams;

/// <summary>Descarga escudos de cualquier URL pública; solo acepta imágenes de hasta 2 MB.</summary>
internal sealed class HttpCrestDownloader(HttpClient http, ILogger<HttpCrestDownloader> logger) : ICrestDownloader
{
    private const int MaxBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml",
    };

    public async Task<CrestImage?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? GuessFromExtension(url);
            if (contentType is null || !ImageTypes.Contains(contentType))
            {
                logger.LogWarning("Escudo {Url}: no es una imagen ({ContentType})", url, contentType);
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxBytes) return null;
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return content.Length is 0 or > MaxBytes ? null : new CrestImage(content, contentType.ToLowerInvariant());
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Escudo {Url}: no se pudo descargar", url);
            return null;
        }
    }

    private static string? GuessFromExtension(string url) => Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => null,
    };
}
