using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Squads.TeamStats;
using TercerTiempo.Application.Videos.Analysis;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Infrastructure.Videos;

namespace TercerTiempo.Application.Tests;

public class VideoProposalTests
{
    [Fact]
    public void The_analyzer_file_is_read_from_both_sides_with_one_line_per_tracked_player()
    {
        var proposal = VideoProposal.Parse(VideoAnalysisSamples.Proposal);

        Assert.Equal([0, 1], proposal.Teams.Select(t => t.Index));
        var grey = proposal.Team(0)!;
        Assert.Equal("#5a5a57", grey.Color);
        Assert.Equal((38, 4, 2, 3, 11, 6), (grey.PossessionPct, grey.Shots, grey.ShotsOnTarget, grey.Corners, grey.ThrowIns, grey.GoalKicks));
        Assert.Equal(62, proposal.Team(1)!.PossessionPct);

        Assert.Equal([696, 781, 12], proposal.Players.Select(p => p.Tracklet));
        var striker = proposal.Players[0];
        Assert.Equal((0, false, "recortes/tracklet-696.jpg", 14, 950), (striker.Team, striker.Goalkeeper, striker.CropFile, striker.MinutesVisible, striker.DistanceM));
        // Pases completados e intentados (los fallados se suman a los intentados).
        Assert.Equal(new VideoPlayerActions(3, 2, 1, 0, 4, 5, 18, 23, 31), striker.Actions);
        Assert.True(proposal.Players[2].Goalkeeper);
        Assert.Equal(7, proposal.Players[2].ShirtNumber);
    }

    [Fact]
    public void A_broken_or_partial_file_never_gives_impossible_numbers()
    {
        var proposal = VideoProposal.Parse("""
            { "equipos": [ { "id": 0, "color": "rojo", "teamStats": { "possessionPct": 140, "shotsFor": 2, "shotsOnTargetFor": 5, "cornersFor": -3 } }, { "id": 7 } ],
              "jugadores": [ { "tracklet": 5, "equipo": 0, "propuesta": { "shots": 1, "shotsOnTarget": 4, "passes": null } },
                             { "tracklet": 5, "equipo": 1 }, { "tracklet": 9, "equipo": null }, { "tracklet": 10, "equipo": 3 } ] }
            """);

        var team = Assert.Single(proposal.Teams);
        Assert.Null(team.Color);
        Assert.Equal((100, 2, 2, 0), (team.PossessionPct, team.Shots, team.ShotsOnTarget, team.Corners));
        var player = Assert.Single(proposal.Players);
        Assert.Equal((1, 1, 0), (player.Actions.Shots, player.Actions.ShotsOnTarget, player.Actions.PassesAttempted));
        Assert.Empty(VideoProposal.Parse("{}").Players);
    }
}

public class VideoSheetMergeTests
{
    private static readonly TeamMatchStatsInput Empty = TeamMatchStatsInput.From(null);
    private static readonly VideoTeamField[] AllFields = Enum.GetValues<VideoTeamField>();

    [Fact]
    public void Team_data_is_taken_from_the_side_the_staff_says_is_theirs()
    {
        var proposal = VideoProposal.Parse(VideoAnalysisSamples.Proposal);

        var asGrey = VideoSheetMerge.Totals([new VideoPartSelection(proposal, 0, 300)]);
        var asWhite = VideoSheetMerge.Totals([new VideoPartSelection(proposal, 1, 300)]);

        Assert.Equal(new VideoTeamTotals(38, 4, 9, 2, 5, 3, 1, 11, 14, 6, 4), asGrey);
        Assert.Equal(new VideoTeamTotals(62, 9, 4, 5, 2, 1, 3, 14, 11, 4, 6), asWhite);
    }

    [Fact]
    public void Two_halves_add_up_and_possession_is_weighted_by_what_was_analysed()
    {
        var first = VideoProposal.Parse(VideoAnalysisSamples.Proposal);
        var second = VideoProposal.Parse(VideoAnalysisSamples.Proposal.Replace("\"possessionPct\": 38", "\"possessionPct\": 60"));

        // En la segunda parte los equipos cambian de campo, pero el analizador puede numerarlos igual o al revés: lo dice el técnico.
        var totals = VideoSheetMerge.Totals([new VideoPartSelection(first, 0, 2700), new VideoPartSelection(second, 0, 900)]);

        Assert.Equal(44, totals.PossessionPct);
        Assert.Equal((8, 18, 6, 2), (totals.ShotsFor, totals.ShotsAgainst, totals.CornersFor, totals.CornersAgainst));
    }

