using System.Diagnostics;
using System.IO;
using MiniExplorer.Helpers;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class DirectoryCacheService
{
    private int _maxDirectories = 16;
    private int _maxTotalEntries = 100_000;

    private readonly Dictionary<string, CachedListing> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly object _lock = new();
    private int _totalEntries;
    private int _hits;
    private int _misses;

    public bool IsCacheable(string path)
    {
        // Cache all directories except ThisPC and root paths
        if (string.IsNullOrWhiteSpace(path) || path == PathConstants.ThisPc)
        {
            return false;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            // Don't cache root drives (e.g., "C:\")
            if (normalizedPath.Length <= 3 && normalizedPath.EndsWith(Path.VolumeSeparatorChar.ToString()))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

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
    
    public (int Hits, int Misses, int Count) GetStatistics()
    {
        return (_hits, _misses, _cache.Count);
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lru.Clear();
            _totalEntries = 0;
            _hits = 0;
            _misses = 0;
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
                Interlocked.Increment(ref _misses);
                return false;
            }

            Touch(key);
            entries = cached.Entries;
            Interlocked.Increment(ref _hits);
            return true;
        }
    }

    public void Set(string path, IReadOnlyList<FileSystemEntry> entries)
    {
        if (!IsCacheable(path) || entries.Count > _maxTotalEntries)
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

            while (_cache.Count > _maxDirectories || _totalEntries > _maxTotalEntries)
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
        
        LogDebug($"Cached directory: {key} ({clones.Count} entries)");
    }
    
    public void UpdateCacheLimits(int maxDirectories, int maxTotalEntries)
    {
        if (maxDirectories < 1)
        {
            maxDirectories = 1;
        }
        if (maxTotalEntries < 100)
        {
            maxTotalEntries = 100;
        }

        _maxDirectories = maxDirectories;
        _maxTotalEntries = maxTotalEntries;
        
        // Trigger eviction if currently over limits
        lock (_lock)
        {
            while ((_cache.Count > _maxDirectories || _totalEntries > _maxTotalEntries) && _lru.First is { } oldest)
            {
                _lru.RemoveFirst();
                if (_cache.Remove(oldest.Value, out var removed))
                {
                    _totalEntries -= removed.Entries.Count;
                }
            }
        }
        
        LogDebug($"Updated directory cache limits: {maxDirectories} dirs, {maxTotalEntries:N0} entries");
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
    
    [Conditional("DEBUG")]
    private static void LogDebug(string message)
    {
        // Debug-only logging - no-op in release builds
        System.Diagnostics.Debug.WriteLine($"[DirectoryCache] {message}");
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
