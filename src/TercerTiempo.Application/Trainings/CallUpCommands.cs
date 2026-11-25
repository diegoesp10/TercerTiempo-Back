using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Grounds;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Grounds;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Squads.Suspensions;
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

public sealed record CallUpStampDto(
    [property: Description("La misma huella que trae la convocatoria (stamp) mientras no cambie nada de lo que se ve.")]
    string Stamp);

// Lo que pregunta cada pocos segundos la página de la convocatoria abierta, para ponerse al día cuando alguien firma.
public sealed record GetMatchCallUpStampQuery(Guid SquadId, Guid MatchId) : IQuery<CallUpStampDto>;

public sealed record SaveMatchCallUpCommand(
    Guid SquadId,
    Guid MatchId,
    IReadOnlyList<Guid> CalledSquadPlayerIds,
    DateTimeOffset? MeetAt,
    [property: Description("Campo de la liga donde se cita (GET /api/squads/{squadId}/grounds). Con él se ignora meetPlace.")]
    Guid? MeetGroundId,
    [property: Description("Otro sitio escrito a mano (obras, campo alternativo…), solo si no se elige un campo de la liga.")]
    string? MeetPlace,
    string? Notes,
    [property: Description("true: se publica (o se vuelve a publicar) y se avisa a la plantilla; false: se guarda como borrador si aún no estaba publicada.")]
    bool Publish,
    [property: Description("A cuántos se convoca. Si la plantilla tiene un número fijo, manda ese; si no, hay que decirlo (o vale el que ya tenía la convocatoria).")]
    int? Size = null) : ICommand<MatchCallUpDto>;

// El jugador firma la convocatoria: Confirmed si va convocado, Acknowledged si no. Sin notas ni «no puedo»: lo que
// cambie se habla con el cuerpo técnico, que es quien emite la convocatoria.
public sealed record RespondCallUpCommand(Guid CallUpId, CallUpResponse Response) : ICommand<MyCallUpDto>;

public sealed record ListMyCallUpsQuery : IQuery<IReadOnlyList<MyCallUpDto>>;

// El jugador abre la convocatoria: el cuerpo técnico pasa a ver el doble check.
public sealed record MarkCallUpSeenCommand(Guid CallUpId) : ICommand<MyCallUpDto>;

// El cuerpo técnico vuelve a enviar la convocatoria a quien todavía no la ha abierto.
public sealed record ResendCallUpCommand(Guid SquadId, Guid MatchId) : ICommand<MatchCallUpDto>;