    [Fact]
    public void Only_the_accepted_fields_are_written_and_an_empty_sheet_is_never_a_conflict()
    {
        var video = new VideoTeamTotals(38, 4, 9, 2, 5, 3, 1, 11, 14, 6, 4);
        var overwritten = new List<string>();
        var written = 0;

        var merged = VideoSheetMerge.MergeTeam(Empty, video, [VideoTeamField.Possession, VideoTeamField.Corners, VideoTeamField.GoalKicks], overwritten, ref written);

        Assert.Equal((38, 3, 1, 6, 4), (merged.PossessionPct, merged.CornersFor, merged.CornersAgainst, merged.GoalKicksFor, merged.GoalKicksAgainst));
        Assert.Equal((0, 0, 0), (merged.ShotsFor, merged.ShotsOnTargetFor, merged.ThrowInsFor));
        Assert.Empty(overwritten);
        Assert.Equal(5, written);
    }

    [Fact]
    public void What_the_staff_already_wrote_is_reported_before_it_is_replaced_and_a_zero_never_erases_it()
    {
        var current = Empty with { PossessionPct = 55, ShotsFor = 7, ShotsAgainst = 9, CornersFor = 2, FoulsCommitted = 12, Notes = "Presión alta" };
        var video = new VideoTeamTotals(38, 4, 9, 2, 5, 0, 1, 11, 14, 6, 4);
        var overwritten = new List<string>();
        var written = 0;

        var merged = VideoSheetMerge.MergeTeam(current, video, AllFields, overwritten, ref written);

        Assert.Equal(["posesión (55 → 38)", "tiros (7 → 4)"], overwritten);
        Assert.Equal((38, 4, 9), (merged.PossessionPct, merged.ShotsFor, merged.ShotsAgainst));
        // El vídeo no vio córners nuestros: se queda lo apuntado. Lo que no sale del vídeo ni se toca.
        Assert.Equal(2, merged.CornersFor);
        Assert.Equal((12, "Presión alta"), (merged.FoulsCommitted, merged.Notes));
    }

    [Fact]
    public void A_player_without_line_gets_one_with_the_minutes_he_was_seen()
    {
        var id = Guid.NewGuid();
        var overwritten = new List<string>();

        var line = VideoSheetMerge.MergeLine(id, "Pelayo", null, new VideoPlayerActions(3, 2, 1, 0, 4, 5, 18, 23, 31), 14, overwritten)!;

        Assert.Equal((true, false, 14), (line.Played, line.Started, line.MinutesPlayed));
        Assert.Equal((3, 2, 1, 4, 5, 18, 23), (line.Shots, line.ShotsOnTarget, line.Goals, line.Recoveries, line.BallLosses, line.PassesCompleted, line.PassesAttempted));
        Assert.Empty(overwritten);
        line.ToStats().EnsureValid();
    }

    [Fact]
    public void An_existing_line_keeps_what_the_video_does_not_know_and_reports_what_changes()
    {
        var id = Guid.NewGuid();
        var current = new MatchSheetEntryInput(id, true, true, 90, Goals: 2, Assists: 1, Shots: 5, ShotsOnTarget: 3, KeyPasses: 2, Tackles: 4,
            Interceptions: 1, Saves: 0, YellowCards: 1, RedCard: false, Notes: "Muy activo", ShirtNumber: 9, Fouls: 2);
        var overwritten = new List<string>();

        var line = VideoSheetMerge.MergeLine(id, "Pelayo", current, new VideoPlayerActions(3, 1, 0, 0, 4, 0, 18, 23, 31), 14, overwritten)!;

        // Los goles no se tocan (el vídeo no vio ninguno), los tiros van con sus tiros a puerta y los minutos son los del acta.
        Assert.Equal((90, true, 2, 1, 3, 1), (line.MinutesPlayed, line.Started, line.Goals, line.Assists, line.Shots, line.ShotsOnTarget));
        Assert.Equal((4, 18, 23, 4, 1, "Muy activo", 9), (line.Recoveries, line.PassesCompleted, line.PassesAttempted, line.Tackles, line.YellowCards, line.Notes, line.ShirtNumber));
        Assert.Equal(["tiros de Pelayo (5 → 3)", "tiros a puerta de Pelayo (3 → 1)"], overwritten);
        line.ToStats().EnsureValid();
    }

