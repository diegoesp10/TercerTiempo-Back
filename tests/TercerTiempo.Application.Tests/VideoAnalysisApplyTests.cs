using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Squads.Suspensions;
using TercerTiempo.Application.Videos.Analysis;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

// La propuesta del vídeo en la API: quién la ve, quién la relanza y cómo se vuelca al acta interna.
public class VideoAnalysisApplyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task The_staff_sees_the_state_of_each_video_and_the_proposal_when_it_is_ready()
    {
        var f = new Fixture();
        var waiting = await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default);
        Assert.True(waiting is { Enabled: true, CanManage: true });
        Assert.Equal(VideoAnalysisState.NotRequested, Assert.Single(waiting.Videos).State);

        f.Analyzed();
        var ready = Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);

        Assert.Equal(VideoAnalysisState.Ready, ready.State);
        Assert.Equal((19.3m, 67.1m, true, false), (ready.Quality!.BallFramesPct, ready.Quality.CalibratedFramesPct, ready.Quality.LowBall, ready.Quality.LowCalibration));
        Assert.Equal([(0, 38, 2), (1, 62, 1)], ready.Teams.Select(t => (t.Index, t.PossessionPct ?? -1, t.Players)));
        Assert.Equal([696, 781, 12], ready.Players.Select(p => p.Tracklet));
        // Solo tienen recorte los jugadores cuyo recorte guardó el analizador.
        Assert.Equal($"/api/squads/{f.Squad.Id}/matches/{f.Played.Id}/video-analysis/crops/696?videoId={f.Video.Id}&v={f.Analysis.FinishedAt!.Value.ToUnixTimeSeconds()}", ready.Players[0].CropUrl);
        Assert.Null(ready.Players[2].CropUrl);
        Assert.All(ready.Players, p => Assert.Null(p.SquadPlayerId));
    }

    [Fact]
    public async Task With_the_official_sheet_each_player_comes_by_shirt_number_with_our_team_and_squad_player_proposed()
    {
        var f = new Fixture();
        f.Sheet = OfficialMatchSheet.Create(f.Played.Id, "test", "1", null, Now);
        f.Sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 2, 1,
        [
            Line(MatchSide.Home, 9, "FERNANDEZ, PELAYO"),
            Line(MatchSide.Home, 1, "SUAREZ, NEL"),
            Line(MatchSide.Away, 9, "GARCIA, JUAN"),
        ], []), Now);
        // El analizador dejó a cada jugador por su dorsal: el equipo 1 lleva los dorsales del local (el Vallobín).
        f.Analyzed("""
            {"version": 2, "identificados": true,
             "equipos": [{"id": 0, "lado": "visitante", "color": "#c03030"}, {"id": 1, "lado": "local", "color": "#20304a"}],
             "jugadores": [
               {"tracklet": 901009, "equipo": 1, "dorsal": 9, "nombreActa": "FERNANDEZ, PELAYO", "confianzaDorsal": 0.71, "tramos": 12, "minutosALaVista": 30},
               {"tracklet": 901001, "equipo": 1, "portero": true, "dorsal": 1, "nombreActa": "SUAREZ, NEL", "confianzaDorsal": 0.6, "tramos": 3, "minutosALaVista": 40},
               {"tracklet": 900009, "equipo": 0, "dorsal": 9, "nombreActa": "GARCIA, JUAN", "confianzaDorsal": 0.5, "tramos": 8, "minutosALaVista": 25}],
             "calidad": {"sinIdentificarS": {"0": 300.4, "1": 120}}}
            """);

        var ready = Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);

        Assert.True(ready.Identified);
        Assert.Equal(1, ready.SuggestedOurTeam);
        Assert.Equal([MatchSide.Away, MatchSide.Home], ready.Teams.Select(t => t.Side!.Value));
        Assert.Equal(new Dictionary<int, int> { [0] = 300, [1] = 120 }, ready.UnidentifiedSeconds);
        // Por equipo y dorsal; los nuestros con la ficha que el acta cruza con ese dorsal, el rival sin ninguna.
        Assert.Equal([(0, 9), (1, 1), (1, 9)], ready.Players.Select(p => (p.Team, p.ShirtNumber!.Value)));
        var pelayo = ready.Players.Single(p => p.Tracklet == 901009);
        Assert.Equal((f.Pelayo.Id, "FERNANDEZ, PELAYO", 0.71m), (pelayo.SuggestedSquadPlayerId!.Value, pelayo.SheetName, pelayo.ShirtConfidence!.Value));
        Assert.NotNull(ready.Players.Single(p => p.Tracklet == 901001).SuggestedSquadPlayerId);
        Assert.Null(ready.Players.Single(p => p.Tracklet == 900009).SuggestedSquadPlayerId);
    }

    [Fact]
    public async Task Each_action_of_the_proposal_comes_with_its_second_in_the_video_to_watch_it()
    {
        var f = new Fixture();
        f.Analyzed("""
            {"version": 2,
             "equipos": [{"id": 0, "teamStats": {"cornersFor": 2, "throwInsFor": 1}, "momentos": {"corners": [610.0, 120.5], "throwIns": [33.2]}},
                         {"id": 1, "teamStats": {"foulsCommitted": 1}, "momentos": {"fouls": [400.0]}}],
             "jugadores": [
               {"tracklet": 696, "equipo": 0, "minutosALaVista": 30, "propuesta": {"passes": 2, "passesFailed": 1, "recoveries": 1},
                "momentos": {"passes": [12.0, 250.3], "passesFailed": [260.0], "recoveries": [240.0]}},
               {"tracklet": 781, "equipo": 0, "minutosALaVista": 20, "propuesta": {"passes": 3}}]}
            """);

        var ready = Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);

        // Con el tipo del archivo, para reproducirlo en cada momento.
        Assert.Equal(f.Video.ContentType, ready.ContentType);
        var ours = ready.Teams.Single(t => t.Index == 0).Moments!;
        Assert.Equal([120.5m, 610.0m], ours.Corners);
        Assert.Equal([33.2m], ours.ThrowIns);
        Assert.Equal([400.0m], ready.Teams.Single(t => t.Index == 1).Moments!.Fouls);
        var moments = ready.Players.Single(p => p.Tracklet == 696).Moments!;
        Assert.Equal([12.0m, 250.3m], moments.PassesCompleted);
        Assert.Equal([260.0m], moments.PassesFailed);
        Assert.Equal([240.0m], moments.Recoveries);
        // Un jugador sin momentos (un análisis anterior a Ojeador 2.1.2) no tiene nada que ver: null, no listas vacías.
        Assert.Null(ready.Players.Single(p => p.Tracklet == 781).Moments);
    }

    [Fact]
    public async Task Without_the_official_sheet_nothing_is_proposed()
    {
        var f = new Fixture();
        f.Analyzed();
        var ready = Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);
        Assert.False(ready.Identified);
        Assert.Null(ready.SuggestedOurTeam);
        Assert.All(ready.Players, p => Assert.Null(p.SuggestedSquadPlayerId));
    }

    private static OfficialLineData Line(MatchSide side, int shirt, string name) => new(side, shirt, name, true, 0, null, 90, 0, 0, 0, 0, false);

    [Fact]
    public async Task The_staff_is_told_when_no_analyzer_is_running()
    {
        var f = new Fixture();
        Assert.False((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).AnalyzerOnline);

        f.Heartbeats.LastSeen = DateTimeOffset.UtcNow.AddSeconds(-40);
        var online = await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default);
        Assert.True(online.AnalyzerOnline);
        Assert.Equal(f.Heartbeats.LastSeen, online.AnalyzerSeenAt);

        // Tres minutos sin latido: el analizador se paró (o se colgó el equipo).
        f.Heartbeats.LastSeen = DateTimeOffset.UtcNow.AddMinutes(-3);
        Assert.False((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).AnalyzerOnline);
    }

    [Fact]
    public async Task The_staff_cancels_what_is_queued_and_only_what_is_queued_or_running()
    {
        var f = new Fixture();
        var notActive = await Assert.ThrowsAsync<ConflictException>(() => f.Cancel(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, null), default));
        Assert.Equal(ErrorCode.VideoAnalysisNotActive, notActive.Code);

        await f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, f.Video.Id), default);
        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Cancel(new Admin(Guid.NewGuid())).Handle(new(f.Squad.Id, f.Played.Id, null), default))).Code);

        var cancelled = await f.Cancel(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, f.Video.Id), default);

        Assert.Equal(VideoAnalysisState.Cancelled, Assert.Single(cancelled.Videos).State);
        Assert.Equal(VideoAnalysisStatus.Cancelled, f.Analysis.Status);
        // Se puede volver a pedir.
        var again = await f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, f.Video.Id), default);
        Assert.Equal(VideoAnalysisState.Pending, Assert.Single(again.Videos).State);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Cancel(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, Guid.NewGuid()), default));
    }

    [Fact]
    public async Task A_video_still_being_compressed_waits_and_no_video_means_an_empty_list()
    {
        var f = new Fixture();
        f.Videos.Items.Clear();
        Assert.Empty((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);

        var uploading = MatchVideo.Upload(f.Squad.Id, f.Team.Id, f.Played.Id, f.Squad.SeasonId, null, VideoPart.Full, "Partido", "partido.mp4", "video/mp4",
            50_000_000, "original.mp4", Now, f.Club.Id);
        f.Videos.Items.Add(uploading);

        Assert.Equal(VideoAnalysisState.WaitingForVideo, Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos).State);
        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, null), default));
        Assert.Equal(ErrorCode.VideoAnalysisVideoNotReady, error.Code);
    }

    [Fact]
    public async Task The_admin_only_looks_and_strangers_see_nothing()
    {
        var f = new Fixture();
        f.Analyzed();
        var admin = new Admin(Guid.NewGuid());
        var other = Club.Register(Guid.NewGuid(), "Real Titánico", Now);
        other.AddTeam(f.Rival.Id, Now);
        f.OtherClubs.Add(other);
        var stranger = new User(ClubId: other.Id, UserId: other.UserId);

        var seen = await f.Get(admin).Handle(new(f.Squad.Id, f.Played.Id), default);
        Assert.False(seen.CanManage);
        Assert.Equal(VideoAnalysisState.Ready, Assert.Single(seen.Videos).State);
        // Quién analizó lo ve todo el que ve la propuesta; con qué modelos, solo el administrador.
        Assert.Equal(("Ojeador", "0.1.0", "balon: afinados/balon.pt"), (seen.Videos[0].AnalyzerName, seen.Videos[0].AnalyzerVersion, seen.Videos[0].Weights));
        var staff = Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);
        Assert.Equal(("Ojeador", "0.1.0", (string?)null), (staff.AnalyzerName, staff.AnalyzerVersion, staff.Weights));
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], (await f.Crop(admin).Handle(new(f.Squad.Id, f.Played.Id, 696, null), default)).Content);

        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Request(admin).Handle(new(f.Squad.Id, f.Played.Id, null), default))).Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Apply(admin).Handle(f.Everything(), default))).Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Get(stranger).Handle(new(f.Squad.Id, f.Played.Id), default))).Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Crop(stranger).Handle(new(f.Squad.Id, f.Played.Id, 696, null), default))).Code);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Asking_again_forgets_the_old_proposal_and_is_refused_while_queued_or_disabled()
    {
        var f = new Fixture();
        f.Analyzed();

        var requested = await f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, f.Video.Id), default);

        Assert.Equal(VideoAnalysisState.Pending, Assert.Single(requested.Videos).State);
        Assert.Empty(requested.Videos[0].Players);
        Assert.Null(f.Analysis.ProposalJson);
        Assert.Equal(1, f.Signal.Wakes);

        Assert.Equal(ErrorCode.VideoAnalysisAlreadyQueued,
            (await Assert.ThrowsAsync<ConflictException>(() => f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, f.Video.Id), default))).Code);
        Assert.Equal(ErrorCode.VideoNotFound,
            (await Assert.ThrowsAsync<NotFoundException>(() => f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, Guid.NewGuid()), default))).Code);

        var disabled = new Fixture(enabled: false);
        Assert.False((await disabled.Get(disabled.ClubUser).Handle(new(disabled.Squad.Id, disabled.Played.Id), default)).Enabled);
        Assert.Equal(ErrorCode.VideoAnalysisDisabled,
            (await Assert.ThrowsAsync<ConflictException>(() => disabled.Request(disabled.ClubUser).Handle(new(disabled.Squad.Id, disabled.Played.Id, null), default))).Code);
    }

    [Fact]
    public async Task What_the_staff_accepts_goes_to_the_sheet_in_its_name_and_the_assignment_is_remembered()
    {
        var f = new Fixture();
        f.Analyzed();

        var result = await f.Apply(f.ClubUser).Handle(f.Everything(), default);

        // Datos de equipo, vistos desde el equipo que el técnico dijo que era el suyo (el 0).
        var team = f.TeamStats.Single();
        Assert.Equal((38, 4, 9, 2, 5, 3, 1, 11, 14, 6, 4),
            (team.PossessionPct, team.ShotsFor, team.ShotsAgainst, team.ShotsOnTargetFor, team.ShotsOnTargetAgainst, team.CornersFor, team.CornersAgainst,
                team.ThrowInsFor, team.ThrowInsAgainst, team.GoalKicksFor, team.GoalKicksAgainst));
        Assert.Equal(f.Club.Id, team.RecordedByClubId);
        // Con qué analizador salieron del vídeo, también en el acta que se devuelve.
        Assert.Equal(("Ojeador 0.1.0", team.UpdatedAt), (team.VideoAnalyzer, team.FromVideoAt));
        Assert.Equal("Ojeador 0.1.0", result.Sheet.TeamStats!.VideoAnalyzer);

        // Los dos tramos de Pelayo se suman en su línea, que se crea con los minutos que se le ha visto.
        var line = Assert.Single(f.Performances);
        Assert.Equal((f.Pelayo.Id, 20, false, 4, 2, 1, 6, 6, 25, 31),
            (line.SquadPlayerId, line.MinutesPlayed, line.Started, line.Shots, line.ShotsOnTarget, line.Goals, line.Recoveries, line.BallLosses, line.PassesCompleted, line.PassesAttempted));
        Assert.Equal(f.Club.Id, line.RecordedByClubId);
        Assert.NotNull(line.Rating);

        Assert.Equal((11, 1, 0), (result.TeamValuesWritten, result.LinesWritten, result.PlayersWithoutChanges));
        Assert.Equal(1, f.Saves);
        Assert.NotNull(result.Sheet.TeamStats);
        Assert.NotNull(result.Sheet.Entries.Single(e => e.Player.Id == f.Pelayo.Id).Performance);

        var part = Assert.Single(result.Analysis.Videos);
        Assert.Equal(0, part.OurTeam);
        Assert.NotNull(part.AppliedAt);
        Assert.Equal([f.Pelayo.Id, f.Pelayo.Id], part.Players.Where(p => p.Team == 0).Select(p => p.SquadPlayerId!.Value));
    }

    [Fact]
    public async Task Data_already_in_the_sheet_is_only_replaced_after_an_explicit_confirmation()
    {
        var f = new Fixture();
        f.Analyzed();
        f.Performances.Add(PlayerMatchPerformance.Record(f.Squad, f.Pelayo, f.Played, SquadEditor.Club(f.Club),
            new PerformanceStats(true, 90, 2, 1, 6, 3, 0, 2, 0, 0, 1, false, "Muy activo"), Now));
        f.TeamStats.Add(TeamMatchStats.Record(f.Squad, f.Played, SquadEditor.Club(f.Club),
            new TeamMatchStatsData(55, 3, 1, 4, 9, 2, 5, 0, 0, 12, 9, 0, 0, "Presión alta"), Now));

        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Apply(f.ClubUser).Handle(f.Everything(), default));

        Assert.Equal(ErrorCode.VideoAnalysisOverwrite, error.Code);
        Assert.Equal("posesión (55 → 38), tiros de Pelayo Fernández (6 → 4), tiros a puerta de Pelayo Fernández (3 → 2)", error.Args.Single());
        Assert.Equal(0, f.Saves);
        Assert.Equal((55, 6, 2), (f.TeamStats.Single().PossessionPct, f.Performances.Single().Shots, f.Performances.Single().Goals));
        Assert.Null(f.Analysis.AppliedAt);

        await f.Apply(f.ClubUser).Handle(f.Everything() with { Overwrite = true }, default);

        var line = f.Performances.Single();
        // Lo que el vídeo no sabe (minutos, titular, asistencias, tarjetas, notas) se queda como estaba, y sus goles también.
        Assert.Equal((90, true, 2, 1, 1, "Muy activo"), (line.MinutesPlayed, line.Started, line.Goals, line.Assists, line.YellowCards, line.Notes));
        Assert.Equal((4, 2, 6, 25, 31), (line.Shots, line.ShotsOnTarget, line.Recoveries, line.PassesCompleted, line.PassesAttempted));
        Assert.Equal((38, 12, "Presión alta"), (f.TeamStats.Single().PossessionPct, f.TeamStats.Single().FoulsCommitted, f.TeamStats.Single().Notes));
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public async Task With_the_closed_official_sheet_minutes_come_from_it_also_for_who_the_video_did_not_see()
    {
        var f = new Fixture();
        f.Sheet = ClosedSheet(f, closed: true);
        f.Analyzed();
        Assert.True((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).OfficialSheetClosed);

        var result = await f.Apply(f.ClubUser).Handle(f.Everything(), default);

        // Pelayo: sus acciones son las del vídeo, pero los minutos (salió en el 70), la titularidad, el gol y la amarilla
        // son los del acta, aunque el vídeo solo lo viera 20 minutos.
        var pelayo = f.Performances.Single(p => p.SquadPlayerId == f.Pelayo.Id);
        Assert.Equal((70, true, 1, 1), (pelayo.MinutesPlayed, pelayo.Started, pelayo.Goals, pelayo.YellowCards));
        Assert.Equal((4, 2, 31), (pelayo.Shots, pelayo.ShotsOnTarget, pelayo.PassesAttempted));
        // Nel no salió en el vídeo, pero jugó el partido entero: su línea sale del acta.
        var nel = f.Performances.Single(p => p.SquadPlayerId != f.Pelayo.Id);
        Assert.Equal((90, true, 0), (nel.MinutesPlayed, nel.Started, nel.Shots));
        Assert.Equal((2, 0, 1), (result.LinesWritten, result.PlayersWithoutChanges, result.LinesFromOfficialSheet));

        // En la nota, los goles encajados son los de mientras estaba en el campo (el del rival, en el 80: Pelayo ya se había
        // ido) y las acciones del vídeo no cuentan hasta que el cuerpo técnico guarda el acta.
        Assert.Equal((true, 0), (pelayo.ActionsFromVideo, pelayo.GoalsConcededOnPitch));
        Assert.Equal((false, 1), (nel.ActionsFromVideo, nel.GoalsConcededOnPitch));
        Assert.True(result.Sheet.Entries.Single(e => e.Player.Id == f.Pelayo.Id).Performance!.ActionsFromVideo);
        var withoutActions = pelayo.Rating;

        await f.Record(f.ClubUser).Handle(new RecordMatchSheetCommand(f.Squad.Id, f.Played.Id, [MatchSheetEntryInput.From(pelayo), MatchSheetEntryInput.From(nel)]), default);

        Assert.False(pelayo.ActionsFromVideo);
        Assert.True(pelayo.Rating > withoutActions);
        Assert.Equal(0, pelayo.GoalsConcededOnPitch);
    }

    [Fact]
    public async Task A_provisional_sheet_or_unticking_it_leaves_the_minutes_to_the_video()
    {
        foreach (var (closed, fill) in new[] { (false, true), (true, false) })
        {
            var f = new Fixture();
            f.Sheet = ClosedSheet(f, closed);
            f.Analyzed();

            var result = await f.Apply(f.ClubUser).Handle(f.Everything() with { FillFromOfficialSheet = fill }, default);

            var line = Assert.Single(f.Performances);
            Assert.Equal((20, false, 1), (line.MinutesPlayed, line.Started, line.Goals));
            Assert.Equal(0, result.LinesFromOfficialSheet);
        }
    }

    [Fact]
    public async Task The_shirt_number_in_the_internal_sheet_is_always_the_one_in_the_official_sheet()
    {
        var f = new Fixture();
        var nel = f.Squad.ActivePlayers.Single(p => p.Id != f.Pelayo.Id);
        RecordMatchSheetCommand Both() => new(f.Squad.Id, f.Played.Id, [PlayedLine(f.Pelayo.Id), PlayedLine(nel.Id)]);

        // Sin acta oficial no hay dorsal: no se escribe a mano.
        var before = await f.Record(f.ClubUser).Handle(Both(), default);
        Assert.All(f.Performances, p => Assert.Null(p.ShirtNumber));
        Assert.False(before.OfficialSheet);
        Assert.All(before.Entries, e => Assert.Null(e.OfficialShirtNumber));

        // Con ella, el de cada uno en el acta aunque su ficha diga otro: Pelayo (el 9 en la ficha) jugó con el 14.
        f.Sheet = OfficialMatchSheet.Create(f.Played.Id, "test", "1", null, Now);
        f.Sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 2, 1,
        [
            new(MatchSide.Home, 14, "FERNANDEZ, PELAYO", true, 0, 70, 70, 1, 0, 0, 0, false),
            new(MatchSide.Home, 13, "SUAREZ, NEL", true, 0, null, 90, 0, 0, 0, 0, false),
            new(MatchSide.Away, 9, "GARCIA, JUAN", true, 0, null, 90, 1, 0, 0, 0, false),
        ], [new(MatchSide.Away, OfficialEventType.Goal, 80, null, "GARCIA, JUAN", null)]), Now, true);

        var after = await f.Record(f.ClubUser).Handle(Both(), default);

        Assert.Equal((14, 13), (f.Performances.Single(p => p.SquadPlayerId == f.Pelayo.Id).ShirtNumber, f.Performances.Single(p => p.SquadPlayerId == nel.Id).ShirtNumber));
        Assert.True(after.OfficialSheet);
        var pelayo = after.Entries.Single(e => e.Player.Id == f.Pelayo.Id);
        Assert.Equal((14, 14), (pelayo.OfficialShirtNumber, pelayo.Performance!.MatchShirtNumber));
    }

    private static MatchSheetEntryInput PlayedLine(Guid squadPlayerId) =>
        new(squadPlayerId, Played: true, Started: true, MinutesPlayed: 90, Goals: 0, Assists: 0, Shots: 0, ShotsOnTarget: 0,
            KeyPasses: 0, Tackles: 0, Interceptions: 0, Saves: 0, YellowCards: 0, RedCard: false, Notes: null);

    private static OfficialMatchSheet ClosedSheet(Fixture f, bool closed)
    {
        var sheet = OfficialMatchSheet.Create(f.Played.Id, "test", "1", null, Now);
        sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 2, 1,
        [
            new(MatchSide.Home, 9, "FERNANDEZ, PELAYO", true, 0, 70, 70, 1, 0, 0, 1, false),
            new(MatchSide.Home, 1, "SUAREZ, NEL", true, 0, null, 90, 0, 0, 0, 0, false),
            new(MatchSide.Away, 9, "GARCIA, JUAN", true, 0, null, 90, 1, 0, 0, 0, false),
        ], [new(MatchSide.Away, OfficialEventType.Goal, 80, null, "GARCIA, JUAN", null)]), Now, closed);
        return sheet;
    }

    [Fact]
    public async Task Only_what_is_ticked_is_written()
    {
        var f = new Fixture();
        f.Analyzed();

        var result = await f.Apply(f.ClubUser).Handle(
            new(f.Squad.Id, f.Played.Id, [new ApplyVideoPart(f.Video.Id, 1, [])], [VideoTeamField.Possession, VideoTeamField.Corners], false), default);

        var team = f.TeamStats.Single();
        Assert.Equal((62, 1, 3, 0, 0, 0), (team.PossessionPct, team.CornersFor, team.CornersAgainst, team.ShotsFor, team.ThrowInsFor, team.GoalKicksFor));
        Assert.Empty(f.Performances);
        Assert.Equal((3, 0), (result.TeamValuesWritten, result.LinesWritten));
        Assert.Equal(1, f.Analysis.OurTeam);
    }

    [Fact]
    public async Task A_tracked_player_must_exist_be_of_the_chosen_team_and_go_once()
    {
        var f = new Fixture();
        f.Analyzed();
        ApplyVideoAnalysisCommand With(params VideoTrackletAssignment[] players) =>
            new(f.Squad.Id, f.Played.Id, [new ApplyVideoPart(f.Video.Id, 0, players)], [], false);

        Assert.Equal(ErrorCode.VideoAnalysisTrackletUnknown,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(new VideoTrackletAssignment(5, f.Pelayo.Id)), default))).Code);
        Assert.Equal(ErrorCode.VideoAnalysisTrackletOtherTeam,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(new VideoTrackletAssignment(12, f.Pelayo.Id)), default))).Code);
        Assert.Equal(ErrorCode.VideoAnalysisTrackletRepeated,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(new(696, f.Pelayo.Id), new(696, f.Pelayo.Id)), default))).Code);
        Assert.Equal(ErrorCode.VideoAnalysisNothingSelected,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(), default))).Code);
        Assert.Equal(0, f.Saves);
        Assert.Empty(f.Performances);
    }

    [Fact]
    public async Task With_the_official_sheet_our_team_is_the_one_on_our_side_and_rival_players_never_go_to_our_squad()
    {
        var f = new Fixture();
        f.Sheet = OfficialMatchSheet.Create(f.Played.Id, "test", "1", null, Now);
        f.Sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 2, 1,
            [Line(MatchSide.Home, 9, "FERNANDEZ, PELAYO"), Line(MatchSide.Away, 9, "GARCIA, JUAN")], []), Now);
        // Por los dorsales, el equipo 1 es el local (el Vallobín) y el 0 el rival.
        f.Analyzed("""
            {"version": 2, "identificados": true,
             "equipos": [{"id": 0, "lado": "visitante", "teamStats": {"possessionPct": 35, "possessionConfidence": "alta", "shotsFor": 2}},
                         {"id": 1, "lado": "local", "teamStats": {"possessionPct": 65, "possessionConfidence": "alta", "shotsFor": 7}}],
             "jugadores": [
               {"tracklet": 901009, "equipo": 1, "dorsal": 9, "nombreActa": "FERNANDEZ, PELAYO", "minutosALaVista": 30},
               {"tracklet": 900009, "equipo": 0, "dorsal": 9, "nombreActa": "GARCIA, JUAN", "minutosALaVista": 25}]}
            """);
        ApplyVideoAnalysisCommand Saying(int ourTeam, VideoTrackletAssignment[] players, VideoTeamField[] fields) =>
            new(f.Squad.Id, f.Played.Id, [new ApplyVideoPart(f.Video.Id, ourTeam, players)], fields, false);

        // Aunque la petición diga que el nuestro es el rival, sus jugadores no van a nuestras fichas...
        Assert.Equal(ErrorCode.VideoAnalysisTrackletOtherTeam,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(Saying(0, [new(900009, f.Pelayo.Id)], []), default))).Code);
        Assert.Equal(0, f.Saves);

        // ...y los datos de equipo se cuentan desde nuestro lado, que es lo que queda guardado.
        var result = await f.Apply(f.ClubUser).Handle(Saying(0, [], [VideoTeamField.Possession, VideoTeamField.Shots]), default);
        var team = f.TeamStats.Single();
        Assert.Equal((65, 7, 2), (team.PossessionPct, team.ShotsFor, team.ShotsAgainst));
        Assert.Equal(1, Assert.Single(result.Analysis.Videos).OurTeam);
    }

    [Fact]
    public async Task Nothing_is_applied_until_the_analysis_is_ready_or_for_a_match_not_played()
    {
        var f = new Fixture();
        f.Analyses.Items.Add(MatchVideoAnalysis.Queue(f.Video, Now));

        Assert.Equal(ErrorCode.VideoAnalysisNotReady,
            (await Assert.ThrowsAsync<ConflictException>(() => f.Apply(f.ClubUser).Handle(f.Everything(), default))).Code);
        Assert.Equal(0, f.Saves);
    }

    private sealed record User(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Abstractions.Identity.AccountType.Club;
    }

    private sealed record Admin(Guid? UserId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => null;
        public Guid? PlayerId => null;
        public Guid? CoachId => null;
        public Guid? ClubId => null;
        public bool IsAdmin => true;
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public List<Club> OtherClubs { get; } = [];
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Rival { get; } = Team.Create("Real Titánico", "TIT", null);
        public Competition Competition { get; } = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        public Match Played { get; }
        public Squad Squad { get; }
        public SquadPlayer Pelayo { get; }
        public MatchVideo Video { get; }
        public MatchVideoAnalysis Analysis => Analyses.Items.Single();
        public FakeVideoStore Videos { get; } = new();
        public FakeAnalysisStore Analyses { get; } = new();
        public FakeAnalysisSignal Signal { get; } = new();
        public FakeAnalyzerHeartbeats Heartbeats { get; } = new();
        public List<PlayerMatchPerformance> Performances { get; } = [];
        public List<TeamMatchStats> TeamStats { get; } = [];
        public User ClubUser => new(ClubId: Club.Id, UserId: Club.UserId);
        public OfficialMatchSheet? Sheet { get; set; }
        public int Saves { get; private set; }
        private readonly bool _enabled;

        public Fixture(bool enabled = true)
        {
            _enabled = enabled;
            var season = Competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
            Competition.MarkCurrent(season.Id);
            Club.AddTeam(Team.Id, Now);
            Played = Match.Schedule(season.Id, 1, Team.Id, Rival.Id, Now.AddDays(-2), null, null);
            Played.RecordResult(2, 1, Now.AddDays(-2).AddHours(2));
            Squad = Squad.Open(Team.Id, season.Id, Now);
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, Now);
            Squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, Now);

            Video = MatchVideo.Upload(Squad.Id, Team.Id, Played.Id, season.Id, null, VideoPart.Full, "Partido", "partido.mp4", "video/mp4",
                50_000_000, "original.mp4", Now.AddHours(-3), Club.Id);
            Video.StartProcessing(Now.AddHours(-3));
            Video.KeepOriginal(5782, 1080, Now.AddHours(-2));
            Videos.Items.Add(Video);
            Analyses.Videos = Videos;
        }

        // El analizador ya ha pasado por el vídeo: propuesta lista y los recortes de dos de los tres jugadores.
        public void Analyzed(string? proposalJson = null)
        {
            var analysis = MatchVideoAnalysis.Queue(Video, Now.AddHours(-2));
            analysis.Start(Now.AddHours(-2));
            var run = VideoAnalysisSamples.Run();
            analysis.Complete(new VideoAnalysisOutcome(run.OutputPath, run.AnalyzerName, run.AnalyzerVersion, run.Weights, proposalJson ?? run.ProposalJson, run.Frames,
                run.BallFramesPct, run.CalibratedFramesPct, run.AnalyzedSeconds, run.PitchLengthM, run.PitchWidthM), Now.AddHours(-1));
            Analyses.Items.Add(analysis);
            Analyses.Crops[analysis.Id] = run.Crops.Select(c => new VideoAnalysisCrop(analysis.Id, c.Key, c.Value)).ToList();
        }

        // Nuestro equipo es el 0, sus dos jugadores seguidos son Pelayo y se aceptan todos los datos de equipo.
        public ApplyVideoAnalysisCommand Everything() => new(
            Squad.Id, Played.Id,
            [new ApplyVideoPart(Video.Id, 0, [new(696, Pelayo.Id), new(781, Pelayo.Id)])],
            Enum.GetValues<VideoTeamField>(), false);

        private ISquadStore SquadStore => TestStub.Of<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Squad.Id ? Squad : null),
            _ => throw new NotSupportedException(method),
        });
        private IClubStore Clubs => TestStub.Of<IClubStore>((_, args) =>
            Task.FromResult(OtherClubs.Append(Club).FirstOrDefault(c => c.Id == (Guid)args![0]! || c.UserId == (Guid)args[0]!)));
        private IOfficialSheetStore Sheets => TestStub.Of<IOfficialSheetStore>((method, _) => method switch
        {
            nameof(IOfficialSheetStore.GetByMatchAsync) => Task.FromResult(Sheet),
            nameof(IOfficialSheetStore.GetAvailableLinesByMatchIdsAsync) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>(new[] { Sheet }.OfType<OfficialMatchSheet>().ToList()),
            _ => throw new NotSupportedException(method),
        });
        private SuspensionReader Suspensions => new(Matches, PerformanceStore, Sheets, new FakeCommitteeStore(), Teams, TimeProvider.System);
        private ICoachStore Coaches => TestStub.Of<ICoachStore>((method, _) => throw new NotSupportedException(method));
        private IMatchStore Matches => TestStub.Of<IMatchStore>((method, args) => method switch
        {
            nameof(IMatchStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Played.Id ? Played : null),
            nameof(IMatchStore.GetTeamMatchesAsync) => Task.FromResult<IReadOnlyList<Match>>([Played]),
            _ => throw new NotSupportedException(method),
        });
        private ITeamStore Teams => TestStub.Of<ITeamStore>((method, args) => method switch
        {
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team, Rival]),
            nameof(ITeamStore.GetByIdAsync) => Task.FromResult(new[] { Team, Rival }.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ICompetitionStore Competitions => TestStub.Of<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(Competition));
        private IPerformanceStore PerformanceStore => TestStub.Of<IPerformanceStore>((method, args) => method switch
        {
            nameof(IPerformanceStore.GetBySquadAndMatchAsync) => Task.FromResult<IReadOnlyList<PlayerMatchPerformance>>(
                Performances.Where(p => p.SquadId == (Guid)args![0]! && p.MatchId == (Guid)args[1]!).ToList()),
            nameof(IPerformanceStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<PlayerMatchPerformance>>(Performances.Where(p => p.SquadId == (Guid)args![0]!).ToList()),
            nameof(IPerformanceStore.Add) => Run(() => Performances.Add((PlayerMatchPerformance)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IPlayerStore Players => TestStub.Of<IPlayerStore>((_, _) => Task.FromResult<IReadOnlyList<Player>>([]));
        private ITeamMatchStatsStore TeamStatsStore => TestStub.Of<ITeamMatchStatsStore>((method, args) => method switch
        {
            nameof(ITeamMatchStatsStore.GetAsync) => Task.FromResult(TeamStats.FirstOrDefault(s => s.SquadId == (Guid)args![0]! && s.MatchId == (Guid)args[1]!)),
            nameof(ITeamMatchStatsStore.Add) => Run(() => TeamStats.Add((TeamMatchStats)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IUnitOfWork Unit => TestStub.Of<IUnitOfWork>((_, _) => Task.FromResult(++Saves));
        private static ICallUpStore NoCallUps => TestStub.Of<ICallUpStore>((method, _) => method switch
        {
            nameof(ICallUpStore.GetByMatchAsync) => Task.FromResult<MatchCallUp?>(null),
            _ => throw new NotSupportedException(method),
        });
        private static object? Run(Action action) { action(); return null; }

        private VideoAnalysisReader Reader(ICurrentUser user) =>
            new(user, SquadStore, Clubs, Matches, Videos, Analyses, Heartbeats, Sheets, Options.Create(new VideoAnalysisOptions { Enabled = _enabled }), TimeProvider.System);

        public GetVideoAnalysisHandler Get(ICurrentUser user) => new(Reader(user));

        public RequestVideoAnalysisHandler Request(ICurrentUser user) => new(Reader(user), Analyses, Signal, Unit, TimeProvider.System);

        public GetVideoAnalysisCropHandler Crop(ICurrentUser user) => new(Reader(user), Analyses);

        public CancelVideoAnalysisHandler Cancel(ICurrentUser user) => new(Reader(user), Unit, TimeProvider.System);

        public RecordMatchSheetHandler Record(ICurrentUser user) =>
            new(user, Coaches, Clubs, SquadStore, Matches, Teams, Competitions, Players, PerformanceStore, TeamStatsStore,
                new MatchSheetWriter(PerformanceStore, TeamStatsStore, new OfficialSheetRatings(SquadStore, Matches, Sheets, PerformanceStore), Suspensions), Unit, NoCallUps,
                Suspensions, Sheets, TimeProvider.System);

        public ApplyVideoAnalysisHandler Apply(ICurrentUser user) =>
            new(Reader(user), user, Coaches, Clubs, Teams, Competitions, Players, PerformanceStore, TeamStatsStore,
                new MatchSheetWriter(PerformanceStore, TeamStatsStore, new OfficialSheetRatings(SquadStore, Matches, Sheets, PerformanceStore), Suspensions), Unit, NoCallUps,
                Suspensions, Sheets, TimeProvider.System);
    }
}
