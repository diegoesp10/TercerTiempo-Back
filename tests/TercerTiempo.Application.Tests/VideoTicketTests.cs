using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Infrastructure.Videos;

namespace TercerTiempo.Application.Tests;

public class VideoTicketTests
{
    [Fact]
    public async Task Playback_ticket_does_not_download_but_a_download_ticket_does()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tickets = new MemoryVideoTickets(cache, Options.Create(new VideoOptions()), TimeProvider.System);
        var video = MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            VideoPart.Full, null, "video.webm", "video/webm", 2048, "video.webm", DateTimeOffset.UtcNow);
        var videos = Proxy<IMatchVideoStore>((_, _) => Task.FromResult<MatchVideo?>(video));
        var storage = Proxy<IVideoStorage>((_, _) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var handler = new OpenVideoStreamHandler(tickets, videos, storage);

        var playback = tickets.Issue(video.Id, Guid.NewGuid()).Ticket;
        var stream = await handler.Handle(new(video.Id, playback), default);
        await stream.Content.DisposeAsync();
        var denied = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(video.Id, playback, Download: true), default));
        Assert.Equal(ErrorCode.VideoDownloadDenied, denied.Code);

        var download = tickets.Issue(video.Id, Guid.NewGuid(), download: true).Ticket;
        var file = await handler.Handle(new(video.Id, download, Download: true), default);
        Assert.Equal("video.webm", file.FileName);
        await file.Content.DisposeAsync();
    }

    [Fact]
    public async Task Ticket_cannot_be_used_on_another_video_route()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tickets = new MemoryVideoTickets(cache, Options.Create(new VideoOptions()), TimeProvider.System);
        var ticket = tickets.Issue(Guid.NewGuid(), Guid.NewGuid()).Ticket;
        var handler = new OpenVideoStreamHandler(tickets, null!, null!);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(Guid.NewGuid(), ticket), default));
    }

    private static T Proxy<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
