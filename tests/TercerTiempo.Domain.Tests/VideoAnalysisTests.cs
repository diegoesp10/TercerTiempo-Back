using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Domain.Tests;

public class VideoAnalysisTests
{
    private static MatchVideo Video() => MatchVideo.Upload(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), VideoPart.Full,
        "Partido", "partido.mp4", "video/mp4", 2048, "file.mp4", TestData.Now);

    private static MatchVideo ReadyVideo()
    {
        var video = Video();
        video.StartProcessing(TestData.Now);
        video.KeepOriginal(5782, 1080, TestData.Now);
        return video;
    }

    private static VideoAnalysisOutcome Outcome(decimal ball = 62.5m, decimal calibrated = 81m) =>
        new("analysis/abc", "0.1.0", "balon: afinados/balon.pt", """{"equipos":[],"jugadores":[]}""", 1500, ball, calibrated, 300, 100m, 62m);

    [Fact]
    public void Only_a_video_whose_file_will_not_change_is_analysed()
    {
        var uploaded = Video();
        Assert.False(MatchVideoAnalysis.CanAnalyze(uploaded));
        Assert.Equal(ErrorCode.VideoAnalysisVideoNotReady, Assert.Throws<DomainException>(() => MatchVideoAnalysis.Queue(uploaded, TestData.Now)).Code);

        uploaded.StartProcessing(TestData.Now);
        Assert.False(MatchVideoAnalysis.CanAnalyze(uploaded));

        // Con la compresión fallida se sigue sirviendo el original, que ya no cambia.
        uploaded.FailProcessing("ffmpeg devolvió 1", TestData.Now);
        Assert.True(MatchVideoAnalysis.CanAnalyze(uploaded));

        var ready = ReadyVideo();
        var analysis = MatchVideoAnalysis.Queue(ready, TestData.Now);
        Assert.Equal((ready.Id, ready.SquadId, ready.MatchId, VideoAnalysisStatus.Pending, 0), (analysis.VideoId, analysis.SquadId, analysis.MatchId, analysis.Status, analysis.Progress));

        ready.Delete(TestData.Now);
        Assert.False(MatchVideoAnalysis.CanAnalyze(ready));
        Assert.Equal(ErrorCode.VideoAlreadyDeleted, Assert.Throws<DomainException>(() => MatchVideoAnalysis.Queue(ready, TestData.Now)).Code);
    }

    [Fact]
    public void A_finished_analysis_keeps_the_proposal_and_how_much_it_can_be_trusted()
    {
        var analysis = MatchVideoAnalysis.Queue(ReadyVideo(), TestData.Now);
        Assert.Equal(ErrorCode.VideoAnalysisNotRunning, Assert.Throws<DomainException>(() => analysis.ReportProgress(10)).Code);

        analysis.Start(TestData.Now);
        analysis.ReportProgress(140);
        Assert.Equal((VideoAnalysisStatus.Running, 99, 1), (analysis.Status, analysis.Progress, analysis.Attempts));

        analysis.Complete(Outcome(), TestData.Now.AddMinutes(5));

        Assert.Equal((VideoAnalysisStatus.Ready, 100), (analysis.Status, analysis.Progress));
        Assert.Equal(("analysis/abc", "0.1.0", 1500, 62.5m, 81m, 300), (analysis.OutputPath, analysis.AnalyzerVersion, analysis.Frames, analysis.BallFramesPct, analysis.CalibratedFramesPct, analysis.AnalyzedSeconds));
        Assert.Equal((100m, 62m), (analysis.PitchLengthM, analysis.PitchWidthM));
        Assert.False(analysis.LowQuality);
        Assert.False(analysis.IsActive);
        Assert.Equal(ErrorCode.VideoAnalysisNotPending, Assert.Throws<DomainException>(() => analysis.Start(TestData.Now)).Code);
    }

    [Theory]
    [InlineData(19.3, 67.1, true)]
    [InlineData(60, 40, true)]
    [InlineData(35, 50, false)]
    public void Little_ball_or_little_calibration_marks_the_proposal_as_low_quality(double ball, double calibrated, bool low)
    {
        var analysis = MatchVideoAnalysis.Queue(ReadyVideo(), TestData.Now);
        analysis.Start(TestData.Now);
        analysis.Complete(Outcome((decimal)ball, (decimal)calibrated), TestData.Now);

        Assert.Equal(low, analysis.LowQuality);
    }

    [Fact]
    public void A_failed_analysis_says_why_and_can_be_asked_again()
    {
        var video = ReadyVideo();
        var analysis = MatchVideoAnalysis.Queue(video, TestData.Now);
        analysis.Start(TestData.Now);

        analysis.Fail(new string('x', 2000), TestData.Now);

        Assert.Equal(VideoAnalysisStatus.Failed, analysis.Status);
        Assert.Equal(MatchVideoAnalysis.MaxErrorLength, analysis.Error!.Length);
        Assert.Null(analysis.ProposalJson);

        analysis.Restart(video, TestData.Now.AddHours(1));
        Assert.Equal((VideoAnalysisStatus.Pending, 0, null), (analysis.Status, analysis.Attempts, analysis.Error));
        Assert.Equal(ErrorCode.VideoAnalysisAlreadyQueued, Assert.Throws<DomainException>(() => analysis.Restart(video, TestData.Now)).Code);
    }

    [Fact]
    public void Asking_again_forgets_the_previous_proposal_and_what_was_applied()
    {
        var video = ReadyVideo();
        var analysis = MatchVideoAnalysis.Queue(video, TestData.Now);
        analysis.Start(TestData.Now);
        analysis.Complete(Outcome(), TestData.Now);
        analysis.MarkApplied(1, """{"696":"00000000-0000-0000-0000-000000000001"}""", TestData.Now);
        Assert.Equal(1, analysis.OurTeam);
        Assert.NotNull(analysis.AppliedAt);

        analysis.Restart(video, TestData.Now.AddHours(1));

        Assert.Equal(VideoAnalysisStatus.Pending, analysis.Status);
        Assert.Null(analysis.ProposalJson);
        Assert.Null(analysis.BallFramesPct);
        Assert.Null(analysis.OurTeam);
        Assert.Null(analysis.AssignmentsJson);
        Assert.Null(analysis.AppliedAt);
    }

    [Fact]
    public void An_interrupted_analysis_goes_back_to_the_queue_without_counting_the_attempt()
    {
        var analysis = MatchVideoAnalysis.Queue(ReadyVideo(), TestData.Now);
        analysis.Start(TestData.Now);
        analysis.ReportProgress(40);

        analysis.Requeue();

        Assert.Equal((VideoAnalysisStatus.Pending, 0, 0), (analysis.Status, analysis.Attempts, analysis.Progress));
    }

    [Fact]
    public void Only_a_ready_proposal_is_applied_and_the_team_is_one_of_the_two_detected()
    {
        var analysis = MatchVideoAnalysis.Queue(ReadyVideo(), TestData.Now);
        Assert.Equal(ErrorCode.VideoAnalysisNotReady, Assert.Throws<DomainException>(() => analysis.MarkApplied(0, "{}", TestData.Now)).Code);

        analysis.Start(TestData.Now);
        analysis.Complete(Outcome(), TestData.Now);
        Assert.Equal(ErrorCode.VideoAnalysisTeamInvalid, Assert.Throws<DomainException>(() => analysis.MarkApplied(2, "{}", TestData.Now)).Code);
    }
}

