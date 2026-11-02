using DeviceGlance.Core;
using DeviceGlance.Infrastructure;
using Windows.Devices.Power;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace DeviceGlance.Sources;

/// <summary>Battery of game controllers (Xbox pads expose none through PnP).</summary>
public sealed class GamepadBatteryReader : IDisposable
{
    /// <summary>Raised when a controller appears or disappears.</summary>
    public event Action? ControllersChanged;

    public void Start()
    {
        RawGameController.RawGameControllerAdded += OnChanged;
        RawGameController.RawGameControllerRemoved += OnChanged;
        // The list fills asynchronously; touch it once so the Added events start flowing.
        try { _ = RawGameController.RawGameControllers.Count; }
        catch (Exception ex) { Log.Error("RawGameControllers enumeration failed", ex); }
    }

    public IReadOnlyList<(ushort Vid, ushort Pid, BatteryReading Reading)> Read()
    {
        var result = new List<(ushort, ushort, BatteryReading)>();
        try
        {
            foreach (var pad in RawGameController.RawGameControllers)
            {
                var report = pad.TryGetBatteryReport();
                if (report?.FullChargeCapacityInMilliwattHours is not { } full || full <= 0
                    || report.RemainingCapacityInMilliwattHours is not { } remaining)
                    continue;

                var percent = Math.Clamp((int)Math.Round(remaining * 100.0 / full), 0, 100);
                result.Add((pad.HardwareVendorId, pad.HardwareProductId,
                    new BatteryReading(percent, report.Status == BatteryStatus.Charging, BatterySource.Gamepad, DateTimeOffset.Now)));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Gamepad battery read failed", ex);
        }
        return result;
    }

    private void OnChanged(object? sender, RawGameController controller) => ControllersChanged?.Invoke();

    public void Dispose()
    {
        RawGameController.RawGameControllerAdded -= OnChanged;
        RawGameController.RawGameControllerRemoved -= OnChanged;
    }
}
