using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

public sealed record SquadAgendaItemDto(
    [property: Description("Training: citación a un entrenamiento (toda la plantilla). Match: partido, con su convocatoria.")]
    SquadEventKind Kind,
    DateTimeOffset StartsAt,
    [property: Description("Ya realizado: el entrenamiento ha terminado o el partido ya se ha jugado.")]
    bool Done,
    [property: Description("Solo en Training.")]
    TrainingSummaryDto? Training,
    [property: Description("Solo en Match.")]
    MatchDto? Match,
    [property: Description("Solo en Match: estado de la convocatoria; null si no hay o si es un borrador que quien mira no puede ver.")]
    CallUpSummaryDto? CallUp);

public sealed record GetSquadAgendaQuery(Guid SquadId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<SquadAgendaItemDto>>;

internal sealed class GetSquadAgendaHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    TimeProvider clock)
    : IQueryHandler<GetSquadAgendaQuery, IReadOnlyList<SquadAgendaItemDto>>
{
    // Lo mismo que da el frontal por jugado a un partido sin resultado: tres horas desde el saque.
    private static readonly TimeSpan MatchLength = TimeSpan.FromHours(3);

    public async Task<IReadOnlyList<SquadAgendaItemDto>> Handle(GetSquadAgendaQuery query, CancellationToken cancellationToken)
    {
        TrainingClock.EnsureRange(query.From, query.To);
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var zone = TrainingClock.Zone;
        var fromDate = query.From.ToDateTime(TimeOnly.MinValue);
        var from = new DateTimeOffset(fromDate, zone.GetUtcOffset(fromDate));
        var toDate = query.To.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var to = new DateTimeOffset(toDate, zone.GetUtcOffset(toDate));
        var now = clock.GetUtcNow();
        var viewer = TrainingAccess.ViewerEntry(squad, currentUser);

        var items = (await trainings.GetBySquadAsync(squad.Id, from, to, cancellationToken))
            .Select(s => new SquadAgendaItemDto(SquadEventKind.Training, s.StartsAt, s.IsDone(now), TrainingMapper.ToSummary(s, squad, viewer, now), null, null))
            .ToList();

        // Los partidos cancelados no se pintan; los aplazados sí, en la fecha que tengan.
        var inRange = (await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken))
            .Where(m => m.KickoffAt >= from && m.KickoffAt < to && m.Status != MatchStatus.Cancelled)
            .ToList();
        if (inRange.Count > 0)
        {
            var teamMap = (await teams.GetByIdsAsync(inRange.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken)).ToDictionary(t => t.Id);
            var manager = await TrainingAccess.CanEditAsync(squad, currentUser, clock, clubs, cancellationToken);
            // Solo las convocatorias de los partidos del intervalo, filtradas en SQL.
            var visible = await callUps.GetBySquadAndMatchesAsync(squad.Id, inRange.Select(m => m.Id), publishedOnly: !manager, cancellationToken);
            var byMatch = CallUps.Summaries(visible, squad, viewer).ToDictionary(c => c.MatchId);
            items.AddRange(inRange.Select(m => new SquadAgendaItemDto(
                SquadEventKind.Match, m.KickoffAt, IsPlayed(m, now), null, MatchMapper.ToDto(m, teamMap), byMatch.GetValueOrDefault(m.Id))));
        }

        return items.OrderBy(i => i.StartsAt).ThenBy(i => i.Kind).ToList();
    }

    private static bool IsPlayed(Match match, DateTimeOffset now) => match.Status switch
    {
        MatchStatus.Played => true,
        MatchStatus.Scheduled or MatchStatus.InProgress => match.KickoffAt + MatchLength <= now,
        _ => false,
    };
}
