using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

public sealed record CallUpSummaryDto(
    Guid CallUpId,
    Guid MatchId,
    CallUpStatus Status,
    DateTimeOffset? PublishedAt,
    CallUpCountsDto Counts,
    [property: Description("La línea de quien mira, si es un jugador de la plantilla y la convocatoria está publicada.")]
    CallUpEntryDto? Mine);

public sealed record ListSquadCallUpsQuery(Guid SquadId) : IQuery<IReadOnlyList<CallUpSummaryDto>>;

public sealed record GetMatchCallUpQuery(Guid SquadId, Guid MatchId) : IQuery<MatchCallUpDto>;

public sealed record SaveMatchCallUpCommand(
    Guid SquadId,
    Guid MatchId,
    IReadOnlyList<Guid> CalledSquadPlayerIds,
    DateTimeOffset? MeetAt,
    string? MeetPlace,
    string? Notes,
    [property: Description("true: se publica (o se vuelve a publicar) y se avisa a la plantilla; false: se guarda como borrador si aún no estaba publicada.")]
    bool Publish) : ICommand<MatchCallUpDto>;

// El jugador firma la convocatoria: Confirmed si va convocado, Acknowledged si no. Sin notas ni «no puedo»: lo que
// cambie se habla con el cuerpo técnico, que es quien emite la convocatoria.
public sealed record RespondCallUpCommand(Guid CallUpId, CallUpResponse Response) : ICommand<MyCallUpDto>;

public sealed record ListMyCallUpsQuery : IQuery<IReadOnlyList<MyCallUpDto>>;

internal sealed class SaveMatchCallUpValidator : AbstractValidator<SaveMatchCallUpCommand>
{
    public SaveMatchCallUpValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.CalledSquadPlayerIds).NotNull();
        RuleFor(x => x.MeetPlace).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

internal sealed class RespondCallUpValidator : AbstractValidator<RespondCallUpCommand>
{
    public RespondCallUpValidator()
    {
        RuleFor(x => x.CallUpId).NotEmpty();
        RuleFor(x => x.Response).IsInEnum();
    }
}

internal static class CallUps
{
    public static async Task<Match> MatchOfSquadAsync(Squad squad, Guid matchId, IMatchStore matches, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(matchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, matchId);
        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.CallUpMatchNotOfSquad);
        return match;
    }

    public static async Task<MatchDto> MatchDtoAsync(Match match, ITeamStore teams, CancellationToken cancellationToken)
    {
        var map = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);
        return MatchMapper.ToDto(match, map);
    }

    public static string Fixture(MatchDto match) => $"{match.HomeTeam.Name} - {match.AwayTeam.Name}";

    public static string When(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, TrainingClock.Zone).ToString("dd/MM HH:mm");

    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, TrainingClock.Zone).DateTime);

    public static string PlayerKeyPrefix(Guid callUpId, Guid entryId) => $"callup:{callUpId}:{entryId}";

    // Avisa a cada jugador con cuenta de lo que le toca: va o no va. Un aviso anterior de la misma convocatoria se da
    // por resuelto; el nuevo lleva fecha para no chocar con la clave del anterior.
    public static async Task NotifyPlayersAsync(
        MatchCallUp callUp, Squad squad, MatchDto match, IEnumerable<CallUpEntry> entries, IAlertStore alerts, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var fixture = Fixture(match);
        var meet = callUp.MeetAt is { } at ? $" Cita: {When(at)}{(callUp.MeetPlace is null ? "" : $" en {callUp.MeetPlace}")}." : "";
        foreach (var entry in entries)
        {
            var player = squad.Players.FirstOrDefault(p => p.Id == entry.SquadPlayerId);
            entry.MarkNotified(now);
            if (player?.PlayerId is not { } playerId) continue;

            var prefix = PlayerKeyPrefix(callUp.Id, entry.Id);
            foreach (var old in await alerts.GetForAsync(null, playerId, null, cancellationToken))
                if (old.Key.StartsWith(prefix, StringComparison.Ordinal)) old.Resolve(now);

            alerts.Add(entry.Called
                ? Alert.ForPlayer(playerId, AlertKind.CallUp, $"{prefix}:{now.UtcTicks}",
                    $"Vas convocado: {fixture}",
                    $"Entras en la convocatoria del partido del {When(match.KickoffAt)}.{meet} Fírmala desde la app.",
                    "/mis-convocatorias", DayOf(match.KickoffAt), now)
                : Alert.ForPlayer(playerId, AlertKind.NotCalledUp, $"{prefix}:{now.UtcTicks}",
                    $"No vas convocado: {fixture}",
                    $"El cuerpo técnico ha publicado la convocatoria del partido del {When(match.KickoffAt)} y esta vez no entras. Date por enterado desde la app.",
                    "/mis-convocatorias", DayOf(match.KickoffAt), now));
        }
    }
}

