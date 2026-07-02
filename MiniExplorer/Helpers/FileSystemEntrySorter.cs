using MiniExplorer.Models;

namespace MiniExplorer.Helpers;

public static class FileSystemEntrySorter
{
    public static void Sort(List<FileSystemEntry> entries, SortField field, bool ascending)
    {
        entries.Sort(CreateComparer(field, ascending));
    }

    public static List<FileSystemEntry> SortToList(
        IEnumerable<FileSystemEntry> entries,
        SortField field,
        bool ascending)
    {
        var list = entries as List<FileSystemEntry> ?? entries.ToList();
        Sort(list, field, ascending);
        return list;
    }

    private static Comparer<FileSystemEntry> CreateComparer(SortField field, bool ascending)
    {
        var nameComparer = StringComparer.OrdinalIgnoreCase;
        return Comparer<FileSystemEntry>.Create((a, b) =>
        {
            var dirCompare = b.IsDirectory.CompareTo(a.IsDirectory);
            if (dirCompare != 0)
            {
                return dirCompare;
            }

            var primary = ComparePrimary(a, b, field, nameComparer);
            if (primary != 0)
            {
                return ascending ? primary : -primary;
            }

            var nameCompare = nameComparer.Compare(a.Name, b.Name);
            return field switch
            {
                SortField.Modified or SortField.Size => nameCompare,
                SortField.Type when !a.IsDirectory => nameCompare,
                _ => 0
            };
        });
    }

    private static int ComparePrimary(
        FileSystemEntry a,
        FileSystemEntry b,
        SortField field,
        StringComparer nameComparer)
    {
        return field switch
        {
            SortField.Modified => (a.Modified ?? DateTime.MinValue).CompareTo(b.Modified ?? DateTime.MinValue),
            SortField.Type when a.IsDirectory && b.IsDirectory => nameComparer.Compare(a.Name, b.Name),
            SortField.Type => nameComparer.Compare(a.Extension, b.Extension),
            SortField.Size => (a.Size ?? -1).CompareTo(b.Size ?? -1),
            _ => nameComparer.Compare(a.Name, b.Name)
        };
    }
}
