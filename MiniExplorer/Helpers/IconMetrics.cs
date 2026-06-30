using MiniExplorer.Models;

namespace MiniExplorer.Helpers;

public static class IconMetrics
{
    public static double ThumbnailImageHeight(IconSizePreset size) => size switch
    {
        IconSizePreset.Small => 88,
        IconSizePreset.Large => 152,
        _ => 120
    };

    public static double TileIconLogicalSize(IconSizePreset size) => size switch
    {
        IconSizePreset.Small => 64,
        IconSizePreset.Large => 128,
        _ => 96
    };
}
