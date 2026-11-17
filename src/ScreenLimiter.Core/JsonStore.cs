using System.Text.Json;

namespace ScreenLimiter.Core;

/// <summary>Reads and writes the settings and usage files. Writes are atomic so a power cut can't corrupt them.</summary>
public sealed class JsonStore(string directory)
{
    public const string ConfigFile = "config.json";
    public const string UsageFile = "usage.json";

    public string Directory { get; } = directory;

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ScreenLimiter");

    public T Load<T>(string name) where T : new()
    {
        var path = Path.Combine(Directory, name);
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Indented) ?? new T();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Keep the unreadable file for inspection and start clean rather than crash the service.
            try { File.Copy(path, path + ".bad", true); } catch (IOException) { }
        }
        return new T();
    }

    public void Save<T>(string name, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, name);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json.Indented));
        File.Move(temp, path, true);
    }
}
