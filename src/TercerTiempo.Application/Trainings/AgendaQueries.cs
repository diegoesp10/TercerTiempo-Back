using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Squads.Suspensions;
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
    CallUpSummaryDto? CallUp,
    [property: Description("Solo en Match y mientras no se ha jugado: quién no puede jugar (sancionados y lesionados), haya convocatoria o no.")]
    IReadOnlyList<UnavailablePlayerDto>? Unavailable = null);

public enum UnavailabilityKind
{
    Suspended = 1,
    Injured = 2,
}

public sealed record UnavailablePlayerDto(
    Guid SquadPlayerId,
    string PlayerName,
    UnavailabilityKind Kind,
    [property: Description("Sancionado: el porqué («5.ª amarilla en la jornada 7»…). Lesionado: «Lesionado» y, para quien puede verla, la lesión.")]
    string Reason);

public sealed record GetSquadAgendaQuery(Guid SquadId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<SquadAgendaItemDto>>;

internal sealed class GetSquadAgendaHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, IMatchStore matches, ITeamStore teams, ICallUpStore callUps,
    IInjuryStore injuries, SuspensionReader suspensions, TimeProvider clock)
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

        var injuryView = await InjuryView.LoadAsync(squad, currentUser, clock, clubs, injuries, cancellationToken);
        var items = (await trainings.GetBySquadAsync(squad.Id, from, to, cancellationToken))
            .Select(s => new SquadAgendaItemDto(SquadEventKind.Training, s.StartsAt, s.IsDone(now), TrainingMapper.ToSummary(s, squad, viewer, now, injuryView), null, null))
            .ToList();

        // Los partidos cancelados no se pintan; los aplazados sí, en la fecha que tengan.
        var season = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);
        var inRange = season
            .Where(m => m.KickoffAt >= from && m.KickoffAt < to && m.Status != MatchStatus.Cancelled)
            .ToList();
        if (inRange.Count > 0)
        {
            var teamMap = (await teams.GetByIdsAsync(inRange.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken)).ToDictionary(t => t.Id);
            var manager = await TrainingAccess.CanEditAsync(squad, currentUser, clock, clubs, cancellationToken);
            // Solo las convocatorias de los partidos del intervalo, filtradas en SQL.
            var visible = await callUps.GetBySquadAndMatchesAsync(squad.Id, inRange.Select(m => m.Id), publishedOnly: !manager, cancellationToken);
            var byMatch = CallUps.Summaries(visible, squad, viewer).ToDictionary(c => c.MatchId);
            // Quién no puede jugar solo se calcula si hay algún partido por jugar en el intervalo.
            var sanctions = inRange.Any(m => !IsPlayed(m, now)) ? await suspensions.LoadAsync(squad, season, cancellationToken) : SuspensionCalendar.Empty;
            items.AddRange(inRange.Select(m =>
            {
                var done = IsPlayed(m, now);
                return new SquadAgendaItemDto(SquadEventKind.Match, m.KickoffAt, done, null, MatchMapper.ToDto(m, teamMap), byMatch.GetValueOrDefault(m.Id),
                    done ? null : Unavailable(squad, m, sanctions, injuryView));
            }));
        }

        return items.OrderBy(i => i.StartsAt).ThenBy(i => i.Kind).ToList();
    }

    // Primero los sancionados y luego los lesionados; quien está las dos cosas sale como sancionado.
    private static IReadOnlyList<UnavailablePlayerDto> Unavailable(Squad squad, Match match, SuspensionCalendar suspensions, InjuryView injuries)
    {
        var day = CallUps.DayOf(match.KickoffAt);
        var list = new List<UnavailablePlayerDto>();
        foreach (var player in squad.ActivePlayers)
        {
            if (suspensions.On(player.Id, match.Id) is { } suspension)
                list.Add(new(player.Id, player.FullName, UnavailabilityKind.Suspended, SuspensionReader.Reason(suspension)));
            else if (injuries.On(player.Id, day) is { } injury)
                list.Add(new(player.Id, player.FullName, UnavailabilityKind.Injured,
                    injuries.BriefFor(player.Id, injury) is { } brief ? $"Lesionado: {brief.TypeLabel.ToLowerInvariant()}" : "Lesionado"));
        }
        return list.OrderBy(u => u.Kind).ThenBy(u => u.PlayerName).ToList();
    }

    private static bool IsPlayed(Match match, DateTimeOffset now) => match.Status switch
    {
        MatchStatus.Played => true,
        MatchStatus.Scheduled or MatchStatus.InProgress => match.KickoffAt + MatchLength <= now,
        _ => false,
    };
}
