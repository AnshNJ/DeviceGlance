using DeviceGlance.Core;
using DeviceGlance.Infrastructure;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace DeviceGlance.Sources;

/// <summary>
/// Tracks every paired Bluetooth device (Classic and LE, merged per container) with connection state
/// and battery. Raises <see cref="Snapshot"/> with the complete list after every refresh.
/// </summary>
public sealed class BluetoothMonitor : IDisposable
{
    private const string KeyConnected = "System.Devices.Aep.IsConnected";
    private const string KeyContainer = "System.Devices.Aep.ContainerId";
    private const string KeyAddress = "System.Devices.Aep.DeviceAddress";
    private const string KeyCategory = "System.Devices.Aep.Category";

    // Hands-Free Profile battery level, observed on the AG node of the device container.
    private const string KeyBattery = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string KeyVendor = "System.DeviceInterface.Bluetooth.VendorId";
    private const string KeyProduct = "System.DeviceInterface.Bluetooth.ProductId";
    private const string KeyLastConnected = "System.DeviceInterface.Bluetooth.LastConnectedTime";

    private static readonly string[] AepProps = [KeyConnected, KeyContainer, KeyAddress, KeyCategory];
    private static readonly string[] PnpProps = [KeyBattery, KeyVendor, KeyProduct, KeyLastConnected];

    private static readonly TimeSpan PerDeviceRefreshDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FullRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WatcherRestartDelay = TimeSpan.FromSeconds(5);

    private sealed record Aep(string Id, bool IsLe, string Name, bool Connected, Guid ContainerId, string Address, string[] Category);

    private readonly GamepadBatteryReader _gamepads;
    private readonly object _gate = new();
    private readonly Dictionary<string, (DeviceInformation Info, bool IsLe)> _entries = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _pending = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<Guid, BluetoothDeviceInfo> _cache = new();   // guarded by _refreshLock
    private readonly HashSet<Guid> _gattLogged = new();                       // guarded by itself
    private readonly CancellationTokenSource _cts = new();
    private readonly bool[] _enumerated = new bool[2];                        // [0]=Classic, [1]=LE
    private DeviceWatcher? _classic, _le;
    private bool _disposed;

    public event Action<IReadOnlyList<BluetoothDeviceInfo>>? Snapshot;

    public BluetoothMonitor(GamepadBatteryReader gamepads)
    {
        _gamepads = gamepads;
        _gamepads.ControllersChanged += OnControllersChanged;
    }

    public void Start()
    {
        _classic = CreateWatcher(le: false);
        _le = CreateWatcher(le: true);
        StartWatcher(_classic);
        StartWatcher(_le);
        _ = Task.Run(PeriodicLoopAsync);
    }

    public async Task RefreshAllAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            List<Aep> all;
            lock (_gate)
                all = _entries.Values.Select(e => Extract(e.Info, e.IsLe)).ToList();

            var groups = all.Where(a => a.ContainerId != Guid.Empty).GroupBy(a => a.ContainerId).ToList();
            var built = await Task.WhenAll(groups.Select(g => BuildAsync(g.Key, g.ToList())));

