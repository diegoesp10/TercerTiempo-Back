using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Domain.Tests;

public class VideoTests
{
    private static MatchVideo Video(VideoPart part = VideoPart.Full) => MatchVideo.Upload(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), part,
        "Partido", "partido.mp4", "video/mp4", 2048, "file.mp4", TestData.Now);

    [Fact]
    public void Club_can_be_the_uploader()
    {
        var clubId = Guid.NewGuid();
        var video = MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, VideoPart.Full,
            null, "partido.mp4", "video/mp4", 2048, "file.mp4", TestData.Now, clubId);
        Assert.Null(video.UploadedByCoachId);
        Assert.Equal(clubId, video.UploadedByClubId);
    }

    [Fact]
    public void Video_requires_exactly_one_responsible_account()
    {
        Assert.Throws<DomainException>(() => MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            VideoPart.Full, null, "partido.mp4", "video/mp4", 2048, "file.mp4", TestData.Now));
        Assert.Throws<DomainException>(() => MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            VideoPart.Full, null, "partido.mp4", "video/mp4", 2048, "file.mp4", TestData.Now, Guid.NewGuid()));
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
        session.Received(chunkSize, TestData.Now, TimeSpan.FromDays(1));
        Assert.Throws<DomainException>(session.EnsureComplete);
        session.EnsureAccepts(1, 1024, TestData.Now);
        session.Received(1024, TestData.Now, TimeSpan.FromDays(1));
        session.EnsureComplete();
        Assert.True(session.IsComplete);
        Assert.Throws<DomainException>(() => session.EnsureAccepts(session.NextChunk, 1024, TestData.Now.AddDays(2)));
    }

    [Fact]
    public void Upload_session_stays_alive_while_chunks_keep_arriving()
    {
        const int chunkSize = 1024 * 1024;
        var lifetime = TimeSpan.FromHours(24);
        var session = VideoUploadSession.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), VideoPart.Full,
            null, "partido.mp4", "video/mp4", chunkSize * 3L, chunkSize, "uploads/test.part", TestData.Now, lifetime);

        // Una subida lenta de un partido entero puede durar más que la caducidad inicial: cada trozo la renueva.
        for (var i = 0; i < 3; i++)
        {
            var at = TestData.Now.AddHours(20 * (i + 1));
            session.EnsureAccepts(i, chunkSize, at);
            session.Received(chunkSize, at, lifetime);
        }

        Assert.True(session.IsComplete);
        Assert.Equal(TestData.Now.AddHours(60) + lifetime, session.ExpiresAt);
    }

    [Fact]
    public void Finished_upload_reports_no_chunk_left_even_with_a_short_last_chunk()
    {
        const int chunkSize = 1024 * 1024;
        const long size = chunkSize * 2L + 12345;
        var session = VideoUploadSession.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), VideoPart.Full,
            null, "partido.mp4", "video/mp4", size, chunkSize, "uploads/test.part", TestData.Now, TimeSpan.FromHours(24));

        session.Received(chunkSize, TestData.Now, TimeSpan.FromHours(24));
        session.Received(chunkSize, TestData.Now, TimeSpan.FromHours(24));
        Assert.Equal(2, session.NextChunk);

        session.Received(12345, TestData.Now, TimeSpan.FromHours(24));
        Assert.True(session.IsComplete);
        Assert.Equal(session.TotalChunks, session.NextChunk);
        Assert.Throws<DomainException>(() => session.EnsureAccepts(2, 0, TestData.Now));
    }
}
