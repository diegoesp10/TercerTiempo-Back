using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Videos;

namespace TercerTiempo.Repository.Videos;

/// <summary>Acceso a los vídeos de partido: compra, tickets de reproducción, streaming y borrado.</summary>
public interface IVideosRepository
{
    Task<MatchVideoDto> PurchaseAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<VideoTicketDto> IssueTicketAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<VideoStreamDto> OpenStreamAsync(string ticket, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default);
}

internal sealed class VideosRepository(
    ICommandHandler<PurchaseVideoCommand, MatchVideoDto> purchase,
    ICommandHandler<IssueVideoTicketCommand, VideoTicketDto> issueTicket,
    IQueryHandler<OpenVideoStreamQuery, VideoStreamDto> openStream,
    ICommandHandler<DeleteVideoCommand, bool> delete) : IVideosRepository
{
    public Task<MatchVideoDto> PurchaseAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        purchase.Handle(new PurchaseVideoCommand(videoId), cancellationToken);

    public Task<VideoTicketDto> IssueTicketAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        issueTicket.Handle(new IssueVideoTicketCommand(videoId), cancellationToken);

    public Task<VideoStreamDto> OpenStreamAsync(string ticket, CancellationToken cancellationToken = default) =>
        openStream.Handle(new OpenVideoStreamQuery(ticket), cancellationToken);

    public Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteVideoCommand(videoId), cancellationToken);
}
