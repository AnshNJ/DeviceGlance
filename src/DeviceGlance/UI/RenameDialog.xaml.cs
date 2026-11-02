using System.Windows;

namespace DeviceGlance.UI;

public partial class RenameDialog : Window
{
    public RenameDialog(string current, string original)
    {
        InitializeComponent();
        Prompt.Text = $"Name (leave empty to use \"{original}\")";
        NameBox.Text = current;
    }

    /// <summary>Trimmed text; empty means "use the original name".</summary>
    public string Result => NameBox.Text.Trim();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
