using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Admin;

public sealed record LicensePriceSettingsDto(
    [property: Description("Club (la licencia de club) o StaffSeat (cada licencia de técnico).")]
    LicenseItem Item,
    int MonthlyPriceCents,
    int YearlyPriceCents,
    string? StripeMonthlyPriceId,
    string? StripeYearlyPriceId);

public sealed record LicensingSettingsDto(
    string Currency,
    int TrialDays,
    [property: Description("Técnicos que admite la prueba.")]
    int TrialStaffSeats,
    [property: Description("Licencias de técnico que puede tener un club como mucho.")]
    int MaxStaffSeats,
    IReadOnlyList<LicensePriceSettingsDto> Prices,
    [property: Description("Pasarela en uso (configuración del entorno, no editable aquí): «mock» o «stripe».")]
    string Provider,
    DateTimeOffset UpdatedAt);

internal static class LicensingSettingsMapper
{
    public static LicensingSettingsDto ToDto(LicensingSettings settings, string provider) => new(
        settings.Currency,
        settings.TrialDays,
        settings.TrialStaffSeats,
        settings.MaxStaffSeats,
        settings.Prices.OrderBy(p => p.Item)
            .Select(p => new LicensePriceSettingsDto(p.Item, p.MonthlyPriceCents, p.YearlyPriceCents, p.StripeMonthlyPriceId, p.StripeYearlyPriceId))
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
    [property: Description("Técnicos que admite la prueba (0 hasta el tope por club).")]
    int TrialStaffSeats,
    [property: Description("Licencias de técnico que puede tener un club como mucho (1-200).")]
    int MaxStaffSeats,
    [property: Description("Los dos precios, Club y StaffSeat, en céntimos (IVA incluido).")]
    IReadOnlyList<LicensePriceSettingsDto> Prices) : ICommand<LicensingSettingsDto>;

internal sealed class UpdateLicensingSettingsValidator : AbstractValidator<UpdateLicensingSettingsCommand>
{
    public UpdateLicensingSettingsValidator()
    {
        RuleFor(x => x.Currency).NotEmpty().Length(3).WithName("Moneda");
        RuleFor(x => x.TrialDays).InclusiveBetween(0, 365).WithName("Días de prueba");
        RuleFor(x => x.MaxStaffSeats).InclusiveBetween(1, 200).WithName("Técnicos por club");
        RuleFor(x => x.TrialStaffSeats).InclusiveBetween(0, 200).LessThanOrEqualTo(x => x.MaxStaffSeats).WithName("Técnicos de la prueba");
        RuleFor(x => x.Prices).NotEmpty();
        RuleForEach(x => x.Prices).ChildRules(price =>
        {
            price.RuleFor(p => p.Item).IsInEnum();
            price.RuleFor(p => p.MonthlyPriceCents).InclusiveBetween(50, 1_000_000).WithName("Precio mensual (céntimos)");
            price.RuleFor(p => p.YearlyPriceCents).InclusiveBetween(50, 10_000_000).WithName("Precio anual (céntimos)");
            price.RuleFor(p => p.StripeMonthlyPriceId).MaximumLength(100);
            price.RuleFor(p => p.StripeYearlyPriceId).MaximumLength(100);
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
            command.TrialStaffSeats,
            command.MaxStaffSeats,
            command.Prices.Select(p => new LicensePriceTerms(p.Item, p.MonthlyPriceCents, p.YearlyPriceCents, p.StripeMonthlyPriceId, p.StripeYearlyPriceId)).ToList(),
            now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        catalog.Invalidate();

        return LicensingSettingsMapper.ToDto(settings, options.Value.Provider);
    }
}
