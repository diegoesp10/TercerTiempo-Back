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
/// Descarga la competición/temporada configurada en el proveedor externo y la vuelca en el dominio
/// (equipos, calendario y resultados, clasificación y plantillas). Es idempotente: se puede repetir.
/// </summary>
public sealed record SyncSeasonCommand : ICommand<SyncResultDto>;

public sealed record SyncResultDto(
    string Provider,
    string Competition,
    string Season,
    int Teams,
    int Matches,
    int Standings,
    int RosterEntries,
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

        // 1. Competición y temporada
        var snapshot = await provider.GetCompetitionSeasonAsync(cancellationToken);
        var competitionRef = ExternalRef.Create(providerName, snapshot.CompetitionExternalId);
        var seasonRef = ExternalRef.Create(providerName, snapshot.SeasonExternalId);

        var competition = await competitions.GetByExternalRefAsync(competitionRef, cancellationToken);
        if (competition is null)
        {
            competition = Competition.Create(snapshot.CompetitionName, snapshot.Region, competitionRef);
            competitions.Add(competition);
        }
        else
        {
            competition.UpdateDetails(snapshot.CompetitionName, snapshot.Region);
        }

        var season = competition.Seasons.FirstOrDefault(s => s.ExternalRef == seasonRef)
            ?? competition.AddSeason(snapshot.SeasonLabel, snapshot.StartsOn, snapshot.EndsOn, seasonRef);
        competition.MarkCurrent(season.Id);

        // 2. Equipos
        var teamSnapshots = await provider.GetTeamsAsync(cancellationToken);
        var teamsByExternalId = (await teams.GetAllAsync(cancellationToken))
            .Where(t => t.ExternalRef?.Provider == providerName)
            .ToDictionary(t => t.ExternalRef!.ExternalId);

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

        // 3. Partidos
        var matchSnapshots = await provider.GetMatchesAsync(cancellationToken);
        var matchesByExternalId = (await matches.GetBySeasonAsync(season.Id, cancellationToken))
            .Where(m => m.ExternalRef?.Provider == providerName)
            .ToDictionary(m => m.ExternalRef!.ExternalId);

        var syncedMatches = 0;
        foreach (var ms in matchSnapshots)
        {
            if (!teamsByExternalId.TryGetValue(ms.HomeTeamExternalId, out var home)
                || !teamsByExternalId.TryGetValue(ms.AwayTeamExternalId, out var away))
            {
                logger.LogWarning("Partido {ExternalId} ignorado: equipo desconocido ({Home} vs {Away})",
                    ms.ExternalId, ms.HomeTeamExternalId, ms.AwayTeamExternalId);
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

        // 4. Clasificación
        var standingSnapshots = await provider.GetStandingsAsync(cancellationToken);
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

        // 5. Plantillas
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Sincronización completada con {Provider}: {Teams} equipos, {Matches} partidos, {Standings} filas, {Roster} fichas",
            providerName, teamSnapshots.Count, syncedMatches, syncedStandings, syncedRoster);

        return new SyncResultDto(providerName, competition.Name, season.Label,
            teamSnapshots.Count, syncedMatches, syncedStandings, syncedRoster, now);
    }

    private static void ApplyMatchSnapshot(Match match, MatchSnapshot snapshot, DateTimeOffset now)
    {
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

            case MatchStatus.Scheduled when match.Status != MatchStatus.Played && match.KickoffAt != snapshot.KickoffAt:
                match.Reschedule(snapshot.KickoffAt);
                break;
        }
    }
}
