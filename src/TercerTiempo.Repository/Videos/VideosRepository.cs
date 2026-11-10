using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Repository.Videos;

// Lo que atiende el servicio de vídeo: subida (de una vez o por trozos), tickets, reproducción, descarga y borrado.
public interface IVideosRepository
{
    Task<VideoTicketDto> IssueTicketAsync(Guid videoId, bool download, CancellationToken cancellationToken = default);
    Task<VideoStreamDto> OpenStreamAsync(Guid videoId, string ticket, bool download, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default);

    Task<MatchVideoDto> UploadAsync(Guid squadId, Guid matchId, VideoPart part, string? title, VideoUpload file, CancellationToken cancellationToken = default);
    Task<VideoUploadDto> BeginUploadAsync(Guid squadId, Guid matchId, BeginVideoUploadRequest request, CancellationToken cancellationToken = default);
    Task<VideoUploadDto> GetUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default);
    Task<VideoUploadDto> AppendChunkAsync(Guid squadId, Guid uploadId, int chunkIndex, long length, Stream content, CancellationToken cancellationToken = default);
    Task<MatchVideoDto> CompleteUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default);
    Task<bool> CancelUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default);
}

internal sealed class VideosRepository(
    ICommandHandler<IssueVideoTicketCommand, VideoTicketDto> issueTicket,
    IQueryHandler<OpenVideoStreamQuery, VideoStreamDto> openStream,
    ICommandHandler<DeleteVideoCommand, bool> delete,
    ICommandHandler<UploadMatchVideoCommand, MatchVideoDto> upload,
    ICommandHandler<BeginVideoUploadCommand, VideoUploadDto> beginUpload,
    IQueryHandler<GetVideoUploadQuery, VideoUploadDto> getUpload,
    ICommandHandler<AppendVideoChunkCommand, VideoUploadDto> appendChunk,
    ICommandHandler<CompleteVideoUploadCommand, MatchVideoDto> completeUpload,
    ICommandHandler<CancelVideoUploadCommand, bool> cancelUpload) : IVideosRepository
{
    public Task<VideoTicketDto> IssueTicketAsync(Guid videoId, bool download, CancellationToken cancellationToken = default) =>
        issueTicket.Handle(new IssueVideoTicketCommand(videoId, download), cancellationToken);

    public Task<VideoStreamDto> OpenStreamAsync(Guid videoId, string ticket, bool download, CancellationToken cancellationToken = default) =>
        openStream.Handle(new OpenVideoStreamQuery(videoId, ticket, download), cancellationToken);

    public Task<bool> DeleteAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteVideoCommand(videoId), cancellationToken);

    public Task<MatchVideoDto> UploadAsync(Guid squadId, Guid matchId, VideoPart part, string? title, VideoUpload file, CancellationToken cancellationToken = default) =>
        upload.Handle(new UploadMatchVideoCommand(squadId, matchId, part, title, file), cancellationToken);

    public Task<VideoUploadDto> BeginUploadAsync(Guid squadId, Guid matchId, BeginVideoUploadRequest request, CancellationToken cancellationToken = default) =>
        beginUpload.Handle(request.ToCommand(squadId, matchId), cancellationToken);

    public Task<VideoUploadDto> GetUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default) =>
        getUpload.Handle(new GetVideoUploadQuery(squadId, uploadId), cancellationToken);

    public Task<VideoUploadDto> AppendChunkAsync(Guid squadId, Guid uploadId, int chunkIndex, long length, Stream content, CancellationToken cancellationToken = default) =>
        appendChunk.Handle(new AppendVideoChunkCommand(squadId, uploadId, chunkIndex, length, content), cancellationToken);

    public Task<MatchVideoDto> CompleteUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default) =>
        completeUpload.Handle(new CompleteVideoUploadCommand(squadId, uploadId), cancellationToken);

    public Task<bool> CancelUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default) =>
        cancelUpload.Handle(new CancelVideoUploadCommand(squadId, uploadId), cancellationToken);
}
