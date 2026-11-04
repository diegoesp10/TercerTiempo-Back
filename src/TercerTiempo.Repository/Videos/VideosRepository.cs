using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Videos;

namespace TercerTiempo.Repository.Videos;

public interface IVideosRepository
{
    Task<VideoTicketDto> IssueTicketAsync(Guid videoId, bool download, CancellationToken cancellationToken = default);
    Task<VideoStreamDto> OpenStreamAsync(Guid videoId, string ticket, bool download, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default);
}

internal sealed class VideosRepository(
    ICommandHandler<IssueVideoTicketCommand, VideoTicketDto> issueTicket,
    IQueryHandler<OpenVideoStreamQuery, VideoStreamDto> openStream,
    ICommandHandler<DeleteVideoCommand, bool> delete) : IVideosRepository
{
    public Task<VideoTicketDto> IssueTicketAsync(Guid videoId, bool download, CancellationToken cancellationToken = default) =>
        issueTicket.Handle(new IssueVideoTicketCommand(videoId, download), cancellationToken);

    public Task<VideoStreamDto> OpenStreamAsync(Guid videoId, string ticket, bool download, CancellationToken cancellationToken = default) =>
        openStream.Handle(new OpenVideoStreamQuery(videoId, ticket, download), cancellationToken);

    public Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteVideoCommand(videoId), cancellationToken);
}
