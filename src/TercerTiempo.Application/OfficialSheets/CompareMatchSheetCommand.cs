using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.OfficialSheets;

public sealed record CompareMatchSheetCommand(Guid SquadId, Guid MatchId, bool Refresh = false) : ICommand<SheetComparisonDto>;

internal sealed class CompareMatchSheetValidator : AbstractValidator<CompareMatchSheetCommand>
{
    public CompareMatchSheetValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
    }
}

internal sealed class CompareMatchSheetHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances,
    IOfficialSheetStore sheets,
    IQueryHandler<GetOfficialSheetQuery, OfficialSheetDto> getOfficialSheet,
    IClubStore clubs,
    TimeProvider clock) : ICommandHandler<CompareMatchSheetCommand, SheetComparisonDto>
{
    public async Task<SheetComparisonDto> Handle(CompareMatchSheetCommand command, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(command.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, command.SquadId);

        await SquadAccess.EnsureCanViewAsync(squad, currentUser, clock, clubs, cancellationToken);

        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);

        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.MatchNotOfSquad);

        var official = await getOfficialSheet.Handle(new GetOfficialSheetQuery(match.Id), cancellationToken);

        var summary = await SquadMapper.ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);
        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);
        var matchDto = MatchMapper.ToDto(match, teamMap);
        var side = match.IsHome(squad.TeamId) ? MatchSide.Home : MatchSide.Away;
        var canEdit = match.Status == MatchStatus.Played && await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken);

        var recorded = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
            .ToDictionary(p => p.SquadPlayerId);

        var linkedIds = squad.Players.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var linkedMap = (await players.GetByIdsAsync(linkedIds, cancellationToken)).ToDictionary(p => p.Id);

        var sheet = official.Status == OfficialSheetAvailability.Available
            ? await sheets.GetByMatchAsync(match.Id, cancellationToken)
            : null;

        var officialLines = sheet?.LinesOf(side).ToList() ?? [];
        var candidates = squad.Players.Where(p => p.IsActive || recorded.ContainsKey(p.Id)).ToList();
        var matched = SheetMatcher.Match(candidates, officialLines);

        var rows = SquadMapper.Ordered(candidates)
            .Select(p =>
            {
                recorded.TryGetValue(p.Id, out var coach);
                matched.TryGetValue(p.Id, out var line);

                // Sin acta oficial no hay nada que comparar; sin línea en ninguna de las dos, tampoco.
                var differences = sheet is null || (coach is null && line is null)
                    ? []
                    : SheetMatcher.Differences(coach, line);

                return new SheetComparisonRowDto(
                    SquadMapper.ToPlayerDto(p, linkedMap),
                    coach is null ? null : PerformanceMapper.ToDto(coach, p),
                    line is null ? null : OfficialSheetMapper.ToDto(line),
                    differences);
            })
            .ToList();

        var matchedLineIds = matched.Values.Select(l => l.Id).ToHashSet();
        var unmatched = officialLines
            .Where(l => !matchedLineIds.Contains(l.Id))
            .OrderBy(l => l.ShirtNumber ?? int.MaxValue)
            .Select(OfficialSheetMapper.ToDto)
            .ToList();

        var summaryDto = new SheetComparisonSummaryDto(
            recorded.Count,
            officialLines.Count,
            matched.Count,
            rows.Count(r => r.Differences.Count > 0),
            rows.Sum(r => r.Differences.Count) + unmatched.Count(l => l.Played));

        return new SheetComparisonDto(summary, matchDto, side, canEdit, official, rows, unmatched, summaryDto);
    }
}
