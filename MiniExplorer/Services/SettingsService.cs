using System.IO;
using System.Text.Json;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class SettingsService
{
    private readonly string _storagePath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniExplorer");
        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_storagePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storagePath, json);
    }
}
