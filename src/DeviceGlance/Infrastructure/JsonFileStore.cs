using System.Text.Json;

namespace DeviceGlance.Infrastructure;

public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static T Load<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path))
            return fallback();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Error($"Corrupt or unreadable {path}", ex);
            try { File.Move(path, path + ".corrupt", overwrite: true); }
            catch (IOException moveEx) { Log.Error($"Could not rename {path}", moveEx); }
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
