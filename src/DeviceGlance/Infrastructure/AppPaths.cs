namespace DeviceGlance.Infrastructure;

public static class AppPaths
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeviceGlance");

    public static string Settings => Path.Combine(Directory, "settings.json");
    public static string State => Path.Combine(Directory, "state.json");
    public static string LogFile => Path.Combine(Directory, "log.txt");

    public static void EnsureDirectory() => System.IO.Directory.CreateDirectory(Directory);
}
