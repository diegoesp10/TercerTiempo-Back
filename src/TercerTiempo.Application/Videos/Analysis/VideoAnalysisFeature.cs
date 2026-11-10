using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Squads.TeamStats;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos.Analysis;

// Quién ve la propuesta del vídeo: los mismos que escriben el acta interna (el club propietario y el cuerpo técnico
// vigente) y el administrador, que solo mira.
internal sealed class VideoAnalysisReader(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchStore matches,
    IMatchVideoStore videos,
    IMatchVideoAnalysisStore analyses,
    IOptions<VideoAnalysisOptions> options,
    TimeProvider clock)
{
    public bool Enabled => options.Value.Enabled;

    public async Task<VideoAnalysisScope> ScopeAsync(Guid squadId, Guid matchId, bool managersOnly, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthenticatedException();

        var squad = await squads.GetByIdAsync(squadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, squadId);
        var manager = await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken);
        if (!manager && (managersOnly || !currentUser.IsAdmin))
            throw new ForbiddenException(ErrorCode.SquadManagersOnly);

        var match = await matches.GetByIdAsync(matchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, matchId);
        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.MatchNotOfSquad);

        var ours = (await videos.GetByMatchAsync(match.Id, cancellationToken))
            .Where(v => v.SquadId == squad.Id && !v.IsDeleted)
            .OrderBy(v => v.Part)
            .ToList();
        var byVideo = (await analyses.GetByVideosAsync(ours.Select(v => v.Id), cancellationToken)).ToDictionary(a => a.VideoId);

        return new VideoAnalysisScope(squad, match, manager, ours, byVideo);
    }

    public async Task<MatchVideoAnalysisDto> BuildAsync(VideoAnalysisScope scope, CancellationToken cancellationToken)
    {
        var parts = new List<VideoAnalysisPartDto>();
        foreach (var video in scope.Videos)
        {
            var analysis = scope.Analyses.GetValueOrDefault(video.Id);
            IReadOnlySet<int> crops = analysis is { Status: VideoAnalysisStatus.Ready }
                ? await analyses.GetCropTrackletsAsync(analysis.Id, cancellationToken)
                : new HashSet<int>();
            parts.Add(VideoAnalysisMapper.ToDto(scope.Squad, video, analysis, crops));
        }
        return new MatchVideoAnalysisDto(Enabled, scope.CanManage, parts);
    }
}

internal sealed record VideoAnalysisScope(
    Squad Squad, Match Match, bool CanManage, IReadOnlyList<MatchVideo> Videos, IReadOnlyDictionary<Guid, MatchVideoAnalysis> Analyses);

public sealed record GetVideoAnalysisQuery(Guid SquadId, Guid MatchId) : IQuery<MatchVideoAnalysisDto>;

internal sealed class GetVideoAnalysisHandler(VideoAnalysisReader reader) : IQueryHandler<GetVideoAnalysisQuery, MatchVideoAnalysisDto>
{
    public async Task<MatchVideoAnalysisDto> Handle(GetVideoAnalysisQuery query, CancellationToken cancellationToken) =>
        await reader.BuildAsync(await reader.ScopeAsync(query.SquadId, query.MatchId, managersOnly: false, cancellationToken), cancellationToken);
}

// Volver a analizar (o analizar por primera vez un vídeo que ya estaba subido). El análisis lo hace el servicio de
// vídeo, que recoge lo que haya en la cola.
public sealed record RequestVideoAnalysisCommand(Guid SquadId, Guid MatchId, Guid? VideoId) : ICommand<MatchVideoAnalysisDto>;

internal sealed class RequestVideoAnalysisValidator : AbstractValidator<RequestVideoAnalysisCommand>
{
    public RequestVideoAnalysisValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
    }
}

