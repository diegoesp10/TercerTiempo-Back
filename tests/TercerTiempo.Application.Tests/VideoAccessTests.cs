using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Passes;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

// Un vídeo nunca es público: hacer pública la plantilla solo deja ver que existe para pedirlo; lo abre un pase del partido.
public class VideoAccessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly Squad _squad = Squad.Open(Guid.NewGuid(), Guid.NewGuid(), Now);
    private readonly MatchVideo _video;

    public VideoAccessTests() =>
        _video = MatchVideo.Upload(_squad.Id, _squad.TeamId, Guid.NewGuid(), _squad.SeasonId, null, VideoPart.Full, null,
            "partido.mp4", "video/mp4", 50_000_000, "clave", Now, Guid.NewGuid());

    [Fact]
    public void A_stranger_does_not_even_see_a_video_of_a_private_squad()
    {
        Assert.Equal(VideoAccessLevel.None, VideoAccess.LevelFor(_video, _squad, new Stranger(), MatchInsight.None, TimeProvider.System));
    }

    [Fact]
    public void A_public_squad_only_lets_strangers_ask_for_the_video()
    {
        _squad.SetVisibility(SquadVisibility.Public);
        var level = VideoAccess.LevelFor(_video, _squad, new Stranger(), MatchInsight.None, TimeProvider.System);

        Assert.Equal(VideoAccessLevel.Locked, level);
        Assert.False(VideoAccess.CanWatch(level));
        Assert.False(VideoAccess.CanDownload(level, manager: false, MatchInsight.None));
    }

    [Fact]
    public void A_pass_opens_the_video_and_only_a_download_pass_lets_download_it()
    {
        var watchOnly = new MatchInsight(Sheet: false, Video: true, Download: false);
        var level = VideoAccess.LevelFor(_video, _squad, new Stranger(), watchOnly, TimeProvider.System);
        Assert.Equal(VideoAccessLevel.Pass, level);
        Assert.True(VideoAccess.CanWatch(level));
        Assert.False(VideoAccess.CanDownload(level, manager: false, watchOnly));

        var withDownload = watchOnly with { Download = true };
        Assert.True(VideoAccess.CanDownload(VideoAccess.LevelFor(_video, _squad, new Stranger(), withDownload, TimeProvider.System), manager: false, withDownload));

        var sheetOnly = new MatchInsight(Sheet: true, Video: false, Download: false);
        Assert.Equal(VideoAccessLevel.None, VideoAccess.LevelFor(_video, _squad, new Stranger(), sheetOnly, TimeProvider.System));
    }

    [Fact]
    public void The_owning_club_watches_and_downloads()
    {
        var level = VideoAccess.LevelFor(_video, _squad, new Stranger(), MatchInsight.None, TimeProvider.System, clubOwner: true);
        Assert.Equal(VideoAccessLevel.Owner, level);
        Assert.True(VideoAccess.CanDownload(level, manager: true, MatchInsight.None));
    }

    [Fact]
    public void A_deleted_video_is_hidden_even_with_a_pass()
    {
        _video.Delete(Now);
        Assert.Equal(VideoAccessLevel.None, VideoAccess.LevelFor(_video, _squad, new Stranger(), new MatchInsight(true, true, true), TimeProvider.System));
    }

    private sealed record Stranger : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public AccountType? AccountType => Abstractions.Identity.AccountType.Coach;
        public Guid? PlayerId => null;
        public Guid? CoachId { get; } = Guid.NewGuid();
        public Guid? ClubId => null;
    }
}
