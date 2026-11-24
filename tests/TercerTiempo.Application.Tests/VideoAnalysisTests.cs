using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Squads.TeamStats;
using TercerTiempo.Application.Videos.Analysis;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
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
        Assert.Equal((VideoPossessionConfidence.Medium, 48.2m, 38), (grey.PossessionConfidence, grey.PossessionBallPct, grey.UsablePossessionPct));
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
        // Sin confianza (un análisis anterior) no se sabe: la posesión se usa como siempre.
        Assert.Null(team.PossessionConfidence);
        Assert.Equal(100, team.UsablePossessionPct);
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
    public void Low_confidence_possession_is_shown_but_never_taken_to_the_sheet()
    {
        var low = VideoProposal.Parse(VideoAnalysisSamples.Proposal.Replace("\"possessionConfidence\": \"media\"", "\"possessionConfidence\": \"baja\""));
        var fine = VideoProposal.Parse(VideoAnalysisSamples.Proposal.Replace("\"possessionPct\": 38", "\"possessionPct\": 60"));

        Assert.Equal((38, VideoPossessionConfidence.Low), (low.Team(0)!.PossessionPct, low.Team(0)!.PossessionConfidence));
        Assert.Null(low.Team(0)!.UsablePossessionPct);
        Assert.Null(VideoSheetMerge.Totals([new VideoPartSelection(low, 0, 2700)]).PossessionPct);
        // Con otra parte fiable, la media sale solo de esa.
        Assert.Equal(60, VideoSheetMerge.Totals([new VideoPartSelection(low, 0, 2700), new VideoPartSelection(fine, 0, 900)]).PossessionPct);
        // Lo demás de la parte floja sí se suma.
        Assert.Equal(8, VideoSheetMerge.Totals([new VideoPartSelection(low, 0, 2700), new VideoPartSelection(fine, 0, 900)]).ShotsFor);
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
    public void Fouls_and_offsides_are_read_from_both_sides()
    {
        var proposal = VideoProposal.Parse("""
            {"equipos": [{"id": 0, "teamStats": {"foulsCommitted": 3, "offsidesFor": 1}}, {"id": 1, "teamStats": {"foulsCommitted": 5, "offsidesFor": 2}}],
             "jugadores": [{"tracklet": 7, "equipo": 0, "propuesta": {"fouls": 2, "foulsReceived": 1, "offsides": 1}}]}
            """);

        // Las faltas que recibimos son las que comete el rival, y sus fueras de juego, los que le pitan a él.
        var totals = VideoSheetMerge.Totals([new VideoPartSelection(proposal, 0, 300)]);
        Assert.Equal((3, 5, 1, 2), (totals.FoulsCommitted, totals.FoulsReceived, totals.OffsidesFor, totals.OffsidesAgainst));
        var actions = proposal.Players.Single().Actions;
        Assert.Equal((2, 1, 1, true), (actions.Fouls, actions.FoulsReceived, actions.Offsides, actions.HasSheetData));

        var overwritten = new List<string>();
        var written = 0;
        var merged = VideoSheetMerge.MergeTeam(Empty with { FoulsCommitted = 4 }, totals, [VideoTeamField.Fouls, VideoTeamField.Offsides], overwritten, ref written);
        Assert.Equal((3, 5, 1, 2), (merged.FoulsCommitted, merged.FoulsReceived, merged.OffsidesFor, merged.OffsidesAgainst));
        Assert.Equal(["faltas cometidas (4 → 3)"], overwritten);
        Assert.Equal(4, written);
    }

    [Fact]
    public void With_the_closed_official_sheet_minutes_start_goals_and_cards_are_its_own()
    {
        var id = Guid.NewGuid();
        var overwritten = new List<string>();

        // Titular sin cambio: el partido entero aunque el vídeo solo lo viera 20 minutos; los goles, los del acta (uno de
        // penalti), no los que cuenta el vídeo. Las acciones y las faltas sí son del vídeo.
        var video = new VideoPlayerActions(3, 2, 3, 0, 4, 1, 10, 12, 30, Fouls: 2);
        var line = VideoSheetMerge.MergeLine(id, "Pelayo", null, video, 20, overwritten, OfficialLine(true, 0, null, 90, goals: 1, penaltyGoals: 1, yellow: 1))!;
        Assert.Equal((true, 90, 2, 1, false), (line.Started, line.MinutesPlayed, line.Goals, line.YellowCards, line.RedCard));
        Assert.Equal((3, 2, 4, 2), (line.Shots, line.ShotsOnTarget, line.Recoveries, line.Fouls));
        Assert.Empty(overwritten);

        // Sin nada del vídeo la línea sale igual del acta: no salir en el vídeo no es no jugar.
        var unseen = VideoSheetMerge.MergeLine(id, "Pelayo", null, VideoPlayerActions.None, 0, overwritten, OfficialLine(false, 60, null, 30))!;
        Assert.Equal((false, 30, 0), (unseen.Started, unseen.MinutesPlayed, unseen.Shots));

        // Lo que ya estaba apuntado distinto se avisa antes de cambiarlo.
        VideoSheetMerge.MergeLine(id, "Pelayo", unseen with { MinutesPlayed = 35, Started = true, Goals = 1 }, VideoPlayerActions.None, 0, overwritten, OfficialLine(false, 60, null, 30));
        Assert.Equal(["minutos de Pelayo (35 → 30)", "titular de Pelayo (sí → no)", "goles de Pelayo (1 → 0)"], overwritten);

        // Con dos amarillas, la roja (el acta interna la exige).
        Assert.True(VideoSheetMerge.MergeLine(id, "Pelayo", null, VideoPlayerActions.None, 0, [], OfficialLine(true, 0, 70, 70, yellow: 2))!.RedCard);
        // Convocado sin jugar: sin vídeo no hay línea.
        Assert.Null(VideoSheetMerge.MergeLine(id, "Pelayo", null, VideoPlayerActions.None, 0, overwritten, OfficialLine(false, null, null, 0)));
    }

    private static OfficialSheetLine OfficialLine(bool started, int? minuteIn, int? minuteOut, int minutes, int goals = 0, int penaltyGoals = 0, int yellow = 0)
    {
        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "test", "1", null, DateTimeOffset.UtcNow);
        sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 2, 0,
            [new(MatchSide.Home, 9, "FERNANDEZ, PELAYO", started, minuteIn, minuteOut, minutes, goals, penaltyGoals, 0, yellow, yellow >= 2)], []), DateTimeOffset.UtcNow);
        return sheet.Lines.Single();
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

        // --frozen: nunca se resuelve ni se reinstala nada (torch) a mitad de un análisis.
        Assert.Equal(["run", "--frozen", "tt-analisis", "analizar", @"C:\videos\clave.mp4"], args.Take(5));
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
        Assert.DoesNotContain("--plantillas", args);
    }

    [Fact]
    public void With_the_official_sheet_the_analyzer_gets_who_played_with_their_shirt_numbers_and_minutes()
    {
        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "test", "1", null, DateTimeOffset.UtcNow);
        sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 0, 1,
        [
            new(MatchSide.Home, 18, "PÉREZ, ÁLVARO", true, 0, 24, 24, 0, 0, 0, 0, false),
            new(MatchSide.Home, 15, "NÚÑEZ, LUIS", false, 24, null, 66, 0, 0, 0, 0, false),
            new(MatchSide.Home, 16, "SIN JUGAR", false, null, null, 0, 0, 0, 0, 0, false),
            new(MatchSide.Home, null, "SIN DORSAL", true, 0, null, 90, 0, 0, 0, 0, false),
            new(MatchSide.Away, 9, "GARCÍA, JUAN", true, 0, null, 90, 0, 0, 0, 0, false),
        ], []), DateTimeOffset.UtcNow);

        var roster = VideoAnalysisProcessor.Roster(sheet, VideoPart.SecondHalf)!;
        Assert.Equal((VideoPart.SecondHalf, 45), (roster.Part, roster.HalfMinutes));
        Assert.Equal([(MatchSide.Home, 15, false, (int?)24, (int?)null), (MatchSide.Home, 18, true, null, 24), (MatchSide.Away, 9, true, null, null)],
            roster.Teams.SelectMany(t => t.Players.Select(p => (t.Side, p.ShirtNumber, p.Started, p.MinuteIn, p.MinuteOut))));
        Assert.Null(VideoAnalysisProcessor.Roster(null, VideoPart.Full));

        var request = new VideoAnalysisRequest(Guid.NewGuid(), "clave.mp4", "A", "B", null, null, roster);
        var args = UvVideoAnalyzer.Arguments("v.mp4", "out", request, new VideoAnalysisOptions()).ToList();
        Assert.Equal(Path.Combine("out", UvVideoAnalyzer.RosterFile), args[args.IndexOf("--plantillas") + 1]);

        using var json = JsonDocument.Parse(UvVideoAnalyzer.RosterJson(roster));
        Assert.Equal("segunda", json.RootElement.GetProperty("parte").GetString());
        var home = json.RootElement.GetProperty("equipos")[0];
        Assert.Equal(("local", "CD Vallobín"), (home.GetProperty("lado").GetString(), home.GetProperty("nombre").GetString()));
        var sub = home.GetProperty("jugadores")[0];
        Assert.Equal((15, "NÚÑEZ, LUIS", false, 24), (sub.GetProperty("dorsal").GetInt32(), sub.GetProperty("nombre").GetString(), sub.GetProperty("titular").GetBoolean(), sub.GetProperty("entra").GetInt32()));
    }

    [Fact]
    public void With_the_closed_sheet_the_analyzer_also_gets_the_score_and_the_minute_of_each_goal()
    {
        static OfficialMatchSheet Sheet(bool closed)
        {
            var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "test", "1", null, DateTimeOffset.UtcNow);
            sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 1, 2,
            [
                new(MatchSide.Home, 9, "FERNÁNDEZ, PELAYO", true, 0, null, 90, 1, 0, 1, 0, false),
                new(MatchSide.Away, 7, "GARCÍA, JUAN", true, 0, null, 90, 0, 1, 0, 0, false),
            ],
            [
                new(MatchSide.Home, OfficialEventType.Goal, 30, null, "FERNÁNDEZ, PELAYO", null),
                new(MatchSide.Home, OfficialEventType.OwnGoal, 45, 2, "FERNÁNDEZ, PELAYO", null),
                new(MatchSide.Away, OfficialEventType.YellowCard, 60, null, "GARCÍA, JUAN", null),
                new(MatchSide.Away, OfficialEventType.PenaltyGoal, 88, null, "GARCÍA, JUAN", null),
            ]), DateTimeOffset.UtcNow, closed);
            return sheet;
        }

        var roster = VideoAnalysisProcessor.Roster(Sheet(closed: true), VideoPart.Full)!;
        Assert.Equal(new VideoAnalysisScore(1, 2), roster.Score);
        // El de propia puerta sube al otro lado, y el descuento va aparte para que el 45+2 siga en la primera parte.
        Assert.Equal([new VideoAnalysisGoal(MatchSide.Home, 30), new VideoAnalysisGoal(MatchSide.Away, 45, 2), new VideoAnalysisGoal(MatchSide.Away, 88)], roster.Goals!);
        Assert.Equal([1, 1], roster.Teams.Select(t => t.Players.Single().Goals));

        using var json = JsonDocument.Parse(UvVideoAnalyzer.RosterJson(roster));
        var score = json.RootElement.GetProperty("resultado");
        Assert.Equal((1, 2), (score.GetProperty("local").GetInt32(), score.GetProperty("visitante").GetInt32()));
        var own = json.RootElement.GetProperty("goles")[1];
        Assert.Equal(("visitante", 45, 2), (own.GetProperty("lado").GetString(), own.GetProperty("minuto").GetInt32(), own.GetProperty("descuento").GetInt32()));
        Assert.Equal(1, json.RootElement.GetProperty("equipos")[0].GetProperty("jugadores")[0].GetProperty("goles").GetInt32());

        // La provisional (sin cerrar) ya trae las alineaciones, pero no el marcador: ni resultado ni goles.
        var provisional = VideoAnalysisProcessor.Roster(Sheet(closed: false), VideoPart.Full)!;
        Assert.Null(provisional.Score);
        using var withoutScore = JsonDocument.Parse(UvVideoAnalyzer.RosterJson(provisional));
        Assert.Equal(JsonValueKind.Null, withoutScore.RootElement.GetProperty("resultado").ValueKind);
        Assert.Equal(0, withoutScore.RootElement.GetProperty("goles").GetArrayLength());
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
            // Un análisis de antes de que el analizador tuviera nombre: el mismo analizador, con las rutas de sus pesos.
            Assert.Equal(("Ojeador", "0.1.0"), (run.AnalyzerName, run.AnalyzerVersion));
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

