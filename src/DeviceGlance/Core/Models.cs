namespace DeviceGlance.Core;

public enum BatterySource { Bluetooth, Gatt, Gamepad, Push }

public record BatteryReading(int Percent, bool? Charging, BatterySource Source, DateTimeOffset? At);

public record BluetoothDeviceInfo(
    Guid ContainerId,
    string Name,
    string Address,
    string[] Category,
    bool IsConnected,
    DateTimeOffset? LastConnectedAt,
    BatteryReading? Battery,
    ushort? VendorId,
    ushort? ProductId);

public record DeviceRow(
    string Key,
    string Name,
    string Glyph,
    string? Address,
    bool IsConnected,
    BatteryReading? Battery,
    bool BatteryIsLive,
    string Subtitle,
    bool IsStale);