    [Fact]
    public void Goals_from_the_video_only_fill_a_line_without_goals()
    {
        var id = Guid.NewGuid();
        var scorer = new MatchSheetEntryInput(id, true, true, 90, Goals: 2, 0, 0, 0, 0, 0, 0, 0, 0, false, null);
        var overwritten = new List<string>();

        var kept = VideoSheetMerge.MergeLine(id, "Pelayo", scorer, VideoPlayerActions.None with { Goals = 1, Recoveries = 3 }, 9, overwritten)!;
        var filled = VideoSheetMerge.MergeLine(id, "Pelayo", scorer with { Goals = 0 }, VideoPlayerActions.None with { Goals = 1 }, 9, overwritten)!;

        Assert.Equal((2, 3), (kept.Goals, kept.Recoveries));
        Assert.Equal(1, filled.Goals);
        Assert.Empty(overwritten);
    }

    [Fact]
    public void Nothing_is_written_for_a_player_the_video_saw_without_actions_or_with_the_same_ones()
    {
        var id = Guid.NewGuid();
        var same = new MatchSheetEntryInput(id, true, true, 90, 0, 0, 3, 2, 0, 0, 0, 0, 0, false, null);

        Assert.Null(VideoSheetMerge.MergeLine(id, "Pelayo", null, VideoPlayerActions.None with { Touches = 6 }, 9, []));
        Assert.Null(VideoSheetMerge.MergeLine(id, "Pelayo", same, VideoPlayerActions.None with { Shots = 3, ShotsOnTarget = 2 }, 9, []));
    }
}

public class UvVideoAnalyzerTests
{
    [Fact]
    public void The_analyzer_is_launched_with_uv_from_its_folder_with_teams_pitch_and_progress()
    {
        var request = new VideoAnalysisRequest(Guid.NewGuid(), "clave.mp4", "CD Vallobín", "Real Titánico, S.A.D.", 98.5m, 62m);

        var args = UvVideoAnalyzer.Arguments(@"C:\videos\clave.mp4", @"C:\videos\analysis\abc", request, new VideoAnalysisOptions()).ToList();

        Assert.Equal(["run", "tt-analisis", "analizar", @"C:\videos\clave.mp4"], args.Take(4));
        Assert.Equal(@"C:\videos\analysis\abc", args[args.IndexOf("--salida") + 1]);
        Assert.Equal("CD Vallobín,Real Titánico  S.A.D.", args[args.IndexOf("--equipos") + 1]);
        Assert.Equal("98.5x62", args[args.IndexOf("--campo") + 1]);
        Assert.Equal("movil", args[args.IndexOf("--camara") + 1]);
        Assert.Contains("--sin-clips", args);
        Assert.Contains("--progreso", args);
        Assert.Contains("--exigir-gpu", args);
        Assert.DoesNotContain("--segmento", args);
    }

    [Fact]
    public void Without_pitch_measures_the_analyzer_uses_its_own_and_a_segment_can_be_set_for_testing()
    {
        var request = new VideoAnalysisRequest(Guid.NewGuid(), "clave.mp4", "A", "B", null, null);

        var args = UvVideoAnalyzer.Arguments("v.mp4", "out", request, new VideoAnalysisOptions { Segment = " 20:00-25:00 ", RequireGpu = false, Camera = "fija" }).ToList();

        Assert.DoesNotContain("--campo", args);
        Assert.DoesNotContain("--exigir-gpu", args);
        Assert.Equal("20:00-25:00", args[args.IndexOf("--segmento") + 1]);
        Assert.Equal("fija", args[args.IndexOf("--camara") + 1]);
    }

    [Theory]
    [InlineData("##progreso 42", 42)]
    [InlineData("##progreso 100", 100)]
    [InlineData("##progreso 180", 100)]
    [InlineData("##progreso", null)]
    [InlineData("Vídeo: 1920×1080 a 50 fps", null)]
    [InlineData(" ##progreso 3", null)]
    public void Progress_lines_give_the_percentage(string line, int? expected) =>
        Assert.Equal(expected, UvVideoAnalyzer.ProgressPercent(line));

