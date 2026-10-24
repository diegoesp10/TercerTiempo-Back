using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Privacy;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Privacy;

public sealed record TrackEventDto(
    AnalyticsEventType Type,
    [property: Description("Ruta vista (sin parámetros de consulta).")]
    string Path,
    string? RouteName,
    [property: Description("Búsqueda: lo escrito. Clic: el texto o la etiqueta de lo pulsado.")]
    string? Label,
    AnalyticsEntity? EntityType,
    Guid? EntityId,
    DateTimeOffset? OccurredAt);

public sealed record TrackEventsCommand(
    string VisitorId,
    string SessionId,
    IReadOnlyList<TrackEventDto> Events) : ICommand<TrackResultDto>;

public sealed record TrackResultDto(
    [property: Description("Eventos guardados: 0 si el visitante no ha aceptado la analítica con la versión vigente.")]
    int Stored);

internal sealed class TrackEventsValidator : AbstractValidator<TrackEventsCommand>
{
    public const int MaxEvents = 50;

    public TrackEventsValidator()
    {
        RuleFor(x => x.VisitorId).Must(v => Guid.TryParse(v, out _));
        RuleFor(x => x.SessionId).Must(v => Guid.TryParse(v, out _));
        RuleFor(x => x.Events).NotNull().Must(e => e.Count <= MaxEvents);
        RuleForEach(x => x.Events).ChildRules(e =>
        {
            e.RuleFor(x => x.Type).IsInEnum();
            e.RuleFor(x => x.EntityType).IsInEnum().When(x => x.EntityType.HasValue);
            e.RuleFor(x => x.Path).NotEmpty().MaximumLength(1000);
            e.RuleFor(x => x.Label).MaximumLength(1000);
        });
    }
}

