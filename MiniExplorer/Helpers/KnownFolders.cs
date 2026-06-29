using System.IO;

namespace MiniExplorer.Helpers;

public static class KnownFolders
{
    public static string Downloads
    {
        get
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var name in new[] { "Downloads", "İndirilenler" })
            {
                var path = Path.Combine(profile, name);
                if (Directory.Exists(path))
                {
                    return path;
                }
            }

            return Path.Combine(profile, "Downloads");
        }
    }
}
