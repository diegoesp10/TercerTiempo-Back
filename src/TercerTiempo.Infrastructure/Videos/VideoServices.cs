using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;

namespace TercerTiempo.Infrastructure.Videos;

internal sealed class FileSystemVideoStorage(IOptions<VideoOptions> options) : IVideoStorage
{
    private string Root => Path.GetFullPath(options.Value.Directory);

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);

        var extension = Path.GetExtension(fileName);
        if (extension.Length > 10 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.')) extension = string.Empty;

        var key = $"{Guid.CreateVersion7():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(Root, key);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);

        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Resolve(storageKey);
        if (!File.Exists(path))
            throw new FileNotFoundException("El archivo del vídeo no está en el almacenamiento.", storageKey);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // Subida por trozos: temporal en uploads/ que se renombra al completarse.
    public Task<string> BeginUploadAsync(string fileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(Root, "uploads"));
        var key = $"uploads/{Guid.CreateVersion7():N}.part";
        using (File.Create(Path.Combine(Root, key))) { }
        return Task.FromResult(key);
    }

    // Cada trozo se escribe a partir de los bytes ya confirmados: si una petición anterior se cortó a medias, lo que
    // dejó escrito se descarta antes, y si esta se corta, también. Así el reintento del mismo trozo nunca corrompe el archivo.
    public async Task<long> AppendUploadAsync(string uploadKey, Stream chunk, long offset, long length, CancellationToken cancellationToken)
    {
        var path = ResolveUpload(uploadKey);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        file.SetLength(offset);
        file.Seek(0, SeekOrigin.End);
        try
        {
            await chunk.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            file.SetLength(offset);
            throw;
        }

        var written = file.Length - offset;
        if (written != length)
        {
            file.SetLength(offset);
            return 0;
        }

        return written;
    }

    public Task<string> CommitUploadAsync(string uploadKey, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Length > 10 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.')) extension = string.Empty;

        var key = $"{Guid.CreateVersion7():N}{extension.ToLowerInvariant()}";
        File.Move(ResolveUpload(uploadKey), Path.Combine(Root, key));
        return Task.FromResult(key);
    }

    public Task DiscardUploadAsync(string uploadKey, CancellationToken cancellationToken)
    {
        var path = ResolveUpload(uploadKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<long?> AvailableBytesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        try
        {
            return Task.FromResult<long?>(new DriveInfo(Root).AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<long?>(null);
        }
    }

    private string ResolveUpload(string uploadKey)
    {
        var uploads = Path.Combine(Root, "uploads");
        var path = Path.GetFullPath(Path.Combine(uploads, Path.GetFileName(uploadKey)));
        if (!path.StartsWith(uploads, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Clave de subida no válida.");
        return path;
    }

    private string Resolve(string storageKey)
    {
        var path = Path.GetFullPath(Path.Combine(Root, Path.GetFileName(storageKey)));
        if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Clave de almacenamiento no válida.");
        return path;
    }
}

internal sealed class FakePaymentGateway(ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    public string ProviderName => "fake";

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var reference = $"fake_{Guid.CreateVersion7():N}";
        logger.LogInformation("Pago simulado de {Amount} {Currency} por «{Description}» (usuario {User}): {Reference}",
            request.AmountCents / 100m, request.Currency, request.Description, request.PayerUserId, reference);

        return Task.FromResult(new PaymentResult(true, reference, null));
    }
}

// Caducidad deslizante: cada petición por rangos del reproductor renueva el ticket, así no se corta a mitad de un
// partido; el tope absoluto evita que un ticket filtrado sirva para siempre.
internal sealed class MemoryVideoTickets(IMemoryCache cache, IOptions<VideoOptions> options, TimeProvider clock) : IVideoTickets
{
    public (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid videoId, Guid userId, bool download = false)
    {
        var ticket = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddHours(settings.TicketMaxHours);

        cache.Set(Key(ticket), new VideoPlaybackGrant(videoId, download), new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(settings.TicketMinutes),
            AbsoluteExpiration = expiresAt,
        });
        return (ticket, expiresAt);
    }

    public VideoPlaybackGrant? Resolve(string ticket) =>
        ticket.Length is > 16 and < 100 && cache.TryGetValue(Key(ticket), out VideoPlaybackGrant? grant) ? grant : null;

    private static string Key(string ticket) => $"video-ticket:{ticket}";
}
