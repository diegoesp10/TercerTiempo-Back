namespace TercerTiempo.Application.Abstractions.Videos;

/// <summary>Dónde se guardan los archivos de vídeo (disco local, blob...). La clave la decide el almacenamiento.</summary>
public interface IVideoStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed record PaymentRequest(Guid PayerUserId, int AmountCents, string Currency, string Description);

public sealed record PaymentResult(bool Succeeded, string Reference, string? Error);

/// <summary>Pasarela de cobro del acceso a los vídeos. De momento solo hay una implementación simulada.</summary>
public interface IPaymentGateway
{
    string ProviderName { get; }
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Tickets de reproducción de un solo uso y corta vida: el reproductor del navegador no puede enviar el JWT, así que
/// la API emite un ticket tras comprobar el acceso y el streaming lo canjea.
/// </summary>
public interface IVideoTickets
{
    (string Ticket, DateTimeOffset ExpiresAt) Issue(Guid videoId, Guid userId);

    /// <summary>Vídeo al que da acceso el ticket, o null si no existe o ha caducado. No consume el ticket (el reproductor pide rangos).</summary>
    Guid? Resolve(string ticket);
}

public sealed class VideoOptions
{
    public const string SectionName = "Videos";

    /// <summary>Carpeta donde se guardan los vídeos (relativa al directorio de trabajo o absoluta).</summary>
    public string Directory { get; set; } = "data/videos";

    public int MaxSizeMb { get; set; } = 2048;

    /// <summary>Precio por defecto del acceso para técnicos de otras plantillas, en céntimos.</summary>
    public int DefaultPriceCents { get; set; } = 500;

    public string Currency { get; set; } = "EUR";

    public string[] AllowedContentTypes { get; set; } = ["video/mp4", "video/webm", "video/quicktime", "video/x-matroska"];

    /// <summary>Vida de los tickets de reproducción.</summary>
    public int TicketMinutes { get; set; } = 30;

    public long MaxSizeBytes => (long)MaxSizeMb * 1024 * 1024;
}
