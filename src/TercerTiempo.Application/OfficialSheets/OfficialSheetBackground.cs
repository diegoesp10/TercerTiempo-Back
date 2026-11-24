using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Abstractions.RealData;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Referees;

namespace TercerTiempo.Application.OfficialSheets;

// Cola de actas por descargar de la federación. La atiende un proceso en segundo plano; las peticiones de los usuarios
// solo leen la base de datos y, si falta el acta, la ponen aquí con prioridad.
public interface IOfficialSheetQueue
{
    bool Enqueue(Guid matchId);
    bool IsQueued(Guid matchId);

    // Hasta cuándo está en pausa la descarga porque la federación limita las peticiones (null = funcionando).
    DateTimeOffset? PausedUntil { get; }
}

// Las actas cerradas son definitivas para la descarga automática. Solo se buscan datos que aún falten.
internal static class OfficialSheetRefresh
{
    private static readonly TimeSpan PendingRecheck = TimeSpan.FromHours(24);

    // Un acta provisional (alineaciones sin goles, cambios ni tarjetas) se vuelve a mirar cada pocas horas: el árbitro
    // suele cerrarla el mismo día o al siguiente.
    public static readonly TimeSpan ProvisionalRecheck = TimeSpan.FromHours(6);

    // Pasado este tiempo desde el inicio se da por buena tal como esté (un 0-0 sin cambios ni tarjetas existe).
    public static readonly TimeSpan ProvisionalLimit = TimeSpan.FromDays(7);

    public static bool IsPlayed(Match match, DateTimeOffset now) =>
        match.Status == MatchStatus.Played
        || (match.Status is MatchStatus.Scheduled or MatchStatus.InProgress && match.KickoffAt + TimeSpan.FromHours(2) <= now);

    // Un acta oculta (de una web desactivada) se busca de nuevo en las activas, como mucho una vez al día.
    public static bool IsDue(OfficialMatchSheet sheet, Match match, DateTimeOffset now, bool hidden = false) =>
        IsDue(OfficialSheetState.From(sheet), now, hidden);

    public static bool IsDue(OfficialSheetState sheet, DateTimeOffset now, bool hidden = false)
    {
        if (hidden) return now - sheet.CheckedAt >= PendingRecheck;
        return sheet.Status switch
        {
            OfficialSheetStatus.Available => false,
            OfficialSheetStatus.Provisional => now - sheet.CheckedAt >= ProvisionalRecheck,
            _ => now - sheet.CheckedAt >= PendingRecheck,
        };
    }

    // La federación publica las alineaciones antes de que el árbitro cierre el acta: hasta entonces la página no trae
    // goles, cambios ni tarjetas (ni sus apartados) aunque el marcador ya diga 1-0. Un acta está cerrada cuando trae algún
    // suceso y, si el partido tuvo goles según el resultado, al menos un gol. No se exige que los goles cuadren con el
    // resultado: los resultados de despacho (3-0 por alineación indebida) no coinciden con lo jugado.
    public static bool IsClosed(OfficialSheetData data, Match match, DateTimeOffset now)
    {
        if (data.Lines.Count == 0) return false;
        if (now - match.KickoffAt >= ProvisionalLimit) return true;

        var goals = data.Events.Count(e => e.Type is OfficialEventType.Goal or OfficialEventType.PenaltyGoal or OfficialEventType.OwnGoal);
        // Las tarjetas sin minuto (999') no entran en la cronología, pero también son sucesos del acta.
        var anything = data.Events.Count > 0 || data.Lines.Any(l => l.YellowCards > 0 || l.RedCard);
        var scored = match.Score is { } score && score.Home + score.Away > 0;
        return anything && (goals > 0 || !scored);
    }
}

// Lo que el proceso en segundo plano ya sabe que no está en cada web (competiciones que no cubre) o en ninguna
// (partidos que no aparecen), para no volver a preguntarlo en cada pasada.
internal sealed class OfficialSheetSyncMemory
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<(string, Guid), DateTimeOffset> _seasonsWithoutCompetition = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _matchesNotFound = new();

    public void SeasonNotFound(string provider, Guid seasonId, DateTimeOffset now) => _seasonsWithoutCompetition[(provider, seasonId)] = now;
    public void MatchNotFound(Guid matchId, DateTimeOffset now) => _matchesNotFound[matchId] = now;

    public bool SkipSeason(string provider, Guid seasonId, DateTimeOffset now) =>
        _seasonsWithoutCompetition.TryGetValue((provider, seasonId), out var at) && now - at < RetryAfter;

    public bool SkipMatch(Guid matchId, DateTimeOffset now) =>
        _matchesNotFound.TryGetValue(matchId, out var at) && now - at < RetryAfter;
}

