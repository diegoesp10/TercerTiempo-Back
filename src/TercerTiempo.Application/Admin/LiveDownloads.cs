using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Admin;

public sealed record LiveDownloadsDto(SyncLiveDto Football, SheetQueueDto Sheets, DateTimeOffset GeneratedAt);

public sealed record SheetQueueDto(
    int Pending,
    int Downloading,
    int Done,
    int Missing,
    int Failed,
    [property: Description("La federación tiene en pausa las descargas hasta entonces (bloqueo o tope de peticiones).")]
    DateTimeOffset? PausedUntil,
    int HourRequests,
    int MaxPerHour,
    int DayRequests,
    int MaxPerDay,
    DateTimeOffset? NextRequestAt,
    [property: Description("Peticiones que faltan para las actas en cola (una por acta y una por jornada que no esté en caché).")]
    int RequestsNeeded,
    [property: Description("Cuándo terminaría la cola al ritmo que permiten los límites de la federación (aproximado).")]
    DateTimeOffset? EstimatedFinishAt,
    IReadOnlyList<SheetQueueItemDto> Items);

public sealed record SheetQueueItemDto(
    Guid MatchId,
    string Status,
    string Competition,
    int Matchday,
    DateTimeOffset KickoffAt,
    SyncTeamRefDto Home,
    SyncTeamRefDto Away,
    int? HomeGoals,
    int? AwayGoals,
    string? Detail,
    int Attempts,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record GetLiveDownloadsQuery : IQuery<LiveDownloadsDto>;

internal sealed class GetLiveDownloadsHandler(
    ISyncProgress progress,
    ISheetDownloadStore downloads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IOfficialSheetQueue queue,
    IJobsMonitor monitor,
    TimeProvider clock) : IQueryHandler<GetLiveDownloadsQuery, LiveDownloadsDto>
{
    private const string Federation = "asturfutbol";
    private const int ItemsShown = 600;
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(30);

    public async Task<LiveDownloadsDto> Handle(GetLiveDownloadsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var rows = await downloads.GetAllAsync(cancellationToken);

        var shown = rows
            .OrderBy(r => r.Status switch { SheetDownloadStatus.Downloading => 0, SheetDownloadStatus.Pending => 1, _ => 2 })
            .ThenBy(r => r.Status == SheetDownloadStatus.Pending ? r.Sequence : 0)
            .ThenByDescending(r => r.FinishedAt)
            .Take(ItemsShown)
            .ToList();

        var byId = (await matches.GetByIdsAsync(rows.Select(r => r.MatchId), cancellationToken)).ToDictionary(m => m.Id);
        var teamById = (await teams.GetByIdsAsync(byId.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken))
            .ToDictionary(t => t.Id);
        var competitionBySeason = (await competitions.GetAllAsync(cancellationToken))
            .SelectMany(c => c.Seasons.Select(s => (s.Id, c.Name)))
            .ToDictionary(x => x.Id, x => x.Name);

        SyncTeamRefDto Ref(Guid id) => teamById.TryGetValue(id, out var team)
            ? new SyncTeamRefDto(team.Id, team.Name, team.CrestLink())
            : new SyncTeamRefDto(id, "Equipo", null);

        var items = shown
            .Where(r => byId.ContainsKey(r.MatchId))
            .Select(r =>
            {
                var match = byId[r.MatchId];
                return new SheetQueueItemDto(
                    match.Id, r.Status, competitionBySeason.GetValueOrDefault(match.SeasonId, "Liga"), match.Matchday, match.KickoffAt,
                    Ref(match.HomeTeamId), Ref(match.AwayTeamId), match.Score?.Home, match.Score?.Away,
                    r.Detail, r.Attempts, r.RequestedAt, r.StartedAt, r.FinishedAt);
            })
            .ToList();

        var source = await monitor.SourceAsync(Federation, cancellationToken);
        var pausedUntil = source?.PausedUntil ?? (queue.PausedUntil > now ? queue.PausedUntil : null);
        var pending = rows.Where(r => r.Status is SheetDownloadStatus.Pending or SheetDownloadStatus.Downloading).ToList();

        // Una petición por acta y otra por cada jornada distinta (la página de la jornada se guarda un día en caché).
        var needed = pending.Count + pending
            .Select(r => byId.GetValueOrDefault(r.MatchId))
            .Where(m => m is not null)
            .Select(m => (m!.SeasonId, m.Matchday))
            .Distinct()
            .Count();

        var maxHour = source?.MaxPerHour ?? 20;
        var maxDay = source?.MaxPerDay ?? 60;
        var estimated = needed == 0 ? (DateTimeOffset?)null : EstimateFinish(
            needed, now, pausedUntil, source?.NextRequestAt, source?.HourRequests ?? 0, maxHour, source?.DayRequests ?? 0, maxDay);

        var sheets = new SheetQueueDto(
            rows.Count(r => r.Status == SheetDownloadStatus.Pending),
            rows.Count(r => r.Status == SheetDownloadStatus.Downloading),
            rows.Count(r => r.Status == SheetDownloadStatus.Done),
            rows.Count(r => r.Status == SheetDownloadStatus.Missing),
            rows.Count(r => r.Status == SheetDownloadStatus.Failed),
            pausedUntil,
            source?.HourRequests ?? 0, maxHour, source?.DayRequests ?? 0, maxDay,
            source?.NextRequestAt,
            needed,
            estimated,
            items);

        return new LiveDownloadsDto(progress.Live(), sheets, now);
    }

    // Recorre las horas que vienen gastando lo que permiten los topes por hora y por día (en UTC, como los contadores).
    internal static DateTimeOffset? EstimateFinish(
        int requests, DateTimeOffset now, DateTimeOffset? pausedUntil, DateTimeOffset? nextRequestAt,
        int hourUsed, int maxHour, int dayUsed, int maxDay)
    {
        if (maxHour <= 0 || maxDay <= 0) return null;

        var at = now;
        if (pausedUntil > at) { at = pausedUntil.Value; hourUsed = 0; dayUsed = 0; }
        if (nextRequestAt > at) at = nextRequestAt.Value;

        var perHour = Math.Min(maxHour, (int)(TimeSpan.FromHours(1) / RequestSpacing));
        for (var guard = 0; guard < 24 * 400; guard++)
        {
            var hourStart = new DateTimeOffset(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero);
            var allowed = Math.Min(perHour - hourUsed, maxDay - dayUsed);
            if (allowed > 0)
            {
                var taken = Math.Min(allowed, requests);
                var available = (int)((hourStart.AddHours(1) - at) / RequestSpacing);
                taken = Math.Min(taken, Math.Max(1, available));
                requests -= taken;
                hourUsed += taken;
                dayUsed += taken;
                if (requests <= 0) return at + RequestSpacing * taken;
            }

            var next = dayUsed >= maxDay ? new DateTimeOffset(at.Year, at.Month, at.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1) : hourStart.AddHours(1);
            if (next.Date != at.Date) dayUsed = 0;
            hourUsed = 0;
            at = next;
        }
        return null;
    }
}