            _cache.Clear();
            foreach (var d in built)
                _cache[d.ContainerId] = d;
            Publish();
        }
        catch (Exception ex)
        {
            Log.Error("Full Bluetooth refresh failed", ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshContainerAsync(Guid cid)
    {
        await _refreshLock.WaitAsync();
        try
        {
            List<Aep> entries;
            lock (_gate)
                entries = _entries.Values.Select(e => Extract(e.Info, e.IsLe)).Where(a => a.ContainerId == cid).ToList();

            if (entries.Count == 0)
                _cache.Remove(cid);
            else
                _cache[cid] = await BuildAsync(cid, entries);
            Publish();
        }
        catch (Exception ex)
        {
            Log.Error($"Bluetooth refresh failed for container {cid}", ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void Publish()
    {
        var list = ApplyGamepadReadings(_cache.Values.ToList(), _gamepads.Read());
        Snapshot?.Invoke(list);
    }

    /// <summary>Gives a gamepad reading to the one connected gaming device without a battery and with equal VID/PID.</summary>
    internal static List<BluetoothDeviceInfo> ApplyGamepadReadings(
        List<BluetoothDeviceInfo> devices, IReadOnlyList<(ushort Vid, ushort Pid, BatteryReading Reading)> readings)
    {
        foreach (var (vid, pid, reading) in readings)
        {
            var matches = devices.Where(d =>
                d.Battery is null && d.IsConnected && d.VendorId == vid && d.ProductId == pid
                && d.Category.Any(c => c.StartsWith("Input.Gaming", StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count != 1)
                continue;
            var i = devices.IndexOf(matches[0]);
            devices[i] = matches[0] with { Battery = reading };
        }
        return devices;
    }

    private async Task<BluetoothDeviceInfo> BuildAsync(Guid cid, IReadOnlyList<Aep> entries)
    {
        var first = entries[0];
        var connected = entries.Any(e => e.Connected);
        var name = entries.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? first.Address;
        var now = DateTimeOffset.Now;

        int? percent = null;
        BatterySource source = BatterySource.Bluetooth;
        ushort? vid = null, pid = null;
        DateTimeOffset? lastConnected = null;

        try
        {
            var nodes = await DeviceInformation.FindAllAsync(
                $"System.Devices.ContainerId:=\"{{{cid}}}\"", PnpProps, DeviceInformationKind.Device);

            foreach (var node in nodes)
            {
                if (percent is null && node.Properties.TryGetValue(KeyBattery, out var b) && b is not null
                    && TryToInt(b, out var level))
                    percent = Math.Clamp(level, 0, 100);

                if (node.Id.Contains("\\Dev_", StringComparison.OrdinalIgnoreCase))
                {
                    if (node.Properties.TryGetValue(KeyVendor, out var v) && TryToInt(v, out var vi) && vi is >= 0 and <= ushort.MaxValue)
                        vid = (ushort)vi;
                    if (node.Properties.TryGetValue(KeyProduct, out var p) && TryToInt(p, out var pi) && pi is >= 0 and <= ushort.MaxValue)
                        pid = (ushort)pi;
                    if (node.Properties.TryGetValue(KeyLastConnected, out var t) && t is DateTimeOffset dto && dto.Year > 2000)
                        lastConnected = dto;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"PnP query failed for {name} ({cid})", ex);
        }

        if (percent is null && entries.FirstOrDefault(e => e.IsLe && e.Connected) is { } le)
        {
            percent = await ReadGattBatteryAsync(cid, name, le.Id);
            source = BatterySource.Gatt;
        }

        BatteryReading? battery = percent is { } pc
            ? new BatteryReading(pc, null, source, connected ? now : lastConnected)
            : null;

        return new BluetoothDeviceInfo(cid, name, first.Address, first.Category, connected, lastConnected, battery, vid, pid);
    }

    /// <remarks>Only ever called for a connected LE entry; touching a disconnected one would force a connection.</remarks>
    private async Task<int?> ReadGattBatteryAsync(Guid cid, string name, string leId)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(leId);
            if (device is null)
                return null;

            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                return LogGattOnce(cid, name, $"battery service status {services.Status}");

            using var service = services.Services[0];
            var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
                return LogGattOnce(cid, name, $"battery characteristic status {chars.Status}");

            var read = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
            if (read.Status != GattCommunicationStatus.Success)
                return LogGattOnce(cid, name, $"battery read status {read.Status}");

            return Math.Clamp((int)DataReader.FromBuffer(read.Value).ReadByte(), 0, 100);
        }
        catch (Exception ex)
        {
            if (FirstGattLog(cid))
                Log.Error($"GATT battery read failed for {name} ({cid})", ex);
            return null;
        }
    }

    private int? LogGattOnce(Guid cid, string name, string what)
    {
        if (FirstGattLog(cid))
            Log.Error($"GATT: {what} for {name} ({cid})");
        return null;
    }

    private bool FirstGattLog(Guid cid)
    {
        lock (_gattLogged)
            return _gattLogged.Add(cid);
    }

    private DeviceWatcher CreateWatcher(bool le)
    {
        var selector = le
            ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)
            : BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var watcher = DeviceInformation.CreateWatcher(selector, AepProps, DeviceInformationKind.AssociationEndpoint);
        var slot = le ? 1 : 0;

        watcher.Added += (_, info) =>
        {
            Guid cid;
            lock (_gate)
            {
                _entries[info.Id] = (info, le);
                cid = ContainerOf(info);
            }
            OnEntryChanged(slot, cid);
        };
        watcher.Updated += (_, update) =>
        {
            Guid cid = Guid.Empty;
            lock (_gate)
            {
                if (_entries.TryGetValue(update.Id, out var e))
                {
                    e.Info.Update(update);
                    cid = ContainerOf(e.Info);
                }
            }
            OnEntryChanged(slot, cid);
        };
        watcher.Removed += (_, update) =>
        {
            Guid cid = Guid.Empty;
            lock (_gate)
            {
                if (_entries.Remove(update.Id, out var e))
                    cid = ContainerOf(e.Info);
            }
            OnEntryChanged(slot, cid);
        };
        watcher.EnumerationCompleted += (_, _) =>
        {
            _enumerated[slot] = true;
            _ = RefreshAllAsync();
        };
        watcher.Stopped += (_, _) =>
        {
            if (!_disposed)
                _ = RestartWatcherAsync(watcher, slot);
        };
        return watcher;
    }

    private static Guid ContainerOf(DeviceInformation info) =>
        info.Properties.TryGetValue(KeyContainer, out var g) && g is Guid cid ? cid : Guid.Empty;

    private static Aep Extract(DeviceInformation info, bool le)
    {
        var p = info.Properties;
        var connected = p.TryGetValue(KeyConnected, out var c) && c is true;
        var address = p.TryGetValue(KeyAddress, out var a) ? a as string ?? "" : "";
        string[] category = p.TryGetValue(KeyCategory, out var k)
            ? k switch
            {
                string[] s => s,
                IEnumerable<object> e => e.Select(x => x?.ToString() ?? "").ToArray(),
                _ => [],
            }
            : [];
        return new Aep(info.Id, le, info.Name, connected, ContainerOf(info), address, category);
    }

    private static bool TryToInt(object? value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private void OnEntryChanged(int slot, Guid cid)
    {
        // Until the initial enumeration finishes, the full refresh at EnumerationCompleted covers everything.
        if (cid == Guid.Empty || !_enumerated[slot] || _disposed)
            return;
        ScheduleRefresh(cid, PerDeviceRefreshDelay);
    }

    /// <summary>Debounced per-container refresh; Hands-Free battery shows up a few seconds after connect.</summary>
    private void ScheduleRefresh(Guid cid, TimeSpan delay)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        lock (_gate)
        {
            if (_pending.Remove(cid, out var old))
                old.Cancel();
            _pending[cid] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                lock (_gate)
                {
                    if (_pending.TryGetValue(cid, out var cur) && ReferenceEquals(cur, cts))
                        _pending.Remove(cid);
                }
                await RefreshContainerAsync(cid);
            }
            catch (OperationCanceledException) { }
        });
    }

    private void StartWatcher(DeviceWatcher watcher)
    {
        try { watcher.Start(); }
        catch (Exception ex) { Log.Error("DeviceWatcher start failed", ex); }
    }

    private async Task RestartWatcherAsync(DeviceWatcher watcher, int slot)
    {
        try
        {
            _enumerated[slot] = false;
            await Task.Delay(WatcherRestartDelay, _cts.Token);
            if (!_disposed)
                watcher.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("DeviceWatcher restart failed", ex);
        }
    }

    private async Task PeriodicLoopAsync()
    {
        using var timer = new PeriodicTimer(FullRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
                await RefreshAllAsync();
        }
        catch (OperationCanceledException) { }
    }

    private void OnControllersChanged() => _ = RefreshAllAsync();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gamepads.ControllersChanged -= OnControllersChanged;
        _cts.Cancel();
        foreach (var w in new[] { _classic, _le })
        {
            try
            {
                if (w is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
                    w.Stop();
            }
            catch (Exception ex)
            {
                Log.Error("DeviceWatcher stop failed", ex);
            }
        }
    }
}