public class UvVideoAnalyzerOutputTests
{
    [Theory]
    [InlineData("\"version\": 3,")]
    [InlineData("")]
    public void A_proposal_of_an_unknown_version_is_rejected_with_a_clear_message(string version) =>
        WithOutput(VideoAnalysisSamples.Proposal.Replace("\"version\": 1,", version), VideoAnalysisSamples.Analysis, folder =>
        {
            var error = Assert.Throws<VideoAnalysisException>(() => UvVideoAnalyzer.ReadRun(folder, "analysis/abc"));
            Assert.Contains("esta app solo entiende la 1", error.Message);
        });

    [Fact]
    public void A_file_with_nan_is_a_failure_of_the_analysis_and_not_a_passing_one() =>
        WithOutput(VideoAnalysisSamples.Proposal, VideoAnalysisSamples.Analysis.Replace("\"pct_calibrados\": 67.1", "\"pct_calibrados\": NaN"), folder =>
        {
            var error = Assert.Throws<VideoAnalysisException>(() => UvVideoAnalyzer.ReadRun(folder, "analysis/abc"));
            Assert.Contains("analisis.json", error.Message);
        });

    [Fact]
    public void What_was_read_from_the_file_is_kept_and_a_badly_damaged_file_fails()
    {
        WithOutput(VideoAnalysisSamples.Proposal, VideoAnalysisSamples.Analysis.Replace("\"tracklets\": 61", "\"tracklets\": 61, \"pct_leido\": 88.04"), folder =>
            Assert.Equal(88m, UvVideoAnalyzer.ReadRun(folder, "analysis/abc").ReadFramesPct));
        WithOutput(VideoAnalysisSamples.Proposal, VideoAnalysisSamples.Analysis, folder =>
            Assert.Null(UvVideoAnalyzer.ReadRun(folder, "analysis/abc").ReadFramesPct));
        WithOutput(VideoAnalysisSamples.Proposal, VideoAnalysisSamples.Analysis.Replace("\"tracklets\": 61", "\"tracklets\": 61, \"pct_leido\": 31"), folder =>
            Assert.Contains("dañado o cortado", Assert.Throws<VideoAnalysisException>(() => UvVideoAnalyzer.ReadRun(folder, "analysis/abc")).Message));
    }

