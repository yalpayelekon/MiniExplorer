using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MiniExplorer.Services;

namespace MiniExplorer.Models;

public sealed class FileSystemEntry : INotifyPropertyChanged
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public long? Size { get; init; }
    public DateTime? Modified { get; init; }
    public string Extension { get; init; } = string.Empty;
    public ImageSource? Icon { get; set; }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (!ReferenceEquals(_thumbnail, value))
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string TypeDisplay => IsDirectory
        ? LocalizationService.Get("FileType_Folder")
        : string.IsNullOrEmpty(Extension)
            ? LocalizationService.Get("FileType_File")
            : LocalizationService.Get("FileType_Extension", Extension.TrimStart('.').ToUpperInvariant());

    public void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(TypeDisplay));
        OnPropertyChanged(nameof(ModifiedDisplay));
    }

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
