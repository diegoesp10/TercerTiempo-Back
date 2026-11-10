using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Passes;

namespace TercerTiempo.Repository.Passes;

public interface IPassesRepository
{
    Task<MatchPassesDto> ListMatchPassesAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchPassDto> CreateMatchPassAsync(Guid squadId, Guid matchId, CreateMatchPassRequest request, CancellationToken cancellationToken = default);
    Task<MatchPassesDto> RevokeMatchPassAsync(Guid squadId, Guid matchId, Guid passId, CancellationToken cancellationToken = default);
    Task<PassPreviewDto> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<MyPassDto> RedeemAsync(string code, CancellationToken cancellationToken = default);
    Task<MyPassesDto> ListMineAsync(CancellationToken cancellationToken = default);
    Task<MyPassRequestDto> CreateRequestAsync(PassAccessRequest request, CancellationToken cancellationToken = default);
    Task<bool> CancelRequestAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubPassRequestDto>> ListClubRequestsAsync(CancellationToken cancellationToken = default);
    Task<ClubPassRequestDto> ApproveRequestAsync(Guid requestId, ApprovePassRequest request, CancellationToken cancellationToken = default);
    Task<ClubPassRequestDto> RejectRequestAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<ClubPassPricesDto> GetClubPricesAsync(CancellationToken cancellationToken = default);
    Task<ClubPassPricesDto> SetClubPricesAsync(ClubPassPricesRequest request, CancellationToken cancellationToken = default);
    Task<MatchPassPriceDto> SetMatchPriceAsync(Guid squadId, Guid matchId, MatchPassPriceRequest request, CancellationToken cancellationToken = default);
    Task<PassEarningsDto> GetClubEarningsAsync(CancellationToken cancellationToken = default);
    Task<PlatformPassEarningsDto> GetPlatformEarningsAsync(CancellationToken cancellationToken = default);
}

public sealed record CreateMatchPassRequest(
    [property: Description("El pase abre el acta interna del partido.")]
    bool IncludesSheet,
    [property: Description("El pase abre los vídeos del partido.")]
    bool IncludesVideo,
    [property: Description("Además de verlos, puede descargar los archivos (solo con vídeo).")]
    bool AllowsDownload,
    [property: Description("Precio en céntimos; si no se indica, el del club para este partido. 0 = gratis.")]
    int? PriceCents,
    [property: Description("Para quién es (nota interna del club); no limita quién lo canjea.")]
    string? Recipient);

public sealed record PassAccessRequest(
    Guid SquadId,
    Guid MatchId,
    bool WantsSheet,
    bool WantsVideo,
    [property: Description("Mensaje opcional para el club (máximo 300 caracteres).")]
    string? Message);

public sealed record ApprovePassRequest(
    bool IncludesSheet,
    bool IncludesVideo,
    bool AllowsDownload,
    [property: Description("Precio en céntimos; si no se indica, el del club para este partido.")]
    int? PriceCents);

public sealed record ClubPassPricesRequest(
    [property: Description("Precio por defecto del acta interna de un partido, en céntimos (0 = gratis).")]
    int SheetPriceCents,
    [property: Description("Precio por defecto de los vídeos de un partido, en céntimos (0 = gratis).")]
    int VideoPriceCents);

public sealed record MatchPassPriceRequest(
    [property: Description("Precio del acta de este partido en céntimos; null = el del club.")]
    int? SheetPriceCents,
    [property: Description("Precio del vídeo de este partido en céntimos; null = el del club.")]
    int? VideoPriceCents);

