using System.Collections.ObjectModel;
using System.IO;
using MiniExplorer.Helpers;
using MiniExplorer.Models;
using VB = Microsoft.VisualBasic.FileIO;

namespace MiniExplorer.Services;

public sealed class FileSystemService
{
    private readonly DirectoryCacheService? _directoryCache;

    public FileSystemService(DirectoryCacheService? directoryCache = null)
    {
        _directoryCache = directoryCache;
    }

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
        CancellationToken cancellationToken,
        SortField sortField = SortField.Name,
        bool sortAscending = true,
        bool bypassCache = false)
    {
        if (path == PathConstants.ThisPc)
        {
            return await Task.Run(() => GetDrives(), cancellationToken);
        }

        if (!bypassCache && _directoryCache?.TryGet(path, out var cached) == true)
        {
            return await Task.Run(
                () => ApplyFilterAndSorting(cached, filter, sortField, sortAscending)
                    .Select(CloneEntry)
                    .ToList(),
                cancellationToken);
        }

        return await Task.Run(() =>
        {
            var entries = new List<FileSystemEntry>();
            var directory = new DirectoryInfo(path);

            foreach (var dir in directory.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
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

            _directoryCache?.Set(path, entries);
            return ApplyFilterAndSorting(entries, filter, sortField, sortAscending);
        }, cancellationToken);
    }

    private static IReadOnlyList<FileSystemEntry> ApplyFilterAndSorting(
        IReadOnlyList<FileSystemEntry> entries,
        string? filter,
        SortField sortField,
        bool sortAscending)
    {
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? entries
            : entries.Where(entry => MatchesFilter(entry.Name, filter)).ToList();
        return ApplySorting(filtered, sortField, sortAscending);
    }

    private static FileSystemEntry CloneEntry(FileSystemEntry entry) => new()
    {
        FullPath = entry.FullPath,
        Name = entry.Name,
        IsDirectory = entry.IsDirectory,
        Size = entry.Size,
        Modified = entry.Modified,
        Extension = entry.Extension
    };

    public FileSystemEntry? TryGetEntry(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                DateTime? modified = null;
                try
                {
                    modified = dir.LastWriteTime;
                }
                catch
                {
                    // Ignore inaccessible metadata.
                }

                return new FileSystemEntry
                {
                    FullPath = dir.FullName,
                    Name = dir.Name,
                    IsDirectory = true,
                    Modified = modified
                };
            }

            if (File.Exists(path))
            {
                var file = new FileInfo(path);
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

                return new FileSystemEntry
                {
                    FullPath = file.FullName,
                    Name = file.Name,
                    IsDirectory = false,
                    Extension = file.Extension,
                    Size = size,
                    Modified = modified
                };
            }
        }
        catch
        {
            // Ignore inaccessible paths.
        }

        return null;
    }

    private static IReadOnlyList<FileSystemEntry> ApplySorting(
        IReadOnlyList<FileSystemEntry> entries,
        SortField sortField,
        bool sortAscending)
    {
        var list = entries as List<FileSystemEntry> ?? entries.ToList();
        FileSystemEntrySorter.Sort(list, sortField, sortAscending);
        return list;
    }

    public void Rename(string path, string newName)
    {
        var trimmedName = newName.Trim();
        ValidateItemName(trimmedName);

        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Geçersiz yol.");
        var destination = Path.GetFullPath(Path.Combine(parent, trimmedName));
        var normalizedParent = Path.GetFullPath(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!destination.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(destination, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Geçersiz hedef yol.");
        }

        if (Directory.Exists(path))
        {
            Directory.Move(path, destination);
            return;
        }

        File.Move(path, destination);
    }

    public void CopyItems(IEnumerable<string> sourcePaths, string destinationDirectory, bool move)
    {
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
                if (IsInsideDirectory(normalizedDestinationPath, normalizedSourcePath))
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
        var normalizedSource = Path.GetFullPath(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var normalizedDestination = Path.GetFullPath(destinationDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.Equals(normalizedSource, normalizedDestination, StringComparison.OrdinalIgnoreCase) ||
            IsInsideDirectory(normalizedDestination, normalizedSource))
        {
            throw new InvalidOperationException("Klasör kendi içine veya alt klasörüne kopyalanamaz.");
        }

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
