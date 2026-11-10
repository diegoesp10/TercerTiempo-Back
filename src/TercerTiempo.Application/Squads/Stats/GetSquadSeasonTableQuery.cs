using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Stats;

public sealed record SquadSeasonRowDto(
    SquadPlayerDto Player,
    [property: Description("Partidos en los que jugó (minutos > 0).")]
    int Matches,
    int Starts,
    int Minutes,
    int Goals,
    [property: Description("Solo de las actas internas: la oficial no recoge asistencias.")]
    int Assists,
    int YellowCards,
    int RedCards,
    [property: Description("Faltas cometidas; solo de las actas internas.")]
    int Fouls,
    [property: Description("Nota media de las actas internas; null si no tiene ninguna.")]
    double? AverageRating,
    [property: Description("Partidos contados con el acta interna del cuerpo técnico.")]
    int FromInternal,
    [property: Description("Partidos contados con el acta oficial de la federación (sin acta interna de ese partido).")]
    int FromOfficial);

public sealed record SquadSeasonTableDto(
    SquadSummaryDto Squad,
    int TeamMatchesPlayed,
    int InternalSheets,
    int OfficialSheets,
    IReadOnlyList<SquadSeasonRowDto> Rows);

public sealed record GetSquadSeasonTableQuery(Guid SquadId) : IQuery<SquadSeasonTableDto>;

// Temporada de cada jugador de la plantilla partido a partido: si el cuerpo técnico registró el acta interna de ese
// partido, cuenta esa línea (minutos, goles, asistencias, tarjetas, faltas, nota); si no, la del acta oficial de la
// federación cuando está descargada (minutos, goles y tarjetas). Así la tabla tiene datos aunque el equipo aún no lleve
// sus actas al día.
internal sealed class GetSquadSeasonTableHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances,
    IOfficialSheetStore sheets,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<GetSquadSeasonTableQuery, SquadSeasonTableDto>
{
    public async Task<SquadSeasonTableDto> Handle(GetSquadSeasonTableQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, query.SquadId);

        await SquadAccess.EnsureCanViewAsync(squad, currentUser, clock, clubs, cancellationToken);

        var summary = await SquadMapper.ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);
        var played = (await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken))
            .Where(m => m.Status == MatchStatus.Played)
            .ToList();

        var internalLines = (await performances.GetBySquadAsync(squad.Id, cancellationToken))
            .ToDictionary(p => (p.MatchId, p.SquadPlayerId));
        var internalMatches = internalLines.Keys.Select(k => k.MatchId).ToHashSet();

        var officialSheets = (await sheets.GetByMatchIdsAsync(played.Select(m => m.Id), cancellationToken))
            .Where(s => s.IsAvailable)
            .ToDictionary(s => s.MatchId);

        var roster = squad.Players.Where(p => p.IsActive).ToList();
        var officialLines = new Dictionary<(Guid MatchId, Guid PlayerId), OfficialSheetLine>();
        foreach (var match in played)
        {
            if (!officialSheets.TryGetValue(match.Id, out var sheet)) continue;
            var side = match.HomeTeamId == squad.TeamId ? MatchSide.Home : MatchSide.Away;
            foreach (var (playerId, line) in SheetMatcher.Match(roster, sheet.LinesOf(side)))
                officialLines[(match.Id, playerId)] = line;
        }

        var linkedIds = roster.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var linkedMap = (await players.GetByIdsAsync(linkedIds, cancellationToken)).ToDictionary(p => p.Id);
        var photos = await SquadMapper.PhotoLinksAsync(squad, teams, cancellationToken);

        var rows = new List<SquadSeasonRowDto>();
        foreach (var player in SquadMapper.Ordered(roster))
        {
            int games = 0, starts = 0, minutes = 0, goals = 0, assists = 0, yellows = 0, reds = 0, fouls = 0, fromInternal = 0, fromOfficial = 0;
            var ratings = new List<double>();

            foreach (var match in played)
            {
                if (internalLines.TryGetValue((match.Id, player.Id), out var line))
                {
                    if (line.MinutesPlayed <= 0) continue;
                    games++;
                    fromInternal++;
                    if (line.Started) starts++;
                    minutes += line.MinutesPlayed;
                    goals += line.Goals;
                    assists += line.Assists;
                    yellows += line.YellowCards;
                    if (line.RedCard) reds++;
                    fouls += line.Fouls;
                    if (line.Rating is { } rating) ratings.Add(rating);
                }
                else if (!internalMatches.Contains(match.Id) && officialLines.TryGetValue((match.Id, player.Id), out var official) && official.Played)
                {
                    games++;
                    fromOfficial++;
                    if (official.Started) starts++;
                    minutes += official.MinutesPlayed;
                    goals += official.Goals + official.PenaltyGoals;
                    yellows += official.YellowCards;
                    if (official.RedCard) reds++;
                }
            }

            rows.Add(new SquadSeasonRowDto(
                SquadMapper.WithPhoto(SquadMapper.ToPlayerDto(player, linkedMap), player, photos, linkedMap),
                games, starts, minutes, goals, assists, yellows, reds, fouls,
                ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2),
                fromInternal, fromOfficial));
        }

        return new SquadSeasonTableDto(summary, played.Count, internalMatches.Count,
            played.Count(m => officialSheets.ContainsKey(m.Id)), rows);
    }
}