internal sealed class ListSquadCallUpsHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ICallUpStore callUps, TimeProvider clock)
    : IQueryHandler<ListSquadCallUpsQuery, IReadOnlyList<CallUpSummaryDto>>
{
    public async Task<IReadOnlyList<CallUpSummaryDto>> Handle(ListSquadCallUpsQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var manager = await TrainingAccess.CanEditAsync(squad, currentUser, clock, clubs, cancellationToken);
        var viewer = TrainingAccess.ViewerEntry(squad, currentUser);
        var players = squad.Players.ToDictionary(p => p.Id);
        var active = squad.ActivePlayers.Select(p => p.Id).ToHashSet();

        // Los borradores solo los ve quien los prepara.
        var list = manager
            ? await callUps.GetBySquadAsync(squad.Id, cancellationToken)
            : await callUps.GetPublishedBySquadAsync(squad.Id, cancellationToken);

        return list
            .Select(c =>
            {
                var entries = c.Entries.Where(e => active.Contains(e.SquadPlayerId)).Select(e => TrainingMapper.ToDto(e, players.GetValueOrDefault(e.SquadPlayerId))).ToList();
                var mine = viewer is { } me && c.IsPublished ? entries.FirstOrDefault(e => e.SquadPlayerId == me) : null;
                return new CallUpSummaryDto(c.Id, c.MatchId, c.Status, c.PublishedAt, TrainingMapper.Counts(entries), mine);
            })
            .ToList();
    }
}

internal sealed class GetMatchCallUpHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IMatchStore matches, ITeamStore teams, ICallUpStore callUps, TimeProvider clock)
    : IQueryHandler<GetMatchCallUpQuery, MatchCallUpDto>
{
    public async Task<MatchCallUpDto> Handle(GetMatchCallUpQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var match = await CallUps.MatchOfSquadAsync(squad, query.MatchId, matches, cancellationToken);
        var manager = await TrainingAccess.CanEditAsync(squad, currentUser, clock, clubs, cancellationToken);
        var callUp = await callUps.GetByMatchAsync(squad.Id, match.Id, cancellationToken);
        // El borrador es cosa del cuerpo técnico: a los jugadores les llega cuando se publica.
        if (callUp is { IsPublished: false } && !manager && !currentUser.IsAdmin) callUp = null;

        var dto = await CallUps.MatchDtoAsync(match, teams, cancellationToken);
        return TrainingMapper.ToDto(callUp, squad, dto, manager, TrainingAccess.ViewerEntry(squad, currentUser), clock.GetUtcNow());
    }
}

