using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Suggestions;

public sealed record SuggestedStatsDto(
    bool Started,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int KeyPasses,
    int Tackles,
    int Interceptions,
    int Saves,
    int YellowCards,
    bool RedCard);

public sealed record SuggestionDto(
    Guid Id,
    Guid SquadId,
    SquadPlayerDto Player,
    MatchDto Match,
    [property: Description("Línea que propone el jugador.")]
    SuggestedStatsDto Proposed,
    [property: Description("Línea actual del acta, si existe; null si el jugador propone una entrada nueva.")]
    PerformanceDto? Current,
    string? Message,
    SuggestionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    [property: Description("Quién la decidió: nombre del técnico o del club.")]
    string? ResolvedBy,
    string? ResolutionNote);

public sealed record SuggestPerformanceCommand(
    Guid MatchId,
    bool Started,
    [property: Description("0-120.")]
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int KeyPasses,
    int Tackles,
    int Interceptions,
    int Saves,
    [property: Description("0-2; con 2 amarillas hay que marcar la roja.")]
    int YellowCards,
    bool RedCard,
    [property: Description("Explicación para el cuerpo técnico, opcional (hasta 500 caracteres).")]
    string? Message) : ICommand<SuggestionDto>
{
    public PerformanceStats ToStats() => new(
        Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, null);
}

public sealed record ListMySuggestionsQuery : IQuery<IReadOnlyList<SuggestionDto>>;

public sealed record ListSquadSuggestionsQuery(Guid SquadId) : IQuery<IReadOnlyList<SuggestionDto>>;

public sealed record ResolveSuggestionCommand(
    Guid SquadId,
    Guid SuggestionId,
    [property: Description("true aprueba y escribe la línea en el acta; false la rechaza.")]
    bool Approve,
    [property: Description("Nota para el jugador, opcional (hasta 500 caracteres).")]
    string? Note) : ICommand<IReadOnlyList<SuggestionDto>>;

internal sealed class SuggestPerformanceValidator : AbstractValidator<SuggestPerformanceCommand>
{
    public SuggestPerformanceValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.MinutesPlayed).InclusiveBetween(0, PerformanceStats.MaxMinutes);
        RuleFor(x => x.Goals).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Assists).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Shots).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ShotsOnTarget).GreaterThanOrEqualTo(0).LessThanOrEqualTo(x => x.Shots)
            .WithCatalogMessage(ErrorCode.SheetShotsOnTarget);
        RuleFor(x => x.KeyPasses).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Tackles).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Interceptions).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Saves).GreaterThanOrEqualTo(0);
        RuleFor(x => x.YellowCards).InclusiveBetween(0, 2);
        RuleFor(x => x.Message).MaximumLength(PerformanceSuggestion.MaxMessageLength);
    }
}

internal sealed class ResolveSuggestionValidator : AbstractValidator<ResolveSuggestionCommand>
{
    public ResolveSuggestionValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SuggestionId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(PerformanceSuggestion.MaxMessageLength);
    }
}

internal sealed class SuggestPerformanceHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICoachStore coaches,
    IClubStore clubs,
    IPerformanceStore performances,
    IPerformanceSuggestionStore suggestions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SuggestPerformanceCommand, SuggestionDto>
{
    public async Task<SuggestionDto> Handle(SuggestPerformanceCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken)
            ?? throw new ForbiddenException(ErrorCode.PlayerNotInSquad);
        var entry = squad.PlayerEntryOf(player.Id)
            ?? throw new ForbiddenException(ErrorCode.PlayerNotInSquad);

        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);

        var existing = await suggestions.GetByPlayerAsync(player.Id, cancellationToken);
        if (existing.Any(s => s.IsPending && s.MatchId == match.Id && s.SquadPlayerId == entry.Id))
            throw new ConflictException(ErrorCode.SuggestionPendingExists);

        var suggestion = PerformanceSuggestion.Create(squad, entry, player.Id, match, command.ToStats(), command.Message, clock.GetUtcNow());
        suggestions.Add(suggestion);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dtos = await SuggestionMapper.ToDtosAsync([suggestion], squad, matches, teams, performances, coaches, clubs, cancellationToken);
        return dtos[0];
    }
}

internal sealed class ListMySuggestionsHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICoachStore coaches,
    IClubStore clubs,
    IPerformanceStore performances,
    IPerformanceSuggestionStore suggestions) : IQueryHandler<ListMySuggestionsQuery, IReadOnlyList<SuggestionDto>>
{
    public async Task<IReadOnlyList<SuggestionDto>> Handle(ListMySuggestionsQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad is null) return [];

        var mine = (await suggestions.GetByPlayerAsync(player.Id, cancellationToken)).Where(s => s.SquadId == squad.Id).ToList();
        return await SuggestionMapper.ToDtosAsync(mine, squad, matches, teams, performances, coaches, clubs, cancellationToken);
    }
}

internal sealed class ListSquadSuggestionsHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICoachStore coaches,
    IClubStore clubs,
    IPerformanceStore performances,
    IPerformanceSuggestionStore suggestions,
    TimeProvider clock) : IQueryHandler<ListSquadSuggestionsQuery, IReadOnlyList<SuggestionDto>>
{
    public async Task<IReadOnlyList<SuggestionDto>> Handle(ListSquadSuggestionsQuery query, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, query.SquadId, clock, cancellationToken);
        var list = await suggestions.GetBySquadAsync(squad.Id, cancellationToken);
        return await SuggestionMapper.ToDtosAsync(list, squad, matches, teams, performances, coaches, clubs, cancellationToken);
    }
}

