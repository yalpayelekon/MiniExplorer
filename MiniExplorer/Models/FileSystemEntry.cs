using System.Windows.Media;

namespace MiniExplorer.Models;

public sealed class FileSystemEntry
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public long? Size { get; init; }
    public DateTime? Modified { get; init; }
    public string Extension { get; init; } = string.Empty;
    public ImageSource? Icon { get; set; }

    public string TypeDisplay => IsDirectory
        ? "Dosya klasörü"
        : string.IsNullOrEmpty(Extension) ? "Dosya" : $"{Extension.TrimStart('.').ToUpperInvariant()} dosyası";

    public string SizeDisplay => IsDirectory || Size is null ? string.Empty : FormatSize(Size.Value);

    public string ModifiedDisplay => Modified?.ToString("g") ?? string.Empty;

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }
}
