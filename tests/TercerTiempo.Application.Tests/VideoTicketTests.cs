using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Infrastructure.Videos;

namespace TercerTiempo.Application.Tests;

public class VideoTicketTests
{
    [Fact]
    public async Task Public_ticket_stops_working_when_the_video_becomes_private()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tickets = new MemoryVideoTickets(cache, Options.Create(new VideoOptions()), TimeProvider.System);
        var video = MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            VideoPart.Full, null, "video.webm", "video/webm", 2048, "video.webm", 500, DateTimeOffset.UtcNow);
        video.SetVisibility(VideoVisibility.Public);
        var ticket = tickets.Issue(video.Id, Guid.Empty, requiresPublicVideo: true).Ticket;
        var videos = Proxy<IMatchVideoStore>((_, _) => Task.FromResult<MatchVideo?>(video));
        var storage = Proxy<IVideoStorage>((_, _) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var handler = new OpenVideoStreamHandler(tickets, videos, storage);
        var stream = await handler.Handle(new(video.Id, ticket), default);
        await stream.Content.DisposeAsync();
        video.SetVisibility(VideoVisibility.Private);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(video.Id, ticket), default));
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
