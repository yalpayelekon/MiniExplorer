using System.Collections.ObjectModel;
using System.IO;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using VB = Microsoft.VisualBasic.FileIO;

namespace MiniExplorer.Services;

public sealed class FileSystemService
{
    public IReadOnlyList<FileSystemEntry> GetDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileSystemEntry
            {
                FullPath = d.RootDirectory.FullName,
                Name = string.IsNullOrWhiteSpace(d.VolumeLabel)
                    ? $"Yerel Disk ({d.Name.TrimEnd('\\')})"
                    : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})",
                IsDirectory = true,
                Modified = null,
                Size = null
            })
            .ToList();
    }

    public async Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(
        string path,
        string? filter,
        CancellationToken cancellationToken)
    {
        if (path == PathConstants.ThisPc)
        {
            return await Task.Run(() => GetDrives(), cancellationToken);
        }

        return await Task.Run(() =>
        {
            var entries = new List<FileSystemEntry>();
            var directory = new DirectoryInfo(path);

            foreach (var dir in directory.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesFilter(dir.Name, filter))
                {
                    continue;
                }

                DateTime? modified = null;
                try
                {
                    modified = dir.LastWriteTime;
                }
                catch
                {
                    // Ignore inaccessible metadata.
                }

                entries.Add(new FileSystemEntry
                {
                    FullPath = dir.FullName,
                    Name = dir.Name,
                    IsDirectory = true,
                    Modified = modified
                });
            }

            foreach (var file in directory.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesFilter(file.Name, filter))
                {
                    continue;
                }

                long? size = null;
                DateTime? modified = null;
                try
                {
                    size = file.Length;
                    modified = file.LastWriteTime;
                }
                catch
                {
                    // Ignore inaccessible metadata.
                }

                entries.Add(new FileSystemEntry
                {
                    FullPath = file.FullName,
                    Name = file.Name,
                    IsDirectory = false,
                    Extension = file.Extension,
                    Size = size,
                    Modified = modified
                });
            }

            return (IReadOnlyList<FileSystemEntry>)entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public void Rename(string path, string newName)
    {
        ValidateItemName(newName);

        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Geçersiz yol.");
        var destination = Path.Combine(parent, newName.Trim());

        if (Directory.Exists(path))
        {
            Directory.Move(path, destination);
            return;
        }

        File.Move(path, destination);
    }

    public void CopyItems(IEnumerable<string> sourcePaths, string destinationDirectory, bool move)
    {
        var normalizedDestinationDirectory = Path.GetFullPath(destinationDirectory);

        foreach (var sourcePath in sourcePaths)
        {
            var name = Path.GetFileName(sourcePath)
                ?? throw new InvalidOperationException("Geçersiz kaynak yolu.");
            var normalizedSourcePath = Path.GetFullPath(sourcePath);
            var directDestinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, name));

            if (move && string.Equals(normalizedSourcePath, directDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destinationPath = GetUniqueDestinationPath(destinationDirectory, name);
            var normalizedDestinationPath = Path.GetFullPath(destinationPath);

            if (Directory.Exists(sourcePath))
            {
                if (IsInsideDirectory(normalizedSourcePath, normalizedDestinationDirectory) ||
                    IsInsideDirectory(normalizedDestinationPath, normalizedSourcePath))
                {
                    throw new InvalidOperationException("Klasör kendi içine veya alt klasörüne taşınamaz.");
                }

                CopyDirectory(sourcePath, destinationPath);
                if (move)
                {
                    Directory.Delete(sourcePath, recursive: true);
                }

                continue;
            }

            if (move)
            {
                File.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }
        }
    }

    public void DeleteToRecycleBin(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                VB.FileSystem.DeleteDirectory(
                    path,
                    VB.UIOption.OnlyErrorDialogs,
                    VB.RecycleOption.SendToRecycleBin);
                continue;
            }

            VB.FileSystem.DeleteFile(
                path,
                VB.UIOption.OnlyErrorDialogs,
                VB.RecycleOption.SendToRecycleBin);
        }
    }

    public string? GetParentPath(string path)
    {
        if (path == PathConstants.ThisPc)
        {
            return null;
        }

        var parent = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(parent) ? PathConstants.ThisPc : parent;
    }

    private static bool MatchesFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueDestinationPath(string directory, string name)
    {
        var destination = Path.Combine(directory, name);
        if (!File.Exists(destination) && !Directory.Exists(destination))
        {
            return destination;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var counter = 1;

        while (true)
        {
            var candidateName = $"{baseName} ({counter}){extension}";
            destination = Path.Combine(directory, candidateName);
            if (!File.Exists(destination) && !Directory.Exists(destination))
            {
                return destination;
            }

            counter++;
        }
    }

    private static void ValidateItemName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("Geçersiz ad.");
        }

        var trimmed = newName.Trim();
        if (trimmed is "." or "..")
        {
            throw new InvalidOperationException("Bu ad kullanılamaz.");
        }

        if (Path.IsPathRooted(trimmed) ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("Ad yol içeremez.");
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("Ad geçersiz karakter içeriyor.");
        }
    }

    private static bool IsInsideDirectory(string candidatePath, string directoryPath)
    {
        if (!directoryPath.EndsWith(Path.DirectorySeparatorChar))
        {
            directoryPath += Path.DirectorySeparatorChar;
        }

        return candidatePath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidatePath.TrimEnd(Path.DirectorySeparatorChar), directoryPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var target = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var target = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, target);
        }
    }
}
