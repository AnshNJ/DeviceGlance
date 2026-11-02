using System.Security.Cryptography;

namespace DeviceGlance.Infrastructure;

public record AppSettings
{
    public int Port { get; init; } = 8765;
    public string Token { get; init; } = RandomNumberGenerator.GetHexString(32);
    public bool AlwaysOnTop { get; init; }
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public int LowThreshold { get; init; } = 20;
    public int RearmThreshold { get; init; } = 30;
    public bool StartupInitialized { get; init; }
    public Dictionary<string, string> Renames { get; init; } = new();
    public Dictionary<string, string> HiddenDevices { get; init; } = new();
}