internal sealed class ResolveSuggestionHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICoachStore coaches,
    IClubStore clubs,
    IPerformanceStore performances,
    IPerformanceSuggestionStore suggestions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ResolveSuggestionCommand, IReadOnlyList<SuggestionDto>>
{
    public async Task<IReadOnlyList<SuggestionDto>> Handle(ResolveSuggestionCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var suggestion = await suggestions.GetByIdAsync(command.SuggestionId, cancellationToken);
        if (suggestion is null || suggestion.SquadId != squad.Id)
            throw new NotFoundException(ErrorCode.SuggestionNotFound, command.SuggestionId);

        var now = clock.GetUtcNow();
        var today = SquadAccess.Today(clock);

        if (command.Approve)
        {
            // La línea se escribe en el acta en nombre de un técnico vigente: el que aprueba o, si aprueba el club,
            // el primer entrenador (o cualquier miembro del staff) de la plantilla.
            var recorder = currentUser.CoachId
                ?? squad.StaffOn(today).OrderBy(s => s.Role == StaffRole.HeadCoach ? 0 : 1).Select(s => (Guid?)s.CoachId).FirstOrDefault()
                ?? throw new ConflictException(ErrorCode.SuggestionNoStaffToRecord);

            var match = await matches.GetByIdAsync(suggestion.MatchId, cancellationToken)
                ?? throw new NotFoundException(ErrorCode.MatchNotFound, suggestion.MatchId);
            var entry = squad.GetPlayer(suggestion.SquadPlayerId);
            var current = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
                .FirstOrDefault(p => p.SquadPlayerId == entry.Id);

            if (current is null)
                performances.Add(PlayerMatchPerformance.Record(squad, entry, match, recorder, suggestion.ToStats(), now));
            else
                // La sugerencia del jugador no trae el detalle que apunta el cuerpo técnico: se conserva el de la línea.
                current.Update(suggestion.ToStats(current.Notes) with
                {
                    Fouls = current.Fouls,
                    BallLosses = current.BallLosses,
                    Recoveries = current.Recoveries,
                    PassesCompleted = current.PassesCompleted,
                    PassesAttempted = current.PassesAttempted,
                }, entry, match, recorder, now);

            suggestion.Approve(currentUser.CoachId, currentUser.ClubId, command.Note, now);
        }
        else
        {
            suggestion.Decline(currentUser.CoachId, currentUser.ClubId, command.Note, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var list = await suggestions.GetBySquadAsync(squad.Id, cancellationToken);
        return await SuggestionMapper.ToDtosAsync(list, squad, matches, teams, performances, coaches, clubs, cancellationToken);
    }
}

internal static class SuggestionMapper
{
    public static async Task<IReadOnlyList<SuggestionDto>> ToDtosAsync(
        IReadOnlyList<PerformanceSuggestion> list,
        Squad squad,
        IMatchStore matches,
        ITeamStore teams,
        IPerformanceStore performances,
        ICoachStore coaches,
        IClubStore clubs,
        CancellationToken cancellationToken)
    {
        if (list.Count == 0) return [];

        var matchMap = new Dictionary<Guid, Match>();
        foreach (var id in list.Select(s => s.MatchId).Distinct())
        {
            if (await matches.GetByIdAsync(id, cancellationToken) is { } match) matchMap[id] = match;
        }
        var teamIds = matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);
        var performanceMap = (await performances.GetBySquadAsync(squad.Id, cancellationToken))
            .ToDictionary(p => (p.MatchId, p.SquadPlayerId));
        var coachMap = (await coaches.GetByIdsAsync(list.Where(s => s.ResolvedByCoachId is not null).Select(s => s.ResolvedByCoachId!.Value).Distinct(), cancellationToken))
            .ToDictionary(c => c.Id);
        var playerMap = squad.Players.ToDictionary(p => p.Id);

        var result = new List<SuggestionDto>();
        foreach (var s in list.OrderBy(s => s.IsPending ? 0 : 1).ThenByDescending(s => s.CreatedAt))
        {
            if (!matchMap.TryGetValue(s.MatchId, out var match) || !playerMap.TryGetValue(s.SquadPlayerId, out var entry)) continue;

            string? resolvedBy = null;
            if (s.ResolvedByCoachId is { } coachId) resolvedBy = coachMap.GetValueOrDefault(coachId)?.DisplayName ?? "Cuerpo técnico";
            else if (s.ResolvedByClubId is { } clubId) resolvedBy = (await clubs.GetByIdAsync(clubId, cancellationToken))?.Name ?? "Club";

            performanceMap.TryGetValue((s.MatchId, s.SquadPlayerId), out var current);

            result.Add(new SuggestionDto(
                s.Id, s.SquadId,
                SquadMapper.ToPlayerDto(entry, null),
                MatchMapper.ToDto(match, teamMap),
                new SuggestedStatsDto(s.Started, s.MinutesPlayed, s.Goals, s.Assists, s.Shots, s.ShotsOnTarget, s.KeyPasses, s.Tackles, s.Interceptions, s.Saves, s.YellowCards, s.RedCard),
                current is null ? null : PerformanceMapper.ToDto(current, entry),
                s.Message, s.Status, s.CreatedAt, s.ResolvedAt, resolvedBy, s.ResolutionNote));
        }
        return result;
    }
}
