using System.Reflection;
using System.Text;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Medical;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// Las pruebas e informes de una lesión (radiografías, informes del hospital): los sube el club o el cuerpo técnico, los abren
// ellos y el propio jugador, y se van con la lesión.
public class InjuryDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nradiografía de tobillo");

    [Fact]
    public async Task The_staff_attaches_a_report_that_only_the_club_the_staff_and_the_player_himself_can_open()
    {
        var f = new Fixture();
        var injury = f.Injure(f.Pelayo);

        var list = await f.Upload(f.CoachUser).Handle(Upload(injury, Pdf, "application/pdf", @"C:\fakepath\Informe ""urgencias"".pdf"), default);

        var document = Assert.Single(list.Single(i => i.Id == injury.Id).Documents);
        Assert.Equal(("Informe urgencias.pdf", "Informe médico", "application/pdf", (long)Pdf.Length), (document.FileName, document.KindLabel, document.ContentType, document.SizeBytes));
        Assert.Equal($"/api/squads/{f.Squad.Id}/injuries/{injury.Id}/documents/{document.Id}", document.Url);

        foreach (var viewer in new[] { f.CoachUser, f.ClubUser, f.PelayoUser })
        {
            var file = await f.Open(viewer).Handle(new OpenInjuryDocumentQuery(f.Squad.Id, injury.Id, document.Id), default);
            using var read = new MemoryStream();
            await file.Content.CopyToAsync(read);
            Assert.Equal(Pdf, read.ToArray());
        }

        foreach (var viewer in new[] { f.NelUser, Fixture.AdminUser })
        {
            var denied = await Assert.ThrowsAsync<ForbiddenException>(() => f.Open(viewer).Handle(new OpenInjuryDocumentQuery(f.Squad.Id, injury.Id, document.Id), default));
            Assert.Equal(ErrorCode.InjuryDocumentPrivate, denied.Code);
        }
    }

    [Fact]
    public async Task A_player_cannot_upload_and_a_file_that_is_not_what_it_says_is_rejected()
    {
        var f = new Fixture();
        var injury = f.Injure(f.Pelayo);

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Upload(f.PelayoUser).Handle(Upload(injury, Pdf, "application/pdf"), default));
        var fake = await Assert.ThrowsAsync<DomainException>(() => f.Upload(f.CoachUser).Handle(Upload(injury, "hola"u8.ToArray(), "application/pdf"), default));

        Assert.Equal(ErrorCode.InjuryDocumentFormat, fake.Code);
        Assert.Empty(f.Files);
        Assert.Empty(f.Documents);
    }

    [Fact]
    public async Task An_injury_takes_at_most_ten_documents()
    {
        var f = new Fixture();
        var injury = f.Injure(f.Pelayo);
        for (var i = 0; i < InjuryDocument.MaxPerInjury; i++)
            await f.Upload(f.CoachUser).Handle(Upload(injury, Pdf, "application/pdf"), default);

        var full = await Assert.ThrowsAsync<ConflictException>(() => f.Upload(f.CoachUser).Handle(Upload(injury, Pdf, "application/pdf"), default));

        Assert.Equal(ErrorCode.InjuryDocumentLimit, full.Code);
        Assert.Equal(InjuryDocument.MaxPerInjury, f.Files.Count);
    }

    [Fact]
    public async Task Removing_a_document_or_the_injury_removes_the_files_too()
    {
        var f = new Fixture();
        var injury = f.Injure(f.Pelayo);
        var other = f.Injure(f.Nel);
        await f.Upload(f.CoachUser).Handle(Upload(injury, Pdf, "application/pdf"), default);
        await f.Upload(f.CoachUser).Handle(Upload(injury, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2], "image/jpeg"), default);
        await f.Upload(f.CoachUser).Handle(Upload(other, Pdf, "application/pdf"), default);

        var first = f.Documents.First(d => d.InjuryId == injury.Id);
        await f.DeleteDocument().Handle(new DeleteInjuryDocumentCommand(f.Squad.Id, injury.Id, first.Id), default);
        Assert.Equal(2, f.Files.Count);

        await f.DeleteInjury().Handle(new DeleteInjuryCommand(f.Squad.Id, injury.Id), default);

        var left = Assert.Single(f.Documents);
        Assert.Equal(other.Id, left.InjuryId);
        Assert.Equal([left.StorageKey], f.Files.Keys);
    }

    private static UploadInjuryDocumentCommand Upload(PlayerInjury injury, byte[] content, string contentType, string fileName = "prueba") =>
        new(injury.SquadId, injury.Id, InjuryDocumentKind.MedicalReport, null, new CrestUpload(fileName, contentType, content.Length, new MemoryStream(content)));

    private sealed record Account(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null, AccountType? Type = null, bool Admin = false) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Type;
        public bool IsAdmin => Admin;
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Coach Coach { get; } = Coach.Register(Guid.NewGuid(), "Abel Pablo", Now);
        public Player PelayoAccount { get; } = Player.Register(Guid.NewGuid(), "Pelayo Fernández", Position.Forward, null, Now);
        public Player NelAccount { get; } = Player.Register(Guid.NewGuid(), "Nel Suárez", Position.Goalkeeper, null, Now);
        public Squad Squad { get; }
        public SquadPlayer Pelayo { get; }
        public SquadPlayer Nel { get; }
        public List<PlayerInjury> Injuries { get; } = [];
        public List<InjuryDocument> Documents { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = [];

        public ICurrentUser CoachUser => new Account(CoachId: Coach.Id, UserId: Coach.UserId, Type: AccountType.Coach);
        public ICurrentUser ClubUser => new Account(ClubId: Club.Id, UserId: Club.UserId, Type: AccountType.Club);
        public ICurrentUser PelayoUser => new Account(PlayerId: PelayoAccount.Id, UserId: PelayoAccount.UserId, Type: AccountType.Player);
        public ICurrentUser NelUser => new Account(PlayerId: NelAccount.Id, UserId: NelAccount.UserId, Type: AccountType.Player);
        public static ICurrentUser AdminUser => new Account(UserId: Guid.NewGuid(), Admin: true);

        public Fixture()
        {
            Club.AddTeam(Team.Id, Now);
            Squad = Squad.Create(Team.Id, Guid.NewGuid(), Coach.Id, Now);
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, Now);
            Nel = Squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, Now);
            Squad.LinkPlayer(Pelayo.Id, PelayoAccount.Id);
            Squad.LinkPlayer(Nel.Id, NelAccount.Id);
        }

        public PlayerInjury Injure(SquadPlayer player)
        {
            var injury = PlayerInjury.Create(Squad, player, SquadEditor.Coach(Coach.Id), TestInjuryTypes.Of(InjuryTypeCodes.AnkleSprain), null, SpainTime.Today(Now).AddDays(-3), null, Now);
            Injuries.Add(injury);
            return injury;
        }

        public UploadInjuryDocumentHandler Upload(ICurrentUser user) => new(user, Squads, Coaches, Clubs, InjuryStore, DocumentStore, Storage, Unit, Clock);
        public OpenInjuryDocumentHandler Open(ICurrentUser user) => new(user, Squads, Clubs, InjuryStore, DocumentStore, Storage, Clock);
        public DeleteInjuryDocumentHandler DeleteDocument() => new(CoachUser, Squads, Clubs, InjuryStore, DocumentStore, Storage, Unit, Clock);
        public DeleteInjuryHandler DeleteInjury() => new(CoachUser, Squads, Clubs, InjuryStore, DocumentStore, Storage, Unit, Clock);

        private static TimeProvider Clock => new FixedClock();

        private ISquadStore Squads => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Squad.Id ? Squad : null),
            _ => throw new NotSupportedException(method),
        });

        private IClubStore Clubs => Stub<IClubStore>((method, args) => method switch
        {
            nameof(IClubStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Club.Id ? Club : null),
            nameof(IClubStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Club.UserId ? Club : null),
            _ => throw new NotSupportedException(method),
        });

        private ICoachStore Coaches => Stub<ICoachStore>((method, args) => method switch
        {
            nameof(ICoachStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Coach.UserId ? Coach : null),
            _ => throw new NotSupportedException(method),
        });

        private IInjuryStore InjuryStore => Stub<IInjuryStore>((method, args) => method switch
        {
            nameof(IInjuryStore.GetByIdAsync) => Task.FromResult(Injuries.FirstOrDefault(i => i.Id == (Guid)args![0]!)),
            nameof(IInjuryStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<PlayerInjury>>(Injuries.Where(i => i.SquadId == (Guid)args![0]!).ToList()),
            nameof(IInjuryStore.Remove) => Injuries.Remove((PlayerInjury)args![0]!),
            _ => throw new NotSupportedException(method),
        });

        private IInjuryDocumentStore DocumentStore => Stub<IInjuryDocumentStore>((method, args) => method switch
        {
            nameof(IInjuryDocumentStore.GetByIdAsync) => Task.FromResult(Documents.FirstOrDefault(d => d.Id == (Guid)args![0]!)),
            nameof(IInjuryDocumentStore.GetByInjuryIdsAsync) => Task.FromResult<IReadOnlyList<InjuryDocument>>(
                Documents.Where(d => ((IReadOnlyCollection<Guid>)args![0]!).Contains(d.InjuryId)).ToList()),
            nameof(IInjuryDocumentStore.Add) => Add((InjuryDocument)args![0]!),
            nameof(IInjuryDocumentStore.Remove) => Documents.Remove((InjuryDocument)args![0]!),
            _ => throw new NotSupportedException(method),
        });

        private object? Add(InjuryDocument document)
        {
            Documents.Add(document);
            return null;
        }

        private IInjuryDocumentStorage Storage => new MemoryStorage(Files);

        private static IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(1));
    }

    private sealed class MemoryStorage(Dictionary<string, byte[]> files) : IInjuryDocumentStorage
    {
        public Task<string> SaveAsync(Guid injuryId, byte[] content, string contentType, CancellationToken cancellationToken)
        {
            var key = $"{injuryId:N}-{Guid.NewGuid():N}";
            files[key] = content;
            return Task.FromResult(key);
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(files.TryGetValue(key, out var content) ? new MemoryStream(content) : null);

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            files.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}
