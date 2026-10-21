using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Sync;

/// <summary>
/// Descarga todas las ligas que publica el proveedor externo y las vuelca en el dominio: competiciones y
/// temporadas, equipos, calendario y resultados, clasificación y plantillas. Es idempotente y cada liga
/// se guarda por separado, de modo que un fallo en una no impide sincronizar las demás.
/// </summary>
public sealed record SyncSeasonCommand : ICommand<SyncResultDto>;

public sealed record SyncResultDto(
    string Provider,
    int Competitions,
    int Teams,
    int Matches,
    int Standings,
    int RosterEntries,
    IReadOnlyList<string> Errors,
    DateTimeOffset SyncedAt);

internal sealed class SyncSeasonHandler(
    IFootballDataProvider provider,
    ICompetitionStore competitions,
    ITeamStore teams,
    IMatchStore matches,
    IStandingStore standings,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<SyncSeasonHandler> logger) : ICommandHandler<SyncSeasonCommand, SyncResultDto>
{
    public async Task<SyncResultDto> Handle(SyncSeasonCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var providerName = provider.ProviderName;

        var snapshots = await provider.GetCompetitionsAsync(cancellationToken);

        // Los equipos pueden cambiar de liga entre temporadas: se indexan por id externo una sola vez.
        var teamsByExternalId = (await teams.GetAllAsync(cancellationToken))
            .Where(t => t.ExternalRef?.Provider == providerName)
            .ToDictionary(t => t.ExternalRef!.ExternalId);

        var totals = (Competitions: 0, Teams: 0, Matches: 0, Standings: 0, Roster: 0);
        var errors = new List<string>();

        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var counts = await SyncCompetitionAsync(snapshot, providerName, teamsByExternalId, now, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                totals.Competitions++;
                totals.Teams += counts.Teams;
                totals.Matches += counts.Matches;
                totals.Standings += counts.Standings;
                totals.Roster += counts.Roster;

                logger.LogInformation("Sincronizada {Competition} ({Season}): {Teams} equipos, {Matches} partidos, {Standings} filas, {Roster} fichas",
                    snapshot.Name, snapshot.SeasonLabel, counts.Teams, counts.Matches, counts.Standings, counts.Roster);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sincronizando la competición {Competition}", snapshot.Name);
                errors.Add($"{snapshot.Name}: {ex.Message}");
            }
        }

        logger.LogInformation("Sincronización con {Provider} completada: {Competitions} competiciones, {Teams} equipos, {Matches} partidos, {Errors} errores",
            providerName, totals.Competitions, totals.Teams, totals.Matches, errors.Count);

        return new SyncResultDto(providerName, totals.Competitions, totals.Teams, totals.Matches, totals.Standings, totals.Roster, errors, now);
    }

    private async Task<(int Teams, int Matches, int Standings, int Roster)> SyncCompetitionAsync(
        CompetitionSnapshot snapshot,
        string providerName,
        Dictionary<string, Team> teamsByExternalId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // 1. Competición y temporada
        var competitionRef = ExternalRef.Create(providerName, snapshot.ExternalId);
        var seasonRef = ExternalRef.Create(providerName, snapshot.SeasonExternalId);

        var competition = await competitions.GetByExternalRefAsync(competitionRef, cancellationToken);
        if (competition is null)
        {
            competition = Competition.Create(snapshot.Name, snapshot.Category, snapshot.Level, snapshot.Tier, snapshot.Group, snapshot.Region, competitionRef);
            competitions.Add(competition);
        }
        else
        {
            competition.UpdateDetails(snapshot.Name, snapshot.Category, snapshot.Level, snapshot.Tier, snapshot.Group, snapshot.Region);
        }

        var season = competition.Seasons.FirstOrDefault(s => s.ExternalRef == seasonRef)
            ?? competition.AddSeason(snapshot.SeasonLabel, snapshot.StartsOn, snapshot.EndsOn, seasonRef);
        competition.MarkCurrent(season.Id);

        // 2. Calendario según el proveedor. Se le pasan los partidos ya guardados de esta temporada para que pueda
        //    reutilizarlos (futbolasturiano.es tiene que leer de una en una las páginas de los partidos que su API oculta).
        //    Va antes que los equipos porque así el proveedor puede descubrir equipos que solo aparecen en esos partidos.
        var matchesByExternalId = (await matches.GetBySeasonAsync(season.Id, cancellationToken))
            .Where(m => m.ExternalRef?.Provider == providerName)
            .ToDictionary(m => m.ExternalRef!.ExternalId);

        var externalIdByTeamId = teamsByExternalId.ToDictionary(kv => kv.Value.Id, kv => kv.Key);
        var known = matchesByExternalId.Values
            .Select(m => ToSnapshot(m, externalIdByTeamId))
            .OfType<MatchSnapshot>()
            .ToList();

        var matchSnapshots = await provider.GetMatchesAsync(snapshot.ExternalId, known, cancellationToken);

        // 3. Equipos
        var teamSnapshots = await provider.GetTeamsAsync(snapshot.ExternalId, cancellationToken);

        foreach (var ts in teamSnapshots)
        {
            if (teamsByExternalId.TryGetValue(ts.ExternalId, out var team))
            {
                team.UpdateDetails(ts.Name, ts.ShortName, ts.CrestUrl);
                continue;
            }

            team = Team.Create(ts.Name, ts.ShortName, ts.CrestUrl, ExternalRef.Create(providerName, ts.ExternalId));
            teams.Add(team);
            teamsByExternalId[ts.ExternalId] = team;
        }

        // 4. Partidos
        var syncedMatches = 0;
        foreach (var ms in matchSnapshots)
        {
            if (!teamsByExternalId.TryGetValue(ms.HomeTeamExternalId, out var home)
                || !teamsByExternalId.TryGetValue(ms.AwayTeamExternalId, out var away))
            {
                logger.LogWarning("Partido {ExternalId} de {Competition} ignorado: equipo desconocido ({Home} vs {Away})",
                    ms.ExternalId, snapshot.Name, ms.HomeTeamExternalId, ms.AwayTeamExternalId);
                continue;
            }

            if (!matchesByExternalId.TryGetValue(ms.ExternalId, out var match))
            {
                match = Match.Schedule(season.Id, ms.Matchday, home.Id, away.Id, ms.KickoffAt, ms.Venue,
                    ExternalRef.Create(providerName, ms.ExternalId));
                matches.Add(match);
                matchesByExternalId[ms.ExternalId] = match;
            }

            ApplyMatchSnapshot(match, ms, now);
            syncedMatches++;
        }

        // 5. Clasificación
        var standingSnapshots = await provider.GetStandingsAsync(snapshot.ExternalId, cancellationToken);
        var standingsByTeam = (await standings.GetBySeasonAsync(season.Id, cancellationToken)).ToDictionary(s => s.TeamId);

        var syncedStandings = 0;
        foreach (var ss in standingSnapshots)
        {
            if (!teamsByExternalId.TryGetValue(ss.TeamExternalId, out var team)) continue;

            var line = new StandingLine(ss.Position, ss.Played, ss.Won, ss.Drawn, ss.Lost, ss.GoalsFor, ss.GoalsAgainst, ss.Points);

            if (standingsByTeam.TryGetValue(team.Id, out var standing))
                standing.Update(line, now);
            else
                standings.Add(Standing.Create(season.Id, team.Id, line, now));

            syncedStandings++;
        }

        // 6. Plantillas
        var rosterByTeam = (await teams.GetSeasonRosterAsync(season.Id, cancellationToken)).ToLookup(r => r.TeamId);

        var syncedRoster = 0;
        foreach (var ts in teamSnapshots)
        {
            var team = teamsByExternalId[ts.ExternalId];
            var current = rosterByTeam[team.Id].ToList();

            foreach (var rs in await provider.GetRosterAsync(ts.ExternalId, cancellationToken))
            {
                var normalized = NameNormalizer.Normalize(rs.FullName);

                var entry = (rs.ExternalId is null ? null : current.FirstOrDefault(r => r.ExternalRef?.ExternalId == rs.ExternalId))
                    ?? current.FirstOrDefault(r => r.NormalizedName == normalized);

                if (entry is null)
                {
                    var externalRef = rs.ExternalId is null ? null : ExternalRef.Create(providerName, rs.ExternalId);
                    teams.AddRosterEntry(RosterEntry.Create(team.Id, season.Id, rs.FullName, rs.Position, rs.ShirtNumber, externalRef, now));
                }
                else
                {
                    entry.Update(rs.FullName, rs.Position, rs.ShirtNumber, now);
                }

                syncedRoster++;
            }
        }

        return (teamSnapshots.Count, syncedMatches, syncedStandings, syncedRoster);
    }

    private static MatchSnapshot? ToSnapshot(Match match, IReadOnlyDictionary<Guid, string> externalIdByTeamId)
    {
        if (match.ExternalRef is null
            || !externalIdByTeamId.TryGetValue(match.HomeTeamId, out var home)
            || !externalIdByTeamId.TryGetValue(match.AwayTeamId, out var away))
            return null;

        return new MatchSnapshot(
            match.ExternalRef.ExternalId, match.Matchday, home, away, match.KickoffAt, match.Status,
            match.Score?.Home, match.Score?.Away, match.Venue);
    }

    private static void ApplyMatchSnapshot(Match match, MatchSnapshot snapshot, DateTimeOffset now)
    {
        // Jornada, fecha y campo se corrigen siempre (también en partidos ya jugados: antes se guardaba la fecha
        // provisional del calendario en lugar de la real).
        match.UpdateFixture(snapshot.Matchday, snapshot.KickoffAt, snapshot.Venue);

        switch (snapshot.Status)
        {
            case MatchStatus.Played when snapshot.HomeGoals is { } home && snapshot.AwayGoals is { } away:
                match.RecordResult(home, away, now);
                break;

            case MatchStatus.Postponed when match.Status != MatchStatus.Played:
                match.Postpone();
                break;

            case MatchStatus.Cancelled when match.Status != MatchStatus.Played:
                match.Cancel();
                break;

            case MatchStatus.InProgress when match.Status != MatchStatus.Played:
                match.MarkInProgress();
                break;

            case MatchStatus.Scheduled when match.Status is MatchStatus.Postponed or MatchStatus.InProgress:
                match.Reschedule(snapshot.KickoffAt);
                break;
        }
    }
}
