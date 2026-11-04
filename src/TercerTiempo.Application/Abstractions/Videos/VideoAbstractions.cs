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

    // Bytes libres donde se guardan los vídeos; null si el almacenamiento no lo sabe.
    Task<long?> AvailableBytesAsync(CancellationToken cancellationToken);
}

public sealed record PaymentRequest(Guid PayerUserId, int AmountCents, string Currency, string Description);

public sealed record PaymentResult(bool Succeeded, string Reference, string? Error);

public interface IPaymentGateway
{
    string ProviderName { get; }
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken);
}

// El reproductor y la descarga no pueden mandar el JWT: piden el archivo con un ticket que se emite tras comprobar el
// acceso. El ticket sigue valiendo mientras se use (un partido entero son 90 minutos de peticiones por rangos), con un tope.
public interface IVideoTickets
{
    (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid videoId, Guid userId, bool download = false);

    VideoPlaybackGrant? Resolve(string ticket);
}

public sealed record VideoPlaybackGrant(Guid VideoId, bool Download);

public sealed class VideoOptions
{
    public const string SectionName = "Videos";

    public string Directory { get; set; } = "data/videos";

    // Un partido entero a 1080p desde un móvil o una cámara ronda los 6 GB; se deja margen para grabaciones de más calidad.
    public int MaxSizeMb { get; set; } = 20480;

    public int ChunkSizeMb { get; set; } = 16;

    // Horas sin recibir trozos tras las que caduca una subida.
    public int UploadExpiryHours { get; set; } = 24;

    // Espacio que se deja libre en el disco además del propio vídeo.
    public int MinFreeSpaceMb { get; set; } = 2048;

    // Sin valor por defecto: al enlazar la configuración, .NET añade los elementos de appsettings a los que ya hubiera.
    public string[] AllowedContentTypes { get; set; } = [];

    // Minutos sin usarse tras los que caduca un ticket, y horas como máximo aunque se siga usando.
    public int TicketMinutes { get; set; } = 30;

    public int TicketMaxHours { get; set; } = 8;

    public long MaxSizeBytes => (long)MaxSizeMb * 1024 * 1024;

    public int ChunkSizeBytes => ChunkSizeMb * 1024 * 1024;
}
