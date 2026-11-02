namespace DeviceGlance.Core;

/// <summary>
/// Hysteresis state machine over a persisted set of already-alerted keys: fires once at or below the
/// low threshold, re-arms only after the battery rises above the re-arm threshold.
/// </summary>
public sealed class LowBatteryAlerter(HashSet<string> alerted, int lowThreshold, int rearmThreshold)
{
    /// <summary>Raised after <c>alerted</c> was mutated, so the owner can persist it.</summary>
    public event Action? SetChanged;

    /// <returns>True when an alert must be shown for <paramref name="key"/>.</returns>
    public bool Evaluate(string key, int percent, bool isLive)
    {
        if (!isLive)
            return false;

        if (percent <= lowThreshold)
        {
            if (!alerted.Add(key))
                return false;
            SetChanged?.Invoke();
            return true;
        }

        if (percent > rearmThreshold && alerted.Remove(key))
            SetChanged?.Invoke();
        return false;
    }
}
