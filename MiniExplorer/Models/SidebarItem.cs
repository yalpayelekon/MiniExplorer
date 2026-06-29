namespace MiniExplorer.Models;

public sealed class SidebarItem
{
    public required string Label { get; init; }
    public required string Path { get; init; }
    public bool IsPinned { get; init; }
    public bool IsDrive { get; init; }
    public bool IsSectionHeader { get; init; }
}