internal sealed class SaveMatchCallUpValidator : AbstractValidator<SaveMatchCallUpCommand>
{
    public SaveMatchCallUpValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.CalledSquadPlayerIds).NotNull();
        RuleFor(x => x.MeetPlace).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Size!.Value).InclusiveBetween(Squad.MinCallUpSize, Squad.MaxCallUpSize).When(x => x.Size.HasValue)
            .WithCatalogMessage(ErrorCode.CallUpSizeInvalid, Squad.MinCallUpSize, Squad.MaxCallUpSize);
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
    // Las lesiones del propio jugador, con su detalle: el resto de la plantilla no le toca.
    public static async Task<InjuryView> OwnInjuriesAsync(Squad squad, SquadPlayer entry, IInjuryStore injuries, DateOnly today, CancellationToken cancellationToken)
    {
        var own = (await injuries.GetBySquadAsync(squad.Id, cancellationToken)).Where(i => i.SquadPlayerId == entry.Id).ToList();
        return own.Count == 0 ? InjuryView.None : new InjuryView(new InjuryCalendar(own), id => id == entry.Id, today);
    }

    public static async Task<Match> MatchOfSquadAsync(Squad squad, Guid matchId, IMatchStore matches, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(matchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, matchId);
        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.CallUpMatchNotOfSquad);
        return match;
    }

    // El partido cuya convocatoria toca preparar: el primero del equipo que aún no ha empezado, porque al empezar se cierra
    // (los aplazados sin fecha nueva no cuentan). El frontal aplica la misma regla a la lista de partidos.
    public static Match? NextToCallUp(IEnumerable<Match> season, DateTimeOffset now) => season
        .Where(m => m.Status == MatchStatus.Scheduled && m.KickoffAt > now)
        .MinBy(m => m.KickoffAt);

    public static async Task<MatchDto> MatchDtoAsync(Match match, ITeamStore teams, CancellationToken cancellationToken)
    {
        var map = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);
        return MatchMapper.ToDto(match, map);
    }

    public static string Fixture(MatchDto match) => $"{match.HomeTeam.Name} - {match.AwayTeam.Name}";

    // El sitio de la cita tal como se lee: el campo elegido (con su localidad) o lo escrito a mano.
    public static string? PlaceOf(MatchCallUp callUp, Ground? ground) =>
        ground is not null ? (ground.Locality is { } locality ? $"{ground.Name} ({locality})" : ground.Name) : callUp.MeetPlace;

    // Campo de la cita: si ya no está en la temporada (no debería), se busca por su Id.
    public static async Task<Ground?> MeetGroundAsync(MatchCallUp? callUp, SeasonGrounds season, IGroundStore grounds, CancellationToken cancellationToken) =>
        callUp?.MeetGroundId is not { } id ? null : season.Find(id) ?? await grounds.GetByIdAsync(id, cancellationToken);

    public static async Task<MatchCallUpDto> DetailAsync(
        MatchCallUp? callUp, Squad squad, Match match, MatchDto dto, bool canEdit, Guid? viewer, DateTimeOffset now,
        IGroundStore grounds, ITeamStore teams, InjuryView injuries, SuspensionCalendar suspensions, CancellationToken cancellationToken, int? suggestedSize = null,
        bool isNext = false)
    {
        var season = await SeasonGrounds.LoadAsync(grounds, squad.SeasonId, cancellationToken);
        var meet = await MeetGroundAsync(callUp, season, grounds, cancellationToken);
        var matchGround = season.OfMatch(match);
        var teamIds = new[] { meet, matchGround }.OfType<Ground>().SelectMany(g => season.HomeTeamsOf(g.Id)).Distinct().ToList();
        var names = teamIds.Count == 0 ? new Dictionary<Guid, string>() : (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
        return TrainingMapper.ToDto(callUp, squad, dto, canEdit, viewer, now,
            meet is null ? null : season.ToDto(meet, names), matchGround is null ? null : season.ToDto(matchGround, names), injuries, suspensions, suggestedSize, isNext);
    }

    public static string When(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, TrainingClock.Zone).ToString("dd/MM HH:mm");

    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, TrainingClock.Zone).DateTime);

    // Los borradores solo los ve quien los prepara.
    public static Task<IReadOnlyList<MatchCallUp>> VisibleAsync(Squad squad, bool manager, ICallUpStore callUps, CancellationToken cancellationToken) =>
        manager ? callUps.GetBySquadAsync(squad.Id, cancellationToken) : callUps.GetPublishedBySquadAsync(squad.Id, cancellationToken);

    // kickoffs: la hora de cada partido, para contar a quien causó baja después de jugarlo (sin ella, solo los de ahora).
    public static IReadOnlyList<CallUpSummaryDto> Summaries(IEnumerable<MatchCallUp> list, Squad squad, Guid? viewer, IReadOnlyDictionary<Guid, DateTimeOffset>? kickoffs = null)
    {
        var players = squad.Players.ToDictionary(p => p.Id);
        return list
            .Select(c =>
            {
                var kickoff = kickoffs is not null && kickoffs.TryGetValue(c.MatchId, out var at) ? at : (DateTimeOffset?)null;
                var entries = TrainingMapper.Members(c.Entries, players, kickoff).Select(e => TrainingMapper.ToDto(e, players[e.SquadPlayerId])).ToList();
                var mine = viewer is { } me && c.IsPublished ? entries.FirstOrDefault(e => e.SquadPlayerId == me) : null;
                return new CallUpSummaryDto(c.Id, c.MatchId, c.Status, c.PublishedAt, TrainingMapper.Counts(entries), mine);
            })
            .ToList();
    }

    public static string PlayerKeyPrefix(Guid callUpId, Guid entryId) => $"callup:{callUpId}:{entryId}";

    public static string ShortKeyPrefix(Guid callUpId) => $"callup-short:{callUpId}";

    // Enviada sin llegar al número: el cuerpo técnico y el club se enteran de cuántos faltan por convocar. Al completarla,
    // o si cambia cuántos faltan, el aviso anterior se da por resuelto (y, si sigue faltando gente, llega el nuevo).
    public static async Task SyncShortfallAsync(
        MatchCallUp callUp, Squad squad, MatchDto match, IClubStore clubs, IAlertStore alerts, TimeProvider clock, CancellationToken cancellationToken)
    {
        var missing = callUp.IsPublished ? callUp.Missing(squad) : 0;
        var prefix = ShortKeyPrefix(callUp.Id);
        var key = $"{prefix}:{missing}";
        var open = await StaffAlerts.OpenAsync(squad, clubs, alerts, prefix, clock, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var alert in open.Where(a => missing == 0 || a.Key != key))
            alert.Resolve(now);
        if (missing == 0 || callUp.Size is not { } size || open.Any(a => a.Key == key)) return;

        var more = missing == 1 ? "a 1 jugador más" : $"a {missing} jugadores más";
        await StaffAlerts.RaiseAsync(squad, clubs, alerts, AlertKind.CallUpShort, key,
            $"Faltan {missing} por convocar: {Fixture(match)}",
            $"La convocatoria del partido del {When(match.KickoffAt)} es de {size} y se ha enviado con {size - missing}. Convoca {more} para completarla.",
            $"/plantillas/{squad.Id}/partidos/{callUp.MatchId}?tab=convocatoria", DayOf(match.KickoffAt), clock, cancellationToken);
    }

    // Avisa a cada jugador con cuenta de lo que le toca: va o no va. Un aviso anterior de la misma convocatoria se da
    // por resuelto; el nuevo lleva fecha para no chocar con la clave del anterior.
    public static async Task NotifyPlayersAsync(
        MatchCallUp callUp, Squad squad, MatchDto match, string? place, IEnumerable<CallUpEntry> entries, IAlertStore alerts, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var fixture = Fixture(match);
        var meet = callUp.MeetAt is { } at ? $" Cita: {When(at)}{(place is null ? "" : $" en {place}")}." : "";
        var list = entries.ToList();
        var players = list
            .Select(e => squad.Players.FirstOrDefault(p => p.Id == e.SquadPlayerId)?.PlayerId)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        // Los avisos anteriores de esta convocatoria de todos los jugadores, de una vez; luego cada uno resuelve los suyos.
        var previous = await alerts.GetOpenByKeyPrefixAsync($"callup:{callUp.Id}:", players, [], null, cancellationToken);
        foreach (var entry in list)
        {
            var player = squad.Players.FirstOrDefault(p => p.Id == entry.SquadPlayerId);
            entry.MarkNotified(now);
            if (player?.PlayerId is not { } playerId) continue;

            var prefix = PlayerKeyPrefix(callUp.Id, entry.Id);
            foreach (var old in previous.Where(a => a.PlayerId == playerId && a.Key.StartsWith(prefix, StringComparison.Ordinal)))
                old.Resolve(now);

            alerts.Add(entry.Called
                ? Alert.ForPlayer(playerId, AlertKind.MatchCallUp, $"{prefix}:{now.UtcTicks}",
                    $"Vas convocado: {fixture}",
                    $"Entras en la convocatoria del partido del {When(match.KickoffAt)}.{meet} Fírmala desde la app.",
                    "/mis-convocatorias", DayOf(match.KickoffAt), now)
                : Alert.ForPlayer(playerId, AlertKind.MatchNotCalledUp, $"{prefix}:{now.UtcTicks}",
                    $"No vas convocado: {fixture}",
                    $"El cuerpo técnico ha publicado la convocatoria del partido del {When(match.KickoffAt)} y esta vez no entras. Date por enterado desde la app.",
                    "/mis-convocatorias", DayOf(match.KickoffAt), now));
        }
    }
}

