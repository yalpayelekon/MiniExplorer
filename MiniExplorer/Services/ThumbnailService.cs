using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniExplorer.Helpers;

namespace MiniExplorer.Services;

public sealed class ThumbnailService
{
    private const int DefaultThumbnailSize = 300;
    private const long MaxCacheBytes = 192L * 1024 * 1024;
    private const int MaxConcurrency = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private const int MaxRetries = 3;

    private readonly SemaphoreSlim _loadSemaphore = new(MaxConcurrency, MaxConcurrency);
    private readonly Dictionary<CacheKey, CacheEntry> _cache = new();
    private readonly LinkedList<CacheKey> _lru = new();
    private readonly object _cacheLock = new();
    private long _cacheBytes;

    public async Task<ImageSource?> GetThumbnailAsync(
        string path,
        CancellationToken cancellationToken,
        int size = DefaultThumbnailSize,
        bool retryIfMissing = false)
    {
        var physicalSize = GetPhysicalThumbnailSize(size);
        var key = new CacheKey(NormalizePath(path), physicalSize);
        var signature = TryGetFileSignature(path);

        if (TryGetValidCached(key, signature, out var cached))
        {
            return cached;
        }

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            signature = TryGetFileSignature(path);

            if (TryGetValidCached(key, signature, out cached))
            {
                return cached;
            }

            var attempts = retryIfMissing ? MaxRetries : 1;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var thumbnail = await Task.Run(() => LoadThumbnail(path, physicalSize), cancellationToken);
                if (thumbnail is not null)
                {
                    signature = TryGetFileSignature(path);
                    if (signature is not null)
                    {
                        AddToCache(key, thumbnail, signature.Value);
                    }

                    return thumbnail;
                }

                if (!retryIfMissing || attempt == attempts - 1)
                {
                    break;
                }

                await Task.Delay(RetryDelay, cancellationToken);
            }

            return null;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public void Invalidate(IEnumerable<string> paths)
    {
        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_cacheLock)
        {
            foreach (var key in _cache.Keys.Where(key => normalizedPaths.Contains(key.Path)).ToList())
            {
                RemoveEntry(key);
            }
        }
    }

    private bool TryGetValidCached(CacheKey key, FileSignature? signature, out ImageSource? image)
    {
        image = null;
        if (signature is null)
        {
            lock (_cacheLock)
            {
                RemoveEntry(key);
            }

            return false;
        }

        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(key, out var entry) || entry.Signature != signature.Value)
            {
                if (entry is not null)
                {
                    RemoveEntry(key);
                }

                return false;
            }

            _lru.Remove(entry.Node);
            _lru.AddLast(entry.Node);
            image = entry.Image;
            return true;
        }
    }

    private void AddToCache(CacheKey key, ImageSource image, FileSignature signature)
    {
        var estimatedBytes = EstimateBytes(image);
        lock (_cacheLock)
        {
            RemoveEntry(key);

            var node = _lru.AddLast(key);
            _cache[key] = new CacheEntry(image, signature, estimatedBytes, node);
            _cacheBytes += estimatedBytes;

            while (_cacheBytes > MaxCacheBytes && _lru.First is { } oldest)
            {
                RemoveEntry(oldest.Value);
            }
        }
    }

    private void RemoveEntry(CacheKey key)
    {
        if (_cache.Remove(key, out var entry))
        {
            _cacheBytes -= entry.EstimatedBytes;
            _lru.Remove(entry.Node);
        }
    }

    private static long EstimateBytes(ImageSource image) =>
        image is BitmapSource bitmap
            ? Math.Max(1L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4)
            : (long)DefaultThumbnailSize * DefaultThumbnailSize * 4;

    private static FileSignature? TryGetFileSignature(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new FileSignature(file.LastWriteTimeUtc.Ticks, file.Length) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static int GetPhysicalThumbnailSize(int logicalSize) =>
        Math.Max(DpiHelper.ToPhysicalPixels(logicalSize), logicalSize);

    private static ImageSource? LoadThumbnail(string path, int size)
    {
        try
        {
            var hr = NativeMethods.SHCreateItemFromParsingName(
                path,
                IntPtr.Zero,
                typeof(NativeMethods.IShellItemImageFactory).GUID,
                out var factory);

            if (hr != 0 || factory is null)
            {
                return LoadBitmapFallback(path, size);
            }

            var shellSize = new NativeMethods.SIZE { cx = size, cy = size };
            hr = factory.GetImage(
                shellSize,
                NativeMethods.SIIGBF_THUMBNAILONLY | NativeMethods.SIIGBF_BIGGERSIZEOK,
                out var hBitmap);

            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                return LoadBitmapFallback(path, size);
            }

            return DpiHelper.FromHBitmap(hBitmap);
        }
        catch
        {
            return LoadBitmapFallback(path, size);
        }
    }

    private static ImageSource? LoadBitmapFallback(string path, int size)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = size;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct CacheKey(string Path, int PhysicalSize)
    {
        public bool Equals(CacheKey other) =>
            PhysicalSize == other.PhysicalSize &&
            StringComparer.OrdinalIgnoreCase.Equals(Path, other.Path);

        public override int GetHashCode() =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Path), PhysicalSize);
    }

    private readonly record struct FileSignature(long LastWriteTicks, long Length);

    private sealed record CacheEntry(
        ImageSource Image,
        FileSignature Signature,
        long EstimatedBytes,
        LinkedListNode<CacheKey> Node);
}
