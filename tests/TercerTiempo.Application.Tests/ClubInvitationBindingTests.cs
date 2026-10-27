using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.Register;
using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Application.Clubs.GetMyClub;
using TercerTiempo.Application.Clubs.Invitations;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// La invitación de la beta va ligada a un club: el administrador lo elige del catálogo y en el alta no se puede cambiar.
public class ClubInvitationBindingTests
{
    private readonly Team _caudal = Team.Create("Caudal Deportivo", "Caudal", null);
    private readonly Team _caudalB = Team.Create("Caudal Deportivo 'B'", "Caudal B", null);
    private readonly Team _barcia = Team.Create("Barcia CF", "Barcia", null);
    private readonly List<Club> _clubs = [];
    private readonly List<ClubInvitation> _invitations = [];

    [Fact]
    public async Task The_admin_search_lists_clubs_with_their_teams_and_invitation()
    {
        _invitations.Add(ClubInvitation.Create("Caudal", null, DateTimeOffset.UtcNow, _caudalB.Id));

        var found = await new SearchInvitationClubsHandler(Stores(), Invitations()).Handle(new SearchInvitationClubsQuery("caud"), default);

        var club = Assert.Single(found);
        Assert.Equal("Caudal Deportivo", club.Name);
        Assert.Equal(_caudal.Id, club.TeamId);
        Assert.Equal(["Caudal Deportivo", "Caudal Deportivo 'B'"], club.Teams.Select(t => t.Name));
        Assert.Equal("available", club.Invitation!.Status);
        Assert.Null(club.RegisteredAs);
    }

    [Fact]
    public async Task The_registration_learns_the_club_from_the_code_and_cannot_switch_it()
    {
        var invitation = ClubInvitation.Create(null, null, DateTimeOffset.UtcNow, _caudalB.Id);
        _invitations.Add(invitation);

        var lookup = await new GetClubInvitationHandler(Stores(), Invitations()).Handle(new GetClubInvitationQuery(invitation.Code.ToLowerInvariant()), default);
        Assert.Equal("Caudal Deportivo", lookup.ClubName);

        var other = await Assert.ThrowsAsync<ValidationException>(() => Register(invitation.Code, "Barcia CF"));
        Assert.Equal(ErrorCode.AuthClubInviteOtherClub, Assert.Single(other.Errors).ErrorCode);
        Assert.Empty(_clubs);

        await Register(invitation.Code, "Caudal Deportivo");
        var club = Assert.Single(_clubs);
        Assert.Equal("Caudal Deportivo", club.Name);
        Assert.Equal(new[] { _caudal.Id, _caudalB.Id }.ToHashSet(), club.Teams.Select(t => t.TeamId).ToHashSet());
        Assert.Equal(club.Id, invitation.ClubId);

        await Assert.ThrowsAsync<NotFoundException>(() => new GetClubInvitationHandler(Stores(), Invitations()).Handle(new GetClubInvitationQuery(invitation.Code), default));
    }

