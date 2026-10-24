using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sync;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Admin;

public sealed record SyncCompetitionDto(Guid Id, string Name, string Category, int Tier, string? Group);

public sealed record FootballSyncSettingsDto(
    [property: Description("Qué hace la pasada automática: CurrentRound (jornada en curso) o FullSeason (temporada entera).")]
    SyncScopeKind ScheduledScope,
    int DaysBack,
    int DaysAhead,
    bool RostersInRound,
    [property: Description("Cada cuántos días la pasada automática es la temporada entera con plantillas (0 = solo a mano).")]
    int FullSyncEveryDays,
    [property: Description("Jornada en curso de hoy con estos ajustes.")]
    DateOnly RoundFrom,
    DateOnly RoundTo,
    DateTimeOffset? LastFullSyncAt,
    DateTimeOffset? NextFullSyncAt,
    [property: Description("Ligas sincronizadas, para elegir cuáles lanzar a mano.")]
    IReadOnlyList<SyncCompetitionDto> Competitions,
    DateTimeOffset UpdatedAt);

internal sealed class FootballSyncSettingsReader(
    IFootballSyncSettingsStore store,
    ICompetitionStore competitions,
    IJobsMonitor monitor,
    TimeProvider clock)
{
    public async Task<FootballSyncSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await store.GetAsync(cancellationToken) ?? FootballSyncSettings.CreateDefault(clock.GetUtcNow());

    public async Task<FootballSyncSettingsDto> ToDtoAsync(FootballSyncSettings settings, CancellationToken cancellationToken)
    {
        var (from, to) = settings.RoundWindow(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var lastFull = await monitor.LastFullFootballSyncAsync(cancellationToken);
        var leagues = (await competitions.GetAllAsync(cancellationToken))
            .Where(c => c.ExternalRef is not null)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new SyncCompetitionDto(c.Id, c.Name, c.Category, c.Tier, c.Group))
            .ToList();

        return new FootballSyncSettingsDto(
            settings.ScheduledScope, settings.DaysBack, settings.DaysAhead, settings.RostersInRound, settings.FullSyncEveryDays,
            from, to, lastFull, FootballSyncPlanner.NextFullSyncAt(settings, lastFull), leagues, settings.UpdatedAt);
    }
}

public sealed record GetFootballSyncSettingsQuery : IQuery<FootballSyncSettingsDto>;

internal sealed class GetFootballSyncSettingsHandler(FootballSyncSettingsReader reader)
    : IQueryHandler<GetFootballSyncSettingsQuery, FootballSyncSettingsDto>
{
    public async Task<FootballSyncSettingsDto> Handle(GetFootballSyncSettingsQuery query, CancellationToken cancellationToken) =>
        await reader.ToDtoAsync(await reader.SettingsAsync(cancellationToken), cancellationToken);
}

public sealed record UpdateFootballSyncSettingsCommand(
    SyncScopeKind ScheduledScope,
    [property: Description("Días antes de hoy que entran en la jornada en curso (0-14).")]
    int DaysBack,
    [property: Description("Días después de hoy que entran en la jornada en curso (0-14).")]
    int DaysAhead,
    bool RostersInRound,
    int FullSyncEveryDays) : ICommand<FootballSyncSettingsDto>;

internal sealed class UpdateFootballSyncSettingsValidator : AbstractValidator<UpdateFootballSyncSettingsCommand>
{
    public UpdateFootballSyncSettingsValidator()
    {
        RuleFor(x => x.ScheduledScope).IsInEnum().WithName("Pasada automática");
        RuleFor(x => x.DaysBack).InclusiveBetween(0, FootballSyncSettings.MaxRoundDays).WithName("Días antes de hoy");
        RuleFor(x => x.DaysAhead).InclusiveBetween(0, FootballSyncSettings.MaxRoundDays).WithName("Días después de hoy");
        RuleFor(x => x.FullSyncEveryDays).InclusiveBetween(0, FootballSyncSettings.MaxFullSyncEveryDays).WithName("Días entre pasadas completas");
    }
}

