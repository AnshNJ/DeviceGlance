using DeviceGlance.Core;
using DeviceGlance.Infrastructure;

namespace DeviceGlance.Tests;

public class DeviceMergerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, PushReport> NoPush = new Dictionary<string, PushReport>();

    private static BluetoothDeviceInfo Bt(
        string name, bool connected = false, int? percent = null, string category = "Audio.Speakers",
        DateTimeOffset? lastConnected = null, BatterySource source = BatterySource.Bluetooth) =>
        new(Guid.NewGuid(), name, "00:00", [category], connected, lastConnected,
            percent is { } p ? new BatteryReading(p, null, source, connected ? Now : lastConnected) : null,
            null, null);

    private static Dictionary<string, PushReport> Push(string name, int percent, TimeSpan age) =>
        new() { [name.ToLowerInvariant()] = new PushReport(name, percent, null, Now - age) };

    [Fact]
    public void Connected_device_with_bt_reading_beats_push()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("iPad", connected: true, percent: 80)], Push("iPad", 10, TimeSpan.FromMinutes(1)), Now));
        Assert.Equal(80, row.Battery!.Percent);
        Assert.Equal(BatterySource.Bluetooth, row.Battery.Source);
        Assert.True(row.BatteryIsLive);
        Assert.Equal("Connected", row.Subtitle);
    }

    [Fact]
    public void Disconnected_device_shows_matching_push_case_insensitively()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("iPad", percent: 50, category: "Computer.Handheld")], Push("ipad", 15, TimeSpan.FromMinutes(2)), Now));
        Assert.Equal(15, row.Battery!.Percent);
        Assert.Equal(BatterySource.Push, row.Battery.Source);
        Assert.True(row.BatteryIsLive);
        Assert.Equal("Updated 2 min ago", row.Subtitle);
    }

    [Fact]
    public void Push_older_than_fifteen_minutes_is_not_live()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("iPad")], Push("iPad", 15, TimeSpan.FromMinutes(15)), Now));
        Assert.Equal(15, row.Battery!.Percent);
        Assert.False(row.BatteryIsLive);
    }

    [Fact]
    public void Ambiguous_name_makes_push_a_standalone_row()
    {
        var rows = DeviceMerger.Merge(
            [Bt("Xbox Wireless Controller"), Bt("Xbox Wireless Controller")],
            Push("Xbox Wireless Controller", 40, TimeSpan.Zero), Now);

        Assert.Equal(3, rows.Count);
        var standalone = Assert.Single(rows, r => r.Key.StartsWith("push:"));
        Assert.Equal(DeviceMerger.GlyphTablet, standalone.Glyph);
        Assert.False(standalone.IsConnected);
        Assert.Equal(40, standalone.Battery!.Percent);
        Assert.All(rows.Where(r => r != standalone), r => Assert.Null(r.Battery));
    }

    [Fact]
    public void Push_for_unknown_device_is_a_standalone_row()
    {
        var rows = DeviceMerger.Merge([], Push("iPad", 70, TimeSpan.FromHours(3)), Now);
        var row = Assert.Single(rows);
        Assert.Equal("push:ipad", row.Key);
        Assert.Equal("Updated 3 h ago", row.Subtitle);
    }

    [Fact]
    public void Device_without_data_has_no_battery_and_is_not_connected()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("Mystery")], NoPush, Now));
        Assert.Null(row.Battery);
        Assert.Equal("Not connected", row.Subtitle);
    }

    [Fact]
    public void Disconnected_device_shows_cached_reading_not_live_with_last_connected()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("Buds", percent: 70, lastConnected: Now - TimeSpan.FromDays(2))], NoPush, Now));
        Assert.Equal(70, row.Battery!.Percent);
        Assert.False(row.BatteryIsLive);
        Assert.Equal("Last connected 2 d ago", row.Subtitle);
    }

    [Fact]
    public void Sorted_connected_first_then_battery_ascending_then_no_battery_then_name()
    {
        var rows = DeviceMerger.Merge(
        [
            Bt("none-b"),
            Bt("disc-30", percent: 30),
            Bt("conn-90", connected: true, percent: 90),
            Bt("none-a"),
            Bt("disc-10", percent: 10),
            Bt("conn-20", connected: true, percent: 20),
        ], NoPush, Now);

        Assert.Equal(["conn-20", "conn-90", "disc-10", "disc-30", "none-a", "none-b"], rows.Select(r => r.Name));
    }

    [Fact]
    public void Push_battery_at_stale_threshold_is_stale_even_when_connected()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("iPad", connected: true)], Push("iPad", 50, TimeSpan.FromHours(13)), Now));
        Assert.True(row.IsStale);
        Assert.Equal("Stale — updated 13 h ago", row.Subtitle);
    }

    [Fact]
    public void Push_battery_just_inside_threshold_is_not_stale()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("iPad", connected: true)], Push("iPad", 50, TimeSpan.FromHours(12)), Now));
        Assert.False(row.IsStale);
        Assert.Equal("Connected", row.Subtitle);
    }

    [Fact]
    public void Push_only_row_reports_stale()
    {
        var row = Assert.Single(DeviceMerger.Merge([], Push("iPad", 50, TimeSpan.FromDays(2)), Now));
        Assert.True(row.IsStale);
        Assert.Equal("Stale — updated 2 d ago", row.Subtitle);
    }

    [Fact]
    public void Old_push_is_ignored_for_staleness_when_bt_reading_wins()
    {
        var row = Assert.Single(DeviceMerger.Merge([Bt("iPad", connected: true, percent: 80)], Push("iPad", 10, TimeSpan.FromDays(2)), Now));
        Assert.False(row.IsStale);
        Assert.Equal("Connected", row.Subtitle);
    }

    [Fact]
    public void Customize_removes_hidden_and_renames()
    {
        var rows = DeviceMerger.Merge([Bt("A"), Bt("B"), Bt("C")], NoPush, Now);
        var a = rows.Single(r => r.Name == "A").Key;
        var b = rows.Single(r => r.Name == "B").Key;

        var result = DeviceMerger.Customize(rows,
            new Dictionary<string, string> { [b] = "Bee" },
            new Dictionary<string, string> { [a] = "A" });

        Assert.Equal(["Bee", "C"], result.Select(r => r.Name));
    }

    [Fact]
    public void Customize_resorts_by_new_name()
    {
        var rows = DeviceMerger.Merge([Bt("Alpha"), Bt("Bravo")], NoPush, Now);
        var bravo = rows.Single(r => r.Name == "Bravo").Key;

        var result = DeviceMerger.Customize(rows, new Dictionary<string, string> { [bravo] = "AAA" }, new Dictionary<string, string>());

        Assert.Equal(["AAA", "Alpha"], result.Select(r => r.Name));
    }

    [Fact]
    public void Rename_does_not_break_push_matching()
    {
        var merged = DeviceMerger.Merge([Bt("iPad")], Push("iPad", 42, TimeSpan.FromMinutes(1)), Now);
        var key = Assert.Single(merged).Key;

        var row = Assert.Single(DeviceMerger.Customize(merged, new Dictionary<string, string> { [key] = "Tablet" }, new Dictionary<string, string>()));

        Assert.Equal("Tablet", row.Name);
        Assert.Equal(42, row.Battery!.Percent);
        Assert.Equal(BatterySource.Push, row.Battery.Source);
    }

    [Theory]
    [InlineData("Communication.Headset.Bluetooth", "\uE7F6")]
    [InlineData("Audio.Speakers", "\uE7F5")]
    [InlineData("Input.Gaming", "\uE7FC")]
    [InlineData("Computer.Handheld", "\uE70A")]
    [InlineData("Whatever", "\uE702")]
    public void Glyph_follows_category(string category, string glyph) =>
        Assert.Equal(glyph, DeviceMerger.GlyphFor([category]));
}
