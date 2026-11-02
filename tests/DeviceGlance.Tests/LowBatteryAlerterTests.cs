using DeviceGlance.Core;

namespace DeviceGlance.Tests;

public class LowBatteryAlerterTests
{
    private static LowBatteryAlerter Create(HashSet<string>? set = null) => new(set ?? new(), 20, 30);

    [Fact]
    public void Above_threshold_does_not_fire() => Assert.False(Create().Evaluate("a", 25, true));

    [Fact]
    public void Fires_once_at_threshold_then_stays_quiet_through_hysteresis_band()
    {
        var a = Create();
        Assert.True(a.Evaluate("a", 20, true));
        Assert.False(a.Evaluate("a", 15, true));
        Assert.False(a.Evaluate("a", 30, true));   // not re-armed yet
        Assert.False(a.Evaluate("a", 19, true));   // still alerted from the first cycle
    }

    [Fact]
    public void Rearms_above_rearm_threshold()
    {
        var a = Create();
        Assert.True(a.Evaluate("a", 20, true));
        Assert.False(a.Evaluate("a", 31, true));
        Assert.True(a.Evaluate("a", 19, true));
    }

    [Fact]
    public void Cached_reading_never_fires_or_rearms()
    {
        var set = new HashSet<string> { "a" };
        var a = Create(set);
        Assert.False(a.Evaluate("b", 5, false));
        Assert.False(a.Evaluate("a", 90, false));
        Assert.Contains("a", set);
    }

    [Fact]
    public void Alerted_set_survives_restart()
    {
        var set = new HashSet<string> { "a" };
        Assert.False(Create(set).Evaluate("a", 18, true));
    }

    [Fact]
    public void Keys_are_independent()
    {
        var a = Create();
        Assert.True(a.Evaluate("a", 10, true));
        Assert.True(a.Evaluate("b", 10, true));
    }

    [Fact]
    public void Notifies_only_when_the_set_changes()
    {
        var a = Create();
        var changes = 0;
        a.SetChanged += () => changes++;
        a.Evaluate("a", 10, true);   // add
        a.Evaluate("a", 10, true);   // no change
        a.Evaluate("a", 25, true);   // no change (band)
        a.Evaluate("a", 50, true);   // remove
        Assert.Equal(2, changes);
    }
}
