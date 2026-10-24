using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Domain.Tests;

public class VideoTests
{
    private static MatchVideo Video(VideoPart part = VideoPart.Full) => MatchVideo.Upload(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), part,
        "Partido", "partido.mp4", "video/mp4", 2048, "file.mp4", 500, TestData.Now);

    [Fact]
    public void Club_can_be_the_uploader_and_share_link_creator()
    {
        var clubId = Guid.NewGuid();
        var video = MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, VideoPart.Full,
            null, "partido.mp4", "video/mp4", 2048, "file.mp4", 500, TestData.Now, clubId);
        Assert.Null(video.UploadedByCoachId);
        Assert.Equal(clubId, video.UploadedByClubId);
        var link = VideoShareLink.Create(video.Id, null, new string('a', 48), TestData.Now, TimeSpan.FromDays(1), clubId);
        Assert.Equal(clubId, link.CreatedByClubId);
        Assert.Null(link.CreatedByCoachId);
        Assert.True(link.IsValid(TestData.Now.AddHours(23)));
        Assert.False(link.IsValid(TestData.Now.AddDays(1).AddSeconds(1)));
        link.Revoke(TestData.Now.AddHours(1));
        Assert.False(link.IsValid(TestData.Now.AddHours(2)));
    }

    [Fact]
    public void Video_requires_exactly_one_responsible_account()
    {
        Assert.Throws<DomainException>(() => VideoShareLink.Create(Guid.NewGuid(), null, "token", TestData.Now, TimeSpan.FromDays(1)));
        Assert.Throws<DomainException>(() => VideoShareLink.Create(Guid.NewGuid(), Guid.NewGuid(), "token", TestData.Now, TimeSpan.FromDays(1), Guid.NewGuid()));
    }

    [Fact]
    public void Video_parts_cannot_be_duplicated_or_mixed_with_a_full_match()
    {
        var full = Video();
        Assert.Equal(ErrorCode.VideoPartMix, Assert.Throws<DomainException>(() => MatchVideo.EnsurePartAvailable([full], VideoPart.FirstHalf)).Code);
        var first = Video(VideoPart.FirstHalf);
        Assert.Equal(ErrorCode.VideoPartTaken, Assert.Throws<DomainException>(() => MatchVideo.EnsurePartAvailable([first], VideoPart.FirstHalf)).Code);
        MatchVideo.EnsurePartAvailable([first], VideoPart.SecondHalf);
        first.Delete(TestData.Now);
        MatchVideo.EnsurePartAvailable([first], VideoPart.Full);
    }

    [Fact]
    public void Upload_requires_ordered_chunks_and_rejects_incomplete_or_expired_uploads()
    {
        const int chunkSize = 1024 * 1024;
        var session = VideoUploadSession.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), VideoPart.FirstHalf,
            null, "partido.mp4", "video/mp4", chunkSize + 1024, chunkSize, "uploads/test.part", TestData.Now, TimeSpan.FromDays(1));
        Assert.Throws<DomainException>(() => session.EnsureAccepts(1, chunkSize, TestData.Now));
        Assert.Throws<DomainException>(() => session.EnsureAccepts(0, 1, TestData.Now));
        session.EnsureAccepts(0, chunkSize, TestData.Now);
        session.Received(chunkSize, TestData.Now);
        Assert.Throws<DomainException>(session.EnsureComplete);
        session.EnsureAccepts(1, 1024, TestData.Now);
        session.Received(1024, TestData.Now);
        session.EnsureComplete();
        Assert.True(session.IsComplete);
        Assert.Throws<DomainException>(() => session.EnsureAccepts(session.NextChunk, 1024, TestData.Now.AddDays(2)));
    }
}
