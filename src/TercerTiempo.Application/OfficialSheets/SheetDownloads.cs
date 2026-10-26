using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Sync;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.OfficialSheets;

public static class SheetDownloadStatus
{
    public const string Pending = "pending";
    public const string Downloading = "downloading";
    // Guardada con alineaciones.
    public const string Done = "done";
    // La federación aún no la tiene (o el partido no aparece): se volverá a mirar en la pasada diaria.
    public const string Missing = "missing";
    public const string Failed = "failed";
}

public sealed record SheetDownloadRow(
    Guid MatchId,
    string Status,
    long Sequence,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Detail,
    int Attempts);

// Actas pedidas desde la administración (la temporada o unas fechas de unas ligas). Se guardan en la base de datos para
// que la descarga, que con los límites de la federación dura días, sobreviva a los reinicios de la API.
public interface ISheetDownloadStore
{
    // Añade los partidos (en ese orden) o vuelve a ponerlos en cola si ya estaban terminados sin acta; devuelve cuántos.
    Task<int> RequestAsync(IReadOnlyList<Guid> matchIds, DateTimeOffset now, CancellationToken cancellationToken);
    Task<Guid?> NextPendingAsync(CancellationToken cancellationToken);
    Task MarkAsync(Guid matchId, string status, string? detail, DateTimeOffset now, CancellationToken cancellationToken);
    // Lo que quedó descargándose al pararse la API vuelve a la cola.
    Task ResetInterruptedAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SheetDownloadRow>> GetAllAsync(CancellationToken cancellationToken);
    Task<int> CancelPendingAsync(CancellationToken cancellationToken);
    Task<int> ClearFinishedAsync(CancellationToken cancellationToken);
}

// Avisa al proceso de las actas de que hay una tanda nueva.
public interface ISheetDownloadSignal
{
    void Signal();
}

public sealed record RequestSheetDownloadsCommand(SyncScope Scope) : ICommand<int>;

// Partidos jugados (y no suspendidos) de las ligas y fechas del alcance cuya acta no está guardada completa. Van en el
// orden en que más rinde la caché de la federación: liga, jornada y hora (una página de jornada sirve para todos sus
// partidos).
internal sealed class RequestSheetDownloadsHandler(
    ICompetitionStore competitions,
    IMatchStore matches,
    IOfficialSheetStore sheets,
    ISheetDownloadStore downloads,
    ISheetDownloadSignal signal,
    TimeProvider clock) : ICommandHandler<RequestSheetDownloadsCommand, int>
{
    public async Task<int> Handle(RequestSheetDownloadsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var scope = command.Scope;
        var wanted = new List<Match>();

        foreach (var competition in (await competitions.GetAllAsync(cancellationToken)).OrderBy(c => c.Tier).ThenBy(c => c.Name))
        {
            if (scope.CompetitionIds.Count > 0 && !scope.CompetitionIds.Contains(competition.Id)) continue;
            if (competition.CurrentSeason is not { } season) continue;

            var played = (await matches.GetBySeasonAsync(season.Id, cancellationToken))
                .Where(m => OfficialSheetRefresh.IsPlayed(m, now) && m.Status != MatchStatus.Cancelled)
                .Where(m => scope.Window is not { } window || window.Contains(DateOnly.FromDateTime(m.KickoffAt.UtcDateTime)))
                .ToList();
            if (played.Count == 0) continue;

            var stored = (await sheets.GetStoredByMatchIdsAsync(played.Select(m => m.Id), cancellationToken))
                .Where(s => s.IsAvailable)
                .Select(s => s.MatchId)
                .ToHashSet();
            wanted.AddRange(played.Where(m => !stored.Contains(m.Id)).OrderBy(m => m.Matchday).ThenBy(m => m.KickoffAt));
        }

        if (wanted.Count == 0) return 0;
        var added = await downloads.RequestAsync(wanted.Select(m => m.Id).ToList(), now, cancellationToken);
        signal.Signal();
        return added;
    }
}

public sealed record QueueRecentSheetsCommand(int Days) : ICommand<RecentSheetsQueued>;

public sealed record RecentSheetsQueued(int Leagues, int Queued, DateOnly From, DateOnly To);

// Las actas de los últimos días de las ligas que publica la federación asturiana: las regionales y las que ya se han
// encontrado en su web (3ª RFEF). 1ª y 2ª RFEF no están ahí y buscarlas solo gastaría peticiones.
internal sealed class QueueRecentSheetsHandler(
    ICompetitionStore competitions,
    IOfficialSheetProvider provider,
    ICommandHandler<RequestSheetDownloadsCommand, int> request,
    TimeProvider clock) : ICommandHandler<QueueRecentSheetsCommand, RecentSheetsQueued>
{
    public async Task<RecentSheetsQueued> Handle(QueueRecentSheetsCommand command, CancellationToken cancellationToken)
    {
        var to = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = to.AddDays(-Math.Max(1, command.Days));
        var leagues = (await competitions.GetAllAsync(cancellationToken))
            .Where(c => c.Level == CompetitionLevel.Regional || c.CurrentSeason?.OfficialRef?.Provider == provider.ProviderName)
            .Select(c => c.Id)
            .ToList();
        if (leagues.Count == 0) return new(0, 0, from, to);

        var scope = new SyncScope(SyncScopeKind.DateRange, from, to, leagues, IncludeRosters: false, IncludeSheets: true);
        return new(leagues.Count, await request.Handle(new RequestSheetDownloadsCommand(scope), cancellationToken), from, to);
    }
}

public sealed record CancelSheetDownloadsCommand(bool OnlyFinished) : ICommand<int>;

internal sealed class CancelSheetDownloadsHandler(ISheetDownloadStore downloads) : ICommandHandler<CancelSheetDownloadsCommand, int>
{
    public Task<int> Handle(CancelSheetDownloadsCommand command, CancellationToken cancellationToken) =>
        command.OnlyFinished ? downloads.ClearFinishedAsync(cancellationToken) : downloads.CancelPendingAsync(cancellationToken);
}
