using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace MiniExplorer.Helpers;

internal static class JsonFileHelper
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        lock (GetLock(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return null;
            }
        }
    }

    public static T ReadOrDefault<T>(string path, Func<T> defaultFactory) where T : class
    {
        return Read<T>(path) ?? defaultFactory();
    }

    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";
        var json = JsonSerializer.Serialize(value, options);

        lock (GetLock(path))
        {
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
    }

    private static object GetLock(string path) => Locks.GetOrAdd(path, static _ => new object());

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the backup file.
        }
    }
}
