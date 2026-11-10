using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Videos;

public enum VideoPart
{
    Full = 1,
    FirstHalf = 2,
    SecondHalf = 3,
}

// Tal cual se subió (en cola para comprimir), comprimiéndose, listo (comprimido o ya ligero) o con la compresión
// fallida. En todos los estados el vídeo se ve y se descarga: hasta que está listo se sirve el original.
public enum VideoStatus
{
    Uploaded = 1,
    Processing = 2,
    Ready = 3,
    Failed = 4,
}

public sealed class MatchVideo : AggregateRoot
{
    public const long MinSizeBytes = 1024;
    public const int MaxErrorLength = 1000;

    public Guid SquadId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid SeasonId { get; private set; }
    public Guid? UploadedByCoachId { get; private set; }
    public Guid? UploadedByClubId { get; private set; }
    public VideoPart Part { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public VideoStatus Status { get; private set; } = VideoStatus.Uploaded;
    public int ProcessingProgress { get; private set; }
    public int ProcessingAttempts { get; private set; }
    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? ProcessingError { get; private set; }

    // Lo que ocupaba el archivo subido cuando se ha sustituido por el comprimido.
    public long? OriginalSizeBytes { get; private set; }
    public int? DurationSeconds { get; private set; }
    public int? Height { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public bool AwaitsProcessing(int maxAttempts) => !IsDeleted && Status == VideoStatus.Uploaded && ProcessingAttempts < maxAttempts;

    private MatchVideo() { }

    public static MatchVideo Upload(
        Guid squadId, Guid teamId, Guid matchId, Guid seasonId, Guid? coachId, VideoPart part,
        string? title, string fileName, string contentType, long sizeBytes, string storageKey, DateTimeOffset now, Guid? clubId = null)
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
            UploadedAt = now,
        };
    }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            throw new DomainException(ErrorCode.VideoAlreadyDeleted);

        DeletedAt = now;
    }

    public void StartProcessing(DateTimeOffset now)
    {
        if (IsDeleted || Status != VideoStatus.Uploaded)
            throw new DomainException(ErrorCode.VideoNotPending);

        Status = VideoStatus.Processing;
        ProcessingAttempts++;
        ProcessingStartedAt = now;
        ProcessingProgress = 0;
        ProcessingError = null;
    }

    public void ReportProgress(int percent)
    {
        EnsureProcessing();
        ProcessingProgress = Math.Clamp(percent, 0, 100);
    }

    // El archivo subido ya venía ligero: se queda tal cual.
    public void KeepOriginal(int? durationSeconds, int? height, DateTimeOffset now)
    {
        EnsureProcessing();
        DurationSeconds = durationSeconds;
        Height = height;
        Finish(now);
    }

    // El comprimido sustituye al original (que se borra después): desde ahora se sirve el nuevo archivo.
    public void Compressed(string storageKey, long sizeBytes, string contentType, string fileName, int durationSeconds, int height, DateTimeOffset now)
    {
        EnsureProcessing();
        if (sizeBytes < MinSizeBytes)
            throw new DomainException(ErrorCode.VideoTooSmall);

        OriginalSizeBytes = SizeBytes;
        StorageKey = Guard.NotEmpty(storageKey, nameof(storageKey), 500);
        SizeBytes = sizeBytes;
        ContentType = Guard.NotEmpty(contentType, "tipo de contenido", 100);
        FileName = Guard.NotEmpty(fileName, "archivo", 255);
        DurationSeconds = durationSeconds;
        Height = height;
        Finish(now);
    }

    public void FailProcessing(string error, DateTimeOffset now)
    {
        EnsureProcessing();
        Status = VideoStatus.Failed;
        ProcessingError = string.IsNullOrWhiteSpace(error) ? "Error desconocido" : error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        ProcessedAt = now;
    }

    // El proceso que lo comprimía se paró a medias (apagado, reinicio): vuelve a la cola sin contar el intento.
    public void Requeue()
    {
        if (Status != VideoStatus.Processing) return;
        Status = VideoStatus.Uploaded;
        ProcessingAttempts = Math.Max(0, ProcessingAttempts - 1);
        ProcessingStartedAt = null;
        ProcessingProgress = 0;
    }

    private void Finish(DateTimeOffset now)
    {
        Status = VideoStatus.Ready;
        ProcessingProgress = 100;
        ProcessedAt = now;
        ProcessingError = null;
    }

    private void EnsureProcessing()
    {
        if (Status != VideoStatus.Processing)
            throw new DomainException(ErrorCode.VideoNotProcessing);
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

// Subida por trozos: el archivo se recibe en orden y se guarda en un temporal hasta completarlo. Cada trozo recibido
// renueva la caducidad, así un partido entero con una conexión lenta no caduca mientras sigue subiendo.
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
    public int NextChunk => IsComplete ? TotalChunks : (int)(ReceivedBytes / ChunkSize);
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

    public void Received(long length, DateTimeOffset now, TimeSpan lifetime)
    {
        ReceivedBytes += length;
        UpdatedAt = now;
        ExpiresAt = now + lifetime;
    }

    public void EnsureComplete()
    {
        if (!IsComplete)
            throw new DomainException(ErrorCode.VideoUploadIncomplete, SizeBytes - ReceivedBytes);
    }
}

public interface IMatchVideoStore
{
    Task<MatchVideo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchVideo>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchVideo>> GetBySquadsAsync(IEnumerable<Guid> squadIds, CancellationToken cancellationToken = default);
    void Add(MatchVideo video);

    // Cola de compresión: el subido hace más tiempo que siga esperando y no haya agotado los intentos.
    Task<MatchVideo?> GetNextPendingAsync(int maxAttempts, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchVideo>> GetProcessingAsync(CancellationToken cancellationToken = default);

    // Leído de la base en el momento, sin pasar por lo que ya esté cargado.
    Task<bool> IsDeletedAsync(Guid id, CancellationToken cancellationToken = default);
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
