using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.Invite;
using TercerTiempo.Application.Auth.Register;
using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players.GetInvite;
using TercerTiempo.Application.Players.JoinSquad;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.Settings;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

public class PlayerInvitationTests
{
    [Fact]
    public async Task Preview_returns_only_the_invited_player_without_any_codes()
    {
        var f = new Fixture();
        f.Squad.GeneratePlayerInviteCode(f.Target.Id);
        f.Squad.GeneratePlayerInviteCode(f.Other.Id);
        var dto = await f.Preview().Handle(new(f.Target.InviteCode!), default);
        Assert.Equal(f.Target.Id, dto.Player.Id);
        Assert.Equal(f.Target.FullName, dto.Player.FullName);
        Assert.Null(dto.Player.InviteCode);
        Assert.DoesNotContain(f.Other.FullName, JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task Join_ignores_the_account_name_and_renames_it_after_the_entry()
    {
        var f = new Fixture();
        f.Squad.GeneratePlayerInviteCode(f.Other.Id);
        var code = f.Other.InviteCode!;
        var preview = await f.Preview().Handle(new(code), default);
        Assert.Equal(f.Other.FullName, preview.Player.FullName);
        var result = await f.Join().Handle(new(code), default);
        Assert.Equal(f.Other.Id, result.Squad!.SquadPlayerId);
        Assert.Equal("Nel Suárez", f.Player.DisplayName);
        Assert.True(f.Other.IsLinked);
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public async Task Register_as_player_needs_only_the_personal_code_and_takes_the_entry_name()
    {
        var f = new Fixture();
        f.Squad.GeneratePlayerInviteCode(f.Target.Id);
        var code = f.Target.InviteCode!;
        var anonymous = await f.AnonymousInvite().Handle(new(code), default);
        Assert.Equal(f.Target.FullName, anonymous.Player.FullName);
        Assert.Null(anonymous.Player.InviteCode);

        var response = await f.Register().Handle(new("nuevo@example.com", "Prueba1234", null, AccountType.Player, null, null, code), default);
        var registered = f.Registered.Single();
        Assert.Equal(AccountType.Player, response.Account.Type);
        Assert.Equal("Pelayo Fernández", registered.DisplayName);
        Assert.Equal(Position.Forward, registered.Position);
        Assert.Equal(registered.Id, f.Target.PlayerId);
        Assert.Null(f.Target.InviteCode);
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public async Task Register_as_player_rejects_missing_or_unknown_codes_before_creating_the_user()
    {
        var f = new Fixture();
        RegisterCommand withoutCode = new("nuevo@example.com", "Prueba1234", "Alguien", AccountType.Player, null, null, null);
        var validation = new RegisterValidator().Validate(withoutCode);
        Assert.Contains(validation.Errors, e => e.ErrorCode == ErrorCode.AuthInviteCodeRequired);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Register().Handle(new("nuevo@example.com", "Prueba1234", "Alguien", AccountType.Player, Position.Forward, null, "J9MEG589"), default));
        Assert.Empty(f.Registered);
        Assert.Equal(0, f.Saves);
    }

    [Theory]
    [InlineData(null, "auth.club_invite_required")]
    [InlineData("NOEXISTE23", "auth.club_invite_invalid")]
    public async Task In_the_closed_beta_a_club_needs_a_valid_invitation_before_any_account_is_created(string? code, string expected)
    {
        var f = new Fixture();

        var error = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            f.Register(clubInviteRequired: true).Handle(new("club@example.com", "Prueba1234", "CD Vallobín", AccountType.Club, null, null, code), default));

        Assert.Equal(expected, Assert.Single(error.Errors).ErrorCode);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Without_a_team_license_players_can_neither_register_nor_link_with_their_code()
    {
        var f = new Fixture { TeamLicensed = false };
        f.Squad.GeneratePlayerInviteCode(f.Target.Id);
        var code = f.Target.InviteCode!;

        var register = await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Register().Handle(new("nuevo@example.com", "Prueba1234", null, AccountType.Player, null, null, code), default));
        var join = await Assert.ThrowsAsync<ForbiddenException>(() => f.Join().Handle(new(code), default));

        Assert.Equal(TercerTiempo.Domain.Common.Errors.ErrorCode.LicenseRequired, register.Code);
        Assert.Equal(TercerTiempo.Domain.Common.Errors.ErrorCode.LicenseRequired, join.Code);
        Assert.Empty(f.Registered);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Forged_player_id_in_json_cannot_override_the_personal_code()
    {
        var f = new Fixture();
        f.Squad.GeneratePlayerInviteCode(f.Target.Id);
        var code = f.Target.InviteCode!;
        var command = JsonSerializer.Deserialize<JoinSquadAsPlayerCommand>(
            JsonSerializer.Serialize(new { inviteCode = code, squadPlayerId = f.Other.Id }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var result = await f.Join().Handle(command, default);
        Assert.Equal(f.Target.Id, result.Squad!.SquadPlayerId);
        Assert.Equal(f.Player.Id, f.Target.PlayerId);
        Assert.False(f.Other.IsLinked);
        Assert.Null(f.Target.InviteCode);
        Assert.Equal(1, f.Saves);
        f.Squad.UnlinkPlayer(f.Player.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Join().Handle(new(code), default));
    }

    [Fact]
    public async Task Missing_or_legacy_shared_codes_cannot_link_any_player()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Join().Handle(new("J9MEG589"), default));
        Assert.False(f.Target.IsLinked);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Non_player_accounts_cannot_preview_invites()
    {
        var f = new Fixture();
        f.Squad.GeneratePlayerInviteCode(f.Target.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Preview(f.CoachUser).Handle(new(f.Target.InviteCode!), default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_owning_club_or_current_staff_can_generate_codes(bool club)
    {
        var f = new Fixture();
        var dto = await f.Generate(club ? f.ClubUser : f.CoachUser).Handle(new(f.Squad.Id, f.Target.Id), default);
        Assert.True(dto.CanInvitePlayers);
        Assert.NotNull(dto.Players.Single(p => p.Id == f.Target.Id).InviteCode);
        Assert.Null(f.Other.InviteCode);
        Assert.Equal(1, f.Saves);
    }

    [Theory]
    [InlineData("player")]
    [InlineData("other-club")]
    [InlineData("other-coach")]
    [InlineData("expired")]
    public async Task Unauthorized_accounts_cannot_generate_codes(string kind)
    {
        var f = new Fixture();
        var user = kind switch
        {
            "player" => f.PlayerUser,
            "other-club" => new User(ClubId: Guid.NewGuid()),
            "other-coach" => new User(CoachId: Guid.NewGuid()),
            _ => f.CoachUser,
        };
        if (kind == "expired") f.Squad.SetStaffValidity(f.Coach.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Generate(user).Handle(new(f.Squad.Id, f.Target.Id), default));
        Assert.Null(f.Target.InviteCode);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Public_visitors_and_players_never_receive_pending_codes()
    {
        var f = new Fixture();
        f.Squad.GeneratePlayerInviteCode(f.Target.Id);
        f.Squad.SetVisibility(SquadVisibility.Public);
        foreach (var user in new[] { new User(), f.PlayerUser, new User(ClubId: Guid.NewGuid()) })
        {
            var dto = await f.Detail(user);
            Assert.False(dto.CanInvitePlayers);
            Assert.All(dto.Players, p => Assert.Null(p.InviteCode));
        }
    }

    [Fact]
    public void Persistence_enforces_single_use_and_one_active_squad_per_account()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=True;TrustServerCertificate=True").Options);
        var entity = db.Model.FindEntityType(typeof(SquadPlayer))!;
        Assert.True(entity.FindProperty(nameof(SquadPlayer.InviteCode))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(SquadPlayer.InviteCode));
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(SquadPlayer.PlayerId)
            && i.GetFilter()!.Contains("'Active'"));
    }

    private sealed record User(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => PlayerId != null ? TercerTiempo.Application.Abstractions.Identity.AccountType.Player : CoachId != null ? TercerTiempo.Application.Abstractions.Identity.AccountType.Coach : TercerTiempo.Application.Abstractions.Identity.AccountType.Club;
    }

    private sealed class Fixture
    {
        public Player Player { get; } = Player.Register(Guid.NewGuid(), " pelayo fernandez ", Position.Forward, null, DateTimeOffset.UtcNow);
        public Coach Coach { get; } = Coach.Register(Guid.NewGuid(), "Entrenador", DateTimeOffset.UtcNow);
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "Club", DateTimeOffset.UtcNow);
        public Team Team { get; } = Team.Create("Equipo", "EQ", null);
        public Competition Competition { get; } = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        public Squad Squad { get; }
        public SquadPlayer Target { get; }
        public SquadPlayer Other { get; }
        public User PlayerUser => new(PlayerId: Player.Id, UserId: Player.UserId);
        public User CoachUser => new(CoachId: Coach.Id, UserId: Coach.UserId);
        public User ClubUser => new(ClubId: Club.Id, UserId: Club.UserId);
        public int Saves { get; private set; }
        public List<Player> Registered { get; } = [];

        public Fixture()
        {
            var season = Competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
            Squad = Squad.Create(Team.Id, season.Id, Coach.Id, DateTimeOffset.UtcNow);
            Target = Squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, DateTimeOffset.UtcNow);
            Other = Squad.AddPlayer("Nel Suárez", Position.Defender, 4, null, DateTimeOffset.UtcNow);
            Club.AddTeam(Team.Id, DateTimeOffset.UtcNow);
        }

        private ISquadStore Squads => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult<Squad?>((Guid)args![0]! == Squad.Id ? Squad : null),
            nameof(ISquadStore.GetByInviteCodeAsync) => Task.FromResult<Squad?>(Squad.ActivePlayers.Any(p => !p.IsLinked && p.InviteCode != null && p.InviteCode == (string)args![0]!) ? Squad : null),
            nameof(ISquadStore.GetByPlayerAsync) => Task.FromResult<Squad?>(Squad.IsMember((Guid)args![0]!) ? Squad : null),
            _ => throw new NotSupportedException(method),
        });
        private IPlayerStore Players => Stub<IPlayerStore>((method, args) => method switch
        {
            nameof(IPlayerStore.GetByUserIdAsync) => Task.FromResult<Player?>(Registered.Concat([Player]).FirstOrDefault(p => p.UserId == (Guid)args![0]!)),
            nameof(IPlayerStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Player>>([Player]),
            nameof(IPlayerStore.Add) => Run(() => Registered.Add((Player)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private static object? Run(Action action) { action(); return null; }
        private IClubStore Clubs => Stub<IClubStore>((_, args) => Task.FromResult<Club?>((Guid)args![0]! == Club.Id ? Club : null));
        private ICoachStore Coaches => Stub<ICoachStore>((_, _) => Task.FromResult<IReadOnlyList<Coach>>([Coach]));
        private ITeamStore Teams => Stub<ITeamStore>((method, _) => method switch
        {
            nameof(ITeamStore.GetRosterAsync) => Task.FromResult<IReadOnlyList<RosterEntry>>([]),
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team]),
            _ => Task.FromResult<Team?>(Team),
        });
        private ICompetitionStore Competitions => Stub<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(Competition));
        private IInjuryStore Injuries => Stub<IInjuryStore>((_, _) => Task.FromResult<IReadOnlyList<PlayerInjury>>([]));
        private IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(++Saves));
        public GetSquadInviteHandler Preview(ICurrentUser? user = null) => new(user ?? PlayerUser, Players, Squads, Teams, Competitions);
        public GetInviteHandler AnonymousInvite() => new(Squads, Teams, Competitions);
        // Sin licencia vigente del equipo, la puerta corta el alta y la vinculación de jugadores.
        public bool TeamLicensed { get; set; } = true;

        public TercerTiempo.Application.Licensing.ILicenseGate Licenses() => Stub<TercerTiempo.Application.Licensing.ILicenseGate>((method, _) => method switch
        {
            nameof(TercerTiempo.Application.Licensing.ILicenseGate.IsViewerLicensedAsync) => Task.FromResult(true),
            nameof(TercerTiempo.Application.Licensing.ILicenseGate.CanAccessSquadAsync) => Task.FromResult(true),
            nameof(TercerTiempo.Application.Licensing.ILicenseGate.EnsureTeamLicensedAsync) => TeamLicensed
                ? Task.CompletedTask
                : Task.FromException(new ForbiddenException(TercerTiempo.Domain.Common.Errors.ErrorCode.LicenseRequired)),
            _ => throw new NotSupportedException(method),
        });

        public RegisterHandler Register(bool clubInviteRequired = false)
        {
            var identity = Stub<IIdentityService>((method, _) => method == nameof(IIdentityService.GetSessionStampAsync) ? (object)Task.FromResult("stamp") : Task.FromResult(Guid.NewGuid()));
            var tokens = Stub<ITokenService>((_, args) => new AuthToken("token", DateTimeOffset.UtcNow.AddHours(1)));
            var appointments = Stub<ICoachAppointmentStore>((method, _) => throw new NotSupportedException(method));
            var licenses = Licenses();
            var stores = new AccountStores(Players, Coaches, Clubs, appointments, Squads, Teams, Competitions, licenses, TimeProvider.System, new NoSeasons());
            var invitations = Stub<TercerTiempo.Domain.Clubs.IClubInvitationStore>((_, _) => Task.FromResult<TercerTiempo.Domain.Clubs.ClubInvitation?>(null));
            var registration = Microsoft.Extensions.Options.Options.Create(new TercerTiempo.Application.Clubs.Invitations.RegistrationOptions { ClubInviteRequired = clubInviteRequired });
            return new RegisterHandler(identity, stores, new AppointmentRedemption(appointments, Squads, Teams, licenses), invitations, registration, Unit, tokens);
        }
        public JoinSquadAsPlayerHandler Join() => new(PlayerUser, Players, Squads, Teams, Competitions, Licenses(), Unit);
        public GeneratePlayerInviteCodeHandler Generate(ICurrentUser user) => new(user, Squads, Coaches, Players, Teams, Competitions, Injuries, Clubs, Unit, TimeProvider.System);
        public Task<SquadDetailDto> Detail(ICurrentUser user) => SquadMapper.ToDetailAsync(Squad, user, TimeProvider.System, Coaches, Players, Teams, Competitions, Injuries, Clubs, default);
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, StoreProxy>();
        ((StoreProxy)(object)stub).Call = call;
        return stub;
    }

    public class StoreProxy : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