public class TeamPitchTests
{
    [Fact]
    public void The_club_notes_the_pitch_measures_and_the_federation_profile_does_not_erase_them()
    {
        var team = Team.Create("CD Vallobín", "VAL", null);
        Assert.True(team.Profile.IsEmpty);

        team.SetPitch(98.46m, 62m);

        Assert.Equal((98.5m, 62m), (team.Profile.PitchLengthM, team.Profile.PitchWidthM));
        Assert.True(team.Profile.HasPitch);
        Assert.False(team.Profile.IsEmpty);

        team.UpdateProfile(new TeamProfile { Ground = "El Vallobín", GroundSurface = "Hierba artificial" }, TestData.Now);

        Assert.Equal(("El Vallobín", 98.5m, 62m), (team.Profile.Ground, team.Profile.PitchLengthM, team.Profile.PitchWidthM));

        team.SetPitch(null, null);
        Assert.False(team.Profile.HasPitch);
        Assert.Equal("Hierba artificial", team.Profile.GroundSurface);
    }

    [Theory]
    [InlineData(100, null)]
    [InlineData(null, 62)]
    [InlineData(59, 62)]
    [InlineData(131, 62)]
    [InlineData(100, 34)]
    [InlineData(100, 91)]
    public void Pitch_measures_go_together_and_within_what_a_football_pitch_can_be(int? length, int? width)
    {
        var team = Team.Create("CD Vallobín", "VAL", null);

        Assert.Throws<DomainException>(() => team.SetPitch(length, width));
        Assert.False(team.Profile.HasPitch);
    }
}

public class TeamGoalKicksTests
{
    [Fact]
    public void Goal_kicks_are_part_of_the_team_data()
    {
        var (squad, coachId) = TestData.NewSquad();
        var match = TestData.PlayedMatch();
        var data = new TeamMatchStatsData(55, 6, 3, 12, 7, 5, 2, 2, 1, 11, 14, 1, 0, null) { GoalKicksFor = 8, GoalKicksAgainst = 11 };

        var stats = TeamMatchStats.Record(squad, match, SquadEditor.Coach(coachId), data, TestData.Now);

        Assert.Equal((8, 11), (stats.GoalKicksFor, stats.GoalKicksAgainst));
        Assert.Throws<DomainException>(() => stats.Update(squad, match, SquadEditor.Coach(coachId), data with { GoalKicksFor = -1 }, TestData.Now));
    }
}
