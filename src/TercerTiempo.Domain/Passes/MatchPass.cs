using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Passes;

public enum MatchPassStatus
{
    Pending = 1,
    Redeemed = 2,
    Revoked = 3,
    Expired = 4,
}

// Pase de un partido: abre a una persona lo interno que una plantilla tiene de ese partido (el acta interna, el vídeo y,
// si el club lo permite, la descarga del archivo). Lo da el club o su cuerpo técnico, se canjea una sola vez dentro de la
// app y queda ligado a la cuenta que lo canjea; si tiene precio, se paga al canjearlo. Nada interno se publica para todos.
public sealed class MatchPass : AggregateRoot
{
    public const int CodeLength = 12;
    public const int MaxPriceCents = 100_000;

    public Guid SquadId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid MatchId { get; private set; }

    // El club propietario: cobra su parte de las ventas.
    public Guid ClubId { get; private set; }

    public string Code { get; private set; } = string.Empty;
    public bool IncludesSheet { get; private set; }
    public bool IncludesVideo { get; private set; }
    public bool AllowsDownload { get; private set; }
    public int PriceCents { get; private set; }
    public string Currency { get; private set; } = "EUR";

    // Para quién es, como nota del club («Analista del Llanes»).
    public string? Recipient { get; private set; }

    // Si sale de una solicitud, solo puede canjearlo quien la hizo.
    public Guid? ForUserId { get; private set; }
    public Guid? RequestId { get; private set; }

    public Guid? CreatedByCoachId { get; private set; }
    public Guid? CreatedByClubId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Hasta cuándo se puede canjear; una vez canjeado, el acceso no caduca.
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? RedeemedByUserId { get; private set; }

    // Nombre de la cuenta que lo canjeó, tal como era entonces, para la lista del club.
    public string? RedeemedByName { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRedeemed => RedeemedByUserId is not null;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsFree => PriceCents == 0;

    private MatchPass() { }

    public static MatchPass Create(
        Guid squadId, Guid teamId, Guid matchId, Guid clubId, bool includesSheet, bool includesVideo, bool allowsDownload,
        int priceCents, string currency, string? recipient, Guid? createdByCoachId, Guid? createdByClubId,
        DateTimeOffset now, TimeSpan redeemWindow, Guid? forUserId = null, Guid? requestId = null)
    {
        if (!includesSheet && !includesVideo)
            throw new DomainException(ErrorCode.PassEmpty);

        if (allowsDownload && !includesVideo)
            throw new DomainException(ErrorCode.PassDownloadNeedsVideo);

        if ((createdByCoachId is null) == (createdByClubId is null))
            throw new DomainException(ErrorCode.VideoManagerRequired);

        return new MatchPass
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
            Code = InviteCodes.Generate(CodeLength),
            IncludesSheet = includesSheet,
            IncludesVideo = includesVideo,
            AllowsDownload = allowsDownload,
            PriceCents = Guard.InRange(priceCents, 0, MaxPriceCents, "precio"),
            Currency = Guard.NotEmpty(currency, "moneda", 3),
            Recipient = string.IsNullOrWhiteSpace(recipient) ? null : Guard.NotEmpty(recipient, "destinatario", 100),
            ForUserId = forUserId,
            RequestId = requestId,
            CreatedByCoachId = createdByCoachId,
            CreatedByClubId = createdByClubId,
            CreatedAt = now,
            ExpiresAt = now + redeemWindow,
        };
    }

    public MatchPassStatus StatusOn(DateTimeOffset now) =>
        IsRevoked ? MatchPassStatus.Revoked
        : IsRedeemed ? MatchPassStatus.Redeemed
        : now > ExpiresAt ? MatchPassStatus.Expired
        : MatchPassStatus.Pending;

    public void Redeem(Guid userId, string userName, DateTimeOffset now)
    {
        Guard.NotEmpty(userId, nameof(userId));

        if (IsRevoked)
            throw new DomainException(ErrorCode.PassRevoked);

        if (IsRedeemed)
            throw new DomainException(RedeemedByUserId == userId ? ErrorCode.PassAlreadyYours : ErrorCode.PassAlreadyRedeemed);

        if (now > ExpiresAt)
            throw new DomainException(ErrorCode.PassExpired);

        if (ForUserId is { } intended && intended != userId)
            throw new DomainException(ErrorCode.PassNotForYou);

        RedeemedByUserId = userId;
        RedeemedByName = Guard.NotEmpty(userName, "nombre", 150);
        RedeemedAt = now;
    }

    // Si el cobro falla después de reservar el pase, vuelve a quedar libre para quien lo tenía.
    public void ReleaseRedemption()
    {
        RedeemedByUserId = null;
        RedeemedByName = null;
        RedeemedAt = null;
    }

    // Uno ya pagado no se retira: quien lo compró conserva el acceso.
    public void Revoke(DateTimeOffset now)
    {
        if (IsRedeemed && !IsFree)
            throw new DomainException(ErrorCode.PassPaidCannotRevoke);

        RevokedAt ??= now;
    }

    public bool Grants(Guid userId) => !IsRevoked && RedeemedByUserId == userId;
}

public enum MatchPassRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

