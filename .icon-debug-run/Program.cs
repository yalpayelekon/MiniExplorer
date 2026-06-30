using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MiniExplorer.Helpers;
using MiniExplorer.Services;

namespace MiniExplorer.IconDebugRun;

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

        var shell = new ShellService();
        var dpiScale = DpiHelper.Scale;
        var logicalSize = 96.0;
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(downloads).Take(5))
            {
                var isDir = Directory.Exists(path);
                var raw = shell.GetTileIcon(path, isDir, logicalSize, dpiScale);
                if (raw is BitmapSource bitmap)
                {
                    var scaled = DpiHelper.ScaleToLogicalDisplaySize(bitmap, logicalSize);
                    try
                    {
                        var line = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            sessionId = "7f76f0",
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            location = "IconDebugRun",
                            message = "scaled tile icon",
                            hypothesisId = "H12",
                            runId = "post-fix",
                            data = new
                            {
                                file = Path.GetFileName(path),
                                dpiScale,
                                rawW = bitmap.PixelWidth,
                                scaledW = scaled.PixelWidth,
                                scaledDpi = scaled.DpiX,
                                targetPhysical = DpiHelper.ToPhysicalPixels(logicalSize, dpiScale)
                            }
                        });
                        File.AppendAllText(
                            @"C:\Users\yasinalpay\Projects\MiniExplorer\debug-7f76f0.log",
                            line + Environment.NewLine);
                    }
                    catch { }
                }
            }
        }

        app.Shutdown();
    }
}
