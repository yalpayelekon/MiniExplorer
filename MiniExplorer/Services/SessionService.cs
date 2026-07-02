using System.IO;
using System.Text.Json;
using MiniExplorer.Helpers;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class SessionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _storagePath;

    public SessionService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniExplorer");
        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(directory, "session.json");
    }

    public TabSession? Load() => JsonFileHelper.Read<TabSession>(_storagePath);

    public void Save(TabSession session) =>
        JsonFileHelper.Write(_storagePath, session, SerializerOptions);

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
