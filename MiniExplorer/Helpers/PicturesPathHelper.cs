using System.IO;

namespace MiniExplorer.Helpers;

public static class PicturesPathHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif"
    };

    public static string? PicturesRoot
    {
        get
        {
            var known = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(known))
            {
                return known;
            }

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var name in new[] { "Resimler", "Pictures" })
            {
                var path = Path.Combine(profile, name);
                if (Directory.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }

    public static bool IsUnderPictures(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == PathConstants.ThisPc)
        {
            return false;
        }

        try
        {
            var root = PicturesRoot;
            if (root is null)
            {
                return false;
            }

            var normalizedPath = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var normalizedRoot = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && ImageExtensions.Contains(extension);
    }
}
