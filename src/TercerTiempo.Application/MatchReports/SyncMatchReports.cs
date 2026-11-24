using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.MatchReports;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.MatchReports;

public sealed record SyncMatchReportsCommand : ICommand<MatchReportsSynced>;

public sealed record MatchReportsSynced(int Due, int Read, int WithReport, int Added, int Changed, int Removed, IReadOnlyList<string> Errors, int Expired = 0);

// Las crónicas de los partidos en futbolasturiano.es, para saber quién se lesionó: el acta de la federación no lo dice.
// Solo partidos de los últimos Window días con el acta oficial ya cerrada, que dice quién jugó y cuándo salió cada uno.
// Se piden de Batch en Batch en una sola petición, y cada partido se lee:
//   - en cuanto tiene el acta cerrada;
//   - sin crónica, cada RetrySpacing hasta MaxAttemptsWithoutReport veces (muchos partidos nunca la tienen);
//   - con crónica, otra vez RereadAfter después, por si la completan, y ya no más.
// De cada crónica solo se guardan las frases que cuentan una lesión, nunca el texto entero.
internal sealed class SyncMatchReportsHandler(
    IMatchReportStore store,
    IOfficialSheetStore sheetStore,
    IFootballDataProvider provider,
    IMatchStore matches,
    ITeamStore teams,
    IJobProgress progress,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncMatchReportsCommand, MatchReportsSynced>
{
    private const string Job = JobNames.MatchReports;
    internal const int Batch = 20;
    internal const int MaxPerPass = 200;
    internal const int MaxAttemptsWithoutReport = 6;
    // Menos que esto no es una crónica (una línea suelta o la ficha vacía del partido).
    internal const int MinReportLength = 120;
    internal static readonly TimeSpan Window = TimeSpan.FromDays(7);
    internal static readonly TimeSpan AfterKickoff = TimeSpan.FromHours(3);
    internal static readonly TimeSpan RetrySpacing = TimeSpan.FromHours(6);
    internal static readonly TimeSpan RereadAfter = TimeSpan.FromHours(24);
    // Lo que se guarda de una crónica (la frase y el enlace) se borra a los 60 días: para entonces ya se registró o se
    // descartó en las plantillas, que guardan su copia.
    internal static readonly TimeSpan KeepMentions = TimeSpan.FromDays(60);

    public async Task<MatchReportsSynced> Handle(SyncMatchReportsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var expired = await store.RemoveMentionsUpdatedBeforeAsync(now - KeepMentions, cancellationToken);
        var candidates = await store.GetCandidatesAsync(provider.ProviderName, now - Window, now - AfterKickoff, cancellationToken);
        var checks = (await store.GetChecksAsync(candidates.Select(c => c.MatchId).ToList(), cancellationToken)).ToDictionary(c => c.MatchId);
        var due = candidates
            .Where(c => IsDue(checks.GetValueOrDefault(c.MatchId), now))
            .OrderByDescending(c => c.KickoffAt)
            .Take(MaxPerPass)
            .ToList();
        var sheets = due.Count == 0
            ? []
            : (await sheetStore.GetAvailableLinesByMatchIdsAsync(due.Select(d => d.MatchId), cancellationToken)).ToDictionary(s => s.MatchId);
        due = due.Where(d => sheets.ContainsKey(d.MatchId)).ToList();
        if (due.Count == 0) return new MatchReportsSynced(0, 0, 0, 0, 0, 0, [], expired);

        var names = await TeamsAsync(due.Select(d => d.MatchId), cancellationToken);
        var errors = new List<string>();
        int read = 0, withReport = 0, added = 0, changed = 0, removed = 0;
        foreach (var chunk in due.Chunk(Batch))
        {
            progress.Step(Job, "Crónicas de futbolasturiano.es", read, due.Count);
            var reports = await provider.GetMatchReportsAsync(chunk.Select(c => c.ExternalId).ToList(), cancellationToken);
            if (reports is null)
            {
                // La web no responde: no se insiste con los demás, ya tocarán en la próxima pasada.
                errors.Add("futbolasturiano.es no respondió al pedir las crónicas");
                progress.Event(Job, "No se pudieron leer las crónicas; se intenta en la próxima pasada", JobEventTone.Warning);
                break;
            }

            var byId = reports.GroupBy(r => r.ExternalId).ToDictionary(g => g.Key, g => g.First());
            var stored = (await store.GetStoredByMatchesAsync(chunk.Select(c => c.MatchId).ToList(), cancellationToken)).ToLookup(m => m.MatchId);
            foreach (var source in chunk)
            {
                read++;
                var report = byId.GetValueOrDefault(source.ExternalId);
                var found = report is not null && report.Text.Length >= MinReportLength;
                if (found)
                {
                    withReport++;
                    var (a, c, r) = Apply(source.MatchId, sheets[source.MatchId], report!, stored[source.MatchId].ToList(), names, now);
                    added += a;
                    changed += c;
                    removed += r;
                }
                Touch(checks, source.MatchId, found, now);
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        progress.Step(Job, null, read, due.Count);
        return new MatchReportsSynced(due.Count, read, withReport, added, changed, removed, errors, expired);
    }

    internal static bool IsDue(MatchReportCheck? check, DateTimeOffset now) =>
        check is null || !check.Done && (check.FoundAt is { } foundAt ? now - foundAt >= RereadAfter : now - check.CheckedAt >= RetrySpacing);

    // Lo que dice ahora la crónica: las lesiones nuevas se añaden, las que cambian se actualizan y las que ya no cuenta
    // se quitan (las propuestas ya hechas conservan su copia).
    private (int Added, int Changed, int Removed) Apply(
        Guid matchId, OfficialMatchSheet sheet, MatchReportSnapshot report, List<MatchInjuryMention> stored,
        IReadOnlyDictionary<Guid, (SyncTeamRefDto Home, SyncTeamRefDto Away)> names, DateTimeOffset now)
    {
        var players = sheet.Lines.Select(l => new ReportPlayer(l.Side, l.FullName, l.ShirtNumber, l.Played, l.MinuteOut)).ToList();
        var existing = stored.ToDictionary(m => (m.Side, m.NormalizedName));
        var seen = new HashSet<(MatchSide, string)>();
        int added = 0, changed = 0, removed = 0;
        foreach (var injury in InjuryMentionDetector.Detect(report.Text, players))
        {
            var player = injury.Player;
            var key = (player.Side, NameNormalizer.Normalize(player.FullName.Replace(',', ' ')));
            if (!seen.Add(key)) continue;

            if (existing.TryGetValue(key, out var mention))
            {
                if (mention.Apply(player.ShirtNumber, player.MinuteOut, injury.Excerpt, injury.Phrase, report.Url, now)) changed++;
                continue;
            }

            store.Add(MatchInjuryMention.Create(
                matchId, player.Side, player.FullName, player.ShirtNumber, player.MinuteOut, injury.Excerpt, injury.Phrase,
                provider.ProviderName, report.Url, now));
            added++;
            var minute = player.MinuteOut is { } m ? $" (sale en el {m}')" : string.Empty;
            Report(matchId, $"Posible lesión de {player.FullName}{minute}: «{injury.Phrase}»", JobEventTone.Success, names);
        }

        foreach (var (key, mention) in existing)
        {
            if (seen.Contains(key)) continue;
            store.Remove(mention);
            removed++;
        }
        return (added, changed, removed);
    }

    private void Touch(Dictionary<Guid, MatchReportCheck> checks, Guid matchId, bool found, DateTimeOffset now)
    {
        if (checks.TryGetValue(matchId, out var check))
        {
            check.Again(found, now);
            if (check.FoundAt is null && check.Attempts >= MaxAttemptsWithoutReport) check.GiveUp();
            return;
        }
        check = MatchReportCheck.First(matchId, found, now);
        store.AddCheck(check);
        checks[matchId] = check;
    }

    private void Report(Guid matchId, string text, string tone, IReadOnlyDictionary<Guid, (SyncTeamRefDto Home, SyncTeamRefDto Away)> names)
    {
        names.TryGetValue(matchId, out var pair);
        progress.Event(Job, text, tone, pair.Home, pair.Away, matchId);
    }

    private async Task<IReadOnlyDictionary<Guid, (SyncTeamRefDto Home, SyncTeamRefDto Away)>> TeamsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken)
    {
        var found = await matches.GetByIdsAsync(matchIds.Distinct(), cancellationToken);
        var byId = (await teams.GetByIdsAsync(found.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken))
            .ToDictionary(t => t.Id);

        SyncTeamRefDto Ref(Guid id) => byId.TryGetValue(id, out var team)
            ? new SyncTeamRefDto(team.Id, team.Name, team.CrestLink())
            : new SyncTeamRefDto(id, "Equipo", null);

        return found.ToDictionary(m => m.Id, m => (Ref(m.HomeTeamId), Ref(m.AwayTeamId)));
    }
}
