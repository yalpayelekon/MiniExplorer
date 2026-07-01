namespace MiniExplorer.Models;

public enum ThemePreset
{
    Dark,
    Blue,
    Amoled,
    Nord,
    Dracula,
    Light
}

public enum HeaderStylePreset
{
    Default,
    Darker,
    Accent
}

public enum HeaderDensityPreset
{
    Compact,
    Normal,
    Spacious
}

public enum IconSizePreset
{
    Small,
    Medium,
    Large
}

public enum SortField
{
    Name,
    Modified,
    Type,
    Size
}

public enum LanguagePreset
{
    Turkish,
    English
}

public enum ViewMode
{
    List,
    Icons
}

public sealed class AppSettings
{
    public LanguagePreset Language { get; set; } = LanguagePreset.Turkish;
    public ThemePreset Theme { get; set; } = ThemePreset.Dark;
    public IconSizePreset IconSize { get; set; } = IconSizePreset.Medium;
    public HeaderStylePreset HeaderStyle { get; set; } = HeaderStylePreset.Default;
    public HeaderDensityPreset HeaderDensity { get; set; } = HeaderDensityPreset.Normal;
    public bool ShowBackButton { get; set; } = true;
    public bool ShowForwardButton { get; set; } = true;
    public bool ShowUpButton { get; set; } = true;
    public bool ShowRefreshButton { get; set; } = true;
    public bool ShowCopyPathButton { get; set; } = true;
    public bool ShowExplorerButton { get; set; } = true;
    public double SidebarWidth { get; set; } = 240;
    public SortField SortField { get; set; } = SortField.Name;
    public bool SortAscending { get; set; } = true;
    public ViewMode ViewMode { get; set; } = ViewMode.List;

    public int DirectoryCacheMaxDirectories { get; set; } = 16;
    public int DirectoryCacheMaxTotalEntries { get; set; } = 100_000;
}