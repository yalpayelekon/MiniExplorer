using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MiniExplorer.Helpers;
using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class ShellService
{
    private readonly ConcurrentDictionary<string, ImageSource> _listIconCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PerFileIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".cpl", ".scr", ".lnk", ".ico", ".url"
    };

    private static readonly string[] CodeCandidatePaths =
    [
        "code",
        "cursor",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\Microsoft VS Code\Code.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\cursor\Cursor.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Microsoft VS Code\Code.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft VS Code\Code.exe")
    ];

    private static readonly string[] NotepadPlusPlusCandidatePaths =
    [
        "notepad++.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Notepad++\notepad++.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Notepad++\notepad++.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\Notepad++\notepad++.exe")
    ];

    public ImageSource GetIcon(string path, bool isDirectory) => GetListIcon(path, isDirectory) ?? CreateFallbackIcon(16);

    public ImageSource? GetListIcon(string path, bool isDirectory)
    {
        var cacheKey = BuildListIconCacheKey(path, isDirectory);
        return _listIconCache.GetOrAdd(cacheKey, _ => LoadListIcon(path, isDirectory, cacheKey));
    }

    private static string BuildListIconCacheKey(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return $"list|directory|{path}";
        }

        var extension = Path.GetExtension(path);
        if (PerFileIconExtensions.Contains(extension))
        {
            return $"list|path|{path}";
        }

        if (string.IsNullOrEmpty(extension))
        {
            return "list|file-no-ext";
        }

        return $"list|ext|{extension}";
    }

    private static ImageSource LoadListIcon(string path, bool isDirectory, string cacheKey)
    {
        if (cacheKey == "list|file-no-ext")
        {
            return LoadSmallIconByAttributes("dummy", NativeMethods.FILE_ATTRIBUTE_NORMAL);
        }

        if (cacheKey.StartsWith("list|ext|", StringComparison.Ordinal))
        {
            var extension = cacheKey["list|ext|".Length..];
            return LoadSmallIconByAttributes($"dummy{extension}", NativeMethods.FILE_ATTRIBUTE_NORMAL);
        }

        return LoadSmallIcon(path, isDirectory);
    }

    public ImageSource GetTileIcon(string path, bool isDirectory, double logicalSize = 187)
    {
        var dpiScale = DpiHelper.Scale;
        var physicalSize = Math.Max((int)Math.Ceiling(logicalSize * dpiScale), 32);
        var (source, _) = LoadTileIconWithSource(path, isDirectory, physicalSize);
        return source;
    }

    public void OpenDefault(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void OpenInTerminal(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new InvalidOperationException("Klasör bulunamadı.");
        }

        var escapedPath = directoryPath.Replace("'", "''");
        var candidates = new (string FileName, string Arguments, string? WorkingDirectory)[]
        {
            ("wt.exe", $"-d \"{directoryPath}\"", null),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\wt.exe"), $"-d \"{directoryPath}\"", null),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Windows Terminal\wt.exe"), $"-d \"{directoryPath}\"", null),
            ("pwsh.exe", $"-NoExit -Command Set-Location -LiteralPath '{escapedPath}'", null),
            ("powershell.exe", $"-NoExit -Command Set-Location -LiteralPath '{escapedPath}'", null),
            ("cmd.exe", $"/k cd /d \"{directoryPath}\"", directoryPath)
        };

        foreach (var (fileName, arguments, workingDirectory) in candidates)
        {
            try
            {
                if (fileName.Contains('\\') && !File.Exists(fileName))
                {
                    continue;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory ?? directoryPath,
                    UseShellExecute = true
                });
                return;
            }
            catch
            {
                // Try next terminal.
            }
        }

        throw new InvalidOperationException("Terminal uygulaması bulunamadı.");
    }

    public void OpenInExplorer(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return;
        }

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            return;
        }

        throw new InvalidOperationException("Konum bulunamadı.");
    }

    public void OpenWithCode(string path)
    {
        foreach (var candidate in CodeCandidatePaths)
        {
            try
            {
                if (candidate is "code" or "cursor")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                    return;
                }

                if (File.Exists(candidate))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }
            catch
            {
                // Try next candidate.
            }
        }

        throw new InvalidOperationException("VS Code veya Cursor bulunamadı.");
    }

    public void OpenWithNotepadPlusPlus(string path)
    {
        foreach (var candidate in NotepadPlusPlusCandidatePaths)
        {
            try
            {
                if (candidate is "notepad++.exe")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                    return;
                }

                if (File.Exists(candidate))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }
            catch
            {
                // Try next candidate.
            }
        }

        throw new InvalidOperationException("Notepad++ bulunamadı.");
    }

    public void RunAsAdmin(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    public bool CanRunAsAdmin(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msi", StringComparison.OrdinalIgnoreCase);
    }

    public void OpenWith(string filePath, string executablePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"\"{filePath}\"",
            UseShellExecute = true
        });
    }

    public void ShowOpenWithDialog(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"shell32.dll,OpenAs_RunDLL \"{filePath}\"",
            UseShellExecute = true
        });
    }

    public IReadOnlyList<AssociatedApp> GetAssociatedApps(string filePath)
    {
        var apps = new List<AssociatedApp>();
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return apps;
        }

        CollectOpenWithListApps(extension, apps);
        CollectUserChoiceApps(extension, apps);

        return apps
            .GroupBy(a => a.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectOpenWithListApps(string extension, List<AssociatedApp> apps)
    {
        using var openWithList = Registry.ClassesRoot.OpenSubKey($@"{extension}\OpenWithList");
        if (openWithList is null)
        {
            return;
        }

        foreach (var appKeyName in openWithList.GetSubKeyNames())
        {
            var executable = ResolveApplicationExecutable(appKeyName);
            if (string.IsNullOrWhiteSpace(executable))
            {
                continue;
            }

            apps.Add(new AssociatedApp
            {
                Name = Path.GetFileNameWithoutExtension(appKeyName),
                ExecutablePath = executable
            });
        }
    }

    private static void CollectUserChoiceApps(string extension, List<AssociatedApp> apps)
    {
        using var openWithProgIds = Registry.ClassesRoot.OpenSubKey($@"{extension}\OpenWithProgids");
        if (openWithProgIds is null)
        {
            return;
        }

        foreach (var progId in openWithProgIds.GetValueNames())
        {
            if (progId.StartsWith("AppX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var command = commandKey?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var executable = ExtractExecutable(command);
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                continue;
            }

            apps.Add(new AssociatedApp
            {
                Name = progId,
                ExecutablePath = executable
            });
        }
    }

    private static string? ResolveApplicationExecutable(string appKeyName)
    {
        if (File.Exists(appKeyName))
        {
            return appKeyName;
        }

        using var appCommandKey = Registry.ClassesRoot.OpenSubKey($@"Applications\{appKeyName}\shell\open\command");
        var appCommand = appCommandKey?.GetValue(null) as string;
        if (!string.IsNullOrWhiteSpace(appCommand))
        {
            var executable = ExtractExecutable(appCommand);
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                return executable;
            }
        }

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var appPathsKey = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{appKeyName}");
            var appPath = appPathsKey?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(appPath))
            {
                var executable = ExtractExecutable(appPath);
                if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                {
                    return executable;
                }
            }
        }

        return null;
    }

    public void CopyPath(string path) => Clipboard.SetText($"\"{path}\"");

    public void CopyPaths(IEnumerable<string> paths)
    {
        var lines = paths.Select(p => $"\"{p}\"");
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }

    private static ImageSource LoadSmallIcon(string path, bool isDirectory)
    {
        var attributes = GetFileAttributes(path, isDirectory);
        return LoadSmallIconByAttributes(path, attributes, useFileAttributes: !PathExists(path));
    }

    private static ImageSource LoadSmallIconByAttributes(string path, uint attributes, bool useFileAttributes = true)
    {
        var info = new NativeMethods.SHFILEINFO();
        var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON;
        if (useFileAttributes)
        {
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
        }

        NativeMethods.SHGetFileInfo(
            path,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            flags);

        if (info.hIcon == IntPtr.Zero)
        {
            return CreateFallbackIcon(16);
        }

        return DpiHelper.FromHIcon(info.hIcon);
    }

    private static (ImageSource Source, string Loader) LoadTileIconWithSource(string path, bool isDirectory, int physicalSize)
    {
        // Executables commonly contain a 256 px icon that the shell thumbnail
        // cache may replace with an already-upscaled 32/48 px image. Let the
        // shell resource extractor select the best embedded frame first.
        var result = TryLoadEmbeddedExecutableIcon(path, isDirectory, physicalSize);
        if (result is not null)
        {
            return (result, "EmbeddedExecutable");
        }

        // This is the same general-purpose shell image path used by Explorer.
        // ICONONLY can select a low-resolution icon-cache entry and upscale it.
        result = TryLoadShellImage(path, Math.Max(physicalSize, 256));
        if (result is not null)
        {
            return (result, "ShellImage");
        }

        result = TryLoadJumboIcon(path, isDirectory);
        if (result is not null)
        {
            return (result, "JumboIcon");
        }

        result = TryLoadLargeHIcon(path, isDirectory);
        if (result is not null)
        {
            return (result, "LargeHIcon");
        }

        return (LoadSmallIcon(path, isDirectory), "SmallIconFallback");
    }

    private static ImageSource? TryLoadEmbeddedExecutableIcon(string path, bool isDirectory, int physicalSize)
    {
        if (isDirectory || !File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".cpl", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".scr", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        IntPtr largeIcon = IntPtr.Zero;
        IntPtr smallIcon = IntPtr.Zero;
        try
        {
            // Prefer the PNG-backed 256 px frame commonly embedded in modern
            // executables, then let WPF downsample it to the requested display
            // size. Asking for 48/64 px can make the extractor upscale a 32 px
            // legacy frame even when a clean 256 px frame is available.
            var requestedSize = (uint)Math.Clamp(Math.Max(physicalSize, 256), 16, ushort.MaxValue);
            var hr = NativeMethods.SHDefExtractIcon(
                path,
                0,
                0,
                out largeIcon,
                out smallIcon,
                requestedSize);
            if (hr != 0 || largeIcon == IntPtr.Zero)
            {
                return null;
            }

            var result = DpiHelper.FromHIcon(largeIcon);
            largeIcon = IntPtr.Zero;
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (largeIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(largeIcon);
            }

            if (smallIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(smallIcon);
            }
        }
    }

    private static ImageSource? TryLoadLargeHIcon(string path, bool isDirectory)
    {
        var info = new NativeMethods.SHFILEINFO();
        var attributes = GetFileAttributes(path, isDirectory);

        NativeMethods.SHGetFileInfo(
            path,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            GetShellFileInfoFlags(path, NativeMethods.SHGFI_ICON));

        if (info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        return DpiHelper.FromHIcon(info.hIcon);
    }

    private static ImageSource? TryLoadJumboIcon(string path, bool isDirectory)
    {
        NativeMethods.IImageList? imageList = null;
        try
        {
            var info = new NativeMethods.SHFILEINFO();
            var attributes = GetFileAttributes(path, isDirectory);

            NativeMethods.SHGetFileInfo(
                path,
                attributes,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                GetShellFileInfoFlags(path, NativeMethods.SHGFI_SYSICONINDEX));

            if (info.iIcon < 0)
            {
                return null;
            }

            var imageListGuid = NativeMethods.ImageListGuid;
            var hr = NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref imageListGuid, out imageList);
            if (hr != 0 || imageList is null)
            {
                return null;
            }

            hr = imageList.GetIcon(info.iIcon, NativeMethods.ILD_TRANSPARENT, out var hIcon);
            if (hr != 0 || hIcon == IntPtr.Zero)
            {
                return null;
            }

            return DpiHelper.FromHIcon(hIcon);
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(imageList);
        }
    }

    private static ImageSource? TryLoadShellImage(string path, int size)
    {
        NativeMethods.IShellItemImageFactory? factory = null;
        try
        {
            var hr = NativeMethods.SHCreateItemFromParsingName(
                path,
                IntPtr.Zero,
                typeof(NativeMethods.IShellItemImageFactory).GUID,
                out factory);

            if (hr != 0 || factory is null)
            {
                return null;
            }

            var shellSize = new NativeMethods.SIZE { cx = size, cy = size };
            var flags = NativeMethods.SIIGBF_RESIZETOFIT | NativeMethods.SIIGBF_BIGGERSIZEOK;

            hr = factory.GetImage(shellSize, flags, out var hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                return null;
            }

            return DpiHelper.FromHBitmap(hBitmap);
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(factory);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static uint GetFileAttributes(string path, bool isDirectory) =>
        isDirectory ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY : NativeMethods.FILE_ATTRIBUTE_NORMAL;

    private static uint GetShellFileInfoFlags(string path, uint baseFlags) =>
        PathExists(path) ? baseFlags : baseFlags | NativeMethods.SHGFI_USEFILEATTRIBUTES;

    private static ImageSource CreateFallbackIcon(int size = 16)
    {
        return new DrawingImage(new GeometryDrawing(
            Brushes.DimGray,
            null,
            new RectangleGeometry(new Rect(0, 0, size, size))));
    }

    private static string? ExtractExecutable(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            return endQuote > 0 ? command[1..endQuote] : null;
        }

        var spaceIndex = command.IndexOf(' ');
        return spaceIndex > 0 ? command[..spaceIndex] : command;
    }
}
