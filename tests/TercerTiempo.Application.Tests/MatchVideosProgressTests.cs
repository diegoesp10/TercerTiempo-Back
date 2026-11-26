using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

public class MatchVideosProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);

    private static MatchVideo Video(Guid matchId) =>
        MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), matchId, Guid.NewGuid(), Guid.NewGuid(), VideoPart.Full,
            "Partido", "partido.webm", "video/webm", 1_000_000, "original.webm", Now);

    [Fact]
    public async Task Only_the_requested_videos_of_that_match_are_reported_and_deleted_ones_are_left_out()
    {
        var matchId = Guid.NewGuid();
        var compressing = Video(matchId);
        compressing.StartProcessing(Now);
        compressing.ReportProgress(40);
        var queued = Video(matchId);
        var deleted = Video(matchId);
        deleted.Delete(Now);
        var otherMatch = Video(Guid.NewGuid());
        var notAsked = Video(matchId);
        var store = new FakeVideoStore();
        store.Items.AddRange([compressing, queued, deleted, otherMatch, notAsked]);

        var result = await new GetMatchVideosProgressHandler(store).Handle(
            new GetMatchVideosProgressQuery(matchId, [compressing.Id, queued.Id, deleted.Id, otherMatch.Id, compressing.Id]), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Id == compressing.Id && r.Status == VideoStatus.Processing && r.Progress == 40);
        Assert.Contains(result, r => r.Id == queued.Id && r.Status == VideoStatus.Uploaded && r.Progress == 0);
    }

    [Fact]
    public async Task Without_ids_nothing_is_reported()
    {
        var result = await new GetMatchVideosProgressHandler(new FakeVideoStore())
            .Handle(new GetMatchVideosProgressQuery(Guid.NewGuid(), []), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task At_most_twenty_videos_are_asked_for()
    {
        var matchId = Guid.NewGuid();
        var store = new FakeVideoStore();
        store.Items.AddRange(Enumerable.Range(0, 25).Select(_ => Video(matchId)));

        var result = await new GetMatchVideosProgressHandler(store)
            .Handle(new GetMatchVideosProgressQuery(matchId, store.Items.Select(v => v.Id).ToList()), CancellationToken.None);

        Assert.Equal(GetMatchVideosProgressHandler.MaxVideos, result.Count);
    }
}
