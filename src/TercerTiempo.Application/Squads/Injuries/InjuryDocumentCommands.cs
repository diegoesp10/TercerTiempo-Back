using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Medical;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

// Las pruebas e informes de una lesión los sube el club o el cuerpo técnico; los abren ellos y el propio jugador.
public sealed record UploadInjuryDocumentCommand(
    Guid SquadId,
    Guid InjuryId,
    InjuryDocumentKind Kind,
    [property: Description("Nota opcional (hasta 200 caracteres): qué prueba es, de qué día, quién la pidió...")]
    string? Note,
    CrestUpload File) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record DeleteInjuryDocumentCommand(Guid SquadId, Guid InjuryId, Guid DocumentId) : ICommand<IReadOnlyList<InjuryDto>>;

public sealed record OpenInjuryDocumentQuery(Guid SquadId, Guid InjuryId, Guid DocumentId) : IQuery<InjuryDocumentFile>;

public sealed record InjuryDocumentFile(Stream Content, string ContentType, string FileName);

internal sealed class UploadInjuryDocumentValidator : AbstractValidator<UploadInjuryDocumentCommand>
{
    public UploadInjuryDocumentValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.InjuryId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum().WithCatalogMessage(ErrorCode.InjuryDocumentInvalidKind);
        RuleFor(x => x.Note).MaximumLength(InjuryDocument.NoteMaxLength);
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.Length).GreaterThan(0).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(InjuryDocument.MaxBytes).WithCatalogMessage(ErrorCode.InjuryDocumentTooLarge)
            .When(x => x.File is not null);
        RuleFor(x => x.File.ContentType).Must(InjuryDocument.ContentTypes.Contains).WithCatalogMessage(ErrorCode.InjuryDocumentFormat)
            .When(x => x.File is not null);
    }
}

// El tipo que manda el navegador se puede inventar: el PDF tiene que empezar como un PDF y la imagen como una imagen.
internal static class InjuryDocumentSignature
{
    public static bool Matches(ReadOnlySpan<byte> data, string contentType) =>
        contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? data.StartsWith("%PDF-"u8)
            : ImageSignature.Matches(data, contentType);
}

internal static class InjuryDocuments
{
    public static async Task<InjuryDocument> OfAsync(PlayerInjury injury, Guid documentId, IInjuryDocumentStore documents, CancellationToken cancellationToken)
    {
        var document = await documents.GetByIdAsync(documentId, cancellationToken);
        if (document is null || document.InjuryId != injury.Id)
            throw new NotFoundException(ErrorCode.InjuryDocumentNotFound, documentId);
        return document;
    }
}

internal sealed class UploadInjuryDocumentHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IClubStore clubs,
    IInjuryStore injuries,
    IInjuryDocumentStore documents,
    IInjuryDocumentStorage storage,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UploadInjuryDocumentCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(UploadInjuryDocumentCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);

        if ((await documents.GetByInjuryIdsAsync([injury.Id], cancellationToken)).Count >= InjuryDocument.MaxPerInjury)
            throw new ConflictException(ErrorCode.InjuryDocumentLimit);

        using var buffer = new MemoryStream();
        await command.File.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
            throw new DomainException(ErrorCode.FileEmpty);
        if (buffer.Length > InjuryDocument.MaxBytes)
            throw new DomainException(ErrorCode.InjuryDocumentTooLarge);

        var contentType = command.File.ContentType.ToLowerInvariant();
        if (!InjuryDocumentSignature.Matches(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), contentType))
            throw new DomainException(ErrorCode.InjuryDocumentFormat);

        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        var content = buffer.ToArray();
        var key = await storage.SaveAsync(injury.Id, content, contentType, cancellationToken);
        try
        {
            documents.Add(InjuryDocument.Create(
                injury, editor, command.Kind, command.File.FileName, contentType, content.Length, key, command.Note, clock.GetUtcNow()));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Sin fila en la base de datos el fichero no lo vería nadie: no se deja huérfano.
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

internal sealed class DeleteInjuryDocumentHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IInjuryStore injuries,
    IInjuryDocumentStore documents,
    IInjuryDocumentStorage storage,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DeleteInjuryDocumentCommand, IReadOnlyList<InjuryDto>>
{
    public async Task<IReadOnlyList<InjuryDto>> Handle(DeleteInjuryDocumentCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, command.InjuryId, injuries, cancellationToken);
        var document = await InjuryDocuments.OfAsync(injury, command.DocumentId, documents, cancellationToken);

        documents.Remove(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(document.StorageKey, CancellationToken.None);

        return await InjuryAccess.ListAsync(squad, injuries, documents, clock, cancellationToken);
    }
}

internal sealed class OpenInjuryDocumentHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IInjuryStore injuries,
    IInjuryDocumentStore documents,
    IInjuryDocumentStorage storage,
    TimeProvider clock) : IQueryHandler<OpenInjuryDocumentQuery, InjuryDocumentFile>
{
    public async Task<InjuryDocumentFile> Handle(OpenInjuryDocumentQuery query, CancellationToken cancellationToken)
    {
        var squad = await InjuryAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var injury = await InjuryAccess.InjuryOfAsync(squad, query.InjuryId, injuries, cancellationToken);

        if (!await InjuryAccess.CanOpenDocumentsAsync(squad, injury, currentUser, clock, clubs, cancellationToken))
            throw new ForbiddenException(ErrorCode.InjuryDocumentPrivate);

        var document = await InjuryDocuments.OfAsync(injury, query.DocumentId, documents, cancellationToken);
        var content = await storage.OpenReadAsync(document.StorageKey, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.InjuryDocumentNotFound, document.Id);

        return new InjuryDocumentFile(content, document.ContentType, document.FileName);
    }
}