    [Fact]
    public async Task Invitations_need_a_club_of_the_catalog_that_has_no_account_yet()
    {
        var create = new CreateClubInvitationHandler(Invitations(), Stores(), Stub<IUnitOfWork>((_, _) => Task.FromResult(1)));

        var created = await create.Handle(new CreateClubInvitationCommand(null, 30, _barcia.Id), default);
        Assert.Equal(("Barcia CF", _barcia.Id), (created.Note, created.TeamId));

        _clubs.Add(Club.Register(Guid.NewGuid(), "Barcia CF", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ConflictException>(() => create.Handle(new CreateClubInvitationCommand(null, 30, _barcia.Id), default));
        await Assert.ThrowsAsync<NotFoundException>(() => create.Handle(new CreateClubInvitationCommand(null, 30, Guid.NewGuid()), default));
        Assert.False(new CreateClubInvitationValidator().Validate(new CreateClubInvitationCommand("Barcia", 30, null)).IsValid);
    }

    [Fact]
    public async Task The_club_license_only_takes_filials_of_the_same_club()
    {
        var userId = Guid.NewGuid();
        var club = Club.Register(userId, "Caudal Deportivo", DateTimeOffset.UtcNow);
        club.AddTeam(_caudal.Id, DateTimeOffset.UtcNow);
        _clubs.Add(club);
        var user = Stub<ICurrentUser>((method, _) => method switch
        {
            "get_IsAuthenticated" => true,
            "get_UserId" => userId,
            _ => null,
        });

        var mine = await new GetMyClubHandler(user, Stores()).Handle(new GetMyClubQuery(), default);
        Assert.Equal([_caudalB.Id], mine.AvailableFilials.Select(f => f.TeamId));

        var add = new AddClubTeamHandler(user, Stores(), Stub<IUnitOfWork>((_, _) => Task.FromResult(1)));
        var other = await Assert.ThrowsAsync<DomainException>(() => add.Handle(new AddClubTeamCommand(_barcia.Id), default));
        Assert.Equal(ErrorCode.ClubTeamNotFilial, other.Code);

        var updated = await add.Handle(new AddClubTeamCommand(_caudalB.Id), default);
        Assert.Equal([_caudal.Id, _caudalB.Id], updated.Teams.Select(t => t.TeamId));
        Assert.Empty(updated.AvailableFilials);
    }

    private Task<AuthResponse> Register(string code, string club)
    {
        var stores = Stores();
        var handler = new RegisterHandler(
            Stub<IIdentityService>((method, _) => method == nameof(IIdentityService.GetSessionStampAsync) ? (object)Task.FromResult("stamp") : Task.FromResult(Guid.NewGuid())),
            stores,
            new AppointmentRedemption(stores.Appointments, stores.Squads, stores.Teams, stores.Licenses),
            Invitations(),
            Options.Create(new RegistrationOptions { ClubInviteRequired = true }),
            Stub<IUnitOfWork>((_, _) => Task.FromResult(1)),
            Stub<ITokenService>((_, _) => new AuthToken("token", DateTimeOffset.UtcNow.AddHours(1))));

        return handler.Handle(new RegisterCommand("club@example.com", "secreto123", club, AccountType.Club, null, null, code), default);
    }

    private AccountStores Stores() => new(
        Empty<TercerTiempo.Domain.Players.IPlayerStore>(),
        Empty<TercerTiempo.Domain.Coaches.ICoachStore>(),
        Stub<IClubStore>((method, args) => method switch
        {
            nameof(IClubStore.GetByNormalizedNameAsync) => Task.FromResult(_clubs.FirstOrDefault(c => c.NormalizedName == (string)args![0]!)),
            nameof(IClubStore.GetByTeamAsync) => Task.FromResult(_clubs.FirstOrDefault(c => c.Manages((Guid)args![0]!))),
            nameof(IClubStore.GetByIdAsync) => Task.FromResult(_clubs.FirstOrDefault(c => c.Id == (Guid)args![0]!)),
            nameof(IClubStore.GetByUserIdAsync) => Task.FromResult(_clubs.FirstOrDefault(c => c.UserId == (Guid)args![0]!)),
            nameof(IClubStore.Add) => Run(() => _clubs.Add((Club)args![0]!)),
            _ => Default(method),
        }),
        Empty<ICoachAppointmentStore>(),
        Empty<TercerTiempo.Domain.Squads.ISquadStore>(),
        Stub<ITeamStore>((method, args) => method switch
        {
            nameof(ITeamStore.GetAllAsync) => Task.FromResult<IReadOnlyList<Team>>([_caudalB, _caudal, _barcia]),
            nameof(ITeamStore.GetByIdAsync) => Task.FromResult(new[] { _caudal, _caudalB, _barcia }.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>(
                [.. new[] { _caudal, _caudalB, _barcia }.Where(t => ((IEnumerable<Guid>)args![0]!).Contains(t.Id))]),
            _ => Task.FromResult<IReadOnlyList<Team>>([]),
        }),
        Empty<TercerTiempo.Domain.Competitions.ICompetitionStore>(),
        Stub<TercerTiempo.Application.Licensing.ILicenseGate>((method, args) => method == nameof(TercerTiempo.Application.Licensing.ILicenseGate.EnsureForClubAsync)
            ? Task.FromResult(TercerTiempo.Domain.Licensing.ClubLicense.Start(((Club)args![0]!).Id, DateOnly.FromDateTime(DateTime.UtcNow), 30, DateTimeOffset.UtcNow))
            : throw new NotSupportedException(method)),
        TimeProvider.System,
        new NoSeasons());

    private IClubInvitationStore Invitations() => Stub<IClubInvitationStore>((method, args) => method switch
    {
        nameof(IClubInvitationStore.GetByCodeAsync) => Task.FromResult(_invitations.FirstOrDefault(i => i.Code == (string)args![0]!)),
        nameof(IClubInvitationStore.GetAllAsync) => Task.FromResult<IReadOnlyList<ClubInvitation>>(_invitations),
        nameof(IClubInvitationStore.Add) => Run(() => _invitations.Add((ClubInvitation)args![0]!)),
        _ => throw new NotSupportedException(method),
    });

    private static object? Run(Action action) { action(); return null; }

    private static object? Default(string method) => throw new NotSupportedException(method);

    // Lo que la prueba no mira: listas vacías, nulos y tareas hechas según lo que devuelva cada método.
    private static T Empty<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, EmptyProxy>();
        return proxy;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }

    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var type = targetMethod!.ReturnType;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Task<>)) return null;

            var inner = type.GetGenericArguments()[0];
            object? value = inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? Array.CreateInstance(inner.GetGenericArguments()[0], 0)
                : inner.IsValueType ? Activator.CreateInstance(inner) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
        }
    }
}
