using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniExplorer.Helpers;

public static class DpiHelper
{
    private static double _cachedScale = 1.0;

    public static double Scale
    {
        get
        {
            RefreshScaleIfOnUiThread();
            return _cachedScale;
        }
    }

    /// <summary>
    /// Reads the current monitor scale on the UI thread and caches it for background workers.
    /// </summary>
    public static void RefreshScaleIfOnUiThread()
    {
        var app = Application.Current;
        if (app is null || !app.Dispatcher.CheckAccess())
        {
            return;
        }

        var window = app.MainWindow;
        if (window is null)
        {
            return;
        }

        var source = PresentationSource.FromVisual(window);
        _cachedScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    public static int ToPhysicalPixels(double logicalPixels) =>
        (int)Math.Ceiling(logicalPixels * Scale);

    public static double SystemDpi => 96.0 * Scale;

    public const int MinTileIconPixels = 48;

    public static bool IsSufficientTileSize(ImageSource? source) =>
        source is BitmapSource bitmap &&
        bitmap.PixelWidth >= MinTileIconPixels &&
        bitmap.PixelHeight >= MinTileIconPixels;

    public static int MaxPixelDimension(ImageSource? source) =>
        source is BitmapSource bitmap ? Math.Max(bitmap.PixelWidth, bitmap.PixelHeight) : 0;

    public static BitmapSource EnsureDisplayDpi(BitmapSource bitmap)
    {
        var dpi = SystemDpi;
        if (Math.Abs(bitmap.DpiX - dpi) < 0.5 && Math.Abs(bitmap.DpiY - dpi) < 0.5)
        {
            return bitmap;
        }

        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var stride = (converted.PixelWidth * converted.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            dpi,
            dpi,
            converted.Format,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    public static BitmapSource FromHBitmap(IntPtr hBitmap)
    {
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return EnsureDisplayDpi(bitmap);
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    public static BitmapSource FromHIcon(IntPtr hIcon)
    {
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return EnsureDisplayDpi(bitmap);
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
