using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.TeamStats;

public sealed record TeamMatchStatsDto(
    [property: Description("Posesión del equipo (0-100); null si no se apuntó.")]
    int? PossessionPct,
    int CornersFor,
    int CornersAgainst,
    int ShotsFor,
    int ShotsAgainst,
    int ShotsOnTargetFor,
    int ShotsOnTargetAgainst,
    int OffsidesFor,
    int OffsidesAgainst,
    int FoulsCommitted,
    int FoulsReceived,
    int SetPieceGoalsFor,
    int SetPieceGoalsAgainst,
    string? Notes,
    DateTimeOffset UpdatedAt,
    [property: Description("Penaltis a favor (lanzados por el equipo) y en contra.")]
    int PenaltiesFor,
    int PenaltiesAgainst,
    [property: Description("Goles de penalti: son parte de los goles a balón parado.")]
    int PenaltyGoalsFor,
    int PenaltyGoalsAgainst,
    int ThrowInsFor,
    int ThrowInsAgainst);

// Lo que envía el cuerpo técnico; «For» es su equipo y «Against» el rival.
public sealed record TeamMatchStatsInput(
    int? PossessionPct,
    int CornersFor,
    int CornersAgainst,
    int ShotsFor,
    int ShotsAgainst,
    int ShotsOnTargetFor,
    int ShotsOnTargetAgainst,
    int OffsidesFor,
    int OffsidesAgainst,
    int FoulsCommitted,
    int FoulsReceived,
    int SetPieceGoalsFor,
    int SetPieceGoalsAgainst,
    string? Notes,
    int PenaltiesFor = 0,
    int PenaltiesAgainst = 0,
    int PenaltyGoalsFor = 0,
    int PenaltyGoalsAgainst = 0,
    int ThrowInsFor = 0,
    int ThrowInsAgainst = 0)
{
    public TeamMatchStatsData ToData() => new(
        PossessionPct, CornersFor, CornersAgainst, ShotsFor, ShotsAgainst, ShotsOnTargetFor, ShotsOnTargetAgainst,
        OffsidesFor, OffsidesAgainst, FoulsCommitted, FoulsReceived, SetPieceGoalsFor, SetPieceGoalsAgainst, Notes,
        PenaltiesFor, PenaltiesAgainst, PenaltyGoalsFor, PenaltyGoalsAgainst, ThrowInsFor, ThrowInsAgainst);
}

public sealed record TeamMatchStatsRowDto(MatchDto Match, TeamMatchStatsDto Stats);

// Medias por partido con datos apuntados (la posesión, sobre los partidos en que se apuntó).
public sealed record TeamStatsAveragesDto(
    double? PossessionPct,
    double CornersFor,
    double CornersAgainst,
    double ShotsFor,
    double ShotsAgainst,
    double ShotsOnTargetFor,
    double ShotsOnTargetAgainst,
    double OffsidesFor,
    double OffsidesAgainst,
    double FoulsCommitted,
    double FoulsReceived,
    int SetPieceGoalsFor,
    int SetPieceGoalsAgainst,
    int PenaltiesFor,
    int PenaltiesAgainst,
    int PenaltyGoalsFor,
    int PenaltyGoalsAgainst,
    double ThrowInsFor,
    double ThrowInsAgainst);

public sealed record SquadTeamStatsDto(int MatchesRecorded, TeamStatsAveragesDto? Averages, IReadOnlyList<TeamMatchStatsRowDto> Matches);

internal static class TeamMatchStatsMapper
{
    public static TeamMatchStatsDto ToDto(TeamMatchStats s) => new(
        s.PossessionPct, s.CornersFor, s.CornersAgainst, s.ShotsFor, s.ShotsAgainst, s.ShotsOnTargetFor, s.ShotsOnTargetAgainst,
        s.OffsidesFor, s.OffsidesAgainst, s.FoulsCommitted, s.FoulsReceived, s.SetPieceGoalsFor, s.SetPieceGoalsAgainst,
        s.Notes, s.UpdatedAt, s.PenaltiesFor, s.PenaltiesAgainst, s.PenaltyGoalsFor, s.PenaltyGoalsAgainst, s.ThrowInsFor, s.ThrowInsAgainst);

    public static TeamStatsAveragesDto? Averages(IReadOnlyCollection<TeamMatchStats> all)
    {
        if (all.Count == 0) return null;

        double Avg(Func<TeamMatchStats, int> f) => Math.Round(all.Average(f), 2);
        var withPossession = all.Where(s => s.PossessionPct is not null).ToList();

        return new TeamStatsAveragesDto(
            withPossession.Count > 0 ? Math.Round(withPossession.Average(s => s.PossessionPct!.Value), 1) : null,
            Avg(s => s.CornersFor), Avg(s => s.CornersAgainst),
            Avg(s => s.ShotsFor), Avg(s => s.ShotsAgainst),
            Avg(s => s.ShotsOnTargetFor), Avg(s => s.ShotsOnTargetAgainst),
            Avg(s => s.OffsidesFor), Avg(s => s.OffsidesAgainst),
            Avg(s => s.FoulsCommitted), Avg(s => s.FoulsReceived),
            all.Sum(s => s.SetPieceGoalsFor), all.Sum(s => s.SetPieceGoalsAgainst),
            all.Sum(s => s.PenaltiesFor), all.Sum(s => s.PenaltiesAgainst),
            all.Sum(s => s.PenaltyGoalsFor), all.Sum(s => s.PenaltyGoalsAgainst),
            Avg(s => s.ThrowInsFor), Avg(s => s.ThrowInsAgainst));
    }
}

