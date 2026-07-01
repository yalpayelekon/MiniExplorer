using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MiniExplorer.Helpers;
using MiniExplorer.Services;

namespace MiniExplorer.ThumbTest;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        app.MainWindow = window;
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        var file = Directory.EnumerateFiles(dir, "*.png").FirstOrDefault();
        if (file is null)
        {
            Console.WriteLine("no file");
            app.Shutdown();
            return;
        }

        Console.WriteLine($"File: {file}");
        Console.WriteLine($"IsImage: {PicturesPathHelper.IsImageFile(file)}");

        var svc = new ThumbnailService();
        var thumb = svc.GetThumbnailAsync(file, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine($"Shell thumb: {(thumb is BitmapSource b ? $"{b.PixelWidth}x{b.PixelHeight}" : "null")}");

        Task.Run(() =>
        {
            var mtaThumb = svc.GetThumbnailAsync(file, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"MTA shell thumb: {(mtaThumb is BitmapSource b2 ? $"{b2.PixelWidth}x{b2.PixelHeight}" : "null")}");
        }).GetAwaiter().GetResult();

        // Test URI fallback directly
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(file, UriKind.Absolute);
            bitmap.DecodePixelWidth = 300;
            bitmap.EndInit();
            bitmap.Freeze();
            Console.WriteLine($"Uri thumb: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Uri failed: {ex.Message}");
            try
            {
                using var stream = File.OpenRead(file);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.DecodePixelWidth = 300;
                bitmap.EndInit();
                bitmap.Freeze();
                Console.WriteLine($"Stream thumb: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"Stream failed: {ex2.Message}");
            }
        }

        app.Shutdown();
    }
}
