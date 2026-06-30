using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniExplorer.Helpers;

namespace MiniExplorer.Services;

public sealed class ThumbnailService
{
    private const int DefaultThumbnailSize = 300;
    private const int MaxCacheEntries = 500;

    private readonly SemaphoreSlim _loadSemaphore = new(6, 6);
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _cacheOrder = new();
    private readonly object _cacheLock = new();

    public async Task<ImageSource?> GetThumbnailAsync(string path, CancellationToken cancellationToken, int size = DefaultThumbnailSize)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            TouchCache(path);
            return cached;
        }

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_cache.TryGetValue(path, out cached))
            {
                TouchCache(path);
                return cached;
            }

            var thumbnail = await Task.Run(() => LoadThumbnail(path, size), cancellationToken);
            if (thumbnail is not null)
            {
                AddToCache(path, thumbnail);
            }

            return thumbnail;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public void Invalidate(IEnumerable<string> paths)
    {
        lock (_cacheLock)
        {
            foreach (var path in paths)
            {
                _cache.Remove(path, out _);
                _cacheOrder.Remove(path);
            }
        }
    }

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

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                NativeMethods.DeleteObject(hBitmap);
            }
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

    private void AddToCache(string path, ImageSource thumbnail)
    {
        lock (_cacheLock)
        {
            if (_cache.TryAdd(path, thumbnail))
            {
                _cacheOrder.AddLast(path);
                while (_cacheOrder.Count > MaxCacheEntries)
                {
                    var oldest = _cacheOrder.First!.Value;
                    _cacheOrder.RemoveFirst();
                    _cache.Remove(oldest, out _);
                }
            }
        }
    }

    private void TouchCache(string path)
    {
        lock (_cacheLock)
        {
            _cacheOrder.Remove(path);
            _cacheOrder.AddLast(path);
        }
    }
}