internal sealed class TrackEventsHandler(
    ICookieStore cookies,
    IAnalyticsStore analytics,
    ICurrentUser currentUser,
    IClubStore clubs,
    ISquadStore squads,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<TrackEventsCommand, TrackResultDto>
{
    private static DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    public async Task<TrackResultDto> Handle(TrackEventsCommand command, CancellationToken cancellationToken)
    {
        if (command.Events.Count == 0) return new TrackResultDto(0);

        // Sin el consentimiento de analítica con la versión vigente no se guarda nada (el frontal ni lo envía).
        var policy = await CookieMapper.PolicyAsync(cookies, clock, cancellationToken);
        var consent = await cookies.GetLatestConsentAsync(command.VisitorId.ToLowerInvariant(), cancellationToken);
        if (consent is null || !consent.Analytics || consent.PolicyVersion != policy.Version)
            return new TrackResultDto(0);

        var now = clock.GetUtcNow();
        var (actor, actorClub) = await ActorAsync(cancellationToken);
        var subjectClubs = new Dictionary<(AnalyticsEntity, Guid), Guid?>();
        var events = new List<AnalyticsEvent>(command.Events.Count);

        foreach (var e in command.Events)
        {
            // La hora del navegador solo se acepta si es razonable; si no, la del servidor.
            var at = e.OccurredAt is { } t && t <= now && t > now.AddDays(-1) ? t : now;
            Guid? subject = null;
            if (e.EntityType is { } type && e.EntityId is { } id)
            {
                if (!subjectClubs.TryGetValue((type, id), out subject))
                    subjectClubs[(type, id)] = subject = await SubjectClubAsync(type, id, cancellationToken);
            }

            events.Add(AnalyticsEvent.Create(
                at, command.VisitorId, command.SessionId, e.Type, StripQuery(e.Path), e.RouteName, e.Label,
                e.EntityType, e.EntityId, actor, actorClub, subject));
        }

        analytics.AddRange(events);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (now - _lastPurge > TimeSpan.FromDays(1))
        {
            _lastPurge = now;
            await analytics.PurgeOlderThanAsync(now - AnalyticsEvent.Retention, cancellationToken);
        }

        return new TrackResultDto(events.Count);
    }

    private async Task<(AnalyticsActor, Guid?)> ActorAsync(CancellationToken cancellationToken)
    {
        switch (currentUser.AccountType)
        {
            case AccountType.Club:
                return (AnalyticsActor.Club, currentUser.ClubId);
            case AccountType.Player when currentUser.PlayerId is { } playerId:
                var squad = await squads.GetByPlayerAsync(playerId, cancellationToken);
                return (AnalyticsActor.Player, squad is null ? null : (await clubs.GetByTeamAsync(squad.TeamId, cancellationToken))?.Id);
            case AccountType.Coach when currentUser.CoachId is { } coachId:
                var first = (await squads.GetByCoachAsync(coachId, SquadAccess.Today(clock), cancellationToken)).FirstOrDefault();
                return (AnalyticsActor.Coach, first is null ? null : (await clubs.GetByTeamAsync(first.TeamId, cancellationToken))?.Id);
            default:
                return (AnalyticsActor.Anonymous, null);
        }
    }

    private async Task<Guid?> SubjectClubAsync(AnalyticsEntity type, Guid id, CancellationToken cancellationToken) => type switch
    {
        AnalyticsEntity.Club => id,
        AnalyticsEntity.Team => (await clubs.GetByTeamAsync(id, cancellationToken))?.Id,
        AnalyticsEntity.Squad => await squads.GetByIdAsync(id, cancellationToken) is { } squad
            ? (await clubs.GetByTeamAsync(squad.TeamId, cancellationToken))?.Id
            : null,
        _ => null,
    };

    private static string StripQuery(string path)
    {
        var cut = path.IndexOfAny(['?', '#']);
        return cut < 0 ? path : path[..cut];
    }
}

// --- Informe para la administración ---

public enum ClubScope
{
    // Lo que hacen los usuarios del club (técnicos, jugadores y la cuenta del club).
    Actor = 1,
    // Las visitas a las páginas de sus equipos y plantillas (de cualquiera).
    Subject = 2,
}

public sealed record AnalyticsFilter(DateTimeOffset From, DateTimeOffset To, Guid? ActorClubId, Guid? SubjectClubId);

public sealed record AnalyticsTotalsDto(int PageViews, int Visitors, int Sessions, int Searches, int Clicks);
public sealed record AnalyticsDayDto(DateOnly Day, int PageViews, int Visitors);
public sealed record AnalyticsCountDto(string Key, string Label, int Count, int Visitors);
public sealed record AnalyticsEntityCountDto(AnalyticsEntity Type, Guid Id, string Name, string? CrestUrl, int Count, int Visitors);
public sealed record AnalyticsClubCountDto(Guid ClubId, string Name, string? CrestUrl, int Count, int Visitors);
public sealed record ConsentStatsDto(int Total, int AcceptAll, int RejectAll, int Custom, int WithAnalytics);

public sealed record AnalyticsRaw(
    AnalyticsTotalsDto Totals,
    IReadOnlyList<AnalyticsDayDto> Days,
    IReadOnlyList<AnalyticsCountDto> Pages,
    IReadOnlyList<AnalyticsCountDto> Searches,
    IReadOnlyList<AnalyticsCountDto> Clicks,
    IReadOnlyList<(AnalyticsEntity Type, Guid Id, int Count, int Visitors)> Entities,
    IReadOnlyList<AnalyticsCountDto> Actors,
    IReadOnlyList<(Guid ClubId, int Count, int Visitors)> ActorClubs,
    IReadOnlyList<(Guid ClubId, int Count, int Visitors)> SubjectClubs,
    ConsentStatsDto Consents);

// Agregados sobre AnalyticsEvents y CookieConsents (se calculan en la base de datos).
public interface IAnalyticsReports
{
    Task<AnalyticsRaw> ReportAsync(AnalyticsFilter filter, int top, CancellationToken cancellationToken);
}

public sealed record AnalyticsReportDto(
    DateOnly From,
    DateOnly To,
    Guid? ClubId,
    ClubScope Scope,
    AnalyticsTotalsDto Totals,
    IReadOnlyList<AnalyticsDayDto> Days,
    IReadOnlyList<AnalyticsCountDto> Pages,
    IReadOnlyList<AnalyticsCountDto> Searches,
    IReadOnlyList<AnalyticsCountDto> Clicks,
    IReadOnlyList<AnalyticsEntityCountDto> Entities,
    IReadOnlyList<AnalyticsCountDto> Actors,
    IReadOnlyList<AnalyticsClubCountDto> ActiveClubs,
    IReadOnlyList<AnalyticsClubCountDto> ViewedClubs,
    ConsentStatsDto Consents,
    IReadOnlyList<AnalyticsClubCountDto> Clubs);

public sealed record GetAnalyticsReportQuery(DateOnly? From, DateOnly? To, Guid? ClubId, ClubScope Scope = ClubScope.Subject) : IQuery<AnalyticsReportDto>;

internal sealed class GetAnalyticsReportHandler(
    IAnalyticsReports reports,
    IClubStore clubs,
    ITeamStore teams,
    ICompetitionStore competitions,
    IMatchStore matches,
    ISquadStore squads,
    TimeProvider clock) : IQueryHandler<GetAnalyticsReportQuery, AnalyticsReportDto>
{
    private const int Top = 15;

    public async Task<AnalyticsReportDto> Handle(GetAnalyticsReportQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-29);
        if (from > to) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 400) from = to.AddDays(-400);

        var filter = new AnalyticsFilter(
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            query.Scope == ClubScope.Actor ? query.ClubId : null,
            query.Scope == ClubScope.Subject ? query.ClubId : null);

        var raw = await reports.ReportAsync(filter, Top, cancellationToken);

        var entities = new List<AnalyticsEntityCountDto>();
        foreach (var (type, id, count, visitors) in raw.Entities)
        {
            var (name, crest) = await DescribeAsync(type, id, cancellationToken);
            entities.Add(new AnalyticsEntityCountDto(type, id, name, crest, count, visitors));
        }

        return new AnalyticsReportDto(
            from, to, query.ClubId, query.Scope, raw.Totals, raw.Days, raw.Pages, raw.Searches, raw.Clicks, entities, raw.Actors,
            await ClubsAsync(raw.ActorClubs, cancellationToken),
            await ClubsAsync(raw.SubjectClubs, cancellationToken),
            raw.Consents,
            (await clubs.GetAllAsync(cancellationToken))
                .OrderBy(c => c.Name)
                .Select(c => new AnalyticsClubCountDto(c.Id, c.Name, null, 0, 0))
                .ToList());
    }

    private async Task<IReadOnlyList<AnalyticsClubCountDto>> ClubsAsync(IReadOnlyList<(Guid ClubId, int Count, int Visitors)> rows, CancellationToken cancellationToken)
    {
        var result = new List<AnalyticsClubCountDto>(rows.Count);
        foreach (var (clubId, count, visitors) in rows)
        {
            var club = await clubs.GetByIdAsync(clubId, cancellationToken);
            var crest = club?.Teams.FirstOrDefault() is { } first ? (await teams.GetByIdAsync(first.TeamId, cancellationToken))?.CrestLink() : null;
            result.Add(new AnalyticsClubCountDto(clubId, club?.Name ?? "Club eliminado", crest, count, visitors));
        }
        return result;
    }

    private async Task<(string Name, string? Crest)> DescribeAsync(AnalyticsEntity type, Guid id, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case AnalyticsEntity.Team:
                var team = await teams.GetByIdAsync(id, cancellationToken);
                return (team?.Name ?? "Equipo", team?.CrestLink());
            case AnalyticsEntity.Club:
                var club = await clubs.GetByIdAsync(id, cancellationToken);
                return (club?.Name ?? "Club", null);
            case AnalyticsEntity.Competition:
                return ((await competitions.GetByIdAsync(id, cancellationToken))?.Name ?? "Liga", null);
            case AnalyticsEntity.Squad:
                var squad = await squads.GetByIdAsync(id, cancellationToken);
                var squadTeam = squad is null ? null : await teams.GetByIdAsync(squad.TeamId, cancellationToken);
                return (squadTeam is null ? "Plantilla" : $"Plantilla de {squadTeam.Name}", squadTeam?.CrestLink());
            case AnalyticsEntity.Match:
                var match = await matches.GetByIdAsync(id, cancellationToken);
                if (match is null) return ("Partido", null);
                var pair = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);
                return ($"{pair.GetValueOrDefault(match.HomeTeamId)?.Name ?? "?"} - {pair.GetValueOrDefault(match.AwayTeamId)?.Name ?? "?"}",
                    pair.GetValueOrDefault(match.HomeTeamId)?.CrestLink());
            default:
                return ("—", null);
        }
    }
}
