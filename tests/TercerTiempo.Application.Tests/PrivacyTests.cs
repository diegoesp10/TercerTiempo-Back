using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Privacy;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Privacy;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Tests;

public class PrivacyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string _visitor = Guid.NewGuid().ToString();
    private readonly List<CookieConsent> _consents = [];
    private readonly List<AnalyticsEvent> _stored = [];
    private CookiePolicy _policy = CookiePolicy.Create("2026-10-24", Now);

    [Fact]
    public void Accepting_or_rejecting_everything_decides_analytics_and_the_visitor_must_be_a_uuid()
    {
        Assert.True(CookieConsent.Record(_visitor, null, false, ConsentAction.AcceptAll, "v1", Now).Analytics);
        Assert.False(CookieConsent.Record(_visitor, null, true, ConsentAction.RejectAll, "v1", Now).Analytics);
        Assert.True(CookieConsent.Record(_visitor, null, true, ConsentAction.Custom, "v1", Now).Analytics);
        Assert.Throws<DomainException>(() => CookieConsent.Record("no-es-un-uuid", null, true, ConsentAction.Custom, "v1", Now));
    }

    [Fact]
    public async Task Nothing_is_stored_without_analytics_consent_for_the_current_policy()
    {
        Assert.Equal(0, (await Track(new Anonymous())).Stored);

        _consents.Add(CookieConsent.Record(_visitor, null, false, ConsentAction.RejectAll, _policy.Version, Now.AddMinutes(-5)));
        Assert.Equal(0, (await Track(new Anonymous())).Stored);

        _consents.Add(CookieConsent.Record(_visitor, null, true, ConsentAction.AcceptAll, _policy.Version, Now.AddMinutes(-1)));
        Assert.Equal(2, (await Track(new Anonymous())).Stored);

        // Una versión nueva de la política anula el consentimiento anterior.
        _policy = CookiePolicy.Create("2026-11-02", Now);
        Assert.Equal(0, (await Track(new Anonymous())).Stored);
        Assert.Equal(2, _stored.Count);
    }

    [Fact]
    public async Task Each_event_knows_who_acted_and_whose_page_it_was()
    {
        _consents.Add(CookieConsent.Record(_visitor, null, true, ConsentAction.AcceptAll, _policy.Version, Now.AddMinutes(-1)));
        var ownClub = Club.Register(Guid.NewGuid(), "Caudal Deportivo", Now);
        var teamId = Guid.NewGuid();
        var visitedClub = Club.Register(Guid.NewGuid(), "CD Lealtad", Now);
        visitedClub.AddTeam(teamId, Now);

        await Track(new ClubUser(ownClub.Id), teamId, visitedClub);

        var view = _stored.First(e => e.Type == AnalyticsEventType.PageView);
        Assert.Equal((AnalyticsActor.Club, ownClub.Id, visitedClub.Id), (view.Actor, view.ActorClubId, view.SubjectClubId));
        Assert.Equal("/equipos/x", view.Path);
        Assert.Equal("caudal", _stored.Single(e => e.Type == AnalyticsEventType.Search).Label);
    }

    private Task<TrackResultDto> Track(ICurrentUser user, Guid? teamId = null, Club? teamClub = null)
    {
        var handler = new TrackEventsHandler(
            Stub<ICookieStore>((method, args) => method switch
            {
                nameof(ICookieStore.GetPolicyAsync) => Task.FromResult<CookiePolicy?>(_policy),
                nameof(ICookieStore.GetLatestConsentAsync) => Task.FromResult(_consents.Where(c => c.VisitorId == (string)args![0]!).OrderByDescending(c => c.CreatedAt).FirstOrDefault()),
                _ => throw new NotSupportedException(method),
            }),
            Stub<IAnalyticsStore>((method, args) => method switch
            {
                nameof(IAnalyticsStore.AddRange) => Run(() => _stored.AddRange((IEnumerable<AnalyticsEvent>)args![0]!)),
                nameof(IAnalyticsStore.PurgeOlderThanAsync) => Task.FromResult(0),
                _ => throw new NotSupportedException(method),
            }),
            user,
            Stub<IClubStore>((method, args) => method == nameof(IClubStore.GetByTeamAsync)
                ? Task.FromResult((Guid)args![0]! == teamId ? teamClub : null)
                : throw new NotSupportedException(method)),
            Stub<ISquadStore>((method, _) => throw new NotSupportedException(method)),
            Stub<IUnitOfWork>((_, _) => Task.FromResult(1)),
            new FixedClock(Now));

        return handler.Handle(new TrackEventsCommand(_visitor, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
        [
            new TrackEventDto(AnalyticsEventType.PageView, "/equipos/x?tab=1", "team", null, teamId is null ? null : AnalyticsEntity.Team, teamId, Now.AddSeconds(-5)),
            new TrackEventDto(AnalyticsEventType.Search, "/equipos", "teams", "  caudal ", null, null, null),
        ]), default);
    }

    private static object? Run(Action action) { action(); return null; }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private class Anonymous : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public virtual AccountType? AccountType => null;
        public Guid? PlayerId => null;
        public Guid? CoachId => null;
        public virtual Guid? ClubId => null;
    }

    private sealed class ClubUser(Guid clubId) : Anonymous
    {
        public override AccountType? AccountType => Abstractions.Identity.AccountType.Club;
        public override Guid? ClubId => clubId;
    }
}
