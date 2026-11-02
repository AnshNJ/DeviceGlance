namespace DeviceGlance.Infrastructure;

public static class Log
{
    private static readonly object Gate = new();

    public static void Error(string message, Exception? ex = null)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}" + (ex is null ? "" : $" | {ex}");
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureDirectory();
                File.AppendAllText(AppPaths.LogFile, line.ReplaceLineEndings(" ") + Environment.NewLine);
            }
        }
        catch (IOException) { /* nothing left to report to */ }
        catch (UnauthorizedAccessException) { }
    }
}
