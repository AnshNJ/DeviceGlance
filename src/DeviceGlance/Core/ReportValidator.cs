using System.Text.Json;
using DeviceGlance.Infrastructure;

namespace DeviceGlance.Core;

public static class ReportValidator
{
    public static bool TryParse(JsonElement body, DateTimeOffset now, out PushReport? report, out string? error)
    {
        report = null;

        if (body.ValueKind != JsonValueKind.Object)
        {
            error = "body invalid";
            return false;
        }

        if (!body.TryGetProperty("device", out var dev) || dev.ValueKind != JsonValueKind.String
            || dev.GetString()?.Trim() is not { Length: >= 1 and <= 64 } device)
        {
            error = "device invalid";
            return false;
        }

        if (!body.TryGetProperty("battery", out var bat) || bat.ValueKind != JsonValueKind.Number
            || !bat.TryGetDouble(out var level) || double.IsNaN(level) || level < 0 || level > 100)
        {
            error = "battery invalid";
            return false;
        }

        bool? charging = null;
        if (body.TryGetProperty("charging", out var ch) && ch.ValueKind != JsonValueKind.Null)
        {
            if (ch.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = "charging invalid";
                return false;
            }
            charging = ch.GetBoolean();
        }

        report = new PushReport(device, (int)Math.Round(level, MidpointRounding.AwayFromZero), charging, now);
        error = null;
        return true;
    }
}
