using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Privacy;

namespace TercerTiempo.Application.Privacy;

public sealed record CookieDto(
    Guid Id,
    string Name,
    CookieCategory Category,
    CookieStorage Storage,
    string Provider,
    string Purpose,
    string Duration,
    int SortOrder,
    bool RequiresConsent);

public sealed record CookiePolicyDto(
    [property: Description("Versión vigente: el consentimiento guardado en el navegador solo vale si es de esta versión.")]
    string Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CookieDto> Cookies);

internal static class CookieMapper
{
    public static CookieDto ToDto(CookieDefinition c) =>
        new(c.Id, c.Name, c.Category, c.Storage, c.Provider, c.Purpose, c.Duration, c.SortOrder, c.RequiresConsent);

    public static IReadOnlyList<CookieDto> ToDtos(IEnumerable<CookieDefinition> cookies) =>
        cookies.OrderBy(c => c.Category).ThenBy(c => c.SortOrder).ThenBy(c => c.Name).Select(ToDto).ToList();

    // Sin la fila sembrada (base recién creada a mano), la primera versión publicada.
    public static async Task<CookiePolicy> PolicyAsync(ICookieStore store, TimeProvider clock, CancellationToken cancellationToken) =>
        await store.GetPolicyAsync(cancellationToken) ?? CookiePolicy.Create(CookiePolicy.InitialVersion, clock.GetUtcNow());
}

public sealed record GetCookiePolicyQuery : IQuery<CookiePolicyDto>;

internal sealed class GetCookiePolicyHandler(ICookieStore store, TimeProvider clock) : IQueryHandler<GetCookiePolicyQuery, CookiePolicyDto>
{
    public async Task<CookiePolicyDto> Handle(GetCookiePolicyQuery query, CancellationToken cancellationToken)
    {
        var policy = await CookieMapper.PolicyAsync(store, clock, cancellationToken);
        return new CookiePolicyDto(policy.Version, policy.UpdatedAt, CookieMapper.ToDtos(await store.GetAllAsync(cancellationToken)));
    }
}

public sealed record ConsentDto(string VisitorId, bool Analytics, ConsentAction Action, string PolicyVersion, DateTimeOffset CreatedAt);

public sealed record RecordConsentCommand(
    [property: Description("Id aleatorio (UUID) que el navegador genera y guarda en tt.consent.")]
    string VisitorId,
    [property: Description("AcceptAll, RejectAll o Custom (entonces vale analytics).")]
    ConsentAction Action,
    bool Analytics) : ICommand<ConsentDto>;

internal sealed class RecordConsentValidator : AbstractValidator<RecordConsentCommand>
{
    public RecordConsentValidator()
    {
        RuleFor(x => x.VisitorId).NotEmpty().Must(v => Guid.TryParse(v, out _)).WithMessage("Identificador de visitante no válido.");
        RuleFor(x => x.Action).IsInEnum();
    }
}

internal sealed class RecordConsentHandler(ICookieStore store, ICurrentUser currentUser, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordConsentCommand, ConsentDto>
{
    public async Task<ConsentDto> Handle(RecordConsentCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var policy = await CookieMapper.PolicyAsync(store, clock, cancellationToken);
        var consent = CookieConsent.Record(command.VisitorId, currentUser.UserId, command.Analytics, command.Action, policy.Version, now);
        store.AddConsent(consent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ConsentDto(consent.VisitorId, consent.Analytics, consent.Action, consent.PolicyVersion, consent.CreatedAt);
    }
}

// --- Administración: catálogo de cookies y versión de la política ---

public sealed record UpsertCookieCommand(
    Guid? Id,
    string Name,
    CookieCategory Category,
    CookieStorage Storage,
    string Provider,
    string Purpose,
    string Duration,
    int SortOrder) : ICommand<CookiePolicyDto>;

internal sealed class UpsertCookieValidator : AbstractValidator<UpsertCookieCommand>
{
    public UpsertCookieValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithName("Nombre");
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Storage).IsInEnum();
        RuleFor(x => x.Provider).NotEmpty().MaximumLength(100).WithName("Responsable");
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(500).WithName("Finalidad");
        RuleFor(x => x.Duration).NotEmpty().MaximumLength(100).WithName("Duración");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000).WithName("Orden");
    }
}

internal sealed class UpsertCookieHandler(ICookieStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpsertCookieCommand, CookiePolicyDto>
{
    public async Task<CookiePolicyDto> Handle(UpsertCookieCommand command, CancellationToken cancellationToken)
    {
        var all = await store.GetAllForUpdateAsync(cancellationToken);
        if (command.Id is { } id)
        {
            var cookie = all.FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException(ErrorCode.CookieNotFound, id);
            cookie.Update(command.Category, command.Storage, command.Provider.Trim(), command.Purpose.Trim(), command.Duration.Trim(), command.SortOrder);
        }
        else
        {
            store.Add(CookieDefinition.Create(command.Name.Trim(), command.Category, command.Storage, command.Provider.Trim(), command.Purpose.Trim(), command.Duration.Trim(), command.SortOrder));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new GetCookiePolicyHandler(store, clock).Handle(new GetCookiePolicyQuery(), cancellationToken);
    }
}

public sealed record DeleteCookieCommand(Guid Id) : ICommand<CookiePolicyDto>;

internal sealed class DeleteCookieHandler(ICookieStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DeleteCookieCommand, CookiePolicyDto>
{
    public async Task<CookiePolicyDto> Handle(DeleteCookieCommand command, CancellationToken cancellationToken)
    {
        var cookie = (await store.GetAllForUpdateAsync(cancellationToken)).FirstOrDefault(c => c.Id == command.Id)
            ?? throw new NotFoundException(ErrorCode.CookieNotFound, command.Id);
        store.Remove(cookie);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new GetCookiePolicyHandler(store, clock).Handle(new GetCookiePolicyQuery(), cancellationToken);
    }
}

// Publicar una versión nueva hace que el aviso vuelva a salir a todos (los consentimientos anteriores dejan de valer).
public sealed record PublishCookiePolicyCommand(
    [property: Description("Nueva versión, p. ej. la fecha: 2026-11-02.")]
    string Version) : ICommand<CookiePolicyDto>;

internal sealed class PublishCookiePolicyValidator : AbstractValidator<PublishCookiePolicyCommand>
{
    public PublishCookiePolicyValidator() => RuleFor(x => x.Version).NotEmpty().MaximumLength(40).WithName("Versión");
}

internal sealed class PublishCookiePolicyHandler(ICookieStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<PublishCookiePolicyCommand, CookiePolicyDto>
{
    public async Task<CookiePolicyDto> Handle(PublishCookiePolicyCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var policy = await store.GetPolicyForUpdateAsync(cancellationToken);
        if (policy is null)
            store.AddPolicy(CookiePolicy.Create(command.Version.Trim(), now));
        else
            policy.Publish(command.Version.Trim(), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await new GetCookiePolicyHandler(store, clock).Handle(new GetCookiePolicyQuery(), cancellationToken);
    }
}
