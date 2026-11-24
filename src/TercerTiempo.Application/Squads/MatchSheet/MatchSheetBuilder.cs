using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Squads.Suspensions;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Squads.MatchSheet;

internal static class MatchSheetBuilder
{
    public static async Task<MatchSheetDto> BuildAsync(
        Squad squad,
        Match match,
        ICurrentUser currentUser,
        TimeProvider clock,
        ITeamStore teams,
        ICompetitionStore competitions,
        IPlayerStore players,
        IPerformanceStore performances,
        ITeamMatchStatsStore teamStats,
        IClubStore clubs,
        ICallUpStore callUps,
        SuspensionCalendar suspensions,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken)
    {
        var summary = await SquadMapper.ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);

        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);
        var matchDto = MatchMapper.ToDto(match, teamMap);

        var recorded = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
            .ToDictionary(p => p.SquadPlayerId);

        var linkedIds = squad.Players.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var linkedMap = (await players.GetByIdsAsync(linkedIds, cancellationToken)).ToDictionary(p => p.Id);

        // Las observaciones del técnico son para el club y el cuerpo técnico: un jugador solo lee las de su propia línea.
        var isManager = await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken);
        var seesAllNotes = isManager || currentUser.IsAdmin;

        // El acta se prepara con los convocados del partido; quién va convocado solo lo ven quienes la preparan.
        var callUp = seesAllNotes ? await callUps.GetByMatchAsync(squad.Id, match.Id, cancellationToken) : null;
        var called = callUp?.Called.Select(e => e.SquadPlayerId).ToHashSet() ?? [];

        // El dorsal de cada uno es el del acta oficial del partido, también el de quien aún no tiene línea.
        var official = await sheets.GetByMatchAsync(match.Id, cancellationToken);
        var officialLines = OfficialSheetRatings.LinesByPlayer(squad, match, official, recorded.Keys.ToHashSet());

        // Quien era del equipo ese día (también si causó baja después) y quien ya tiene línea.
        var entries = SquadMapper.Ordered(squad.Players.Where(p => p.WasMemberAt(match.KickoffAt) || recorded.ContainsKey(p.Id)))
            .Select(p => (Player: p, Suspension: suspensions.On(p.Id, match.Id)))
            .Select(x => new MatchSheetEntryDto(
                SquadMapper.ToPlayerDto(x.Player, linkedMap),
                recorded.TryGetValue(x.Player.Id, out var performance)
                    ? PerformanceMapper.ToDto(performance, x.Player) is var dto && (seesAllNotes || (currentUser.PlayerId is { } me && x.Player.PlayerId == me)) ? dto : dto with { Notes = null }
                    : null,
                called.Contains(x.Player.Id),
                x.Suspension is not null,
                x.Suspension is null ? null : SuspensionReader.Brief(x.Suspension),
                officialLines.GetValueOrDefault(x.Player.Id)?.ShirtNumber is int number and >= 1 and <= 99 ? number : null))
            .ToList();

        var canEdit = match.Status == MatchStatus.Played && isManager;
        var team = await teamStats.GetAsync(squad.Id, match.Id, cancellationToken);

        return new MatchSheetDto(summary, matchDto, canEdit, entries, team is null ? null : TeamStats.TeamMatchStatsMapper.ToDto(team), called.Count > 0,
            official is { IsAvailable: true });
    }
}