internal sealed class ListSquadCallUpsHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ICallUpStore callUps, IMatchStore matches, TimeProvider clock)
    : IQueryHandler<ListSquadCallUpsQuery, IReadOnlyList<CallUpSummaryDto>>
{
    public async Task<IReadOnlyList<CallUpSummaryDto>> Handle(ListSquadCallUpsQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var manager = await TrainingAccess.CanEditAsync(squad, currentUser, clock, clubs, cancellationToken);
        var viewer = TrainingAccess.ViewerEntry(squad, currentUser);
        var list = await CallUps.VisibleAsync(squad, manager, callUps, cancellationToken);
        var kickoffs = (await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken)).ToDictionary(m => m.Id, m => m.KickoffAt);

        return CallUps.Summaries(list, squad, viewer, kickoffs);
    }
}

internal sealed class GetMatchCallUpHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IMatchStore matches, ITeamStore teams, ICallUpStore callUps, IGroundStore grounds, IInjuryStore injuries,
    SuspensionReader suspensions, TimeProvider clock)
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
        // Sin número fijo, al preguntar a cuántos se convoca se propone el de la última convocatoria.
        var suggested = manager && squad.CallUpSize is null && callUp?.Size is null ? await callUps.GetLatestSizeAsync(squad.Id, cancellationToken) : null;
        var now = clock.GetUtcNow();
        var season = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);
        return await CallUps.DetailAsync(callUp, squad, match, dto, manager, TrainingAccess.ViewerEntry(squad, currentUser), now, grounds, teams,
            await InjuryView.LoadAsync(squad, currentUser, clock, clubs, injuries, cancellationToken), await suspensions.LoadAsync(squad, season, cancellationToken), cancellationToken,
            suggested, CallUps.NextToCallUp(season, now)?.Id == match.Id);
    }
}

