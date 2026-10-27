using TercerTiempo.Application.Common;
using TercerTiempo.Application.Moderation;
using TercerTiempo.Application.Referees;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Moderation;
using TercerTiempo.Domain.Referees;

namespace TercerTiempo.Application.Tests;

// Comentarios de las notas a los árbitros: la tabla PalabrasProhibidas, su filtro y la moderación del administrador.
public class ModerationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 18, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Terms = ForbiddenWordCatalog.All.Select(w => w.Term).ToList();

    [Fact]
    public void The_seeded_repertoire_is_large_normalized_and_without_duplicates()
    {
        Assert.True(ForbiddenWordCatalog.All.Count > 250, $"Solo {ForbiddenWordCatalog.All.Count} términos");
        Assert.Equal(ForbiddenWordCatalog.All.Count, Terms.Distinct().Count());
        Assert.All(Terms, term => Assert.Equal(ProfanityFilter.Normalize(term), term));
        Assert.All(ForbiddenWordCatalog.All.GroupBy(w => w.Kind), group => Assert.NotEmpty(group));
        Assert.Contains("coño", Terms);
        Assert.Contains("hijo de puta", Terms);
    }

    [Theory]
    [InlineData("Eres un gilipollas", "gilipollas")]
    [InlineData("HIJO DE PUTA", "hijo de puta")]
    [InlineData("Menudos cabrones, los del silbato", "cabron")]
    [InlineData("árbitro vendido, nos robó", "arbitro vendido")]
    [InlineData("Te voy a matar como vuelvas", "te voy a matar")]
    [InlineData("Me cago en tus muertos", "me cago en")]
    [InlineData("Imbéciles todos", "imbecil")]
    [InlineData("Qué coño pitas", "coño")]
    [InlineData("vaya p0ll4 de arbitraje", "polla")]
    [InlineData("putaaaaa vergüenza", "puta")]
    [InlineData("eres un c4br0n", "cabron")]
    [InlineData("m*erda de partido", "mierda")]
    [InlineData("p u t a pena", "puta")]
    [InlineData("g.i.l.i.p.o.l.l.a.s", "gilipollas")]
    [InlineData("Maricón el que lo lea", "maricon")]
    [InlineData("Sudacas fuera", "sudaca")]
    public void Insults_profanity_slurs_and_threats_are_found_even_disguised(string comment, string expected)
    {
        Assert.Contains(expected, ProfanityFilter.Find(comment, Terms));
    }

    [Fact]
    public void Each_offence_is_reported_once()
    {
        Assert.Equal(["hijo de puta", "gilipollas"], ProfanityFilter.Find("Gilipollas, hijo de puta", Terms).OrderByDescending(t => t.Length));
    }

    [Theory]
    [InlineData("Buen arbitraje: dejó jugar, acertó en el penalti del minuto 80 y estuvo bien colocado en los fueras de juego.")]
    [InlineData("Se equivocó con la segunda amarilla y usó tarde el pito; tampoco vio un robo de balón claro antes del gol.")]
    [InlineData("Hubo mucha disputa por cada balón dividido; el partido empezó retrasado y pitó bien la pelota parada.")]
    [InlineData("No vio la mano: un error gordo que cambió el partido. Acertó al expulsar al número 4 con el 2-2.")]
    [InlineData("Concha, la delegada, avisó de los conos del calentamiento. Buena reputación y criterio con las tarjetas.")]
    [InlineData("Este año ha mejorado mucho: autoridad, buena forma física y un trato correcto con los capitanes.")]
    [InlineData("Arbitraje muy flojo, sin criterio y lejos de las jugadas; no protegió a los jugadores.")]
    public void Normal_criticism_and_football_words_are_allowed(string comment)
    {
        Assert.Empty(ProfanityFilter.Find(comment, Terms));
    }

    [Fact]
    public async Task The_rating_cannot_be_saved_with_forbidden_words_and_says_which()
    {
        var validator = new RateRefereeValidator(new CommentModeration(new InMemoryForbiddenWordStore()));

        var rejected = await validator.ValidateAsync(new RateRefereeCommand(Guid.NewGuid(), Guid.NewGuid(), 2, "Un payaso, que te den", Confirmed: true));
        var error = Assert.Single(rejected.Errors);
        Assert.Equal((ErrorCode.RefereeCommentNotAllowed, nameof(RateRefereeCommand.Comment)), (error.ErrorCode, error.PropertyName));
        Assert.Contains("«payaso»", error.ErrorMessage);
        Assert.Contains("«que te den»", error.ErrorMessage);

        var fine = await validator.ValidateAsync(new RateRefereeCommand(Guid.NewGuid(), Guid.NewGuid(), 4, "Lejos de las jugadas y sin criterio con las tarjetas.", Confirmed: true));
        Assert.True(fine.IsValid);
    }

    [Fact]
    public async Task The_admin_hides_a_comment_and_the_rating_stays()
    {
        var official = MatchOfficial.Assign(Guid.NewGuid(), Guid.NewGuid(), OfficialRole.Referee, "asturfutbol", Now);
        var rating = RefereeRating.Rate(official, Guid.NewGuid(), 3, "Comentario que se coló", new RefereeRater(Guid.NewGuid(), null, Guid.NewGuid(), "CD Vallobín"), Now);
        var store = new InMemoryRefereeStore();
        store.Ratings.Add(rating);
        var admin = Guid.NewGuid();

        rating.HideComment(admin, Now);
        Assert.Equal((true, (string?)null, "Comentario que se coló", 3, admin), (rating.CommentHidden, rating.PublicComment, rating.Comment, rating.Score, rating.CommentHiddenByUserId));
        var shown = RefereeAccess.TeamRating(rating, new Dictionary<Guid, Domain.Teams.Team>());
        Assert.Equal((true, (string?)null, 3), (shown.CommentHidden, shown.Comment, shown.Score));

        rating.ShowComment();
        Assert.Equal(("Comentario que se coló", false), (rating.PublicComment, rating.CommentHidden));

        var withoutComment = RefereeRating.Rate(official, Guid.NewGuid(), 6, null, new RefereeRater(Guid.NewGuid(), null, null, "Otro club"), Now);
        Assert.Equal(ErrorCode.RefereeRatingNoComment, Assert.Throws<DomainException>(() => withoutComment.HideComment(admin, Now)).Code);
        Assert.Single(await store.GetCommentedRatingsAsync(10));
    }

    [Fact]
    public async Task The_admin_adds_and_removes_words_and_the_next_comment_uses_them()
    {
        var words = new InMemoryForbiddenWordStore();
        var unitOfWork = new CountingUnitOfWork();
        var moderation = new CommentModeration(words);
        Assert.Empty(await moderation.FindAsync("Un tuercebotas de árbitro", default));

        var added = await new AddForbiddenWordHandler(words, unitOfWork, TimeProvider.System).Handle(new AddForbiddenWordCommand("  TuerceBotas ", ForbiddenWordKind.Insult), default);
        Assert.Contains(added.Words, w => w.Term == "tuercebotas");
        Assert.NotEmpty(await moderation.FindAsync("Un tuercebotas de árbitro", default));

        var again = await Assert.ThrowsAsync<ConflictException>(() => new AddForbiddenWordHandler(words, unitOfWork, TimeProvider.System)
            .Handle(new AddForbiddenWordCommand("tuercebotas", ForbiddenWordKind.Insult), default));
        Assert.Equal(ErrorCode.ForbiddenWordExists, again.Code);

        await new RemoveForbiddenWordHandler(words, unitOfWork).Handle(new RemoveForbiddenWordCommand("Tuercebotas"), default);
        Assert.Empty(await moderation.FindAsync("Un tuercebotas de árbitro", default));
        Assert.Equal(2, unitOfWork.Saves);
        Assert.Equal(ErrorCode.ForbiddenWordInvalid, Assert.Throws<DomainException>(() => ForbiddenWord.Create("*", ForbiddenWordKind.Insult, Now)).Code);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }
}

// La tabla PalabrasProhibidas en memoria, con el repertorio sembrado.
internal sealed class InMemoryForbiddenWordStore : IForbiddenWordStore
{
    private readonly List<ForbiddenWord> _words = [.. ForbiddenWordCatalog.All];

    public Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_words.Select(w => w.Term).ToList());
    public Task<IReadOnlyList<ForbiddenWord>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ForbiddenWord>>(_words.OrderBy(w => w.Term).ToList());
    public Task<ForbiddenWord?> GetAsync(string term, CancellationToken cancellationToken = default) => Task.FromResult(_words.FirstOrDefault(w => w.Term == term));
    public void Add(ForbiddenWord word) => _words.Add(word);
    public void Remove(ForbiddenWord word) => _words.Remove(word);
    public void ForgetTerms() { }
}
