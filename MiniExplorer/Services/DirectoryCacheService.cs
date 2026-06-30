using System.IO;
using MiniExplorer.Helpers;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class DirectoryCacheService
{
    private const int MaxDirectories = 16;
    private const int MaxTotalEntries = 100_000;

    private readonly Dictionary<string, CachedListing> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly object _lock = new();
    private int _totalEntries;

    public bool IsCacheable(string path) => PicturesPathHelper.IsUnderPictures(path);

    public bool Contains(string path)
    {
        if (!IsCacheable(path))
        {
            return false;
        }

        lock (_lock)
        {
            return _cache.ContainsKey(NormalizePath(path));
        }
    }

    public bool TryGet(string path, out IReadOnlyList<FileSystemEntry> entries)
    {
        entries = Array.Empty<FileSystemEntry>();
        if (!IsCacheable(path))
        {
            return false;
        }

        var key = NormalizePath(path);
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var cached))
            {
                return false;
            }

            Touch(key);
            entries = cached.Entries;
            return true;
        }
    }

    public void Set(string path, IReadOnlyList<FileSystemEntry> entries)
    {
        if (!IsCacheable(path) || entries.Count > MaxTotalEntries)
        {
            return;
        }

        var key = NormalizePath(path);
        var clones = entries.Select(CloneEntry).ToList();
        lock (_lock)
        {
            if (_cache.Remove(key, out var previous))
            {
                _totalEntries -= previous.Entries.Count;
                _lru.Remove(key);
            }

            _cache[key] = new CachedListing(clones);
            _lru.AddLast(key);
            _totalEntries += clones.Count;

            while (_cache.Count > MaxDirectories || _totalEntries > MaxTotalEntries)
            {
                var oldest = _lru.First?.Value;
                if (oldest is null)
                {
                    break;
                }

                _lru.RemoveFirst();
                if (_cache.Remove(oldest, out var removed))
                {
                    _totalEntries -= removed.Entries.Count;
                }
            }
        }
    }

    public void InvalidateDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var key = NormalizePath(directoryPath);
        lock (_lock)
        {
            if (_cache.Remove(key, out var removed))
            {
                _totalEntries -= removed.Entries.Count;
                _lru.Remove(key);
            }
        }
    }

    public void InvalidateForPath(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            InvalidateDirectory(parent);
        }
    }

    private void Touch(string key)
    {
        _lru.Remove(key);
        _lru.AddLast(key);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static FileSystemEntry CloneEntry(FileSystemEntry entry) => new()
    {
        FullPath = entry.FullPath,
        Name = entry.Name,
        IsDirectory = entry.IsDirectory,
        Size = entry.Size,
        Modified = entry.Modified,
        Extension = entry.Extension
    };

    private sealed record CachedListing(IReadOnlyList<FileSystemEntry> Entries);
}