// Solo la huella: ni lesiones, ni sanciones, ni campos. Quien no ve la plantilla tampoco ve esto.
internal sealed class GetMatchCallUpStampHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IMatchStore matches, ICallUpStore callUps, TimeProvider clock)
    : IQueryHandler<GetMatchCallUpStampQuery, CallUpStampDto>
{
    public async Task<CallUpStampDto> Handle(GetMatchCallUpStampQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var match = await CallUps.MatchOfSquadAsync(squad, query.MatchId, matches, cancellationToken);
        var callUp = await callUps.GetByMatchAsync(squad.Id, match.Id, cancellationToken);
        return new CallUpStampDto(CallUpStamp.Of(callUp, match.KickoffAt <= clock.GetUtcNow()));
    }
}

internal sealed class SaveMatchCallUpHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    IGroundStore grounds, IAlertStore alerts, IUnitOfWork unitOfWork, IInjuryStore injuries, SuspensionReader suspensions, TimeProvider clock)
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

        // Un lesionado el día del partido no puede ir convocado.
        var calendar = new InjuryCalendar(await injuries.GetBySquadAsync(squad.Id, cancellationToken));
        var matchDay = CallUps.DayOf(match.KickoffAt);
        foreach (var id in command.CalledSquadPlayerIds.Distinct())
            if (calendar.On(id, matchDay, match.Id) is not null)
                throw new DomainException(ErrorCode.CallUpPlayerInjured, squad.Players.First(p => p.Id == id).FullName);

        // Ni un sancionado: la 5.ª amarilla o una expulsión en un partido anterior le dejan fuera de este.
        var teamSeason = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);
        var sanctions = await suspensions.LoadAsync(squad, teamSeason, cancellationToken);
        foreach (var id in command.CalledSquadPlayerIds.Distinct())
            if (sanctions.On(id, match.Id) is { } suspension)
                throw new DomainException(ErrorCode.CallUpPlayerSuspended, squad.Players.First(p => p.Id == id).FullName, SuspensionReader.ReasonInSentence(suspension));

        // Solo los campos de la liga de la plantilla (los de sus partidos de la temporada); otro sitio va escrito a mano.
        var season = await SeasonGrounds.LoadAsync(grounds, squad.SeasonId, cancellationToken);
        Ground? meetGround = null;
        if (command.MeetGroundId is { } groundId && groundId != Guid.Empty)
            meetGround = season.Find(groundId) ?? throw new NotFoundException(ErrorCode.GroundNotFound, groundId);

        var changed = callUp.Arrange(squad, command.Size, command.CalledSquadPlayerIds.ToHashSet(), command.MeetAt, meetGround?.Id, command.MeetPlace, command.Notes, now);
        var dto = await CallUps.MatchDtoAsync(match, teams, cancellationToken);

        if (command.Publish || callUp.IsPublished)
        {
            var firstTime = !callUp.IsPublished;
            callUp.Publish(now);
            // La primera vez se avisa a toda la plantilla; después, solo a quien le cambia la convocatoria.
            var toNotify = firstTime ? callUp.Entries.Where(e => active.Contains(e.SquadPlayerId)) : changed.Where(e => active.Contains(e.SquadPlayerId));
            await CallUps.NotifyPlayersAsync(callUp, squad, dto, CallUps.PlaceOf(callUp, meetGround), toNotify, alerts, clock, cancellationToken);
            await CallUps.SyncShortfallAsync(callUp, squad, dto, clubs, alerts, clock, cancellationToken);
            // Enviada: el recordatorio de que estaba por enviar deja de tener sentido ya, sin esperar al trabajo.
            if (firstTime)
                await StaffAlerts.ResolveAsync(squad, clubs, alerts, CallUpReminders.KeyPrefix(squad.Id, match.Id), clock, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await CallUps.DetailAsync(callUp, squad, match, dto, true, TrainingAccess.ViewerEntry(squad, currentUser), now, grounds, teams,
            await InjuryView.LoadAsync(squad, currentUser, clock, clubs, injuries, cancellationToken), sanctions, cancellationToken,
            isNext: CallUps.NextToCallUp(teamSeason, now)?.Id == match.Id);
    }
}