// Solicitud de acceso a lo interno de un partido: la hace cualquier cuenta de la app sobre una plantilla que abre sus
// actas a solicitudes, y la decide el club propietario (al aprobarla genera el pase para esa persona).
public sealed class MatchPassRequest : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid ClubId { get; private set; }
    public Guid RequesterUserId { get; private set; }
    public string RequesterName { get; private set; } = string.Empty;
    public bool WantsSheet { get; private set; }
    public bool WantsVideo { get; private set; }
    public string? Message { get; private set; }
    public MatchPassRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? PassId { get; private set; }

    public bool IsPending => Status == MatchPassRequestStatus.Pending;

    private MatchPassRequest() { }

    public static MatchPassRequest Create(
        Guid squadId, Guid teamId, Guid matchId, Guid clubId, Guid requesterUserId, string requesterName,
        bool wantsSheet, bool wantsVideo, string? message, DateTimeOffset now)
    {
        if (!wantsSheet && !wantsVideo)
            throw new DomainException(ErrorCode.PassEmpty);

        return new MatchPassRequest
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
            RequesterUserId = Guard.NotEmpty(requesterUserId, nameof(requesterUserId)),
            RequesterName = Guard.NotEmpty(requesterName, "nombre", 150),
            WantsSheet = wantsSheet,
            WantsVideo = wantsVideo,
            Message = string.IsNullOrWhiteSpace(message) ? null : Guard.NotEmpty(message, "mensaje", 300),
            Status = MatchPassRequestStatus.Pending,
            CreatedAt = now,
        };
    }

    public void Approve(Guid passId, DateTimeOffset now)
    {
        EnsurePending();
        PassId = Guard.NotEmpty(passId, nameof(passId));
        Status = MatchPassRequestStatus.Approved;
        DecidedAt = now;
    }

    public void Reject(DateTimeOffset now)
    {
        EnsurePending();
        Status = MatchPassRequestStatus.Rejected;
        DecidedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsurePending();
        Status = MatchPassRequestStatus.Cancelled;
        DecidedAt = now;
    }

    private void EnsurePending()
    {
        if (!IsPending)
            throw new DomainException(ErrorCode.PassRequestClosed);
    }
}

// Precio que el club pone a un partido concreto; lo que quede vacío toma el precio general de su perfil.
public sealed class MatchPassPrice : AggregateRoot
{
    public Guid ClubId { get; private set; }
    public Guid SquadId { get; private set; }
    public Guid MatchId { get; private set; }
    public int? SheetPriceCents { get; private set; }
    public int? VideoPriceCents { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsEmpty => SheetPriceCents is null && VideoPriceCents is null;

    private MatchPassPrice() { }

    public static MatchPassPrice Create(Guid clubId, Guid squadId, Guid matchId, DateTimeOffset now) =>
        new()
        {
            Id = NewId(),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            UpdatedAt = now,
        };

    public void Set(int? sheetPriceCents, int? videoPriceCents, DateTimeOffset now)
    {
        SheetPriceCents = sheetPriceCents is { } sheet ? Guard.InRange(sheet, 0, MatchPass.MaxPriceCents, "precio del acta") : null;
        VideoPriceCents = videoPriceCents is { } video ? Guard.InRange(video, 0, MatchPass.MaxPriceCents, "precio del vídeo") : null;
        UpdatedAt = now;
    }
}

// Cobro de un pase con su reparto: el porcentaje del club se guarda en cada venta, así cambiarlo no reescribe el pasado.
public sealed class PassSale : AggregateRoot
{
    public Guid PassId { get; private set; }
    public Guid ClubId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid BuyerUserId { get; private set; }
    public int AmountCents { get; private set; }
    public int ClubSharePercent { get; private set; }
    public int ClubShareCents { get; private set; }
    public int PlatformShareCents { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public string Provider { get; private set; } = string.Empty;
    public string PaymentReference { get; private set; } = string.Empty;
    public DateTimeOffset PaidAt { get; private set; }

    private PassSale() { }

    public static PassSale Record(
        MatchPass pass, Guid buyerUserId, int clubSharePercent, string provider, string paymentReference, DateTimeOffset now)
    {
        var percent = Guard.InRange(clubSharePercent, 0, 100, "porcentaje del club");
        var clubShare = (int)Math.Round(pass.PriceCents * percent / 100m, MidpointRounding.AwayFromZero);

        return new PassSale
        {
            Id = NewId(),
            PassId = pass.Id,
            ClubId = pass.ClubId,
            MatchId = pass.MatchId,
            BuyerUserId = Guard.NotEmpty(buyerUserId, nameof(buyerUserId)),
            AmountCents = pass.PriceCents,
            ClubSharePercent = percent,
            ClubShareCents = clubShare,
            PlatformShareCents = pass.PriceCents - clubShare,
            Currency = pass.Currency,
            Provider = Guard.NotEmpty(provider, "proveedor", 30),
            PaymentReference = Guard.NotEmpty(paymentReference, "referencia de pago", 200),
            PaidAt = now,
        };
    }
}

public interface IMatchPassStore
{
    Task<MatchPass?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MatchPass?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchPass>> GetBySquadMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchPass>> GetRedeemedByAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchPass>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    void Add(MatchPass pass);
}

public interface IMatchPassRequestStore
{
    Task<MatchPassRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchPassRequest>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchPassRequest>> GetByRequesterAsync(Guid userId, CancellationToken cancellationToken = default);
    void Add(MatchPassRequest request);
}

public interface IMatchPassPriceStore
{
    Task<MatchPassPrice?> GetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchPassPrice>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    void Add(MatchPassPrice price);
    void Remove(MatchPassPrice price);
}

public interface IPassSaleStore
{
    Task<IReadOnlyList<PassSale>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PassSale>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(PassSale sale);
}