internal sealed class UpdateFootballSyncSettingsHandler(
    IFootballSyncSettingsStore store,
    FootballSyncSettingsReader reader,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateFootballSyncSettingsCommand, FootballSyncSettingsDto>
{
    public async Task<FootballSyncSettingsDto> Handle(UpdateFootballSyncSettingsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var settings = await store.GetForUpdateAsync(cancellationToken);
        if (settings is null)
        {
            settings = FootballSyncSettings.CreateDefault(now);
            store.Add(settings);
        }

        settings.Update(command.ScheduledScope, command.DaysBack, command.DaysAhead, command.RostersInRound, command.FullSyncEveryDays, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await reader.ToDtoAsync(settings, cancellationToken);
    }
}

public sealed record RunFootballSyncCommand(
    [property: Description("CurrentRound (jornada en curso según los ajustes), DateRange (de From a To) o FullSeason.")]
    SyncScopeKind Scope,
    DateOnly? From,
    DateOnly? To,
    [property: Description("Ligas a sincronizar (ids de competición); vacío o null = todas.")]
    IReadOnlyList<Guid>? CompetitionIds,
    [property: Description("Descargar también las plantillas (catálogo entero de jugadores). Por defecto: sí en la temporada completa; en la jornada, lo de los ajustes; en fechas, no.")]
    bool? IncludeRosters) : ICommand<JobDto>;

internal sealed class RunFootballSyncValidator : AbstractValidator<RunFootballSyncCommand>
{
    public const int MaxRangeDays = 366;

    public RunFootballSyncValidator()
    {
        RuleFor(x => x.Scope).IsInEnum().WithName("Alcance");
        RuleFor(x => x.CompetitionIds).Must(ids => ids is null || ids.Count <= 100).WithName("Ligas");

        When(x => x.Scope == SyncScopeKind.DateRange, () =>
        {
            RuleFor(x => x.From).NotNull().WithName("Desde");
            RuleFor(x => x.To).NotNull().WithName("Hasta");
            RuleFor(x => x.To)
                .Must((command, to) => command.From is null || to is null || to >= command.From)
                .WithMessage("'Hasta' no puede ir antes de 'Desde'.");
            RuleFor(x => x.To)
                .Must((command, to) => command.From is null || to is null || to.Value.DayNumber - command.From.Value.DayNumber < MaxRangeDays)
                .WithMessage($"Como mucho {MaxRangeDays} días: para más, la temporada completa.");
        });
    }
}

internal sealed class RunFootballSyncHandler(
    FootballSyncSettingsReader reader,
    ICompetitionStore competitions,
    IJobsMonitor monitor,
    TimeProvider clock) : ICommandHandler<RunFootballSyncCommand, JobDto>
{
    public async Task<JobDto> Handle(RunFootballSyncCommand command, CancellationToken cancellationToken)
    {
        var ids = (command.CompetitionIds ?? []).Distinct().ToList();
        if (ids.Count > 0)
        {
            var known = (await competitions.GetAllAsync(cancellationToken)).Where(c => c.ExternalRef is not null).Select(c => c.Id).ToHashSet();
            if (ids.FirstOrDefault(id => !known.Contains(id)) is var missing && missing != Guid.Empty)
                throw new NotFoundException(ErrorCode.CompetitionNotFound, missing);
        }

        var scope = command.Scope switch
        {
            SyncScopeKind.CurrentRound => SyncScope.Round(
                await reader.SettingsAsync(cancellationToken), DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ids, command.IncludeRosters),
            SyncScopeKind.DateRange => new SyncScope(SyncScopeKind.DateRange, command.From, command.To, ids, command.IncludeRosters ?? false),
            _ => SyncScope.FullSeason(ids, command.IncludeRosters ?? true),
        };

        return await JobLauncher.LaunchAsync(monitor, JobNames.FootballData, scope, cancellationToken);
    }
}
