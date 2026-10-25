using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Admin;

public sealed record LicensePlanSettingsDto(
    LicensePlan Plan,
    int MonthlyPriceCents,
    int YearlyPriceCents,
    int MaxStaff,
    string? StripeMonthlyPriceId,
    string? StripeYearlyPriceId);

public sealed record LicensingSettingsDto(
    string Currency,
    int TrialDays,
    IReadOnlyList<LicensePlanSettingsDto> Plans,
    [property: Description("Pasarela en uso (configuración del entorno, no editable aquí): «mock» o «stripe».")]
    string Provider,
    DateTimeOffset UpdatedAt);

internal static class LicensingSettingsMapper
{
    public static LicensingSettingsDto ToDto(LicensingSettings settings, string provider) => new(
        settings.Currency,
        settings.TrialDays,
        settings.Plans.OrderBy(p => p.Plan)
            .Select(p => new LicensePlanSettingsDto(p.Plan, p.MonthlyPriceCents, p.YearlyPriceCents, p.MaxStaff, p.StripeMonthlyPriceId, p.StripeYearlyPriceId))
            .ToList(),
        provider,
        settings.UpdatedAt);
}

public sealed record GetLicensingSettingsQuery : IQuery<LicensingSettingsDto>;

internal sealed class GetLicensingSettingsHandler(ILicensingCatalog catalog, IOptions<LicensingOptions> options)
    : IQueryHandler<GetLicensingSettingsQuery, LicensingSettingsDto>
{
    public async Task<LicensingSettingsDto> Handle(GetLicensingSettingsQuery query, CancellationToken cancellationToken) =>
        LicensingSettingsMapper.ToDto(await catalog.GetAsync(cancellationToken), options.Value.Provider);
}

public sealed record UpdateLicensingSettingsCommand(
    [property: Description("Código ISO de la moneda (EUR).")]
    string Currency,
    [property: Description("Días de prueba de las licencias nuevas (0-365).")]
    int TrialDays,
    [property: Description("Los dos planes, Basic y Premium, con precios en céntimos (IVA incluido) y técnicos admitidos.")]
    IReadOnlyList<LicensePlanSettingsDto> Plans) : ICommand<LicensingSettingsDto>;

internal sealed class UpdateLicensingSettingsValidator : AbstractValidator<UpdateLicensingSettingsCommand>
{
    public UpdateLicensingSettingsValidator()
    {
        RuleFor(x => x.Currency).NotEmpty().Length(3).WithName("Moneda");
        RuleFor(x => x.TrialDays).InclusiveBetween(0, 365).WithName("Días de prueba");
        RuleFor(x => x.Plans).NotEmpty();
        RuleForEach(x => x.Plans).ChildRules(plan =>
        {
            plan.RuleFor(p => p.Plan).IsInEnum();
            plan.RuleFor(p => p.MonthlyPriceCents).InclusiveBetween(50, 1_000_000).WithName("Precio mensual (céntimos)");
            plan.RuleFor(p => p.YearlyPriceCents).InclusiveBetween(50, 10_000_000).WithName("Precio anual (céntimos)");
            plan.RuleFor(p => p.MaxStaff).InclusiveBetween(1, 50).WithName("Técnicos");
            plan.RuleFor(p => p.StripeMonthlyPriceId).MaximumLength(100);
            plan.RuleFor(p => p.StripeYearlyPriceId).MaximumLength(100);
        });
    }
}

internal sealed class UpdateLicensingSettingsHandler(
    ILicensingSettingsStore store,
    ILicensingCatalog catalog,
    IOptions<LicensingOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateLicensingSettingsCommand, LicensingSettingsDto>
{
    public async Task<LicensingSettingsDto> Handle(UpdateLicensingSettingsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var settings = await store.GetForUpdateAsync(cancellationToken);
        if (settings is null)
        {
            settings = LicensingSettings.CreateDefault(now);
            store.Add(settings);
        }

        settings.Update(
            command.Currency,
            command.TrialDays,
            command.Plans.Select(p => new LicensePlanTerms(p.Plan, p.MonthlyPriceCents, p.YearlyPriceCents, p.MaxStaff, p.StripeMonthlyPriceId, p.StripeYearlyPriceId)).ToList(),
            now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        catalog.Invalidate();

        return LicensingSettingsMapper.ToDto(settings, options.Value.Provider);
    }
}
