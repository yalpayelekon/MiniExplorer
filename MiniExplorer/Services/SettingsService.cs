using System.IO;
using System.Text.Json;
using MiniExplorer.Helpers;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _storagePath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniExplorer");
        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load() =>
        JsonFileHelper.ReadOrDefault(_storagePath, static () => new AppSettings());

    public void Save(AppSettings settings) =>
        JsonFileHelper.Write(_storagePath, settings, SerializerOptions);
}
