using System.IO;
using System.Text.Json;
using MiniExplorer.Helpers;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class SessionService
{
    private readonly string _storagePath;

    public SessionService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniExplorer");
        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(directory, "session.json");
    }

    public TabSession? Load()
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<TabSession>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(TabSession session)
    {
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storagePath, json);
    }

    public static bool IsValidTabPath(string path)
    {
        if (path == PathConstants.ThisPc)
        {
            return true;
        }

        if (Directory.Exists(path))
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && Directory.Exists(root);
        }
        catch
        {
            return false;
        }
    }
}
