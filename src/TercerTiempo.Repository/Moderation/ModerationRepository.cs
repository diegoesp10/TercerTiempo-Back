using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Moderation;

namespace TercerTiempo.Repository.Moderation;

public interface IModerationRepository
{
    Task<CommentCheckDto> CheckAsync(string? text, CancellationToken cancellationToken = default);
    Task<RefereeCommentsDto> ListCommentsAsync(CommentFilter? filter, string? search, CancellationToken cancellationToken = default);
    Task<RefereeCommentAdminDto> SetCommentHiddenAsync(Guid ratingId, bool hidden, CancellationToken cancellationToken = default);
    Task<ForbiddenWordsDto> ListWordsAsync(CancellationToken cancellationToken = default);
    Task<ForbiddenWordsDto> AddWordAsync(AddForbiddenWordCommand command, CancellationToken cancellationToken = default);
    Task<ForbiddenWordsDto> RemoveWordAsync(string term, CancellationToken cancellationToken = default);
}

public sealed record CommentCheckRequest(
    [property: Description("El comentario que se quiere publicar.")]
    string? Text);

public sealed record CommentHiddenRequest(
    [property: Description("true retira el comentario (la nota sigue publicada); false lo vuelve a publicar.")]
    bool Hidden);

internal sealed class ModerationRepository(
    IQueryHandler<CheckCommentQuery, CommentCheckDto> check,
    IQueryHandler<ListRefereeCommentsQuery, RefereeCommentsDto> comments,
    ICommandHandler<SetRefereeCommentHiddenCommand, RefereeCommentAdminDto> hide,
    IQueryHandler<ListForbiddenWordsQuery, ForbiddenWordsDto> words,
    ICommandHandler<AddForbiddenWordCommand, ForbiddenWordsDto> add,
    ICommandHandler<RemoveForbiddenWordCommand, ForbiddenWordsDto> remove) : IModerationRepository
{
    public Task<CommentCheckDto> CheckAsync(string? text, CancellationToken cancellationToken = default) =>
        check.Handle(new CheckCommentQuery(text), cancellationToken);

    public Task<RefereeCommentsDto> ListCommentsAsync(CommentFilter? filter, string? search, CancellationToken cancellationToken = default) =>
        comments.Handle(new ListRefereeCommentsQuery(filter, search), cancellationToken);

    public Task<RefereeCommentAdminDto> SetCommentHiddenAsync(Guid ratingId, bool hidden, CancellationToken cancellationToken = default) =>
        hide.Handle(new SetRefereeCommentHiddenCommand(ratingId, hidden), cancellationToken);

    public Task<ForbiddenWordsDto> ListWordsAsync(CancellationToken cancellationToken = default) =>
        words.Handle(new ListForbiddenWordsQuery(), cancellationToken);

    public Task<ForbiddenWordsDto> AddWordAsync(AddForbiddenWordCommand command, CancellationToken cancellationToken = default) =>
        add.Handle(command, cancellationToken);

    public Task<ForbiddenWordsDto> RemoveWordAsync(string term, CancellationToken cancellationToken = default) =>
        remove.Handle(new RemoveForbiddenWordCommand(term), cancellationToken);
}
