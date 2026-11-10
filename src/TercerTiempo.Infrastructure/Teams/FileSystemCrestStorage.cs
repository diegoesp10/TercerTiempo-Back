using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Teams;

namespace TercerTiempo.Infrastructure.Teams;

internal sealed class FileSystemCrestStorage(IOptions<CrestOptions> options) : ICrestStorage
{
    private readonly FileSystemImageStore _store = new(options.Value.Directory);

    public Task<string> SaveAsync(Guid teamId, CrestImage image, CancellationToken cancellationToken) =>
        _store.SaveAsync(teamId, image, cancellationToken);

    public Task<CrestFile?> OpenReadAsync(string crestKey, CancellationToken cancellationToken) =>
        _store.OpenReadAsync(crestKey);

    // Para la copia de los escudos descargados entre la base de datos reales y los ficheros de cada entorno.
    public bool Exists(string crestKey) => _store.Exists(crestKey);

    public Task WriteAsync(string crestKey, byte[] content, CancellationToken cancellationToken) =>
        _store.WriteAsync(crestKey, content, cancellationToken);

    public Task<CrestImage?> ReadAllBytesAsync(string crestKey, CancellationToken cancellationToken) =>
        _store.ReadAllBytesAsync(crestKey, cancellationToken);
}

internal sealed class FileSystemPlayerPhotoStorage(IOptions<CrestOptions> options) : IPlayerPhotoStorage
{
    private readonly FileSystemImageStore _store = new(options.Value.PhotosDirectory);

    public Task<string> SaveAsync(Guid rosterEntryId, CrestImage image, CancellationToken cancellationToken) =>
        _store.SaveAsync(rosterEntryId, image, cancellationToken);

    public Task<CrestFile?> OpenReadAsync(string photoKey, CancellationToken cancellationToken) =>
        _store.OpenReadAsync(photoKey);
}

internal sealed class FileSystemImageStore(string directory)
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/gif"] = "gif",
        ["image/webp"] = "webp",
        ["image/svg+xml"] = "svg",
    };

    private string Root => Path.GetFullPath(directory);

    public async Task<string> SaveAsync(Guid ownerId, CrestImage image, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);

        var key = NewKey(ownerId, image.ContentType);
        await File.WriteAllBytesAsync(Path.Combine(Root, key), image.Content, cancellationToken);

        foreach (var stale in Directory.EnumerateFiles(Root, $"{ownerId:N}*"))
        {
            if (!string.Equals(Path.GetFileName(stale), key, StringComparison.OrdinalIgnoreCase)) File.Delete(stale);
        }

        return key;
    }

    // Versión aleatoria (no derivada del reloj) para que dos cambios seguidos nunca compartan URL.
    public static string NewKey(Guid ownerId, string contentType) =>
        $"{ownerId:N}-{Guid.NewGuid():N}"[..41] + $".{Extensions.GetValueOrDefault(contentType) ?? "img"}";

    public static string ContentTypeOf(string key) =>
        Extensions.FirstOrDefault(e => e.Value.Equals(Path.GetExtension(key).TrimStart('.'), StringComparison.OrdinalIgnoreCase)).Key
            ?? "application/octet-stream";

    public bool Exists(string key) => File.Exists(Resolve(key));

    // Con la clave que ya trae (la de la base de datos reales); no borra otras versiones del mismo dueño.
    public async Task WriteAsync(string key, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        await File.WriteAllBytesAsync(Resolve(key), content, cancellationToken);
    }

    public async Task<CrestImage?> ReadAllBytesAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        return File.Exists(path) ? new CrestImage(await File.ReadAllBytesAsync(path, cancellationToken), ContentTypeOf(key)) : null;
    }

    public Task<CrestFile?> OpenReadAsync(string key)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) return Task.FromResult<CrestFile?>(null);

        var extension = Path.GetExtension(path).TrimStart('.');
        var contentType = Extensions.FirstOrDefault(e => e.Value.Equals(extension, StringComparison.OrdinalIgnoreCase)).Key ?? "application/octet-stream";
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 14, useAsync: true);
        return Task.FromResult<CrestFile?>(new CrestFile(stream, contentType));
    }

    private string Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(Root, Path.GetFileName(key)));
        if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Clave de imagen no válida.");
        return path;
    }
}
