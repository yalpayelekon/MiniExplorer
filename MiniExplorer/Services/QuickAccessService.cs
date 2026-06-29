using System.IO;
using System.Text.Json;

namespace MiniExplorer.Services;

public sealed class QuickAccessService
{
    private readonly string _storagePath;
    private readonly List<string> _pinnedPaths = [];

    public event Action? Changed;

    public QuickAccessService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniExplorer");
        Directory.CreateDirectory(directory);
        _storagePath = Path.Combine(directory, "quickaccess.json");
        Load();
    }

    public IReadOnlyList<string> GetPinnedPaths() => _pinnedPaths.ToList();

    public bool IsPinned(string path) =>
        _pinnedPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    public void Pin(string path)
    {
        if (_pinnedPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _pinnedPaths.Add(path);
        Save();
        Changed?.Invoke();
    }

    public void Unpin(string path)
    {
        var removed = _pinnedPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return;
        }

        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(_storagePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_storagePath);
            var items = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            _pinnedPaths.Clear();
            _pinnedPaths.AddRange(items.Where(Directory.Exists));
        }
        catch
        {
            _pinnedPaths.Clear();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_pinnedPaths, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storagePath, json);
    }
}
