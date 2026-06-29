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
    private readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);

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

    public ImageSource GetIcon(string path, bool isDirectory)
    {
        var cacheKey = $"{path}|{isDirectory}";
        return _iconCache.GetOrAdd(cacheKey, _ => LoadIcon(path, isDirectory));
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

    private static ImageSource LoadIcon(string path, bool isDirectory)
    {
        var info = new NativeMethods.SHFILEINFO();
        var attributes = isDirectory
            ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY
            : NativeMethods.FILE_ATTRIBUTE_NORMAL;

        NativeMethods.SHGetFileInfo(
            path,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON | NativeMethods.SHGFI_USEFILEATTRIBUTES);

        if (info.hIcon == IntPtr.Zero)
        {
            return CreateFallbackIcon();
        }

        try
        {
            return Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private static ImageSource CreateFallbackIcon()
    {
        return new DrawingImage(new GeometryDrawing(
            Brushes.DimGray,
            null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
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