    [Fact]
    public async Task The_whole_error_output_is_kept_in_a_file_and_the_exit_code_is_returned()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateTempSubdirectory("tt-analisis-").FullName;
        try
        {
            var stderr = Path.Combine(folder, UvVideoAnalyzer.StderrFile);
            var progress = new List<int>();

            var result = await UvVideoAnalyzer.RunAsync("cmd.exe", ["/c", "echo ##progreso 40& echo Traceback: CUDA out of memory 1>&2& exit /b 7"], folder, stderr,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), (u, _) => { if (u.Percent is { } p) progress.Add(p); return Task.CompletedTask; }, default);

            Assert.Equal(7, result.ExitCode);
            Assert.Equal([40], progress);
            Assert.Contains("CUDA out of memory", result.StderrTail);
            Assert.Contains("CUDA out of memory", await File.ReadAllTextAsync(stderr));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task An_analyzer_that_stops_writing_is_killed_as_hung()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateTempSubdirectory("tt-analisis-").FullName;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();

            // Un minuto en silencio, con dos segundos de margen: se mata enseguida.
            var error = await Assert.ThrowsAsync<VideoAnalysisException>(() => UvVideoAnalyzer.RunAsync(
                "cmd.exe", ["/c", "ping -n 60 127.0.0.1 >nul"], folder, Path.Combine(folder, UvVideoAnalyzer.StderrFile),
                TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2), (_, _) => Task.CompletedTask, default));

            Assert.Contains("sin dar señales", error.Message);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"Tardó {clock.Elapsed}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_detailed_state_of_the_analyzer_is_read_with_the_names_of_the_app()
    {
        var live = UvVideoAnalyzer.LiveState("""##estado {"etapa":"pasada","porcentaje":54,"pausado":false,"pausa_s":0,"t_video":1834.6,"tramo_inicio_s":1200,"tramo_fin_s":2700,"fotogramas":3170,"fotogramas_total":7500,"jugadores_en_imagen":17,"jugadores_seguidos":42,"balon_pct":61.27,"calibrado_pct":88.04,"fps":9.47,"velocidad":1.89,"resta_s":3355,"colores":["#1E3A8A","#f5f5f5","rojo"]}""");

        Assert.NotNull(live);
        Assert.Equal(("tracking", false, 1835, 1200, 2700), (live.Stage, live.Paused, live.VideoSecond, live.FromSecond, live.ToSecond));
        Assert.Equal((3170, 7500, 17, 42), (live.Frames, live.FramesTotal, live.PlayersOnScreen, live.PlayersTracked));
        Assert.Equal((61.3m, 88m, 9.47m, 1.89m, 3355), (live.BallSeenPct, live.CalibratedPct, live.Fps, live.Speed, live.RemainingSeconds));
        Assert.Equal(["#1E3A8A", "#f5f5f5"], live.Colors);

        var paused = UvVideoAnalyzer.LiveState("""##estado {"etapa":"equipos","pausado":true,"pausa_s":42,"muestras":120,"objetivo":400}""");
        Assert.Equal(("teams", true, 42, 120, 400), (paused!.Stage, paused.Paused, paused.PausedSeconds, paused.TeamSamples, paused.TeamSamplesTarget));

        Assert.Null(UvVideoAnalyzer.LiveState("##estado {roto"));
        Assert.Null(UvVideoAnalyzer.LiveState("##progreso 40"));
        Assert.Equal("Equipos separados: CD Vallobín de azul", UvVideoAnalyzer.LogLine("##hito Equipos separados: CD Vallobín de azul "));
        Assert.Null(UvVideoAnalyzer.LogLine("##hito   "));
        Assert.Null(UvVideoAnalyzer.LogLine("Traceback"));
    }

    [Fact]
    public async Task The_analyzer_receives_pause_and_resume_orders_and_its_state_and_log_are_read()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateTempSubdirectory("tt-analisis-").FullName;
        try
        {
            // Un «analizador» que cuenta su estado, espera una orden por la entrada y la repite como hito.
            var script = Path.Combine(folder, "ordenes.cmd");
            await File.WriteAllTextAsync(script, string.Join("\r\n",
                "@echo off",
                "echo ##estado {\"etapa\":\"pasada\",\"pausado\":false,\"jugadores_seguidos\":12}",
                "set /p orden=",
                "echo ##hito orden %orden%",
                "echo ##progreso 60"));
            var updates = new List<VideoAnalysisUpdate>();
            var control = new VideoAnalysisControl();

            var result = await UvVideoAnalyzer.RunAsync("cmd.exe", ["/c", script], folder, Path.Combine(folder, UvVideoAnalyzer.StderrFile),
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), (u, _) =>
                {
                    updates.Add(u);
                    if (u.Live is not null) control.Set(true);
                    return Task.CompletedTask;
                }, default, control: control);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(12, updates.Single(u => u.Live is not null).Live!.PlayersTracked);
            Assert.Equal("orden ##pausa", updates.Single(u => u.Log is not null).Log);
            Assert.Equal(60, updates.Single(u => u.Percent is not null).Percent);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_analysis_says_which_analyzer_and_which_models_analyzed_the_video()
    {
        var analysis = VideoAnalysisSamples.Analysis.Replace(
            "\"version\": \"0.1.0\",",
            """
            "version": "1.0.0",
            "analizador": { "nombre": "Ojeador", "version": "1.0.0", "modelos": [
              { "que": "jugadores", "modelo": "RF-DETR Medium", "entrenamiento": "rfdetr-jugadores-20261117", "motor": "TensorRT", "huella": "d5c932ba" },
              { "que": "balon", "modelo": "RF-DETR Medium", "entrenamiento": "rfdetr-balon-20261117-v2", "motor": "TensorRT", "huella": "b46dbd49" },
              { "que": "dorsales", "modelo": "PARSeq", "entrenamiento": "afinado con nuestros partidos", "motor": "PyTorch", "huella": "1a2b3c4d" },
              { "que": "campo", "modelo": "RF-DETR Keypoint" } ] },
            """);

        WithOutput(VideoAnalysisSamples.Proposal, analysis, folder =>
        {
            var run = UvVideoAnalyzer.ReadRun(folder, "analysis/abc");

            Assert.Equal(("Ojeador", "1.0.0"), (run.AnalyzerName, run.AnalyzerVersion));
            Assert.Equal(
                "Jugadores: RF-DETR Medium (rfdetr-jugadores-20261117, TensorRT, huella d5c932ba) · Balón: RF-DETR Medium (rfdetr-balon-20261117-v2, TensorRT, huella b46dbd49) · "
                + "Dorsales: PARSeq (afinado con nuestros partidos, PyTorch, huella 1a2b3c4d) · Campo: RF-DETR Keypoint",
                run.Weights);
        });
    }

    private static void WithOutput(string proposal, string analysis, Action<string> check)
    {
        var folder = Directory.CreateTempSubdirectory("tt-analisis-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "acta-propuesta.json"), proposal);
            File.WriteAllText(Path.Combine(folder, "analisis.json"), analysis);
            check(folder);
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

        var analysis = await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default);
        Assert.Equal(VideoAnalysisStatus.Running, analysis!.Status);
        await world.Processor.ProcessAsync(analysis, default);

        Assert.Equal(VideoAnalysisStatus.Ready, analysis.Status);
        Assert.Equal(100, analysis.Progress);
        Assert.Equal(VideoAnalysisSamples.Run().ProposalJson, analysis.ProposalJson);
        Assert.Equal((19.3m, 67.1m, 1500, "Ojeador 0.1.0"), (analysis.BallFramesPct, analysis.CalibratedFramesPct, analysis.Frames, analysis.Analyzer));
        Assert.True(analysis.LowQuality);
        Assert.Equal([696, 781], world.Analyses.Crops[analysis.Id].Select(c => c.Tracklet));

        // El analizador recibe nuestro equipo primero y las medidas del campo del local.
        var request = world.Analyzer.Request!;
        Assert.Equal(("CD Vallobín", "Real Titánico", 98.5m, 62m, "comprimido.mp4"), (request.OurTeam, request.RivalTeam, request.PitchLengthM, request.PitchWidthM, request.StorageKey));

        Assert.Equal(JobNames.VideoAnalysis, world.Jobs.StartedJob);
        Assert.True(world.Jobs.Finished!.Value.Succeeded);
        Assert.Contains("CD Vallobín - Real Titánico (partido completo)", world.Jobs.Finished!.Value.Summary);
        Assert.Contains("19,3 %", world.Jobs.Finished!.Value.Summary);
        Assert.Null(await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default));
    }

    [Fact]
    public async Task Away_from_home_the_pitch_is_the_one_of_the_home_team()
    {
        var world = new AnalysisWorld(away: true);
        var video = world.ReadyVideo();
        await world.Queue.EnqueueAsync(video, default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();

        await world.Processor.ProcessAsync((await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!, default);

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

        var analysis = await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default);
        await world.Processor.ProcessAsync(analysis!, default);

        Assert.Equal(VideoAnalysisStatus.Failed, analysis!.Status);
        Assert.StartsWith("Este equipo no tiene una GPU", analysis.Error);
        Assert.Null(analysis.ProposalJson);
        Assert.Equal((VideoStatus.Ready, "comprimido.mp4"), (video.Status, video.StorageKey));
        Assert.False(world.Jobs.Finished!.Value.Succeeded);
        Assert.StartsWith("Este equipo no tiene una GPU", world.Jobs.Finished!.Value.Error);
        // No se reintenta solo: lo vuelve a pedir el cuerpo técnico.
        Assert.Null(await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default));
    }

    [Fact]
    public async Task Progress_is_saved_at_most_every_few_seconds()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();
        world.Analyzer.ProgressSteps = [10, 20, 30];
        world.Analyzer.BeforeProgress = () => world.Clock.Now += TimeSpan.FromSeconds(3);

        var analysis = await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default);
        var savesBefore = world.UnitOfWork.Saves;
        await world.Processor.ProcessAsync(analysis!, default);

        // 10 % (3 s, no), 20 % (6 s, sí), 30 % (9 s, no); el final lo guarda el almacén junto con los recortes.
        Assert.Equal(savesBefore + 1, world.UnitOfWork.Saves);
        Assert.Equal(VideoAnalysisStatus.Ready, analysis!.Status);
        Assert.NotEmpty(world.Analyses.Crops[analysis.Id]);
    }

    [Fact]
    public async Task Shutdown_mid_analysis_puts_it_back_in_the_queue_and_deleting_the_video_discards_it()
    {
        var world = new AnalysisWorld();
        var video = world.ReadyVideo();
        await world.Queue.EnqueueAsync(video, default);
        using var stopping = new CancellationTokenSource();
        world.Analyzer.OnAnalyze = () => stopping.Cancel();

        var analysis = await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Processor.ProcessAsync(analysis!, stopping.Token));

        Assert.Equal((VideoAnalysisStatus.Pending, 0), (analysis!.Status, analysis.Attempts));

        using var deleted = new CancellationTokenSource();
        world.Analyzer.OnAnalyze = () =>
        {
            world.Videos.DeletedIds.Add(video.Id);
            deleted.Cancel();
        };
        analysis = await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Processor.ProcessAsync(analysis!, deleted.Token));

        Assert.Equal(VideoAnalysisStatus.Running, analysis!.Status);
        Assert.False(world.Jobs.Finished!.Value.Succeeded);
    }

    [Fact]
    public async Task What_a_dead_analyzer_left_running_goes_back_to_the_queue_and_a_live_one_is_left_alone()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;
        Assert.Equal((AnalysisWorld.Worker, AnalysisWorld.Start), (analysis.WorkerId, analysis.HeartbeatAt));

        // Latido reciente y de otro proceso vivo: no se toca.
        world.Clock.Now += TimeSpan.FromMinutes(5);
        Assert.Equal(0, await world.Processor.RecoverAbandonedAsync(_ => false, default));
        Assert.Equal(VideoAnalysisStatus.Running, analysis.Status);

        // Al arrancar, el de un proceso de este equipo que ya no existe se recoge sin esperar a que caduque.
        Assert.Equal(1, await world.Processor.RecoverAbandonedAsync(worker => worker == AnalysisWorld.Worker, default));
        Assert.Equal((VideoAnalysisStatus.Pending, 1, null), (analysis.Status, analysis.Attempts, analysis.WorkerId));
        Assert.Contains("se paró a medias", analysis.Error);
    }

    [Fact]
    public async Task A_lease_without_heartbeat_expires_and_after_the_last_attempt_the_analysis_fails()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);

        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;
        world.Clock.Now += TimeSpan.FromMinutes(11);
        Assert.Equal(1, await world.Processor.RecoverAbandonedAsync(_ => false, default));
        Assert.Equal(VideoAnalysisStatus.Pending, analysis.Status);

        // Segundo intento (MaxAttempts = 2) y vuelve a morir: ya no se reintenta.
        Assert.Same(analysis, await world.Processor.ClaimNextAsync("otro:7", default));
        world.Clock.Now += TimeSpan.FromMinutes(11);
        Assert.Equal(1, await world.Processor.RecoverAbandonedAsync(_ => false, default));
        Assert.Equal((VideoAnalysisStatus.Failed, 2), (analysis.Status, analysis.Attempts));
        Assert.Null(await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default));
    }

    [Fact]
    public async Task A_passing_failure_is_retried_while_attempts_remain()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Failure = new IOException("La red del volumen se cortó");

        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;
        await world.Processor.ProcessAsync(analysis, default);

        Assert.Equal((VideoAnalysisStatus.Pending, 1, "La red del volumen se cortó"), (analysis.Status, analysis.Attempts, analysis.Error));
        Assert.Contains("se vuelve a intentar", world.Jobs.Finished!.Value.Summary);

        await world.Processor.ProcessAsync((await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!, default);
        Assert.Equal((VideoAnalysisStatus.Failed, 2), (analysis.Status, analysis.Attempts));
    }

    [Fact]
    public async Task When_the_row_changes_mid_analysis_it_stops_without_saving_anything()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();
        world.Analyzer.ProgressSteps = [10, 20];
        world.Analyzer.BeforeProgress = () => world.Clock.Now += TimeSpan.FromSeconds(10);

        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;
        // El cuerpo técnico lo cancela (o se elimina el vídeo): al guardar el avance, la fila ya es otra.
        world.UnitOfWork.Failure = () => new ConcurrentChangeException();
        await world.Processor.ProcessAsync(analysis, default);

        Assert.Empty(world.Analyses.Crops);
        Assert.Null(analysis.ProposalJson);
        Assert.False(world.Jobs.Finished!.Value.Succeeded);
        Assert.Contains("detenido", world.Jobs.Finished!.Value.Summary);
    }

    [Fact]
    public async Task What_the_analyzer_is_doing_and_what_it_learns_is_saved_on_the_row()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();
        world.Analyzer.Updates =
        [
            new(Live: new VideoAnalysisLive("teams", false, TeamSamples: 120, TeamSamplesTarget: 400)),
            new(Log: "Equipos separados: CD Vallobín de gris"),
            new(Percent: 30, Live: new VideoAnalysisLive("tracking", false, VideoSecond: 1500, PlayersTracked: 20, BallSeenPct: 55.5m)),
            new(Percent: 31, Live: new VideoAnalysisLive("tracking", false, VideoSecond: 1510, PlayersTracked: 21, BallSeenPct: 56m)),
        ];
        string? seen = null;
        world.Analyzer.AfterUpdates = () => seen = world.Analyses.Items.Single().LiveJson;
        var states = new List<(string? Stage, int Progress, int Log)>();
        world.Analyzer.AfterEach = () => { var a = world.Analyses.Items.Single(); states.Add((a.Stage, a.Progress, a.Log.Count)); };

        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;
        await world.Processor.ProcessAsync(analysis, default);

        // El cambio de etapa y la línea del registro se guardan enseguida; el minuto 1510 espera a los 5 s.
        Assert.Equal([("teams", 0, 0), ("teams", 0, 1), ("tracking", 30, 1), ("tracking", 30, 1)], states);
        var live = VideoAnalysisProcessor.ReadLive(seen);
        Assert.Equal((1500, 20, 55.5m), (live!.VideoSecond, live.PlayersTracked, live.BallSeenPct));
        Assert.Equal("Equipos separados: CD Vallobín de gris", analysis.Log.Single().Text);

        // Al terminar, lo de en directo se olvida.
        Assert.Equal((VideoAnalysisStatus.Ready, null), (analysis.Status, analysis.LiveJson));
    }

    [Fact]
    public async Task A_pause_asked_from_the_app_reaches_the_analyzer_and_the_paused_time_does_not_count()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();
        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;

        // El cuerpo técnico lo pausa a los 10 minutos y lo reanuda a la media hora; el analizador lo nota por su cuenta.
        var timeline = new List<bool>();
        world.Analyzer.WhileRunning = async (control, token) =>
        {
            world.Clock.Now += TimeSpan.FromMinutes(10);
            analysis.Pause(world.Clock.Now);
            await WaitForAsync(() => control.Paused, token);
            timeline.Add(control.Paused);
            world.Clock.Now += TimeSpan.FromMinutes(20);
            analysis.Resume(world.Clock.Now);
            await WaitForAsync(() => !control.Paused, token);
            timeline.Add(control.Paused);
            world.Clock.Now += TimeSpan.FromMinutes(5);
        };
        await world.Processor.ProcessAsync(analysis, default);

        Assert.Equal([true, false], timeline);
        Assert.Equal((VideoAnalysisStatus.Ready, 1200), (analysis.Status, analysis.PausedSeconds));
        Assert.Contains("en 15 min", world.Jobs.Finished!.Value.Summary);
    }

    [Fact]
    public async Task A_paused_analysis_that_is_cancelled_stops_without_saving_anything()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Run = VideoAnalysisSamples.Run();
        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;

        world.Analyzer.WhileRunning = async (control, token) =>
        {
            analysis.Pause(world.Clock.Now);
            await WaitForAsync(() => control.Paused, token);
            analysis.Cancel(world.Clock.Now);
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        };
        await world.Processor.ProcessAsync(analysis, default);

        Assert.Equal(VideoAnalysisStatus.Cancelled, analysis.Status);
        Assert.Empty(world.Analyses.Crops);
        Assert.Contains("detenido", world.Jobs.Finished!.Value.Summary);
    }

    [Fact]
    public async Task What_was_paused_in_the_queue_is_not_started_until_it_is_resumed()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        var analysis = world.Analyses.Items.Single();

        analysis.Pause(world.Clock.Now);
        Assert.Null(await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default));

        analysis.Resume(world.Clock.Now.AddMinutes(3));
        Assert.Equal((VideoAnalysisStatus.Pending, 180), (analysis.Status, analysis.PausedSeconds));
        Assert.Same(analysis, await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default));
        Assert.Equal(VideoAnalysisStatus.Running, analysis.Status);
    }

    [Fact]
    public async Task An_analyzer_that_dies_while_paused_leaves_it_paused_to_start_again_when_resumed()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;
        analysis.Pause(world.Clock.Now);

        world.Clock.Now += TimeSpan.FromMinutes(11);
        Assert.Equal(1, await world.Processor.RecoverAbandonedAsync(_ => false, default));
        Assert.Equal((VideoAnalysisStatus.Paused, null, false), (analysis.Status, analysis.WorkerId, analysis.IsWorking));

        analysis.Resume(world.Clock.Now);
        Assert.Equal(VideoAnalysisStatus.Pending, analysis.Status);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("El analizador no notó la orden");
            await Task.Delay(50, cancellationToken);
        }
    }

    [Fact]
    public async Task What_could_not_be_read_from_the_file_is_kept_and_lowers_the_quality()
    {
        var world = new AnalysisWorld();
        await world.Queue.EnqueueAsync(world.ReadyVideo(), default);
        world.Analyzer.Run = VideoAnalysisSamples.Run() with { BallFramesPct = 80m, CalibratedFramesPct = 90m, ReadFramesPct = 82.5m };

        var analysis = (await world.Processor.ClaimNextAsync(AnalysisWorld.Worker, default))!;
        await world.Processor.ProcessAsync(analysis, default);

        Assert.Equal((VideoAnalysisStatus.Ready, 82.5m, true), (analysis.Status, analysis.ReadFramesPct, analysis.LowQuality));
        Assert.Contains("82,5 %", world.Jobs.Finished!.Value.Summary);
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
              "teamStats": { "possessionPct": 38, "possessionConfidence": "media", "possessionBallPct": 48.2, "cornersFor": 3, "cornersAgainst": 1, "shotsFor": 4, "shotsAgainst": 9, "shotsOnTargetFor": 2,
                             "shotsOnTargetAgainst": 5, "throwInsFor": 11, "throwInsAgainst": 14, "goalKicksFor": 6, "goalsFor": 1, "goalsAgainst": 2,
                             "goalsCandidatesFor": 2, "offsidesFor": null, "foulsCommitted": null, "notes": "Propuesta automática del vídeo" } },
            { "id": 1, "nombre": "Rival", "color": "#9c8e8d",
              "teamStats": { "possessionPct": 62, "possessionConfidence": "media", "possessionBallPct": 48.2, "cornersFor": 1, "cornersAgainst": 3, "shotsFor": 9, "shotsAgainst": 4, "shotsOnTargetFor": 5,
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
        "analysis/abc", "Ojeador", "0.1.0", "balon: afinados/balon.pt", System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(Proposal).RootElement),
        1500, 19.3m, 67.1m, 300, 98.5m, 62m,
        new Dictionary<int, byte[]> { [696] = [0xFF, 0xD8, 0xFF, 0xD9], [781] = [0xFF, 0xD8, 0xFF, 0xD9] });
}