internal sealed class PassesRepository(
    IQueryHandler<ListMatchPassesQuery, MatchPassesDto> listMatchPasses,
    ICommandHandler<CreateMatchPassCommand, MatchPassDto> createMatchPass,
    ICommandHandler<RevokeMatchPassCommand, MatchPassesDto> revokeMatchPass,
    IQueryHandler<GetPassByCodeQuery, PassPreviewDto> getByCode,
    ICommandHandler<RedeemPassCommand, MyPassDto> redeem,
    IQueryHandler<ListMyPassesQuery, MyPassesDto> listMine,
    ICommandHandler<CreatePassRequestCommand, MyPassRequestDto> createRequest,
    ICommandHandler<CancelPassRequestCommand, bool> cancelRequest,
    IQueryHandler<ListClubPassRequestsQuery, IReadOnlyList<ClubPassRequestDto>> listClubRequests,
    ICommandHandler<ApprovePassRequestCommand, ClubPassRequestDto> approveRequest,
    ICommandHandler<RejectPassRequestCommand, ClubPassRequestDto> rejectRequest,
    IQueryHandler<GetClubPassPricesQuery, ClubPassPricesDto> getClubPrices,
    ICommandHandler<SetClubPassPricesCommand, ClubPassPricesDto> setClubPrices,
    ICommandHandler<SetMatchPassPriceCommand, MatchPassPriceDto> setMatchPrice,
    IQueryHandler<GetClubPassEarningsQuery, PassEarningsDto> getClubEarnings,
    IQueryHandler<GetPlatformPassEarningsQuery, PlatformPassEarningsDto> getPlatformEarnings) : IPassesRepository
{
    public Task<MatchPassesDto> ListMatchPassesAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        listMatchPasses.Handle(new ListMatchPassesQuery(squadId, matchId), cancellationToken);

    public Task<MatchPassDto> CreateMatchPassAsync(Guid squadId, Guid matchId, CreateMatchPassRequest request, CancellationToken cancellationToken = default) =>
        createMatchPass.Handle(new CreateMatchPassCommand(
            squadId, matchId, request.IncludesSheet, request.IncludesVideo, request.AllowsDownload, request.PriceCents, request.Recipient), cancellationToken);

    public Task<MatchPassesDto> RevokeMatchPassAsync(Guid squadId, Guid matchId, Guid passId, CancellationToken cancellationToken = default) =>
        revokeMatchPass.Handle(new RevokeMatchPassCommand(squadId, matchId, passId), cancellationToken);

    public Task<PassPreviewDto> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        getByCode.Handle(new GetPassByCodeQuery(code), cancellationToken);

    public Task<MyPassDto> RedeemAsync(string code, CancellationToken cancellationToken = default) =>
        redeem.Handle(new RedeemPassCommand(code), cancellationToken);

    public Task<MyPassesDto> ListMineAsync(CancellationToken cancellationToken = default) =>
        listMine.Handle(new ListMyPassesQuery(), cancellationToken);

    public Task<MyPassRequestDto> CreateRequestAsync(PassAccessRequest request, CancellationToken cancellationToken = default) =>
        createRequest.Handle(new CreatePassRequestCommand(request.SquadId, request.MatchId, request.WantsSheet, request.WantsVideo, request.Message), cancellationToken);

    public Task<bool> CancelRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        cancelRequest.Handle(new CancelPassRequestCommand(requestId), cancellationToken);

    public Task<IReadOnlyList<ClubPassRequestDto>> ListClubRequestsAsync(CancellationToken cancellationToken = default) =>
        listClubRequests.Handle(new ListClubPassRequestsQuery(), cancellationToken);

    public Task<ClubPassRequestDto> ApproveRequestAsync(Guid requestId, ApprovePassRequest request, CancellationToken cancellationToken = default) =>
        approveRequest.Handle(new ApprovePassRequestCommand(
            requestId, request.IncludesSheet, request.IncludesVideo, request.AllowsDownload, request.PriceCents), cancellationToken);

    public Task<ClubPassRequestDto> RejectRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        rejectRequest.Handle(new RejectPassRequestCommand(requestId), cancellationToken);

    public Task<ClubPassPricesDto> GetClubPricesAsync(CancellationToken cancellationToken = default) =>
        getClubPrices.Handle(new GetClubPassPricesQuery(), cancellationToken);

    public Task<ClubPassPricesDto> SetClubPricesAsync(ClubPassPricesRequest request, CancellationToken cancellationToken = default) =>
        setClubPrices.Handle(new SetClubPassPricesCommand(request.SheetPriceCents, request.VideoPriceCents), cancellationToken);

    public Task<MatchPassPriceDto> SetMatchPriceAsync(Guid squadId, Guid matchId, MatchPassPriceRequest request, CancellationToken cancellationToken = default) =>
        setMatchPrice.Handle(new SetMatchPassPriceCommand(squadId, matchId, request.SheetPriceCents, request.VideoPriceCents), cancellationToken);

    public Task<PassEarningsDto> GetClubEarningsAsync(CancellationToken cancellationToken = default) =>
        getClubEarnings.Handle(new GetClubPassEarningsQuery(), cancellationToken);

    public Task<PlatformPassEarningsDto> GetPlatformEarningsAsync(CancellationToken cancellationToken = default) =>
        getPlatformEarnings.Handle(new GetPlatformPassEarningsQuery(), cancellationToken);
}