internal sealed class RespondCallUpHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    IGroundStore grounds, IAlertStore alerts, IUnitOfWork unitOfWork, IInjuryStore injuries, SuspensionReader suspensions, TimeProvider clock)
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
        var ground = callUp.MeetGroundId is { } groundId ? await grounds.GetByIdAsync(groundId, cancellationToken) : null;
        var own = await CallUps.OwnInjuriesAsync(squad, entry, injuries, SquadAccess.Today(clock), cancellationToken);
        var sanctions = await suspensions.LoadAsync(squad, cancellationToken);
        return new MyCallUpDto(callUp.Id, squad.Id, team?.Name ?? "Tu equipo", dto, callUp.MeetAt, ground is null ? null : GroundDto.From(ground), callUp.MeetPlace,
            callUp.Notes, callUp.PublishedAt ?? now, match.KickoffAt <= now,
            TrainingMapper.WithSuspension(TrainingMapper.WithInjury(TrainingMapper.ToDto(line, entry), match.KickoffAt, match.Id, own), match.Id, sanctions));
    }
}

internal sealed class MarkCallUpSeenHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    IGroundStore grounds, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<MarkCallUpSeenCommand, MyCallUpDto>
{
    public async Task<MyCallUpDto> Handle(MarkCallUpSeenCommand command, CancellationToken cancellationToken)
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
        var line = callUp.MarkSeen(entry.Id, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = await CallUps.MatchDtoAsync(match, teams, cancellationToken);
        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
        var ground = callUp.MeetGroundId is { } groundId ? await grounds.GetByIdAsync(groundId, cancellationToken) : null;
        return new MyCallUpDto(callUp.Id, squad.Id, team?.Name ?? "Tu equipo", dto, callUp.MeetAt, ground is null ? null : GroundDto.From(ground), callUp.MeetPlace,
            callUp.Notes, callUp.PublishedAt ?? now, match.KickoffAt <= now, TrainingMapper.ToDto(line, entry));
    }
}