public sealed record GetOfficialSheetQuery(Guid MatchId) : IQuery<OfficialSheetDto>;

// Lectura del acta para el usuario: solo base de datos, nunca las webs de actas. Si falta o está por refrescar,
// se encola para el proceso en segundo plano y se devuelve lo que haya.
internal sealed class GetOfficialSheetHandler(
    IOfficialSheetProvider provider,
    IMatchStore matches,
    ICompetitionStore competitions,
    IRefereeStore referees,
    IOfficialSheetStore sheets,
    IOfficialSheetQueue queue,
    OfficialSheetSyncMemory memory,
    TercerTiempo.Application.Abstractions.Identity.ICurrentUser currentUser,
    TercerTiempo.Domain.Clubs.ICoachAppointmentStore appointments,
    TimeProvider clock) : IQueryHandler<GetOfficialSheetQuery, OfficialSheetDto>
{
    public async Task<OfficialSheetDto> Handle(GetOfficialSheetQuery query, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        var sheet = await sheets.GetByMatchAsync(match.Id, cancellationToken);
        var dto = Read(match, sheet);
        dto = dto with { Referees = await RefereesAsync(match, cancellationToken) ?? dto.Referees };
        if (dto.Home?.Staff is not { Count: > 0 } && dto.Away?.Staff is not { Count: > 0 }) return dto;

        var named = await appointments.GetByTeamsAndSeasonsAsync([match.HomeTeamId, match.AwayTeamId], [match.SeasonId], cancellationToken);
        return OfficialSheetMapper.LinkStaff(dto, named);
    }

    // Quien pitó el partido, de las actas; si aún no se ha jugado, quien ha designado la federación (solo en sus ligas: en
    // las demás no se va a saber). Si no, queda el árbitro que trae el acta, por si su equipo arbitral aún no se ha leído.
    private async Task<SheetRefereesDto?> RefereesAsync(Match match, CancellationToken cancellationToken)
    {
        var officials = await referees.GetOfficialsAsync(match.Id, cancellationToken);
        if (officials.Count > 0) return new SheetRefereesDto(false, await NamedAsync(officials.Select(o => (o.Role, o.RefereeId)), cancellationToken));

        if (match.Status == MatchStatus.Scheduled && match.KickoffAt > clock.GetUtcNow())
        {
            var competition = await competitions.GetBySeasonAsync(match.SeasonId, cancellationToken);
            if (competition is null || !provider.Publishes(competition.Category)) return null;
            var designated = await referees.GetDesignationsAsync(match.Id, cancellationToken);
            return new SheetRefereesDto(true, await NamedAsync(designated.Select(d => (d.Role, d.RefereeId)), cancellationToken));
        }

        return null;
    }

    private async Task<IReadOnlyList<SheetOfficialDto>> NamedAsync(IEnumerable<(OfficialRole Role, Guid RefereeId)> officials, CancellationToken cancellationToken)
    {
        var list = officials.OrderBy(o => o.Role).ToList();
        var people = (await referees.GetByIdsAsync(list.Select(o => o.RefereeId), cancellationToken)).ToDictionary(r => r.Id);
        return list
            .Select(o => people.TryGetValue(o.RefereeId, out var person) ? new SheetOfficialDto(o.Role, person.Name, person.Id) : null)
            .OfType<SheetOfficialDto>()
            .ToList();
    }

    private OfficialSheetDto Read(Match match, OfficialMatchSheet? sheet)
    {
        var now = clock.GetUtcNow();

        if (match.Status is MatchStatus.Cancelled)
            return Result(sheet, match, OfficialSheetAvailability.Unavailable, "El partido está cancelado.");

        if (!OfficialSheetRefresh.IsPlayed(match, now))
            return Result(sheet, match, OfficialSheetAvailability.NotPlayed, "El partido todavía no se ha jugado.");

        if (sheet is null)
        {
            if (memory.SkipMatch(match.Id, now))
                return OfficialSheetMapper.Empty(match.Id, OfficialSheetAvailability.Unavailable, provider.ProviderName,
                    "No se ha encontrado este partido en ninguna web de actas.");

            // Solo una cuenta adelanta la descarga: la cola con prioridad gasta las peticiones que la federación permite y
            // una visita sin sesión recorriendo ids de partidos no puede dejar sin ellas a los equipos de la app. Para
            // el resto se encarga la planificación periódica.
            if (currentUser.IsAuthenticated) queue.Enqueue(match.Id);
            var message = queue.PausedUntil is { } until && until > now
                ? $"Las webs de actas están limitando las consultas; el acta se descargará en cuanto lo permitan (hacia las {SpainTime.Local(until):HH:mm})."
                : "El acta aún no está guardada. Se descargará en segundo plano respetando los límites de cada web.";
            return OfficialSheetMapper.Empty(match.Id, OfficialSheetAvailability.Fetching, provider.ProviderName, message);
        }

        if (currentUser.IsAuthenticated && OfficialSheetRefresh.IsDue(sheet, match, now))
            queue.Enqueue(match.Id);

        if (sheet.IsAvailable) return Result(sheet, match, OfficialSheetAvailability.Available, null);
        if (sheet.IsProvisional) return Result(sheet, match, OfficialSheetAvailability.Provisional, OfficialSheetMapper.ProvisionalMessage);
        return Result(sheet, match, OfficialSheetAvailability.Pending, "El acta guardada aún está pendiente. La descarga de datos nuevos se comprueba como máximo una vez al día.");
    }

    private static OfficialSheetDto Result(OfficialMatchSheet? sheet, Match match, OfficialSheetAvailability status, string? message) =>
        sheet is null
            ? OfficialSheetMapper.Empty(match.Id, status, null, message)
            : OfficialSheetMapper.ToDto(sheet, match.HomeTeamId, match.AwayTeamId, status, message);
}

