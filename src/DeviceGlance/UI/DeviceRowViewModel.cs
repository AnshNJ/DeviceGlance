using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using DeviceGlance.Core;

namespace DeviceGlance.UI;

public sealed class DeviceRowViewModel : INotifyPropertyChanged
{
    private static readonly Brush Red = Frozen("#E5484D");
    private static readonly Brush Amber = Frozen("#F5A524");
    private static readonly Brush Green = Frozen("#30A46C");
    private static readonly Brush Gray = Frozen("#A0A0A0");

    private DeviceRow _row;

    public DeviceRowViewModel(DeviceRow row) => _row = row;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key => _row.Key;
    public string Glyph => _row.Glyph;
    public string Name => _row.Name;
    public string Subtitle => _row.Subtitle;
    public Brush SubtitleBrush => _row.IsStale ? Amber : Gray;

    public bool HasBattery => _row.Battery is not null;
    public int Percent => _row.Battery?.Percent ?? 0;
    public string PercentText => $"{Percent}%";
    public Visibility BatteryVisibility => HasBattery ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoBatteryVisibility => HasBattery ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ChargingVisibility => _row.Battery?.Charging == true ? Visibility.Visible : Visibility.Collapsed;
    public double RowOpacity => _row.BatteryIsLive || _row.IsConnected ? 1.0 : 0.55;

    public Brush BarBrush => Percent switch
    {
        <= 20 => Red,
        <= 40 => Amber,
        _ => Green,
    };

    public string ToolTip
    {
        get
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(_row.Address))
                lines.Add($"Address: {_row.Address}");
            if (_row.Battery is { } b)
            {
                lines.Add($"Source: {b.Source}");
                if (b.At is { } at)
                    lines.Add($"Reading: {at.LocalDateTime:g}");
            }
            else
            {
                lines.Add("No battery data");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    public void Update(DeviceRow row)
    {
        if (row == _row)
            return;
        _row = row;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
