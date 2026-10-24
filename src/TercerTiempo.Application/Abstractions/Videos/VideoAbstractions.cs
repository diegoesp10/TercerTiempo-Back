namespace TercerTiempo.Application.Abstractions.Videos;

public interface IVideoStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    Task<string> BeginUploadAsync(string fileName, CancellationToken cancellationToken);

    Task<long> AppendUploadAsync(string uploadKey, Stream chunk, long offset, long length, CancellationToken cancellationToken);

    Task<string> CommitUploadAsync(string uploadKey, string fileName, CancellationToken cancellationToken);

    Task DiscardUploadAsync(string uploadKey, CancellationToken cancellationToken);
}

public sealed record PaymentRequest(Guid PayerUserId, int AmountCents, string Currency, string Description);

public sealed record PaymentResult(bool Succeeded, string Reference, string? Error);

public interface IPaymentGateway
{
    string ProviderName { get; }
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken);
}

public interface IVideoTickets
{
    (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid videoId, Guid userId, bool requiresPublicVideo = false);

    VideoPlaybackGrant? Resolve(string ticket);
}

public sealed record VideoPlaybackGrant(Guid VideoId, bool RequiresPublicVideo);

public sealed class VideoOptions
{
    public const string SectionName = "Videos";

    public string Directory { get; set; } = "data/videos";

    public int MaxSizeMb { get; set; } = 4096;

    public int ChunkSizeMb { get; set; } = 16;

    public int UploadExpiryHours { get; set; } = 24;

    public int DefaultShareHours { get; set; } = 24;

    public int MaxShareHours { get; set; } = 720;

    public int DefaultPriceCents { get; set; } = 500;

    public string Currency { get; set; } = "EUR";

    // Sin valor por defecto: al enlazar la configuración, .NET añade los elementos de appsettings a los que ya hubiera.
    public string[] AllowedContentTypes { get; set; } = [];

    public int TicketMinutes { get; set; } = 30;

    public long MaxSizeBytes => (long)MaxSizeMb * 1024 * 1024;

    public int ChunkSizeBytes => ChunkSizeMb * 1024 * 1024;
}