public sealed record PlanOfficialSheetSyncCommand(int Max) : ICommand<IReadOnlyList<PlannedSheet>>;

// FederationAllowed: el partido es de un equipo con plantilla en la app y, si las otras webs no tienen el acta, se
// puede pedir a la federación.
public sealed record PlannedSheet(Guid MatchId, bool FederationAllowed);

// Planificar solo lee SQL: partidos jugados sin acta o pendientes desde hace al menos un día. Primero los de equipos
// con plantilla en la app y después todos los demás, de los más recientes a los más antiguos. Los demás solo se buscan
// en las webs que aguantan más tráfico; a la federación únicamente van los que importan o alguien abre.
// Corre contra la base de datos reales; las plantillas, que son del entorno, se leen por IEnvironmentData.
internal sealed class PlanOfficialSheetSyncHandler(
    ICompetitionStore competitions,
    IMatchStore matches,
    IEnvironmentData environment,
    IOfficialSheetStore sheets,
    IEnumerable<IPublicSheetSource> sources,
    IOptions<PublicSheetOptions> options,
    OfficialSheetSyncMemory memory,
    TimeProvider clock) : ICommandHandler<PlanOfficialSheetSyncCommand, IReadOnlyList<PlannedSheet>>
{
    public async Task<IReadOnlyList<PlannedSheet>> Handle(PlanOfficialSheetSyncCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var settings = options.Value;
        var due = new List<(Match Match, bool Ours)>();
        // Sin ninguna web activa aparte de la federación, los partidos de equipos sin plantilla no tienen dónde buscarse.
        var othersReachable = sources.Any(s => !s.IsFederation && settings.Allows(s.Name));

        // Los partidos de todas las temporadas en curso y el estado de sus actas, de una vez y sin cargar las actas enteras.
        var seasons = (await competitions.GetAllAsync(cancellationToken))
            .Where(c => c.CurrentSeason is not null)
            .Select(c => c.CurrentSeason!.Id)
            .ToList();
        var playedBySeason = (await matches.GetBySeasonsAsync(seasons, cancellationToken))
            .Where(m => OfficialSheetRefresh.IsPlayed(m, now) && m.Status != MatchStatus.Cancelled && !memory.SkipMatch(m.Id, now))
            .ToLookup(m => m.SeasonId);
        var stored = (await sheets.GetStoredStatesAsync(playedBySeason.SelectMany(g => g).Select(m => m.Id), cancellationToken))
            .ToDictionary(s => s.MatchId);

        foreach (var seasonId in seasons)
        {
            var pending = playedBySeason[seasonId]
                .Where(m => !stored.TryGetValue(m.Id, out var sheet) || OfficialSheetRefresh.IsDue(sheet, now, !settings.Allows(sheet.Provider)))
                .ToList();
            if (pending.Count == 0)
                continue;

            var teamsWithSquad = await environment.TeamsWithSquadAsync(seasonId, cancellationToken);
            due.AddRange(pending
                .Select(m => (m, teamsWithSquad.Contains(m.HomeTeamId) || teamsWithSquad.Contains(m.AwayTeamId)))
                .Where(d => d.Item2 || othersReachable));
        }

        return due
            .OrderByDescending(d => d.Ours)
            .ThenByDescending(d => d.Match.KickoffAt)
            .Take(command.Max)
            .Select(d => new PlannedSheet(d.Match.Id, d.Ours))
            .ToList();
    }
}