// Un partido del CD Vallobín con su vídeo ya comprimido y los dobles de la cola de análisis.
internal sealed class AnalysisWorld
{
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    public const string Worker = "EQUIPO-GPU:4242";

    public readonly ManualClock Clock = new() { Now = Start };
    public readonly FakeVideoStore Videos = new();
    public readonly FakeAnalysisStore Analyses = new();
    public readonly FakeAnalysisSignal Signal = new();
    public readonly FakeAnalyzer Analyzer = new();
    public OfficialMatchSheet? Sheet { get; set; }
    public readonly IOfficialSheetStore Sheets;
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
        var options = Options.Create(new VideoAnalysisOptions { Enabled = enabled, MaxAttempts = 2, LeaseMinutes = 10 });
        var matches = TestStub.Of<IMatchStore>((name, _) => name == nameof(IMatchStore.GetByIdAsync) ? Task.FromResult<Match?>(Match) : throw new NotSupportedException(name));
        var teams = TestStub.Of<ITeamStore>((name, _) => name == nameof(ITeamStore.GetByIdsAsync) ? Task.FromResult<IReadOnlyList<Team>>([Home, Away]) : throw new NotSupportedException(name));
        Sheets = TestStub.Of<IOfficialSheetStore>((name, _) => name == nameof(IOfficialSheetStore.GetByMatchAsync) ? Task.FromResult(Sheet) : throw new NotSupportedException(name));
        Queue = new VideoAnalysisQueue(Analyses, options, Signal, UnitOfWork, Clock);
        Processor = new VideoAnalysisProcessor(Analyses, Videos, matches, teams, Sheets, Analyzer, Jobs, options, UnitOfWork, Clock, NullLogger<VideoAnalysisProcessor>.Instance);
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
    public Task<IReadOnlyList<MatchVideo>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MatchVideo>>(Items.Where(v => !v.IsDeleted).ToList());
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
        Task.FromResult<IReadOnlyList<MatchVideoAnalysis>>(Items.Where(a => a.IsWorking).ToList());
    public Task<VideoAnalysisControlState?> GetControlAsync(Guid analysisId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(a => a.Id == analysisId) is { } a ? new VideoAnalysisControlState(a.Status, a.WorkerId) : null);
    public Task SaveResultAsync(Guid analysisId, IReadOnlyCollection<VideoAnalysisCrop> crops, CancellationToken cancellationToken = default)
    {
        Crops[analysisId] = crops.ToList();
        return Task.CompletedTask;
    }
    public Task<bool> DiscardChangesAsync(MatchVideoAnalysis analysis, CancellationToken cancellationToken = default) => Task.FromResult(Items.Contains(analysis));
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
    public VideoAnalysisUpdate[] Updates = [];
    public Action? BeforeProgress;
    public Action? AfterEach;
    public Action? AfterUpdates;
    public Action? OnAnalyze;
    public Func<VideoAnalysisControl, CancellationToken, Task>? WhileRunning;
    public VideoAnalysisRequest? Request;
    public readonly List<Guid> DeletedOutputs = [];

