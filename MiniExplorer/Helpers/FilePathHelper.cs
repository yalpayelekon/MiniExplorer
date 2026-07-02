using System.IO;

namespace MiniExplorer.Helpers;

public static class FilePathHelper
{
    public static bool IsExtensionlessFile(string path) =>
        string.IsNullOrEmpty(Path.GetExtension(path));

    public static string NormalizeDirectoryPath(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public static bool IsDirectChildPath(string parentDirectory, string candidatePath)
    {
        var parent = NormalizeDirectoryPath(parentDirectory);
        var candidate = Path.GetFullPath(candidatePath);
        var candidateParent = Path.GetDirectoryName(candidate);
        return candidateParent is not null &&
               string.Equals(parent, NormalizeDirectoryPath(candidateParent), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInsideDirectory(string candidatePath, string directoryPath)
    {
        var directory = NormalizeDirectoryPath(directoryPath);
        var candidate = NormalizeDirectoryPath(candidatePath);

        if (string.Equals(directory, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var relative = Path.GetRelativePath(directory, candidate);
        return relative != "." &&
               !relative.StartsWith("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }
}
