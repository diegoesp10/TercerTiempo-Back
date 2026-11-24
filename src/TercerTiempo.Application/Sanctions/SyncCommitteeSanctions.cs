using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Sanctions;

namespace TercerTiempo.Application.Sanctions;

public sealed record SyncCommitteeSanctionsCommand : ICommand<CommitteeSanctionsSynced>;

public sealed record CommitteeSanctionsSynced(int Groups, int Checked, int Added, int Changed, int Removed, IReadOnlyList<string> Errors);

// Las sanciones del comité de competición de cada grupo de la federación (la página de sanciones trae toda la temporada
// en una petición). Para gastar las mínimas, un grupo solo se pide:
//   - la primera vez, para traer lo publicado hasta entonces;
//   - con una jornada jugada que el comité aún no ha publicado: desde PublishDelay después de su último partido (el
//     comité se reúne entre semana), como mucho cada PendingSpacing y durante PendingWindow;
//   - si no, una vez por semana, por si corrige algo (un recurso, un partido aplazado).
// Las peticiones pasan por los límites de siempre de la federación.
internal sealed class SyncCommitteeSanctionsHandler(
    ICompetitionStore competitions,
    IMatchStore matches,
    ICommitteeSanctionStore store,
    IOfficialSheetProvider provider,
    IJobProgress progress,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncCommitteeSanctionsCommand, CommitteeSanctionsSynced>
{
    private const string Job = JobNames.CommitteeSanctions;
    internal static readonly TimeSpan PublishDelay = TimeSpan.FromHours(40);
    internal static readonly TimeSpan PendingSpacing = TimeSpan.FromHours(12);
    internal static readonly TimeSpan PendingWindow = TimeSpan.FromDays(9);
    internal static readonly TimeSpan WeeklySpacing = TimeSpan.FromDays(7);

    public async Task<CommitteeSanctionsSynced> Handle(SyncCommitteeSanctionsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var groups = new List<(Season Season, OfficialCompetitionRef Ref, string Label)>();
        foreach (var competition in await competitions.GetAllAsync(cancellationToken))
        {
            if (competition.CurrentSeason is not { OfficialRef: { } reference } season || reference.Provider != provider.ProviderName) continue;
            if (OfficialCompetitionRef.Parse(reference.ExternalId) is not { } parsed) continue;
            groups.Add((season, parsed, competition.Name));
        }

        var checks = (await store.GetChecksAsync(cancellationToken)).ToDictionary(c => c.SeasonId);
        var due = new List<(Season Season, OfficialCompetitionRef Ref, string Label)>();
        foreach (var group in groups)
        {
            var played = LatestPlayed(await matches.GetBySeasonAsync(group.Season.Id, cancellationToken), now);
            if (played is { } p && IsDue(checks.GetValueOrDefault(group.Season.Id), p, now)) due.Add(group);
        }

        var errors = new List<string>();
        int done = 0, added = 0, changed = 0, removed = 0;
        foreach (var (season, reference, label) in due)
        {
            progress.Step(Job, $"Sanciones de {label}", done, due.Count);
            IReadOnlyList<CommitteeMeetingData>? meetings;
            try
            {
                meetings = await provider.GetSanctionsAsync(reference, cancellationToken);
            }
            catch (ProviderThrottledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                // La federación no responde: no se insiste con los demás grupos, ya tocarán en la próxima pasada.
                errors.Add($"{label}: {ex.Message}");
                progress.Event(Job, $"{label}: no se pudo leer la página de sanciones; se intenta en la próxima pasada", JobEventTone.Warning);
                break;
            }
            done++;

            if (meetings is null || meetings.Count == 0)
            {
                // Sin página o sin nada publicado: se conserva lo guardado y se apunta la consulta para no repetirla enseguida.
                Touch(checks, season.Id, null, now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                progress.Event(Job, meetings is null ? $"{label}: la federación no tiene la página de sanciones" : $"{label}: sin sanciones publicadas todavía");
                continue;
            }

            var (newOnes, changedOnes, removedOnes) = await ApplyAsync(season.Id, meetings, now, cancellationToken);
            var published = meetings.Max(m => m.Matchday);
            Touch(checks, season.Id, published, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            added += newOnes;
            changed += changedOnes;
            removed += removedOnes;

            var suspensions = meetings.Sum(m => m.Sanctions.Count);
            progress.Event(Job,
                $"{label}: publicado hasta la jornada {published} ({suspensions} {(suspensions == 1 ? "sanción" : "sanciones")} en la temporada)" +
                (newOnes + changedOnes + removedOnes == 0 ? "; sin novedades" : $"; {Describe(newOnes, changedOnes, removedOnes)}"),
                newOnes + changedOnes > 0 ? JobEventTone.Success : JobEventTone.Info);
        }

        progress.Step(Job, null, done, due.Count);
        return new CommitteeSanctionsSynced(groups.Count, done, added, changed, removed, errors);
    }

    // Cada reunión-jornada de la página se guarda tal cual: las que ya estaban se actualizan si cambian y las que ya no
    // salen se borran (la página trae siempre la temporada entera).
    private async Task<(int Added, int Changed, int Removed)> ApplyAsync(Guid seasonId, IReadOnlyList<CommitteeMeetingData> meetings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stored = (await store.GetStoredBySeasonAsync(seasonId, cancellationToken)).ToDictionary(r => (r.Committee, r.MeetingOn, r.Matchday));
        var seen = new HashSet<(string, DateOnly, int)>();
        int added = 0, changed = 0, removed = 0;
        foreach (var meeting in meetings)
        {
            var key = (meeting.Committee, meeting.MeetingOn, meeting.Matchday);
            if (!seen.Add(key)) continue;

            if (stored.TryGetValue(key, out var existing))
            {
                if (existing.Apply(meeting.MatchdayOn, meeting.Sanctions, now)) changed++;
            }
            else
            {
                store.Add(CommitteeResolution.Create(
                    seasonId, provider.ProviderName, meeting.Committee, meeting.MeetingOn, meeting.Matchday, meeting.MatchdayOn, meeting.Sanctions, now));
                added++;
            }
        }

        foreach (var (key, resolution) in stored)
        {
            if (seen.Contains(key)) continue;
            store.Remove(resolution);
            removed++;
        }
        return (added, changed, removed);
    }

    private void Touch(Dictionary<Guid, CommitteeSanctionCheck> checks, Guid seasonId, int? published, DateTimeOffset now)
    {
        if (checks.TryGetValue(seasonId, out var check))
        {
            check.Checked(published, now);
            return;
        }
        check = CommitteeSanctionCheck.First(seasonId, published, now);
        store.AddCheck(check);
        checks[seasonId] = check;
    }

    // La última jornada con al menos la mitad de sus partidos ya jugados (un adelantado de la siguiente no cuenta) y la
    // hora del último que se jugó.
    internal static (int Matchday, DateTimeOffset LastKickoff)? LatestPlayed(IEnumerable<Match> seasonMatches, DateTimeOffset now)
    {
        var finished = now - TimeSpan.FromHours(2);
        return seasonMatches
            .Where(m => m.Status is not (MatchStatus.Cancelled or MatchStatus.Postponed))
            .GroupBy(m => m.Matchday)
            .Select(g => (Matchday: g.Key, Played: g.Where(m => m.KickoffAt <= finished).ToList(), Total: g.Count()))
            .Where(g => g.Played.Count > 0 && g.Played.Count * 2 >= g.Total)
            .OrderByDescending(g => g.Matchday)
            .Select(g => ((int, DateTimeOffset)?)(g.Matchday, g.Played.Max(m => m.KickoffAt)))
            .FirstOrDefault();
    }

    internal static bool IsDue(CommitteeSanctionCheck? check, (int Matchday, DateTimeOffset LastKickoff) played, DateTimeOffset now)
    {
        if (check is null) return true;

        var since = now - check.CheckedAt;
        var pending = check.PublishedMatchday is not { } published || played.Matchday > published;
        if (pending && now >= played.LastKickoff + PublishDelay && now < played.LastKickoff + PendingWindow)
            return since >= PendingSpacing;
        return since >= WeeklySpacing;
    }

    private static string Describe(int added, int changed, int removed)
    {
        var parts = new List<string>();
        if (added > 0) parts.Add(added == 1 ? "1 resolución nueva" : $"{added} resoluciones nuevas");
        if (changed > 0) parts.Add(changed == 1 ? "1 cambiada" : $"{changed} cambiadas");
        if (removed > 0) parts.Add(removed == 1 ? "1 retirada" : $"{removed} retiradas");
        return string.Join(", ", parts);
    }
}
