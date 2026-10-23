using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;

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

// Las actas completas son definitivas para la descarga automática. Solo se buscan datos que aún falten.
internal static class OfficialSheetRefresh
{
    private static readonly TimeSpan PendingRecheck = TimeSpan.FromHours(24);

    public static bool IsPlayed(Match match, DateTimeOffset now) =>
        match.Status == MatchStatus.Played
        || (match.Status is MatchStatus.Scheduled or MatchStatus.InProgress && match.KickoffAt + TimeSpan.FromHours(2) <= now);

    public static bool IsDue(OfficialMatchSheet sheet, Match match, DateTimeOffset now)
    {
        return !sheet.IsAvailable && now - sheet.CheckedAt >= PendingRecheck;
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
    IOfficialSheetStore sheets,
    IOfficialSheetQueue queue,
    OfficialSheetSyncMemory memory,
    TimeProvider clock) : IQueryHandler<GetOfficialSheetQuery, OfficialSheetDto>
{
    public async Task<OfficialSheetDto> Handle(GetOfficialSheetQuery query, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        var now = clock.GetUtcNow();
        var sheet = await sheets.GetByMatchAsync(match.Id, cancellationToken);

        if (match.Status is MatchStatus.Cancelled)
            return Result(sheet, match, OfficialSheetAvailability.Unavailable, "El partido está cancelado.");

        if (!OfficialSheetRefresh.IsPlayed(match, now))
            return Result(sheet, match, OfficialSheetAvailability.NotPlayed, "El partido todavía no se ha jugado.");

        if (sheet is null)
        {
            if (memory.SkipMatch(match.Id, now))
                return OfficialSheetMapper.Empty(match.Id, OfficialSheetAvailability.Unavailable, provider.ProviderName,
                    "No se ha encontrado este partido en ninguna web de actas.");

            queue.Enqueue(match.Id);
            var message = queue.PausedUntil is { } until && until > now
                ? $"Las webs de actas están limitando las consultas; el acta se descargará en cuanto lo permitan (hacia las {until.ToLocalTime():HH:mm})."
                : "El acta aún no está guardada. Se descargará en segundo plano respetando los límites de cada web.";
            return OfficialSheetMapper.Empty(match.Id, OfficialSheetAvailability.Fetching, provider.ProviderName, message);
        }

        if (OfficialSheetRefresh.IsDue(sheet, match, now))
            queue.Enqueue(match.Id);

        return sheet.IsAvailable
            ? Result(sheet, match, OfficialSheetAvailability.Available, null)
            : Result(sheet, match, OfficialSheetAvailability.Pending, "El acta guardada aún está pendiente. La descarga de datos nuevos se comprueba como máximo una vez al día.");
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
internal sealed class PlanOfficialSheetSyncHandler(
    ICompetitionStore competitions,
    IMatchStore matches,
    ISquadStore squads,
    IOfficialSheetStore sheets,
    OfficialSheetSyncMemory memory,
    TimeProvider clock) : ICommandHandler<PlanOfficialSheetSyncCommand, IReadOnlyList<PlannedSheet>>
{
    public async Task<IReadOnlyList<PlannedSheet>> Handle(PlanOfficialSheetSyncCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = new List<(Match Match, bool Ours)>();

        foreach (var competition in await competitions.GetAllAsync(cancellationToken))
        {
            if (competition.CurrentSeason is not { } season)
                continue;

            var played = (await matches.GetBySeasonAsync(season.Id, cancellationToken))
                .Where(m => OfficialSheetRefresh.IsPlayed(m, now) && m.Status != MatchStatus.Cancelled && !memory.SkipMatch(m.Id, now))
                .ToList();
            if (played.Count == 0)
                continue;

            var stored = (await sheets.GetByMatchIdsAsync(played.Select(m => m.Id), cancellationToken)).ToDictionary(s => s.MatchId);
            var pending = played.Where(m => !stored.TryGetValue(m.Id, out var sheet) || OfficialSheetRefresh.IsDue(sheet, m, now)).ToList();
            if (pending.Count == 0)
                continue;

            var teamsWithSquad = (await squads.GetBySeasonAsync(season.Id, cancellationToken)).Select(s => s.TeamId).ToHashSet();
            due.AddRange(pending.Select(m => (m, teamsWithSquad.Contains(m.HomeTeamId) || teamsWithSquad.Contains(m.AwayTeamId))));
        }

        return due
            .OrderByDescending(d => d.Ours)
            .ThenByDescending(d => d.Match.KickoffAt)
            .Take(command.Max)
            .Select(d => new PlannedSheet(d.Match.Id, d.Ours))
            .ToList();
    }
}
