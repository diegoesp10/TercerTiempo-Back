using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record InjuryOccasionDto(
    [property: Description("Match: un partido de liga del equipo; Training: un entrenamiento de la plantilla.")]
    InjuryOccasionKind Kind,
    [property: Description("Id del partido o de la sesión de entrenamiento.")]
    Guid Id,
    [property: Description("Día del partido o del entrenamiento (hora de España).")]
    DateOnly Day,
    [property: Description("«J11 · Local – Visitante» o el título del entrenamiento.")]
    string Label,
    [property: Description("Hora del saque o del comienzo; solo en la lista para elegir.")]
    DateTimeOffset? At = null);

public sealed record ListInjuryOccasionsQuery(Guid SquadId) : IQuery<IReadOnlyList<InjuryOccasionDto>>;

// Dónde se pudo lesionar un jugador: los partidos de liga del equipo en la temporada de la plantilla y sus entrenamientos,
// ya empezados (en uno que aún no se ha jugado no se lesiona nadie).
internal static class InjuryOccasions
{
    public static InjuryOccasionDto? ToDto(InjuryOccasion? occasion) =>
        occasion is null ? null : new InjuryOccasionDto(occasion.Kind, occasion.Id, occasion.Day, occasion.Label);

    public static InjuryOccasion Of(Match match, IReadOnlyDictionary<Guid, string> names) =>
        new(InjuryOccasionKind.Match, match.Id, CallUps.DayOf(match.KickoffAt),
            $"J{match.Matchday} · {names.GetValueOrDefault(match.HomeTeamId, "?")} – {names.GetValueOrDefault(match.AwayTeamId, "?")}");

    public static InjuryOccasion Of(TrainingSession session) =>
        new(InjuryOccasionKind.Training, session.Id, TrainingMapper.DayOf(session), session.Title);

    public static bool Happened(Match match, DateTimeOffset now) =>
        match.KickoffAt <= now && match.Status is not (MatchStatus.Postponed or MatchStatus.Cancelled);

    // Al corregir una lesión se puede dejar el sitio que ya tenía aunque ya no salga en la lista (la que viene de la
    // temporada anterior).
    public static async Task<InjuryOccasion?> ResolveAsync(
        Squad squad, Guid? matchId, Guid? trainingSessionId, InjuryOccasion? current, IMatchStore matches, ITeamStore teams, ITrainingStore trainings,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        matchId = matchId == Guid.Empty ? null : matchId;
        trainingSessionId = trainingSessionId == Guid.Empty ? null : trainingSessionId;
        if (matchId is not null && trainingSessionId is not null)
            throw new DomainException(ErrorCode.InjuryOccasionInvalid);
        if ((matchId ?? trainingSessionId) is not { } id)
            return null;
        if (current is not null && current.Id == id)
            return current;

        if (matchId is not null)
        {
            var match = await matches.GetByIdAsync(id, cancellationToken);
            if (match is null || match.SeasonId != squad.SeasonId || (match.HomeTeamId != squad.TeamId && match.AwayTeamId != squad.TeamId) || !Happened(match, now))
                throw new DomainException(ErrorCode.InjuryOccasionNotFound);
            return Of(match, await NamesAsync(teams, [match], cancellationToken));
        }

        var session = await trainings.GetByIdAsync(id, cancellationToken);
        if (session is null || session.SquadId != squad.Id || !session.HasStarted(now))
            throw new DomainException(ErrorCode.InjuryOccasionNotFound);
        return Of(session);
    }

    // Partidos y entrenamientos juntos, los más recientes primero.
    public static async Task<IReadOnlyList<InjuryOccasionDto>> ListAsync(
        Squad squad, IMatchStore matches, ITeamStore teams, ITrainingStore trainings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var played = (await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken)).Where(m => Happened(m, now)).ToList();
        var names = await NamesAsync(teams, played, cancellationToken);
        var sessions = (await trainings.GetBySquadAsync(squad.Id, DateTimeOffset.UnixEpoch, now, cancellationToken)).Where(s => s.HasStarted(now));
        return played.Select(m => ToDto(Of(m, names))! with { At = m.KickoffAt })
            .Concat(sessions.Select(s => ToDto(Of(s))! with { At = s.StartsAt }))
            .OrderByDescending(o => o.At)
            .ToList();
    }

    // La lesión dice que fue en un partido: la posible lesión de su crónica para ese jugador queda registrada con ella (y su
    // aviso, resuelto). Si la que se registró con ella dice ahora que fue en otro sitio, aquella queda descartada.
    public static async Task SyncProposalsAsync(
        Squad squad, PlayerInjury injury, SquadEditor editor, IInjuryProposalStore proposals, IClubStore clubs, IAlertStore alerts, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var proposal in await proposals.GetBySquadAsync(squad.Id, cancellationToken))
        {
            if (proposal.SquadPlayerId != injury.SquadPlayerId) continue;
            if (proposal.IsPending && proposal.MatchId == injury.MatchId)
            {
                proposal.Register(injury.Id, editor, now);
                await StaffAlerts.ResolveAsync(squad, clubs, alerts, InjuryProposalAccess.KeyFor(proposal.Id), clock, cancellationToken);
            }
            else if (proposal.InjuryId == injury.Id && proposal.MatchId != injury.MatchId)
            {
                proposal.ForgetInjury(now);
            }
        }
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(ITeamStore teams, IReadOnlyCollection<Match> list, CancellationToken cancellationToken) =>
        list.Count == 0
            ? new Dictionary<Guid, string>()
            : (await teams.GetByIdsAsync(list.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
}

internal sealed class ListInjuryOccasionsHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IMatchStore matches, ITeamStore teams, ITrainingStore trainings, TimeProvider clock)
    : IQueryHandler<ListInjuryOccasionsQuery, IReadOnlyList<InjuryOccasionDto>>
{
    public async Task<IReadOnlyList<InjuryOccasionDto>> Handle(ListInjuryOccasionsQuery query, CancellationToken cancellationToken)
    {
        var squad = await InjuryProposalAccess.SquadForManagerOrAdminAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        return await InjuryOccasions.ListAsync(squad, matches, teams, trainings, clock.GetUtcNow(), cancellationToken);
    }
}
