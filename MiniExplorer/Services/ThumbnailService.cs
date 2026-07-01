using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniExplorer.Helpers;

namespace MiniExplorer.Services;

public sealed class ThumbnailService
{
    private const int DefaultThumbnailSize = 300;
    private const int MaxConcurrency = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private const int MaxRetries = 3;

    private readonly SemaphoreSlim _loadSemaphore = new(MaxConcurrency, MaxConcurrency);

    public async Task<ImageSource?> GetThumbnailAsync(
        string path,
        CancellationToken cancellationToken,
        int size = DefaultThumbnailSize,
        bool retryIfMissing = false)
    {
        var physicalSize = GetPhysicalThumbnailSize(size);

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attempts = retryIfMissing ? MaxRetries : 1;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var thumbnail = await Task.Run(() => LoadThumbnail(path, physicalSize), cancellationToken);
                if (thumbnail is not null)
                {
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
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
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
}
