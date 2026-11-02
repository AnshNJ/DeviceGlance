using DeviceGlance.Infrastructure;

namespace DeviceGlance.Core;

public static class DeviceMerger
{
    public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(13);

    public const string GlyphHeadphone = "\uE7F6";
    public const string GlyphSpeakers = "\uE7F5";
    public const string GlyphGame = "\uE7FC";
    public const string GlyphKeyboard = "\uE765";
    public const string GlyphMouse = "\uE962";
    public const string GlyphTablet = "\uE70A";
    public const string GlyphPhone = "\uE8EA";
    public const string GlyphBluetooth = "\uE702";

    public static List<DeviceRow> Merge(
        IReadOnlyList<BluetoothDeviceInfo> bt,
        IReadOnlyDictionary<string, PushReport> push,
        DateTimeOffset now)
    {
        var nameCounts = new Dictionary<string, int>();
        foreach (var d in bt)
        {
            var k = d.Name.ToLowerInvariant();
            nameCounts[k] = nameCounts.GetValueOrDefault(k) + 1;
        }

        var rows = new List<DeviceRow>(bt.Count + push.Count);
        var consumed = new HashSet<string>();

        foreach (var d in bt)
        {
            var nameKey = d.Name.ToLowerInvariant();
            PushReport? report = null;
            if (nameCounts[nameKey] == 1 && push.TryGetValue(nameKey, out var match))
            {
                report = match;
                consumed.Add(nameKey);   // a unique name match never shows up as a duplicate standalone row
            }

            BatteryReading? battery;
            bool live;

            if (d.IsConnected && d.Battery is not null)
            {
                battery = d.Battery;
                live = true;
            }
            else if (report is not null)
            {
                battery = ToReading(report);
                live = IsLive(report, now);
            }
            else
            {
                battery = d.Battery;
                live = false;
            }

            rows.Add(new DeviceRow(
                d.ContainerId.ToString(), d.Name, GlyphFor(d.Category), d.Address,
                d.IsConnected, battery, live, SubtitleFor(d, battery, now), IsStalePush(battery, now)));
        }

        foreach (var (key, report) in push)
        {
            if (consumed.Contains(key))
                continue;
            var stale = now - report.ReceivedAt >= StaleAfter;
            rows.Add(new DeviceRow(
                "push:" + key, report.Device, GlyphTablet, null,
                false, ToReading(report), IsLive(report, now),
                (stale ? "Stale — updated " : "Updated ") + Rel(report.ReceivedAt, now), stale));
        }

        rows.Sort(Compare);
        return rows;
    }

    public static List<DeviceRow> Customize(
        IReadOnlyList<DeviceRow> rows,
        IReadOnlyDictionary<string, string> renames,
        IReadOnlyDictionary<string, string> hidden)
    {
        var result = new List<DeviceRow>(rows.Count);
        foreach (var r in rows)
        {
            if (hidden.ContainsKey(r.Key)) continue;
            result.Add(renames.TryGetValue(r.Key, out var name) ? r with { Name = name } : r);
        }
        result.Sort(Compare);
        return result;
    }

    public static string GlyphFor(string[] categories)
    {
        foreach (var c in categories)
        {
            if (c.StartsWith("Communication.Headset", StringComparison.OrdinalIgnoreCase)) return GlyphHeadphone;
            if (c.StartsWith("Audio", StringComparison.OrdinalIgnoreCase)) return GlyphSpeakers;
            if (c.StartsWith("Input.Gaming", StringComparison.OrdinalIgnoreCase)) return GlyphGame;
            if (c.StartsWith("Input.Keyboard", StringComparison.OrdinalIgnoreCase)) return GlyphKeyboard;
            if (c.StartsWith("Input.Mouse", StringComparison.OrdinalIgnoreCase)) return GlyphMouse;
            if (c.StartsWith("Computer.Handheld", StringComparison.OrdinalIgnoreCase)) return GlyphTablet;
            if (c.StartsWith("Phone", StringComparison.OrdinalIgnoreCase)
                || c.StartsWith("Communication.Phone", StringComparison.OrdinalIgnoreCase)) return GlyphPhone;
        }
        return GlyphBluetooth;
    }

    public static string Rel(DateTimeOffset then, DateTimeOffset now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
    }

    private static BatteryReading ToReading(PushReport r) =>
        new(r.Battery, r.Charging, BatterySource.Push, r.ReceivedAt);

    private static bool IsLive(PushReport r, DateTimeOffset now) => now - r.ReceivedAt < LiveWindow;

    private static bool IsStalePush(BatteryReading? shown, DateTimeOffset now) =>
        shown is { Source: BatterySource.Push, At: { } at } && now - at >= StaleAfter;

    private static string SubtitleFor(BluetoothDeviceInfo d, BatteryReading? shown, DateTimeOffset now)
    {
        if (shown is { Source: BatterySource.Push, At: { } staleAt } && now - staleAt >= StaleAfter)
            return "Stale — updated " + Rel(staleAt, now);
        if (d.IsConnected) return "Connected";
        if (shown is { Source: BatterySource.Push, At: { } at }) return "Updated " + Rel(at, now);
        if (d.LastConnectedAt is { } last) return "Last connected " + Rel(last, now);
        return "Not connected";
    }

    private static int Compare(DeviceRow a, DeviceRow b)
    {
        int c = b.IsConnected.CompareTo(a.IsConnected);
        if (c != 0) return c;
        c = (b.Battery is not null).CompareTo(a.Battery is not null);
        if (c != 0) return c;
        if (a.Battery is not null && b.Battery is not null)
        {
            c = a.Battery.Percent.CompareTo(b.Battery.Percent);
            if (c != 0) return c;
        }
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }
}
