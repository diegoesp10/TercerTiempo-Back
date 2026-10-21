using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Videos;

/// <summary>
/// Vídeo de un partido subido por el cuerpo técnico de una plantilla. Lo ve gratis la plantilla que lo sube (su staff
/// y sus jugadores vinculados); el resto de técnicos tienen que comprar el acceso (<see cref="VideoPurchase"/>).
/// </summary>
public sealed class MatchVideo : AggregateRoot
{
    public const long MinSizeBytes = 1024;

    public Guid SquadId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid SeasonId { get; private set; }
    public Guid UploadedByCoachId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    /// <summary>Clave en el almacenamiento (ruta relativa, nombre de blob...).</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>Precio del acceso para quien no es de la plantilla, en céntimos; 0 = gratis para todos.</summary>
    public int PriceCents { get; private set; }

    public string Currency { get; private set; } = "EUR";
    public DateTimeOffset UploadedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;
    public bool IsFree => PriceCents == 0;

    private MatchVideo() { }

    public static MatchVideo Upload(
        Guid squadId, Guid teamId, Guid matchId, Guid seasonId, Guid coachId,
        string? title, string fileName, string contentType, long sizeBytes, string storageKey, int priceCents, DateTimeOffset now)
    {
        if (sizeBytes < MinSizeBytes)
            throw new DomainException(ErrorCode.VideoTooSmall);

        var file = Guard.NotEmpty(fileName, "archivo", 255);

        return new MatchVideo
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            UploadedByCoachId = Guard.NotEmpty(coachId, nameof(coachId)),
            Title = string.IsNullOrWhiteSpace(title) ? file : Guard.NotEmpty(title, "título", 150),
            FileName = file,
            ContentType = Guard.NotEmpty(contentType, "tipo de contenido", 100),
            SizeBytes = sizeBytes,
            StorageKey = Guard.NotEmpty(storageKey, nameof(storageKey), 500),
            PriceCents = Guard.NotNegative(priceCents, "precio"),
            UploadedAt = now,
        };
    }

    public void SetPrice(int priceCents) => PriceCents = Guard.NotNegative(priceCents, "precio");

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            throw new DomainException(ErrorCode.VideoAlreadyDeleted);

        DeletedAt = now;
    }
}

/// <summary>Acceso de pago de un técnico ajeno a la plantilla a un vídeo.</summary>
public sealed class VideoPurchase : AggregateRoot
{
    public Guid VideoId { get; private set; }
    public Guid CoachId { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; } = "EUR";

    /// <summary>Referencia de la pasarela de pago.</summary>
    public string PaymentReference { get; private set; } = string.Empty;

    public DateTimeOffset PurchasedAt { get; private set; }

    private VideoPurchase() { }

    public static VideoPurchase Create(Guid videoId, Guid coachId, int amountCents, string currency, string paymentReference, DateTimeOffset now) =>
        new()
        {
            Id = NewId(),
            VideoId = Guard.NotEmpty(videoId, nameof(videoId)),
            CoachId = Guard.NotEmpty(coachId, nameof(coachId)),
            AmountCents = Guard.NotNegative(amountCents, "importe"),
            Currency = Guard.NotEmpty(currency, "moneda", 3),
            PaymentReference = Guard.NotEmpty(paymentReference, "referencia de pago", 200),
            PurchasedAt = now,
        };
}

public interface IMatchVideoStore
{
    Task<MatchVideo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Vídeos no eliminados de un partido.</summary>
    Task<IReadOnlyList<MatchVideo>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    void Add(MatchVideo video);
}

public interface IVideoPurchaseStore
{
    Task<VideoPurchase?> GetAsync(Guid videoId, Guid coachId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoPurchase>> GetByCoachAsync(Guid coachId, CancellationToken cancellationToken = default);
    void Add(VideoPurchase purchase);
}
