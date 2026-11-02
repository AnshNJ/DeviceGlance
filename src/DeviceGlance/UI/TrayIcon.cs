namespace DeviceGlance.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _alwaysOnTop;
    private readonly System.Windows.Forms.ToolStripMenuItem _startup;
    private readonly Func<bool> _startupEnabled;

    public TrayIcon(
        bool alwaysOnTop,
        Func<bool> startupEnabled,
        Action toggleWidget,
        Action refreshNow,
        Action<bool> setAlwaysOnTop,
        Action<bool> setStartup,
        Action showIpadSetup,
        Func<IReadOnlyList<(string Key, string Name)>> hiddenDevices,
        Action<string> unhide,
        Action exit)
    {
        _startupEnabled = startupEnabled;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show/Hide widget", null, (_, _) => toggleWidget());
        menu.Items.Add("Refresh now", null, (_, _) => refreshNow());

        _alwaysOnTop = new System.Windows.Forms.ToolStripMenuItem("Always on top") { CheckOnClick = true, Checked = alwaysOnTop };
        _alwaysOnTop.Click += (_, _) => setAlwaysOnTop(_alwaysOnTop.Checked);
        menu.Items.Add(_alwaysOnTop);

        _startup = new System.Windows.Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startup.Click += (_, _) => setStartup(_startup.Checked);
        menu.Items.Add(_startup);

        var hiddenMenu = new System.Windows.Forms.ToolStripMenuItem("Hidden devices");
        menu.Items.Add(hiddenMenu);
        menu.Items.Add("iPad setup…", null, (_, _) => showIpadSetup());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        menu.Opening += (_, _) =>
        {
            _startup.Checked = _startupEnabled();
            hiddenMenu.DropDownItems.Clear();
            var hidden = hiddenDevices();
            foreach (var (key, name) in hidden)
                hiddenMenu.DropDownItems.Add("Show " + name, null, (_, _) => unhide(key));
            if (hidden.Count == 0)
                hiddenMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("(none)") { Enabled = false });
        };

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "DeviceGlance",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => toggleWidget();
    }

    public void ShowBalloon(string title, string text, System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Warning) =>
        _icon.ShowBalloonTip(10000, title, text, icon);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
