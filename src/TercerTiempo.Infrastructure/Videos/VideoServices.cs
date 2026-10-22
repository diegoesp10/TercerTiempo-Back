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

internal sealed class MemoryVideoTickets(IMemoryCache cache, IOptions<VideoOptions> options, TimeProvider clock) : IVideoTickets
{
    public (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid videoId, Guid userId)
    {
        var ticket = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var expiresAt = clock.GetUtcNow().AddMinutes(options.Value.TicketMinutes);

        cache.Set(Key(ticket), videoId, expiresAt);
        return (ticket, expiresAt);
    }

    public Guid? Resolve(string ticket) =>
        ticket.Length is > 16 and < 100 && cache.TryGetValue(Key(ticket), out Guid videoId) ? videoId : null;

    private static string Key(string ticket) => $"video-ticket:{ticket}";
}
