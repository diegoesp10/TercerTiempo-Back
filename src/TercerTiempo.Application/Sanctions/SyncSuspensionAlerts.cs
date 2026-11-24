using System.Globalization;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Squads.Suspensions;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Sanctions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Sanctions;

public sealed record SyncSuspensionAlertsCommand : ICommand<SuspensionAlertsSynced>;

public sealed record SuspensionAlertsSynced(int Squads, int Raised, int Resolved);

// Avisos de lo que pone el comité de competición: al jugador («Te han caído 2 partidos de suspensión»), a su cuerpo técnico
// y a su club, con los partidos que se pierde. Solo de reuniones de los últimos RecentDays días (lo anterior ya se sabía)
// y una vez por sanción: si el comité cambia los partidos (un recurso) llega un aviso nuevo y el anterior se resuelve, y
// se resuelven también cuando la sanción ya se ha cumplido. No llama a ninguna web: lee lo copiado de los datos reales.
internal sealed class SyncSuspensionAlertsHandler(
    ICommitteeSanctionStore committee,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IClubStore clubs,
    IAlertStore alerts,
    SuspensionReader suspensions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncSuspensionAlertsCommand, SuspensionAlertsSynced>
{
    public const string KeyPrefix = "suspension:";
    internal const int RecentDays = 14;
    // Se siguen mirando las temporadas con reuniones en este plazo, para resolver los avisos de sanciones largas.
    private const int FollowDays = 120;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public async Task<SuspensionAlertsSynced> Handle(SyncSuspensionAlertsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = CallUps.DayOf(now);
        var existing = (await alerts.GetAllByKeyPrefixAsync(KeyPrefix, cancellationToken)).ToLookup(a => a.Key);
        var current = new HashSet<string>();
        int squadCount = 0, raised = 0, resolved = 0;

        foreach (var seasonId in await committee.GetSeasonsWithMeetingsSinceAsync(today.AddDays(-FollowDays), cancellationToken))
        {
            foreach (var squad in await squads.GetBySeasonAsync(seasonId, cancellationToken))
            {
                squadCount++;
                var season = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);
                var calendar = await suspensions.LoadAsync(squad, season, cancellationToken);
                var byId = season.ToDictionary(m => m.Id);
                var names = (await teams.GetByIdsAsync(season.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken))
                    .ToDictionary(t => t.Id, t => t.Name);

                foreach (var (matchId, decision) in calendar.Decisions)
                {
                    if (!byId.TryGetValue(matchId, out var from)) continue;
                    foreach (var (playerId, penalties) in decision.ByPlayer)
                    {
                        // A quien ya no está en la plantilla (de baja) no se le avisa.
                        if (squad.ActivePlayers.FirstOrDefault(p => p.Id == playerId) is not { } player) continue;
                        var total = penalties.Sum(p => p.Matches);
                        var key = $"{KeyPrefix}{playerId:N}:{matchId:N}:{total}";
                        var served = calendar.All
                            .Where(s => s.Cause == SuspensionCause.Committee && s.SquadPlayerId == playerId && s.FromMatchId == matchId && byId.ContainsKey(s.MatchId))
                            .Select(s => byId[s.MatchId])
                            .OrderBy(m => m.KickoffAt)
                            .ToList();
                        var upcoming = served.Where(m => m.KickoffAt > now).ToList();
                        // Cumplida: ya no hace falta el aviso (y si aún no se había avisado, no se avisa).
                        if (served.Count > 0 && upcoming.Count == 0) continue;
                        current.Add(key);

                        if (existing.Contains(key) || penalties.Max(p => p.DecidedOn) < today.AddDays(-RecentDays)) continue;
                        await RaiseAsync(squad, player, from, total, penalties, upcoming, names, key, now, cancellationToken);
                        raised++;
                    }
                }
            }
        }

        // Los abiertos que ya no corresponden a ninguna sanción pendiente: cumplida o cambiada por el comité.
        foreach (var alert in existing.SelectMany(g => g).Where(a => !a.IsResolved && !current.Contains(a.Key)))
        {
            alert.Resolve(now);
            resolved++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SuspensionAlertsSynced(squadCount, raised, resolved);
    }

    private async Task RaiseAsync(
        Squad squad, SquadPlayer player, Match from, int total, IReadOnlyList<CommitteePenalty> penalties, IReadOnlyList<Match> upcoming,
        IReadOnlyDictionary<Guid, string> names, string key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var matchesText = total == 1 ? "1 partido" : $"{total} partidos";
        var fixture = $"{names.GetValueOrDefault(from.HomeTeamId, "?")} - {names.GetValueOrDefault(from.AwayTeamId, "?")}";
        var why = penalties.Count == 1
            ? Lower(penalties[0].Reason)
            : string.Join(", ", penalties.Select(p => $"{Lower(p.Reason)} ({(p.Matches == 1 ? "1 partido" : $"{p.Matches} partidos")})"));
        // Sin día de vencimiento: la campanita lo leería como «Acaba hoy» el día del primer partido que se pierde, y la
        // sanción puede ser de varios. Los partidos ya van en el mensaje.
        DateOnly? dueOn = null;

        if (player.PlayerId is { } account)
        {
            alerts.Add(Alert.ForPlayer(account, AlertKind.LeagueSuspension, key,
                total == 1 ? "Te ha caído 1 partido de suspensión" : $"Te han caído {total} partidos de suspensión",
                $"El comité de competición te ha sancionado por la jornada {from.Matchday} ({fixture}): {why}.{Missing(upcoming, squad.TeamId, names, "No puedes jugar")}",
                "/mis-convocatorias", dueOn, now));
        }

        await StaffAlerts.RaiseAsync(squad, clubs, alerts, AlertKind.LeagueSuspension, key,
            $"{player.FullName}: {matchesText} de suspensión",
            $"El comité de competición le ha sancionado por la jornada {from.Matchday} ({fixture}): {why}.{Missing(upcoming, squad.TeamId, names, "No puede jugar")} No se le puede convocar ni poner en el acta de esos partidos.",
            $"/plantillas/{squad.Id}/agenda", dueOn, clock, cancellationToken);
    }

    // Los partidos que se pierde: « No puedes jugar contra Real Titánico (28/11) ni contra C. Hispano (05/12).»
    private static string Missing(IReadOnlyList<Match> upcoming, Guid teamId, IReadOnlyDictionary<Guid, string> names, string lead)
    {
        if (upcoming.Count == 0) return string.Empty;
        var games = upcoming.Select(m =>
        {
            var rival = names.GetValueOrDefault(m.IsHome(teamId) ? m.AwayTeamId : m.HomeTeamId, "?");
            var day = TimeZoneInfo.ConvertTime(m.KickoffAt, TrainingClock.Zone).ToString("dd/MM", Spanish);
            return $"contra {rival} ({day})";
        }).ToList();
        var list = games.Count == 1 ? games[0] : $"{string.Join(", ", games.Take(games.Count - 1))} ni {games[^1]}";
        return $" {lead} {list}.";
    }

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