    [Fact]
    public void The_output_folder_gives_proposal_quality_weights_and_the_crop_of_each_player()
    {
        var folder = Directory.CreateTempSubdirectory("tt-analisis-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "acta-propuesta.json"), VideoAnalysisSamples.Proposal);
            File.WriteAllText(Path.Combine(folder, "analisis.json"), VideoAnalysisSamples.Analysis);
            Directory.CreateDirectory(Path.Combine(folder, "recortes"));
            File.WriteAllBytes(Path.Combine(folder, "recortes", "tracklet-696.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
            File.WriteAllBytes(Path.Combine(folder, "recortes", "tracklet-12.jpg"), []);

            var run = UvVideoAnalyzer.ReadRun(folder, "analysis/abc");

            Assert.Equal("analysis/abc", run.OutputPath);
            Assert.Equal("0.1.0", run.AnalyzerVersion);
            Assert.Equal("jugadores: football-player-detection.pt · balon: afinados/balon.pt · campo: football-pitch-detection.pt", run.Weights);
            Assert.Equal((1500, 19.3m, 67.1m, 300), (run.Frames, run.BallFramesPct, run.CalibratedFramesPct, run.AnalyzedSeconds));
            Assert.Equal((100m, 62m), (run.PitchLengthM, run.PitchWidthM));
            // Solo los recortes que existen y no están vacíos; el JSON se guarda compacto.
            Assert.Equal([696], run.Crops.Keys);
            Assert.DoesNotContain('\n', run.ProposalJson);
            Assert.Equal(3, VideoProposal.Parse(run.ProposalJson).Players.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_analyzer_that_ends_without_its_files_is_a_failure()
    {
        var folder = Directory.CreateTempSubdirectory("tt-analisis-").FullName;
        try
        {
            Assert.Throws<VideoAnalysisException>(() => UvVideoAnalyzer.ReadRun(folder, "analysis/abc"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class VideoAnalysisProcessorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_ready_video_is_queued_once_and_only_where_the_analysis_is_enabled()
    {
        var world = new AnalysisWorld();
        var video = world.ReadyVideo();

        Assert.True(await world.Queue.EnqueueAsync(video, default));
        Assert.False(await world.Queue.EnqueueAsync(video, default));

        var analysis = Assert.Single(world.Analyses.Items);
        Assert.Equal((video.Id, video.SquadId, video.MatchId, VideoAnalysisStatus.Pending), (analysis.VideoId, analysis.SquadId, analysis.MatchId, analysis.Status));
        Assert.Equal(1, world.Signal.Wakes);

        var disabled = new AnalysisWorld(enabled: false);
        Assert.False(await disabled.Queue.EnqueueAsync(disabled.ReadyVideo(), default));
        Assert.Empty(disabled.Analyses.Items);
    }

    [Fact]
    public async Task A_video_still_being_compressed_is_not_queued()
    {
        var world = new AnalysisWorld();
        var video = world.Upload();
        video.StartProcessing(Start);

        Assert.False(await world.Queue.EnqueueAsync(video, default));
        Assert.Empty(world.Analyses.Items);
    }

    [Fact]
    public async Task The_analysis_leaves_the_proposal_its_quality_and_the_crops_and_is_logged_as_its_own_job()
    {
        var world = new AnalysisWorld();
        var video = world.ReadyVideo();
        world.Home.SetPitch(98.5m, 62m);
        await world.Queue.EnqueueAsync(video, default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();

        var analysis = await world.Processor.ClaimNextAsync(default);
        Assert.Equal(VideoAnalysisStatus.Running, analysis!.Status);
        await world.Processor.ProcessAsync(analysis, default);

        Assert.Equal(VideoAnalysisStatus.Ready, analysis.Status);
        Assert.Equal(100, analysis.Progress);
        Assert.Equal(VideoAnalysisSamples.Run().ProposalJson, analysis.ProposalJson);
        Assert.Equal((19.3m, 67.1m, 1500, "0.1.0"), (analysis.BallFramesPct, analysis.CalibratedFramesPct, analysis.Frames, analysis.AnalyzerVersion));
        Assert.True(analysis.LowQuality);
        Assert.Equal([696, 781], world.Analyses.Crops[analysis.Id].Select(c => c.Tracklet));

        // El analizador recibe nuestro equipo primero y las medidas del campo del local.
        var request = world.Analyzer.Request!;
        Assert.Equal(("CD Vallobín", "Real Titánico", 98.5m, 62m, "comprimido.mp4"), (request.OurTeam, request.RivalTeam, request.PitchLengthM, request.PitchWidthM, request.StorageKey));

        Assert.Equal(JobNames.VideoAnalysis, world.Jobs.StartedJob);
        Assert.True(world.Jobs.Finished!.Value.Succeeded);
        Assert.Contains("CD Vallobín - Real Titánico (partido completo)", world.Jobs.Finished!.Value.Summary);
        Assert.Contains("19,3 %", world.Jobs.Finished!.Value.Summary);
        Assert.Null(await world.Processor.ClaimNextAsync(default));
    }

    [Fact]
    public async Task Away_from_home_the_pitch_is_the_one_of_the_home_team()
    {
        var world = new AnalysisWorld(away: true);
        var video = world.ReadyVideo();
        await world.Queue.EnqueueAsync(video, default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();

        await world.Processor.ProcessAsync((await world.Processor.ClaimNextAsync(default))!, default);

        // Nuestro equipo es el visitante y el local no ha apuntado las medidas de su campo.
        Assert.Equal(("Real Titánico", "CD Vallobín"), (world.Analyzer.Request!.OurTeam, world.Analyzer.Request.RivalTeam));
        Assert.Null(world.Analyzer.Request.PitchLengthM);
        Assert.Null(world.Analyzer.Request.PitchWidthM);
    }

    [Fact]
    public async Task Without_gpu_or_uv_the_analysis_fails_with_a_clear_message_and_the_video_is_untouched()
    {
        var world = new AnalysisWorld();
        var video = world.ReadyVideo();
        await world.Queue.EnqueueAsync(video, default);
        world.Analyzer.Failure = new VideoAnalysisException("Este equipo no tiene una GPU que el analizador pueda usar (torch no ve CUDA): el vídeo no se analiza.");

        var analysis = await world.Processor.ClaimNextAsync(default);
        await world.Processor.ProcessAsync(analysis!, default);

        Assert.Equal(VideoAnalysisStatus.Failed, analysis!.Status);
        Assert.StartsWith("Este equipo no tiene una GPU", analysis.Error);
        Assert.Null(analysis.ProposalJson);
        Assert.Equal((VideoStatus.Ready, "comprimido.mp4"), (video.Status, video.StorageKey));
        Assert.False(world.Jobs.Finished!.Value.Succeeded);
        Assert.StartsWith("Este equipo no tiene una GPU", world.Jobs.Finished!.Value.Error);
        // No se reintenta solo: lo vuelve a pedir el cuerpo técnico.
        Assert.Null(await world.Processor.ClaimNextAsync(default));
    }

    [Fact]
    public async Task Progress_is_saved_at_most_every_few_seconds()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();
        world.Analyzer.ProgressSteps = [10, 20, 30];
        world.Analyzer.BeforeProgress = () => world.Clock.Now += TimeSpan.FromSeconds(3);

        var analysis = await world.Processor.ClaimNextAsync(default);
        var savesBefore = world.UnitOfWork.Saves;
        await world.Processor.ProcessAsync(analysis!, default);

        // 10 % (3 s, no), 20 % (6 s, sí), 30 % (9 s, no) y el final.
        Assert.Equal(savesBefore + 2, world.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Shutdown_mid_analysis_puts_it_back_in_the_queue_and_deleting_the_video_discards_it()
    {
        var world = new AnalysisWorld();
        var video = world.ReadyVideo();
        await world.Queue.EnqueueAsync(video, default);
        using var stopping = new CancellationTokenSource();
        world.Analyzer.OnAnalyze = () => stopping.Cancel();

        var analysis = await world.Processor.ClaimNextAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Processor.ProcessAsync(analysis!, stopping.Token));

        Assert.Equal((VideoAnalysisStatus.Pending, 0), (analysis!.Status, analysis.Attempts));

        using var deleted = new CancellationTokenSource();
        world.Analyzer.OnAnalyze = () =>
        {
            world.Videos.DeletedIds.Add(video.Id);
            deleted.Cancel();
        };
        analysis = await world.Processor.ClaimNextAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Processor.ProcessAsync(analysis!, deleted.Token));

        Assert.Equal(VideoAnalysisStatus.Running, analysis!.Status);
        Assert.False(world.Jobs.Finished!.Value.Succeeded);
    }

    [Fact]
    public async Task Interrupted_analyses_are_requeued_when_the_service_starts()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        await world.Processor.ClaimNextAsync(default);

        Assert.Equal(1, await world.Processor.RequeueInterruptedAsync(default));
        Assert.Equal(VideoAnalysisStatus.Pending, world.Analyses.Items.Single().Status);
    }
}

internal static class VideoAnalysisSamples
{
    // Como lo escribe el analizador: datos de equipo desde los dos lados y una línea por jugador seguido.
    public const string Proposal = """
        {
          "version": 1,
          "equipos": [
            { "id": 0, "nombre": "CD Vallobín", "color": "#5A5A57",
              "teamStats": { "possessionPct": 38, "cornersFor": 3, "cornersAgainst": 1, "shotsFor": 4, "shotsAgainst": 9, "shotsOnTargetFor": 2,
                             "shotsOnTargetAgainst": 5, "throwInsFor": 11, "throwInsAgainst": 14, "goalKicksFor": 6, "goalsFor": 1, "goalsAgainst": 2,
                             "goalsCandidatesFor": 2, "offsidesFor": null, "foulsCommitted": null, "notes": "Propuesta automática del vídeo" } },
            { "id": 1, "nombre": "Rival", "color": "#9c8e8d",
              "teamStats": { "possessionPct": 62, "cornersFor": 1, "cornersAgainst": 3, "shotsFor": 9, "shotsAgainst": 4, "shotsOnTargetFor": 5,
                             "shotsOnTargetAgainst": 2, "throwInsFor": 14, "throwInsAgainst": 11, "goalKicksFor": 4, "goalsFor": 2, "goalsAgainst": 1,
                             "goalsCandidatesFor": 2, "offsidesFor": null, "foulsCommitted": null, "notes": "Propuesta automática del vídeo" } }
          ],
          "jugadores": [
            { "tracklet": 696, "equipo": 0, "portero": false, "recorte": "recortes\\tracklet-696.jpg", "dorsal_llm": null, "minutosALaVista": 14,
              "desde": "21:02", "hasta": "35:10", "distanciaM": 950,
              "propuesta": { "shots": 3, "shotsOnTarget": 2, "goals": 1, "saves": 0, "recoveries": 4, "ballLosses": 5, "passes": 18, "passesFailed": 5, "touches": 31 } },
            { "tracklet": 781, "equipo": 0, "portero": false, "recorte": "recortes\\tracklet-781.jpg", "dorsal_llm": null, "minutosALaVista": 6,
              "desde": "36:00", "hasta": "42:00", "distanciaM": 410,
              "propuesta": { "shots": 1, "shotsOnTarget": 0, "goals": 0, "saves": 0, "recoveries": 2, "ballLosses": 1, "passes": 7, "passesFailed": 1, "touches": 12 } },
            { "tracklet": 12, "equipo": 1, "portero": true, "recorte": "recortes\\tracklet-12.jpg", "dorsal_llm": 7, "minutosALaVista": 20,
              "desde": "20:00", "hasta": "40:00", "distanciaM": 300,
              "propuesta": { "shots": 0, "shotsOnTarget": 0, "goals": 0, "saves": 2, "recoveries": 0, "ballLosses": 0, "passes": 4, "passesFailed": 2, "touches": 9 } }
          ],
          "nuestroEquipo": null
        }
        """;

    public const string Analysis = """
        {
          "version": "0.1.0",
          "video": { "ruta": "partido.mp4", "ancho": 1920, "alto": 1080, "fps": 50.0, "duracion_s": 5782.0, "tramo_inicio_s": 1200.0, "tramo_fin_s": 1500.0 },
          "parametros": { "fps_muestreo": 5.0, "device": "cuda", "camara": "movil",
                          "modelos": { "jugadores": "football-player-detection.pt", "balon": "afinados/balon.pt", "campo": "football-pitch-detection.pt" },
                          "campo_m": [100.0, 62.0] },
          "recuento": { "fotogramas": 1500, "con_balon": 290, "pct_con_balon": 19.3, "pct_calibrados": 67.1, "tracklets": 61 }
        }
        """;

    public static VideoAnalysisRun Run() => new(
        "analysis/abc", "0.1.0", "balon: afinados/balon.pt", System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(Proposal).RootElement),
        1500, 19.3m, 67.1m, 300, 98.5m, 62m,
        new Dictionary<int, byte[]> { [696] = [0xFF, 0xD8, 0xFF, 0xD9], [781] = [0xFF, 0xD8, 0xFF, 0xD9] });
}

// Un partido del CD Vallobín con su vídeo ya comprimido y los dobles de la cola de análisis.
internal sealed class AnalysisWorld
{
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    public readonly ManualClock Clock = new() { Now = Start };
    public readonly FakeVideoStore Videos = new();
    public readonly FakeAnalysisStore Analyses = new();
    public readonly FakeAnalysisSignal Signal = new();
    public readonly FakeAnalyzer Analyzer = new();
    public readonly FakeVideoJobs Jobs = new();
    public readonly CountingUnitOfWork UnitOfWork = new();
    public readonly Team Home = Team.Create("CD Vallobín", "VAL", null);
    public readonly Team Away = Team.Create("Real Titánico", "TIT", null);
    public readonly Match Match;
    public readonly VideoAnalysisQueue Queue;
    public readonly VideoAnalysisProcessor Processor;
    private readonly bool _away;

    public AnalysisWorld(bool enabled = true, bool away = false)
    {
        _away = away;
        Match = Match.Schedule(Guid.NewGuid(), 1, Home.Id, Away.Id, Start.AddDays(-3), null, null);
        var options = Options.Create(new VideoAnalysisOptions { Enabled = enabled });
        var matches = TestStub.Of<IMatchStore>((name, _) => name == nameof(IMatchStore.GetByIdAsync) ? Task.FromResult<Match?>(Match) : throw new NotSupportedException(name));
        var teams = TestStub.Of<ITeamStore>((name, _) => name == nameof(ITeamStore.GetByIdsAsync) ? Task.FromResult<IReadOnlyList<Team>>([Home, Away]) : throw new NotSupportedException(name));
        Queue = new VideoAnalysisQueue(Analyses, options, Signal, UnitOfWork, Clock);
        Processor = new VideoAnalysisProcessor(Analyses, Videos, matches, teams, Analyzer, Jobs, options, UnitOfWork, Clock, NullLogger<VideoAnalysisProcessor>.Instance);
        Analyses.Videos = Videos;
    }

    public MatchVideo Upload()
    {
        var video = MatchVideo.Upload(Guid.NewGuid(), _away ? Away.Id : Home.Id, Match.Id, Match.SeasonId, Guid.NewGuid(), VideoPart.Full,
            "Partido", "partido.mp4", "video/mp4", 6_000_000_000, "original.mp4", Start.AddMinutes(-90));
        Videos.Items.Add(video);
        return video;
    }

    public MatchVideo ReadyVideo()
    {
        var video = Upload();
        video.StartProcessing(Start.AddMinutes(-80));
        video.Compressed("comprimido.mp4", 3_000_000_000, "video/mp4", "partido.mp4", 5782, 1080, Start.AddMinutes(-10));
        return video;
    }
}

internal static class TestStub
{
    public static T Of<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = System.Reflection.DispatchProxy.Create<T, ClubAsStaffTests.StoreProxy>();
        ((ClubAsStaffTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeVideoStore : IMatchVideoStore
{
    public readonly List<MatchVideo> Items = [];
    public readonly HashSet<Guid> DeletedIds = [];

    public Task<MatchVideo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(v => v.Id == id));
    public Task<IReadOnlyList<MatchVideo>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MatchVideo>>(Items.Where(v => v.MatchId == matchId && !v.IsDeleted).ToList());
    public Task<IReadOnlyList<MatchVideo>> GetBySquadsAsync(IEnumerable<Guid> squadIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void Add(MatchVideo video) => Items.Add(video);
    public Task<MatchVideo?> GetNextPendingAsync(int maxAttempts, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Where(v => v.AwaitsProcessing(maxAttempts)).OrderBy(v => v.UploadedAt).FirstOrDefault());
    public Task<IReadOnlyList<MatchVideo>> GetProcessingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MatchVideo>>(Items.Where(v => v.Status == VideoStatus.Processing).ToList());
    public Task<bool> IsDeletedAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(DeletedIds.Contains(id));
}

internal sealed class FakeAnalysisStore : IMatchVideoAnalysisStore
{
    public readonly List<MatchVideoAnalysis> Items = [];
    public readonly Dictionary<Guid, List<VideoAnalysisCrop>> Crops = [];
    public FakeVideoStore? Videos;

    public Task<MatchVideoAnalysis?> GetByVideoAsync(Guid videoId, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(a => a.VideoId == videoId));
    public Task<IReadOnlyList<MatchVideoAnalysis>> GetByVideosAsync(IEnumerable<Guid> videoIds, CancellationToken cancellationToken = default)
    {
        var ids = videoIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<MatchVideoAnalysis>>(Items.Where(a => ids.Contains(a.VideoId)).ToList());
    }
    public void Add(MatchVideoAnalysis analysis) => Items.Add(analysis);
    public void Remove(MatchVideoAnalysis analysis) => Items.Remove(analysis);
    public Task<MatchVideoAnalysis?> GetNextPendingAsync(int maxAttempts, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Where(a => a.Status == VideoAnalysisStatus.Pending && a.Attempts < maxAttempts && Videos?.Items.Any(v => v.Id == a.VideoId && !v.IsDeleted) != false)
            .OrderBy(a => a.RequestedAt).FirstOrDefault());
    public Task<IReadOnlyList<MatchVideoAnalysis>> GetRunningAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MatchVideoAnalysis>>(Items.Where(a => a.Status == VideoAnalysisStatus.Running).ToList());
    public Task ReplaceCropsAsync(Guid analysisId, IReadOnlyCollection<VideoAnalysisCrop> crops, CancellationToken cancellationToken = default)
    {
        Crops[analysisId] = crops.ToList();
        return Task.CompletedTask;
    }
    public Task<byte[]?> GetCropAsync(Guid analysisId, int tracklet, CancellationToken cancellationToken = default) =>
        Task.FromResult(Crops.GetValueOrDefault(analysisId)?.FirstOrDefault(c => c.Tracklet == tracklet)?.Content);
    public Task<IReadOnlySet<int>> GetCropTrackletsAsync(Guid analysisId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<int>>((Crops.GetValueOrDefault(analysisId) ?? []).Select(c => c.Tracklet).ToHashSet());
}

internal sealed class FakeAnalysisSignal : IVideoAnalysisSignal
{
    public int Wakes;
    public readonly List<Guid> Cancelled = [];

    public void Wake() => Wakes++;
    public void Cancel(Guid videoId) => Cancelled.Add(videoId);
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;
    public IDisposable Track(Guid videoId, out CancellationToken cancellation) => throw new NotSupportedException();
}

internal sealed class FakeAnalyzer : IVideoAnalyzer
{
    public VideoAnalysisRun? Run;
    public Exception? Failure;
    public int[] ProgressSteps = [];
    public Action? BeforeProgress;
    public Action? OnAnalyze;
    public VideoAnalysisRequest? Request;
    public readonly List<Guid> DeletedOutputs = [];

    public async Task<VideoAnalysisRun> AnalyzeAsync(VideoAnalysisRequest request, Func<int, CancellationToken, Task> progress, CancellationToken cancellationToken)
    {
        Request = request;
        OnAnalyze?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        foreach (var step in ProgressSteps)
        {
            BeforeProgress?.Invoke();
            await progress(step, cancellationToken);
        }
        return Run ?? throw new InvalidOperationException("Sin resultado");
    }

    public Task DeleteOutputAsync(Guid videoId, CancellationToken cancellationToken)
    {
        DeletedOutputs.Add(videoId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeVideoJobs : IVideoJobLog
{
    public string? StartedJob;
    public string? StartedLabel;
    public (bool Succeeded, string Summary, string? Error)? Finished;

    public Task<Guid?> StartedAsync(string job, string label, CancellationToken cancellationToken)
    {
        StartedJob = job;
        StartedLabel = label;
        return Task.FromResult<Guid?>(Guid.NewGuid());
    }

    public Task FinishedAsync(Guid? runId, bool succeeded, string summary, string? error, CancellationToken cancellationToken)
    {
        Finished = (succeeded, summary, error);
        return Task.CompletedTask;
    }
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.FromResult(1);
    }
}
