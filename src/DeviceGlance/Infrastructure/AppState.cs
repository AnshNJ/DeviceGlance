namespace DeviceGlance.Infrastructure;

/// <param name="Device">Name as reported by the sender.</param>
public record PushReport(string Device, int Battery, bool? Charging, DateTimeOffset ReceivedAt);

public record AppState
{
    /// <summary>Keyed by device name, lowercased.</summary>
    public Dictionary<string, PushReport> PushReports { get; init; } = new();

    /// <summary>Row keys that already fired a low-battery alert in the current discharge cycle.</summary>
    public HashSet<string> Alerted { get; init; } = new();
}