    public async Task<VideoAnalysisRun> AnalyzeAsync(VideoAnalysisRequest request, Func<VideoAnalysisUpdate, CancellationToken, Task> report,
        VideoAnalysisControl control, CancellationToken cancellationToken)
    {
        Request = request;
        OnAnalyze?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        foreach (var update in ProgressSteps.Select(step => new VideoAnalysisUpdate(Percent: step)).Concat(Updates))
        {
            BeforeProgress?.Invoke();
            await report(update, cancellationToken);
            AfterEach?.Invoke();
        }
        AfterUpdates?.Invoke();
        if (WhileRunning is not null) await WhileRunning(control, cancellationToken);
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
    public Func<Exception>? Failure;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (Failure is not null) throw Failure();
        Saves++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeAnalyzerHeartbeats : IVideoAnalyzerHeartbeatStore
{
    public DateTimeOffset? LastSeen;

    public Task BeatAsync(string workerId, DateTimeOffset startedAt, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        LastSeen = now;
        return Task.CompletedTask;
    }
    public Task<DateTimeOffset?> LastSeenAsync(CancellationToken cancellationToken = default) => Task.FromResult(LastSeen);
    public Task RemoveAsync(string workerId, CancellationToken cancellationToken = default)
    {
        LastSeen = null;
        return Task.CompletedTask;
    }
}
