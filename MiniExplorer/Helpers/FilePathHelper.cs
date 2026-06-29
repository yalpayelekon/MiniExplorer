using System.IO;

namespace MiniExplorer.Helpers;

public static class FilePathHelper
{
    public static bool IsExtensionlessFile(string path) =>
        string.IsNullOrEmpty(Path.GetExtension(path));
}
