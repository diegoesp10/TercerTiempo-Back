using System.Globalization;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Referees;

// El trabajo de los equipos arbitrales: primero lo que dicen las actas de la federación ya guardadas (se releen de la
// caché, sin pedir nada) y después, para los partidos jugados que sigan sin él, la página del partido en
// futbolasturiano.es, que solo lo publica en algunos (se vuelve a mirar unos días después por si lo añade). De paso lee
// el cuerpo técnico de las actas guardadas antes de que se leyera (también de la caché). Al final, los árbitros
// designados para los partidos de los próximos días, de la página de cada jornada de la federación.
internal sealed class SyncMatchOfficialsHandler(
    IRefereeStore referees,
    IOfficialSheetProvider sheets,
    IOfficialSheetStore sheetStore,
    IFootballDataProvider footballData,
    MatchOfficialsWriter writer,
    ICompetitionStore competitions,
    IMatchStore matches,
    ITeamStore teams,
    IJobProgress progress,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncMatchOfficialsCommand, MatchOfficialsSynced>
{
    private const int LookupBatch = 60;
    private const int StaffBatch = 2000;
    private const int MaxAttempts = 3;
    private const string Job = JobNames.MatchOfficials;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(3);

    // La federación publica el árbitro de cada partido unos días antes en la página de la jornada; para los partidos de
    // los próximos DesignationHorizon se vuelve a pedir como mucho cada DesignationMaxAge (una petición por jornada y grupo).
    internal static readonly TimeSpan DesignationHorizon = TimeSpan.FromDays(6);
    internal static readonly TimeSpan DesignationMaxAge = TimeSpan.FromHours(6);
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public async Task<MatchOfficialsSynced> Handle(SyncMatchOfficialsCommand command, CancellationToken cancellationToken)
    {
        await ReadStaffAsync(cancellationToken);

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

        var designated = await DesignationsAsync(cancellationToken);

        progress.Step(Job, null, total, total);
        return new MatchOfficialsSynced(fromSheets, fromProvider, looked, designated);
    }

    private async Task<int> DesignationsAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = new List<(Season Season, OfficialCompetitionRef Ref, int Matchday, string Label, IReadOnlyList<Match> Matches)>();
        foreach (var competition in await competitions.GetAllAsync(cancellationToken))
        {
            if (!sheets.Publishes(competition.Category)) continue;
            if (competition.CurrentSeason is not { OfficialRef: { } reference } season || reference.Provider != sheets.ProviderName) continue;
            if (OfficialCompetitionRef.Parse(reference.ExternalId) is not { } parsed) continue;

            var seasonMatches = await matches.GetBySeasonAsync(season.Id, cancellationToken);
            foreach (var matchday in seasonMatches.Where(m => Upcoming(m, now)).Select(m => m.Matchday).Distinct().Order())
                due.Add((season, parsed, matchday, competition.Name, seasonMatches.Where(m => m.Matchday == matchday).ToList()));
        }

        var changed = 0;
        for (var i = 0; i < due.Count; i++)
        {
            var (_, reference, matchday, label, matchdayMatches) = due[i];
            progress.Step(Job, $"Árbitros designados · {label}, jornada {matchday}", i, due.Count);
            IReadOnlyList<OfficialFixture> fixtures;
            try
            {
                fixtures = await sheets.GetMatchdayAsync(reference, matchday, DesignationMaxAge, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                // Límite de la federación o sin respuesta: lo designado se conserva y se mira en la próxima pasada.
                progress.Event(Job, $"Árbitros designados: {(ex is ProviderThrottledException ? "la federación está limitando las consultas" : "la federación no responde")}; se miran en la próxima pasada", JobEventTone.Warning);
                break;
            }
            if (fixtures.Count == 0) continue;

            var teamMap = (await teams.GetByIdsAsync(matchdayMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken))
                .ToDictionary(t => t.Id);
            var names = await TeamsAsync(matchdayMatches.Where(m => Upcoming(m, now)).Select(m => m.Id), cancellationToken);
            foreach (var (ours, fixture) in OfficialSheetLocator.Pair(matchdayMatches, fixtures, teamMap))
            {
                if (!Upcoming(ours, now) || await writer.DesignateAsync(ours.Id, fixture.Officials, sheets.ProviderName, cancellationToken) == 0) continue;
                changed++;
                Report(ours.Id, fixture.Officials.Count > 0 ? $"Designado · {Describe(fixture.Officials)}" : "La federación ya no publica el árbitro designado", JobEventTone.Success, names);
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return changed;
    }

    private static bool Upcoming(Match match, DateTimeOffset now) =>
        match.Status == MatchStatus.Scheduled && match.KickoffAt > now && match.KickoffAt <= now + DesignationHorizon;

    // Cuerpo técnico de las actas bajadas antes de leerlo (o antes de leer sus tarjetas): de la página guardada. Si ya no
    // está guardada se marca leída igual, con lo que hubiera, y lo traerá la próxima vez que se baje el acta.
    private async Task ReadStaffAsync(CancellationToken cancellationToken)
    {
        var pending = await sheetStore.GetWithoutStaffAsync(sheets.ProviderName, StaffBatch, cancellationToken);
        if (pending.Count == 0) return;

        var now = clock.GetUtcNow();
        var read = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            progress.Step(Job, "Cuerpos técnicos de las actas ya guardadas", i, pending.Count);
            var data = await sheets.GetCachedSheetAsync(pending[i].ExternalId, cancellationToken);
            if (data?.Staff is { } staff) pending[i].ApplyStaff(staff, now);
            else pending[i].MarkStaffRead(now);
            if (pending[i].Staff.Count > 0) read++;
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        progress.Event(Job, $"Cuerpo técnico leído de {read} {(read == 1 ? "acta guardada" : "actas guardadas")} (sin pedir nada a la federación)", JobEventTone.Success);
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
