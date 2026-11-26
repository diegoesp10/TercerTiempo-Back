using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StbImageSharp;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Infrastructure.Images;

namespace TercerTiempo.Infrastructure.Teams;

// Las miniaturas se hacen la primera vez que se piden y se quedan en memoria. La clave del escudo cambia con cada versión,
// así que nunca hay que invalidarlas. SVG y WebP (y lo que no se pueda leer) se sirven tal cual.
internal sealed class CrestThumbnails(ILogger<CrestThumbnails> logger) : ICrestThumbnails, IDisposable
{
    private const int MaxSourceBytes = 8 * 1024 * 1024;
    private const int MaxSourceSide = 4096;

    private static readonly HashSet<string> Decodable = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif", "image/bmp" };

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 32L * 1024 * 1024 });

    public async Task<CrestFile?> GetAsync(string crestKey, int size, Func<CancellationToken, Task<CrestFile?>> openOriginal, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue((crestKey, size), out Thumbnail? cached) && cached is not null) return cached.Open();

        var original = await openOriginal(cancellationToken);
        if (original is null || !Decodable.Contains(original.ContentType)) return original;

        byte[]? bytes;
        await using (original.Content)
        {
            bytes = await ReadUpToAsync(original.Content, MaxSourceBytes, cancellationToken);
        }

        if (bytes is null) return await openOriginal(cancellationToken);

        var thumbnail = Shrink(bytes, size, crestKey) is { } png ? new Thumbnail(png, "image/png") : new Thumbnail(bytes, original.ContentType);
        cache.Set((crestKey, size), thumbnail, new MemoryCacheEntryOptions { Size = thumbnail.Bytes.Length });
        return thumbnail.Open();
    }

    public void Dispose() => cache.Dispose();

    private byte[]? Shrink(byte[] bytes, int size, string crestKey)
    {
        try
        {
            ImageInfo? info;
            using (var probe = new MemoryStream(bytes, writable: false))
            {
                info = ImageInfo.FromStream(probe);
            }

            // Sin cabecera legible, con medidas absurdas (una imagen bomba) o ya pequeña: el original.
            if (info is not { } header || header.Width <= 0 || header.Height <= 0 || header.Width > MaxSourceSide || header.Height > MaxSourceSide) return null;
            if (Math.Max(header.Width, header.Height) <= size) return null;

            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            var scale = (double)size / Math.Max(image.Width, image.Height);
            var width = Math.Max(1, (int)Math.Round(image.Width * scale));
            var height = Math.Max(1, (int)Math.Round(image.Height * scale));

            var png = PngEncoder.Encode(ImageShrink.AreaAverage(image.Data, image.Width, image.Height, width, height), width, height);
            return png.Length < bytes.Length ? png : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "No se pudo hacer la miniatura del escudo {CrestKey}; se sirve el original", crestKey);
            return null;
        }
    }

    private static async Task<byte[]?> ReadUpToAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private sealed record Thumbnail(byte[] Bytes, string ContentType)
    {
        public CrestFile Open() => new(new MemoryStream(Bytes, writable: false), ContentType);
    }
}
