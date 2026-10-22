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

// Cuándo toca volver a pedir un acta: pendiente cada 10 minutos; publicada, una vez al día durante la semana siguiente
// al partido (la federación corrige actas unos días después).
internal static class OfficialSheetRefresh
{
    private static readonly TimeSpan PendingRecheck = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AvailableRefresh = TimeSpan.FromHours(24);
    private static readonly TimeSpan CorrectionWindow = TimeSpan.FromDays(7);

    public static bool IsPlayed(Match match, DateTimeOffset now) =>
        match.Status == MatchStatus.Played
        || (match.Status is MatchStatus.Scheduled or MatchStatus.InProgress && match.KickoffAt + TimeSpan.FromHours(2) <= now);

    public static bool IsDue(OfficialMatchSheet sheet, Match match, DateTimeOffset now)
    {
        if (!sheet.IsAvailable)
            return now - sheet.CheckedAt >= PendingRecheck;

        var fetchedAt = sheet.FetchedAt ?? sheet.CheckedAt;
        return now - match.KickoffAt <= CorrectionWindow && now - fetchedAt >= AvailableRefresh;
    }
}

// Lo que el proceso en segundo plano ya sabe que no está en la federación (competiciones que no gestiona, partidos que
// no aparecen en su calendario), para no volver a preguntarlo en cada pasada.
internal sealed class OfficialSheetSyncMemory
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _seasonsWithoutCompetition = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _matchesNotFound = new();

    public void SeasonNotFound(Guid seasonId, DateTimeOffset now) => _seasonsWithoutCompetition[seasonId] = now;
    public void MatchNotFound(Guid matchId, DateTimeOffset now) => _matchesNotFound[matchId] = now;

    public bool SkipSeason(Guid seasonId, DateTimeOffset now) =>
        _seasonsWithoutCompetition.TryGetValue(seasonId, out var at) && now - at < RetryAfter;

    public bool SkipMatch(Guid matchId, DateTimeOffset now) =>
        _matchesNotFound.TryGetValue(matchId, out var at) && now - at < RetryAfter;
}

public sealed record GetOfficialSheetQuery(Guid MatchId) : IQuery<OfficialSheetDto>;

// Lectura del acta para el usuario: solo base de datos, nunca la web de la federación. Si falta o está por refrescar,
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
                    "No se ha encontrado este partido en la federación.");

            queue.Enqueue(match.Id);
            var message = queue.PausedUntil is { } until && until > now
                ? $"La federación está limitando las consultas; el acta se descargará en cuanto lo permita (hacia las {until.ToLocalTime():HH:mm})."
                : "Descargando el acta de la federación; aparecerá en unos segundos.";
            return OfficialSheetMapper.Empty(match.Id, OfficialSheetAvailability.Fetching, provider.ProviderName, message);
        }

        if (OfficialSheetRefresh.IsDue(sheet, match, now))
            queue.Enqueue(match.Id);

        return sheet.IsAvailable
            ? Result(sheet, match, OfficialSheetAvailability.Available, null)
            : Result(sheet, match, OfficialSheetAvailability.Pending, "La federación todavía no ha publicado el acta.");
    }

    private static OfficialSheetDto Result(OfficialMatchSheet? sheet, Match match, OfficialSheetAvailability status, string? message) =>
        sheet is null
            ? OfficialSheetMapper.Empty(match.Id, status, null, message)
            : OfficialSheetMapper.ToDto(sheet, match.HomeTeamId, match.AwayTeamId, status, message);
}

public sealed record PlanOfficialSheetSyncCommand(int Max) : ICommand<IReadOnlyList<Guid>>;

// Qué actas toca descargar en esta pasada: partidos jugados de las temporadas en curso sin acta guardada o con el
// acta por refrescar, de los más recientes a los más antiguos. Resuelve de paso la competición de cada temporada en
// la federación (una petición por temporada, una sola vez) y salta las que la federación no tiene.
internal sealed class PlanOfficialSheetSyncHandler(
    ICompetitionStore competitions,
    IMatchStore matches,
    IOfficialSheetStore sheets,
    OfficialSheetLocator locator,
    OfficialSheetSyncMemory memory,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<PlanOfficialSheetSyncHandler> logger) : ICommandHandler<PlanOfficialSheetSyncCommand, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(PlanOfficialSheetSyncCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = new List<Match>();

        foreach (var competition in await competitions.GetAllAsync(cancellationToken))
        {
            if (competition.CurrentSeason is not { } season || memory.SkipSeason(season.Id, now))
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

            try
            {
                if (await locator.ResolveCompetitionAsync(competition, season, cancellationToken) is null)
                {
                    memory.SeasonNotFound(season.Id, now);
                    continue;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "No se pudo localizar {Competition} en la federación; se reintentará en la próxima pasada", competition.Name);
                continue;
            }

            due.AddRange(pending);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return due
            .OrderByDescending(m => m.KickoffAt)
            .Take(command.Max)
            .Select(m => m.Id)
            .ToList();
    }
}