internal sealed class SaveMatchCallUpHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    IAlertStore alerts, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<SaveMatchCallUpCommand, MatchCallUpDto>
{
    public async Task<MatchCallUpDto> Handle(SaveMatchCallUpCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var match = await CallUps.MatchOfSquadAsync(squad, command.MatchId, matches, cancellationToken);
        var now = clock.GetUtcNow();

        var callUp = await callUps.GetByMatchAsync(squad.Id, match.Id, cancellationToken);
        if (callUp is null)
        {
            var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
            callUp = MatchCallUp.Draft(squad, match, editor, now);
            callUps.Add(callUp);
        }
        else if (match.KickoffAt <= now)
        {
            throw new DomainException(ErrorCode.CallUpMatchStarted);
        }

        var active = squad.ActivePlayers.Select(p => p.Id).ToHashSet();
        foreach (var id in command.CalledSquadPlayerIds)
            if (!active.Contains(id)) throw new NotFoundException(ErrorCode.SquadPlayerNotFound, id);

        var changed = callUp.Arrange(squad, command.CalledSquadPlayerIds.ToHashSet(), command.MeetAt, command.MeetPlace, command.Notes, now);
        var dto = await CallUps.MatchDtoAsync(match, teams, cancellationToken);

        if (command.Publish || callUp.IsPublished)
        {
            var firstTime = !callUp.IsPublished;
            callUp.Publish(now);
            // La primera vez se avisa a toda la plantilla; después, solo a quien le cambia la convocatoria.
            var toNotify = firstTime ? callUp.Entries.Where(e => active.Contains(e.SquadPlayerId)) : changed.Where(e => active.Contains(e.SquadPlayerId));
            await CallUps.NotifyPlayersAsync(callUp, squad, dto, toNotify, alerts, clock, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TrainingMapper.ToDto(callUp, squad, dto, true, TrainingAccess.ViewerEntry(squad, currentUser), now);
    }
}

internal sealed class RespondCallUpHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    IAlertStore alerts, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RespondCallUpCommand, MyCallUpDto>
{
    public async Task<MyCallUpDto> Handle(RespondCallUpCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var callUp = await callUps.GetByIdAsync(command.CallUpId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.CallUpNotFound, command.CallUpId);
        var squad = await squads.GetByIdAsync(callUp.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, callUp.SquadId);
        var entry = squad.PlayerEntryOf(player.Id)
            ?? throw new ForbiddenException(ErrorCode.SquadPrivate);
        var match = await matches.GetByIdAsync(callUp.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, callUp.MatchId);

        var now = clock.GetUtcNow();
        var line = callUp.Respond(entry.Id, command.Response, match, now);
        var dto = await CallUps.MatchDtoAsync(match, teams, cancellationToken);

        // Firmada: el aviso de la convocatoria que tenía el jugador se da por resuelto. Al cuerpo técnico no le llega
        // nada: la firma se ve en la propia convocatoria.
        foreach (var alert in await alerts.GetForAsync(null, player.Id, null, cancellationToken))
            if (alert.Key.StartsWith(CallUps.PlayerKeyPrefix(callUp.Id, line.Id), StringComparison.Ordinal)) alert.Resolve(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
        return new MyCallUpDto(callUp.Id, squad.Id, team?.Name ?? "Tu equipo", dto, callUp.MeetAt, callUp.MeetPlace, callUp.Notes, callUp.PublishedAt ?? now,
            match.KickoffAt <= now, TrainingMapper.ToDto(line, entry));
    }
}

internal sealed class ListMyCallUpsHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, IMatchStore matches, ITeamStore teams, ICallUpStore callUps, TimeProvider clock)
    : IQueryHandler<ListMyCallUpsQuery, IReadOnlyList<MyCallUpDto>>
{
    // Las convocatorias publicadas de los partidos que quedan y las de la última semana.
    private static readonly TimeSpan KeepAfterKickoff = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<MyCallUpDto>> Handle(ListMyCallUpsQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad?.PlayerEntryOf(player.Id) is not { } entry) return [];

        var now = clock.GetUtcNow();
        var published = await callUps.GetPublishedBySquadAsync(squad.Id, cancellationToken);
        if (published.Count == 0) return [];

        var matchMap = (await matches.GetByIdsAsync(published.Select(c => c.MatchId), cancellationToken)).ToDictionary(m => m.Id);
        var teamIds = matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);
        var teamName = teamMap.GetValueOrDefault(squad.TeamId)?.Name ?? "Tu equipo";

        return published
            .Where(c => matchMap.ContainsKey(c.MatchId) && matchMap[c.MatchId].KickoffAt >= now - KeepAfterKickoff)
            .Select(c => (CallUp: c, Match: matchMap[c.MatchId], Line: c.EntryOf(entry.Id)))
            .Where(x => x.Line is not null)
            .OrderBy(x => x.Match.KickoffAt)
            .Select(x => new MyCallUpDto(x.CallUp.Id, squad.Id, teamName, MatchMapper.ToDto(x.Match, teamMap), x.CallUp.MeetAt, x.CallUp.MeetPlace, x.CallUp.Notes,
                x.CallUp.PublishedAt ?? x.CallUp.UpdatedAt, x.Match.KickoffAt <= now, TrainingMapper.ToDto(x.Line!, entry)))
            .ToList();
    }
}
