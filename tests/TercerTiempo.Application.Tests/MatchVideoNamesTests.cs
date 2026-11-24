using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

public sealed class MatchVideoNamesTests
{
    private static readonly DateTimeOffset Kickoff = new(2026, 11, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_video_is_named_after_the_match_and_its_date()
    {
        Assert.Equal("CD Vallobín vs Real Titánico 01-11-2026", MatchVideoNames.Title("CD Vallobín", "Real Titánico", Kickoff, VideoPart.Full));
        Assert.Equal("CD Vallobín vs Real Titánico 01-11-2026 (1ª parte)", MatchVideoNames.Title("CD Vallobín", "Real Titánico", Kickoff, VideoPart.FirstHalf));
        Assert.Equal("CD Vallobín vs Real Titánico 01-11-2026 (2ª parte)", MatchVideoNames.Title("CD Vallobín", "Real Titánico", Kickoff, VideoPart.SecondHalf));
    }

    [Fact]
    public void The_date_is_the_local_one_in_Spain()
    {
        // 23:30 UTC del 1 de noviembre son las 00:30 del día 2 en Madrid.
        var lateKickoff = new DateTimeOffset(2026, 11, 1, 23, 30, 0, TimeSpan.Zero);
        Assert.EndsWith(" 02-11-2026", MatchVideoNames.Title("Local", "Visitante", lateKickoff, VideoPart.Full));
    }

    [Fact]
    public void Long_team_names_are_shortened_but_the_date_and_part_are_kept()
    {
        var title = MatchVideoNames.Title(new string('A', 120), new string('B', 120), Kickoff, VideoPart.SecondHalf);

        Assert.Equal(MatchVideo.MaxTitleLength, title.Length);
        Assert.EndsWith(" 01-11-2026 (2ª parte)", title);
    }

    [Fact]
    public void The_file_keeps_the_real_extension_and_drops_what_file_systems_reject()
    {
        Assert.Equal("C.D. Vallobín vs R. Titánico 01-11-2026.mp4",
            MatchVideoNames.FileName("C.D. Vallobín vs R. Titánico 01-11-2026", "DRAY-PARTIDOS-C-D-VALLOBIN-0-R-TITANICO-1.MP4", "video/mp4"));
        Assert.Equal("Oviedo B - Avilés vs Lealtad 01-11-2026.webm",
            MatchVideoNames.FileName("Oviedo B / Avilés vs Lealtad 01-11-2026", "grabacion", "video/webm"));
        Assert.Equal("Local vs Visitante 01-11-2026.mp4", MatchVideoNames.FileName("Local vs Visitante 01-11-2026", null, "video/desconocido"));
    }

    [Fact]
    public void A_video_is_renamed_only_when_its_name_changes()
    {
        var video = MatchVideo.Upload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), VideoPart.Full,
            "Partido", "partido.mp4", "video/mp4", 2048, "file.mp4", Kickoff);

        Assert.True(video.Rename("Local vs Visitante 01-11-2026", "Local vs Visitante 01-11-2026.mp4"));
        Assert.False(video.Rename("Local vs Visitante 01-11-2026", "Local vs Visitante 01-11-2026.mp4"));
        Assert.Equal(("Local vs Visitante 01-11-2026", "Local vs Visitante 01-11-2026.mp4"), (video.Title, video.FileName));
    }
}
