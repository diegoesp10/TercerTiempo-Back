using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Squads.Suspensions;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.MatchSheet;

// El único camino por el que se escribe el acta interna: lo usan el PUT del acta, el de los datos de equipo y el
// volcado de la propuesta del vídeo. No guarda: quien lo llama cierra la unidad de trabajo.
internal sealed class MatchSheetWriter(IPerformanceStore performances, ITeamMatchStatsStore teamStats, OfficialSheetRatings officialRatings, SuspensionReader suspensions)
{
    // videoActions: el volcado del vídeo dice de qué jugadores son suyas las acciones (no cuentan en la nota hasta que el
    // cuerpo técnico guarde el acta); el resto de sus líneas quedan como estaban. Sin él (el PUT del acta), quien guarda
    // hace suyas todas las líneas.
    public async Task WriteLinesAsync(
        Squad squad, Match match, SquadEditor editor, IReadOnlyList<MatchSheetEntryInput> entries, DateTimeOffset now, CancellationToken cancellationToken,
        IReadOnlySet<Guid>? videoActions = null)
    {
        PerformancePolicy.EnsureCanRecord(squad, editor, match, Squad.Today(now));

        var existing = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
            .ToDictionary(p => p.SquadPlayerId);
        var written = new List<PlayerMatchPerformance>();
        SuspensionCalendar? sanctions = null;

        foreach (var input in entries)
        {
            var player = squad.GetPlayer(input.SquadPlayerId);
            existing.TryGetValue(player.Id, out var current);

            if (!input.Played)
            {
                if (current is not null)
                    performances.Remove(current);
                continue;
            }

            var stats = input.ToStats();

            if (current is null)
            {
                // Quien causó baja antes del partido ya no era del equipo: no puede salir en su acta.
                if (!player.WasMemberAt(match.KickoffAt))
                    throw new DomainException(ErrorCode.SquadPlayerLeftBefore, player.FullName, player.LeftOn?.ToString("dd/MM/yyyy") ?? "");

                // Un sancionado no pudo jugar ese partido: no se le abre línea. La que ya tuviera se conserva para no
                // bloquear el acta si la sanción se supo después (el acta oficial puede tardar una semana).
                sanctions ??= await suspensions.LoadAsync(squad, cancellationToken);
                if (sanctions.On(player.Id, match.Id) is { } suspension)
                    throw new DomainException(ErrorCode.SheetPlayerSuspended, player.FullName, SuspensionReader.ReasonInSentence(suspension));

                current = PlayerMatchPerformance.Record(squad, player, match, editor, stats, now);
                performances.Add(current);
            }
            else
            {
                current.Update(stats, player, match, editor, now);
            }

            current.SetShirtNumber(input.ShirtNumber);
            if (videoActions is null || videoActions.Contains(player.Id))
                current.MarkActionsFromVideo(videoActions is not null, player, match);
            written.Add(current);
        }

        await officialRatings.SyncAsync(squad, match, written, cancellationToken);
    }

    public async Task<TeamMatchStats> WriteTeamStatsAsync(
        Squad squad, Match match, SquadEditor editor, TeamMatchStatsData data, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await teamStats.GetAsync(squad.Id, match.Id, cancellationToken);
        if (current is null)
        {
            current = TeamMatchStats.Record(squad, match, editor, data, now);
            teamStats.Add(current);
        }
        else
        {
            current.Update(squad, match, editor, data, now);
        }

        return current;
    }
}
