using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Videos;

public enum VideoVisibility
{
    Private = 1,
    Public = 2,
}

public enum VideoPart
{
    Full = 1,
    FirstHalf = 2,
    SecondHalf = 3,
}

public sealed class MatchVideo : AggregateRoot
{
    public const long MinSizeBytes = 1024;

    public Guid SquadId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid SeasonId { get; private set; }
    public Guid? UploadedByCoachId { get; private set; }
    public Guid? UploadedByClubId { get; private set; }
    public VideoPart Part { get; private set; }
    public VideoVisibility Visibility { get; private set; } = VideoVisibility.Private;
    public string Title { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public int PriceCents { get; private set; }

    public string Currency { get; private set; } = "EUR";
    public DateTimeOffset UploadedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;
    public bool IsFree => PriceCents == 0;
    public bool IsPublic => Visibility == VideoVisibility.Public;

    private MatchVideo() { }

    public static MatchVideo Upload(
        Guid squadId, Guid teamId, Guid matchId, Guid seasonId, Guid? coachId, VideoPart part,
        string? title, string fileName, string contentType, long sizeBytes, string storageKey, int priceCents, DateTimeOffset now, Guid? clubId = null)
    {
        VideoOwner.EnsureValid(coachId, clubId);
        if (sizeBytes < MinSizeBytes)
            throw new DomainException(ErrorCode.VideoTooSmall);

        if (!Enum.IsDefined(part))
            throw new DomainException(ErrorCode.VideoPartInvalid);

        var file = Guard.NotEmpty(fileName, "archivo", 255);

        return new MatchVideo
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            UploadedByCoachId = coachId,
            UploadedByClubId = clubId,
            Part = part,
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

    public void SetVisibility(VideoVisibility visibility)
    {
        if (!Enum.IsDefined(visibility))
            throw new DomainException(ErrorCode.VideoVisibilityInvalid);
        Visibility = visibility;
    }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            throw new DomainException(ErrorCode.VideoAlreadyDeleted);

        DeletedAt = now;
    }

    // Un partido se sube entero o en dos partes, nunca mezclado; y cada parte una sola vez por plantilla.
    public static void EnsurePartAvailable(IEnumerable<MatchVideo> squadVideos, VideoPart part)
    {
        var existing = squadVideos.Where(v => !v.IsDeleted).Select(v => v.Part).ToList();
        if (existing.Count == 0) return;

        if (existing.Contains(part))
            throw new DomainException(ErrorCode.VideoPartTaken);

        if (part == VideoPart.Full || existing.Contains(VideoPart.Full))
            throw new DomainException(ErrorCode.VideoPartMix);
    }
}

public sealed class VideoPurchase : AggregateRoot
{
    public Guid VideoId { get; private set; }
    public Guid CoachId { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; } = "EUR";

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

// Subida por trozos: el archivo se recibe en orden y se guarda en un temporal hasta completarlo.
public sealed class VideoUploadSession : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid? CoachId { get; private set; }
    public Guid? ClubId { get; private set; }
    public VideoPart Part { get; private set; }
    public string? Title { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int ChunkSize { get; private set; }
    public long ReceivedBytes { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsComplete => ReceivedBytes == SizeBytes;
    public int NextChunk => (int)(ReceivedBytes / ChunkSize);
    public int TotalChunks => (int)((SizeBytes + ChunkSize - 1) / ChunkSize);

    private VideoUploadSession() { }

    public static VideoUploadSession Begin(
        Guid squadId, Guid matchId, Guid? coachId, VideoPart part, string? title, string fileName, string contentType,
        long sizeBytes, int chunkSize, string storageKey, DateTimeOffset now, TimeSpan lifetime, Guid? clubId = null)
    {
        VideoOwner.EnsureValid(coachId, clubId);
        if (sizeBytes < MatchVideo.MinSizeBytes)
            throw new DomainException(ErrorCode.VideoTooSmall);

        if (!Enum.IsDefined(part))
            throw new DomainException(ErrorCode.VideoPartInvalid);

        return new VideoUploadSession
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            CoachId = coachId,
            ClubId = clubId,
            Part = part,
            Title = string.IsNullOrWhiteSpace(title) ? null : Guard.NotEmpty(title, "título", 150),
            FileName = Guard.NotEmpty(fileName, "archivo", 255),
            ContentType = Guard.NotEmpty(contentType, "tipo de contenido", 100),
            SizeBytes = sizeBytes,
            ChunkSize = Guard.InRange(chunkSize, 1024 * 1024, 256 * 1024 * 1024, "tamaño de trozo"),
            StorageKey = Guard.NotEmpty(storageKey, nameof(storageKey), 500),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }

    public bool IsExpired(DateTimeOffset now) => now > ExpiresAt;

    public void EnsureAccepts(int chunkIndex, long length, DateTimeOffset now)
    {
        if (IsExpired(now))
            throw new DomainException(ErrorCode.VideoUploadExpired);

        if (chunkIndex != NextChunk)
            throw new DomainException(ErrorCode.VideoUploadChunkOutOfOrder, NextChunk);

        var expected = Math.Min(ChunkSize, SizeBytes - ReceivedBytes);
        if (length != expected)
            throw new DomainException(ErrorCode.VideoUploadChunkSize, expected);
    }

    public void Received(long length, DateTimeOffset now)
    {
        ReceivedBytes += length;
        UpdatedAt = now;
    }

    public void EnsureComplete()
    {
        if (!IsComplete)
            throw new DomainException(ErrorCode.VideoUploadIncomplete, SizeBytes - ReceivedBytes);
    }
}

// Enlace público al visor con caducidad: lo genera el cuerpo técnico y lo puede revocar.
public sealed class VideoShareLink : AggregateRoot
{
    public Guid VideoId { get; private set; }
    public Guid? CreatedByCoachId { get; private set; }
    public Guid? CreatedByClubId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private VideoShareLink() { }

    public static VideoShareLink Create(Guid videoId, Guid? coachId, string token, DateTimeOffset now, TimeSpan lifetime, Guid? clubId = null)
    {
        VideoOwner.EnsureValid(coachId, clubId);
        return new()
        {
            Id = NewId(),
            VideoId = Guard.NotEmpty(videoId, nameof(videoId)),
            CreatedByCoachId = coachId,
            CreatedByClubId = clubId,
            Token = Guard.NotEmpty(token, "token", 100),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }

    public bool IsValid(DateTimeOffset now) => RevokedAt is null && now <= ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public interface IVideoShareStore
{
    Task<VideoShareLink?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoShareLink>> GetByVideoAsync(Guid videoId, CancellationToken cancellationToken = default);
    void Add(VideoShareLink link);
}

public interface IMatchVideoStore
{
    Task<MatchVideo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchVideo>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    void Add(MatchVideo video);
}

public interface IVideoPurchaseStore
{
    Task<VideoPurchase?> GetAsync(Guid videoId, Guid coachId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoPurchase>> GetByCoachAsync(Guid coachId, CancellationToken cancellationToken = default);
    void Add(VideoPurchase purchase);
}

public interface IVideoUploadStore
{
    Task<VideoUploadSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoUploadSession>> GetByOwnerAsync(Guid? coachId, Guid? clubId, CancellationToken cancellationToken = default);
    void Add(VideoUploadSession session);
    void Remove(VideoUploadSession session);
}

internal static class VideoOwner
{
    public static void EnsureValid(Guid? coachId, Guid? clubId)
    {
        if ((coachId is null) == (clubId is null) || coachId == Guid.Empty || clubId == Guid.Empty)
            throw new DomainException(ErrorCode.VideoManagerRequired);
    }
}