public sealed record RecordTeamMatchStatsCommand(Guid SquadId, Guid MatchId, TeamMatchStatsInput Stats) : ICommand<TeamMatchStatsDto>;

internal sealed class RecordTeamMatchStatsValidator : AbstractValidator<RecordTeamMatchStatsCommand>
{
    public RecordTeamMatchStatsValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Stats).NotNull();
        When(x => x.Stats is not null, () =>
        {
            RuleFor(x => x.Stats.PossessionPct).InclusiveBetween(0, 100).When(x => x.Stats.PossessionPct.HasValue);
            RuleFor(x => x.Stats.CornersFor).InclusiveBetween(0, 50);
            RuleFor(x => x.Stats.CornersAgainst).InclusiveBetween(0, 50);
            RuleFor(x => x.Stats.ShotsFor).InclusiveBetween(0, 100);
            RuleFor(x => x.Stats.ShotsAgainst).InclusiveBetween(0, 100);
            RuleFor(x => x.Stats.ShotsOnTargetFor).GreaterThanOrEqualTo(0).LessThanOrEqualTo(x => x.Stats.ShotsFor)
                .WithCatalogMessage(ErrorCode.SheetShotsOnTarget);
            RuleFor(x => x.Stats.ShotsOnTargetAgainst).GreaterThanOrEqualTo(0).LessThanOrEqualTo(x => x.Stats.ShotsAgainst)
                .WithCatalogMessage(ErrorCode.SheetShotsOnTarget);
            RuleFor(x => x.Stats.OffsidesFor).InclusiveBetween(0, 50);
            RuleFor(x => x.Stats.OffsidesAgainst).InclusiveBetween(0, 50);
            RuleFor(x => x.Stats.FoulsCommitted).InclusiveBetween(0, 100);
            RuleFor(x => x.Stats.FoulsReceived).InclusiveBetween(0, 100);
            RuleFor(x => x.Stats.SetPieceGoalsFor).InclusiveBetween(0, 30);
            RuleFor(x => x.Stats.SetPieceGoalsAgainst).InclusiveBetween(0, 30);
            RuleFor(x => x.Stats.PenaltiesFor).InclusiveBetween(0, 20);
            RuleFor(x => x.Stats.PenaltiesAgainst).InclusiveBetween(0, 20);
            RuleFor(x => x.Stats.PenaltyGoalsFor).InclusiveBetween(0, 20);
            RuleFor(x => x.Stats.PenaltyGoalsAgainst).InclusiveBetween(0, 20);
            RuleFor(x => x.Stats.ThrowInsFor).InclusiveBetween(0, 150);
            RuleFor(x => x.Stats.ThrowInsAgainst).InclusiveBetween(0, 150);
            RuleFor(x => x.Stats.Notes).MaximumLength(TeamMatchStatsData.MaxNotesLength);
        });
    }
}

internal sealed class RecordTeamMatchStatsHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    ISquadStore squads,
    IMatchStore matches,
    ITeamMatchStatsStore teamStats,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RecordTeamMatchStatsCommand, TeamMatchStatsDto>
{
    public async Task<TeamMatchStatsDto> Handle(RecordTeamMatchStatsCommand command, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, coaches, cancellationToken);
        var squad = await Resolvers.SquadForStaffAsync(squads, currentUser, command.SquadId, clock, cancellationToken);

        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);

        var now = clock.GetUtcNow();
        var current = await teamStats.GetAsync(squad.Id, match.Id, cancellationToken);
        if (current is null)
        {
            current = TeamMatchStats.Record(squad, match, coach.Id, command.Stats.ToData(), now);
            teamStats.Add(current);
        }
        else
        {
            current.Update(squad, match, coach.Id, command.Stats.ToData(), now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TeamMatchStatsMapper.ToDto(current);
    }
}

public sealed record GetSquadTeamStatsQuery(Guid SquadId) : IQuery<SquadTeamStatsDto>;

// Datos del equipo de la temporada, partido a partido y en media: los ve quien puede ver la plantilla.
internal sealed class GetSquadTeamStatsHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ITeamMatchStatsStore teamStats,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<GetSquadTeamStatsQuery, SquadTeamStatsDto>
{
    public async Task<SquadTeamStatsDto> Handle(GetSquadTeamStatsQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, query.SquadId);

        await SquadAccess.EnsureCanViewAsync(squad, currentUser, clock, clubs, cancellationToken);

        var all = await teamStats.GetBySquadAsync(squad.Id, cancellationToken);
        var byMatch = all.ToDictionary(s => s.MatchId);
        var teamMatches = (await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken))
            .Where(m => byMatch.ContainsKey(m.Id))
            .OrderBy(m => m.KickoffAt)
            .ToList();
        var teamIds = teamMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var rows = teamMatches
            .Select(m => new TeamMatchStatsRowDto(MatchMapper.ToDto(m, teamMap), TeamMatchStatsMapper.ToDto(byMatch[m.Id])))
            .ToList();
        var recorded = teamMatches.Select(m => byMatch[m.Id]).ToList();

        return new SquadTeamStatsDto(rows.Count, TeamMatchStatsMapper.Averages(recorded), rows);
    }
}
