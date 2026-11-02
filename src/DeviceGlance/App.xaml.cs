using System.Windows;
using System.Windows.Threading;
using DeviceGlance.Core;
using DeviceGlance.Infrastructure;
using DeviceGlance.Sources;
using DeviceGlance.UI;

namespace DeviceGlance;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppSettings _settings = new();
    private AppState _state = new();
    private LowBatteryAlerter _alerter = null!;
    private WidgetWindow? _widget;
    private TrayIcon? _tray;
    private GamepadBatteryReader? _gamepads;
    private BluetoothMonitor? _monitor;
    private ReportServer? _server;
    private DispatcherTimer? _subtitleTimer;

    private IReadOnlyList<BluetoothDeviceInfo> _bluetooth = [];
    private volatile IReadOnlyList<DeviceRow> _rows = [];
    private IReadOnlyList<DeviceRow> _merged = [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mutex = new Mutex(true, "Local\\DeviceGlance.SingleInstance", out var created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };

        try
        {
            AppPaths.EnsureDirectory();
            _settings = JsonFileStore.Load(AppPaths.Settings, () => new AppSettings());
            JsonFileStore.Save(AppPaths.Settings, _settings);   // persists a freshly generated token
            _state = JsonFileStore.Load(AppPaths.State, () => new AppState());

            _alerter = new LowBatteryAlerter(_state.Alerted, _settings.LowThreshold, _settings.RearmThreshold);
            _alerter.SetChanged += SaveState;

            _widget = new WidgetWindow(_settings.WindowLeft, _settings.WindowTop, _settings.AlwaysOnTop);
            _widget.Moved += (left, top) => UpdateSettings(s => s with { WindowLeft = left, WindowTop = top });
            _widget.RenameRequested += RenameDevice;
            _widget.HideRequested += HideDevice;

            _tray = new TrayIcon(
                _settings.AlwaysOnTop,
                () => StartupRegistration.IsEnabled,
                ToggleWidget,
                () => _ = _monitor?.RefreshAllAsync(),
                SetAlwaysOnTop,
                SetStartup,
                ShowIpadSetup,
                () => _settings.HiddenDevices.Select(kv => (kv.Key, kv.Value)).ToList(),
                UnhideDevice,
                () => Shutdown());

            if (!_settings.StartupInitialized)
            {
                StartupRegistration.Enable();
                UpdateSettings(s => s with { StartupInitialized = true });
            }

            _widget.Show();
            Remerge();

            _subtitleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _subtitleTimer.Tick += (_, _) => Remerge();
            _subtitleTimer.Start();

            await StartServerAsync();

            _gamepads = new GamepadBatteryReader();
            _monitor = new BluetoothMonitor(_gamepads);
            _monitor.Snapshot += OnSnapshot;
            _gamepads.Start();
            _monitor.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _subtitleTimer?.Stop();
        _tray?.Dispose();
        _monitor?.Dispose();
        _gamepads?.Dispose();
        if (_server is not null)
        {
            try { Task.Run(() => _server.StopAsync()).Wait(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { Log.Error("Server stop failed", ex); }
        }
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }

    private async Task StartServerAsync()
    {
        var server = new ReportServer(
            _settings,
            report => Dispatcher.InvokeAsync(() => HandleReport(report)).Task,
            () => _rows);
        try
        {
            await server.StartAsync();
            _server = server;
        }
        catch (IOException ex)
        {
            Log.Error($"Report server could not listen on port {_settings.Port}", ex);
            _tray?.ShowBalloon("DeviceGlance", $"Report server off: port {_settings.Port} in use");
            await server.DisposeAsync();
        }
    }

    private void OnSnapshot(IReadOnlyList<BluetoothDeviceInfo> devices) =>
        Dispatcher.InvokeAsync(() =>
        {
            _bluetooth = devices;
            Remerge();
        });

    private void HandleReport(PushReport report)
    {
        _state.PushReports[report.Device.ToLowerInvariant()] = report;
        SaveState();
        Remerge();
    }

    /// <summary>Merge, alert, publish to the widget. UI thread only.</summary>
    private void Remerge()
    {
        var now = DateTimeOffset.Now;
        _merged = DeviceMerger.Merge(_bluetooth, _state.PushReports, now);
        var rows = DeviceMerger.Customize(_merged, _settings.Renames, _settings.HiddenDevices);
        _rows = rows;

        var fired = rows
            .Where(r => r.Battery is not null && r.BatteryIsLive && _alerter.Evaluate(r.Key, r.Battery.Percent, true))
            .ToList();
        _widget?.Apply(rows);

        if (fired.Count == 1)
            _tray?.ShowBalloon($"{fired[0].Name} battery low", $"{fired[0].Battery!.Percent}% — charge it soon.");
        else if (fired.Count > 1)
            _tray?.ShowBalloon("Battery low", string.Join("\n", fired.Select(r => $"{r.Name} {r.Battery!.Percent}%")));
    }

    private void SaveState()
    {
        try { JsonFileStore.Save(AppPaths.State, _state); }
        catch (IOException ex) { Log.Error("Saving state failed", ex); }
    }

    private void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        _settings = change(_settings);
        try { JsonFileStore.Save(AppPaths.Settings, _settings); }
        catch (IOException ex) { Log.Error("Saving settings failed", ex); }
    }

    private void RenameDevice(string key)
    {
        var merged = _merged.FirstOrDefault(r => r.Key == key);
        if (merged is null) return;

        var dlg = new RenameDialog(_settings.Renames.GetValueOrDefault(key) ?? merged.Name, merged.Name);
        if (dlg.ShowDialog() != true) return;

        var result = dlg.Result;
        UpdateSettings(s =>
        {
            var renames = new Dictionary<string, string>(s.Renames);
            if (result.Length == 0) renames.Remove(key);
            else renames[key] = result;
            return s with { Renames = renames };
        });
        Remerge();
    }

    private void HideDevice(string key)
    {
        var name = _rows.FirstOrDefault(r => r.Key == key)?.Name;
        if (name is null) return;
        UpdateSettings(s => s with { HiddenDevices = new(s.HiddenDevices) { [key] = name } });
        Remerge();
    }

    private void UnhideDevice(string key)
    {
        UpdateSettings(s =>
        {
            var hidden = new Dictionary<string, string>(s.HiddenDevices);
            hidden.Remove(key);
            return s with { HiddenDevices = hidden };
        });
        Remerge();
    }

    private void ToggleWidget()
    {
        if (_widget is null)
            return;
        if (_widget.IsVisible)
            _widget.Hide();
        else
            _widget.Show();
    }

    private void SetAlwaysOnTop(bool value)
    {
        UpdateSettings(s => s with { AlwaysOnTop = value });
        if (_widget is not null)
            _widget.Topmost = value;
    }

    private void SetStartup(bool enable)
    {
        try
        {
            if (enable) StartupRegistration.Enable();
            else StartupRegistration.Disable();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("Updating the Run key failed", ex);
        }
    }

    private void ShowIpadSetup()
    {
        var text = IpadSetup.BuildText(_settings);
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException ex) { Log.Error("Clipboard unavailable", ex); }
        MessageBox.Show(text + Environment.NewLine + "(Copied to clipboard.)", "DeviceGlance - iPad setup");
    }
}
