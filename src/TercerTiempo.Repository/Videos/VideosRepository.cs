using TercerTiempo.Application.Abstractions.Messaging;
using System.ComponentModel;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Repository.Videos;

public interface IVideosRepository
{
    Task<MatchVideoDto> PurchaseAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<VideoTicketDto> IssueTicketAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<VideoStreamDto> OpenStreamAsync(Guid videoId, string ticket, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<MatchVideoDto> SetVisibilityAsync(Guid videoId, VideoVisibility visibility, CancellationToken cancellationToken = default);
    Task<VideoShareLinkDto> CreateShareLinkAsync(Guid videoId, int? expiresInHours, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoShareLinkDto>> ListShareLinksAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoShareLinkDto>> RevokeShareLinkAsync(Guid videoId, Guid linkId, CancellationToken cancellationToken = default);
    Task<SharedVideoDto> GetSharedAsync(string token, CancellationToken cancellationToken = default);
    Task<VideoStreamDto> OpenSharedStreamAsync(string token, CancellationToken cancellationToken = default);
}

public sealed record VideoVisibilityRequest(
    [property: Description("Private (plantilla y compradores) o Public (cualquiera que vea el partido, sin pagar).")]
    VideoVisibility Visibility);

public sealed record VideoShareRequest(
    [property: Description("Horas de validez; por defecto 24, máximo Videos:MaxShareHours.")]
    int? ExpiresInHours);

internal sealed class VideosRepository(
    ICommandHandler<PurchaseVideoCommand, MatchVideoDto> purchase,
    ICommandHandler<IssueVideoTicketCommand, VideoTicketDto> issueTicket,
    IQueryHandler<OpenVideoStreamQuery, VideoStreamDto> openStream,
    ICommandHandler<DeleteVideoCommand, bool> delete,
    ICommandHandler<SetVideoVisibilityCommand, MatchVideoDto> setVisibility,
    ICommandHandler<CreateVideoShareLinkCommand, VideoShareLinkDto> createShare,
    IQueryHandler<ListVideoShareLinksQuery, IReadOnlyList<VideoShareLinkDto>> listShares,
    ICommandHandler<RevokeVideoShareLinkCommand, IReadOnlyList<VideoShareLinkDto>> revokeShare,
    IQueryHandler<GetSharedVideoQuery, SharedVideoDto> getShared,
    IQueryHandler<OpenSharedVideoStreamQuery, VideoStreamDto> openShared) : IVideosRepository
{
    public Task<MatchVideoDto> PurchaseAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        purchase.Handle(new PurchaseVideoCommand(videoId), cancellationToken);

    public Task<VideoTicketDto> IssueTicketAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        issueTicket.Handle(new IssueVideoTicketCommand(videoId), cancellationToken);

    public Task<VideoStreamDto> OpenStreamAsync(Guid videoId, string ticket, CancellationToken cancellationToken = default) =>
        openStream.Handle(new OpenVideoStreamQuery(videoId, ticket), cancellationToken);

    public Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteVideoCommand(videoId), cancellationToken);

    public Task<MatchVideoDto> SetVisibilityAsync(Guid videoId, VideoVisibility visibility, CancellationToken cancellationToken = default) =>
        setVisibility.Handle(new SetVideoVisibilityCommand(videoId, visibility), cancellationToken);

    public Task<VideoShareLinkDto> CreateShareLinkAsync(Guid videoId, int? expiresInHours, CancellationToken cancellationToken = default) =>
        createShare.Handle(new CreateVideoShareLinkCommand(videoId, expiresInHours), cancellationToken);

    public Task<IReadOnlyList<VideoShareLinkDto>> ListShareLinksAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        listShares.Handle(new ListVideoShareLinksQuery(videoId), cancellationToken);

    public Task<IReadOnlyList<VideoShareLinkDto>> RevokeShareLinkAsync(Guid videoId, Guid linkId, CancellationToken cancellationToken = default) =>
        revokeShare.Handle(new RevokeVideoShareLinkCommand(videoId, linkId), cancellationToken);

    public Task<SharedVideoDto> GetSharedAsync(string token, CancellationToken cancellationToken = default) =>
        getShared.Handle(new GetSharedVideoQuery(token), cancellationToken);

    public Task<VideoStreamDto> OpenSharedStreamAsync(string token, CancellationToken cancellationToken = default) =>
        openShared.Handle(new OpenSharedVideoStreamQuery(token), cancellationToken);
}