internal sealed class RequestVideoAnalysisHandler(
    VideoAnalysisReader reader,
    IMatchVideoAnalysisStore analyses,
    IVideoAnalysisSignal signal,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RequestVideoAnalysisCommand, MatchVideoAnalysisDto>
{
    public async Task<MatchVideoAnalysisDto> Handle(RequestVideoAnalysisCommand command, CancellationToken cancellationToken)
    {
        var scope = await reader.ScopeAsync(command.SquadId, command.MatchId, managersOnly: true, cancellationToken);
        if (!reader.Enabled)
            throw new ConflictException(ErrorCode.VideoAnalysisDisabled);

        var targets = command.VideoId is { } videoId ? scope.Videos.Where(v => v.Id == videoId).ToList() : scope.Videos;
        if (targets.Count == 0)
            throw command.VideoId is { } missing ? new NotFoundException(ErrorCode.VideoNotFound, missing) : new NotFoundException(ErrorCode.VideoAnalysisNoVideo);

        var now = clock.GetUtcNow();
        var queued = 0;
        var created = new Dictionary<Guid, MatchVideoAnalysis>();
        foreach (var video in targets)
        {
            var analysis = scope.Analyses.GetValueOrDefault(video.Id);
            // Con un solo vídeo se dice por qué no se puede; con varios se analiza lo que se pueda.
            var waiting = !MatchVideoAnalysis.CanAnalyze(video);
            if (waiting || analysis is { IsActive: true })
            {
                if (targets.Count > 1) continue;
                throw new ConflictException(waiting ? ErrorCode.VideoAnalysisVideoNotReady : ErrorCode.VideoAnalysisAlreadyQueued);
            }

            if (analysis is null)
            {
                analysis = MatchVideoAnalysis.Queue(video, now);
                analyses.Add(analysis);
                created[video.Id] = analysis;
            }
            else
            {
                analysis.Restart(video, now);
            }
            queued++;
        }
        if (queued == 0)
            throw new ConflictException(ErrorCode.VideoAnalysisAlreadyQueued);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        signal.Wake();

        var all = scope.Analyses.Concat(created).ToDictionary(a => a.Key, a => a.Value);
        return await reader.BuildAsync(scope with { Analyses = all }, cancellationToken);
    }
}

public sealed record VideoTrackletAssignment(int Tracklet, Guid SquadPlayerId);

public sealed record ApplyVideoPart(Guid VideoId, int OurTeam, IReadOnlyList<VideoTrackletAssignment> Players);

public sealed record ApplyVideoAnalysisCommand(
    Guid SquadId,
    Guid MatchId,
    IReadOnlyList<ApplyVideoPart> Videos,
    IReadOnlyList<VideoTeamField> TeamFields,
    bool Overwrite) : ICommand<ApplyVideoAnalysisResultDto>;

internal sealed class ApplyVideoAnalysisValidator : AbstractValidator<ApplyVideoAnalysisCommand>
{
    public ApplyVideoAnalysisValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Videos).NotEmpty().WithCatalogMessage(ErrorCode.VideoAnalysisNothingSelected);
        RuleFor(x => x.Videos).Must(videos => videos.Select(v => v.VideoId).Distinct().Count() == videos.Count)
            .When(x => x.Videos is not null)
            .WithMessage("Cada vídeo solo puede ir una vez.");
        RuleFor(x => x.TeamFields).NotNull();
        RuleForEach(x => x.TeamFields).IsInEnum();
        RuleForEach(x => x.Videos).ChildRules(part =>
        {
            part.RuleFor(p => p.VideoId).NotEmpty();
            part.RuleFor(p => p.OurTeam).InclusiveBetween(0, 1).WithCatalogMessage(ErrorCode.VideoAnalysisTeamInvalid);
            part.RuleFor(p => p.Players).NotNull();
            part.RuleFor(p => p.Players.Count).LessThanOrEqualTo(MatchVideoAnalysis.MaxAssignments).When(p => p.Players is not null);
            part.RuleForEach(p => p.Players).ChildRules(player =>
            {
                player.RuleFor(a => a.Tracklet).GreaterThanOrEqualTo(0);
                player.RuleFor(a => a.SquadPlayerId).NotEmpty();
            });
        });
    }
}

