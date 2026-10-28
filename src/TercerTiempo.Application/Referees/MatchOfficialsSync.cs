using System.Globalization;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Referees;

// El trabajo de los equipos arbitrales: primero lo que dicen las actas de la federación ya guardadas (se releen de la
// caché, sin pedir nada) y después, para los partidos jugados que sigan sin él, la página del partido en
// futbolasturiano.es, que solo lo publica en algunos (se vuelve a mirar unos días después por si lo añade).
internal sealed class SyncMatchOfficialsHandler(
    IRefereeStore referees,
    IOfficialSheetProvider sheets,
    IFootballDataProvider footballData,
    MatchOfficialsWriter writer,
    IMatchStore matches,
    ITeamStore teams,
    IJobProgress progress,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncMatchOfficialsCommand, MatchOfficialsSynced>
{
    private const int LookupBatch = 60;
    private const int MaxAttempts = 3;
    private const string Job = JobNames.MatchOfficials;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(3);
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public async Task<MatchOfficialsSynced> Handle(SyncMatchOfficialsCommand command, CancellationToken cancellationToken)
    {
        var fromCache = await referees.GetSheetsWithoutOfficialsAsync(sheets.ProviderName, cancellationToken);
        var names = await TeamsAsync(fromCache.Select(s => s.MatchId), cancellationToken);
        var done = 0;
        var total = fromCache.Count;

        var fromSheets = 0;
        foreach (var source in fromCache)
        {
            progress.Step(Job, "Actas de la federación ya guardadas", done++, total);
            if (await sheets.GetCachedSheetAsync(source.ExternalId, cancellationToken) is not { Officials: { Count: > 0 } officials }) continue;
            if (await writer.ApplyAsync(source.MatchId, officials, sheets.ProviderName, authoritative: true, cancellationToken) > 0)
            {
                fromSheets++;
                Report(source.MatchId, $"{Describe(officials)} · del acta de la federación", JobEventTone.Success, names);
            }
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Se buscan después de leer las actas: los que ya han salido de ellas no se piden a futbolasturiano.es.
        var now = clock.GetUtcNow();
        var toLookUp = await referees.GetMatchesToLookUpAsync(footballData.ProviderName, now, RetryAfter, MaxAttempts, LookupBatch, cancellationToken);
        names = await TeamsAsync(toLookUp.Select(s => s.MatchId), cancellationToken);
        total += toLookUp.Count;

        var fromProvider = 0;
        var looked = 0;
        foreach (var source in toLookUp)
        {
            progress.Step(Job, "Página del partido en futbolasturiano.es", done++, total);
            // null: la página no se ha podido leer; se intenta en la próxima pasada sin contarlo.
            var officials = await footballData.GetMatchOfficialsAsync(source.ExternalId, cancellationToken);
            if (officials is null)
            {
                Report(source.MatchId, "No se pudo leer la página del partido; se intenta en la próxima pasada", JobEventTone.Warning, names);
                continue;
            }
            looked++;

            if (officials.Count > 0 && await writer.ApplyAsync(source.MatchId, officials, footballData.ProviderName, authoritative: false, cancellationToken) > 0)
            {
                fromProvider++;
                Report(source.MatchId, $"{Describe(officials)} · de futbolasturiano.es", JobEventTone.Success, names);
            }
            else
            {
                if (await referees.GetLookupAsync(source.MatchId, cancellationToken) is { } lookup) lookup.Again(now);
                else referees.AddLookup(MatchOfficialLookup.First(source.MatchId, now));
                Report(source.MatchId, $"Sin árbitro publicado; se vuelve a mirar el {(now + RetryAfter).ToString("d MMM", Spanish).TrimEnd('.')}", JobEventTone.Info, names);
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        progress.Step(Job, null, total, total);
        return new MatchOfficialsSynced(fromSheets, fromProvider, looked);
    }

    private void Report(Guid matchId, string text, string tone, IReadOnlyDictionary<Guid, (SyncTeamRefDto Home, SyncTeamRefDto Away)> names)
    {
        var teamsOf = names.GetValueOrDefault(matchId);
        progress.Event(Job, text, tone, teamsOf.Home, teamsOf.Away, matchId);
    }

    private static string Describe(IReadOnlyList<MatchOfficialData> officials)
    {
        var main = officials.FirstOrDefault(o => o.Role == OfficialRole.Referee)?.Name;
        var assistants = officials.Where(o => o.Role != OfficialRole.Referee).OrderBy(o => o.Role).Select(o => o.Name).ToList();
        var parts = new List<string>();
        if (main is not null) parts.Add($"Árbitro: {main}");
        if (assistants.Count > 0) parts.Add($"{(assistants.Count == 1 ? "Asistente" : "Asistentes")}: {string.Join(", ", assistants)}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Equipo arbitral";
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
