using System.ComponentModel;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Suspensions;

public sealed record SuspensionBriefDto(
    [property: Description("YellowCards (ciclo de cinco amarillas), SecondYellow (expulsado por doble amarilla) o RedCard (roja directa).")]
    SuspensionCause Cause,
    [property: Description("Por qué, para leer: «5.ª amarilla en la jornada 7», «Roja directa en la jornada 8»…")]
    string Reason,
    Guid FromMatchId,
    int FromMatchday);

// Las sanciones de una plantilla con las tarjetas de sus partidos de liga. En cada partido manda el acta oficial cerrada,
// que es la que usa el comité de competición; mientras no llega (puede tardar una semana), la interna. Un jugador que no
// sale en el acta oficial (el cruce por nombre no lo encuentra) se queda con lo de la interna.
internal sealed class SuspensionReader(IMatchStore matches, IPerformanceStore performances, IOfficialSheetStore sheets, TimeProvider clock)
{
    public async Task<SuspensionCalendar> LoadAsync(Squad squad, CancellationToken cancellationToken) =>
        await LoadAsync(squad, await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken), cancellationToken);

    public async Task<SuspensionCalendar> LoadAsync(Squad squad, IReadOnlyList<Match> season, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var past = season.Where(m => m.KickoffAt <= now && m.Status is not (MatchStatus.Cancelled or MatchStatus.Postponed)).ToList();
        if (past.Count == 0) return SuspensionCalendar.Empty;

        var internalLines = (await performances.GetBySquadAsync(squad.Id, cancellationToken)).ToLookup(p => p.MatchId);
        var official = (await sheets.GetAvailableLinesByMatchIdsAsync(past.Select(m => m.Id), cancellationToken))
            .Where(s => s.IsAvailable)
            .GroupBy(s => s.MatchId)
            .ToDictionary(g => g.Key, g => g.First());

        var cards = new Dictionary<Guid, IReadOnlyDictionary<Guid, MatchCards>>();
        foreach (var match in past)
        {
            var byPlayer = internalLines[match.Id]
                .GroupBy(p => p.SquadPlayerId)
                .ToDictionary(g => g.Key, g => new MatchCards(g.First().YellowCards, g.First().RedCard));
            if (official.TryGetValue(match.Id, out var sheet))
            {
                var side = match.IsHome(squad.TeamId) ? MatchSide.Home : MatchSide.Away;
                foreach (var (playerId, line) in SheetMatcher.Match(squad.Players, sheet.LinesOf(side)))
                    byPlayer[playerId] = new MatchCards(line.YellowCards, line.RedCard);
            }
            if (byPlayer.Count > 0) cards[match.Id] = byPlayer;
        }

        return SuspensionCalendar.Build(season, cards);
    }

    public static SuspensionBriefDto Brief(Suspension suspension) =>
        new(suspension.Cause, Reason(suspension), suspension.FromMatchId, suspension.FromMatchday);

    public static string Reason(Suspension suspension) => suspension.Cause switch
    {
        SuspensionCause.YellowCards => $"{suspension.Yellows}.ª amarilla en la jornada {suspension.FromMatchday}",
        SuspensionCause.SecondYellow => $"Expulsado por doble amarilla en la jornada {suspension.FromMatchday}",
        _ => $"Roja directa en la jornada {suspension.FromMatchday}",
    };

    // Para meterlo en mitad de una frase: «… sancionado para este partido (roja directa en la jornada 8)».
    public static string ReasonInSentence(Suspension suspension)
    {
        var text = Reason(suspension);
        return char.ToLowerInvariant(text[0]) + text[1..];
    }
}