// Vuelca al acta interna lo que el cuerpo técnico ha aceptado de la propuesta: los datos de equipo marcados y, por cada
// jugador seguido que ha asignado a una ficha, sus acciones (sumando los tramos de un mismo jugador y las dos partes
// del partido). Escribe por el mismo camino que el PUT del acta, en nombre de quien lo vuelca.
internal sealed class ApplyVideoAnalysisHandler(
    VideoAnalysisReader reader,
    ICurrentUser currentUser,
    ICoachStore coaches,
    IClubStore clubs,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances,
    ITeamMatchStatsStore teamStats,
    MatchSheetWriter writer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ApplyVideoAnalysisCommand, ApplyVideoAnalysisResultDto>
{
    public async Task<ApplyVideoAnalysisResultDto> Handle(ApplyVideoAnalysisCommand command, CancellationToken cancellationToken)
    {
        var scope = await reader.ScopeAsync(command.SquadId, command.MatchId, managersOnly: true, cancellationToken);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        var squad = scope.Squad;
        var match = scope.Match;
        var now = clock.GetUtcNow();

        var selections = new List<VideoPartSelection>();
        var perPlayer = new Dictionary<Guid, (VideoPlayerActions Actions, int Minutes)>();
        var applied = new List<(MatchVideoAnalysis Analysis, ApplyVideoPart Part)>();

        foreach (var part in command.Videos)
        {
            if (scope.Videos.All(v => v.Id != part.VideoId))
                throw new NotFoundException(ErrorCode.VideoNotFound, part.VideoId);
            if (!scope.Analyses.TryGetValue(part.VideoId, out var analysis))
                throw new NotFoundException(ErrorCode.VideoAnalysisNotFound);
            if (analysis is not { Status: VideoAnalysisStatus.Ready, ProposalJson: { } json })
                throw new ConflictException(ErrorCode.VideoAnalysisNotReady);

            var proposal = VideoProposal.Parse(json);
            var byTracklet = proposal.Players.ToDictionary(p => p.Tracklet);
            var seen = new HashSet<int>();
            foreach (var assignment in part.Players)
            {
                if (!byTracklet.TryGetValue(assignment.Tracklet, out var tracked))
                    throw new DomainException(ErrorCode.VideoAnalysisTrackletUnknown, assignment.Tracklet);
                if (tracked.Team != part.OurTeam)
                    throw new DomainException(ErrorCode.VideoAnalysisTrackletOtherTeam, assignment.Tracklet);
                if (!seen.Add(assignment.Tracklet))
                    throw new DomainException(ErrorCode.VideoAnalysisTrackletRepeated, assignment.Tracklet);

                var player = squad.GetPlayer(assignment.SquadPlayerId);
                var sum = perPlayer.GetValueOrDefault(player.Id, (VideoPlayerActions.None, 0));
                perPlayer[player.Id] = (sum.Item1.Plus(tracked.Actions), sum.Item2 + tracked.MinutesVisible);
            }

            selections.Add(new VideoPartSelection(proposal, part.OurTeam, analysis.AnalyzedSeconds ?? 0));
            applied.Add((analysis, part));
        }

        var fields = command.TeamFields.Distinct().ToList();
        if (fields.Count == 0 && perPlayer.Count == 0)
            throw new DomainException(ErrorCode.VideoAnalysisNothingSelected);

        var overwritten = new List<string>();

        var teamWritten = 0;
        TeamMatchStatsInput? teamInput = null;
        if (fields.Count > 0)
        {
            var current = TeamMatchStatsInput.From(await teamStats.GetAsync(squad.Id, match.Id, cancellationToken));
            var merged = VideoSheetMerge.MergeTeam(current, VideoSheetMerge.Totals(selections), fields, overwritten, ref teamWritten);
            if (merged != current) teamInput = merged;
        }

        var existing = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken)).ToDictionary(p => p.SquadPlayerId);
        var lines = new List<MatchSheetEntryInput>();
        foreach (var (squadPlayerId, (actions, minutes)) in perPlayer)
        {
            var player = squad.GetPlayer(squadPlayerId);
            var current = existing.TryGetValue(squadPlayerId, out var performance) ? MatchSheetEntryInput.From(performance) : null;
            if (VideoSheetMerge.MergeLine(squadPlayerId, player.FullName, current, actions, minutes, overwritten) is { } line)
                lines.Add(line);
        }

        if (overwritten.Count > 0 && !command.Overwrite)
            throw new ConflictException(ErrorCode.VideoAnalysisOverwrite, VideoSheetMerge.Describe(overwritten));

        if (teamInput is not null)
            await writer.WriteTeamStatsAsync(squad, match, editor, teamInput.ToData(), now, cancellationToken);
        if (lines.Count > 0)
            await writer.WriteLinesAsync(squad, match, editor, lines, now, cancellationToken);
        else
            PerformancePolicy.EnsureCanRecord(squad, editor, match, Squad.Today(now));

        foreach (var (analysis, part) in applied)
            analysis.MarkApplied(
                part.OurTeam,
                VideoAnalysisMapper.AssignmentsJson(part.Players.Select(a => KeyValuePair.Create(a.Tracklet, a.SquadPlayerId))),
                now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var sheet = await MatchSheetBuilder.BuildAsync(squad, match, currentUser, clock, teams, competitions, players, performances, teamStats, clubs, cancellationToken);
        return new ApplyVideoAnalysisResultDto(
            sheet, await reader.BuildAsync(scope, cancellationToken), teamInput is null ? 0 : teamWritten, lines.Count, perPlayer.Count - lines.Count);
    }
}

public sealed record GetVideoAnalysisCropQuery(Guid SquadId, Guid MatchId, int Tracklet, Guid? VideoId) : IQuery<VideoAnalysisCropDto>;

internal sealed class GetVideoAnalysisCropHandler(VideoAnalysisReader reader, IMatchVideoAnalysisStore analyses) : IQueryHandler<GetVideoAnalysisCropQuery, VideoAnalysisCropDto>
{
    public async Task<VideoAnalysisCropDto> Handle(GetVideoAnalysisCropQuery query, CancellationToken cancellationToken)
    {
        var scope = await reader.ScopeAsync(query.SquadId, query.MatchId, managersOnly: false, cancellationToken);
        var video = query.VideoId is { } videoId ? scope.Videos.FirstOrDefault(v => v.Id == videoId) : scope.Videos.FirstOrDefault();
        if (video is null || !scope.Analyses.TryGetValue(video.Id, out var analysis) || analysis.Status != VideoAnalysisStatus.Ready)
            throw new NotFoundException(ErrorCode.VideoAnalysisCropNotFound, query.Tracklet);

        var content = await analyses.GetCropAsync(analysis.Id, query.Tracklet, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.VideoAnalysisCropNotFound, query.Tracklet);
        return new VideoAnalysisCropDto(content, "image/jpeg");
    }
}
