using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Teams;

namespace TercerTiempo.Infrastructure.Teams;

internal sealed class FileSystemCrestStorage(IOptions<CrestOptions> options) : ICrestStorage
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/gif"] = "gif",
        ["image/webp"] = "webp",
        ["image/svg+xml"] = "svg",
    };

    private string Root => Path.GetFullPath(options.Value.Directory);

    public async Task<string> SaveAsync(Guid teamId, CrestImage image, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);

        var extension = Extensions.GetValueOrDefault(image.ContentType) ?? "img";
        // Versión aleatoria (no derivada del reloj) para que dos cambios seguidos nunca compartan URL.
        var key = $"{teamId:N}-{Guid.NewGuid():N}"[..41] + $".{extension}";

        await File.WriteAllBytesAsync(Path.Combine(Root, key), image.Content, cancellationToken);

        foreach (var stale in Directory.EnumerateFiles(Root, $"{teamId:N}*"))
        {
            if (!string.Equals(Path.GetFileName(stale), key, StringComparison.OrdinalIgnoreCase)) File.Delete(stale);
        }

        return key;
    }

    public Task<CrestFile?> OpenReadAsync(string crestKey, CancellationToken cancellationToken)
    {
        var path = Resolve(crestKey);
        if (!File.Exists(path)) return Task.FromResult<CrestFile?>(null);

        var extension = Path.GetExtension(path).TrimStart('.');
        var contentType = Extensions.FirstOrDefault(e => e.Value.Equals(extension, StringComparison.OrdinalIgnoreCase)).Key ?? "application/octet-stream";
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 14, useAsync: true);
        return Task.FromResult<CrestFile?>(new CrestFile(stream, contentType));
    }

    private string Resolve(string crestKey)
    {
        var path = Path.GetFullPath(Path.Combine(Root, Path.GetFileName(crestKey)));
        if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Clave de escudo no válida.");
        return path;
    }
}
