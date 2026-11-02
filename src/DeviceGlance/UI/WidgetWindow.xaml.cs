using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DeviceGlance.Core;

namespace DeviceGlance.UI;

public partial class WidgetWindow : Window
{
    private const double Width_ = 300;
    private readonly ObservableCollection<DeviceRowViewModel> _items = new();

    /// <summary>Raised after the user dragged the widget; carries the new position.</summary>
    public event Action<double, double>? Moved;

    /// <summary>Raised from a row's context menu; carries the row key.</summary>
    public event Action<string>? RenameRequested;
    public event Action<string>? HideRequested;

    public WidgetWindow(double? savedLeft, double? savedTop, bool alwaysOnTop)
    {
        InitializeComponent();
        Rows.ItemsSource = _items;
        Topmost = alwaysOnTop;
        UpdateEmptyText();

        WindowStartupLocation = WindowStartupLocation.Manual;
        if (savedLeft is { } l && savedTop is { } t && IsInsideVirtualScreen(l, t))
        {
            Left = l;
            Top = t;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width_ - 16;
            Top = work.Top + 16;
        }
    }

    /// <summary>Applies the sorted rows: removes gone rows, adds new ones and reorders in place by key.</summary>
    public void Apply(IReadOnlyList<DeviceRow> rows)
    {
        var keys = rows.Select(r => r.Key).ToHashSet();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_items[i].Key))
                _items.RemoveAt(i);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int at = IndexOf(row.Key);
            if (at < 0)
            {
                _items.Insert(i, new DeviceRowViewModel(row));
                continue;
            }
            if (at != i)
                _items.Move(at, i);
            _items[i].Update(row);
        }
        UpdateEmptyText();
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Key == key)
                return i;
        }
        return -1;
    }

    private void UpdateEmptyText() =>
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static bool IsInsideVirtualScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft
        && top >= SystemParameters.VirtualScreenTop
        && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
        && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return; // button was released before the drag started
        }
        Moved?.Invoke(Left, Top);
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (RowKeyOf(sender) is { } key) RenameRequested?.Invoke(key);
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        if (RowKeyOf(sender) is { } key) HideRequested?.Invoke(key);
    }

    private static string? RowKeyOf(object sender)
    {
        var item = (System.Windows.Controls.MenuItem)sender;
        var target = (item.Parent as System.Windows.Controls.ContextMenu)?.PlacementTarget as FrameworkElement;
        return ((target?.DataContext ?? item.DataContext) as DeviceRowViewModel)?.Key;
    }
}
