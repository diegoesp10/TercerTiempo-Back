using System.ComponentModel;
using FluentValidation;
using FluentValidation.Results;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Referees;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Moderation;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Moderation;

// Revisa un texto contra la tabla PalabrasProhibidas (de caché): lo usan el guardado de las notas y su comprobación previa.
public sealed class CommentModeration(IForbiddenWordStore words)
{
    public async Task<IReadOnlyList<string>> FindAsync(string? text, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(text) ? [] : ProfanityFilter.Find(text, await words.GetTermsAsync(cancellationToken));

    public static string Message(IReadOnlyList<string> found) =>
        ErrorCatalog.Format(ErrorCode.RefereeCommentNotAllowed, Quote(found));

    public static ValidationFailure Failure(string field, IReadOnlyList<string> found)
    {
        var args = new object[] { Quote(found) };
        return new ValidationFailure(field, ErrorCatalog.Format(ErrorCode.RefereeCommentNotAllowed, args))
        {
            ErrorCode = ErrorCode.RefereeCommentNotAllowed,
            CustomState = args,
        };
    }

    private static string Quote(IReadOnlyList<string> found) => string.Join(", ", found.Select(f => $"«{f}»"));
}

public sealed record CommentCheckDto(
    [property: Description("true si el texto se puede publicar.")]
    bool Allowed,
    [property: Description("Palabras o expresiones prohibidas que aparecen (como están en la lista).")]
    IReadOnlyList<string> Terms,
    [property: Description("El motivo, si no se puede publicar.")]
    string? Message);

public sealed record CheckCommentQuery(string? Text) : IQuery<CommentCheckDto>;

internal sealed class CheckCommentHandler(CommentModeration moderation) : IQueryHandler<CheckCommentQuery, CommentCheckDto>
{
    public async Task<CommentCheckDto> Handle(CheckCommentQuery query, CancellationToken cancellationToken)
    {
        var found = await moderation.FindAsync(query.Text, cancellationToken);
        return new CommentCheckDto(found.Count == 0, found, found.Count == 0 ? null : CommentModeration.Message(found));
    }
}

// --- Administración: comentarios de las notas a los árbitros ---

public enum CommentFilter
{
    All = 1,
    Flagged = 2,
    Hidden = 3,
    Visible = 4,
}

public sealed record RefereeCommentAdminDto(
    Guid RatingId,
    Guid RefereeId,
    string RefereeName,
    Guid MatchId,
    DateTimeOffset KickoffAt,
    string HomeTeamName,
    string AwayTeamName,
    [property: Description("Equipo que puso la nota.")]
    string TeamName,
    RefereeRaterDto RatedBy,
    OfficialRole Role,
    int Score,
    [property: Description("El comentario completo, también si está oculto.")]
    string Comment,
    [property: Description("Retirado por la administración: la nota se publica, el comentario no.")]
    bool Hidden,
    DateTimeOffset? HiddenAt,
    [property: Description("Palabras o expresiones de la lista actual que contiene (puede ser anterior a ellas).")]
    IReadOnlyList<string> FlaggedTerms,
    DateTimeOffset RatedAt);

public sealed record RefereeCommentsDto(
    int Total,
    int Flagged,
    int Hidden,
    [property: Description("Los comentarios del filtro, del más reciente al más antiguo.")]
    IReadOnlyList<RefereeCommentAdminDto> Comments);

public sealed record ListRefereeCommentsQuery(CommentFilter? Filter = null, string? Search = null) : IQuery<RefereeCommentsDto>;

public sealed record SetRefereeCommentHiddenCommand(Guid RatingId, bool Hidden) : ICommand<RefereeCommentAdminDto>;

internal static class RefereeCommentRows
{
    public const int Max = 500;

    public static async Task<IReadOnlyList<RefereeCommentAdminDto>> BuildAsync(
        IReadOnlyList<RefereeRating> ratings, IRefereeStore referees, IMatchStore matches, ITeamStore teams, IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        var people = (await referees.GetByIdsAsync(ratings.Select(r => r.RefereeId), cancellationToken)).ToDictionary(r => r.Id);
        var matchMap = (await matches.GetByIdsAsync(ratings.Select(r => r.MatchId).Distinct(), cancellationToken)).ToDictionary(m => m.Id);
        var teamIds = matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Concat(ratings.Select(r => r.TeamId)).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);
        string Name(Guid id) => teamMap.GetValueOrDefault(id)?.Name ?? "Equipo";

        return ratings
            .Select(r =>
            {
                var match = matchMap.GetValueOrDefault(r.MatchId);
                return new RefereeCommentAdminDto(
                    r.Id, r.RefereeId, people.GetValueOrDefault(r.RefereeId)?.Name ?? "Árbitro", r.MatchId, match?.KickoffAt ?? r.CreatedAt,
                    match is null ? "Local" : Name(match.HomeTeamId), match is null ? "Visitante" : Name(match.AwayTeamId), Name(r.TeamId),
                    RefereeAccess.Rater(r), r.Role, r.Score, r.Comment ?? string.Empty, r.CommentHidden, r.CommentHiddenAt,
                    ProfanityFilter.Find(r.Comment, terms), r.CreatedAt);
            })
            .ToList();
    }
}

