using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Medical;

namespace TercerTiempo.Infrastructure.Medical;

// En su propia carpeta del volumen de datos, que no sirve nadie: cada fichero sale solo por la API. La clave lleva la lesión
// y una parte aleatoria, así que no se puede adivinar ni chocar con otra.
internal sealed class FileSystemInjuryDocumentStorage(IOptions<InjuryDocumentOptions> options) : IInjuryDocumentStorage
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = "pdf",
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
    };

    private string Root => Path.GetFullPath(options.Value.Directory);

    public async Task<string> SaveAsync(Guid injuryId, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        if (!Extensions.TryGetValue(contentType, out var extension))
            throw new InvalidOperationException($"Tipo de documento no admitido: {contentType}.");

        Directory.CreateDirectory(Root);
        var key = $"{injuryId:N}-{Guid.NewGuid():N}.{extension}";
        await File.WriteAllBytesAsync(Resolve(key), content, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 14, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(Root, Path.GetFileName(key)));
        if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Clave de documento no válida.");
        return path;
    }
}