internal sealed class ResendCallUpHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    IGroundStore grounds, IAlertStore alerts, IUnitOfWork unitOfWork, IInjuryStore injuries, SuspensionReader suspensions, TimeProvider clock)
    : ICommandHandler<ResendCallUpCommand, MatchCallUpDto>
{
    public async Task<MatchCallUpDto> Handle(ResendCallUpCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var match = await CallUps.MatchOfSquadAsync(squad, command.MatchId, matches, cancellationToken);
        var now = clock.GetUtcNow();
        var callUp = await callUps.GetByMatchAsync(squad.Id, match.Id, cancellationToken);
        if (callUp is not { IsPublished: true })
            throw new DomainException(ErrorCode.CallUpNotPublished);
        if (match.KickoffAt <= now)
            throw new DomainException(ErrorCode.CallUpMatchStarted);

        // Solo a quien tiene cuenta: al que no la tiene no hay a quién enviársela.
        var linked = squad.ActivePlayers.Where(p => p.IsLinked).Select(p => p.Id).ToHashSet();
        var dto = await CallUps.MatchDtoAsync(match, teams, cancellationToken);
        var ground = callUp.MeetGroundId is { } groundId ? await grounds.GetByIdAsync(groundId, cancellationToken) : null;
        await CallUps.NotifyPlayersAsync(callUp, squad, dto, CallUps.PlaceOf(callUp, ground),
            callUp.UnseenEntries().Where(e => linked.Contains(e.SquadPlayerId)).ToList(), alerts, clock, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var season = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);
        return await CallUps.DetailAsync(callUp, squad, match, dto, true, TrainingAccess.ViewerEntry(squad, currentUser), now, grounds, teams,
            await InjuryView.LoadAsync(squad, currentUser, clock, clubs, injuries, cancellationToken), await suspensions.LoadAsync(squad, season, cancellationToken), cancellationToken,
            isNext: CallUps.NextToCallUp(season, now)?.Id == match.Id);
    }
}

internal sealed class ListMyCallUpsHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, IMatchStore matches, ITeamStore teams, ICallUpStore callUps, IGroundStore grounds,
    IInjuryStore injuries, SuspensionReader suspensions, TimeProvider clock)
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
        var groundIds = published.Select(c => c.MeetGroundId).OfType<Guid>().Distinct().ToList();
        var groundMap = groundIds.Count == 0 ? new Dictionary<Guid, Ground>() : (await grounds.GetByIdsAsync(groundIds, cancellationToken)).ToDictionary(g => g.Id);
        var own = await CallUps.OwnInjuriesAsync(squad, entry, injuries, SquadAccess.Today(clock), cancellationToken);
        var sanctions = await suspensions.LoadAsync(squad, cancellationToken);

        return published
            .Where(c => matchMap.ContainsKey(c.MatchId) && matchMap[c.MatchId].KickoffAt >= now - KeepAfterKickoff)
            .Select(c => (CallUp: c, Match: matchMap[c.MatchId], Line: c.EntryOf(entry.Id)))
            .Where(x => x.Line is not null)
            .OrderBy(x => x.Match.KickoffAt)
            .Select(x => new MyCallUpDto(x.CallUp.Id, squad.Id, teamName, MatchMapper.ToDto(x.Match, teamMap), x.CallUp.MeetAt,
                x.CallUp.MeetGroundId is { } g && groundMap.TryGetValue(g, out var ground) ? GroundDto.From(ground) : null, x.CallUp.MeetPlace, x.CallUp.Notes,
                x.CallUp.PublishedAt ?? x.CallUp.UpdatedAt, x.Match.KickoffAt <= now,
                TrainingMapper.WithSuspension(TrainingMapper.WithInjury(TrainingMapper.ToDto(x.Line!, entry), x.Match.KickoffAt, x.Match.Id, own), x.Match.Id, sanctions)))
            .ToList();
    }
}
