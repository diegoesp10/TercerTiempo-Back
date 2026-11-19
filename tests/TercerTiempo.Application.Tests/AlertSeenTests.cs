using TercerTiempo.Application.Alerts;
using TercerTiempo.Domain.Alerts;

namespace TercerTiempo.Application.Tests;

// La campanita como en las redes sociales: lo visto deja de contar y pasa a «Anteriores»; lo pulsado queda además leído.
public class AlertSeenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Player = Guid.NewGuid();

    private static Alert New(int minutesAgo, string key) =>
        Alert.ForPlayer(Player, AlertKind.TrainingCall, key, "Te citan a entrenar", "Martes a las 19:00.", "/mis-entrenamientos", null, Now.AddMinutes(-minutesAgo));

    [Fact]
    public void Seeing_an_alert_takes_it_out_of_the_count_without_reading_it()
    {
        var alert = New(5, "training-call:a");
        Assert.True(alert.IsNew);

        alert.MarkSeen(Now);

        Assert.False(alert.IsNew);
        Assert.False(alert.IsRead);
        Assert.Equal(0, ClubAlerts.ToDto([alert]).Unread);
    }

    [Fact]
    public void Opening_an_alert_marks_it_read_and_seen()
    {
        var alert = New(5, "training-call:a");

        alert.MarkRead(Now);

        Assert.True(alert.IsSeen);
        Assert.True(alert.IsRead);
        Assert.False(alert.IsNew);
    }

    [Fact]
    public void Seeing_again_keeps_the_first_moment()
    {
        var alert = New(5, "training-call:a");
        alert.MarkSeen(Now);
        alert.MarkRead(Now.AddHours(1));

        Assert.Equal(Now, alert.SeenAt);
    }

    [Fact]
    public void New_alerts_all_come_and_only_the_latest_earlier_ones_follow()
    {
        var fresh = Enumerable.Range(0, 3).Select(i => New(i, $"new:{i}")).ToList();
        var earlier = Enumerable.Range(10, ClubAlerts.EarlierShown + 5).Select(i => New(i, $"old:{i}")).ToList();
        foreach (var alert in earlier) alert.MarkSeen(Now);
        var resolved = New(1, "resolved");
        resolved.Resolve(Now);

        var dto = ClubAlerts.ToDto([.. earlier, resolved, .. fresh]);

        Assert.Equal(3, dto.Unread);
        Assert.Equal(3, dto.Alerts.Count(a => !a.Seen && !a.Resolved));
        Assert.Equal(ClubAlerts.EarlierShown, dto.Alerts.Count(a => a.Seen || a.Resolved));
        Assert.Contains(dto.Alerts, a => a.Resolved);
        Assert.Equal(dto.Alerts.OrderByDescending(a => a.CreatedAt).Select(a => a.Id), dto.Alerts.Select(a => a.Id));
    }
}
