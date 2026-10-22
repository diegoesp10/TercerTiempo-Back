namespace TercerTiempo.Application.Abstractions.Videos;

public interface IVideoStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
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
    (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid videoId, Guid userId);

    Guid? Resolve(string ticket);
}

public sealed class VideoOptions
{
    public const string SectionName = "Videos";

    public string Directory { get; set; } = "data/videos";

    public int MaxSizeMb { get; set; } = 2048;

    public int DefaultPriceCents { get; set; } = 500;

    public string Currency { get; set; } = "EUR";

    public string[] AllowedContentTypes { get; set; } = ["video/mp4", "video/webm", "video/quicktime", "video/x-matroska"];

    public int TicketMinutes { get; set; } = 30;

    public long MaxSizeBytes => (long)MaxSizeMb * 1024 * 1024;
}
