using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using DeviceGlance.Infrastructure;

namespace DeviceGlance.UI;

public static class IpadSetup
{
    public static IReadOnlyList<string> Addresses(int port)
    {
        var urls = new List<string> { $"http://{Environment.MachineName}.local:{port}/api/report" };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    urls.Add($"http://{ua.Address}:{port}/api/report");
            }
        }
        return urls;
    }

    public static string BuildText(AppSettings settings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PC addresses:");
        foreach (var url in Addresses(settings.Port))
            sb.AppendLine("  " + url);
        sb.AppendLine();
        sb.AppendLine($"Token: {settings.Token}");
        sb.AppendLine();
        sb.AppendLine("Steps:");
        sb.AppendLine("1. In Shortcuts, create the shortcut \"Report to DeviceGlance\":");
        sb.AppendLine("   - Get Battery Level");
        sb.AppendLine("   - Is Connected to Charger (only if the action exists)");
        sb.AppendLine("   - Get Contents of URL:");
        sb.AppendLine("       URL: the first address above");
        sb.AppendLine("       Method: POST");
        sb.AppendLine("       Header: X-DeviceGlance-Token = the token above");
        sb.AppendLine("       Request Body: JSON with fields:");
        sb.AppendLine("         device (Text) = iPad   (must exactly match the Bluetooth name shown in the widget)");
        sb.AppendLine("         battery (Number) = Battery Level");
        sb.AppendLine("         charging (Boolean) = Is Connected to Charger");
        sb.AppendLine("2. Create automations that each run this shortcut with \"Run Immediately\":");
        sb.AppendLine("   Battery Level Falls Below 50, Falls Below 21, Rises Above 80;");
        sb.AppendLine("   Charger Is Connected; Charger Is Disconnected;");
        sb.AppendLine("   Time of Day at 09:00, 14:00 and 21:00.");
        sb.AppendLine("3. Run once in an elevated terminal on the PC (Wi-Fi network must be Private):");
        sb.AppendLine($"   netsh advfirewall firewall add rule name=\"DeviceGlance\" dir=in action=allow protocol=TCP localport={settings.Port} profile=private");
        sb.AppendLine("4. If .local does not resolve on the iPad, use one of the IP address lines.");
        return sb.ToString();
    }
}