internal sealed class ListRefereeCommentsHandler(
    IRefereeStore referees, IMatchStore matches, ITeamStore teams, IForbiddenWordStore words) : IQueryHandler<ListRefereeCommentsQuery, RefereeCommentsDto>
{
    public async Task<RefereeCommentsDto> Handle(ListRefereeCommentsQuery query, CancellationToken cancellationToken)
    {
        var ratings = await referees.GetCommentedRatingsAsync(RefereeCommentRows.Max, cancellationToken);
        var rows = await RefereeCommentRows.BuildAsync(ratings, referees, matches, teams, await words.GetTermsAsync(cancellationToken), cancellationToken);

        var search = NameNormalizer.Normalize(query.Search);
        var shown = rows
            .Where(r => query.Filter switch
            {
                CommentFilter.Flagged => r.FlaggedTerms.Count > 0,
                CommentFilter.Hidden => r.Hidden,
                CommentFilter.Visible => !r.Hidden,
                _ => true,
            })
            .Where(r => search.Length == 0
                || new[] { r.Comment, r.RefereeName, r.TeamName, r.RatedBy.Name }.Any(text => NameNormalizer.Normalize(text).Contains(search, StringComparison.Ordinal)))
            .ToList();
        return new RefereeCommentsDto(rows.Count, rows.Count(r => r.FlaggedTerms.Count > 0), rows.Count(r => r.Hidden), shown);
    }
}

internal sealed class SetRefereeCommentHiddenHandler(
    ICurrentUser currentUser,
    IRefereeStore referees,
    IMatchStore matches,
    ITeamStore teams,
    IForbiddenWordStore words,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetRefereeCommentHiddenCommand, RefereeCommentAdminDto>
{
    public async Task<RefereeCommentAdminDto> Handle(SetRefereeCommentHiddenCommand command, CancellationToken cancellationToken)
    {
        var rating = await referees.GetRatingByIdAsync(command.RatingId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.RefereeRatingNotFound, command.RatingId);
        if (command.Hidden) rating.HideComment(currentUser.UserId, clock.GetUtcNow());
        else rating.ShowComment();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var rows = await RefereeCommentRows.BuildAsync([rating], referees, matches, teams, await words.GetTermsAsync(cancellationToken), cancellationToken);
        return rows[0];
    }
}

// --- Administración: la tabla PalabrasProhibidas ---

public sealed record ForbiddenWordDto(
    [property: Description("Como se compara: minúsculas, sin tildes salvo la ñ.")]
    string Term,
    ForbiddenWordKind Kind,
    DateTimeOffset CreatedAt);

public sealed record ForbiddenWordsDto(int Total, IReadOnlyList<ForbiddenWordDto> Words);

public sealed record ListForbiddenWordsQuery : IQuery<ForbiddenWordsDto>;

public sealed record AddForbiddenWordCommand(
    [property: Description("Palabra o expresión; se guarda en minúsculas y sin tildes (salvo la ñ).")]
    string Term,
    ForbiddenWordKind Kind) : ICommand<ForbiddenWordsDto>;

public sealed record RemoveForbiddenWordCommand(string Term) : ICommand<ForbiddenWordsDto>;

internal sealed class AddForbiddenWordValidator : AbstractValidator<AddForbiddenWordCommand>
{
    public AddForbiddenWordValidator()
    {
        RuleFor(x => x.Term).NotEmpty().MaximumLength(ForbiddenWord.MaxLength * 2).WithName("Palabra");
        RuleFor(x => x.Kind).IsInEnum();
    }
}

internal static class ForbiddenWordList
{
    public static async Task<ForbiddenWordsDto> LoadAsync(IForbiddenWordStore words, CancellationToken cancellationToken)
    {
        var all = await words.GetAllAsync(cancellationToken);
        return new ForbiddenWordsDto(all.Count, all.Select(w => new ForbiddenWordDto(w.Term, w.Kind, w.CreatedAt)).ToList());
    }
}

internal sealed class ListForbiddenWordsHandler(IForbiddenWordStore words) : IQueryHandler<ListForbiddenWordsQuery, ForbiddenWordsDto>
{
    public Task<ForbiddenWordsDto> Handle(ListForbiddenWordsQuery query, CancellationToken cancellationToken) =>
        ForbiddenWordList.LoadAsync(words, cancellationToken);
}

internal sealed class AddForbiddenWordHandler(IForbiddenWordStore words, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<AddForbiddenWordCommand, ForbiddenWordsDto>
{
    public async Task<ForbiddenWordsDto> Handle(AddForbiddenWordCommand command, CancellationToken cancellationToken)
    {
        var word = ForbiddenWord.Create(command.Term, command.Kind, clock.GetUtcNow());
        if (await words.GetAsync(word.Term, cancellationToken) is not null) throw new ConflictException(ErrorCode.ForbiddenWordExists, word.Term);

        words.Add(word);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        words.ForgetTerms();
        return await ForbiddenWordList.LoadAsync(words, cancellationToken);
    }
}

internal sealed class RemoveForbiddenWordHandler(IForbiddenWordStore words, IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveForbiddenWordCommand, ForbiddenWordsDto>
{
    public async Task<ForbiddenWordsDto> Handle(RemoveForbiddenWordCommand command, CancellationToken cancellationToken)
    {
        var term = ProfanityFilter.Normalize(command.Term);
        var word = await words.GetAsync(term, cancellationToken) ?? throw new NotFoundException(ErrorCode.ForbiddenWordNotFound, term);

        words.Remove(word);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        words.ForgetTerms();
        return await ForbiddenWordList.LoadAsync(words, cancellationToken);
    }
}
